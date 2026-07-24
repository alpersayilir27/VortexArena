using TMPro;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak oyuncu hayaleti prefab sürücüsü: RemotePlayerRegistry'den interpolasyonlu
    /// arena-uzayı pozunu okur, ArenaSpace ile dünyaya çevirip baş/el transformlarına
    /// uygular. İlk poz gelene dek görseller gizli kalır. Ad etiketi kameraya döner;
    /// takım rengi MaterialPropertyBlock ile _BaseColor'a yazılır (URP Lit).
    /// RemotePlayerSpawner tarafından Instantiate + Initialize ile kurulur.
    /// <para>
    /// Faz 3: snapshot'taki alive bayrağı okunur — ölü oyuncu SOLUKLAŞIR (URP Lit opak
    /// materyalde alpha işe yaramadığı için takım rengi karartılır), ad etiketine
    /// " (ölü)" eklenir ve vuruş kutuları kapatılır (ölüye ateş edilemez).
    /// </para>
    /// </summary>
    public class RemoteAvatar : MonoBehaviour
    {
        [Header("Görseller")]
        [SerializeField] private Transform head;
        [Tooltip("Gövde kapsülü; kafanın BodyDropMeters altında, yalnız yaw döner (opsiyonel).")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform handL;
        [SerializeField] private Transform handR;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Renderer[] teamRenderers;

        [Header("Vuruş kutuları")]
        [Tooltip("Kafa + gövde collider'ları; boş bırakılırsa çocuklardan otomatik toplanır.")]
        [SerializeField] private Collider[] hitColliders;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color TeamRedColor = new Color(0.85f, 0.20f, 0.20f);
        private static readonly Color TeamBlueColor = new Color(0.20f, 0.40f, 0.90f);
        private static readonly Color NeutralColor = new Color(0.6f, 0.6f, 0.6f);

        private const float CameraRetryIntervalSeconds = 1f;

        /// <summary>Gövde merkezinin kafa merkezinden aşağı ofseti (metre).</summary>
        private const float BodyDropMeters = 0.55f;

        /// <summary>Ölü avatarın renk çarpanı (opak materyalde alpha yerine karartma).</summary>
        private const float DeadColorScale = 0.35f;

        private const string DeadLabelSuffix = " (ölü)";

        /// <summary>Bu avatarın temsil ettiği uzak oyuncunun id'si.</summary>
        public int PlayerId { get; private set; }

        /// <summary>Son snapshot'taki canlılık bayrağı (kayıt yoksa true).</summary>
        public bool IsAlive { get; private set; } = true;

        // GC üretmemek için alan olarak tutulur (SetInfo her lobby_state'te çağrılabilir).
        private MaterialPropertyBlock _propertyBlock;

        // Camera.main her karede aranmaz — önbellek, null ise 1 sn'de bir yenilenir.
        private Camera _mainCamera;
        private float _cameraRetryTimer;

        private bool _visible = true;

        // Ad/renk SetInfo'da saklanır; ölüm görünümü bunların üstüne uygulanır.
        private string _displayName = "";
        private Color _teamColor = NeutralColor;

        private void Awake()
        {
            // Prefabda liste bağlanmadıysa çocuk collider'ları vuruş kutusu sayılır.
            if (hitColliders == null || hitColliders.Length == 0)
            {
                hitColliders = GetComponentsInChildren<Collider>(true);
            }
        }

        /// <summary>Spawner kurar; poz okumaları bu id ile yapılır.</summary>
        public void Initialize(int playerId)
        {
            PlayerId = playerId;
        }

        /// <summary>Ad etiketini ve takım rengini günceller ("red"/"blue"/diğer=gri).</summary>
        public void SetInfo(string displayName, string team)
        {
            _displayName = displayName ?? "";
            _teamColor = team == "red" ? TeamRedColor : team == "blue" ? TeamBlueColor : NeutralColor;

            ApplyLabelText();
            ApplyTeamColor();
        }

        /// <summary>Ad etiketi; ölüyken " (ölü)" eki taşır.</summary>
        private void ApplyLabelText()
        {
            if (nameLabel != null)
            {
                nameLabel.text = IsAlive ? _displayName : _displayName + DeadLabelSuffix;
            }
        }

        /// <summary>Takım rengini MaterialPropertyBlock ile yazar; ölüyken karartır.</summary>
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

            Color color = IsAlive
                ? _teamColor
                : new Color(_teamColor.r * DeadColorScale, _teamColor.g * DeadColorScale, _teamColor.b * DeadColorScale, _teamColor.a);

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

        /// <summary>Ölü/görünmez avatara ateş edilemez: vuruş kutuları kapatılır.</summary>
        private void RefreshColliders()
        {
            if (hitColliders == null)
            {
                return;
            }

            bool enable = _visible && IsAlive;
            for (int i = 0; i < hitColliders.Length; i++)
            {
                if (hitColliders[i] != null)
                {
                    hitColliders[i].enabled = enable;
                }
            }
        }

        private void LateUpdate()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null ||
                !registry.GetInterpolatedPose(PlayerId, out Pose headPose, out Pose handLPose, out Pose handRPose))
            {
                SetVisible(false); // ilk poz gelene dek gizli
                return;
            }

            SetVisible(true);
            UpdateAlive(registry);

            // Pozlar arena uzayında — sahnedeki origin'e göre dünyaya çevir.
            Pose headWorld = ArenaSpace.ArenaToWorld(headPose);
            Apply(head, headWorld);
            Apply(handL, ArenaSpace.ArenaToWorld(handLPose));
            Apply(handR, ArenaSpace.ArenaToWorld(handRPose));
            ApplyBody(headWorld);

            UpdateLabelBillboard();
        }

        /// <summary>
        /// Gövde kapsülü kafanın altında durur ve yalnız YAW döner (öne eğilmez);
        /// böylece oyuncu eğildiğinde gövde yatmaz, vuruş kutusu makul kalır.
        /// </summary>
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

        /// <summary>Snapshot alive bayrağını okur; değiştiyse görünüm + collider'ları tazeler.</summary>
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
            RefreshColliders();
        }

        private static void Apply(Transform target, in Pose worldPose)
        {
            if (target != null)
            {
                target.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            }
        }

        /// <summary>Ad etiketini kameraya döndürür (etiket ters bakmasın diye etiket→kamera tersi).</summary>
        private void UpdateLabelBillboard()
        {
            if (nameLabel == null)
            {
                return;
            }

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

            if (teamRenderers != null)
            {
                for (int i = 0; i < teamRenderers.Length; i++)
                {
                    if (teamRenderers[i] != null)
                    {
                        teamRenderers[i].enabled = visible;
                    }
                }
            }

            if (nameLabel != null)
            {
                nameLabel.enabled = visible;
            }

            RefreshColliders();
        }
    }
}
