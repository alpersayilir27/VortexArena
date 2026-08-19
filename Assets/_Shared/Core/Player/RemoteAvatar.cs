using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Remote player ghost prefab driver: reads the interpolated arena-space pose from
    /// RemotePlayerRegistry, converts it to world space and applies it. Visuals stay hidden until the
    /// first pose. Created by RemotePlayerSpawner via Instantiate + Initialize.
    /// <para>⚠️ <b>The name label is drawn only for TEAMMATES</b> — never for an opponent, and for
    /// nobody in teamless modes (<see cref="ShouldShowNameLabel"/>).</para>
    /// <para>A dead player becomes a <b>ghost body</b> (translucent, coloured by their OWN team), gets
    /// " (ölü)" on the label and loses its hit boxes. The same look marks an uncalibrated player, where
    /// it pulses orange — uncalibrated OVERRIDES dead.</para>
    /// <para>The <b>spawn protection shield</b> (§10.4) is not a material swap but a second renderer on
    /// top (a shell), and is PRESENTATION only — the server decides who is protected.</para>
    /// <para><b>Held items</b> (§6.6): <c>itemL</c>/<c>itemR</c> are resolved via
    /// <see cref="NetItemCatalog"/> and driven from that hand's pose; instances are rebuilt only on
    /// CHANGE. The instance is a visual, not a working weapon (<see cref="SterilizeVisual"/>).</para>
    /// </summary>
    public class RemoteAvatar : MonoBehaviour
    {
        [Header("Görseller")]
        [Tooltip("Karakterli avatarda kullanılmaz; etiket/eski kapsül yolu için kafa transformu.")]
        [SerializeField] private Transform head;
        [Tooltip("Gövde kapsülü; kafanın BodyDropMeters altında, yalnız yaw döner (opsiyonel).")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform handL;
        [SerializeField] private Transform handR;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Renderer[] teamRenderers;

        [Tooltip("Karakter mesh'i — canlı+kalibreli oyuncuda buna HİÇ dokunulmaz. Takım rengi " +
                 "buraya YAZILMAZ (düşmanı işaretlemek duvar arkası avantaj olurdu); yalnız " +
                 "hayalet durumunda hayalet materyaline çevrilir.")]
        [SerializeField] private Renderer[] bodyRenderers;

        [Header("Hayalet gövde (ölü / kalibresiz)")]
        [Tooltip("Yarı saydam hayalet materyali (VortexArena/AvatarGhost). BOŞSA hayalet " +
                 "görünümü hiç uygulanmaz — ölü oyuncu canlıdan ayırt edilemez.")]
        [SerializeField] private Material ghostMaterial;

        [Header("Doğma koruması kalkanı")]
        [Tooltip("Kalkan materyali (VortexArena/CharacterShieldV2) — İKİ TAKIM İÇİN DE aynısı. " +
                 "BOŞSA kalkan hiç çizilmez — korunan oyuncu normal görünür (koruma yine işler). " +
                 "Kabarcığın kalınlığı, rengi ve hareketi MATERYALDEN ayarlanır; burada kod işi yok.")]
        [SerializeField] private Material shieldMaterial;

        [Header("Takıma göre gövde")]
        [Tooltip("KIRMIZI takımın gövde alt ağacı (Ch18). Boşsa herkes varsayılan gövdeyi kullanır — " +
                 "takım gövdesi kurulmamış sayılır ve hiçbir davranış değişmez. " +
                 "Tools > VortexArena > Avatars > Takım Gövdesini Kur ile kurulur.")]
        [SerializeField] private GameObject redBodyRoot;

        [Header("Karakter")]
        [Tooltip("Bağlıysa gövde ağdan gelen Movement SDK iskeletiyle çizilir; boşsa eski " +
                 "kafa/el/kapsül yolu kullanılır.")]
        [SerializeField] private ArenaNetCharacterBehaviour character;

        // ⚠️ There is NO friend/teammate marker above the head and none will be added: a
        // viewer-dependent marker makes heads readable from anywhere in the arena. Team identity is
        // already readable from the label colour and the red team's separate body, both depth-tested.

        [Tooltip("İlk poz gelene dek gizlenecek görsel kök. Boşsa teamRenderers listesi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

        // ⚠️ There is NO serialized list for hit boxes and none will be added: a hand-maintained array
        // beside hand-placed boxes is a SECOND source of truth that eventually desyncs. Boxes are
        // collected under each body by the RemoteHitBox marker; the marker filter keeps an unmarked
        // collider (future decor) from silently becoming shootable.

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color TeamRedColor = new Color(0.85f, 0.20f, 0.20f);
        private static readonly Color TeamBlueColor = new Color(0.20f, 0.40f, 0.90f);
        private static readonly Color NeutralColor = new Color(0.6f, 0.6f, 0.6f);

        private const float CameraRetryIntervalSeconds = 1f;

        /// <summary>Body centre offset below the head centre (metres).</summary>
        private const float BodyDropMeters = 0.55f;

        /// <summary>Colour multiplier for a dead avatar — ONLY for <see cref="teamRenderers"/> on the
        /// legacy capsule path; the character mesh becomes a ghost instead.</summary>
        private const float DeadColorScale = 0.35f;

        /// <summary>Name label height above the head centre (metres).</summary>
        private const float NameLabelHeightMeters = 0.5f;

        /// <summary>Label colour for a teamless (FFA / unassigned) player: white rather than grey,
        /// which would only be hard to read.</summary>
        private static readonly Color NameLabelNeutralColor = Color.white;

        /// <summary>Darkening factor of a dead player's label — HIGHER than
        /// <see cref="DeadColorScale"/>: the text must stay readable, the suffix already says the
        /// state.</summary>
        private const float NameLabelDeadScale = 0.55f;

        /// <summary>Snap radius of the empty hand to the foregrip while two-handed (m).
        /// <para>⚠️ A <b>packet-loss safety, not a cosmetic tweak</b> (§6.6): <c>FLAG_GRIP_LINKED</c> can
        /// be lost or stale on UDP, and an unconditional snap would stretch the arm across the arena in
        /// the window where the weapon really was dropped. Beyond this radius the REAL pose wins.</para></summary>
        private const float SecondaryGripSnapRadius = 0.25f;

        /// <summary>Draw items in a dead avatar's hands? <b>No</b> — a presentation decision with no wire
        /// counterpart: a weapon left in a dead hand reads as still threatening.</summary>
        private const bool DrawItemsWhileDead = false;

        /// <summary>Child holding the weapon geometry in an item prefab (<c>WeaponKitBuilder</c> creates
        /// it at the root; locally <c>Weapon.modelPivot</c> binds to the same node).</summary>
        private const string ModelPivotChildName = "Model";

        private const string DeadLabelSuffix = " (ölü)";
        private const string UncalibratedLabelSuffix = " (KALİBRESİZ)";

        /// <summary>Pulse rate of an uncalibrated avatar (full round trips per second).</summary>
        private const float UncalibratedPulseHz = 1.6f;

        /// <summary>Maximum blend of the pulse between team colour and white.</summary>
        private const float UncalibratedPulseAmount = 0.85f;

        /// <summary>Pulse colour of an uncalibrated character, deliberately far from the team colours:
        /// NOT a team marker but a "this avatar's position is a lie" warning.</summary>
        private static readonly Color UncalibratedTint = new Color(1f, 0.45f, 0.1f);

        /// <summary>Base alpha of the ghost body; the shader adds rim glow on top, so the silhouette is
        /// always more opaque.</summary>
        private const float GhostBaseAlpha = 0.28f;

        /// <summary>RED team colour of the ghost body. ⚠️ The opaque team colours are not reused: a
        /// colour picked for an opaque body washes out at <see cref="GhostBaseAlpha"/>. Second tones of
        /// the same team, not a second palette.</summary>
        private static readonly Color GhostRedColor = new Color(0.90f, 0.20f, 0.20f);

        /// <summary>BLUE team ghost colour (rationale in <see cref="GhostRedColor"/>).</summary>
        private static readonly Color GhostBlueColor = new Color(0.20f, 0.45f, 0.90f);

        /// <summary>Ghost colour of a player with NO team (FFA). ⚠️ Must stay neutral: a team colour
        /// would point at a team that does not exist. It says "no team", not "enemy".</summary>
        private static readonly Color GhostNeutralColor = new Color(0.85f, 0.85f, 0.85f);

        /// <summary>Id of the remote player this avatar represents.</summary>
        public int PlayerId { get; private set; }

        /// <summary>Alive flag from the last snapshot (true when there is no record).</summary>
        public bool IsAlive { get; private set; } = true;

        /// <summary>Is this player's alignment valid per the server (§10.6; from the roster)?</summary>
        public bool IsCalibrated { get; private set; } = true;

        /// <summary>Spawn protection flag from the last snapshot (§10.4; false when there is no record).
        /// ⚠️ PRESENTATION only: duration and damage blocking belong to the server; the client follows
        /// the flag and counts nothing.</summary>
        public bool IsSpawnProtected { get; private set; }

        /// <summary>Is this client an <b>observer</b> (admin)? Written only by <c>AdminSpectator</c>,
        /// never in the player build. Its only effect is bypassing the label gate: hiding opponent
        /// labels is a <b>game</b> rule and the operator must see who is where.
        /// <para>Static because <see cref="RemoteAvatar"/> is in Core while the role-aware
        /// <c>AppSession</c> is in App (dependencies flow downward); same pattern as
        /// <c>ArenaBoundary.SetSpectatorMode</c>.</para></summary>
        public static bool SpectatorMode { get; set; }

        // Kept as a field to avoid GC (SetInfo can run on every lobby_state).
        private MaterialPropertyBlock _propertyBlock;

        // Camera.main is not searched every frame — cached, retried once a second while null.
        private Camera _mainCamera;
        private float _cameraRetryTimer;

        private bool _visible = true;

        // Name/number/colour are stored in SetInfo; the dead look is applied on top of them.
        private string _displayName = "";

        /// <summary>Jersey number (§2); 0 = unassigned → not printed on the label.</summary>
        private int _number;

        private Color _teamColor = NeutralColor;

        /// <summary>This player's team (roster <c>team</c> field; empty/unknown =
        /// <see cref="Team.Neutral"/>). Kept separate from the colour because the label gate is an
        /// identity question, not a colour one.</summary>
        private Team _team = Team.Neutral;

        /// <summary>Ghost body colour — same source as <see cref="_teamColor"/>, own tone so it reads
        /// while translucent.</summary>
        private Color _ghostTeamColor = GhostNeutralColor;

        /// <summary>Label colour — same source as <see cref="_teamColor"/>, falling back to
        /// <see cref="NameLabelNeutralColor"/> when teamless.</summary>
        private Color _labelColor = NameLabelNeutralColor;

        // ── Held items (§6.6) ───────────────────────────────────────────────────────────
        // Catalogue held in a field to avoid a per-frame lookup.
        private NetItemCatalog _itemCatalog;

        // Container of drawn instances: visibility is toggled from this single root and instances are
        // NOT destroyed, so an unchanged state never re-instantiates.
        private Transform _itemsRoot;

        // ⚠️ INACTIVE staging root: a new instance is built here first (inactive → the prefab's Awake
        // never runs) and moved to _itemsRoot after sterilisation. Stripping components on an active
        // root would not stop Awake running once (audio, physics, subscriptions).
        private Transform _itemStagingRoot;

        // Currently drawn state — compared against the incoming state each frame.
        private byte _shownItemL;
        private byte _shownItemR;
        private bool _shownGripLinked;

        // Effective primary hand (only while _shownGripLinked). May DIFFER from the wire: if the slot
        // flagged primary is empty, the other counts as primary (rationale below).
        private bool _shownPrimaryRight;

        private ItemDefinition _itemDefL;
        private ItemDefinition _itemDefR;
        private Transform _itemInstanceL;
        private Transform _itemInstanceR;

        // HoldMode ↔ GRIP_LINKED conflict is logged ONCE per state (at 20 Hz it would be a flood).
        private bool _holdModeMismatchWarned;

        // ── Recoil (derived from the §6.4/6.5 shot event) ───────────────────────────────
        /// <summary>Recoil state of one hand. Recoil DOES NOT EXIST on the wire: the local curve
        /// (<c>Weapon.Update</c>) is reproduced here from the incoming shot event. A struct passed by
        /// <c>ref</c>, so no boxing or per-frame allocation.</summary>
        private struct RecoilSlot
        {
            /// <summary>The instance's <c>Model</c> child — searched ONLY when the instance is built.</summary>
            public Transform Pivot;

            public Vector3 BasePosition;
            public Quaternion BaseRotation;

            public float Kick;
            public float KickBack;

            /// <summary>Recovery speed from the last shot's definition (degrees/s).</summary>
            public float RecoverSpeed;

            /// <summary>Write the transform this frame? The final frame that reaches zero IS written
            /// (the pivot lands exactly on its base), then the flag drops — no traffic when idle.</summary>
            public bool Settling;
        }

        private RecoilSlot _recoilL;
        private RecoilSlot _recoilR;

        // "Instance has no Model child" warns ONCE per player (the event path runs 53-160/s).
        private bool _modelPivotWarned;

        /// <summary>Unbound <see cref="character"/> warns once per instance (LateUpdate is 72/s).</summary>
        private bool _characterWarned;

        // ── Ghost body ──────────────────────────────────────────────────────────────────
        // Each bodyRenderer's ORIGINAL array (to undo the swap) plus a ghost array of the SAME LENGTH.
        // ⚠️ Length must match exactly: extra materials draw the LAST sub-mesh twice, missing ones
        // leave a sub-mesh undrawn.
        private Material[][] _bodyOriginalMaterials;
        private Material[][] _bodyGhostMaterials;

        // ── Shield shell (§10.4) ────────────────────────────────────────────────────────
        // A second renderer under every renderer of the active body, drawing the SAME mesh with the
        // SAME bones. ⚠️ The shield material CANNOT be appended to the body's array (extras land on the
        // LAST sub-mesh only, leaving half a two-sub-mesh body unshielded) and a material SWAP is not
        // an option either — a protected player must stay visible.
        private Renderer[] _bodyShieldShells;
        private Renderer[] _redShieldShells;

        /// <summary>Name of the shell object — sits alone under the body in the hierarchy.</summary>
        private const string ShieldShellName = "ShieldShell";

        /// <summary>Fade in/out factor of the shield material (<c>_Fade</c> in <c>CharacterShieldV2</c>).</summary>
        private static readonly int ShieldFadeId = Shader.PropertyToID("_Fade");

        /// <summary>Flash ceiling at the moment the shield is born (1 = normal).</summary>
        private const float ShieldBirthFlash = 1.8f;

        /// <summary>Time for the birth flash to settle to normal (s).</summary>
        private const float ShieldBirthSeconds = 0.25f;

        /// <summary>Time for the shield to melt away after protection ends (s).</summary>
        private const float ShieldReleaseSeconds = 0.3f;

        /// <summary>Padding added to the shell's bounds (m) — matching the shader's inflation.</summary>
        private const float ShieldBoundsPadding = 0.2f;

        /// <summary>Visual intensity of the shield: 0 = none, 1 = normal, above 1 = birth flash.
        /// ⚠️ NOT a protection timer — only the snapshot flag says whether protection is on. These
        /// durations are the visual tail after the flag drops; the client can neither extend nor
        /// shorten protection.</summary>
        private float _shieldFade;

        /// <summary>Last <c>_Fade</c> written to the shells — skipped on frames where it is unchanged.</summary>
        private float _shieldFadeWritten = -1f;

        /// <summary>Property block of the shells, SEPARATE from the body's <c>_BaseColor</c> block.
        /// ⚠️ One block cannot be shared: a property block applies per renderer as a whole, so the
        /// ghost colour would reach the shell too.</summary>
        private MaterialPropertyBlock _shieldBlock;

        // ── Team body ───────────────────────────────────────────────────────────────────
        /// <summary>Renderers of the red team body; null when <see cref="redBodyRoot"/> is empty.</summary>
        private Renderer[] _redBodyRenderers;

        /// <summary>Bridge driving the red body from the live skeleton; toggled with the body.</summary>
        private SkeletonPoseMirror _redBodyDriver;

        /// <summary>Is this player on the RED team — selects both the drawn body and the hit box set.</summary>
        private bool _useRedBody;

        // Each body's OWN boxes (collected once in Awake) — rationale in CacheHitColliders.
        private Collider[] _defaultHitColliders;
        private Collider[] _redHitColliders;

        [Tooltip("Eli boş oyuncunun parmak duruşu (§6.9 — telde gitmez, burada çizilir). " +
                 "Tümü 0 = yazılmamış → gevşek dinlenme duruşu kullanılır.")]
        [SerializeField] private HandPoseProfile idleHandPose;


        // ⚠️ The material swap applies to the ACTIVE body, so each body needs its own original/ghost
        // arrays — one shared set would write to the wrong renderer after a team change.
        private Material[][] _redOriginalMaterials;
        private Material[][] _redGhostMaterials;

        /// <summary>Renderers of the body being drawn — ghost/visibility always apply to this.</summary>
        private Renderer[] ActiveBodyRenderers =>
            _useRedBody && _redBodyRenderers != null && _redBodyRenderers.Length > 0
                ? _redBodyRenderers
                : bodyRenderers;

        /// <summary>ORIGINAL material arrays of the drawn body (same order as <see cref="ActiveBodyRenderers"/>).</summary>
        private Material[][] ActiveOriginalMaterials =>
            _useRedBody && _redBodyRenderers != null && _redBodyRenderers.Length > 0
                ? _redOriginalMaterials
                : _bodyOriginalMaterials;

        /// <summary>Ghost material arrays of the drawn body.</summary>
        private Material[][] ActiveGhostMaterials =>
            _useRedBody && _redBodyRenderers != null && _redBodyRenderers.Length > 0
                ? _redGhostMaterials
                : _bodyGhostMaterials;

        /// <summary>Shield shells of the drawn body. ⚠️ Shells are per body (different meshes and bones)
        /// but the MATERIAL is shared: the shield says "cannot be touched right now", not which team —
        /// a team-coloured shield would blend into the team-coloured ghost.</summary>
        private Renderer[] ActiveShieldShells =>
            _useRedBody && _redBodyRenderers != null && _redBodyRenderers.Length > 0
                ? _redShieldShells
                : _bodyShieldShells;

        /// <summary>Draw state of the body. Ghost and shield use different mechanisms but there is ONE
        /// state, not two flags: two flags would leave the override order to the caller and the body
        /// could be drawn ghosted and shielded in the same frame.</summary>
        private enum BodyVisual
        {
            Normal,
            Ghost,
            Shield
        }

        // Applied look — avoids needless per-frame material/renderer traffic.
        private BodyVisual _bodyVisual;
        private bool _bodyVisualKnown;

        /// <summary>Missing ghost setup warns once per instance.</summary>
        private bool _ghostSetupWarned;

        /// <summary>Missing shield setup warns once per instance.</summary>
        private bool _shieldSetupWarned;

        private void Awake()
        {
            CacheHitColliders();

            // ⚠️ Built HERE, not in the prefab: finger axes must be measured in the BIND POSE and Awake
            // is the only safe moment before the retargeter first writes to the skeleton. The red body
            // needs no setup — SkeletonPoseMirror copies localRotations, so fingers carry over.
            if (character != null)
            {
                gameObject.AddComponent<RemoteHandPoser>().Bind(this, character.transform);
            }

            _itemCatalog = NetItemCatalog.Load();

            // ⚠️ Order matters: ghost arrays are built for BOTH bodies, so the red body's renderers must
            // already be collected.
            CacheRedBody();
            CacheGhostMaterials();

            // ⚠️ Shells come AFTER CacheRedBody: _redBodyRenderers is collected with
            // GetComponentsInChildren, and a leaked shell would get the body's enable/ghost swap too.
            CacheShieldShells();
        }

        /// <summary>Collects the red team body ONCE and spawns it DISABLED (the team arrives with the
        /// first <see cref="SetInfo"/>). ⚠️ Scope is under <see cref="redBodyRoot"/> so the two bodies
        /// stay separate; with the field empty nothing happens.</summary>
        private void CacheRedBody()
        {
            if (redBodyRoot == null)
            {
                return;
            }

            _redBodyRenderers = redBodyRoot.GetComponentsInChildren<Renderer>(true);
            SetRenderersEnabled(_redBodyRenderers, false);

            // Driving the pose of an invisible body is wasted work — the driver toggles with the body.
            _redBodyDriver = redBodyRoot.GetComponentInChildren<SkeletonPoseMirror>(true);
            if (_redBodyDriver != null)
            {
                _redBodyDriver.enabled = false;
            }
        }

        /// <summary>Builds the ghost swap arrays ONCE: the ghost is a material swap on the drawn body's
        /// OWN mesh, so with no pose transfer it cannot structurally drift.
        /// <para>⚠️ <c>sharedMaterials</c> returns a NEW array per call, so it is read once and stored
        /// (needed anyway to swap back). ⚠️ Originals are stored unconditionally so a prefab without a
        /// ghost material never ends up material-less.</para></summary>
        private void CacheGhostMaterials()
        {
            // ⚠️ Built for BOTH bodies: going ghost is team-independent, so a player who dies while on
            // the team body must have their swap array ready.
            _bodyOriginalMaterials = CaptureOriginalMaterials(bodyRenderers);
            _redOriginalMaterials = CaptureOriginalMaterials(_redBodyRenderers);

            _bodyGhostMaterials = BuildSwapMaterials(_bodyOriginalMaterials, ghostMaterial);
            _redGhostMaterials = BuildSwapMaterials(_redOriginalMaterials, ghostMaterial);
        }

        /// <summary>Builds the shield shells ONCE: a disabled second renderer under every body renderer,
        /// drawing the same mesh with the same bones.
        /// <para>⚠️ Not a breach of the "do not bind a second model to the character's skeleton" ban —
        /// that ban is about ANOTHER FBX's mesh (different proportions → deformed body). With no
        /// material bound no shell is built and protection still works on the server.</para></summary>
        private void CacheShieldShells()
        {
            _bodyShieldShells = BuildShieldShells(bodyRenderers, shieldMaterial);
            _redShieldShells = BuildShieldShells(_redBodyRenderers, shieldMaterial);
        }

        /// <summary>Stores a body's original material arrays (the single source for undoing the swap).</summary>
        private static Material[][] CaptureOriginalMaterials(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return null;
            }

            var original = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer target = renderers[i];
                if (target != null)
                {
                    original[i] = target.sharedMaterials;
                }
            }

            return original;
        }

        /// <summary>Builds swap arrays of the SAME LENGTH as the originals, filled with one material;
        /// <c>null</c> when unbound. ⚠️ Length is copied from the original — a mismatch draws the last
        /// sub-mesh twice or not at all.</summary>
        private static Material[][] BuildSwapMaterials(Material[][] original, Material swap)
        {
            if (original == null || swap == null)
            {
                return null;
            }

            var built = new Material[original.Length][];
            for (int i = 0; i < original.Length; i++)
            {
                Material[] sourceMaterials = original[i];
                if (sourceMaterials == null)
                {
                    continue;
                }

                var swapped = new Material[sourceMaterials.Length];
                for (int m = 0; m < swapped.Length; m++)
                {
                    swapped[m] = swap;
                }

                built[i] = swapped;
            }

            return built;
        }

        /// <summary>Builds a body's shield shells (same order as the source array); <c>null</c> when no
        /// material is bound.</summary>
        private static Renderer[] BuildShieldShells(Renderer[] sources, Material shieldMaterial)
        {
            if (sources == null || sources.Length == 0 || shieldMaterial == null)
            {
                return null;
            }

            var shells = new Renderer[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                shells[i] = BuildShieldShell(sources[i], shieldMaterial);
            }

            return shells;
        }

        /// <summary>Builds one body renderer's shell: an identity-transform child under the source
        /// drawing its mesh on the same bones, born disabled.
        /// <para>⚠️ Shadows off — a translucent shield casting an opaque shadow reads as a solid body.
        /// ⚠️ The shell draws the mesh <b>as is</b>; thickness comes from
        /// <c>CharacterShieldV2</c>'s vertex stage, and inflating by duplicating the mesh will not come
        /// back (FBX Read/Write is off, and thickness would become tunable from two places).</para>
        /// <para>Unsupported renderer types and mesh-less sources are skipped.</para></summary>
        private static Renderer BuildShieldShell(Renderer source, Material shieldMaterial)
        {
            if (source == null)
            {
                return null;
            }

            var skinned = source as SkinnedMeshRenderer;
            MeshFilter sourceFilter = null;
            if (skinned == null && source is MeshRenderer)
            {
                sourceFilter = source.GetComponent<MeshFilter>();
            }

            Mesh sourceMesh = skinned != null
                ? skinned.sharedMesh
                : sourceFilter != null ? sourceFilter.sharedMesh : null;
            if (sourceMesh == null)
            {
                return null;
            }

            Mesh shellMesh = sourceMesh;

            var shellObject = new GameObject(ShieldShellName)
            {
                // Layer copied from the source: the shell follows the body's culling rules.
                layer = source.gameObject.layer
            };

            Transform shell = shellObject.transform;
            shell.SetParent(source.transform, false);
            shell.localPosition = Vector3.zero;
            shell.localRotation = Quaternion.identity;
            shell.localScale = Vector3.one;

            Renderer shellRenderer;
            if (skinned != null)
            {
                var shellSkinned = shellObject.AddComponent<SkinnedMeshRenderer>();
                shellSkinned.sharedMesh = shellMesh;
                shellSkinned.bones = skinned.bones;
                shellSkinned.rootBone = skinned.rootBone;
                shellSkinned.quality = skinned.quality;

                // ⚠️ Bounds are EXPANDED a little: the shader offsets vertices along their normals, so
                // verbatim source bounds would cull the bubble's edge while the body is on screen.
                Bounds shellBounds = skinned.localBounds;
                shellBounds.Expand(ShieldBoundsPadding);
                shellSkinned.localBounds = shellBounds;

                shellSkinned.updateWhenOffscreen = skinned.updateWhenOffscreen;
                shellRenderer = shellSkinned;
            }
            else
            {
                shellObject.AddComponent<MeshFilter>().sharedMesh = shellMesh;
                shellRenderer = shellObject.AddComponent<MeshRenderer>();
            }

            // ⚠️ Filled to the sub-mesh count — a short array leaves sub-meshes undrawn, a long one
            // draws the last one twice.
            var materials = new Material[Mathf.Max(1, shellMesh.subMeshCount)];
            for (int m = 0; m < materials.Length; m++)
            {
                materials[m] = shieldMaterial;
            }

            shellRenderer.sharedMaterials = materials;
            shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
            shellRenderer.enabled = false;
            return shellRenderer;
        }

        /// <summary>Set up by the spawner; pose lookups use this id.</summary>
        public void Initialize(int playerId)
        {
            if (PlayerId != playerId)
            {
                // Handed to another player: drop the old player's items.
                ClearHeldItems();

                // ⚠️ The old skeleton and root must go too — latched state that does not fix itself;
                // otherwise the new owner is drawn with the previous player's body.
                RemoteSkeletonRegistry.Instance?.Forget(PlayerId);
            }

            PlayerId = playerId;

            // Driven by the networked skeleton: this avatar never has input authority (our own body is
            // LocalBodyAvatar's). This also disables the sensor source.
            if (character != null)
            {
                character.Initialize(playerId, hasInputAuthority: false);
            }
        }

        /// <summary>Updates the name label, jersey number and team colour ("red"/"blue"/other=grey). A
        /// <paramref name="number"/> of 0 is NOT printed (§2).
        /// <para>⚠️ The ghost colour comes from here too, so a team CHANGE must refresh the look: an
        /// admin can change teams mid-match and a dead player's ghost would freeze in the old
        /// colour.</para></summary>
        public void SetInfo(string displayName, int number, string team)
        {
            _displayName = displayName ?? "";
            _number = number;
            _team = team == "red" ? Team.Red : team == "blue" ? Team.Blue : Team.Neutral;
            _teamColor = team == "red" ? TeamRedColor : team == "blue" ? TeamBlueColor : NeutralColor;
            _ghostTeamColor = team == "red" ? GhostRedColor : team == "blue" ? GhostBlueColor : GhostNeutralColor;
            _labelColor = team == "red" ? TeamRedColor
                : team == "blue" ? TeamBlueColor : NameLabelNeutralColor;

            ApplyRedBody(team == "red");

            ApplyLabelText();
            RefreshLabelVisibility();
            ApplyTeamColor();
            ApplyBodyVisual();
        }

        /// <summary>Picks the body to draw by team; only ONE is drawn at a time.
        /// <para>⚠️ Switching bodies needs the same cleanup as leaving ghost state: the passive body is
        /// never touched again, so a ghost material or property block left on it FREEZES there and comes
        /// back on the player's next death. With no team body set up nothing happens.</para></summary>
        private void ApplyRedBody(bool useRed)
        {
            if (_redBodyRenderers == null || _redBodyRenderers.Length == 0)
            {
                return;
            }

            if (_useRedBody == useRed)
            {
                return;
            }

            _useRedBody = useRed;

            Renderer[] passive = useRed ? bodyRenderers : _redBodyRenderers;
            ClearPropertyBlocks(passive);
            WriteMaterials(passive, useRed ? _bodyOriginalMaterials : _redOriginalMaterials);

            // Re-apply the visual state from scratch on the NEW active body (untouched until now).
            _bodyVisualKnown = false;

            // ⚠️ The hit box set must change WITH the body: boxes hang off bones and the two models have
            // different proportions, so otherwise the player keeps the old body's hit volume.
            RefreshColliders();
        }

        /// <summary>Applies calibration state (§10.6; from <c>lobby_state</c>). An uncalibrated avatar
        /// glows, gets " (KALİBRESİZ)" on its label and has its hit boxes disabled — the server already
        /// rejects the damage, so this only spares the shooter a phantom hit.</summary>
        public void SetCalibrated(bool calibrated)
        {
            if (IsCalibrated == calibrated)
            {
                return;
            }

            IsCalibrated = calibrated;
            ApplyLabelText();
            ApplyTeamColor();
            ApplyBodyVisual();
            RefreshColliders();
        }

        /// <summary>Applies the body scale (§10.8; from <c>lobby_state</c>). <c>0</c> = unmeasured → 1.
        /// <see cref="ArenaNetCharacterBehaviour"/> writes it onto the character root; no transform is
        /// touched here. The red body, the ghost and the bone-mounted hit boxes follow by themselves.
        /// <para>⚠️ <b>An item drawn in hand does not</b> — it is driven from the raw hand pose. The
        /// weapon stays real size but its POSITION must follow the scale or it detaches as the body
        /// grows (<see cref="ApplyItemPoses"/>).</para></summary>
        public void SetBodyScale(float bodyScale)
        {
            if (character == null)
            {
                return;
            }

            float applied = bodyScale > 0f ? bodyScale : 1f;
            if (Mathf.Approximately(character.BodyScale, applied))
            {
                return;
            }

            character.BodyScale = applied;
        }

        /// <summary>Name label; suffixed " (ölü)" while dead and " (KALİBRESİZ)" while uncalibrated. The
        /// COLOUR is the team (darkened while dead), matching the admin card and top-view marker.
        /// <para>⚠️ Not an exception to "team colour is never written to the character mesh": the label
        /// already spells out identity, opens no new information and is depth-tested.</para></summary>
        private void ApplyLabelText()
        {
            if (nameLabel != null)
            {
                string suffix = !IsCalibrated ? UncalibratedLabelSuffix : IsAlive ? "" : DeadLabelSuffix;
                string prefix = _number > 0 ? _number + " · " : "";
                nameLabel.text = prefix + _displayName + suffix;
                nameLabel.color = IsAlive
                    ? _labelColor
                    : new Color(_labelColor.r * NameLabelDeadScale, _labelColor.g * NameLabelDeadScale,
                        _labelColor.b * NameLabelDeadScale, _labelColor.a);
            }
        }

        /// <summary>
        /// <b>Who sees the name label:</b> only the local player's TEAMMATES, in every mode/map/phase.
        /// Three gates: teamless mode (<see cref="ModeRuntime.IsTeamless"/>) draws nobody's; a
        /// <see cref="Team.Neutral"/> local team draws nothing either (showing labels while "unknown" is
        /// exactly the leaking case); otherwise team equality is required. The only exception is the
        /// observer (<see cref="SpectatorMode"/>).
        /// <para>⚠️ The gate is asked <b>at draw time</b>, not via an event subscription: the local team
        /// can change mid-match and the mode changes with <c>load_match</c> — one missed event would
        /// leave an opponent's name on screen forever.</para></summary>
        private bool ShouldShowNameLabel()
        {
            if (SpectatorMode)
            {
                return true;
            }

            if (ModeRuntime.IsTeamless)
            {
                return false;
            }

            Team local = ArenaCombat.LocalTeam;
            return local != Team.Neutral && _team == local;
        }

        /// <summary>Refreshes whether the label draws (visibility + opponent gate).</summary>
        private void RefreshLabelVisibility()
        {
            if (nameLabel == null)
            {
                return;
            }

            bool show = _visible && ShouldShowNameLabel();
            if (nameLabel.enabled != show)
            {
                nameLabel.enabled = show;
            }
        }

        /// <summary>Writes the team colour through a MaterialPropertyBlock; darkened while dead, glowing
        /// while uncalibrated.
        /// <para>⚠️ The glow uses a <c>_BaseColor</c> pulse, NOT emission: a property block cannot enable
        /// a shader keyword, so <c>_EmissionColor</c> would silently do nothing, and a second material
        /// instance would break SRP batching on Quest.</para></summary>
        private void ApplyTeamColor()
        {
            if (teamRenderers == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            Color color;
            if (!IsCalibrated)
            {
                // Uncalibrated OVERRIDES dead: what matters is "this one's alignment is broken".
                float pulse = Mathf.PingPong(Time.time * UncalibratedPulseHz, 1f) * UncalibratedPulseAmount;
                color = Color.Lerp(_teamColor, Color.white, pulse);
            }
            else if (IsAlive)
            {
                color = _teamColor;
            }
            else
            {
                color = new Color(_teamColor.r * DeadColorScale, _teamColor.g * DeadColorScale, _teamColor.b * DeadColorScale, _teamColor.a);
            }

            for (int i = 0; i < teamRenderers.Length; i++)
            {
                Renderer target = teamRenderers[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// Body look: alive + calibrated = <b>untouched</b>, dead or uncalibrated = <b>ghost</b>, spawn
        /// protected = <b>shield</b> (§10.4).
        /// <para>⚠️ The shield is a different mechanism: it never touches the body's material, it draws
        /// as a second renderer on top — a protected player must keep looking normal. Both are driven
        /// from one state, so they cannot override each other in a frame.</para>
        /// <para>⚠️ The shield material CANNOT be appended to the body's material array (extras land on
        /// the LAST sub-mesh only). ⚠️ <b>Ghosting is a material swap, NOT alpha:</b> the character's
        /// material is URP Lit and OPAQUE, so writing <c>_BaseColor.a</c> does nothing.</para>
        /// <para>⚠️ The ghost is NOT a separate model — the drawn body's own mesh is swapped, so drift
        /// is structurally impossible; its look is changed in <c>M_AvatarGhost</c>, not in code.</para>
        /// <para>⚠️ <b>Team colour is still never written to a LIVE character</b> (marking enemies would
        /// be an advantage); only the ghost, which is no longer a threat, is team-coloured.</para>
        /// </summary>
        private void ApplyBodyVisual()
        {
            BodyVisual visual = ResolveBodyVisual();

            if (_bodyVisualKnown && visual == _bodyVisual)
            {
                // Same state: only the colour is refreshed (the uncalibrated pulse runs every frame).
                if (visual == BodyVisual.Ghost)
                {
                    ApplyGhostColor();
                }

                return;
            }

            _bodyVisualKnown = true;
            _bodyVisual = visual;

            if (visual == BodyVisual.Ghost && ActiveGhostMaterials == null)
            {
                WarnMissingGhostSetup();
            }
            else if (visual == BodyVisual.Shield && (ActiveShieldShells == null || ActiveShieldShells.Length == 0))
            {
                WarnMissingShieldSetup();
            }

            ApplyBodyMaterials(visual);

            // Body selection is independent of the visual state: passive off, active on.
            SyncBodyRendererEnable();

            if (visual == BodyVisual.Ghost)
            {
                ApplyGhostColor();
            }
            else
            {
                // ⚠️ The property block is REMOVED in shield state too: the shield's colour lives in its
                // material, and _BaseColor on top would leak the ghost's team tone into it.
                ClearGhostColor();
            }
        }

        /// <summary>Picks the body's draw state.
        /// <para>⚠️ Visibility is part of the decision: no swap on an invisible avatar — the state is
        /// recomputed from <see cref="SetVisible"/> when it returns. ⚠️ <b>Ghost OVERRIDES shield</b> and
        /// never the reverse: shielding a dead body would read as "this one is protected".</para>
        /// <para>The shield draws a little past the flag until <see cref="_shieldFade"/> hits zero, so it
        /// "ends" rather than "disappears". ⚠️ That tail is VISUAL only — the player is already
        /// hittable throughout it.</para></summary>
        private BodyVisual ResolveBodyVisual()
        {
            if (!_visible)
            {
                return BodyVisual.Normal;
            }

            if (!IsCalibrated || !IsAlive)
            {
                return BodyVisual.Ghost;
            }

            return IsSpawnProtected || _shieldFade > 0f ? BodyVisual.Shield : BodyVisual.Normal;
        }

        /// <summary>The ghost is coloured by the player's OWN team; it pulses orange while uncalibrated
        /// (uncalibrated OVERRIDES dead).
        /// <para>⚠️ The colour is <b>not friend/foe</b>: that depends on the viewer, so the same dead
        /// player would differ per headset and collapse to one colour on the admin screen. No
        /// see-through advantage — normal <c>ZTest</c>, and only on a body that is no threat.</para></summary>
        private void ApplyGhostColor()
        {
            Color color = _ghostTeamColor;

            if (!IsCalibrated)
            {
                float pulse = Mathf.PingPong(Time.time * UncalibratedPulseHz, 1f) * UncalibratedPulseAmount;
                color = Color.Lerp(color, UncalibratedTint, pulse);
            }

            color.a = GhostBaseAlpha;
            WriteBaseColor(GhostTargets, color);
        }

        /// <summary>Puts both bodies' renderers into the right state. ⚠️ The active body stays ENABLED in
        /// ghost state (the ghost is a material swap on that mesh) and the passive one is ALWAYS
        /// disabled (two bodies draw as interpenetrating characters).</summary>
        private void SyncBodyRendererEnable()
        {
            Renderer[] active = ActiveBodyRenderers;
            SetRenderersEnabled(active, true);

            Renderer[] passive = ReferenceEquals(active, bodyRenderers) ? _redBodyRenderers : bodyRenderers;
            SetRenderersEnabled(passive, false);

            SyncShieldShells();
            RefreshRedBodyDriver();
        }

        /// <summary>Enables/disables the shield shells by body state (§10.4). The passive body's shells
        /// are ALWAYS off. Shells live under the body, so a hidden avatar needs no extra handling.</summary>
        private void SyncShieldShells()
        {
            Renderer[] active = ActiveShieldShells;
            SetRenderersEnabled(active, _bodyVisual == BodyVisual.Shield);

            // ⚠️ The written value is FORGOTTEN: a team change swaps the shells, and the new ones would
            // never have had _Fade written (drawing with the material's default).
            _shieldFadeWritten = -1f;

            Renderer[] passive = ReferenceEquals(active, _bodyShieldShells) ? _redShieldShells : _bodyShieldShells;
            SetRenderersEnabled(passive, false);
        }

        /// <summary>Toggles the red body bridge: driven only while that body is ACTIVE and the avatar is
        /// VISIBLE. ⚠️ <see cref="SetVisible"/> must call this separately — on a live avatar a visibility
        /// change does not change the ghost state, so <see cref="ApplyBodyVisual"/> returns early and the
        /// body would freeze in T-pose when it returns.</summary>
        private void RefreshRedBodyDriver()
        {
            if (_redBodyDriver != null)
            {
                _redBodyDriver.enabled = _visible && _useRedBody;
            }
        }

        /// <summary>Renderers the ghost colour is written to: the ACTIVE body's mesh.</summary>
        private Renderer[] GhostTargets => ActiveBodyRenderers;

        /// <summary>Leaving ghost state REMOVES the property block rather than clearing it, so the
        /// renderer returns to the SRP Batcher (a renderer with a property block stays outside it).</summary>
        private void ClearGhostColor()
        {
            // Both bodies are cleared: removal is cheap and a block left on the passive body would
            // silently return later.
            ClearPropertyBlocks(bodyRenderers);
            ClearPropertyBlocks(_redBodyRenderers);
        }

        /// <summary>Switches the ACTIVE body's mesh to the requested look's materials, falling back to
        /// the ORIGINAL array when the ghost material is unbound. ⚠️ Only the <b>ghost</b> swaps; the
        /// shield never touches the body's materials.</summary>
        private void ApplyBodyMaterials(BodyVisual visual)
        {
            Material[][] target = visual == BodyVisual.Ghost ? ActiveGhostMaterials : ActiveOriginalMaterials;

            WriteMaterials(ActiveBodyRenderers, target ?? ActiveOriginalMaterials);
        }

        /// <summary>Writes stored material arrays to renderers (order matches
        /// <see cref="CaptureOriginalMaterials"/>).</summary>
        private static void WriteMaterials(Renderer[] targets, Material[][] materials)
        {
            if (targets == null || materials == null)
            {
                return;
            }

            int count = Mathf.Min(targets.Length, materials.Length);
            for (int i = 0; i < count; i++)
            {
                Renderer target = targets[i];
                if (target != null && materials[i] != null)
                {
                    target.sharedMaterials = materials[i];
                }
            }
        }

        /// <summary>Ghost requested but there is no target — logs an ERROR once per instance. ⚠️ Error,
        /// not warning: a dead player would be indistinguishable from a live one.</summary>
        private void WarnMissingGhostSetup()
        {
            if (_ghostSetupWarned)
            {
                return;
            }

            _ghostSetupWarned = true;
            Debug.LogError(
                $"[RemoteAvatar] Oyuncu {PlayerId}: hayalet görünümü kurulmamış — RemoteAvatar " +
                "prefabında 'ghostMaterial' (M_AvatarGhost) bağlanmalı. Ölü/kalibresiz oyuncu " +
                "canlıdan ayırt edilemiyor.", this);
        }

        /// <summary>Shield requested but no shell could be built — logs a WARNING once per instance.
        /// ⚠️ A warning, not an error: only the visual is missing, the server still enforces protection
        /// (§10.4).</summary>
        private void WarnMissingShieldSetup()
        {
            if (_shieldSetupWarned)
            {
                return;
            }

            _shieldSetupWarned = true;
            Debug.LogWarning(
                $"[RemoteAvatar] Oyuncu {PlayerId}: doğma koruması kalkanı çizilemiyor — çizilen " +
                "gövde için kalkan kabuğu kurulamadı. RemoteAvatar prefabında 'shieldMaterial' " +
                "bağlanmalı ve o gövdenin 'bodyRenderers' (kırmızıda 'redBodyRoot') listesi dolu " +
                "olmalı. Koruma işliyor, yalnız görsel çizilmiyor.", this);
        }

        private void WriteBaseColor(Renderer[] targets, in Color color)
        {
            if (targets == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Renderer target = targets[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

        private static void ClearPropertyBlocks(Renderer[] targets)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].SetPropertyBlock(null);
                }
            }
        }

        private static void SetRenderersEnabled(Renderer[] targets, bool enabled)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].enabled = enabled;
                }
            }
        }

        /// <summary>Collects each body's OWN hit boxes once (colliders with the
        /// <see cref="RemoteHitBox"/> marker).
        /// <para>⚠️ <b>Two bodies, TWO SEPARATE sets, deliberately:</b> boxes hang off bones whose
        /// proportions differ per model, so a shared set would drift the hit volume from the drawn body.
        /// The set changes with the drawn body. With no character bound (legacy path) the whole avatar is
        /// scanned.</para></summary>
        private void CacheHitColliders()
        {
            Transform defaultRoot = character != null ? character.transform : transform;
            _defaultHitColliders = CollectHitColliders(defaultRoot);
            _redHitColliders = redBodyRoot != null
                ? CollectHitColliders(redBodyRoot.transform)
                : System.Array.Empty<Collider>();
        }

        private static Collider[] CollectHitColliders(Transform root)
        {
            RemoteHitBox[] boxes = root.GetComponentsInChildren<RemoteHitBox>(true);
            var found = new List<Collider>(boxes.Length);

            for (int i = 0; i < boxes.Length; i++)
            {
                var collider = boxes[i] != null ? boxes[i].GetComponent<Collider>() : null;
                if (collider != null)
                {
                    found.Add(collider);
                }
            }

            return found.ToArray();
        }

        /// <summary>Hit boxes of the drawn body; the other body's are always disabled.</summary>
        private Collider[] ActiveHitColliders =>
            _useRedBody && _redHitColliders != null && _redHitColliders.Length > 0
                ? _redHitColliders
                : _defaultHitColliders;

        /// <summary>A dead/hidden/uncalibrated avatar cannot be shot: hit boxes are disabled.
        /// ⚠️ The undrawn body's boxes are disabled UNCONDITIONALLY — otherwise an invisible volume
        /// swallows bullets and the shooter thinks they missed.</summary>
        private void RefreshColliders()
        {
            bool enable = _visible && IsAlive && IsCalibrated;

            Collider[] active = ActiveHitColliders;
            SetCollidersEnabled(active, enable);
            SetCollidersEnabled(ReferenceEquals(active, _defaultHitColliders)
                ? _redHitColliders
                : _defaultHitColliders, false);
        }

        private static void SetCollidersEnabled(Collider[] targets, bool enabled)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].enabled = enabled;
                }
            }
        }

        private void LateUpdate()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null ||
                !registry.GetInterpolatedPose(PlayerId, out Pose headPose, out Pose handLPose, out Pose handRPose))
            {
                SetVisible(false); // hidden until the first pose
                return;
            }

            SetVisible(true);
            UpdateAlive(registry);
            UpdateSpawnProtection(registry);
            TickShieldFade();

            // The pulse runs ONLY while uncalibrated — otherwise the colour does not change and the
            // state refreshes on events.
            if (!IsCalibrated)
            {
                ApplyTeamColor();
                ApplyBodyVisual();
            }

            // Poses are in arena space — convert to world.
            Pose headWorld = ArenaSpace.ArenaToWorld(headPose);
            Pose handLWorld = ArenaSpace.ArenaToWorld(handLPose);
            Pose handRWorld = ArenaSpace.ArenaToWorld(handRPose);

            // §6.6: items are driven from the RAW hand pose, BEFORE the snap correction — an item's
            // place comes from the primary hand's physical pose.
            UpdateHeldItems(registry);
            ApplyItemPoses(handLWorld, handRWorld);

            // Recoil runs AFTER the grip and does not race it: ApplyGrip writes the instance's ROOT world
            // pose, this curve the local TRS of its 'Model' CHILD (the local weapon uses the same pivot).
            TickRecoil(ref _recoilL);
            TickRecoil(ref _recoilR);

            // ⚠️ The pose channel is NOT touched (§6.2: the raw pose is physical truth) — the snap only
            // changes the DISPLAY copy.
            Pose displayHandL = handLWorld;
            Pose displayHandR = handRWorld;
            ApplySecondaryGripSnap(ref displayHandL, ref displayHandR);

            // ⚠️ With a character bound the BODY IS NEVER TOUCHED here: the skeleton comes over the
            // network and ArenaNetCharacterBehaviour applies it on its own cadence (§6.10). Writing
            // head/hand transforms would be a second driver on top of retargeted bones.
            if (character == null)
            {
                // ⚠️ NOT a "capsule avatar" path: head/handL/handR/body may be empty in the prefab, making
                // all four calls below silent no-ops and freezing the body in T-pose at the world origin.
                WarnMissingCharacter();

                // The root is still moved to the right place: wrong POSE in the right PLACE is a
                // diagnosable fault, unlike an avatar nowhere to be seen.
                transform.SetPositionAndRotation(
                    headWorld.position - Vector3.up * BodyDropMeters,
                    Quaternion.identity);

                Apply(head, headWorld);
                Apply(handL, displayHandL);
                Apply(handR, displayHandR);
                ApplyBody(headWorld);
            }

            // §10.8: anything hung ABOVE the head must follow the DRAWN head, or at scale 1.3 the label
            // floats half a metre below it.
            // ⚠️ The same correction is NOT applied to items: their place is the primary hand's physical
            // pose (where the shot ray leaves) and RemoteHandPoser already bends the arm to meet it —
            // correcting here would apply the scale twice.
            Pose headDrawn = headWorld;
            if (character != null)
            {
                headDrawn.position = character.ScalePointAboutRoot(headWorld.position);
            }

            UpdateLabel(headDrawn);
        }

        /// <summary>The body capsule sits below the head and rotates in YAW only, so the body does not
        /// lie down when the player leans.</summary>
        private void ApplyBody(in Pose headWorldPose)
        {
            if (body == null)
            {
                return;
            }

            Vector3 forward = headWorldPose.rotation * Vector3.forward;
            forward.y = 0f;
            Quaternion yaw = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : body.rotation;

            body.SetPositionAndRotation(headWorldPose.position - Vector3.up * BodyDropMeters, yaw);
        }

        /// <summary>Logs an ERROR once per instance when <see cref="character"/> is unbound. ⚠️ Error,
        /// not warning: nothing drives the body then, and the symptom reads on site as "the network is
        /// broken" when only a prefab reference is missing.</summary>
        private void WarnMissingCharacter()
        {
            if (_characterWarned)
            {
                return;
            }

            _characterWarned = true;
            Debug.LogError(
                $"[RemoteAvatar] Oyuncu {PlayerId}: 'character' alanı boş — uzak gövdeyi çizen " +
                "hiçbir bileşen yok (T-pozunda donar). RemoteAvatar.prefab'daki Character objesine " +
                "ArenaNetCharacterBehaviour + NetworkCharacterRetargeter kurulmalı.", this);
        }

        /// <summary>Reads the snapshot alive flag; refreshes look + colliders when it changed.</summary>
        private void UpdateAlive(RemotePlayerRegistry registry)
        {
            bool alive = registry.IsAlive(PlayerId);
            if (alive == IsAlive)
            {
                return;
            }

            IsAlive = alive;
            ApplyLabelText();
            ApplyTeamColor();
            ApplyBodyVisual();
            RefreshColliders();
            RefreshHeldItemVisibility();
        }

        /// <summary>§10.4: reads the snapshot's spawn protection flag; refreshes the body look on change.
        /// <para>⚠️ WHEN protection ends is not counted on the client — the server owns the duration and
        /// the flag arrives with every snapshot; the birth/release durations are a visual tail only.</para>
        /// <para>Label and hit boxes are NOT refreshed here: protection is read from the body, and
        /// shooting stays legitimate because the server drops the damage.</para></summary>
        private void UpdateSpawnProtection(RemotePlayerRegistry registry)
        {
            bool spawnProtected = registry.IsSpawnProtected(PlayerId);
            if (spawnProtected == IsSpawnProtected)
            {
                return;
            }

            IsSpawnProtected = spawnProtected;

            // Born flashing on the frame protection turns ON; on release TickShieldFade melts it.
            if (spawnProtected)
            {
                _shieldFade = ShieldBirthFlash;
            }

            ApplyBodyVisual();
        }

        /// <summary>Drives the shield's visual intensity and writes it to the shells: the birth flash
        /// settles to normal, then melts to zero when protection ends.
        /// <para>⚠️ Ghost/invisibility drop the shield WITHOUT waiting (a shield melting on a dead player
        /// reads as "still protected"). ⚠️ <c>_Fade</c> is written only when it CHANGES — it sits at 1 for
        /// seconds and per-frame property blocks would be pointless traffic.</para></summary>
        private void TickShieldFade()
        {
            float previous = _shieldFade;

            if (!_visible || !IsAlive || !IsCalibrated)
            {
                _shieldFade = 0f;
            }
            else if (IsSpawnProtected)
            {
                // Come down from the flash; the floor is 1 while protection lasts.
                float step = (ShieldBirthFlash - 1f) / Mathf.Max(ShieldBirthSeconds, 0.0001f);
                _shieldFade = Mathf.Max(1f, _shieldFade - step * Time.deltaTime);
            }
            else if (_shieldFade > 0f)
            {
                float step = 1f / Mathf.Max(ShieldReleaseSeconds, 0.0001f);
                _shieldFade = Mathf.Max(0f, _shieldFade - step * Time.deltaTime);
            }

            // Shield fully out → state is Normal; ApplyBodyVisual disables the shells.
            if (previous > 0f && _shieldFade <= 0f)
            {
                ApplyBodyVisual();
                return;
            }

            if (_bodyVisual != BodyVisual.Shield || Mathf.Approximately(_shieldFade, _shieldFadeWritten))
            {
                return;
            }

            _shieldFadeWritten = _shieldFade;
            _shieldBlock ??= new MaterialPropertyBlock();
            _shieldBlock.SetFloat(ShieldFadeId, _shieldFade);

            Renderer[] shells = ActiveShieldShells;
            if (shells == null)
            {
                return;
            }

            for (int i = 0; i < shells.Length; i++)
            {
                if (shells[i] != null)
                {
                    shells[i].SetPropertyBlock(_shieldBlock);
                }
            }
        }

        /// <summary>§6.6: reads the held item state and builds/destroys instances <b>only on change</b>.
        /// State changes at human speed and Instantiate/Destroy per frame is not free on Quest.</summary>
        private void UpdateHeldItems(RemotePlayerRegistry registry)
        {
            if (!registry.TryGetHeldItems(PlayerId, out byte itemL, out byte itemR, out bool gripLinked, out bool primaryRight))
            {
                itemL = 0;
                itemR = 0;
                gripLinked = false;
                primaryRight = false;
            }

            byte wantL = itemL;
            byte wantR = itemR;

            if (gripLinked)
            {
                // Tolerance for a stale/lost flag: if the slot flagged primary is EMPTY while the other is
                // full, flip the primary hand — otherwise the rifle is not drawn at all that tick.
                if (primaryRight && itemR == 0 && itemL != 0)
                {
                    primaryRight = false;
                }
                else if (!primaryRight && itemL == 0 && itemR != 0)
                {
                    primaryRight = true;
                }

                // ⚠️ GRIP_LINKED = ONE instance, from the primary hand's pose (§6.6). Even with the same id
                // in both slots the second instance is NOT built — only this flag separates it from
                // akimbo.
                if (primaryRight)
                {
                    wantL = 0;
                }
                else
                {
                    wantR = 0;
                }
            }

            if (wantL == _shownItemL && wantR == _shownItemR &&
                gripLinked == _shownGripLinked && primaryRight == _shownPrimaryRight)
            {
                return; // nothing changed: one comparison per frame
            }

            if (wantL != _shownItemL)
            {
                _shownItemL = wantL;
                _itemDefL = Resolve(wantL);
                RebuildItemInstance(ref _itemInstanceL, ref _recoilL, _itemDefL);
            }

            if (wantR != _shownItemR)
            {
                _shownItemR = wantR;
                _itemDefR = Resolve(wantR);
                RebuildItemInstance(ref _itemInstanceR, ref _recoilR, _itemDefR);
            }

            _shownGripLinked = gripLinked;
            _shownPrimaryRight = primaryRight;
            _holdModeMismatchWarned = false;
            WarnOnHoldModeMismatch();
        }

        /// <summary>When an item's <c>HoldMode</c> conflicts with <c>GRIP_LINKED</c>, <b>the wire wins</b>
        /// (the shooting client owns the state, §6.2) — but the conflict signals a content/code bug, so it
        /// is logged once instead of silently showing as a wrong pose.</summary>
        private void WarnOnHoldModeMismatch()
        {
            if (!_shownGripLinked || _holdModeMismatchWarned)
            {
                return;
            }

            ItemDefinition primary = _shownPrimaryRight ? _itemDefR : _itemDefL;
            if (primary == null || primary.IsTwoHanded)
            {
                return;
            }

            _holdModeMismatchWarned = true;
            Debug.LogWarning(
                $"[RemoteAvatar] Oyuncu {PlayerId}: '{primary.DisplayName}' tek elli (HoldMode) ama " +
                "GRIP_LINKED ile geldi — telde gelen esas alındı, duruş yanlış görünebilir (§6.6).");
        }

        private ItemDefinition Resolve(byte netItemId)
        {
            return netItemId == 0 || _itemCatalog == null ? null : _itemCatalog.FindByNetItemId(netItemId);
        }

        /// <summary>Rebuilds one hand's item instance. Called only on state change, so allocation is
        /// legitimate here.</summary>
        private void RebuildItemInstance(ref Transform instance, ref RecoilSlot recoil, ItemDefinition definition)
        {
            // Pivot and accumulated kick are reset: stale recoil would shake a rifle before its first
            // shot.
            recoil = default;

            if (instance != null)
            {
                // Destroy is deferred to end of frame and that frame still DRAWS — disable first so a
                // dropped item is not shown once more in a stale pose.
                instance.gameObject.SetActive(false);
                Destroy(instance.gameObject);
                instance = null;
            }

            if (definition == null || definition.Prefab == null)
            {
                return;
            }

            EnsureItemRoots();

            // Built under the inactive staging root → none of the prefab's Awakes run.
            GameObject spawned = Instantiate(definition.Prefab, _itemStagingRoot);
            SterilizeVisual(spawned);

            // Moved into the visible container after sterilisation.
            spawned.transform.SetParent(_itemsRoot, false);
            instance = spawned.transform;

            CacheRecoilPivot(ref recoil, instance);
        }

        /// <summary>Caches the <c>Model</c> child recoil applies to and its BASE local TRS — the same
        /// thing <c>Weapon.Awake</c> does locally.
        /// <para>⚠️ The search happens ONLY here, when the instance is built: the shot event path runs
        /// 53-160/s, so a <c>Find</c> per frame/event is unacceptable. When not found the pivot stays
        /// null (no recoil visual) and one warning per player is logged.</para></summary>
        private void CacheRecoilPivot(ref RecoilSlot recoil, Transform instance)
        {
            // Direct child first (WeaponKitBuilder places it at the root), then a deep search.
            Transform pivot = instance.Find(ModelPivotChildName);
            if (pivot == null)
            {
                pivot = FindChildByName(instance, ModelPivotChildName);
            }

            if (pivot == null)
            {
                if (!_modelPivotWarned)
                {
                    _modelPivotWarned = true;
                    Debug.LogWarning(
                        $"[RemoteAvatar] Oyuncu {PlayerId}: '{instance.name}' örneğinde " +
                        $"'{ModelPivotChildName}' çocuğu yok — uzak silah geri tepmeyecek.");
                }

                return;
            }

            recoil.Pivot = pivot;
            recoil.BasePosition = pivot.localPosition;
            recoil.BaseRotation = pivot.localRotation;
        }

        // GetComponentsInChildren is NOT used (it allocates): manual recursion is allocation-free and
        // this runs once per item change. The FIRST match wins — an item has one body.
        private static Transform FindChildByName(Transform parent, string childName)
        {
            int count = parent.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform found = FindChildByName(child, childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Reduces the built instance to a <b>pure visual</b>.
        /// <para>⚠️ <b>Cannot be skipped:</b> a working weapon on a remote copy plays audio, does physics,
        /// becomes grabbable and can even fire and emit <c>hit_report</c> — one of the hardest bugs to
        /// diagnose.</para>
        /// <para>⚠️ MonoBehaviours go in REVERSE order (a <c>[RequireComponent]</c> dependant was added
        /// later, so it must go first), then physics and audio, with <c>DestroyImmediate</c> because a
        /// deferred <c>Destroy</c> still counts as present and logs "can't remove component". The instance
        /// sits on an inactive root, so we are not inside a callback.</para></summary>
        private static void SterilizeVisual(GameObject instance)
        {
            // ⚠️ Removed WHOLESALE rather than by type (Weapon, WeaponAudio, ShellEjector, Meta's
            // Grabbable/interactables, MetaXRAudioSource…): a type list cannot wait to be updated when a
            // prefab gains a component — a forgotten one is a working remote weapon on site.
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
            {
                if (behaviours[i] != null)
                {
                    DestroyImmediate(behaviours[i]);
                }
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = colliders.Length - 1; i >= 0; i--)
            {
                if (colliders[i] != null)
                {
                    DestroyImmediate(colliders[i]);
                }
            }

            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = bodies.Length - 1; i >= 0; i--)
            {
                if (bodies[i] != null)
                {
                    DestroyImmediate(bodies[i]);
                }
            }

            // AudioSource is NOT a MonoBehaviour: with playOnAwake it can play by itself.
            AudioSource[] audioSources = instance.GetComponentsInChildren<AudioSource>(true);
            for (int i = audioSources.Length - 1; i >= 0; i--)
            {
                if (audioSources[i] != null)
                {
                    DestroyImmediate(audioSources[i]);
                }
            }

            // ⚠️ The grip authoring hand models are hidden here too: the instance is built on the
            // inactive staging root, so Weapon.Awake and its guard never run and a SECOND hand would be
            // drawn in the remote player's hand.
            ItemHandRig.HideAll(instance.transform);

            // Particle systems (flash/smoke/casings) are KEPT — their trigger is gone, so only
            // playOnAwake is disabled; RemoteShotFx uses the drawn item's muzzle.
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                {
                    ParticleSystem.MainModule main = particles[i].main;
                    main.playOnAwake = false;
                }
            }
        }

        private void EnsureItemRoots()
        {
            if (_itemsRoot == null)
            {
                var root = new GameObject("HeldItems");
                root.transform.SetParent(transform, false);
                _itemsRoot = root.transform;
                RefreshHeldItemVisibility();
            }

            if (_itemStagingRoot == null)
            {
                var staging = new GameObject("HeldItemStaging");
                staging.transform.SetParent(transform, false);
                staging.SetActive(false); // stays inactive: nothing built here sees Awake
                _itemStagingRoot = staging.transform;
            }
        }

        /// <summary>
        /// Drives items from the relevant hand's palm pose (the pose is not on the wire, §6.6 — the
        /// canonical grip lives in every client's APK).
        /// <para>⚠️ <b>The source is the CONTROLLER, not the drawn wrist</b> (§6.6): the weapon is the
        /// authority, the hand follows. Using the wrist loses left/right symmetry (two retargeted wrists
        /// are not symmetric to their controllers while <c>primaryGrip</c> is one value) and drifts the
        /// weapon from where the player aims (the shot ray leaves from the raw pose). Fitting the hand to
        /// the weapon is <c>RemoteHandPoser</c>'s job.</para>
        /// <para>With <c>GRIP_LINKED</c> there is ONE instance on the two-handed solve: the primary hand
        /// carries the item and the other hand's palm POSITION pulls its ORIENTATION (that hand's rotation
        /// is never read). The solver is the same one the local side uses; <c>aimBlend</c> is a constant
        /// <c>1</c> because smoothing comes from the wire's interpolation.</para>
        /// <para>⚠️ The item is never made a CHILD of the hand bone; its world pose is written. As a child
        /// the bone's scale and intermediate transforms would leak into the grip offset (in metres) and
        /// race the recoil curve.</para></summary>
        private void ApplyItemPoses(in Pose handLWorld, in Pose handRWorld)
        {
            Pose palmL = HandGripPivot.Resolve(handLWorld, false);
            Pose palmR = HandGripPivot.Resolve(handRWorld, true);

            if (_shownGripLinked)
            {
                Transform item = _shownPrimaryRight ? _itemInstanceR : _itemInstanceL;
                ItemDefinition definition = _shownPrimaryRight ? _itemDefR : _itemDefL;
                if (item == null || definition == null)
                {
                    return;
                }

                ApplyGrip(item, _shownPrimaryRight ? palmR : palmL, definition, _shownPrimaryRight,
                    true, (_shownPrimaryRight ? palmL : palmR).position);
                return;
            }

            if (_itemInstanceL != null && _itemDefL != null)
            {
                ApplyGrip(_itemInstanceL, palmL, _itemDefL, false, false, Vector3.zero);
            }

            if (_itemInstanceR != null && _itemDefR != null)
            {
                ApplyGrip(_itemInstanceR, palmR, _itemDefR, true, false, Vector3.zero);
            }
        }

        /// <summary>This hand's finger pose (§6.9): the SLOT's authored finger rig when holding an
        /// item, otherwise idle.
        /// <para>⚠️ <b>The pose is NOT sent on the wire</b> (§6.6), so it is derived from the grip
        /// record itself — the SAME rig the local synthetic hand applies; a second description would
        /// hold the same weapon differently on each screen. The empty hand keeps its separate path
        /// (a tunable rest pose, not a grip).</para>
        /// <para>⚠️ The remote hand is HUMANOID (Mixamo), so it takes CURL RATIOS, not the authored
        /// joint quaternions: raw rotations from another skeleton are never written onto humanoid
        /// bones (<c>Docs/Sistem-Ozeti.md</c> §7). The bridge is
        /// <c>ItemDefinition.GripFingerCurl</c>, which measures the ratios off the very same rig.</para>
        /// <para>Under <c>GRIP_LINKED</c> the two hands differ: the primary takes its own slot
        /// (trigger), the foregrip hand takes <see cref="GripSocketKind.Secondary"/> (wrap).</para></summary>
        public HandPoseProfile ResolveHandPose(bool rightHand)
        {
            ItemDefinition definition;
            GripSocketKind kind;

            if (_shownGripLinked)
            {
                definition = _shownPrimaryRight ? _itemDefR : _itemDefL;
                kind = rightHand == _shownPrimaryRight
                    ? GripSocketKind.Primary
                    : GripSocketKind.Secondary;
            }
            else
            {
                definition = rightHand ? _itemDefR : _itemDefL;
                kind = GripSocketKind.Primary;
            }

            if (definition != null)
            {
                return definition.GripFingerCurl(kind, rightHand);
            }

            return idleHandPose.IsEmpty ? HandPoseProfile.Idle : idleHandPose;
        }

        /// <summary>
        /// This hand's <b>palm</b> target: the grip point of the held item, or <c>false</c> when empty.
        /// Read by <see cref="RemoteHandPoser"/>, which fits the remote wrist to the item.
        /// <para>⚠️ <b>The item is the authority, the hand follows.</b> The item's place comes from the
        /// primary hand's PHYSICAL pose and must stay that way (the shot ray leaves from it); the reverse
        /// direction breaks left/right symmetry and drifts the weapon from where the player aims.</para>
        /// <para>⚠️ Both grip records live in the SAME space (the controller anchor's local pose relative
        /// to the item), so both targets are found forward as <c>item.position + item.rotation * point</c>
        /// — a second space would only produce sign errors. Same composition as
        /// <see cref="ApplySecondaryGripSnap"/> and <c>Weapon.SecondaryGripWorld</c>.</para>
        /// <para>⚠️ The returned pose is in the ANCHOR frame, like the record (no conversion):
        /// <see cref="RemoteHandPoser"/> measures its correction for anchor space. ⚠️ The point is read
        /// <b>per hand</b> — the grip is not symmetric. ⚠️ Manual composition, NOT
        /// <c>TransformPoint</c>: grip offsets are in METRES and must not scale with the item.</para>
        /// </summary>
        public bool TryResolveGripPalm(bool rightHand, out Pose palm)
        {
            palm = default;

            Transform item;
            ItemDefinition definition;
            bool isPrimaryHand;

            if (_shownGripLinked)
            {
                item = _shownPrimaryRight ? _itemInstanceR : _itemInstanceL;
                definition = _shownPrimaryRight ? _itemDefR : _itemDefL;
                isPrimaryHand = rightHand == _shownPrimaryRight;
            }
            else
            {
                item = rightHand ? _itemInstanceR : _itemInstanceL;
                definition = rightHand ? _itemDefR : _itemDefL;
                isPrimaryHand = true;
            }

            if (item == null || definition == null)
            {
                return false;
            }

            Quaternion itemRotation = item.rotation;

            if (isPrimaryHand)
            {
                // ⚠️ The reverse direction MUST read the SAME record ApplyGrip solves the item from —
                // another measure splits hand and weapon by centimetres ("the hand floats next to the
                // weapon"). In this branch the primary hand IS rightHand. The record is anchor-space and
                // carries no rotation (the weapon is always aligned with the controller).
                palm = new Pose(
                    item.position + itemRotation * definition.PrimaryGripPointOnItem(rightHand),
                    itemRotation);
                return true;
            }

            // Foregrip hand: the item ALREADY aims at the second hand (ItemGripSolver), so only that
            // hand's anchor is brought onto the socket.
            // ⚠️ The record is read from the ASKED hand (this branch is only entered while !isPrimaryHand).
            // ⚠️ An unwritten foregrip falls at the item's root — the hand is not pulled there, it stays
            // at its wire pose.
            if (!definition.HasSecondaryGrip)
            {
                return false;
            }

            // Record and RemoteHandPoser are both in the ANCHOR frame: direct composition.
            palm = new Pose(
                item.position + itemRotation * definition.SecondaryGripPosition(rightHand),
                itemRotation);
            return true;
        }

        /// <summary>The ONLY implementation of the grip maths is <see cref="ItemGripSolver"/>; this just
        /// writes the result to the transform.
        /// <para>⚠️ The primary grip uses <b>the same formula as the local side</b> — two measures would
        /// place the same weapon differently on each screen. The record is anchor-space, so no delta or
        /// constant is read and a rig-less observer (admin) gets an identical result.</para>
        /// <para><paramref name="primaryRight"/>: whether the PRIMARY hand is the right one. The grip is
        /// recorded per hand, so this is mandatory. ⚠️ There is NO "which hand" parameter for the
        /// secondary: the wrapping hand is by definition the opposite of the primary, and all that is
        /// needed from it is the palm POSITION.</para></summary>
        private static void ApplyGrip(Transform item, in Pose palm, ItemDefinition definition,
            bool primaryRight, bool hasSecondary, in Vector3 secondaryPalmPosition)
        {
            ItemGripSolver.Solve(definition, primaryRight, !primaryRight, palm, hasSecondary,
                secondaryPalmPosition, 1f, out Vector3 position, out Quaternion rotation);

            item.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// §6.6 "empty hand in a two-handed hold": the non-primary hand's display pose is pulled to the
        /// item's <c>secondaryGrip</c> point, but only within <see cref="SecondaryGripSnapRadius"/>
        /// (packet-loss safety).
        /// <para>⚠️ A final touch-up only: the weapon already aims at the second hand, this just seats the
        /// empty hand's VISUAL on the socket. Removed, the weapon is still correct and the hand floats a
        /// few centimetres beside it.</para>
        /// <para>⚠️ The target is found forward, not by inverse composition (a sign error here silently
        /// produces a wrong pose), and the result is already in the ANCHOR frame. ⚠️ The record read is
        /// the wrapping hand's own — the grip is not symmetric, so the primary hand's record would seat
        /// the empty hand on the far side of the weapon.</para></summary>
        private void ApplySecondaryGripSnap(ref Pose displayHandL, ref Pose displayHandR)
        {
            if (!_shownGripLinked)
            {
                return;
            }

            Transform item = _shownPrimaryRight ? _itemInstanceR : _itemInstanceL;
            ItemDefinition definition = _shownPrimaryRight ? _itemDefR : _itemDefL;
            // An unwritten foregrip falls at the item's root — the empty hand is not snapped there.
            if (item == null || definition == null || !definition.HasSecondaryGrip)
            {
                return;
            }

            // ⚠️ Manual composition instead of TransformPoint: the grip offset is in METRES and must not
            // scale.
            bool secondaryRight = !_shownPrimaryRight;

            // Record and display poses are both anchor-space (so is the radius comparison).
            Quaternion itemRotation = item.rotation;
            var anchorPose = new Pose(
                item.position + itemRotation * definition.SecondaryGripPosition(secondaryRight),
                itemRotation);

            if (_shownPrimaryRight)
            {
                if (IsWithinSnapRadius(displayHandL.position, anchorPose.position))
                {
                    displayHandL = anchorPose;
                }
            }
            else if (IsWithinSnapRadius(displayHandR.position, anchorPose.position))
            {
                displayHandR = anchorPose;
            }
        }

        private static bool IsWithinSnapRadius(in Vector3 actual, in Vector3 target)
        {
            return (actual - target).sqrMagnitude <= SecondaryGripSnapRadius * SecondaryGripSnapRadius;
        }

        /// <summary>§6.6: root transform of the item instance DRAWN in that hand; null when empty.
        /// RemoteShotFx finds the muzzle through this, not by a scene search. Under GRIP_LINKED the single
        /// instance is returned for either hand.</summary>
        public Transform GetHeldItemVisual(bool rightHand)
        {
            // ⚠️ The reference IS returned for a hidden/dead avatar too (visibility is the caller's
            // decision): null would read as "no muzzle" and drop into a pointless fallback.
            return ResolveSlotIsRight(rightHand) ? _itemInstanceR : _itemInstanceL;
        }

        /// <summary>Maps an event's "which hand" to the DRAWN slot (under <c>GRIP_LINKED</c> the primary
        /// hand's). ⚠️ The muzzle and the recoil share this one helper — written separately they could
        /// pick different instances and the flash would come from one weapon and the shake from the
        /// other.</summary>
        private bool ResolveSlotIsRight(bool rightHand)
        {
            return _shownGripLinked ? _shownPrimaryRight : rightHand;
        }

        /// <summary>§6.4/6.5: kicks this avatar's weapon into recoil on an incoming shot event — exactly
        /// the local <c>Weapon</c> curve, without a byte on the wire.
        /// <para>⚠️ The driver lives here, not in a component on the instance:
        /// <see cref="SterilizeVisual"/> strips ALL MonoBehaviours wholesale, so such a component would
        /// vanish on the next build. The two-handed multiplier is
        /// <see cref="Weapon.DefaultTwoHandRecoilMultiplier"/>, since only <c>FLAG_GRIP_LINKED</c> reaches
        /// us.</para></summary>
        public void ApplyShotRecoil(bool rightHand, WeaponDefinition definition)
        {
            if (definition == null)
            {
                return; // non-weapon item (grenade etc.) or an id the catalogue cannot resolve
            }

            if (ResolveSlotIsRight(rightHand))
            {
                AddKick(ref _recoilR, definition);
            }
            else
            {
                AddKick(ref _recoilL, definition);
            }
        }

        /// <summary>Same accumulate + ceiling rule as local <c>Weapon.Fire</c>.</summary>
        private void AddKick(ref RecoilSlot recoil, WeaponDefinition definition)
        {
            if (recoil.Pivot == null)
            {
                return; // no item drawn in that hand, or the prefab has no Model child
            }

            float scale = _shownGripLinked ? Weapon.DefaultTwoHandRecoilMultiplier : 1f;

            recoil.Kick = Mathf.Min(recoil.Kick + definition.KickDegrees * scale, definition.KickDegrees * 4f);
            recoil.KickBack = Mathf.Min(recoil.KickBack + definition.KickBackMeters * scale, definition.KickBackMeters * 3f);
            recoil.RecoverSpeed = definition.RecoilRecoverSpeed;
            recoil.Settling = true;
        }

        /// <summary>Damps the recoil and applies it to the pivot — same as local <c>Weapon.Update</c>.
        /// Nothing is written for an idle weapon: the flag covers the FINAL frame that reaches zero, so the
        /// pivot lands exactly on its base.</summary>
        private static void TickRecoil(ref RecoilSlot recoil)
        {
            if (!recoil.Settling || recoil.Pivot == null)
            {
                return;
            }

            recoil.Kick = Mathf.MoveTowards(recoil.Kick, 0f, recoil.RecoverSpeed * Time.deltaTime);
            recoil.KickBack = Mathf.MoveTowards(recoil.KickBack, 0f, recoil.RecoverSpeed * 0.02f * Time.deltaTime);

            Quaternion rotation = recoil.BaseRotation * Quaternion.Euler(-recoil.Kick, 0f, 0f);
            recoil.Pivot.localRotation = rotation;
            recoil.Pivot.localPosition = recoil.BasePosition + rotation * (Vector3.back * recoil.KickBack);

            recoil.Settling = recoil.Kick > 0f || recoil.KickBack > 0f;
        }

        /// <summary>Items become invisible on a hidden/dead avatar — instances are NOT destroyed, only the
        /// container is disabled, so a revive does not re-Instantiate an unchanged state.</summary>
        private void RefreshHeldItemVisibility()
        {
            if (_itemsRoot != null)
            {
                _itemsRoot.gameObject.SetActive(_visible && (IsAlive || DrawItemsWhileDead));
            }
        }

        /// <summary>Clears instances and state (when the avatar is handed to another player).</summary>
        private void ClearHeldItems()
        {
            if (_itemInstanceL != null)
            {
                Destroy(_itemInstanceL.gameObject);
                _itemInstanceL = null;
            }

            if (_itemInstanceR != null)
            {
                Destroy(_itemInstanceR.gameObject);
                _itemInstanceR = null;
            }

            // Instances are gone: pivot references must go too.
            _recoilL = default;
            _recoilR = default;

            _itemDefL = null;
            _itemDefR = null;
            _shownItemL = 0;
            _shownItemR = 0;
            _shownGripLinked = false;
            _shownPrimaryRight = false;
            _holdModeMismatchWarned = false;

            // New owner: the warning quota is reset for them as well.
            _modelPivotWarned = false;
        }

        private static void Apply(Transform target, in Pose worldPose)
        {
            if (target != null)
            {
                target.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            }
        }

        /// <summary>Keeps the name label above the head and turned to the camera. The position is written
        /// every frame because the label is not a child of the head bone (it must not tilt with it).</summary>
        private void UpdateLabel(in Pose headWorld)
        {
            if (nameLabel == null)
            {
                return;
            }

            // ⚠️ The opponent gate is asked EVERY FRAME — rationale in ShouldShowNameLabel.
            RefreshLabelVisibility();
            if (!nameLabel.enabled)
            {
                return;
            }

            nameLabel.transform.position = headWorld.position + Vector3.up * NameLabelHeightMeters;

            if (_mainCamera == null)
            {
                _cameraRetryTimer -= Time.deltaTime;
                if (_cameraRetryTimer > 0f)
                {
                    return;
                }

                _cameraRetryTimer = CameraRetryIntervalSeconds;
                _mainCamera = Camera.main;
                if (_mainCamera == null)
                {
                    return;
                }
            }

            Transform label = nameLabel.transform;
            Vector3 direction = label.position - _mainCamera.transform.position;
            if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }

            label.rotation = Quaternion.LookRotation(direction);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
            {
                return;
            }

            _visible = visible;

            if (visualRoot != null)
            {
                // Character avatar: one root is toggled.
                visualRoot.SetActive(visible);
            }
            else if (teamRenderers != null)
            {
                for (int i = 0; i < teamRenderers.Length; i++)
                {
                    if (teamRenderers[i] != null)
                    {
                        teamRenderers[i].enabled = visible;
                    }
                }
            }

            if (redBodyRoot != null)
            {
                // ⚠️ The red body is a SIBLING of visualRoot, which does not disable it — it would hang
                // in mid-air before the first pose.
                redBodyRoot.SetActive(visible);
            }

            RefreshRedBodyDriver();

            RefreshLabelVisibility();

            RefreshColliders();
            RefreshHeldItemVisibility();

            // The ghost decision depends on _visible too, or a returning avatar freezes in its previous
            // state.
            ApplyBodyVisual();
        }
    }
}
