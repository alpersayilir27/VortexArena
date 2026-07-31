using Meta.XR.Movement.Retargeting.IK;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Üç noktalı pozdan (kafa + iki el) humanoid bir iskeleti çözer — uzak oyuncu
    /// avatarlarının sürücüsü.
    /// <para>
    /// <b>Neden IK gerekiyor:</b> ağdan gelen tek şey kafa ve iki elin pozu (§ <c>pose</c>
    /// mesajı, 92 B). Gövde, kollar ve bacaklar bu üç noktadan TÜRETİLİR; ağ tarafına tek bayt
    /// eklenmez. Dirsek/omuz yönü bir tahmindir — gerçek body tracking değildir.
    /// </para>
    /// <para>
    /// <b>Kemikler isimle DEĞİL <see cref="Animator.GetBoneTransform"/> ile bulunur:</b> karakter
    /// humanoid rig'e sahip olduğu için eşleme Avatar'da yaşar; model değiştiğinde (Mixamo'dan
    /// başka bir karaktere geçilse bile) bu bileşende tek satır değişmez ve prefabda elle
    /// bağlanacak Transform kalmaz. Gövde ölçüleri (kalça düşüşü, ayak bileği yüksekliği) de
    /// aynı sebeple SABİT DEĞİL, karakterin bind pozundan ÖLÇÜLÜR.
    /// </para>
    /// <para>
    /// <b>Bacaklar tamamen prosedüreldir</b> (adım döngüsü + ayak IK): projede yürüme animasyon
    /// klibi yoktur ve free-roam'da oyuncu fiziksel yürüdüğü için kök hareketi zaten gerçektir —
    /// tek eksik ayakların nereye basacağıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Üç kural bozulursa avatar görsel olarak PATLAR</b> (uzamış/kopmuş uzuvlar):
    /// (1) <see cref="IKUtilities.SolveCCDIK"/> zinciri <b>UÇ→KÖK</b> sırasında bekler
    /// (effector = dizinin 0. elemanı); ters verilirse çözücü omzu/kalçayı ele doğru sürükler.
    /// (2) Her kare <b>bind pozuna dönülür</b>: bu bileşen kemiklere mutlak değil BİRİKİMLİ
    /// (<c>rotation = delta * rotation</c>) yazıyor ve sahnede pozu sıfırlayacak bir
    /// AnimatorController yok — sıfırlanmazsa gövde her karede biraz daha bükülür.
    /// (3) El/kafa kemiğine <b>konum yazılmaz, yalnız rotasyon</b>: konum yazmak kemiği
    /// ebeveyninden koparır (mesh gerilir), üstelik erişilemeyen hedefte bunu her kare yapar.
    /// </para>
    /// <para>
    /// ⚠️ <b>Çözücü İKİ dikey referansı birleştirir</b> ve bu onun en kırılgan yeridir: kafa/eller
    /// AĞDAN (gönderenin uzayı), kök ve ayaklar ARENA ZEMİNİNDEN gelir. İkisi uyuşmazsa —
    /// gönderenin rig'i hizalı değilse — çözücü imkânsız bir gövde kurmak zorunda kalır: kafa
    /// zeminin altındayken ayak hedefi kalçanın ÜSTÜNE düşer ve CCD bacakları gövdenin üzerine
    /// sarar (avatar "top" olur, kafa göğsün içinde kalır). Bu yüzden gelen poz önce
    /// <b>makullüğe</b> bakılır: kafa arena zemininden
    /// [<see cref="MinPlausibleHeadHeight"/>, <see cref="MaxPlausibleHeadHeight"/>] m dışındaysa
    /// zemin referansı ARENADAN değil POZDAN türetilir — avatar yanlış yükseklikte çizilir ama
    /// <b>bütün bir insan</b> kalır (maç ortasında düşmanı kaybetmemek bilinçli bir tercihtir).
    /// </para>
    /// <para>
    /// ⚠️ <b>Ağdan gelen rotasyon kemiğe DOĞRUDAN yazılmaz</b> — izleme uzayı ile kemik uzayının
    /// bind ekseni farklıdır (köprü <see cref="HandGripConvention"/>). Ch15'te kafa/boyun/kalça
    /// kemiklerinin bind ekseni TESADÜFEN kimlik olduğu için <c>_head.rotation = head.rotation</c>
    /// çalışıyor (ölçüldü: 0° sapma); eller 115°/128° sapıyordu. Başka bir karaktere geçilirse
    /// kafa da aynı sebeple kırılır — o zaman çözüm ellerinkiyle aynıdır: bazı ölçüp çarpmak.
    /// </para>
    /// <para>
    /// ⚠️ <b>Hiçbir tahmin MANDALLANMAZ.</b> Boy tahmini sonsuz maksimum tutuyordu ve ayak bastığı
    /// noktaya kilitleniyordu; tek bozuk kare avatarı kalıcı olarak bozuyordu (poz düzeldiği hâlde
    /// dev + ayakları zeminin altında kalıyordu — ölçüldü). Boy artık kayan pencere maksimumu,
    /// ayaklar da ışınlanmada yeniden ziplatılıyor.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ThreePointBodyIK : MonoBehaviour
    {
        [Header("Gövde")]
        [Tooltip("Kalçanın kafa yönünün GERİSİNDE durma payı — öne eğilince gövde yatar.")]
        [SerializeField] private float hipsBackOffsetMeters = 0.06f;

        [Tooltip("Gövdenin kafa yaw'ını takip hızı (derece/sn). Düşük değer omuz-kafa farkı bırakır.")]
        [SerializeField] private float torsoYawFollowSpeed = 360f;

        [Tooltip("Gövdenin kafaya göre en fazla sapabileceği açı — bu aşılırsa gövde anında yetişir.")]
        [SerializeField] private float torsoMaxYawLagDegrees = 70f;

        [Header("Kollar")]
        [Tooltip("El/ayak hedefine kabul edilen yakınlık (metre).")]
        [SerializeField] private float armTolerance = 0.01f;

        [Tooltip("Kol IK yineleme sayısı.")]
        [SerializeField] private int armIterations = 8;

        [Header("Bacaklar")]
        [Tooltip("İki ayağın duruş genişliğinin yarısı (metre).")]
        [SerializeField] private float stanceHalfWidth = 0.11f;

        [Tooltip("Ayak, olması gereken yerden bu kadar uzaklaşınca adım başlar (metre).")]
        [SerializeField] private float stepTriggerDistance = 0.28f;

        [Tooltip("Bir adımın süresi (saniye).")]
        [SerializeField] private float stepDuration = 0.22f;

        [Tooltip("Adım yayının tepe yüksekliği (metre).")]
        [SerializeField] private float stepArcHeight = 0.09f;

        /// <summary>Avatar ölçeğinin alt/üst sınırı — bozuk bir poz ölçümü avatarı devleştirmesin.</summary>
        private const float MinScale = 0.75f;
        private const float MaxScale = 1.35f;

        /// <summary>
        /// Kafanın arena zemininden makul yüksekliği (m). Bu aralığın DIŞINDAKİ poz "gönderenin
        /// rig'i hizalı değil" demektir; zemin referansı o zaman arenadan değil pozdan alınır.
        /// <para>Aralık geniş tutulur (çömelmiş kısa oyuncu ↔ zıplayan uzun oyuncu): amaç boy
        /// denetlemek değil, METRELERCE kayan bir uzayı yakalamaktır.</para>
        /// </summary>
        private const float MinPlausibleHeadHeight = 0.6f;
        private const float MaxPlausibleHeadHeight = 2.6f;

        /// <summary>
        /// Boy tahmininin kayan pencere uzunluğu (sn). ⚠️ <b>Sonsuz maksimum kullanılmaz:</b> tek
        /// bozuk kare avatarı kalıcı olarak devleştiriyordu. Pencere, çömelmede küçülmeyi
        /// önleyecek kadar uzun, hatalı bir örneği unutacak kadar kısadır.
        /// </summary>
        private const float StandingHeightWindowSeconds = 5f;

        /// <summary>
        /// Ayak, bulunması gereken yerden bu kadar uzaktaysa adım atılmaz, doğrudan ZIPLATILIR (m).
        /// <para>⚠️ <see cref="stepTriggerDistance"/> ile karıştırma: o "adım başlat" eşiği, bu
        /// "adım atmanın anlamsız olduğu sıçrama" eşiğidir. Işınlanan avatarda (harita değişimi,
        /// kalibrasyon düzeltmesi, poz sıçraması) ayak eski noktada kalıp bacağı metrelerce
        /// uzatıyordu.</para>
        /// </summary>
        private const float FootResnapDistance = 1f;

        /// <summary>Güvenilmez poz uyarısının en sık tekrar aralığı (sn) — 20 Hz'de log seli olurdu.</summary>
        private const float UntrustedWarnCooldownSeconds = 10f;

        private Animator _animator;
        private bool _bonesResolved;

        // Gövde
        private Transform _hips;
        private Transform _spine;
        private Transform _chest;
        private Transform _upperChest;
        private Transform _neck;
        private Transform _head;

        // Kollar — CCD zincirleri her karede yeniden ayrılmasın diye alanda tutulur.
        // ⚠️ Sıra UÇ→KÖK: { el, önkol, üstkol } (bkz. sınıf özeti).
        private Transform[] _leftArmChain;
        private Transform[] _rightArmChain;
        private Transform _leftHand;
        private Transform _rightHand;

        /// <summary>İzleme uzayı → el kemiği ekseni köprüsü; karakterin bind pozundan BİR KEZ
        /// ölçülür (bkz. <see cref="HandGripConvention"/>). Ölçülemezse kimlik kalır.</summary>
        private Quaternion _leftHandCorrection = Quaternion.identity;
        private Quaternion _rightHandCorrection = Quaternion.identity;

        // Bacaklar — aynı sıra: { ayak, alt bacak, üst bacak }.
        private Transform[] _leftLegChain;
        private Transform[] _rightLegChain;

        // Bind pozu: her kare buraya dönülür (birikimli yazımın panzehiri).
        private Transform[] _drivenBones;
        private Quaternion[] _bindLocalRotations;
        private Vector3[] _bindLocalPositions;

        /// <summary>Karakterin KENDİ ölçüleri (bind pozunda, kök uzayında; ölçek 1 iken).</summary>
        private float _modelHeadHeight;
        private float _modelHipsDrop;
        private float _modelAnkleHeight;

        /// <summary>Oyuncunun ölçülen ayakta kafa yüksekliği — avatar buna göre ölçeklenir.
        /// <para>Kayan pencere maksimumu: <see cref="_recentMaxHeadHeight"/> ikinci kovadır,
        /// pencere dolunca yerine geçer (O(1), tahsissiz).</para></summary>
        private float _standingHeadHeight;
        private float _recentMaxHeadHeight;
        private float _heightWindowTimer;
        private float _scale = 1f;

        /// <summary>Son karede gelen poz makul müydü (bkz. sınıf özeti). Değişimde ayaklar sıfırlanır.</summary>
        private bool _poseTrusted = true;
        private float _lastUntrustedWarnTime = float.NegativeInfinity;

        /// <summary>
        /// Çözücüye verilecek tolerans. ⚠️ <see cref="IKUtilities.SolveCCDIK"/> toleransı
        /// <b>KARESİYLE</b> karşılaştırır (<c>sqrMagnitude &gt; tolerance</c>) — metre cinsinden
        /// verilirse 0.01 m istenirken 0.1 m'de durur ve el hedefin bir karış uzağında kalır.
        /// </summary>
        private float SolverTolerance => armTolerance * armTolerance;

        private Quaternion _torsoYaw = Quaternion.identity;
        private bool _yawInitialized;

        /// <summary>Ayağın şu an bastığı (dünya) nokta.</summary>
        private Vector3 _leftFootPlanted;
        private Vector3 _rightFootPlanted;

        /// <summary>Adım başlangıç noktası ve ilerleme [0..1]; 1 = adım bitti.</summary>
        private Vector3 _leftFootFrom;
        private Vector3 _rightFootFrom;
        private float _leftStepProgress = 1f;
        private float _rightStepProgress = 1f;

        private bool _feetInitialized;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            ResolveBones();
        }

        /// <summary>
        /// Bir karelik çözüm. Pozlar DÜNYA uzayındadır (çağıran arena→dünya dönüşümünü yapmıştır).
        /// <see cref="LateUpdate"/> yerine dışarıdan çağrılır: sürücü <c>RemoteAvatar</c>'dır ve
        /// pozu ancak kayıt defterinden okuduktan sonra verebilir.
        /// </summary>
        public void Solve(in Pose head, in Pose handL, in Pose handR)
        {
            if (!_bonesResolved || _hips == null)
            {
                return;
            }

            // NaN/sonsuz poz: çizilecek makul hiçbir şey yok. Avatar son geçerli karesinde
            // bırakılır — bozuk sayıyı kemiklere yazmak Unity'de matris hatası basar ve iskelet
            // o kareden sonra geri gelmez.
            if (!IsFinite(head) || !IsFinite(handL) || !IsFinite(handR))
            {
                WarnUntrusted("poz NaN/sonsuz değer taşıyor", 0f, 0f, 0f);
                return;
            }

            Pose safeHead = Sanitize(head);
            Pose safeHandL = Sanitize(handL);
            Pose safeHandR = Sanitize(handR);

            float deltaTime = Time.deltaTime;
            float arenaGroundY = ArenaSpace.HasOrigin
                ? ArenaSpace.ArenaToWorld(Vector3.zero).y
                : 0f;

            // Gelen pozun arena zeminiyle uyumu: uyumsuzsa gövde ARENAYA göre değil KENDİNE göre
            // kurulur (bkz. sınıf özeti — yanlış yükseklikte ama bütün bir insan).
            float headHeight = safeHead.position.y - arenaGroundY;
            bool trusted = headHeight >= MinPlausibleHeadHeight && headHeight <= MaxPlausibleHeadHeight;

            RestoreBindPose();

            if (trusted)
            {
                ApplyScale(headHeight, deltaTime);
            }
            else
            {
                WarnUntrusted("kafa arena zemininden makul olmayan yükseklikte",
                    headHeight, arenaGroundY, safeHead.position.y);
            }

            // ⚠️ Güvenilmez pozda zemin POZDAN türetilir. Arena zeminini kullanmak kökü gövdeden
            // metrelerce ayırır: ayak hedefi kalçanın üstüne çıkar ve bacaklar gövdeye sarılır.
            float groundY = trusted
                ? arenaGroundY
                : safeHead.position.y - _modelHeadHeight * _scale;

            if (trusted != _poseTrusted)
            {
                // Zemin referansı değişti: eski referansta basılan ayaklar artık anlamsız.
                _poseTrusted = trusted;
                _feetInitialized = false;
            }

            Quaternion yaw = SolveTorsoYaw(safeHead.rotation, deltaTime);

            PlaceRoot(safeHead.position, yaw, groundY);
            PlaceTorso(safeHead, yaw);
            SolveArm(_leftArmChain, _leftHand, safeHandL, _leftHandCorrection);
            SolveArm(_rightArmChain, _rightHand, safeHandR, _rightHandCorrection);
            SolveLegs(yaw, deltaTime, groundY);
        }

        /// <summary>
        /// Avatar başka bir oyuncuya devredildiğinde ya da poz akışı baştan başladığında çağrılır:
        /// tahmin edilen HER ŞEYİ (boy, ölçek, gövde yaw'ı, basılan ayaklar) sıfırlar.
        /// <para>⚠️ Bu olmadan önceki oyuncunun boyu/duruşu yeni oyuncuya miras kalır — hepsi
        /// mandallı tahminler olduğu için kendiliğinden düzelmezler.</para>
        /// </summary>
        public void ResetPoseState()
        {
            _standingHeadHeight = _modelHeadHeight;
            _recentMaxHeadHeight = _modelHeadHeight;
            _heightWindowTimer = 0f;
            _scale = 1f;
            transform.localScale = Vector3.one;
            _yawInitialized = false;
            _feetInitialized = false;
            _leftStepProgress = 1f;
            _rightStepProgress = 1f;
            _poseTrusted = true;
            _lastUntrustedWarnTime = float.NegativeInfinity;
        }

        // -------------------------------------------------------------- poz denetimi

        /// <summary>Poz sonlu sayılardan mı oluşuyor (NaN/∞ kemiklere yazılmaz).</summary>
        private static bool IsFinite(in Pose pose)
        {
            Vector3 p = pose.position;
            Quaternion q = pose.rotation;
            return IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z) &&
                   IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Rotasyonu kullanılabilir hâle getirir. ⚠️ <b>Sıfır quaternion telde meşru bir değer
        /// gibi görünür</b> (dört sıfır bayt) ama Unity'de geçersizdir: bir Transform'a yazılınca
        /// "Quaternion To Matrix conversion failed" basar ve o kemik o kareden sonra bozuk kalır
        /// (ölçüldü: ayak zeminin altına kilitleniyordu). Normalize edilemeyen rotasyon kimliğe
        /// düşürülür — yanlış yön, bozuk iskeletten iyidir.
        /// </summary>
        private static Pose Sanitize(in Pose pose)
        {
            Quaternion q = pose.rotation;
            float sqrMagnitude = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sqrMagnitude < 0.5f || sqrMagnitude > 2f)
            {
                return new Pose(pose.position, Quaternion.identity);
            }

            return new Pose(pose.position, Quaternion.Normalize(q));
        }

        /// <summary>
        /// Güvenilmez pozu bir kez (sonra en fazla <see cref="UntrustedWarnCooldownSeconds"/>'de bir)
        /// SAYILARLA loglar: sahada "avatar tuhaf" demek yerine hangi uzayın kaydığı okunabilsin.
        /// </summary>
        private void WarnUntrusted(string reason, float headHeight, float groundY, float headWorldY)
        {
            if (Time.time - _lastUntrustedWarnTime < UntrustedWarnCooldownSeconds)
            {
                return;
            }

            _lastUntrustedWarnTime = Time.time;
            Debug.LogWarning(
                $"[ThreePointBodyIK] '{name}': {reason} " +
                $"(kafa yüksekliği {headHeight:F2} m, arena zemini Y={groundY:F2}, kafa dünya Y={headWorldY:F2}; " +
                $"makul aralık [{MinPlausibleHeadHeight:F2}, {MaxPlausibleHeadHeight:F2}] m). " +
                "Gönderen oyuncunun rig'i arenayla hizalı değil — kalibrasyon yapılmamış ya da " +
                "kayıtlı hizalama yanlış geri yüklenmiş olabilir. Gövde, zemini POZDAN türetilerek " +
                "bütün çizildi (yükseklik yanlış).", this);
        }

        // ------------------------------------------------------------ bind + ölçek

        /// <summary>
        /// Sürülen tüm kemikleri bind pozuna döndürür. Zorunludur: <see cref="ApplyLean"/> ve CCD
        /// kemiklere mevcut rotasyonun ÜSTÜNE yazıyor, sahnede de pozu sıfırlayan bir
        /// AnimatorController yok — sıfırlanmazsa hata her karede birikir ve gövde dakikalar
        /// içinde katlanır.
        /// </summary>
        private void RestoreBindPose()
        {
            for (int i = 0; i < _drivenBones.Length; i++)
            {
                Transform bone = _drivenBones[i];
                if (bone != null)
                {
                    bone.localRotation = _bindLocalRotations[i];
                    bone.localPosition = _bindLocalPositions[i];
                }
            }
        }

        /// <summary>
        /// Avatarı oyuncunun boyuna ölçekler. Model sabit boydadır (Ch15 ≈ 1.56 m kafa kemiği);
        /// ölçeklenmezse kısa kollar ele yetişemez, bacaklar zemine değmez — tam da "uzamış/kopuk
        /// uzuv" görüntüsü buradan çıkar.
        /// <para>Ölçü <b>en yüksek</b> gözlenen kafa yüksekliğinden alınır: eğilen/çömelen oyuncuda
        /// anlık yüksekliğe uyulsaydı avatar her çömelmede küçülürdü.</para>
        /// <para>⚠️ Maksimum <b>KAYAN PENCEREDE</b> tutulur (iki kova,
        /// <see cref="StandingHeightWindowSeconds"/>). Sonsuz maksimum, tek bir yüksek (ama makul
        /// aralıkta kalan) örneği kalıcı yapıyordu: 2.05 m'lik bir kafa avatarı 1.32×'e kilitliyor
        /// ve poz düzelse bile geri dönmüyordu.</para>
        /// </summary>
        private void ApplyScale(float headHeightMeters, float deltaTime)
        {
            if (_modelHeadHeight <= 0.01f || headHeightMeters <= 0.01f)
            {
                return;
            }

            if (headHeightMeters > _standingHeadHeight)
            {
                _standingHeadHeight = headHeightMeters;
            }

            if (headHeightMeters > _recentMaxHeadHeight)
            {
                _recentMaxHeadHeight = headHeightMeters;
            }

            _heightWindowTimer += deltaTime;
            if (_heightWindowTimer >= StandingHeightWindowSeconds)
            {
                // Pencere devri: geçmiş kova düşer, yerine son pencerenin maksimumu geçer.
                _heightWindowTimer = 0f;
                _standingHeadHeight = _recentMaxHeadHeight;
                _recentMaxHeadHeight = headHeightMeters;
            }

            float scale = Mathf.Clamp(_standingHeadHeight / _modelHeadHeight, MinScale, MaxScale);
            if (Mathf.Abs(scale - _scale) < 0.001f)
            {
                return;
            }

            _scale = scale;
            transform.localScale = new Vector3(scale, scale, scale);
        }

        // ------------------------------------------------------------------- gövde

        /// <summary>
        /// Gövde yaw'ı kafayı GECİKMELİ takip eder (anında yapışsaydı avatar her bakışta
        /// bütün gövdesiyle dönerdi). Fark <see cref="torsoMaxYawLagDegrees"/>'i aşınca gövde
        /// yetişir — sırtı dönük duran bir avatar oluşmasın diye.
        /// </summary>
        private Quaternion SolveTorsoYaw(Quaternion headRotation, float deltaTime)
        {
            Vector3 forward = headRotation * Vector3.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-6f)
            {
                // Kafa tam yukarı/aşağı bakıyor — yaw'ı kafanın tepesinden türet.
                forward = headRotation * Vector3.up;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-6f)
                {
                    return _torsoYaw;
                }
            }

            Quaternion target = Quaternion.LookRotation(forward.normalized, Vector3.up);

            if (!_yawInitialized)
            {
                _yawInitialized = true;
                _torsoYaw = target;
                return _torsoYaw;
            }

            _torsoYaw = Quaternion.RotateTowards(_torsoYaw, target, torsoYawFollowSpeed * deltaTime);

            float lag = Quaternion.Angle(_torsoYaw, target);
            if (lag > torsoMaxYawLagDegrees)
            {
                _torsoYaw = Quaternion.RotateTowards(_torsoYaw, target, lag - torsoMaxYawLagDegrees);
            }

            return _torsoYaw;
        }

        /// <summary>Kökü zemine oturtur: yatay konum kafadan, yükseklik ARENA ZEMİNİNDEN gelir.
        /// <para>Zemin kafadan sabit bir boy çıkararak bulunmaz — oyuncu eğildiğinde avatar yere
        /// gömülürdü.</para></summary>
        private void PlaceRoot(Vector3 headPosition, Quaternion yaw, float groundY)
        {
            transform.SetPositionAndRotation(
                new Vector3(headPosition.x, groundY, headPosition.z), yaw);
        }

        /// <summary>
        /// Kalça + omurga + kafa. Kalçanın kafadan düşüşü karakterin KENDİ bind pozundan ölçülür
        /// (ölçekle çarpılır) — sabit bir sayı yazılsaydı model değiştiğinde ya da oyuncu
        /// ölçeklendiğinde omurga gerilirdi.
        /// <para>Omurga eğimi kalçadan kafaya giden vektörden gelir ve zincire PAYLAŞTIRILIR
        /// (tek kemiğe verilseydi bel kırılır gibi görünürdü).</para>
        /// </summary>
        private void PlaceTorso(in Pose head, Quaternion yaw)
        {
            Vector3 hipsPosition = head.position
                                   - Vector3.up * (_modelHipsDrop * _scale)
                                   - (yaw * Vector3.forward) * (hipsBackOffsetMeters * _scale);

            _hips.SetPositionAndRotation(hipsPosition, yaw);

            Vector3 spineDirection = head.position - hipsPosition;
            if (spineDirection.sqrMagnitude > 1e-6f)
            {
                Quaternion lean = Quaternion.FromToRotation(Vector3.up, spineDirection.normalized);

                // Eğim omurga kemiklerine eşit paylaştırılır; kaç kemik olduğu karaktere göre değişir.
                int segments = 0;
                if (_spine != null) segments++;
                if (_chest != null) segments++;
                if (_upperChest != null) segments++;

                if (segments > 0)
                {
                    Quaternion share = Quaternion.Slerp(Quaternion.identity, lean, 1f / segments);
                    ApplyLean(_spine, share);
                    ApplyLean(_chest, share);
                    ApplyLean(_upperChest, share);
                }
            }

            if (_neck != null)
            {
                // Boyun kafayı taşır ama kafanın kendi rotasyonu aşağıda birebir yazılır.
                _neck.rotation = Quaternion.Slerp(_neck.rotation, head.rotation, 0.5f);
            }

            if (_head != null)
            {
                // ⚠️ Yalnız ROTASYON: kafa kemiğine konum yazmak onu boyundan koparır (boyun mesh'i
                // gerilir). Konumu zaten kalça + omurga zinciri getiriyor.
                _head.rotation = head.rotation;
            }
        }

        private static void ApplyLean(Transform bone, Quaternion share)
        {
            if (bone != null)
            {
                bone.rotation = share * bone.rotation;
            }
        }

        // -------------------------------------------------------------------- kol

        /// <summary>
        /// Kolu ele ulaştırır (CCD), sonra elin KENDİ rotasyonunu yazar: silah tutuşu elin
        /// yönünden okunuyor, çözücünün bulduğu yönelim yeterli değil.
        /// <para>⚠️ Rotasyon <b>çarpımla</b> yazılır: gelen poz kumanda anchor'ı uzayındadır,
        /// kemik ise karakterin bind eksenindedir. <paramref name="correction"/> bu iki eksen
        /// arasındaki köprüdür ve <see cref="ResolveBones"/> içinde karakterin bind pozundan
        /// ölçülür (<see cref="HandGripConvention"/>); doğrudan atansaydı bilek ters çizilirdi.</para>
        /// <para>⚠️ El kemiğine KONUM yazılmaz. Hedef kolun erişemeyeceği kadar uzaktayken
        /// (kumanda pozu bileğin değil avucun ötesindedir) konum yazmak eli önkoldan koparır ve
        /// mesh gerilir; ulaşılamayan hedefte kol kısa kalsın, kopmasın.</para>
        /// </summary>
        private void SolveArm(Transform[] chain, Transform hand, in Pose handPose, in Quaternion correction)
        {
            if (chain == null || hand == null)
            {
                return;
            }

            IKUtilities.SolveCCDIK(chain, handPose.position, SolverTolerance, armIterations);
            hand.rotation = handPose.rotation * correction;
        }

        // ----------------------------------------------------------------- bacaklar

        /// <summary>
        /// Prosedürel adım: her ayağın bastığı nokta saklanır; gövde yeterince uzaklaşınca o ayak
        /// yeni yerine bir yay çizerek gider. AYNI ANDA iki ayak birden adım atmaz — atsaydı
        /// avatar zıplıyormuş gibi görünürdü.
        /// <para>Hedef yüksekliği zemin DEĞİL, zemin + ayak bileği yüksekliğidir: IK'nın ucu ayak
        /// kemiği (bilek) olduğu için zemine hizalanırsa ayak tabanı yere gömülür.</para>
        /// </summary>
        private void SolveLegs(Quaternion yaw, float deltaTime, float groundY)
        {
            if (_leftLegChain == null || _rightLegChain == null)
            {
                return;
            }

            Vector3 root = transform.position;
            root.y = groundY + _modelAnkleHeight * _scale;

            Vector3 right = yaw * Vector3.right;
            float halfWidth = stanceHalfWidth * _scale;
            Vector3 leftTarget = root - right * halfWidth;
            Vector3 rightTarget = root + right * halfWidth;

            // ⚠️ Işınlanma emniyeti — ADIM eşiğinden AYRI: hedef bastığı noktadan bu kadar
            // uzaktaysa oraya adımla gidilmez, ayak ziplatılır. Yoksa avatar sıçradığında (harita
            // değişimi, kalibrasyon düzeltmesi, bozuk kare) ayak eski dünya noktasında kalıp
            // bacağı metrelerce uzatıyor ve o poz bir daha düzelmiyordu.
            float resnapDistance = FootResnapDistance * _scale;
            if (!_feetInitialized ||
                Vector3.Distance(_leftFootPlanted, leftTarget) > resnapDistance ||
                Vector3.Distance(_rightFootPlanted, rightTarget) > resnapDistance)
            {
                _feetInitialized = true;
                _leftFootPlanted = _leftFootFrom = leftTarget;
                _rightFootPlanted = _rightFootFrom = rightTarget;
                _leftStepProgress = 1f;
                _rightStepProgress = 1f;
            }

            bool leftStepping = _leftStepProgress < 1f;
            bool rightStepping = _rightStepProgress < 1f;

            // Adım başlatma — sırayla, hiç değilse bir ayak yerde kalsın.
            if (!leftStepping && !rightStepping)
            {
                float trigger = stepTriggerDistance * _scale;
                float leftError = Vector3.Distance(_leftFootPlanted, leftTarget);
                float rightError = Vector3.Distance(_rightFootPlanted, rightTarget);

                if (leftError > trigger && leftError >= rightError)
                {
                    _leftFootFrom = _leftFootPlanted;
                    _leftStepProgress = 0f;
                    leftStepping = true;
                }
                else if (rightError > trigger)
                {
                    _rightFootFrom = _rightFootPlanted;
                    _rightStepProgress = 0f;
                    rightStepping = true;
                }
            }

            Vector3 leftFoot = AdvanceStep(
                ref _leftStepProgress, ref _leftFootPlanted, _leftFootFrom, leftTarget, leftStepping, deltaTime);
            Vector3 rightFoot = AdvanceStep(
                ref _rightStepProgress, ref _rightFootPlanted, _rightFootFrom, rightTarget, rightStepping, deltaTime);

            IKUtilities.SolveCCDIK(_leftLegChain, leftFoot, SolverTolerance, armIterations);
            IKUtilities.SolveCCDIK(_rightLegChain, rightFoot, SolverTolerance, armIterations);
        }

        /// <summary>Adımı bir kare ilerletir; adım yoksa ayak bastığı yerde kalır.</summary>
        private Vector3 AdvanceStep(
            ref float progress, ref Vector3 planted, Vector3 from, Vector3 target, bool stepping, float deltaTime)
        {
            if (!stepping)
            {
                return planted;
            }

            progress = stepDuration > 0f
                ? Mathf.Min(1f, progress + deltaTime / stepDuration)
                : 1f;

            Vector3 position = Vector3.Lerp(from, target, progress);
            position.y += Mathf.Sin(progress * Mathf.PI) * stepArcHeight * _scale;

            if (progress >= 1f)
            {
                planted = target;
                return target;
            }

            return position;
        }

        // ----------------------------------------------------------------- kurulum

        /// <summary>Humanoid Avatar'dan kemikleri toplar. Omuz/üst göğüs gibi İSTEĞE BAĞLI
        /// kemikler karakterde olmayabilir — zincirler o duruma göre kurulur.
        /// <para>Aynı geçişte karakterin bind pozu (her kare geri dönülecek referans) ve gövde
        /// ölçüleri (kalça düşüşü, ayak bileği yüksekliği) kaydedilir.</para></summary>
        private void ResolveBones()
        {
            if (_animator == null || !_animator.isHuman)
            {
                Debug.LogWarning(
                    "[ThreePointBodyIK] Animator humanoid değil; IK devre dışı. " +
                    "Karakterin import ayarında Animation Type = Humanoid olmalı.", this);
                return;
            }

            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);

            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);

            // ⚠️ Zincirler UÇ→KÖK sıralanır: Meta'nın CCD çözücüsü effector olarak dizinin
            // 0. elemanını alır ve zincirin kökünü son elemanın EBEVEYNİNDEN bulur.
            _leftArmChain = BuildChain(
                _leftHand,
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
            _rightArmChain = BuildChain(
                _rightHand,
                _animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                _animator.GetBoneTransform(HumanBodyBones.RightUpperArm));

            _leftLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
            _rightLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.RightFoot),
                _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));

            _bonesResolved = _hips != null;

            if (!_bonesResolved)
            {
                Debug.LogWarning("[ThreePointBodyIK] Hips kemiği bulunamadı; IK devre dışı.", this);
                return;
            }

            CacheBindPose();
            MeasureModel();
            MeasureHandCorrections();
        }

        /// <summary>
        /// El kemiklerinin bind eksenini ölçüp izleme uzayına köprüyü kurar
        /// (<see cref="HandGripConvention"/>).
        /// <para>⚠️ <b>Bind pozu bozulmadan ÖNCE</b> çağrılmalıdır: <see cref="Awake"/> anında
        /// henüz hiçbir <see cref="Solve"/> koşmamıştır, sonraki bir kare ölçülseydi baz o karenin
        /// duruşunu içerirdi ve düzeltme kalıcı olarak yanlış çıkardı.</para>
        /// </summary>
        private void MeasureHandCorrections()
        {
            MeasureHandCorrection(
                _leftHand,
                _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal),
                _animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal),
                false,
                ref _leftHandCorrection);

            MeasureHandCorrection(
                _rightHand,
                _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal),
                _animator.GetBoneTransform(HumanBodyBones.RightThumbProximal),
                true,
                ref _rightHandCorrection);
        }

        /// <summary>Tek elin düzeltmesi; parmak kemikleri humanoid'de İSTEĞE BAĞLI olduğu için
        /// ölçüm düşebilir — o zaman düzeltme kimlik kalır ve durum AÇIKÇA loglanır.</summary>
        private void MeasureHandCorrection(
            Transform hand,
            Transform middleProximal,
            Transform thumbProximal,
            bool rightHand,
            ref Quaternion correction)
        {
            if (HandGripConvention.TryMeasureBoneBasis(
                    hand, middleProximal, thumbProximal, rightHand, out Quaternion boneBasis))
            {
                correction = HandGripConvention.Correction(rightHand, boneBasis);
                return;
            }

            correction = Quaternion.identity;
            Debug.LogWarning(
                $"[ThreePointBodyIK] '{name}': {(rightHand ? "SAĞ" : "SOL")} elin bind ekseni " +
                "ölçülemedi (orta parmak / başparmak kemiği yok ya da yönleri dejenere). " +
                "Humanoid'de parmak kemikleri isteğe bağlıdır; karakterin Avatar eşlemesinde " +
                "MiddleProximal ve ThumbProximal atanmalı. Düzeltme kimlik bırakıldı — " +
                "bu elin BİLEĞİ YANLIŞ YÖNDE çizilecek.", this);
        }

        /// <summary>Sürülen kemiklerin bind pozunu saklar — her kare buraya dönülür.</summary>
        private void CacheBindPose()
        {
            var bones = new System.Collections.Generic.List<Transform>
            {
                _hips, _spine, _chest, _upperChest, _neck, _head
            };

            AddChain(bones, _leftArmChain);
            AddChain(bones, _rightArmChain);
            AddChain(bones, _leftLegChain);
            AddChain(bones, _rightLegChain);

            bones.RemoveAll(b => b == null);

            _drivenBones = bones.ToArray();
            _bindLocalRotations = new Quaternion[_drivenBones.Length];
            _bindLocalPositions = new Vector3[_drivenBones.Length];

            for (int i = 0; i < _drivenBones.Length; i++)
            {
                _bindLocalRotations[i] = _drivenBones[i].localRotation;
                _bindLocalPositions[i] = _drivenBones[i].localPosition;
            }
        }

        private static void AddChain(System.Collections.Generic.List<Transform> target, Transform[] chain)
        {
            if (chain == null)
            {
                return;
            }

            for (int i = 0; i < chain.Length; i++)
            {
                if (!target.Contains(chain[i]))
                {
                    target.Add(chain[i]);
                }
            }
        }

        /// <summary>
        /// Karakterin bind pozundaki ölçüleri (kök uzayında, ölçek 1 iken). Sabit sayı yerine
        /// modelden okunur: başka bir karaktere geçildiğinde tek satır değişmesin diye.
        /// </summary>
        private void MeasureModel()
        {
            Vector3 hipsLocal = transform.InverseTransformPoint(_hips.position);

            if (_head != null)
            {
                Vector3 headLocal = transform.InverseTransformPoint(_head.position);
                _modelHeadHeight = headLocal.y;
                _modelHipsDrop = Mathf.Max(0.05f, headLocal.y - hipsLocal.y);
            }
            else
            {
                _modelHeadHeight = hipsLocal.y;
                _modelHipsDrop = 0f;
            }

            Transform foot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _modelAnkleHeight = foot != null
                ? Mathf.Max(0f, transform.InverseTransformPoint(foot.position).y)
                : 0f;

            _standingHeadHeight = _modelHeadHeight;
            _recentMaxHeadHeight = _modelHeadHeight;
        }

        /// <summary>Üç kemikten zincir kurar; biri bile eksikse zincir kurulmaz (CCD en az iki
        /// kemik ister ve yarım zincir sessizce yanlış poz üretir).
        /// <para>⚠️ Sıra UÇ→KÖK verilir (<c>{ el, önkol, üstkol }</c>).</para></summary>
        private static Transform[] BuildChain(Transform tip, Transform middle, Transform root)
        {
            if (tip == null || middle == null || root == null)
            {
                return null;
            }

            return new[] { tip, middle, root };
        }
    }
}
