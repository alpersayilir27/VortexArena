using Meta.XR.Movement;
using Meta.XR.Movement.Networking;
using Meta.XR.Movement.Retargeting;
using Unity.Collections;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// The <b>only bridge</b> between the Movement SDK's networking layer and ArenaNet: sends the SDK's
    /// skeleton blob as <c>0x07</c>, feeds incoming blobs back to the SDK, and places the character root
    /// in arena space (§6.9/6.10). The SDK's <see cref="INetworkCharacterBehaviour"/> is
    /// transport-agnostic; no Meta account, entitlement or internet is involved.
    /// <para><b>Role split comes from one place:</b> <see cref="HasInputAuthority"/>. Local: the SDK
    /// solves the body from sensors and produces blobs. Remote/admin: no body tracking, incoming blobs
    /// are applied. Hence one prefab and one retarget config — the difference is the source, not the
    /// code.</para>
    /// <para>⚠️ <b>THIS class writes the root, not the SDK.</b> Joint 0 is in the sender's world space
    /// (§6.9) and the blob is opaque, so the root travels separately — as body yaw + an offset from
    /// the pose-channel head since v19 (§6.9), rebuilt on the receiver's own interpolated head — and
    /// is written in <see cref="LateUpdate"/>, AFTER the SDK's <c>ApplyBodyPose</c>.</para>
    /// <para>⚠️ <b>Never parent the character to anything</b> (especially the rig) — see
    /// Docs/Sistem-Ozeti.md §7 "retarget avatarı hareket eden kökün altına konmaz": the SDK writes the
    /// root joint locally, so a non-identity parent is applied twice.</para>
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(NetworkCharacterHandler))]
    public class ArenaNetCharacterBehaviour : MonoBehaviour, INetworkCharacterBehaviour
    {
        /// <summary>Fake receiver id for the SDK's <c>SendData</c>: the packet goes to the server and is
        /// fanned out from there (§6.10), so there is one "target".
        /// <para>⚠️ Must DIFFER from <see cref="LocalClientId"/> — the SDK skips a receiver equal to its
        /// own id and would produce no packets. <c>playerId</c> is a <c>u8</c>, so it never reaches
        /// this.</para></summary>
        private const ulong RelayClientId = ulong.MaxValue;

        /// <summary>Largest root jump accepted in one send while the guard is armed (m). Free-roam
        /// cannot cover this at 20 Hz; more is a solver failure, not a measurement.</summary>
        private const float RootJumpLimitMeters = 1.5f;

        /// <summary>Max sends the root may be held (≈2 s @ 20 Hz). ⚠️ Without a ceiling one bad
        /// measurement would pin the body to its old spot forever — this guard is not a position
        /// authority.</summary>
        private const int RootHoldMaxSends = 24;

        /// <summary>Largest accepted HORIZONTAL distance between the SDK root and the HMD's floor
        /// projection (m, arena space). The root sits below the hips under the head; crouching, lying
        /// or running never puts it this far away.</summary>
        private const float SuspectHorizontalMeters = 1.5f;

        /// <summary>Accepted |y| ceiling for the root in arena space (m). The arena contract pins the
        /// floor at y=0 (<see cref="ArenaSpace"/>); more means the floor estimate drifted (multi-storey
        /// venue, stale tracking map).</summary>
        private const float SuspectRootHeightMeters = 1.0f;

        /// <summary>Stream counts as STALE after this long without an SDK frame (s). A silent source
        /// freezes the remote avatar with no visible fault; the fallback makes it visible.</summary>
        private const float StaleFrameSeconds = 1.0f;

        /// <summary>Hysteresis: consecutive clean frames needed to return to the SDK path (≈1 s @ 12 Hz).
        /// ⚠️ Returning on one clean frame would flip between the two paths frame by frame — the remote
        /// body would flicker between T-pose and broken pose.</summary>
        private const int RecoverStreakFrames = 12;

        /// <summary>Top of the head above the HMD's eye anchor (m) — turns a measured eye height into the
        /// stature <c>OVRBody</c>'s calibration hint wants. Approximate on purpose: the runtime treats it
        /// as a hint, and a hint from the arena floor beats a solve from a stale floor.</summary>
        private const float HeadTopAboveEyeMeters = 0.11f;

        /// <summary>Stature range the calibration hint is accepted in (m). Outside it the hint is NOT
        /// sent: a bad number is worse than none, because the runtime would trust it.</summary>
        private const float MinHintHeightMeters = 1.2f;

        /// <inheritdoc cref="MinHintHeightMeters"/>
        private const float MaxHintHeightMeters = 2.2f;

        /// <summary>The skeleton stream counts as DEAD after this long without a root (ms); past it the
        /// remote body is drawn from the POSE channel instead (§6.11).
        /// <para>⚠️ Must stay well above the skeleton period (≈83 ms at
        /// <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>) plus the interp buffer: switching on ordinary
        /// packet loss would hand the body back and forth between two drivers every few frames.</para></summary>
        private const int SkeletonDeadAfterMs = 1000;

        /// <summary>This character's player. Set by <see cref="LocalBodyAvatar"/> locally and
        /// <see cref="RemoteAvatar"/> remotely.</summary>
        public int PlayerId { get; private set; }

        /// <inheritdoc cref="INetworkCharacterBehaviour.HasInputAuthority"/>
        public bool HasInputAuthority { get; private set; }

        /// <summary>Uniform body scale (§10.8) — <c>1</c> = unmeasured. Written by
        /// <see cref="RemoteAvatar"/> from the roster.
        /// <para>⚠️ <b>Applied only on the REMOTE body</b> (<see cref="ApplyArenaRoot"/>). The sender's
        /// skeleton goes on the wire at prefab proportions, read from the live bone transforms scale
        /// included — a locally scaled root would be streamed and applied a second time.</para></summary>
        public float BodyScale { get; set; } = 1f;

        /// <summary>Is the sensor source actually running?
        /// <para>⚠️ <see cref="Initialize"/> enabling it is NOT enough: if <c>OVRBody.OnEnable</c> cannot
        /// start body tracking it disables itself and never retries — the body is then silently never
        /// drawn. Callers should read this right after setup and report it.</para></summary>
        public bool IsSourceProviderRunning => _sourceProvider != null && _sourceProvider.enabled;

        /// <summary>Is this REMOTE body being drawn from the pose channel instead of the skeleton
        /// (§6.11)? Owned by <see cref="ApplyArenaRoot"/>, read by <see cref="RemotePoseBody"/>, which
        /// takes over the bones while it is true.
        /// <para>⚠️ <c>false</c> also covers a live <b>T-pose fallback</b> stream: that is the sender's
        /// own statement about itself and is not second-guessed here. Only a stream that produces
        /// NOTHING hands the body over.</para></summary>
        public bool IsPoseDriven { get; private set; }

        /// <inheritdoc cref="INetworkCharacterBehaviour.CharacterPrefab"/>
        /// <remarks>The SDK does not spawn the character (<c>Setup(instantiateCharacter: false)</c>);
        /// this field only satisfies the interface.</remarks>
        public GameObject CharacterPrefab => gameObject;

        /// <inheritdoc cref="INetworkCharacterBehaviour.MetaId"/>
        /// <remarks>Never read by the core; only the NGO/Fusion <i>spawner samples</i> need
        /// entitlement/internet and those are unused.</remarks>
        public ulong MetaId => 0;

        /// <inheritdoc cref="INetworkCharacterBehaviour.CharacterId"/>
        public int CharacterId => 0;

        /// <inheritdoc cref="INetworkCharacterBehaviour.LocalClientId"/>
        public ulong LocalClientId => (ulong)PlayerId;

        /// <inheritdoc cref="INetworkCharacterBehaviour.ClientIds"/>
        public ulong[] ClientIds => _relayTargets;

        /// <inheritdoc cref="INetworkCharacterBehaviour.DeltaTime"/>
        public float DeltaTime => Time.deltaTime;

        /// <summary>Shared axis of the SDK's send stamp and the receiver's interpolation: the server
        /// tick clock (<see cref="RemotePlayerRegistry.TryGetServerTimeSeconds"/>).
        /// <para>⚠️ <c>Time.unscaledTime</c> CANNOT be used — it is machine-local, so the two ends sit on
        /// different epochs and the SDK's interpolation locks up or never runs. Before the snapshot
        /// stream starts we fall back to local time; there is no remote body to draw then.</para></summary>
        public float NetworkTime
        {
            get
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry != null && registry.TryGetServerTimeSeconds(out float seconds))
                {
                    return seconds;
                }

                return Time.unscaledTime;
            }
        }

        /// <summary>Render time = shared clock − <see cref="ArenaProtocol.INTERP_DELAY_MS"/>. ⚠️ The
        /// buffer must stay the SAME as the pose channel's, or a constant time offset appears between
        /// body and hands (weapon in hand, body a tick behind).</summary>
        public float RenderTime => NetworkTime - ArenaProtocol.INTERP_DELAY_MS / 1000f;

        private readonly ulong[] _relayTargets = { RelayClientId };

        private NetworkCharacterHandler _handler;
        private Transform _characterRoot;
        private bool _initialized;

        /// <summary>SDK retargeter — the serialisation gate of the T-pose fallback (handle + bind pose).</summary>
        private NetworkCharacterRetargeter _retargeter;

        /// <summary>Has the T-pose fallback been requested (local body only)? A latch —
        /// <see cref="TickTPoseFallback"/> re-judges its gate every frame.</summary>
        private bool _tPoseFallbackRequested;

        /// <summary>Cadence accumulator for fallback frames (the SDK's <c>IntervalToSendData</c>).</summary>
        private float _tPoseFallbackElapsed;

        /// <summary>Local time of the SDK's last CLEAN frame (<c>0</c> = none this session). At zero
        /// staleness is not judged: "not started yet" is answered by the cold-start fallback.</summary>
        private float _lastSdkFrameTime;

        /// <summary>Recovery counter: consecutive clean SDK frames (<see cref="RecoverStreakFrames"/>).</summary>
        private int _saneStreak;

        /// <summary>Did the SDK root fail the sanity check? Clears only through hysteresis, unlike
        /// staleness which clears on the first frame.</summary>
        private bool _poseSuspect;

        /// <summary>Has this suspicion episode already warned (no per-frame logging)?</summary>
        private bool _poseSuspectWarned;

        /// <summary>
        /// Sensor source (<c>MetaSourceDataProvider</c>).
        /// <para>⚠️ <b>Never DELETE it from the prefab, only disable it:</b>
        /// <c>CharacterRetargeter.Awake</c> looks it up on its OWN GameObject and asserts when missing —
        /// one prefab serving both local and remote depends on it staying there. It is disabled on
        /// remote bodies so every avatar is not an active <c>OVRBody</c> reading the local sensor.</para>
        /// <para>⚠️ <b>It ships DISABLED in the prefab and must stay so</b> — enabled here for the local
        /// body only. <c>OnEnable</c> runs at <c>Instantiate</c> time, BEFORE <see cref="Initialize"/>,
        /// so an enabled provider calls <c>OVRBody.StartBodyTracking</c> per spawned remote avatar (an
        /// error line per spawn on admin, a tracking restart on Quest). Disabling it later does not
        /// close that window.</para>
        /// </summary>
        private Behaviour _sourceProvider;

        /// <summary>Reused native buffer for incoming blobs — a field to avoid per-frame allocation
        /// (the SDK takes its own copy anyway).</summary>
        private NativeArray<byte> _receiveScratch;

        /// <summary>Managed copy of the outgoing blob (SDK is native, the wire wants managed); stays at
        /// its grown size.</summary>
        private byte[] _sendScratch;

        /// <summary>Last SENT arena root — the comparison reference of the jump guard.</summary>
        private Pose _lastSentRoot;
        private bool _hasLastSentRoot;

        /// <summary>How many consecutive sends the root has been held (see
        /// <see cref="RootHoldMaxSends"/>).</summary>
        private int _heldRootSends;

        /// <summary>Which alignment generation it was stored under — the reference drops when it
        /// changes (<see cref="ArenaCalibrator.CalibrationGeneration"/>).</summary>
        private int _rootCalibrationGeneration = -1;

        /// <summary>Has this hold episode already warned (no per-frame logging)?</summary>
        private bool _rootHoldWarned;

        /// <summary>The held root relative to the HEAD — established once per episode.</summary>
        private Pose _heldRootLocalToHead;
        private bool _hasHeldRootFrame;

        /// <summary>Minimum interval between HMD anchor searches when none is found (s).</summary>
        private const float CenterEyeSearchIntervalSeconds = 0.5f;

        private Transform _centerEye;
        private float _centerEyeSearchTime = float.NegativeInfinity;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>Resolves component references; idempotent.
        /// <para>⚠️ <b>Must be separate from <see cref="Awake"/>:</b> <see cref="Initialize"/> can be
        /// called while the object is still INACTIVE, and an inactive object's <c>Awake</c> never runs —
        /// every field would stay null and <c>Setup</c> would throw, leaving the retargeter's ownership
        /// at <c>None</c> and the character frozen in T-pose.</para></summary>
        private void ResolveReferences()
        {
            if (_handler != null)
            {
                return;
            }

            _handler = GetComponent<NetworkCharacterHandler>();

            // Root = the transform the SDK writes joint 0 into, i.e. the retargeter's own object.
            // ⚠️ Do NOT trust the handler's property (filled in its own Awake, ordering not guaranteed);
            // on null search the subtree — a wrong root draws the body in a wholly wrong place.
            NetworkCharacterRetargeter retargeter = _handler != null
                ? _handler.NetworkCharacterRetargeter
                : null;

            if (retargeter == null)
            {
                retargeter = GetComponentInChildren<NetworkCharacterRetargeter>(true);
            }

            _retargeter = retargeter;
            _characterRoot = retargeter != null ? retargeter.transform : transform;

            if (retargeter != null)
            {
                _sourceProvider = retargeter.GetComponent<ISourceDataProvider>() as Behaviour;
            }
        }

        private void OnDestroy()
        {
            if (_receiveScratch.IsCreated)
            {
                _receiveScratch.Dispose();
            }
        }

        /// <summary>Binds the character to a player and starts the SDK.
        /// <paramref name="hasInputAuthority"/> is <c>true</c> locally, <c>false</c> for remote bodies.
        /// <para>⚠️ Called with <c>Setup(instantiateCharacter: false)</c>: the character was already
        /// spawned by <see cref="RemoteAvatar"/>/<see cref="LocalBodyAvatar"/>, so
        /// <c>INetworkCharacterSpawner</c> is not needed.</para></summary>
        public void Initialize(int playerId, bool hasInputAuthority)
        {
            // May be called while the object is inactive → Awake may not have run (see ResolveReferences).
            ResolveReferences();

            if (_handler == null)
            {
                Debug.LogError("[ArenaNetCharacterBehaviour] NetworkCharacterHandler yok — karakter " +
                               "sürülemez. Prefabdaki Character objesine kurulmalı.", this);
                return;
            }

            PlayerId = playerId;
            HasInputAuthority = hasInputAuthority;

            // The ONLY place the role split is applied: the sensor runs only on the body's owner.
            if (_sourceProvider != null)
            {
                _sourceProvider.enabled = hasInputAuthority;
            }

            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _handler.Setup(false);
        }

        /// <summary>
        /// Requests the T-pose fallback (local body only; caller is <see cref="LocalBodyAvatar"/>). If
        /// body tracking never produced a valid pose the SDK's send gate (<c>AppliedPose</c>) never
        /// opens and the player stays COMPLETELY invisible to others; the fallback sends the bind (T)
        /// pose with an HMD-derived root instead, so the fault reads as "tracking is broken" rather than
        /// "player missing / network broken".
        /// <para>⚠️ Once the first real pose is applied the SDK path takes over and this request is
        /// permanently inert — the two paths never send at once.</para></summary>
        public void RequestTPoseFallback()
        {
            _tPoseFallbackRequested = true;
        }

        /// <summary>
        /// Restarts the sensor source and re-seeds body-tracking calibration (local body only). Returns
        /// whether the source came back up.
        /// <para>⚠️ <b>This is the retry <c>OVRBody</c> never performs itself.</b> A failed
        /// <c>StartBodyTracking</c> leaves it <c>enabled = false</c> and nothing flips it back, so the
        /// player streams no body for the rest of the session. Toggling <c>enabled</c> runs
        /// <c>OnDisable</c> → <c>OnEnable</c>, i.e. a real <c>StopBodyTracking</c>/
        /// <c>StartBodyTracking2</c> pair against the runtime — not merely a component reset.</para>
        /// <para>⚠️ Order is deliberate: restart FIRST, then clear the stale calibration, then seed the
        /// hint. Clearing before the restart would be adopted back by the dying session, and seeding
        /// before the clear would be wiped by it.</para>
        /// <para>⚠️ <b>Owner only.</b> On a remote body the provider must stay disabled — enabling it
        /// there makes every avatar an active <c>OVRBody</c> reading the local sensor.</para>
        /// </summary>
        public bool RepairBodyTracking()
        {
            if (!HasInputAuthority || _sourceProvider == null)
            {
                return false;
            }

            _sourceProvider.enabled = false;
            _sourceProvider.enabled = true;

            // ⚠️ Can be false again on this very line: OVRBody turns itself off INSIDE OnEnable when the
            // start fails. That is the answer to report, not a state to assert against.
            if (!_sourceProvider.enabled)
            {
                return false;
            }

            OVRBody.ResetBodyTrackingCalibration();
            SeedBodyHeightHint();
            return true;
        }

        /// <summary>Hands the runtime the player's stature measured against the ARENA floor (local body
        /// only). Returns the metres sent, <c>0</c> when nothing plausible was available.
        /// <para>⚠️ Left alone, the runtime infers stature from head height above ITS OWN floor. With
        /// stale space data that floor sits well below the real one: the inferred body is too tall and
        /// its legs reach the bogus floor, so the remote avatar stands sunk into the ground.</para></summary>
        public float SeedBodyHeightHint()
        {
            if (!HasInputAuthority || !TryGetHintHeightMeters(out float meters))
            {
                return 0f;
            }

            return OVRBody.SuggestBodyTrackingCalibrationOverride(meters) ? meters : 0f;
        }

        /// <summary>Player stature for the calibration hint, measured against the ARENA floor.
        /// <para>Standing eye height first (<see cref="StandingHeightState"/>, posture-independent); the
        /// live HMD only before one is learned — a hint taken mid-stoop would become the stature.</para>
        /// <para>⚠️ <b>Deliberately not <see cref="BodyScale"/>:</b> that is a RATIO (§10.8) and carries
        /// no metres at all.</para>
        /// <para>⚠️ Read in ARENA space on purpose. The headset's own floor is exactly what is
        /// untrustworthy here; the arena floor is pinned at y=0 by our own alignment, which survives
        /// stale headset space data.</para></summary>
        private bool TryGetHintHeightMeters(out float meters)
        {
            meters = 0f;
            if (!StandingHeightState.TryGet(out float eye))
            {
                if (!TryGetHeadYawPose(out Pose head))
                {
                    return false;
                }

                eye = ArenaSpace.WorldToArena(head.position).y;
            }

            meters = eye + HeadTopAboveEyeMeters;
            return meters >= MinHintHeightMeters && meters <= MaxHintHeightMeters;
        }

        /// <summary>
        /// Fallback cadence. TWO triggers, both emitting at the SDK's <c>IntervalToSendData</c>.
        /// <para><b>(a) Cold start</b> — requested and the SDK applied NO pose this session. ⚠️ The gate
        /// is <c>AppliedPose</c>, NOT <c>RetargeterValid</c>: <c>_isValid</c> flickers frame to frame
        /// (lost sight, scene load) and binding to it would interleave fallback frames with legitimate
        /// frozen-last-pose frames. <c>AppliedPose</c> is a latch, so this trigger disables itself
        /// permanently.</para>
        /// <para><b>(b) In-flight fault</b> — the SDK applies poses but its root failed the sanity check
        /// (<see cref="_poseSuspect"/>) or the stream went stale. ⚠️ This trigger does NOT depend on
        /// <see cref="_tPoseFallbackRequested"/> (it arms itself): the latch says the source worked once,
        /// the fault appears later. Without it a broken floor/height solve shows the player frozen or in
        /// a random spot — both read as "network broken" on site.</para>
        /// <para>⚠️ They cannot send simultaneously: (a) runs only while <c>AppliedPose</c> is false, (b)
        /// only while it is true, and while (b) is active the SDK's own frame is suppressed in
        /// <see cref="ReceiveStreamData"/>.</para></summary>
        private void TickTPoseFallback()
        {
            if (_retargeter == null)
            {
                return;
            }

            SkeletonRetargeter skeleton = _retargeter.SkeletonRetargeter;
            if (skeleton == null || !skeleton.IsInitialized ||
                _retargeter.RetargetingHandle == MSDKUtility.INVALID_HANDLE)
            {
                return;
            }

            bool coldStart = _tPoseFallbackRequested && !skeleton.AppliedPose;
            bool inFlightFault = skeleton.AppliedPose && (_poseSuspect || IsSdkStreamStale());
            if (!coldStart && !inFlightFault)
            {
                return;
            }

            _tPoseFallbackElapsed += Time.deltaTime;
            if (_tPoseFallbackElapsed < _retargeter.IntervalToSendData)
            {
                return;
            }

            _tPoseFallbackElapsed = 0f;
            SendTPoseFrame();
        }

        /// <summary>No CLEAN SDK frame for this long? With no frame yet (<c>0</c>) it is not stale —
        /// that case belongs to the cold-start trigger.</summary>
        private bool IsSdkStreamStale()
        {
            return _lastSdkFrameTime > 0f && Time.time - _lastSdkFrameTime > StaleFrameSeconds;
        }

        /// <summary>
        /// Serialises and sends one fallback frame. The blob holds the character's CURRENT bones: the
        /// bind (T) pose on cold start, the SDK's last applied (now frozen) pose on an in-flight fault.
        /// The receiver decodes it as an ordinary <c>0x07</c> frame; no special case on the wire or the
        /// server (§6.9).
        /// <para>⚠️ The root is NOT read from the character (never written on cold start, untrusted on a
        /// fault) but derived from the HMD — floor projection, yaw only; the floor is world y=0 by arena
        /// contract (<see cref="ArenaSpace"/>).</para>
        /// <para>⚠️ Joint 0's scale is pinned to 1: on the cold-start path
        /// <c>SkeletonRetargeter.RetargetedPose</c> was never written (undefined memory) and the
        /// receiver applies this scale straight onto the character root.</para>
        /// <para>⚠️ <see cref="GuardRootJump"/> is skipped ON PURPOSE (that gate is for the SDK root
        /// snapping to the rig origin when a controller dies) and <c>_lastSentRoot</c> is not written —
        /// the guard's reference must come from the SDK path's own frames.</para></summary>
        private void SendTPoseFrame()
        {
            if (!TryGetHeadYawPose(out Pose head))
            {
                return;
            }

            var worldRoot = new Pose(new Vector3(head.position.x, 0f, head.position.z), head.rotation);

            NativeArray<MSDKUtility.NativeTransform> bodyPose =
                _retargeter.GetCurrentBodyPose(JointType.NoWorldSpace);
            if (bodyPose.Length == 0)
            {
                bodyPose.Dispose();
                return;
            }

            MSDKUtility.NativeTransform rootJoint = bodyPose[0];
            bodyPose[0] = new MSDKUtility.NativeTransform(rootJoint.Orientation, rootJoint.Position, Vector3.one);

            NativeArray<float> facePose = _retargeter.GetCurrentFacePose(true);

            // Index lists come from the same source as the SDK's SerializeData; BaselineAck = -1 =
            // full keyframe — delta is disabled in this project anyway (§6.9).
            int[] bodySync = _retargeter.BodyIndicesToSync;
            int[] faceSync = _retargeter.FaceIndicesToSync;
            var bodyIndices = new NativeArray<int>(bodySync?.Length ?? 0, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var faceIndices = new NativeArray<int>(faceSync?.Length ?? 0, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            if (bodySync != null && bodySync.Length > 0)
            {
                bodyIndices.CopyFrom(bodySync);
            }

            if (faceSync != null && faceSync.Length > 0)
            {
                faceIndices.CopyFrom(faceSync);
            }

            var snapshot = new MSDKUtility.SnapshotData
            {
                BaselineAck = -1,
                Timestamp = NetworkTime,
                TargetSkeletonPose = bodyPose,
                TargetSkeletonIndices = bodyIndices,
                FacePose = facePose,
                FaceIndices = faceIndices,
                RecordingCoordinateSpaceSource = new MSDKUtility.CoordinateSpace(
                    up: new Vector3(0.0f, 1.0f, 0.0f),
                    forward: new Vector3(0.0f, 0.0f, 1.0f),
                    right: new Vector3(1.0f, 0.0f, 0.0f),
                    metersToUnitScale: 1.0f)
            };

            NativeArray<byte> serialized = default;
            bool serializedOk = MSDKUtility.SerializeSkeletonAndFace(
                _retargeter.RetargetingHandle, snapshot, ref serialized);

            if (bodyPose.IsCreated)
            {
                bodyPose.Dispose();
            }

            if (facePose.IsCreated)
            {
                facePose.Dispose();
            }

            if (!serializedOk || !serialized.IsCreated || serialized.Length == 0)
            {
                return;
            }

            SendBlobToRelay(serialized, ArenaSpace.WorldToArena(worldRoot));
        }

        private void Update()
        {
            // Remote body only: queue the incoming frame for the SDK.
            // ⚠️ Execution order 50 < the handler's 100: the frame is applied in the SAME frame, with
            // no one-frame delay.
            if (!_initialized || HasInputAuthority)
            {
                return;
            }

            RemoteSkeletonRegistry registry = RemoteSkeletonRegistry.Instance;
            if (registry == null || !registry.TryTakeBlob(PlayerId, out byte[] blob, out int length))
            {
                return;
            }

            EnsureScratch(length);
            NativeArray<byte>.Copy(blob, 0, _receiveScratch, 0, length);
            _handler.ReceiveData(_receiveScratch.GetSubArray(0, length));
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            if (HasInputAuthority)
            {
                // Local body: the root comes from sensors and sending happens through ReceiveStreamData
                // from the SDK's own LateUpdate. The only exception is the T-pose fallback, which
                // produces the frame itself when the SDK never applied a pose or its frames are
                // suppressed.
                TickTPoseFallback();
                return;
            }

            ApplyArenaRoot();
        }

        /// <summary>
        /// Places the character root at the interpolated arena-space pose (§6.9).
        /// <para>⚠️ Must run AFTER the SDK's <c>ApplyBodyPose</c>, which writes joint 0 into the root
        /// transform; <c>LateUpdate</c> guarantees that ordering.</para>
        /// <para>⚠️ <b>THIS class also writes the scale</b> (§10.8) and is its single writer — two
        /// writers would make the drawn height depend on the frame.</para>
        /// <para>⚠️ <b>The sender's root scale being <c>1</c> depends on <c>ApplyRootScale</c> being OFF
        /// in the retargeter</b>, not on <c>Calibrate()</c> going uncalled: with it on,
        /// <c>_characterRoot.position</c> stops being a world point and drifts with distance from the
        /// origin. See Docs/Sistem-Ozeti.md §7.</para></summary>
        private void ApplyArenaRoot()
        {
            if (!TryGetEffectiveRoot(out Pose world, out bool fromSkeleton))
            {
                // Neither channel has anything at all — hide by ZERO SCALE rather than draw the bind
                // pose wherever the root happens to sit.
                // ⚠️ RemoteAvatar.SetVisible owns visibility in general; this does not race it, it only
                // uses the scale this class is the SOLE writer of.
                IsPoseDriven = false;
                _characterRoot.localScale = Vector3.zero;
                return;
            }

            IsPoseDriven = !fromSkeleton;

            _characterRoot.SetPositionAndRotation(world.position, world.rotation);

            // ⚠️ Written unconditionally every frame: with a second scale writer (ApplyRootScale
            // re-enabled) a "write only if changed" guard would go stale for a frame.
            _characterRoot.localScale = Vector3.one * BodyScale;
        }

        /// <summary>The root to draw this frame, and whether the SKELETON stream produced it. Computed
        /// from the source, not read from the transform: the writer runs in this class's
        /// <c>LateUpdate</c>, so an earlier reader would get a one-frame stale value; the same
        /// <c>RenderTime</c> yields an identical result.
        /// <para>⚠️ <b>The root's POSITION is ALWAYS anchored to the pose channel</b> (§6.9, v19): the
        /// wire carries yaw + an offset from the head's floor projection, and the root is rebuilt here
        /// on the receiver's OWN interpolated head — the same data that draws the name label, so a
        /// lagging skeleton channel cannot separate the body from it. With no head sample at all
        /// there is nothing to anchor to and the body hides (zero scale in the caller).</para>
        /// <para>⚠️ <b>The skeleton contribution counts only while the stream is LIVE.</b> Registry
        /// samples never expire, so without the age test a stream that died minutes ago would keep the
        /// stale lean offset and body yaw forever; a dead stream drops to the head-derived root — same
        /// formula as the sender's T-pose path (floor projection, YAW only), or the body would jump
        /// the moment a real stream returns.</para></summary>
        private bool TryGetEffectiveRoot(out Pose world, out bool fromSkeleton)
        {
            world = default;
            fromSkeleton = false;

            RemotePlayerRegistry poses = RemotePlayerRegistry.Instance;
            if (poses == null || !poses.GetInterpolatedPose(PlayerId, out Pose head, out _, out _))
            {
                return false;
            }

            var headFloor = new Vector3(head.position.x, 0f, head.position.z);

            RemoteSkeletonRegistry skeletons = RemoteSkeletonRegistry.Instance;
            int ageMs = skeletons != null ? skeletons.GetRootAgeMs(PlayerId) : -1;

            if (ageMs >= 0 && ageMs <= SkeletonDeadAfterMs &&
                skeletons.TryGetInterpolatedRoot(PlayerId, out float yawDeg, out Vector3 offset))
            {
                var arenaRoot = new Pose(headFloor + offset, Quaternion.Euler(0f, yawDeg, 0f));
                world = ArenaSpace.ArenaToWorld(arenaRoot);
                fromSkeleton = true;
                return true;
            }

            Vector3 forward = head.rotation * Vector3.forward;
            forward.y = 0f;

            var fallback = new Pose(
                headFloor,
                forward.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(forward, Vector3.up)
                    : Quaternion.identity);

            world = ArenaSpace.ArenaToWorld(fallback);
            return true;
        }

        /// <summary>
        /// Where a raw (unscaled) WORLD point is actually <b>drawn</b> on a scaled body (§10.8).
        /// <para>⚠️ <see cref="BodyScale"/> is written to the character's ROOT, so the skeleton scales
        /// about the root POINT while hand poses from the wire stay raw. Anything driven off the hand
        /// pose detaches unless it goes through this transform — at scale 1.3 the weapon is drawn half a
        /// metre from the hand.</para>
        /// <para>⚠️ Position only: uniform scale does not change direction, and the item itself is not
        /// scaled (a real-size weapon sits inside an enlarged hand).</para></summary>
        public Vector3 ScalePointAboutRoot(in Vector3 worldPoint)
        {
            float scale = BodyScale;
            if (scale <= 0f || Mathf.Approximately(scale, 1f) || !TryGetEffectiveRoot(out Pose root, out _))
            {
                return worldPoint;
            }

            return root.position + (worldPoint - root.position) * scale;
        }

        /// <inheritdoc cref="INetworkCharacterBehaviour.ReceiveStreamData"/>
        /// <remarks>
        /// Misleading name — this is the hook where the SDK hands us the data to be <b>sent</b>.
        /// <para>⚠️ The root is read AT THIS EXACT MOMENT: the character root currently holds the
        /// sender's world pose; reading it a frame later would separate body and root.</para>
        /// <para>⚠️ The frame passes a <b>sanity check</b> here: the SDK can produce a garbage root while
        /// flagging it valid (floor/height confusion), which draws the player at a random spot — silent
        /// and undiagnosable. A rejected frame is NOT sent; <see cref="TickTPoseFallback"/> sends a
        /// frozen body instead, turning the fault into a visible symptom.</para></remarks>
        public void ReceiveStreamData(ulong clientId, bool isReliable, NativeArray<byte> bytes)
        {
            if (!bytes.IsCreated || bytes.Length == 0)
            {
                return;
            }

            // With no channel, do not advance the root guard's state — a frame that is never sent cannot
            // be its comparison reference.
            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.UdpChannel == null)
            {
                return;
            }

            // ⚠️ This being a WORLD point depends on ApplyRootScale being OFF in the retargeter (see
            // ApplyArenaRoot, Docs/Sistem-Ozeti.md §7).
            Pose candidate = ArenaSpace.WorldToArena(
                new Pose(_characterRoot.position, _characterRoot.rotation));

            if (!AcceptSdkFrame(candidate))
            {
                // ⚠️ A suppressed frame must NOT touch the guard's state (hence GuardRootJump runs after
                // the decision): a garbage root in _lastSentRoot would make the next clean frame look
                // like a jump.
                return;
            }

            SendBlobToRelay(bytes, GuardRootJump(candidate));
        }

        /// <summary>
        /// Is the SDK frame's root plausible enough for the wire? The reference is the HMD: the root
        /// sits below the hips at floor level, so it must stay near the head's floor projection and near
        /// arena floor y=0.
        /// <para>⚠️ With no HMD pose the check is SKIPPED and the frame accepted: dropping frames with
        /// no reference would mute the body, and the fallback needs the same reference anyway.</para>
        /// <para>⚠️ Leaving suspicion uses hysteresis (<see cref="RecoverStreakFrames"/>) and clean
        /// frames are suppressed until the counter fills — mixing the paths during recovery gives a
        /// remote body that changes frame by frame.</para></summary>
        private bool AcceptSdkFrame(in Pose arenaRoot)
        {
            if (TryGetHeadYawPose(out Pose head))
            {
                Vector3 arenaHead = ArenaSpace.WorldToArena(head.position);
                float horizontal = Vector2.Distance(
                    new Vector2(arenaRoot.position.x, arenaRoot.position.z),
                    new Vector2(arenaHead.x, arenaHead.z));

                if (horizontal > SuspectHorizontalMeters ||
                    Mathf.Abs(arenaRoot.position.y) > SuspectRootHeightMeters)
                {
                    _poseSuspect = true;
                    _saneStreak = 0;
                    if (!_poseSuspectWarned)
                    {
                        _poseSuspectWarned = true;
                        Debug.LogWarning(
                            "[ArenaNetCharacterBehaviour] Gövde kökü akıl sağlığı denetiminden düştü " +
                            $"(kafaya yatay uzaklık {horizontal:F1} m, kök yüksekliği " +
                            $"{arenaRoot.position.y:F1} m) — gövde T-poz yedeğine geçti; diğerleri " +
                            "oyuncuyu konumunu izleyen donuk T-pozda görecek. Zemin/boy çözümü " +
                            "bozulmuş olabilir (izleme haritası bayat).", this);
                    }

                    return false;
                }
            }

            _lastSdkFrameTime = Time.time;

            if (!_poseSuspect)
            {
                return true;
            }

            _saneStreak++;
            if (_saneStreak < RecoverStreakFrames)
            {
                return false;
            }

            _poseSuspect = false;
            _poseSuspectWarned = false;
            _saneStreak = 0;
            Debug.Log("[ArenaNetCharacterBehaviour] Gövde kökü yeniden makul — SDK yoluna dönüldü, " +
                      "T-poz yedeği susuyor.", this);
            return true;
        }

        /// <summary>Copies the native blob into a managed buffer and sends it on the skeleton channel
        /// (§6.9) — the shared exit of the SDK path and the T-pose fallback. The copy exists because the
        /// wire layer's <c>BinaryWriter</c> only takes <c>byte[]</c>; the buffer stays at its grown
        /// size.</summary>
        private void SendBlobToRelay(NativeArray<byte> bytes, in Pose arenaRoot)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.UdpChannel == null)
            {
                return;
            }

            byte[] managed = _sendScratch;
            if (managed == null || managed.Length < bytes.Length)
            {
                managed = new byte[Mathf.NextPowerOfTwo(bytes.Length)];
                _sendScratch = managed;
            }

            NativeArray<byte>.Copy(bytes, 0, managed, 0, bytes.Length);
            client.UdpChannel.SendSkeleton(managed, bytes.Length, arenaRoot);
        }

        /// <summary>
        /// Root jump guard: while a known device fault is ongoing, suppresses root jumps larger than
        /// <see cref="RootJumpLimitMeters"/> in one send and re-sends the last root.
        /// <para>⚠️ <b>Armed ONLY while a controller is lost</b>; otherwise the root passes untouched. An
        /// unconditional speed clamp would be a second authority over the root and would rubber-band
        /// real movement. The gate hangs on a defined fault: a dead controller makes the rig write its
        /// hand anchor to the rig origin, the body solve targets that hand, and the root jumps.</para>
        /// <para>⚠️ Recalibration moves everyone legitimately, so the stored root is invalidated with
        /// <see cref="ArenaCalibrator.CalibrationGeneration"/> — otherwise the alignment would be
        /// mistaken for a fault and the old position sent for <see cref="RootHoldMaxSends"/> sends.</para>
        /// </summary>
        private Pose GuardRootJump(Pose candidate)
        {
            int generation = ArenaCalibrator.CalibrationGeneration;
            if (generation != _rootCalibrationGeneration)
            {
                _rootCalibrationGeneration = generation;
                _hasLastSentRoot = false;
                _heldRootSends = 0;
            }

            bool armed = !ControllerTracking.IsValid(false) || !ControllerTracking.IsValid(true);
            bool jumped = _hasLastSentRoot &&
                          Vector3.Distance(candidate.position, _lastSentRoot.position) > RootJumpLimitMeters;

            if (armed && jumped && _heldRootSends < RootHoldMaxSends)
            {
                _heldRootSends++;
                if (!_rootHoldWarned)
                {
                    _rootHoldWarned = true;
                    Debug.LogWarning(
                        "[ArenaNetCharacterBehaviour] Kumanda izlemesi düşükken gövde kökü " +
                        $"{Vector3.Distance(candidate.position, _lastSentRoot.position):F1} m sıçradı — " +
                        "kök KAFAYA sabitlenerek gönderiliyor. Kumandanın pili bitmiş olabilir.", this);
                }

                return HoldRootRelativeToHead();
            }

            // Accept: unarmed, no jump, or the hold hit its ceiling (the player may really have moved
            // there → counters reset, the next episode may warn again).
            _lastSentRoot = candidate;
            _hasLastSentRoot = true;
            _heldRootSends = 0;
            _rootHoldWarned = false;
            _hasHeldRootFrame = false;
            return candidate;
        }

        /// <summary>
        /// Pins the held root <b>to the head</b> instead of freezing it in WORLD space.
        /// <para>⚠️ The fault is in the controller, not the HMD — the player keeps walking. A
        /// world-frozen root nails the body in place while the hand channel rebuilds hands relative to
        /// the head (<c>PlayerPoseTracker</c>), so the remote avatar splits in two for the hold window.
        /// Binding the root to the head puts both channels on the same reference.</para>
        /// <para>⚠️ YAW only: the body need not tilt when the head does (same contract as
        /// <c>RemoteAvatar.ApplyBody</c>). With no head (no rig) we fall back to world-space
        /// freezing.</para></summary>
        private Pose HoldRootRelativeToHead()
        {
            if (!TryGetHeadYawPose(out Pose head))
            {
                return _lastSentRoot;
            }

            if (!_hasHeldRootFrame)
            {
                _hasHeldRootFrame = true;
                Quaternion inverse = Quaternion.Inverse(head.rotation);
                _heldRootLocalToHead = new Pose(
                    inverse * (_lastSentRoot.position - head.position),
                    inverse * _lastSentRoot.rotation);
            }

            return new Pose(
                head.position + head.rotation * _heldRootLocalToHead.position,
                head.rotation * _heldRootLocalToHead.rotation);
        }

        /// <summary>World pose of the HMD, rotation flattened to YAW. The search is throttled so a
        /// missing rig (admin observer) does not cause a scene-wide search every frame.</summary>
        private bool TryGetHeadYawPose(out Pose head)
        {
            if (_centerEye == null && Time.unscaledTime - _centerEyeSearchTime >= CenterEyeSearchIntervalSeconds)
            {
                _centerEyeSearchTime = Time.unscaledTime;
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                _centerEye = rig != null ? rig.centerEyeAnchor : null;
            }

            if (_centerEye == null)
            {
                head = default;
                return false;
            }

            Vector3 forward = _centerEye.forward;
            forward.y = 0f;
            head = new Pose(
                _centerEye.position,
                forward.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(forward, Vector3.up)
                    : Quaternion.identity);
            return true;
        }

        /// <inheritdoc cref="INetworkCharacterBehaviour.ReceiveStreamAck"/>
        /// <remarks>⚠️ <b>Intentionally empty.</b> Acks only serve delta compression, which is OFF here
        /// (§6.9): Meta's delta keeps a baseline per receiver while our topology is a server relay
        /// broadcast, so it would mean one serialisation per receiver per tick. Enabling delta would
        /// require turning both this method and <see cref="RemoteSkeletonRegistry"/>'s single-frame blob
        /// slot into queues.</remarks>
        public void ReceiveStreamAck(ulong clientId, int ack)
        {
        }

        private void EnsureScratch(int length)
        {
            if (_receiveScratch.IsCreated && _receiveScratch.Length >= length)
            {
                return;
            }

            if (_receiveScratch.IsCreated)
            {
                _receiveScratch.Dispose();
            }

            _receiveScratch = new NativeArray<byte>(
                Mathf.NextPowerOfTwo(length), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
    }
}
