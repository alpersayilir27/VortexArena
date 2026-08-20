using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Free-roam arena guard for a physical play space. The player moves 1:1 with their real body;
    /// this component watches the HMD position in the arena's local space and fades the screen —
    /// gently as the player nears the edge, to <b>full black</b> the moment they step outside —
    /// plus shows a warning and pulses the controllers.
    /// Attach to an object positioned inside the arena, aligned with the arena's rotation.
    /// <para>
    /// <b>Being outside the area has the SAME presentation as being inside an obstacle</b>
    /// (<c>ObstacleViolationProbe</c>): full fade + pulsing haptics + a warning text on top of the
    /// fade. Both are two faces of a single rule — <i>if the view is outside the playable area the
    /// screen closes</i> — and both go through the same two arbiters
    /// (<see cref="ScreenFade"/>, <see cref="ControllerHaptics"/>).
    /// ⚠️ The difference is <b>in the penalty, not the presentation</b>: being out of bounds does
    /// NOT COST HEALTH (§10.9), an obstacle does.
    /// </para>
    /// <para>
    /// <b>The ONLY source of the arena size is <see cref="dimensionsJson"/></b> (the dimensions
    /// file, resolved into <see cref="ArenaDimensions"/>). Even when the area is rectangular it is
    /// written as a four-corner <c>plane</c> ring — there is NO "fast path if rectangular"
    /// distinction, two separate expressions of the same measurement kept drifting apart. The
    /// <see cref="ArenaObstacle"/>s in the scene are taken into account in addition to the plan.
    /// </para>
    /// <para>
    /// ⚠️ If the dimensions file is missing/unreadable the boundary <b>shuts itself down</b> —
    /// rationale in <see cref="ResolvePlan"/>.
    /// </para>
    /// <para>
    /// ⚠️ <b>The semi-transparent boundary wall was REMOVED and is not brought back.</b> There used
    /// to be a wall geometry that became more visible as the player approached the edge; since the
    /// arena's real walls come from the environment art, the eye already does that job. The
    /// mechanism also CANNOT BE MOVED onto the art wall: writing alpha only works on a Transparent
    /// material (real walls are opaque) and it disabled the Renderer once alpha dropped — the wall
    /// would vanish entirely while the player was far away. That is why the approach warning was
    /// moved onto the fade quad (<see cref="warnFadeAlpha"/>): being attached to the HMD makes it
    /// completely independent of arena geometry.
    /// </para>
    /// <para>
    /// ⚠️ This component is <b>NOT the origin of arena space</b>: the zero of network coordinates is
    /// the <b>world origin</b> (<see cref="ArenaSpace"/> — arena space coincides with world space).
    /// Scaling or moving the boundary object does not affect players' network positions.
    /// </para>
    /// </summary>
    public class ArenaBoundary : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Quad parented to the HMD used for the approach/out-of-bounds fade.")]
        [SerializeField] private Renderer fadeRenderer;
        [SerializeField] private TextMesh warningText;

        [Header("Arena size (meters)")]
        [Tooltip("Boyut dosyası (JSON, TextAsset) — arena ölçüsünün TEK kaynağı, ZORUNLUDUR. " +
                 "Dosya MEKAN başınadır: bir işletmenin tüm sahneleri aynı dosyayı gösterir. " +
                 "İşletmenin ölçüsü şeritmetreyle alınıp doğrudan bu dosyaya yazılır; alan " +
                 "dikdörtgen olsa bile dört köşeli bir 'plane' halkası olarak girilir. Boşsa " +
                 "muhafaza devre dışı kalır. " +
                 "Örnek: Assets/Arenas/Venues/VortexAntep/Data/VortexAntep_dimensions.json")]
        [SerializeField] private TextAsset dimensionsJson;

        [Header("Warning behaviour")]
        [Tooltip("Distance from the edge (m) where the approach fade starts.")]
        [SerializeField] private float warnDistance = 1f;
        [Tooltip("Fade alpha reached exactly AT the boundary (approach warning ceiling).")]
        [SerializeField] private float warnFadeAlpha = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock propertyBlock;

        /// <summary>This component's source id in <see cref="ScreenFade"/>.</summary>
        private const string FadeSourceId = "boundary";

        /// <summary>This component's source id in <see cref="ControllerHaptics"/>.</summary>
        private const string HapticSourceId = "boundary";

        /// <summary>
        /// The fade drawn once the boundary is crossed. ⚠️ <b>It is NOT tunable</b> — neither a
        /// ceiling field (like 0.96) nor a second distance ramp outside is added back: being
        /// outside the area is the same question as being inside an obstacle. There is nothing
        /// legitimate to see out there and even a few percent of transparency leaves the other side
        /// of the curtain <b>readable</b> — looking into the arena from outside is exactly the
        /// exploit itself. The value is identical to the obstacle fade
        /// (<c>ObstacleViolationProbe</c>), because the two are a single rule: <i>if the view is
        /// outside the play area the screen closes.</i>
        /// </summary>
        private const float OutsideFadeAlpha = 1f;

        /// <summary>
        /// The boundary in the scene — <b>the first instance that becomes enabled</b>. There is a
        /// single boundary in a scene (the <c>VA_ArenaBoundary</c> prefab instance), so the "which
        /// one" question never arises.
        /// <para>Its consumers are the readers of the out-of-bounds state (pose reporting, fire
        /// gate); in a scene with no boundary/plan it returns <c>null</c> or a locked
        /// <c>false</c> — saying "outside" without knowing the measurements would be wrong
        /// information.</para>
        /// </summary>
        public static ArenaBoundary Active { get; private set; }

        /// <summary>True while the HMD is outside the allowed area.
        /// <para>⚠️ In spectator mode (<see cref="SetSpectatorMode"/>) and in a boundary without a
        /// plan it is LOCKED to <c>false</c> — the admin has no HMD, and in an arena whose
        /// measurements are unknown saying "outside" would be made up.</para></summary>
        public bool IsOutOfBounds { get; private set; }

        /// <summary>
        /// Arena half extents (meters, X/Z) — the admin top-down framing reads this. It comes from
        /// the bounding box of the plan's floor ring; <see cref="Vector2.zero"/> when there is no
        /// plan.
        /// </summary>
        public Vector2 HalfExtents
        {
            get
            {
                EnsurePlan();
                if (activePlan == null)
                {
                    return Vector2.zero;
                }

                Rect bounds = activePlan.LocalBounds();
                return new Vector2(bounds.width * 0.5f, bounds.height * 0.5f);
            }
        }

        /// <summary>
        /// The center of the arena area in LOCAL space (XZ, meters) = the center of the floor
        /// ring's bounding box. <see cref="Vector2.zero"/> when there is no plan.
        /// <para>
        /// ⚠️ <see cref="HalfExtents"/> alone is not enough when framing: measurements are usually
        /// taken from a corner (the plan's zero is that corner), so the box is NOT exactly at the
        /// center of this transform.
        /// </para>
        /// </summary>
        public Vector2 LocalCenter
        {
            get
            {
                EnsurePlan();
                return activePlan != null ? activePlan.LocalBounds().center : Vector2.zero;
            }
        }

        /// <summary>
        /// Height of the admin top-down camera above the floor (meters), from the dimensions file.
        /// 0 = not written in the file, the camera uses its own default.
        /// <para>
        /// ⚠️ This component is the ONLY place that resolves the dimensions file: the camera does
        /// not open the JSON itself, otherwise the same file would be parsed twice and the two
        /// could silently drift apart (same rationale as
        /// <see cref="TryGetCalibrationMarks"/>).
        /// </para>
        /// </summary>
        public float TopDownHeight
        {
            get
            {
                EnsurePlan();
                return activePlan != null ? activePlan.topViewHeight : 0f;
            }
        }

        /// <summary>
        /// Returns the venue's two calibration points in WORLD space (at floor level, on this
        /// transform's plane). Returns <c>false</c> when the file has no points or the two are too
        /// close to each other.
        /// <para>
        /// ⚠️ This component is the only place that reads the plan; <c>ArenaCalibrator</c> does not
        /// parse the dimensions file itself, it positions its anchors from here. Otherwise the same
        /// JSON would be resolved twice and the two could drift apart.
        /// </para>
        /// </summary>
        public bool TryGetCalibrationMarks(out Vector3 worldA, out Vector3 worldB)
        {
            worldA = Vector3.zero;
            worldB = Vector3.zero;

            EnsurePlan();
            if (activePlan == null || !activePlan.HasCalibration)
            {
                return false;
            }

            worldA = LocalToWorld(activePlan.calibration.a);
            worldB = LocalToWorld(activePlan.calibration.b);
            return true;
        }

        // Spectator (admin) mode: the visual boundary goes silent.
        private bool spectatorMode;

        // Plan cache: so JSON parsing and the ring arrays are not rebuilt per frame
        // (Update runs every frame, the gizmo on every repaint — allocation means GC pressure).
        private ArenaDimensions activePlan;      // resolved plan (null = boundary disabled)
        private Vector2[] cachedPlane;           // activePlan.plane (fast access)
        private Vector2[][] cachedColumns;       // column rings that enter the boundary test
        private TextAsset cachedJsonSource;      // which TextAsset the plan was resolved from
        // ⚠️ Whether it was resolved once: with a missing/invalid JSON activePlan stays null, and
        // without this flag it would be re-parsed per frame and flood the error log.
        private bool planResolved;

        /// <summary>A rotated obstacle rectangle (local XZ) that enters the boundary test.</summary>
        private struct ObstacleRect
        {
            public Vector2 Center;
            public Vector2 HalfSize;
            public float SinYaw;
            public float CosYaw;
        }

        private void Awake()
        {
            // The first instance takes ownership: if a second boundary slips into the scene it must
            // not silently take over and change the measurements (that is a scene setup error, not
            // a behaviour shift).
            Active ??= this;

            propertyBlock = new MaterialPropertyBlock();
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (warningText != null)
                warningText.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void OnEnable()
        {
            // The fields may have been changed at runtime (or while disabled): resolve from scratch
            // on every enable, otherwise a stale plan would be carried over.
            planResolved = false;
            ResolvePlan();
        }

        /// <summary>
        /// Spectator (admin) mode. Silences the visual boundary: the fade quad and the
        /// out-of-bounds warning are turned off, <see cref="IsOutOfBounds"/> is locked to false.
        /// <para>
        /// Rationale: the admin is on a desktop and has no HMD; since their head (the disabled
        /// rig's CenterEyeAnchor) stays put, the boundary logic produces meaningless data. The
        /// component is silenced instead of disabled so that <see cref="HalfExtents"/> /
        /// <see cref="LocalCenter"/> (top-down framing) can still be read.
        /// </para>
        /// </summary>
        public void SetSpectatorMode(bool on)
        {
            spectatorMode = on;
            if (!on)
                return; // the next Update redraws according to the real state

            propertyBlock ??= new MaterialPropertyBlock();
            IsOutOfBounds = false;
            // In spectator mode the arbiter is NEVER consulted and the quad is unconditionally
            // closed: the admin has no HMD, no fade source is meaningful on their screen.
            if (fadeRenderer != null)
                SetFade(fadeRenderer, 0f, Color.black);
            if (warningText != null && warningText.gameObject.activeSelf)
                warningText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (spectatorMode)
            {
                // ⚠️ Haptics are not reported either and that is the right thing: per the arbiter's
                // heartbeat contract a source that goes silent drops out on its own (the admin has
                // no controllers anyway).
                return; // the boundary stays silent
            }

            EnsurePlan();
            if (activePlan == null)
            {
                // No plan = boundary disabled (rationale in ResolvePlan). The out-of-bounds state
                // and the fade are reset and the warning text is hidden — without the measurements
                // there is no answer to "how close are we to the edge", and fading the screen at
                // random would give wrong information.
                IsOutOfBounds = false;
                // ⚠️ Its own alpha is 0 but the draw still HAPPENS: the quad's other source
                // (obstacle violation) must not be affected by the boundary's setup error.
                DrawFade(0f);
                ReportHaptics(false);
                if (warningText != null && warningText.gameObject.activeSelf)
                    warningText.gameObject.SetActive(false);
                return;
            }

            if (head == null)
            {
                DrawFade(0f);
                ReportHaptics(false);
                return;
            }

            Vector3 local = transform.InverseTransformPoint(head.position);
            float edgeDistance = EdgeDistance(new Vector2(local.x, local.z));
            IsOutOfBounds = edgeDistance < 0f;

            DrawFade(FadeAlphaFor(edgeDistance));
            ReportHaptics(IsOutOfBounds);
            if (warningText != null && warningText.gameObject.activeSelf != IsOutOfBounds)
                warningText.gameObject.SetActive(IsOutOfBounds);
        }

        /// <summary>
        /// Pulsing controller haptics while out of bounds — <b>through the same gate as the
        /// fade</b>: a darkening screen on its own raises the question "what happened", the pulse
        /// answers it with "you are outside the area, come back".
        /// <para>
        /// ⚠️ The gate is <b>the boundary itself, not the approach ramp</b>: the ramp is a warning
        /// and lets the player stay inside the area; the vibration is the response to a violation.
        /// The same distinction exists in the fade (ramp ≠ full fade).
        /// </para>
        /// <para>
        /// ⚠️ <b>The motor is not driven directly</b> (<see cref="OVRInput.SetControllerVibration"/>
        /// is not called): obstacle violation asks for the same vibration and both can be true at
        /// the same time — the boundary also counts the scene's <see cref="ArenaObstacle"/>s as out
        /// of bounds. The arbiter (<see cref="ControllerHaptics"/>) applies exactly the same
        /// contract as <see cref="ScreenFade"/>; the pulse's frequency and amplitude live there and
        /// are not repeated here.
        /// </para>
        /// </summary>
        private void ReportHaptics(bool outside) =>
            ControllerHaptics.ReportPulse(HapticSourceId, outside);

        /// <summary>
        /// Reports the boundary's fade request to <see cref="ScreenFade"/> and draws the
        /// <b>winner</b>.
        /// <para>
        /// ⚠️ <b>The quad is not — and must not be — written directly:</b> there is a second system
        /// that wants the same renderer (the obstacle violation fade,
        /// <c>ObstacleViolationProbe</c>). If both wrote their own value they would overwrite each
        /// other per frame — the symptom would be "the screen flickers when I enter an obstacle
        /// while approaching the boundary" and its cause would be spread over two components. This
        /// class is still the OWNER of the renderer (the quad is its serialized field); the arbiter
        /// only says which value gets drawn.
        /// </para>
        /// </summary>
        private void DrawFade(float ownAlpha)
        {
            if (fadeRenderer == null)
                return;

            ScreenFade.Report(FadeSourceId, ownAlpha, BaseColorOf(fadeRenderer));

            if (ScreenFade.Resolve(out float alpha, out Color color))
                SetFade(fadeRenderer, alpha, color);
            else
                SetFade(fadeRenderer, 0f, color);
        }

        /// <summary>
        /// Fade alpha: <b>inside</b> the approach ramp, <b>outside</b> an ungraded full fade.
        /// <para>
        /// The approach ramp (<see cref="warnDistance"/> → <see cref="warnFadeAlpha"/>) is the only
        /// warning channel that replaces the removed semi-transparent wall: the player must be
        /// warned before crossing the boundary, otherwise they would find out only after hitting
        /// the real wall.
        /// </para>
        /// <para>
        /// ⚠️ <b>The transition at the boundary is deliberately DISCONTINUOUS:</b> the moment the
        /// boundary is crossed the alpha jumps to <see cref="OutsideFadeAlpha"/>, there is NO
        /// second ramp outside. The rationale is the same as the obstacle fade — an outside ramp
        /// draws a semi-transparent curtain for a few frames, meaning the player who steps out of
        /// the area stays able to <b>read</b> the inside, and looking into the arena from outside
        /// is exactly the exploit itself. The cost is that a head sitting exactly on the boundary
        /// line oscillates between 0.35 and 1.0 with tracking jitter; the payoff is that the view
        /// from outside is closed entirely.
        /// </para>
        /// </summary>
        private float FadeAlphaFor(float edgeDistance)
        {
            if (edgeDistance < 0f)
            {
                return OutsideFadeAlpha;
            }

            float warn = warnDistance > 0f ? Mathf.Clamp01(1f - edgeDistance / warnDistance) : 0f;
            return warn * warnFadeAlpha;
        }

        // ------------------------------------------------------------ distance computation

        /// <summary>
        /// The signed distance of the given LOCAL XZ point to "the nearest danger": <b>positive</b>
        /// = inside the safe area with that many meters of margin, <b>negative</b> = outside (or
        /// inside an obstacle) by that many meters. The fade and the warning derive from this
        /// single number.
        /// <para>
        /// ⚠️ Only called when a plan exists — the caller (<c>Update</c>) already filters out the
        /// plan-less case with an early return.
        /// </para>
        /// </summary>
        private float EdgeDistance(Vector2 point)
        {
            float distance = Polygon2D.SignedDistance(cachedPlane, point);

            if (cachedColumns != null)
            {
                for (int i = 0; i < cachedColumns.Length; i++)
                {
                    distance = Mathf.Min(distance, Polygon2D.ObstacleDistance(cachedColumns[i], point));
                }
            }

            // Scene obstacles are re-read every frame: they are movable objects, and if cached they
            // would silently produce warnings at their old positions. The list is walked by index
            // (foreach would box an interface enumerator).
            IReadOnlyList<ArenaObstacle> obstacles = ArenaObstacle.All;
            for (int i = 0; i < obstacles.Count; i++)
            {
                ArenaObstacle obstacle = obstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                obstacle.GetLocalRect(transform, out Vector2 center, out Vector2 size, out float yaw);
                distance = Mathf.Min(distance, DistanceToRect(point, MakeRect(center, size, yaw)));
            }

            return distance;
        }

        /// <summary>
        /// The signed distance of the point to a rotated obstacle rectangle: + when outside (the
        /// distance to the box), − when inside (the depth to the nearest face). It uses the same
        /// sign contract as the boundary computation, so the two can be combined with a single
        /// <c>Mathf.Min</c>.
        /// <para>
        /// ⚠️ It only remains for <see cref="ArenaObstacle"/> — the plan's columns are polygons now
        /// and are measured with <see cref="Polygon2D.ObstacleDistance"/>. The representation of
        /// decor placed by hand in the scene stayed rectangular: reading a movable object's size
        /// from a single field (<c>Size</c>) is simpler than making someone author a corner list
        /// for it as well.
        /// </para>
        /// </summary>
        private static float DistanceToRect(Vector2 point, in ObstacleRect rect)
        {
            // Move the point into the rectangle's own axes (rotate back by yaw).
            Vector2 delta = point - rect.Center;
            Vector2 localPoint = new Vector2(
                delta.x * rect.CosYaw - delta.y * rect.SinYaw,
                delta.x * rect.SinYaw + delta.y * rect.CosYaw);

            float dx = Mathf.Abs(localPoint.x) - rect.HalfSize.x;
            float dy = Mathf.Abs(localPoint.y) - rect.HalfSize.y;

            if (dx > 0f || dy > 0f)
            {
                float outsideX = Mathf.Max(dx, 0f);
                float outsideY = Mathf.Max(dy, 0f);
                return Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            }

            // We are inside: the distance to the nearest face (negative).
            return Mathf.Max(dx, dy);
        }

        private static ObstacleRect MakeRect(Vector2 center, Vector2 size, float yaw)
        {
            // The inverse rotation angle is stored (the point will be moved into the rectangle's axes).
            float radians = -yaw * Mathf.Deg2Rad;
            return new ObstacleRect
            {
                Center = center,
                HalfSize = new Vector2(Mathf.Abs(size.x) * 0.5f, Mathf.Abs(size.y) * 0.5f),
                SinYaw = Mathf.Sin(radians),
                CosYaw = Mathf.Cos(radians)
            };
        }

        /// <summary>
        /// Refreshes the cache only if <b>the source reference changed</b> (or it was never
        /// resolved).
        /// <para>
        /// ⚠️ Parsing/error handling is not here but inside <see cref="ResolvePlan"/>: this method
        /// is called every frame (Update) and on every repaint (gizmo), and without the condition
        /// the JSON would be parsed per frame.
        /// </para>
        /// </summary>
        private void EnsurePlan()
        {
            if (!planResolved || cachedJsonSource != dimensionsJson)
            {
                ResolvePlan();
            }
        }

        /// <summary>
        /// Resolves the plan from a single source — <see cref="dimensionsJson"/>. The column rings
        /// are collected once here: the source does not change at runtime, but <c>Update</c> runs
        /// every frame.
        /// <para>
        /// ⚠️ If the file is not assigned or cannot be parsed, <b>loud failure</b> is chosen: an
        /// error is logged once and the boundary goes completely silent. Rationale: a correct
        /// boundary cannot be produced for an arena whose measurements are unknown anyway; a silent
        /// failure (e.g. fading the screen every frame) would make the game entirely unplayable at
        /// the venue. This is a SETUP error and is caught in the editor/QA — it must not take down
        /// the session in the field.
        /// </para>
        /// </summary>
        private void ResolvePlan()
        {
            cachedJsonSource = dimensionsJson;
            planResolved = true;

            activePlan = null;
            cachedPlane = null;
            cachedColumns = null;

            activePlan = ArenaDimensions.FromTextAsset(dimensionsJson, out string error);

            if (activePlan == null)
            {
                string reason = string.IsNullOrEmpty(error) ? string.Empty : " — " + error;
                Debug.LogError(
                    $"[ArenaBoundary] '{name}': boyut dosyası (dimensionsJson) bağlanmamış ya da " +
                    $"okunamadı{reason}. Muhafaza DEVRE DIŞI. Arena ölçüsünün tek kaynağı bu dosyadır.",
                    this);
                return;
            }

            cachedPlane = activePlan.plane;

            ArenaDimensions.Column[] columns = activePlan.columns;
            if (columns == null || columns.Length == 0)
            {
                return;
            }

            // Parsing already filtered out columns with invalid rings; only the rings are collected here.
            cachedColumns = new Vector2[columns.Length][];
            for (int i = 0; i < columns.Length; i++)
            {
                cachedColumns[i] = columns[i].points;
            }
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>
        /// Draws the plan while selected: the floor ring + column prisms (computed in local space
        /// and moved to world). This drawing is essential when tuning a skewed arena by hand — the
        /// plan is a list of numbers, and moving a corner without seeing its counterpart in the
        /// scene is blind work.
        /// <para>
        /// ⚠️ When there is no plan, NOTHING is drawn: a made-up box would hide the fact that the
        /// boundary is actually disabled. Its cause is the error line in the console.
        /// </para>
        /// <para>
        /// ⚠️ The TOP edge of the boundary is no longer drawn: the wall height field was removed
        /// (there is neither wall generation nor a wall indicator), and a measurement with no
        /// reader would go stale.
        /// </para>
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            EnsurePlan();

            if (activePlan == null)
            {
                return;
            }

            Vector2[] ring = activePlan.plane;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Gizmos.DrawLine(LocalToWorld(ring[j]), LocalToWorld(ring[i]));
            }

            // Calibration points: this is the only way to show in the scene where the floor tape
            // goes — the marker objects stay DISABLED in the scene (they are only enabled during
            // calibration), so without the gizmo their positions cannot be inspected by eye.
            if (activePlan.HasCalibration)
            {
                Vector3 markA = LocalToWorld(activePlan.calibration.a);
                Vector3 markB = LocalToWorld(activePlan.calibration.b);
                Gizmos.color = new Color(0.35f, 1f, 0.45f, 0.9f);
                Gizmos.DrawLine(markA, markB);
                Gizmos.DrawWireSphere(markA, 0.12f);
                Gizmos.DrawWireSphere(markB, 0.2f); // B is bigger so the A→B direction is readable from the gizmo
            }

            ArenaDimensions.Column[] columns = activePlan.columns;
            if (columns == null)
            {
                return;
            }

            Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.9f);

            for (int i = 0; i < columns.Length; i++)
            {
                Vector2[] footprint = columns[i].points;
                if (!Polygon2D.IsValid(footprint))
                {
                    continue;
                }

                float height = activePlan.HeightOf(columns[i]);
                for (int k = 0, m = footprint.Length - 1; k < footprint.Length; m = k++)
                {
                    Gizmos.DrawLine(LocalToWorld(footprint[m]), LocalToWorld(footprint[k]));
                    Gizmos.DrawLine(LocalToWorld(footprint[m], height), LocalToWorld(footprint[k], height));
                    Gizmos.DrawLine(LocalToWorld(footprint[k]), LocalToWorld(footprint[k], height));
                }
            }
        }

        private Vector3 LocalToWorld(Vector2 localPoint, float height = 0f)
        {
            return transform.TransformPoint(new Vector3(localPoint.x, height, localPoint.y));
        }

        /// <summary>The material's own base color (RGB) — this is the boundary's fade tint.</summary>
        private static Color BaseColorOf(Renderer target)
        {
            return target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId)
                ? target.sharedMaterial.GetColor(BaseColorId)
                : Color.white;
        }

        /// <summary>
        /// The ONLY method that draws the fade quad: both the RGB and the alpha come from the
        /// winning source.
        /// <para>The color also comes from the source because the sources say different things: the
        /// boundary fades neutrally, an obstacle violation fades <b>red</b>.</para>
        /// </summary>
        private void SetFade(Renderer target, float alpha, Color rgb)
        {
            propertyBlock ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(propertyBlock);
            rgb.a = alpha;
            propertyBlock.SetColor(BaseColorId, rgb);
            target.SetPropertyBlock(propertyBlock);
            target.enabled = alpha > 0.001f;
        }
    }
}
