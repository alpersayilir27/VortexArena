using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Player
{
    /// <summary>Measures whether the LOCAL player's <b>head</b> is inside an interior obstacle
    /// (Docs/ArenaNet-Protokol.md §10.9). One rule: <b>head inside</b>.</summary>
    /// <remarks>
    /// <b>Measures, does not punish.</b> The result is read from <see cref="IsViolating"/>;
    /// <c>PlayerPoseTracker</c> puts it into the pose packet as a bit (§6.2) and the <b>server</b> drains
    /// health. This class knows nothing about health/score/death.
    /// <para><b>It produces TWO distinct outputs that must not be confused:</b>
    /// <see cref="IsViolating"/> (HEAD only → wire flag + penalty) and <see cref="IsBodyBlocked"/>
    /// (head <b>or a hand</b> → fire gate only, never on the wire).</para>
    /// <para>⚠️ <b>Blackout and haptics belong to neither of those but to SIGHT</b> — head touching
    /// geometry (<see cref="HeadInsideLevel"/> &gt; 0) <b>or</b> the eye point closer to an obstacle
    /// surface than the camera's near clip: they do not wait for the penalty threshold (rationale in
    /// <see cref="ReportPresentation"/>). ⚠️ They are also independent of match phase and aliveness —
    /// same reason: sight being inside a solid is the same thing in every situation. Phase/aliveness
    /// gates belong <b>to the penalty only</b> and the server applies them.</para>
    /// <para><b>Why the penalty looks only at the head:</b> its job is to punish sight entering solid
    /// geometry — hands, arms and torso do not do that. A rule judging the ratio of measured mass is
    /// <b>ABSENT and does not come back</b>: on Quest the lower body is not sensed but GENERATED from
    /// the upper body (OVRBody FullBody = generative legs), and a generated limb can solve INSIDE cover
    /// while the player stands behind it — a ratio rule would inevitably punish an untouched solid.</para>
    /// <para><b>Head centre comes from the HMD, not a bone:</b> the headset knows where the real head is
    /// better than the skeleton, and the rule works even if body tracking never runs.</para>
    /// <para>The point-inside test itself lives in <see cref="ObstacleVolumes"/>, not here (as does the
    /// convexity requirement's rationale): the shot path asks the same question and two copies would
    /// inevitably diverge.</para>
    /// <para>A self-bootstrapping singleton (<c>WeaponGranter</c>/<c>PlayerCombatState</c> pattern):
    /// placed by hand it would add a setup step per arena and a forgotten arena would silently have no
    /// penalty.</para>
    /// <para>⚠️ Late execution order (30100): the HMD anchor is written in the rig's own loop, so an
    /// early frame would measure a one-frame-stale head position.</para>
    /// </remarks>
    [DefaultExecutionOrder(30100)]
    public class ObstacleViolationProbe : MonoBehaviour
    {
        // ------------------------------------------------------------------ settings

        /// <summary>Sampling cadence — same as pose sending (20 Hz) so the flag is fresh when the packet
        /// leaves.</summary>
        private const float SampleInterval = 1f / 20f;

        /// <summary>Head sphere radius (m) — the shell's seven points derive from it.</summary>
        private const float HeadRadius = 0.11f;

        /// <summary>Back offset from eye level to head centre (m): the HMD sits in front of the eyes.</summary>
        private const float HeadCenterBackOffset = 0.06f;

        /// <summary>Candidate-gathering sphere radius (m): the head shell <b>and both hands</b> must fit
        /// inside — a fully extended arm is ~0.8 m from the head centre.</summary>
        private const float QueryRadius = 1.2f;

        /// <summary>Hand sphere radius (m): palm + fingers roughly occupy this.</summary>
        private const float HandRadius = 0.07f;

        /// <summary>Uninterrupted time a violation must last to count (s), so a single-frame tracking jump
        /// does not start a penalty.</summary>
        private const float MinViolationSeconds = 0.15f;

        /// <summary>Shell points required to count a violation while the centre point is outside. If the
        /// centre is inside it suffices alone — this threshold is the <b>second</b> gate (entering
        /// sideways, the centre is the last point in).</summary>
        private const int EnterPointCount = 3;

        /// <summary>Blackout release time (s). ⚠️ <b>There is NO entry counterpart</b> — the blackout drops
        /// instantly on contact (rationale in <see cref="ReportPresentation"/>); the ramp exists only on
        /// exit and for comfort: a head hovering on the boundary would flicker black/clear at the sampling
        /// cadence.</summary>
        private const float FadeOutSeconds = 0.25f;

        /// <summary>Blackout colour: sight closing is not a warning, it means <b>there is nothing to
        /// see</b>.</summary>
        private static readonly Color BlackoutColor = new Color(0.02f, 0.01f, 0.01f);

        /// <summary><see cref="ScreenFade"/> source id.</summary>
        private const string FadeSourceId = "obstacle";

        /// <summary><see cref="ControllerHaptics"/> source id. ⚠️ Pulse frequency and amplitude live in the
        /// arbiter, NOT here: overlapping with the second source wanting the same feel (the boundary
        /// guard), two separate numbers would mean two separate phases.</summary>
        private const string HapticSourceId = "obstacle";

        /// <summary>Minimum interval between rig searches when none is found (s).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>Head shell point count (centre + ±x/±y/±z).</summary>
        private const int HeadSampleCount = 7;

        /// <summary>How much farther the near plane's <b>CORNER</b> is than its centre. Clipping starts
        /// <c>nearClipPlane</c> ahead of the eye point but the plane is a rectangle: at 90° FOV and square
        /// aspect the corner falls <c>near·√3 ≈ 1.73×</c> away. The blackout must cover the corner too —
        /// ⚠️ a strip left in peripheral vision gives the whole exploit back.</summary>
        private const float NearPlaneCornerFactor = 1.8f;

        /// <summary>Stereo margin (m): each eye sits half an IPD sideways from the <b>centre</b> eye point
        /// where the measurement happens, i.e. that much closer to a side wall.</summary>
        private const float StereoEyeMargin = 0.035f;

        /// <summary>Fallback near clip distance if the camera cannot be resolved (m).</summary>
        private const float DefaultNearClip = 0.05f;

        /// <summary>Lower bound of the sight clearance (m): however small the near plane is, the blackout
        /// must arrive at least a tracking jitter early.</summary>
        private const float MinEyeClearance = 0.05f;

        /// <summary>Upper bound of the sight clearance (m) = <see cref="HeadRadius"/>. ⚠️ <b>The ceiling is
        /// a CONTRACT, not a safety net:</b> the blackout gate is at most the moment "my head is touching
        /// the solid". A larger clearance would black out the screen while the player walks <b>past</b> a
        /// block; so it cannot be solved by raising the near clip alone — the camera's
        /// <c>nearClipPlane</c> is also kept small enough to stay under this ceiling
        /// (<c>VA_CameraRig</c>).</summary>
        private const float MaxEyeClearance = HeadRadius;

        // ------------------------------------------------------------------- state

        public static ObstacleViolationProbe Instance { get; private set; }

        /// <summary>Is the local player's head inside an interior obstacle right now — <b>the only
        /// information carried on the wire</b> (<c>gripFlags</c> bit5, §6.3). Read by
        /// <c>PlayerPoseTracker</c>.</summary>
        public static bool IsViolating { get; private set; }

        /// <summary>How much of the head shell is inside geometry (0..1): the fraction of the seven points
        /// that are inside. ⚠️ <b>NOT a severity measure and never converted to alpha</b> — one of the
        /// blackout gates is whether this is greater than zero, not how large it is. Ramping on the ratio
        /// means "I am inside but I can see". Its remaining consumer is diagnostics (Dev window).</summary>
        public static float HeadInsideLevel { get; private set; }

        /// <summary>This frame's drawn blackout value (0..1). The warning text follows it so it appears
        /// together with the blackout, never before it.</summary>
        public static float FadeAlpha { get; private set; }

        /// <summary>Is one of the player's <b>measured</b> parts (head or a hand) inside an obstacle — the
        /// <b>fire gate</b> (<c>PlayerCombatState.CanFire</c>, §10.9).</summary>
        /// <remarks>
        /// Three deliberate differences from <see cref="IsViolating"/>:
        /// <list type="number">
        /// <item><b>Hands count too.</b> The penalty only looks at the head (a sight rule); the fire gate
        /// asks "am I shooting without exposing my body" — a player standing inside a block and poking
        /// the weapon out is doing exactly that.</item>
        /// <item><b>No dwell time</b> (<see cref="MinViolationSeconds"/> is not applied): blocking fire
        /// one frame too long beats allowing it one frame too long.</item>
        /// <item><b>Never goes on the wire.</b> The flag (<c>FLAG_IN_OBSTACLE</c>) carries only
        /// <see cref="IsViolating"/> — the hand rule has no protocol counterpart.</item>
        /// </list>
        /// <para>⚠️ <b>An untracked hand is never asked:</b> the rig writes an untracked hand's anchor to
        /// the rig origin (<see cref="ControllerTracking"/>) and the player's feet may well be inside an
        /// obstacle — a player whose controller dies would become unable to fire for no reason.</para>
        /// </remarks>
        public static bool IsBodyBlocked { get; private set; }

        /// <summary>Last measurement outcome (diagnostics).</summary>
        public enum Trigger
        {
            /// <summary>The head touches no obstacle.</summary>
            None,

            /// <summary>The shell touches a surface but the head does not count as inside:
            /// <b>blackout yes, penalty no</b>.</summary>
            Grazing,

            /// <summary>The head is inside — a violation.</summary>
            Inside
        }

        /// <summary>Result of the last measurement (diagnostics).</summary>
        public static Trigger LastTrigger { get; private set; }

        /// <summary>Name of the obstacle that last answered "inside" (diagnostics); empty otherwise.</summary>
        public static string LastTriggerCollider =>
            ObstacleVolumes.LastHit != null ? ObstacleVolumes.LastHit.name : "";

        private float _sampleAccumulator;
        private float _violationHold;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        /// <summary>HMD camera — the sight clearance derives from its near clip.</summary>
        private Camera _headCamera;

        /// <summary>The drawn blackout (smoothed).</summary>
        private float _fadeAlpha;

        // --------------------------------------------------------------- bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[ObstacleViolationProbe]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ObstacleViolationProbe>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            ClearState();
        }

        // -------------------------------------------------------------------- loop

        private void LateUpdate()
        {
            // ⚠️ The head is resolved EVERY FRAME: the blackout gate (sight clearance) reads it and that
            // gate cannot be left at 20 Hz — rationale in ReportPresentation.
            Transform head = ResolveHead();

            _sampleAccumulator += Time.unscaledDeltaTime;
            if (_sampleAccumulator >= SampleInterval)
            {
                float elapsed = _sampleAccumulator;
                _sampleAccumulator = 0f;
                Evaluate(head, elapsed);
            }

            // The blackout is reported EVERY FRAME (the arbiter's heartbeat contract); penalty sampling
            // runs at 20 Hz.
            ReportPresentation(head);
        }

        /// <summary>One measurement pass: broad phase → head shell → threshold + hysteresis.</summary>
        private void Evaluate(Transform head, float elapsed)
        {
            if (head == null)
            {
                // Not a player (admin) or the rig is not up yet: nothing to measure, no violation.
                ClearState();
                return;
            }

            Vector3 headCenter = head.position - head.forward * HeadCenterBackOffset;

            int count = ObstacleVolumes.Sample(headCenter, QueryRadius);
            if (count == 0)
            {
                // ⚠️ THE COMMON CASE and the sole reason for this early out: with no obstacle nearby only
                // one physics query runs per pass and the point test never runs.
                // (If the layer is undefined Sample also returns 0; ArenaLayers already complained once.)
                HeadInsideLevel = 0f;
                IsBodyBlocked = false;
                LastTrigger = Trigger.None;
                SetViolation(false, elapsed);
                return;
            }

            bool centerInside = EvaluateHead(headCenter, count, out int inside);
            HeadInsideLevel = inside / (float)HeadSampleCount;

            // ⚠️ This threshold belongs to the PENALTY ONLY; the blackout does not wait for it (rationale
            // in ReportPresentation). Enter: centre inside, or a third of the shell inside. Exit: NO point
            // inside — hysteresis comes from here rather than a second threshold, because "I went in" and
            // "I am fully out" are naturally different questions.
            bool rule = IsViolating
                ? inside > 0
                : centerInside || inside >= EnterPointCount;

            LastTrigger = rule ? Trigger.Inside
                : inside > 0 ? Trigger.Grazing
                : Trigger.None;

            // The fire gate is computed SEPARATELY from the penalty (rationale in IsBodyBlocked).
            IsBodyBlocked = rule || IsHandInside(false, count) || IsHandInside(true, count);

            SetViolation(rule, elapsed);
        }

        /// <summary>Is a hand's small shell (centre + ±x/±y/±z) inside an obstacle? An untracked hand is
        /// <b>not asked</b> — rationale in <see cref="IsBodyBlocked"/>.</summary>
        private bool IsHandInside(bool right, int count)
        {
            if (!ControllerTracking.IsValid(right))
            {
                return false;
            }

            Transform anchor = _rig != null
                ? (right ? _rig.rightHandAnchor : _rig.leftHandAnchor)
                : null;

            if (anchor == null)
            {
                return false;
            }

            Vector3 center = anchor.position;
            return ObstacleVolumes.Contains(center, count) ||
                   ObstacleVolumes.Contains(center + Vector3.right * HandRadius, count) ||
                   ObstacleVolumes.Contains(center - Vector3.right * HandRadius, count) ||
                   ObstacleVolumes.Contains(center + Vector3.up * HandRadius, count) ||
                   ObstacleVolumes.Contains(center - Vector3.up * HandRadius, count) ||
                   ObstacleVolumes.Contains(center + Vector3.forward * HandRadius, count) ||
                   ObstacleVolumes.Contains(center - Vector3.forward * HandRadius, count);
        }

        /// <summary>Hysteresis + minimum dwell. Entry waits <see cref="MinViolationSeconds"/>, exit does
        /// not: delaying the penalty favours the player, delaying its release does not.</summary>
        private void SetViolation(bool rule, float elapsed)
        {
            if (!rule)
            {
                _violationHold = 0f;
                IsViolating = false;
                return;
            }

            if (IsViolating)
            {
                return;
            }

            _violationHold += elapsed;
            if (_violationHold >= MinViolationSeconds)
            {
                IsViolating = true;
            }
        }

        private void ClearState()
        {
            IsViolating = false;
            IsBodyBlocked = false;
            HeadInsideLevel = 0f;
            LastTrigger = Trigger.None;
            _violationHold = 0f;
        }

        // -------------------------------------------------------------------- rule

        /// <summary>Tests the head sphere's seven points (centre + ±x/±y/±z); returns whether the
        /// <b>centre</b> is inside, with <paramref name="inside"/> being the count of points inside.</summary>
        /// <remarks>⚠️ A "distance from centre to surface ≥ radius" computation is NOT possible:
        /// <see cref="Collider.ClosestPoint"/> returns the point itself for a point inside, so <b>surface
        /// distance cannot be measured from within</b>. This point count is all we have about depth.</remarks>
        private static bool EvaluateHead(Vector3 center, int count, out int inside)
        {
            inside = 0;

            bool centerInside = ObstacleVolumes.Contains(center, count);
            if (centerInside) inside++;

            if (ObstacleVolumes.Contains(center + Vector3.right * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center - Vector3.right * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center + Vector3.up * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center - Vector3.up * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center + Vector3.forward * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center - Vector3.forward * HeadRadius, count)) inside++;

            return centerInside;
        }

        // --------------------------------------------------------------- resources

        /// <summary>HMD transform. ⚠️ <b>Also the "are we a player" gate</b>: on an admin observer the rig
        /// is disabled, so this returns <c>null</c> forever and the probe never runs (the same gate as
        /// <see cref="LocalBodyAvatar"/> — Core cannot see <c>AppSession</c>).</summary>
        /// <remarks>The search is throttled so that with no rig it does not run a scene-wide type search
        /// every frame.</remarks>
        private Transform ResolveHead()
        {
            if (_rig != null && _rig.isActiveAndEnabled && _rig.centerEyeAnchor != null)
            {
                return _rig.centerEyeAnchor;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            _headCamera = null; // rig changed: the camera must be re-resolved too (map transition)
            return _rig != null ? _rig.centerEyeAnchor : null;
        }

        /// <summary>Radius at which sight closes (m): if the eye point is closer than this to an obstacle
        /// surface, the screen goes black.</summary>
        /// <remarks>⚠️ <b>DERIVED from the camera's own near clip, never hard-coded:</b> whoever does the
        /// clipping and whoever wants to black out before clipping must look at the same number. Written in
        /// two places, changing one would silently bring the leak back — with the symptom "I can see inside
        /// the obstacle" again.</remarks>
        private float EyeClearance()
        {
            if (_headCamera == null && _rig != null && _rig.centerEyeAnchor != null)
            {
                _headCamera = _rig.centerEyeAnchor.GetComponent<Camera>();
            }

            float near = _headCamera != null ? _headCamera.nearClipPlane : DefaultNearClip;
            return Mathf.Clamp(
                near * NearPlaneCornerFactor + StereoEyeMargin, MinEyeClearance, MaxEyeClearance);
        }

        // ------------------------------------------------------------ presentation

        /// <summary>Blackout + haptics.</summary>
        /// <remarks>
        /// <b>The gate for both is CONTACT or PROXIMITY</b> — both ask the same question: <i>can the eyes
        /// see inside a solid?</i>
        /// <list type="number">
        /// <item><b>Contact:</b> <b>any</b> of the head shell's seven points is inside geometry
        /// (<see cref="HeadInsideLevel"/> &gt; 0).</item>
        /// <item><b>Sight clearance:</b> the eye point is closer than <see cref="EyeClearance"/> metres to
        /// an obstacle surface. ⚠️ <b>Without the second one the first is TOO LATE — a fact of geometry,
        /// not a tuning choice:</b> the camera clips not at the eye point but on the plane
        /// <c>nearClipPlane</c> AHEAD of it, while the shell points extend along world axes from the head
        /// centre (~6 cm BEHIND the eye), passing the eye point by at most a few centimetres in the gaze
        /// direction and not at all diagonally. The band between is exactly the "wall clipped but screen
        /// still clear" band, and that is where the inside of blocks is read.</item>
        /// </list>
        /// The gate is NOT <see cref="IsViolating"/> and must not be wired back to it: the penalty
        /// threshold (point count + 0.15 s) is deliberately tolerant, and applied to sight that tolerance
        /// lets a player push their head into a block <b>far enough to see</b> — which is the exploit
        /// itself. The penalty asks *"is this person standing in the wall"*, the blackout asks *"are the
        /// eyes inside a solid"*; the latter admits no hesitation.
        /// <para>⚠️ <b>There is neither a ramp nor a grazing band (partial darkening):</b> both draw a
        /// semi-transparent veil for a few frames, leaving the far side of the wall <b>readable</b>. The
        /// abrupt darkening that calibration drift costs is deliberately accepted; outer walls, floor and
        /// ceiling are not on the <c>Obstacle</c> layer anyway.</para>
        /// <para>The ramp exists only on <b>exit</b> (<see cref="FadeOutSeconds"/>): a head hovering on the
        /// boundary would flicker black/clear at the 20 Hz sampling cadence, which in VR is a strobe.</para>
        /// <para>⚠️ <b>The damage red is NOT here.</b> The <see cref="ScreenFade"/> arbiter picks the
        /// highest alpha; at black 1.0 no red can pass it. <see cref="DamageVignette"/> draws health loss
        /// as a separate layer ON TOP of the quad.</para>
        /// <para>⚠️ <b>The gate contains neither phase nor aliveness, and they are not added back:</b> the
        /// blackout works on any map, mode and phase (lobby · loading · countdown · playing · finished) and
        /// even while dead. Same rationale as the threshold: eyes inside a solid is the same thing in every
        /// situation, and reading the far side of a wall before the match or while dead is the exploit
        /// itself. Silencing it while dead would also contradict the revive gate (no revive inside an
        /// obstacle, §10.9): the player could not see why they are not reviving. Phase/aliveness gate
        /// <b>only the penalty</b>, on the server (<c>MatchDirector.TickObstacleLocked</c>) — no second
        /// copy is kept here.</para>
        /// </remarks>
        private void ReportPresentation(Transform head)
        {
            bool contact = HeadInsideLevel > 0f || IsEyeAgainstGeometry(head);

            if (contact)
            {
                _fadeAlpha = 1f;
            }
            else
            {
                _fadeAlpha = Mathf.MoveTowards(
                    _fadeAlpha, 0f, Time.unscaledDeltaTime / FadeOutSeconds);
            }

            FadeAlpha = _fadeAlpha;

            ScreenFade.Report(FadeSourceId, _fadeAlpha, BlackoutColor);

            // Haptics is fed from the SAME gate as the blackout (contact): a darkening screen alone raises
            // "what happened", the pulse answers "you are in the wall, back off".
            // ⚠️ The motor is not driven DIRECTLY: a second source wants the same vibration
            // (leaving the arena boundary, ArenaBoundary) and both can be true at once — the arbiter
            // applies the same contract as ScreenFade.
            // ⚠️ Reporting is UNCONDITIONAL (0 is reported without contact too): the arbiter only
            // recomputes on a report, so this singleton reporting every frame is its heartbeat.
            ControllerHaptics.ReportPulse(HapticSourceId, contact);
        }

        /// <summary>Is the eye point closer than <see cref="EyeClearance"/> to an obstacle surface — the
        /// blackout gate that <b>precedes clipping</b>.</summary>
        /// <remarks>
        /// ⚠️ <b>Cannot be tied to the penalty sampling cadence (20 Hz)</b> and runs every frame: 50 ms is
        /// enough for a fast-turning head to cross the clearance band entirely, leaving the screen clear
        /// for those frames. It costs <b>one</b> small sphere query per frame (clearance ≤ 11 cm),
        /// negligible next to the body measurement's 1.2 m query.
        /// <para>⚠️ The result is a <b>threshold</b>, not a ratio: there is no distance-to-alpha ramp and
        /// none is added — a semi-transparent veil leaves the far side of the wall readable (full rationale
        /// above).</para>
        /// </remarks>
        private bool IsEyeAgainstGeometry(Transform head)
        {
            if (head == null)
            {
                return false; // not a player (admin) or the rig is not up yet
            }

            float clearance = EyeClearance();
            return ObstacleVolumes.DistanceToSurface(head.position, clearance) < clearance;
        }
    }
}
