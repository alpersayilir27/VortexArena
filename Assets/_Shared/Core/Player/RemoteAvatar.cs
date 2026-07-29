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
    /// Snapshot'taki alive bayrağı okunur — ölü oyuncu SOLUKLAŞIR (URP Lit opak
    /// materyalde alpha işe yaramadığı için takım rengi karartılır), ad etiketine
    /// " (ölü)" eklenir ve vuruş kutuları kapatılır (ölüye ateş edilemez).
    /// </para>
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

        [Header("Karakter")]
        [Tooltip("Bağlıysa gövde üç noktalı IK ile çözülür; boşsa eski kafa/el/kapsül yolu kullanılır.")]
        [SerializeField] private ThreePointBodyIK bodyIK;

        [Tooltip("YEREL oyuncuyla aynı takımdayken görünen dost göstergesi (kafanın üstündeki küp).")]
        [SerializeField] private GameObject friendMarker;

        [Tooltip("İlk poz gelene dek gizlenecek görsel kök. Boşsa teamRenderers listesi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

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

        /// <summary>Dost göstergesinin kafa merkezinin üstündeki yüksekliği (metre).</summary>
        private const float FriendMarkerHeightMeters = 0.32f;

        /// <summary>Ad etiketinin kafa merkezinin üstündeki yüksekliği (metre) — göstergenin üstünde.</summary>
        private const float NameLabelHeightMeters = 0.5f;

        private const string DeadLabelSuffix = " (ölü)";
        private const string UncalibratedLabelSuffix = " (KALİBRESİZ)";

        /// <summary>Kalibresiz avatarın nabız hızı (saniyedeki tam gidiş-dönüş).</summary>
        private const float UncalibratedPulseHz = 1.6f;

        /// <summary>Nabzın takım rengiyle beyaz arasındaki en yüksek karışım oranı.</summary>
        private const float UncalibratedPulseAmount = 0.85f;

        /// <summary>Bu avatarın temsil ettiği uzak oyuncunun id'si.</summary>
        public int PlayerId { get; private set; }

        /// <summary>Son snapshot'taki canlılık bayrağı (kayıt yoksa true).</summary>
        public bool IsAlive { get; private set; } = true;

        /// <summary>Sunucuya göre bu oyuncunun hizalaması geçerli mi (§10.6; roster'dan gelir).</summary>
        public bool IsCalibrated { get; private set; } = true;

        // GC üretmemek için alan olarak tutulur (SetInfo her lobby_state'te çağrılabilir).
        private MaterialPropertyBlock _propertyBlock;

        // Camera.main her karede aranmaz — önbellek, null ise 1 sn'de bir yenilenir.
        private Camera _mainCamera;
        private float _cameraRetryTimer;

        private bool _visible = true;

        /// <summary>Bu uzak oyuncu YEREL oyuncuyla aynı takımda mı (dost göstergesini sürer).</summary>
        private bool _isFriendly;

        // Ad/numara/renk SetInfo'da saklanır; ölüm görünümü bunların üstüne uygulanır.
        private string _displayName = "";

        /// <summary>Forma numarası (§2); 0 = atanmamış → etikette basılmaz.</summary>
        private int _number;

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

        /// <summary>Ad etiketini, forma numarasını ve takım rengini günceller ("red"/"blue"/diğer=gri).
        /// <para><paramref name="number"/> 0 ise numara BASILMAZ (atanmamış ya da admin): adlar
        /// benzersiz olmadığı için ayırt edici alan numaradır, uydurma bir sayı göstermek onu
        /// güvenilmez kılardı (§2).</para></summary>
        public void SetInfo(string displayName, int number, string team)
        {
            _displayName = displayName ?? "";
            _number = number;
            _teamColor = team == "red" ? TeamRedColor : team == "blue" ? TeamBlueColor : NeutralColor;

            ApplyLabelText();
            ApplyTeamColor();
        }

        /// <summary>
        /// Kalibrasyon durumunu uygular (§10.6; <c>lobby_state</c>'ten gelir). Kalibresiz avatar
        /// <b>parlar</b>, etiketine " (KALİBRESİZ)" eklenir ve <b>vuruş kutuları kapanır</b> —
        /// sunucu zaten hasarı reddediyor, istemcide de kapatmak atıcının "vurdum ama olmadı"
        /// hissini yaşamasını engeller.
        /// </summary>
        public void SetCalibrated(bool calibrated)
        {
            if (IsCalibrated == calibrated)
            {
                return;
            }

            IsCalibrated = calibrated;
            ApplyLabelText();
            ApplyTeamColor();
            RefreshColliders();
        }

        /// <summary>
        /// Bu uzak oyuncu YEREL oyuncuyla aynı takımda mı — kafanın üstündeki dost göstergesi
        /// buna göre açılır.
        /// <para>
        /// Takım rengi karakter mesh'ine UYGULANMAZ: herkes aynı modeli kullanıyor ve ayrımı
        /// yapan tek şey bu göstergedir. Düşmanda hiçbir işaret olmaması bilinçlidir — düşmanı
        /// da işaretlemek arenada duvar arkasından okunabilen bir avantaj üretirdi.
        /// </para>
        /// </summary>
        public void SetFriendly(bool friendly)
        {
            if (_isFriendly == friendly)
            {
                return;
            }

            _isFriendly = friendly;
            RefreshFriendMarker();
        }

        /// <summary>Göstergeyi kafanın üstünde tutar — kafa KEMİĞİNE bağlanmaz, çünkü IK her
        /// karede kemiği yeniden yerleştiriyor ve gösterge onunla birlikte eğilmemeli.</summary>
        private void UpdateFriendMarker(in Pose headWorld)
        {
            if (friendMarker == null || !friendMarker.activeSelf)
            {
                return;
            }

            friendMarker.transform.position = headWorld.position + Vector3.up * FriendMarkerHeightMeters;
        }

        private void RefreshFriendMarker()
        {
            if (friendMarker != null)
            {
                friendMarker.SetActive(_visible && _isFriendly);
            }
        }

        /// <summary>Ad etiketi; ölüyken " (ölü)", kalibresizken " (KALİBRESİZ)" eki taşır.</summary>
        private void ApplyLabelText()
        {
            if (nameLabel != null)
            {
                string suffix = !IsCalibrated ? UncalibratedLabelSuffix : IsAlive ? "" : DeadLabelSuffix;
                string prefix = _number > 0 ? _number + " · " : "";
                nameLabel.text = prefix + _displayName + suffix;
            }
        }

        /// <summary>
        /// Takım rengini MaterialPropertyBlock ile yazar; ölüyken karartır, kalibresizken parlatır.
        /// <para>
        /// ⚠️ Parlama <c>_BaseColor</c> nabzıyla yapılır, emission ile DEĞİL:
        /// <see cref="MaterialPropertyBlock"/> shader keyword'ü açamaz, bu yüzden paylaşılan
        /// materyalde <c>_EMISSION</c> önceden açık olmadıkça <c>_EmissionColor</c> yazmak sessizce
        /// hiçbir şey yapmazdı. İkinci bir materyal örneği yaratmak da Quest'te SRP batch'ini bozar.
        /// </para>
        /// </summary>
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
                // Kalibresiz durum ölümü EZER: operatörün ve diğer oyuncuların görmesi gereken
                // şey "bu adamın hizalaması bozuk", ölü olup olmadığı değil.
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

        /// <summary>Ölü/görünmez/kalibresiz avatara ateş edilemez: vuruş kutuları kapatılır.</summary>
        private void RefreshColliders()
        {
            if (hitColliders == null)
            {
                return;
            }

            bool enable = _visible && IsAlive && IsCalibrated;
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

            // Nabız yalnız KALİBRESİZKEN sürülür — kalibreli avatarda her kare MPB yazmak
            // boşuna iş olurdu (renk değişmiyor, durum olay tabanlı tazeleniyor).
            if (!IsCalibrated)
            {
                ApplyTeamColor();
            }

            // Pozlar arena uzayında — sahnedeki origin'e göre dünyaya çevir.
            Pose headWorld = ArenaSpace.ArenaToWorld(headPose);
            Pose handLWorld = ArenaSpace.ArenaToWorld(handLPose);
            Pose handRWorld = ArenaSpace.ArenaToWorld(handRPose);

            if (bodyIK != null)
            {
                // Karakterli avatar: gövde, kollar ve bacaklar bu üç noktadan türetilir.
                bodyIK.Solve(headWorld, handLWorld, handRWorld);
            }
            else
            {
                // Eski kapsül avatarı — prefabda IK bağlı değilse davranış değişmesin.
                Apply(head, headWorld);
                Apply(handL, handLWorld);
                Apply(handR, handRWorld);
                ApplyBody(headWorld);
            }

            UpdateFriendMarker(headWorld);
            UpdateLabel(headWorld);
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

        /// <summary>
        /// Ad etiketini kafanın üstünde tutar ve kameraya döndürür (etiket ters bakmasın diye
        /// etiket→kamera tersi).
        /// <para>Konum her karede YAZILIR: karakterli avatarda etiket bir kafa objesinin çocuğu
        /// değildir (kafa artık IK'nın sürdüğü bir kemiktir ve etiket onunla eğilmemeli).</para>
        /// </summary>
        private void UpdateLabel(in Pose headWorld)
        {
            if (nameLabel == null)
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
                // Karakterli avatar: tek kök kapatılır (mesh listesi tutmak gerekmez).
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

            if (nameLabel != null)
            {
                nameLabel.enabled = visible;
            }

            RefreshFriendMarker();
            RefreshColliders();
        }
    }
}
