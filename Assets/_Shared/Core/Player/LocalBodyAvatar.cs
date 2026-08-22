using Meta.XR.Movement.Networking;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>The local player's body — <b>network source only</b>: never drawn, produces the body
    /// others see.</summary>
    /// <remarks>
    /// ⚠️ <b>The player sees NOTHING of their own body</b> — no body, arm or hand is drawn. The hands
    /// they see in the headset are the rig's <b>synthetic hands</b> (<c>VA_CameraRig</c> →
    /// <c>OVRHandVisualLeft/Right</c>, ISDK <c>SyntheticHand</c>), unrelated to this class.
    /// <para>⚠️ <b>Invisibility is only in RENDERING; the full body goes on the wire:</b> Renderers are
    /// disabled and no bone is touched — the networked skeleton is read from the live bone transforms.</para>
    /// <para><b>SAME FBX, retarget config and code path as the remote avatar</b> (separate prefabs:
    /// <c>Avatars/Resources/LocalBodyAvatar.prefab</c> and <c>App/Prefabs/RemoteAvatar.prefab</c>). The
    /// only difference is <see cref="ArenaNetCharacterBehaviour.HasInputAuthority"/>: <c>true</c> here,
    /// so the body is solved from Movement SDK body tracking and streamed out.</para>
    /// <para>⚠️ <b>Why the object exists and must not be deleted:</b> the body others see comes exactly
    /// from here. Destroy it and the player sends no skeleton, so <b>nobody can see them</b>. Being
    /// invisible locally makes the reflex to delete it more dangerous: nothing changes on the deleter's
    /// screen, the cost is only visible on <b>other</b> screens.</para>
    /// <para>A self-bootstrapping persistent singleton (the <c>WeaponGranter</c> pattern) so that every
    /// new arena does not gain a manual setup step.</para>
    /// <para>⚠️ <b>The avatar lives at the SCENE ROOT and is NOT parented under the rig.</b> The SDK
    /// writes the root joint with <c>SetLocalPositionAndRotation</c>; a non-identity parent transform
    /// would be applied twice (<c>Docs/Sistem-Ozeti.md</c> §7).</para>
    /// <para>⚠️ <b>Not drawn on admin, and NO role check is used for that</b>: <c>AppSession</c> lives in
    /// the <c>VortexArena.App</c> asmdef and the dependency runs App → Core, so Core cannot see it. The
    /// gate is: no active <see cref="OVRCameraRig"/> → no body. On an admin observer
    /// <c>AdminSpectator</c> disables the rig, so the gate behaves correctly by itself.</para>
    /// <para>⚠️ <b>No collider on this avatar</b>: the shot raycast in <c>Weapon</c> is unmasked — your
    /// own body would eat your own shot.</para>
    /// <para>⚠️ <b>Body proportions are NOT calibrated here</b> — <c>CharacterRetargeter.Calibrate()</c>
    /// is never called and that path does not come back: changing the sender's body PROPORTIONS conflicts
    /// with the blob's joint-length compression (<c>SerializationCompressionType.High</c>) and puts the
    /// remote avatar into broken poses. Height differences are carried by a single uniform factor
    /// instead (<c>BodyScaleState</c> measures it, it travels as <c>bodyScale</c>, §10.8) and applied
    /// ONLY to the remote avatar. This class's only role there is exposing <see cref="EyeAnchor"/>.</para>
    /// <para>⚠️ <b>Execution order must be GREATER than 100:</b> the measurement reads that frame's
    /// APPLIED pose, so it must run after the SDK's retarget loop and after
    /// <c>NetworkCharacterHandler</c> (<c>[DefaultExecutionOrder(100)]</c>), which serialises the
    /// skeleton onto the wire.</para>
    /// </remarks>
    [DefaultExecutionOrder(30000)]
    public class LocalBodyAvatar : MonoBehaviour
    {
        /// <summary>The prefab's name under <c>Resources</c> (loaded at bootstrap).
        /// ⚠️ Name and location MUST NOT change — it is loaded via <c>Resources.Load</c>; if moved, the
        /// player sends no body and nobody can see them.</summary>
        private const string PrefabResourceName = "LocalBodyAvatar";

        /// <summary>Minimum interval between rig/session searches when none is found (s).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        public static LocalBodyAvatar Instance { get; private set; }

        [Tooltip("Ağ köprüsü + SDK sürücüsü. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private ArenaNetCharacterBehaviour character;

        [Tooltip("Gövdeyi sensörden çözen SDK bileşeni. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private NetworkCharacterRetargeter retargeter;

        [Tooltip("Gövdenin görsel kökü. Boşsa karakterin kendisi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("Karakterin GÖZ hizası — kafa kemiğinin altında, iki gözün arasında duran boş " +
                 "işaretçi. Gövde ölçümünün referansıdır (§10.8): oyuncunun gözü ile buranın " +
                 "yüksekliği oranlanır. Boşsa ölçüm hiç yapılmaz.")]
        [SerializeField] private Transform eyeAnchor;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        private bool _initialized;

        /// <summary>The "sensor did not start" error is printed once (Update runs 72/s).</summary>
        private bool _sourceProviderWarned;

        /// <summary>Grace period for the sensor to start (s). Not checked immediately: without permission
        /// <c>OVRBody</c> disables itself waiting for <c>PermissionGranted</c> and re-enables once the
        /// dialog is answered — erroring instantly would make that legitimate path look broken.
        /// <para>⚠️ It doubles as the watchdog's <b>debounce</b>, and that job is the stricter one:
        /// <c>RetargeterValid</c> drops for a frame on scene load and whenever the source briefly loses
        /// sight. Restarting body tracking on such a flicker would MAKE the fault it is meant to repair,
        /// so only an unbroken outage this long counts as one.</para></summary>
        private const float SourceProviderGraceSeconds = 5f;

        private float _sourceProviderGrace = SourceProviderGraceSeconds;

        /// <summary>Spacing between repair attempts (s), by attempt index. It GROWS because a headset
        /// whose tracking service is genuinely down must not be restarted every few seconds for a whole
        /// session; the first entries stay short so the common transient fault heals unnoticed.</summary>
        private static readonly float[] RepairBackoffSeconds = { 3f, 10f, 30f, 60f };

        /// <summary>Repair attempts in the CURRENT outage episode; reset when the body comes back.</summary>
        private int _repairAttempts;

        private float _nextRepairTime = float.NegativeInfinity;

        /// <summary>The character's eye level (a marker under the head bone in the prefab) — the
        /// reference of the body measurement (§10.8). <c>null</c> if unbound; the measuring side then
        /// refuses to measure and complains.</summary>
        /// <remarks>⚠️ The measurement reads its WORLD position, and that position being a scale-1
        /// reference depends on <see cref="ArenaNetCharacterBehaviour"/> never scaling the local
        /// character — otherwise a second measurement would drag the factor toward 1.</remarks>
        public Transform EyeAnchor => eyeAnchor;

        /// <summary>Is the body actually being solved (initialised + retargeter valid)? Precondition of
        /// the measurement: the eye level of a skeleton with no pose is meaningless.</summary>
        public bool IsBodyPoseValid => _initialized && retargeter != null && retargeter.RetargeterValid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var prefab = Resources.Load<GameObject>(PrefabResourceName);
            if (prefab == null)
            {
                // There is no local drawing anyway; what is lost is REMOTE visibility — hence the wording.
                Debug.LogWarning($"[LocalBodyAvatar] 'Resources/{PrefabResourceName}' prefabı bulunamadı; " +
                                 "ağa gövde gitmeyecek, yani diğer oyuncular bu oyuncuyu göremeyecek.");
                return;
            }

            // ⚠️ No parent is given (rationale in the class summary).
            GameObject instance = Instantiate(prefab);
            instance.name = prefab.name;
            DontDestroyOnLoad(instance);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (character == null)
            {
                character = GetComponentInChildren<ArenaNetCharacterBehaviour>(true);
            }

            if (retargeter == null)
            {
                retargeter = GetComponentInChildren<NetworkCharacterRetargeter>(true);
            }

            if (visualRoot == null && character != null)
            {
                visualRoot = character.gameObject;
            }

            if (character == null || retargeter == null)
            {
                // ⚠️ An ERROR, not a warning: the body never reaches the wire, so nobody can see this
                // player — and in the field that reads as "the network is broken" when the only thing
                // missing is a prefab binding. Staying silent sends diagnosis to the network layer.
                Debug.LogError("[LocalBodyAvatar] ArenaNetCharacterBehaviour / NetworkCharacterRetargeter " +
                               "bulunamadı; ağa gövde gitmeyecek. Resources/LocalBodyAvatar.prefab " +
                               "içindeki Character objesine bu bileşenler kurulmalı.", this);
                enabled = false;
                return;
            }

            // Subscribed only past the component checks: with nothing to repair the command is a no-op
            // anyway, and an unreachable listener is one more thing to reason about.
            NetEvents.OnRestartBodyTracking += RepairBodyTrackingNow;

            // ⚠️ BEFORE setup the whole subtree stays INACTIVE (fully off, not just renderers) — the one
            // legitimate deactivation: an uninitialised retargeter logs "Ownership is None" every frame.
            // On admin the rig never arrives, so it stays off here, which is also correct.
            if (visualRoot != null)
            {
                visualRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnRestartBodyTracking -= RepairBodyTrackingNow;
            Instance = null;
        }

        private void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
                return;
            }

            TickBodyTrackingWatchdog();
        }

        /// <summary>Sets the body up only when <b>both</b> conditions hold: an active rig (i.e. the role
        /// really is player) and a <c>playerId</c> received from the server.</summary>
        /// <remarks>⚠️ <c>playerId</c> is awaited because the streamed body blob is tagged with it
        /// (§6.9); a frame sent without an id would be ownerless on the server.</remarks>
        private void TryInitialize()
        {
            if (ResolveRig() == null)
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.PlayerId <= 0)
            {
                return;
            }

            _initialized = true;

            // ⚠️ ORDER MATTERS — activate first, then initialise. It was inactive until now, and Awake
            // never runs on an inactive object, so the components setup needs would be unresolved (SDK
            // ownership stays None → no body on the wire). SetActive(true) runs the pending Awakes
            // synchronously inside the call. ⚠️ This is the LAST activation — it is never disabled again
            // (rationale in HideAllRenderers).
            if (visualRoot != null)
            {
                visualRoot.SetActive(true);
            }

            HideAllRenderers();

            character.Initialize(client.PlayerId, hasInputAuthority: true);
            _sourceProviderGrace = SourceProviderGraceSeconds;
        }

        /// <summary>Silences the body visually: <b>every Renderer in the subtree is disabled, no
        /// exceptions.</b></summary>
        /// <remarks>
        /// ⚠️ <b>The object is NOT deactivated</b> (<c>SetActive(false)</c>), and this is not a style
        /// choice: the sensor source on the character is an <c>OVRBody</c>, and deactivating the object
        /// runs its <c>OnDisable</c> — when the last enabled instance goes, <c>StopBodyTracking</c> is
        /// called. On re-enable <c>OnEnable</c> retries and <b>if it fails it disables itself
        /// PERMANENTLY</b>. A deactivated body does not stream either, so the player would vanish from
        /// everyone else's screen. Disabling renderers gives the same visual result and triggers no
        /// lifecycle event.
        /// <para>⚠️ <b>Not done by hiding/scaling bones, and that path does not come back:</b> the
        /// networked skeleton is read from the live bone transforms including <c>localScale</c>
        /// (<c>SkeletonJobs.GetPoseJob</c>) — a zeroed bone collapses the body on the remote side.
        /// Disabling renderers touches no transform, so the full body still goes on the wire.</para>
        /// <para>One call is enough: no renderer is added to the body later. They also ship disabled in
        /// the prefab; this pass is only a guarantee.</para>
        /// </remarks>
        private void HideAllRenderers()
        {
            if (visualRoot == null)
            {
                return;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = false;
                }
            }
        }

        /// <summary>Watches whether a body actually reaches the wire and <b>repairs</b> it when it does
        /// not: arms the T-pose fallback once, then restarts body tracking on a growing backoff until it
        /// comes back.</summary>
        /// <remarks>
        /// ⚠️ The criterion is the retargeter APPLYING a pose, not the sensor being enabled: that is the
        /// gate producing the networked skeleton. Checking only "is the provider enabled" would let a
        /// sensor that stays on but yields no valid data drop the player into the fallback <b>with no
        /// warning at all</b>.
        /// <para>⚠️ <b>Why repairing beats reporting:</b> the fault leaves NO local trace — the player's
        /// hands come from the rig, so their screen looks normal and its visible form (a missing body)
        /// exists only on OTHER screens. Nobody on site can be expected to notice, so noticing must not
        /// be the trigger.</para>
        /// <para>⚠️ <b>Not a one-shot.</b> The old check latched after the first report and left the rest
        /// of the session unprotected — but tracking dies MID-session too, and that is the case the field
        /// actually hits. Every latch here clears on recovery.</para>
        /// <para><c>OVRBody</c> logs its own warning and goes quiet when it cannot start, and that line
        /// does not connect to this question; the link is made explicitly here. Debounce rationale in
        /// <see cref="SourceProviderGraceSeconds"/>.</para>
        /// </remarks>
        private void TickBodyTrackingWatchdog()
        {
            if (retargeter.RetargeterValid)
            {
                NoteBodyTrackingHealthy();
                return;
            }

            _sourceProviderGrace -= Time.unscaledDeltaTime;
            if (_sourceProviderGrace > 0f)
            {
                return;
            }

            if (!_sourceProviderWarned)
            {
                _sourceProviderWarned = true;

                // Keep the player visible while the repair runs: the body streams via the T-pose fallback
                // (root follows the HMD). It goes quiet by itself once a real pose is applied.
                character.RequestTPoseFallback();
                LogBodyTrackingFault();
            }

            if (Time.unscaledTime < _nextRepairTime)
            {
                return;
            }

            AttemptBodyTrackingRepair();
        }

        /// <summary>Body is streaming again: clears the episode.</summary>
        /// <remarks>⚠️ The latches are CLEARED, not left standing. A later second outage must get the
        /// same fast first attempt as the first one did; leaving them set would silently downgrade the
        /// watchdog to the one-shot check it replaced.</remarks>
        private void NoteBodyTrackingHealthy()
        {
            _sourceProviderGrace = SourceProviderGraceSeconds;

            if (_repairAttempts == 0 && !_sourceProviderWarned)
            {
                return;
            }

            _repairAttempts = 0;
            _sourceProviderWarned = false;
            _nextRepairTime = float.NegativeInfinity;
            Debug.Log("[LocalBodyAvatar] Gövde izlemesi düzeldi — gerçek iskelet yeniden akıyor.", this);
        }

        /// <summary>One repair attempt, then re-arms both the backoff and the debounce window.</summary>
        /// <remarks>⚠️ The debounce is re-armed even on success: a restart needs a few seconds before its
        /// result is readable, and judging it immediately would spend the whole backoff ladder inside one
        /// second.</remarks>
        private void AttemptBodyTrackingRepair()
        {
            int index = Mathf.Min(_repairAttempts, RepairBackoffSeconds.Length - 1);
            _repairAttempts++;
            _nextRepairTime = Time.unscaledTime + RepairBackoffSeconds[index];
            _sourceProviderGrace = SourceProviderGraceSeconds;

            bool started = character.RepairBodyTracking();

            Debug.LogWarning(started
                ? $"[LocalBodyAvatar] Gövde izlemesi yeniden başlatıldı ({_repairAttempts}. deneme) — " +
                  "birkaç saniye içinde gerçek iskelete dönmezse deneme yinelenecek."
                : $"[LocalBodyAvatar] Gövde izlemesi yeniden BAŞLATILAMADI ({_repairAttempts}. deneme) — " +
                  "gözlüğün izleme servisi yanıt vermiyor. Oyuncu diğer ekranlarda görünmeye devam " +
                  "ediyor, gövdesi poz güdümlü çiziliyor.", this);
        }

        /// <summary>The one actionable line for a body that never reached the wire. Two different faults
        /// share this symptom and have different fixes — it says which one it is.</summary>
        private void LogBodyTrackingFault()
        {
            string cause = character.IsSourceProviderRunning
                ? "Body tracking açık ama geçerli bir gövde pozu hiç üretmedi"
                : "Body tracking hiç başlamadı (sebebi konsolda bunun üstündeki [OVRBody] satırı söyler)";

            Debug.LogError(
                $"[LocalBodyAvatar] {cause} — gövde izlemesi yeniden başlatılmaya çalışılacak. ⚠️ " +
                "Oyuncunun KENDİ ekranında hiçbir belirti olmaz (eller rig'den geliyor); bu satır tek " +
                "uyarıdır. Sık görülen iki sebep: (1) editörden Link ile koşuluyor ve Meta Quest Link " +
                "uygulamasında ilgili geliştirici çalışma zamanı özelliği kapalı, (2) cihazda " +
                "BODY_TRACKING izni verilmemiş.", this);
        }

        /// <summary>Operator-triggered repair: forces an attempt NOW and drops the backoff, so a headset
        /// the watchdog has already backed off to 60 s answers the button at once (§6.11).</summary>
        /// <remarks>⚠️ Safe to call on a healthy body: the restart costs a short tracking hiccup, which
        /// is why only an operator may ask for it — the watchdog itself never acts without a proven
        /// outage.</remarks>
        public void RepairBodyTrackingNow()
        {
            if (!_initialized || character == null)
            {
                return;
            }

            _repairAttempts = 0;
            AttemptBodyTrackingRepair();
        }

        /// <summary>Finds the active rig; cached, but re-searched when the reference goes null (scene
        /// change, observer-disabled rig).</summary>
        /// <remarks>⚠️ The search is <b>throttled</b>: with no rig at all (admin observer —
        /// <c>AdminSpectator</c> disables it) this gate returns null forever, and unthrottled it would
        /// run a scene-wide type search every frame. The rig arrives on a human time scale.</remarks>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
