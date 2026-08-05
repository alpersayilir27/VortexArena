using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// YEREL oyuncunun gövdesinin bir <b>iç engelin içinde</b> olup olmadığını ölçer
    /// (Docs/ArenaNet-Protokol.md §10.9). Üç kuraldan biri yeterlidir:
    /// gövdenin <b>%30'u</b> içeride · <b>kafa tamamen</b> içeride · <b>silah tamamen</b> içeride.
    ///
    /// <para><b>Ölçer, cezalandırmaz.</b> Sonucu <see cref="IsViolating"/>'ten okunur;
    /// <c>PlayerPoseTracker</c> onu poz paketine bir bit olarak koyar (§6.2) ve canı
    /// <b>sunucu</b> eritir. Bu sınıf can/skor/ölüm bilmez.</para>
    ///
    /// <para><b>Neden collider değil nokta örneklemesi:</b> yerel gövdeye collider KONAMAZ —
    /// <see cref="Weapon"/>'ın atış ışını maskesizdir ve proje ayarı trigger'ları da vuruyor, yani
    /// oyuncu kendi atışını kendi gövdesine yerdi (<see cref="LocalBodyAvatar"/> sınıf notu).
    /// Üstelik trigger "değdi mi" der, <b>"ne kadarı içeride"</b> demez — %30 kuralı temas
    /// sayısıyla ölçülemez. Nokta örneklemesi üç kuralı da tek mekanizmayla çözer.</para>
    ///
    /// <para>⚠️ <b>Engel collider'ı KONVEKS olmak zorundadır</b> (Box/Sphere/Capsule ya da
    /// <c>MeshCollider</c> + <c>Convex</c>). Sebep hayati: <see cref="Collider.ClosestPoint"/>
    /// non-convex bir <c>MeshCollider</c>'da <b>girdi noktasını AYNEN döndürür</b> → her nokta
    /// "içeride" okunur → o sahnedeki <b>herkes anında ölmeye başlar</b>. Bu yüzden uygun olmayan
    /// collider burada kalıcı olarak <b>yok sayılır</b> (açık başarısızlık: bir hata satırı + hiç
    /// ceza yok) ve editör tarafında ayrıca taranır.</para>
    ///
    /// <para><b>Neden kendini önyükleyen tekil</b> (<c>WeaponGranter</c>/<c>PlayerCombatState</c>
    /// deseni): sahneye ya da prefaba elle konsaydı her yeni arena bir kurulum adımı doğururdu ve
    /// unutulan bir arena sessizce cezasız kalırdı.</para>
    /// </summary>
    /// <remarks>
    /// ⚠️ Execution order <see cref="LocalBodyAvatar"/>'dan (30000) <b>sonra</b> olmak zorunda:
    /// kemikler retarget döngüsünde yazılıyor, erken okuyan kare bir kare bayat poz ölçer.
    /// </remarks>
    [DefaultExecutionOrder(30100)]
    public class BodyViolationProbe : MonoBehaviour
    {
        // ------------------------------------------------------------------ ayarlar

        /// <summary>Ölçüm kadansı — poz gönderimiyle (20 Hz) aynı: bayrak paket çıkarken taze olsun.</summary>
        private const float SampleInterval = 1f / 20f;

        /// <summary>İhlal eşiği: gövdenin bu oranı içerideyse ceza başlar.</summary>
        private const float EnterRatio = 0.30f;

        /// <summary>
        /// Çıkış eşiği (histerezis). Tek eşik kullanılsaydı sınırda duran oyuncu saniyede onlarca
        /// kez girip çıkardı: admin halkası titrer, karartma çırpınır, tel bayrağı zıplardı.
        /// </summary>
        private const float ExitRatio = 0.24f;

        /// <summary>Hafif uyarı bandının başlangıcı — buradan itibaren ekran hafifçe kızarır.</summary>
        private const float WarnRatio = 0.15f;

        /// <summary>
        /// İhlalin sayılması için kesintisiz geçmesi gereken süre (sn). Body tracking sıçraması
        /// (bir karelik kol ışınlanması) ceza başlatmasın diye.
        /// </summary>
        private const float MinViolationSeconds = 0.15f;

        /// <summary>Kafa küresinin yarıçapı (m) — "kafa tamamen içeride" bu kabuktan ölçülür.</summary>
        private const float HeadRadius = 0.11f;

        /// <summary>Göz hizasından kafa merkezine geri ofset (m): HMD gözlerin önündedir.</summary>
        private const float HeadCenterBackOffset = 0.06f;

        /// <summary>Geniş faz küresinin kafadan aşağı kaydırılması (m) — gövde merkezi.</summary>
        private const float BroadPhaseDrop = 0.8f;

        /// <summary>Geniş faz yarıçapı (m): kafa, ayaklar ve açık kollar bu kürenin içinde kalır.</summary>
        private const float BroadPhaseRadius = 1.4f;

        /// <summary>Aday engel tamponu. Aşılırsa fazlası yok sayılır — bir oyuncunun etrafında
        /// aynı anda sekizden çok engel olması sahne kurulumu hatasıdır.</summary>
        private const int MaxCandidates = 8;

        /// <summary>İhlalde karartma tavanı. ⚠️ <b>1.0 DEĞİL ve olmamalı</b> — gerekçe
        /// <see cref="ReportPresentation"/>'da.</summary>
        private const float ViolationFadeAlpha = 0.75f;

        /// <summary>Uyarı bandının karartma tavanı (hafif kararma).</summary>
        private const float WarnFadeAlpha = 0.25f;

        /// <summary>Karartmanın rengi — kırmızı, "geri çekil" demek için.</summary>
        private static readonly Color FadeColor = new Color(0.55f, 0.04f, 0.04f);

        /// <summary><see cref="ScreenFade"/> kaynak kimliği.</summary>
        private const string FadeSourceId = "obstacle";

        /// <summary>Titreşim nabzının frekansı (Hz) — sürekli titreşim uyarı olmaktan çıkar.</summary>
        private const float HapticPulseHz = 2f;

        /// <summary>Rig bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>Gövde iskeleti çözülemediğinde iki arama arasındaki en kısa süre (sn).</summary>
        private const float BoneSearchIntervalSeconds = 1f;

        /// <summary>
        /// Silah renderer önbelleğinin tavanı. Verilen silah (<c>random</c> modlarında) her
        /// kavramada yeni bir örnektir, yani anahtarlar birikir — tavana varınca önbellek tümden
        /// boşaltılır (yeniden dolması bir kavrama başına tek çağrı).
        /// </summary>
        private const int MaxWeaponCacheEntries = 16;

        // ------------------------------------------------------------------ durum

        public static BodyViolationProbe Instance { get; private set; }

        /// <summary>
        /// Yerel oyuncu şu an bir iç engelin içinde mi — <b>telin taşıdığı tek bilgi</b>
        /// (<c>gripFlags</c> bit5, §6.3). Okuyucusu <c>PlayerPoseTracker</c>'dır.
        /// </summary>
        public static bool IsViolating { get; private set; }

        /// <summary>Gövdenin içeride kalan ağırlık oranı (0..1); yalnız teşhis/uyarı içindir.</summary>
        public static float InsideRatio { get; private set; }

        /// <summary>
        /// İhlalden ÖNCEKİ uyarı bandının şiddeti (0..1): <see cref="WarnRatio"/>'da 0,
        /// <see cref="EnterRatio"/>'da 1. İhlal başlayınca anlamını yitirir (karartma zaten tavanda).
        /// </summary>
        public static float WarnLevel { get; private set; }

        private float _sampleAccumulator;
        private float _violationHold;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        private Animator _bodyAnimator;
        private float _boneWeightTotal;
        private float _boneSearchTime = float.NegativeInfinity;
        private readonly List<SamplePoint> _bonePoints = new List<SamplePoint>();

        private readonly Collider[] _candidates = new Collider[MaxCandidates];
        private readonly Bounds[] _candidateBounds = new Bounds[MaxCandidates];
        /// <summary>Silah başına renderer listesi (anahtar: <c>GetInstanceID</c>). 20 Hz'de
        /// <c>GetComponentsInChildren</c> çağırmak kare başına dizi ayırmak demektir.</summary>
        private readonly Dictionary<int, Renderer[]> _weaponRenderers = new Dictionary<int, Renderer[]>();

        /// <summary>Konveks olmadığı için elenen collider'lar — uyarı bir kez basılsın diye.</summary>
        private static readonly HashSet<int> RejectedColliders = new HashSet<int>();

        /// <summary>
        /// Bir örnek noktası: <see cref="A"/> tek başınaysa o kemiğin konumu, <see cref="B"/> de
        /// varsa <b>ikisinin ortası</b> (uzuv gövdesi). Orta nokta kemik EKSENİ varsaymaz — Mixamo
        /// rig'lerinde kemik yerel eksenleri güvenilir değildir, iki konumun ortası her rig'de aynı
        /// yeri gösterir.
        /// </summary>
        private struct SamplePoint
        {
            public Transform A;
            public Transform B;
            public float Weight;
        }

        // ------------------------------------------------------------------ önyükleme

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[BodyViolationProbe]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BodyViolationProbe>();
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

        // ------------------------------------------------------------------ döngü

        private void LateUpdate()
        {
            _sampleAccumulator += Time.unscaledDeltaTime;
            if (_sampleAccumulator < SampleInterval)
            {
                // Karartma her KAREDE bildirilir (hakemin kalp atışı sözleşmesi), ölçüm 20 Hz.
                ReportPresentation();
                return;
            }

            float elapsed = _sampleAccumulator;
            _sampleAccumulator = 0f;

            Evaluate(elapsed);
            ReportPresentation();
        }

        /// <summary>
        /// Bir ölçüm turu: geniş faz → dar faz → üç kural → histerezis.
        /// </summary>
        private void Evaluate(float elapsed)
        {
            Transform head = ResolveHead();
            if (head == null)
            {
                // Oyuncu değiliz (admin) ya da rig henüz yok: ölçüm yapılamaz, ihlal de yoktur.
                ClearState();
                return;
            }

            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                ClearState(); // layer tanımsız — ArenaLayers zaten bir kez bağırdı
                return;
            }

            Vector3 headForward = head.forward;
            Vector3 headCenter = head.position - headForward * HeadCenterBackOffset;

            int count = Physics.OverlapSphereNonAlloc(
                headCenter - Vector3.up * BroadPhaseDrop, BroadPhaseRadius,
                _candidates, mask, QueryTriggerInteraction.Ignore);

            count = FilterCandidates(count);
            if (count == 0)
            {
                // ⚠️ YAYGIN DURUM ve erken çıkışın tek sebebi bu: yakında engel yokken kare
                // başına tek physics sorgusu yapılır, nokta testi hiç koşmaz.
                SetViolation(false, 0f, elapsed);
                return;
            }

            bool headInside = EvaluateHead(headCenter, count, out int headPointsInside);
            float bodyInside = EvaluateBody(count);
            bool weaponInside = EvaluateWeapon(count);

            // Kafa hem KENDİ kuralıdır hem orana kısmi katkı verir: yarısı içeride bir kafa
            // "tamamen içeride" değildir ama gövdenin %4'ü kadar içeridedir.
            //
            // ⚠️ Gövde iskeleti çözülemediyse (body tracking hiç başlamadı) ORAN KURALI DEVRE DIŞI
            // kalır — payda yalnız kafadan oluşurdu ve 7 noktanın 3'ü içeride olan bir kafa %43
            // okunup tek başına %30 kuralını tetiklerdi. O hâlde yalnız kafa ve silah kuralları
            // çalışır; ikisi de "tamamen içeride" sorar, yani oransız da doğrudur.
            float ratio = 0f;
            if (_boneWeightTotal > 0f)
            {
                float headWeight = HeadWeight * (headPointsInside / (float)HeadSampleCount);
                ratio = (bodyInside + headWeight) / (_boneWeightTotal + HeadWeight);
            }

            bool rule = ratio >= (IsViolating ? ExitRatio : EnterRatio) || headInside || weaponInside;
            SetViolation(rule, ratio, elapsed);
        }

        /// <summary>
        /// Histerezis + minimum süre. Girişte <see cref="MinViolationSeconds"/> beklenir, çıkışta
        /// beklenmez: cezayı geciktirmek oyuncu lehinedir, cezayı bırakmayı geciktirmek değildir.
        /// </summary>
        private void SetViolation(bool rule, float ratio, float elapsed)
        {
            InsideRatio = ratio;
            WarnLevel = Mathf.Clamp01(Mathf.InverseLerp(WarnRatio, EnterRatio, ratio));

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
            InsideRatio = 0f;
            WarnLevel = 0f;
            _violationHold = 0f;
        }

        // ------------------------------------------------------------------ kurallar

        private const int HeadSampleCount = 7;
        private const float HeadWeight = 8.1f;

        /// <summary>
        /// Kafa küresinin yedi noktası (merkez + ±x/±y/±z). <b>Hepsi</b> içerideyse kafa tamamen
        /// içeridedir.
        /// <para>⚠️ "Merkezin yüzeye uzaklığı ≥ yarıçap" hesabı KULLANILAMAZ:
        /// <see cref="Collider.ClosestPoint"/> içerideki bir nokta için noktanın kendisini döner,
        /// yani <b>içeriden yüzey mesafesi ölçülemez</b>.</para>
        /// <para>Kafa merkezi kemikten değil <b>HMD'den</b> gelir: gözlük oyuncunun gerçek kafasının
        /// yerini kemikten daha iyi bilir ve bu sayede kural body tracking olmadan da çalışır.</para>
        /// </summary>
        private bool EvaluateHead(Vector3 center, int count, out int inside)
        {
            inside = 0;

            if (IsInside(center, count)) inside++;
            if (IsInside(center + Vector3.right * HeadRadius, count)) inside++;
            if (IsInside(center - Vector3.right * HeadRadius, count)) inside++;
            if (IsInside(center + Vector3.up * HeadRadius, count)) inside++;
            if (IsInside(center - Vector3.up * HeadRadius, count)) inside++;
            if (IsInside(center + Vector3.forward * HeadRadius, count)) inside++;
            if (IsInside(center - Vector3.forward * HeadRadius, count)) inside++;

            return inside == HeadSampleCount;
        }

        /// <summary>
        /// Gövde noktalarının içeride kalan <b>ağırlık toplamı</b>. Nokta SAYISI değil ağırlık
        /// sayılır: iki el + bir ayak gövdenin %1.8'idir, göğüs tek başına %22'dir — sayı saymak
        /// kuralı anlamsızlaştırırdı.
        /// </summary>
        private float EvaluateBody(int count)
        {
            EnsureBonePoints();

            float inside = 0f;
            for (int i = 0; i < _bonePoints.Count; i++)
            {
                SamplePoint point = _bonePoints[i];
                if (point.A == null)
                {
                    continue; // gövde yıkıldı (sahne değişimi) — bir sonraki turda yeniden çözülür
                }

                Vector3 position = point.B != null
                    ? (point.A.position + point.B.position) * 0.5f
                    : point.A.position;

                if (IsInside(position, count))
                {
                    inside += point.Weight;
                }
            }

            return inside;
        }

        /// <summary>
        /// Elde tutulan silahın <b>dünya AABB'sinin</b> 8 köşesi + merkezi; hepsi içerideyse silah
        /// tamamen içeridedir.
        /// <para>AABB döndürülmüş bir tüfekte boşluğu da kapsar, yani kural <b>ihtiyatlıdır</b>
        /// (biraz daha derine sokmayı ister). Yalancı pozitif üretmemesi yalancı negatiften
        /// önemlidir — silah yüzünden ölmek en az beklenen ölümdür.</para>
        /// </summary>
        private bool EvaluateWeapon(int count)
        {
            for (int w = 0; w < Weapon.Active.Count; w++)
            {
                Weapon weapon = Weapon.Active[w];
                if (weapon == null || !weapon.IsHeld)
                {
                    continue;
                }

                if (TryGetWeaponBounds(weapon, out Bounds bounds) && AllCornersInside(bounds, count))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AllCornersInside(Bounds bounds, int count)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            if (!IsInside(center, count))
            {
                return false;
            }

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    center.x + ((i & 1) == 0 ? -extents.x : extents.x),
                    center.y + ((i & 2) == 0 ? -extents.y : extents.y),
                    center.z + ((i & 4) == 0 ? -extents.z : extents.z));

                if (!IsInside(corner, count))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Silahın çizilen hacmi. Renderer listesi <b>silah başına bir kez</b> toplanır: 20 Hz'de
        /// <c>GetComponentsInChildren</c> çağırmak kare başına dizi ayırmak demektir.
        /// </summary>
        private bool TryGetWeaponBounds(Weapon weapon, out Bounds bounds)
        {
            bounds = default;

            int key = weapon.GetInstanceID();
            if (!_weaponRenderers.TryGetValue(key, out Renderer[] renderers) ||
                renderers.Length == 0 || renderers[0] == null)
            {
                if (_weaponRenderers.Count >= MaxWeaponCacheEntries)
                {
                    // Yok edilmiş silahların anahtarları birikmesin (gerekçe sabitte).
                    _weaponRenderers.Clear();
                }

                renderers = weapon.GetComponentsInChildren<Renderer>(false);
                _weaponRenderers[key] = renderers;
            }

            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return found;
        }

        // ------------------------------------------------------------------ geometri

        /// <summary>
        /// Aday listesini konvekslik açısından süzer ve sınırlayıcı kutularını <b>tur başına bir
        /// kez</b> okur (<see cref="Collider.bounds"/> her erişimde native'e iner).
        /// </summary>
        private int FilterCandidates(int count)
        {
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                Collider collider = _candidates[i];
                if (collider == null || !IsUsable(collider))
                {
                    continue;
                }

                _candidates[kept] = collider;
                _candidateBounds[kept] = collider.bounds;
                kept++;
            }

            return kept;
        }

        /// <summary>
        /// ⚠️ <b>Non-convex <see cref="MeshCollider"/> KULLANILAMAZ.</b>
        /// <see cref="Collider.ClosestPoint"/> orada girdi noktasını aynen döndürür, yani her nokta
        /// "içeride" okunur ve <b>sahnedeki herkes anında ölmeye başlar</b>. Böyle bir collider
        /// kalıcı olarak yok sayılır ve bir kez rapor edilir — açık başarısızlık, sessiz katliam
        /// değil.
        /// </summary>
        private static bool IsUsable(Collider collider)
        {
            if (collider is not MeshCollider mesh || mesh.convex)
            {
                return true;
            }

            if (RejectedColliders.Add(mesh.GetInstanceID()))
            {
                Debug.LogError(
                    $"[BodyViolationProbe] '{mesh.name}' objesi '{ArenaLayers.ObstacleName}' layer'ında " +
                    "ama collider'ı KONVEKS DEĞİL (MeshCollider + Convex kapalı). Bu obje engel " +
                    "ihlali hesabından ÇIKARILDI — nokta-içeride testi non-convex mesh'te her zaman " +
                    "'içeride' der ve tüm oyuncuları anında öldürürdü. Convex işaretle ya da kaba bir " +
                    "Box/Capsule collider kullan.", mesh);
            }

            return false;
        }

        /// <summary>
        /// Nokta adaylardan <b>herhangi birinin</b> içinde mi (birlik semantiği): iki kutunun ek
        /// yerinde duran kafa aksi hâlde "hiçbirinin tam içinde değil" diye kaçardı.
        /// </summary>
        private bool IsInside(Vector3 point, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!_candidateBounds[i].Contains(point))
                {
                    continue; // ucuz AABB elemesi — noktaların çoğu buradan döner
                }

                Collider collider = _candidates[i];
                if (collider == null)
                {
                    continue;
                }

                // Konveks collider'da içerideki bir nokta için ClosestPoint noktanın KENDİSİDİR.
                if ((collider.ClosestPoint(point) - point).sqrMagnitude <= 1e-8f)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ kaynaklar

        /// <summary>
        /// HMD transformu. ⚠️ <b>Aynı zamanda "oyuncu muyuz" kapısıdır</b>: admin gözlemcide rig
        /// kapalı olduğu için burası kalıcı olarak <c>null</c> döner ve sonda hiç çalışmaz
        /// (<see cref="LocalBodyAvatar"/> ile aynı kapı — Core, <c>AppSession</c>'ı göremez).
        /// <para>Arama kısılır: rig hiç yokken her karede sahne geneli tip araması yapılmasın.</para>
        /// </summary>
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
            return _rig != null ? _rig.centerEyeAnchor : null;
        }

        /// <summary>
        /// Kemik örnek noktalarını bir kez çözer (gövde doğduğunda) ve gövde yıkılırsa yeniden
        /// çözer.
        /// <para>⚠️ Kemikler <see cref="Animator.GetBoneTransform"/> ile alınır — bu bir
        /// <b>transform aramasıdır</b>, poz aktarımı değil; <c>HumanPoseHandler</c> yasağı
        /// (Docs/Sistem-Ozeti.md §7) buraya değmez.</para>
        /// <para>Gövde hiç yoksa (body tracking başlamadı) liste boş kalır: oran kuralı devre dışı
        /// düşer, <b>kafa ve silah kuralları çalışmaya devam eder</b> — ikisi de HMD'den ve silahtan
        /// besleniyor.</para>
        /// </summary>
        private void EnsureBonePoints()
        {
            if (_bodyAnimator != null && _bonePoints.Count > 0 && _bonePoints[0].A != null)
            {
                return;
            }

            // ⚠️ Arama KISILIR: gövde hiç doğmamışsa (body tracking başlamadı, izin verilmedi)
            // bu kapı kalıcı olarak boş döner ve kısılmasaydı 20 Hz'de bir sahne geneli
            // GetComponentsInChildren çağrısı — yani saniyede 20 dizi tahsisi — yapılırdı.
            if (Time.unscaledTime - _boneSearchTime < BoneSearchIntervalSeconds)
            {
                return;
            }

            _boneSearchTime = Time.unscaledTime;
            _bonePoints.Clear();
            _boneWeightTotal = 0f;
            _bodyAnimator = ResolveBodyAnimator();

            if (_bodyAnimator == null)
            {
                return;
            }

            // Antropometrik kütle oranları (iki taraf ayrı satır); toplam kafayla birlikte 100.
            AddBone(HumanBodyBones.Hips, HumanBodyBones.LastBone, 14.2f);
            AddBone(HumanBodyBones.Spine, HumanBodyBones.LastBone, 13.9f);

            // UpperChest her rig'de yoktur; yoksa ağırlığı Chest'e katlanır (oran korunur).
            Transform upperChest = _bodyAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            AddBone(HumanBodyBones.Chest, HumanBodyBones.LastBone, upperChest != null ? 10.8f : 21.6f);
            if (upperChest != null)
            {
                AddBone(HumanBodyBones.UpperChest, HumanBodyBones.LastBone, 10.8f);
            }

            AddBone(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 2.8f);
            AddBone(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 2.8f);
            AddBone(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 1.6f);
            AddBone(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1.6f);
            AddBone(HumanBodyBones.LeftHand, HumanBodyBones.LastBone, 0.6f);
            AddBone(HumanBodyBones.RightHand, HumanBodyBones.LastBone, 0.6f);

            AddBone(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 10f);
            AddBone(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 10f);
            AddBone(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 4.65f);
            AddBone(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 4.65f);
            AddBone(HumanBodyBones.LeftFoot, HumanBodyBones.LastBone, 1.45f);
            AddBone(HumanBodyBones.RightFoot, HumanBodyBones.LastBone, 1.45f);
        }

        /// <summary>
        /// Çözülemeyen kemik <b>paydadan da düşer</b>: eksik bir uzuv oranı sessizce küçültseydi
        /// (payda sabit kalsaydı) o gövde hiçbir zaman %30'a ulaşamazdı.
        /// </summary>
        private void AddBone(HumanBodyBones a, HumanBodyBones b, float weight)
        {
            Transform boneA = _bodyAnimator.GetBoneTransform(a);
            if (boneA == null)
            {
                return;
            }

            Transform boneB = b != HumanBodyBones.LastBone
                ? _bodyAnimator.GetBoneTransform(b)
                : null;

            _bonePoints.Add(new SamplePoint { A = boneA, B = boneB, Weight = weight });
            _boneWeightTotal += weight;
        }

        /// <summary>
        /// Yerel gövdenin humanoid <see cref="Animator"/>'ı. Kaynağı <see cref="LocalBodyAvatar"/>
        /// tekilidir — ağa giden gövdenin ta kendisi, yani ölçtüğümüz şeyle başkalarının gördüğü şey
        /// aynı iskelettir.
        /// </summary>
        private static Animator ResolveBodyAnimator()
        {
            LocalBodyAvatar body = LocalBodyAvatar.Instance;
            if (body == null)
            {
                return null;
            }

            Animator[] animators = body.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null && animators[i].isHuman)
                {
                    return animators[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ sunum

        /// <summary>
        /// Karartma + titreşim. ⚠️ <b>Ekran TAMAMEN karartılmaz</b> ve bu bir emniyet kararıdır:
        /// oyuncu <b>fiziksel olarak</b> bir engelin içindedir; onu kör bırakmak gerçek bir objenin
        /// dibinde çıkış yolunu göremez hâle getirir. Karartmanın işlevi cezalandırmak değil
        /// <b>"geri çekil"</b> demektir.
        /// <para>Ölüyken susulur: ölüm ekranı zaten kendi sunumunu yapıyor.</para>
        /// </summary>
        private void ReportPresentation()
        {
            bool alive = ArenaCombat.IsAlive;
            float alpha = 0f;

            if (alive)
            {
                alpha = IsViolating
                    ? ViolationFadeAlpha
                    : WarnLevel * WarnFadeAlpha;
            }

            ScreenFade.Report(FadeSourceId, alpha, FadeColor);

            bool pulse = alive && IsViolating &&
                         Mathf.Repeat(Time.unscaledTime * HapticPulseHz, 1f) < 0.5f;
            SetHaptics(pulse);
        }

        private bool _hapticsOn;

        private void SetHaptics(bool on)
        {
            if (on == _hapticsOn)
            {
                return;
            }

            _hapticsOn = on;
            float amplitude = on ? 0.5f : 0f;
            OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.RTouch);
        }
    }
}
