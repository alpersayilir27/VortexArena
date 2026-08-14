#if UNITY_EDITOR
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silahın kavrama pozunu <b>gerçek elle</b> ölçen dev aracı: seçilen silah oyuncunun tam
    /// karşısında sabit durur, oyuncu elini kabzaya götürüp <b>pinch</b> yapar, beş saniyelik geri
    /// sayımın sonunda elin silaha göre YEREL pozu <c>WD_*.asset</c>'e yazılır.
    /// <para>
    /// Sıra sabittir ve <see cref="Stage"/>'te durur: ana kabza sağ → ana kabza sol → ön kabza sağ
    /// → ön kabza sol.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçünün paydası SİLAHTIR</b> (hareket eden eldir): kaydedilen değer, elin bileğinin
    /// silahın kendi uzayındaki pozudur. Bu yüzden silah bir kez yerleştirilip DONDURULUR ve
    /// oyuncu ona fiziksel olarak yaklaşır — silah kafayı izleseydi ölçüm kendi hedefinin peşinden
    /// koşardı.
    /// </para>
    /// <para>
    /// ⚠️ Dosyanın tamamı editör içidir: yazdığı yer bir asset'tir ve bu bir üretim özelliği değil,
    /// bir yazma aracıdır. Bileşen kalibrasyon sahnesinin köküne konur (sunucu, Boot/Lobby, maç
    /// akışı yoktur).
    /// </para>
    /// </summary>
    public class WeaponGripCalibration : MonoBehaviour
    {
        // ------------------------------------------------------------------ sabitler

        /// <summary>Sayacın uzunluğu (sn). Pinch'ten SONRA elin kabzaya oturtulması için gereken
        /// süre budur — kısaltmak, ölçüyü pinch anındaki (henüz kabzayı sarmamış) ele bağlar.</summary>
        private const float CountdownSeconds = 5f;

        /// <summary>Kayıt onayının ekranda kaldığı süre (sn). Aynı zamanda pinch kenarını
        /// temizleyen tampondur — kaydı bitiren pinch bir sonraki aşamayı başlatmasın.</summary>
        private const float SavedSeconds = 1.2f;

        /// <summary>Silahın kafanın ÖNÜNDE duracağı yatay mesafe (m) — kolun rahat uzandığı yer.</summary>
        private const float PlacementForward = 0.45f;

        /// <summary>Silahın göz hizasının ALTINDA duracağı yükseklik farkı (m): göğüs hizası, hem
        /// silahı hem eli aynı anda görebilecek kadar aşağıda.</summary>
        private const float PlacementDrop = 0.25f;

        /// <summary>Kafa pozunun "geçerli" sayılması için orijinden en az bu kadar uzaklaşması
        /// gerekir (m). İlk karelerde HMD pozu henüz akmaz ve kafa dünya orijininde durur — o anda
        /// yerleştirilen silah oyuncunun ayaklarının dibinde kalırdı.</summary>
        private const float PlacementMinHeadDistance = 0.2f;

        /// <summary>Kafa pozu hiç akmazsa (editörde gözlüksüz deneme) yerleştirmeyi yine de yapan
        /// üst sınır (kare). Beklemek sonsuza kadar sürerse araç sessizce hiç başlamaz.</summary>
        private const int PlacementMaxFrames = 120;

        // HUD renkleri: bekleme nötr, sayım dikkat, kayıt onay, hata kırmızı.
        private static readonly Color WaitColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color CountColor = new Color(1f, 0.8f, 0.25f, 1f);
        private static readonly Color SavedColor = new Color(0.35f, 1f, 0.45f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.4f, 0.35f, 1f);

        // ------------------------------------------------------------------ durum tipleri

        /// <summary>Kalibrasyon aşamaları — <b>sıra budur</b> ve değiştirilmez (HUD'daki "n/4"
        /// numaraları da bu sıradan türer).</summary>
        private enum Stage
        {
            PrimaryRight,
            PrimaryLeft,
            SecondaryRight,
            SecondaryLeft,
            Done
        }

        /// <summary>Bir aşamanın içindeki alt durum.</summary>
        private enum Phase
        {
            WaitingPinch,
            CountingDown,
            Saved
        }

        /// <summary>Aşamanın hangi kavrama noktasına ve hangi ele çözüldüğü + HUD etiketi.</summary>
        private readonly struct Step
        {
            public readonly GripSocketKind Kind;
            public readonly bool RightHand;
            public readonly string Label;

            public Step(GripSocketKind kind, bool rightHand, string label)
            {
                Kind = kind;
                RightHand = rightHand;
                Label = label;
            }
        }

        /// <summary>Aşama tablosu — dizinin sırası <see cref="Stage"/> ile BİREBİR aynıdır ve
        /// indeksle okunur; ikinci bir switch yazmak iki listenin sessizce ayrışması demekti.</summary>
        private static readonly Step[] Steps =
        {
            new Step(GripSocketKind.Primary, true, "1/4 · ANA KABZA · SAĞ EL"),
            new Step(GripSocketKind.Primary, false, "2/4 · ANA KABZA · SOL EL"),
            new Step(GripSocketKind.Secondary, true, "3/4 · ÖN KABZA · SAĞ EL"),
            new Step(GripSocketKind.Secondary, false, "4/4 · ÖN KABZA · SOL EL"),
        };

        // ------------------------------------------------------------------ alanlar

        [Tooltip("Gözlükteki yazılar. Boş bırakılabilir — araç HUD'suz da koşar, sonuç konsola düşer.")]
        [SerializeField] private WeaponGripCalibrationHud hud;

        private WeaponDefinition _definition;

        /// <summary>Ölçünün paydası: silahın kökü (bu objenin altında, dondurulmuş).</summary>
        private Transform _holder;

        private Transform _head;
        private bool _placed;
        private int _frames;

        private Stage _stage = Stage.PrimaryRight;
        private Phase _phase = Phase.WaitingPinch;
        private float _timer;

        /// <summary>Pinch YÜKSELEN KENARDA tetiklenir: basılı tutulan bir pinch sayacı yeniden
        /// başlatmasın.</summary>
        private bool _pinchWasDown;

        // ------------------------------------------------------------------ kurulum

        private void Start()
        {
            // ⚠️ Sentetik el burada HAM izlemeyi göstermeli: HandGripPoser boştaki elin parmaklarını
            // idle duruşuna KİLİTLİYOR (oyunda doğrudur — parmakların tek tanımı olmalı), ama burada
            // parmakları dondurmak oyuncunun pinch yaptığını görmesini de, kabzayı sarışını da
            // gizlerdi. Ölçüm boyunca poz sürücüsü susar.
            HandGripPoser.Suspended = true;

            _definition = WeaponGripCalibrationSession.LoadSelected();
            if (_definition == null)
            {
                Fail("Silah seçilmedi — Dev penceresinden bir WD_*.asset seç ve Play'e tekrar bas.");
                return;
            }

            if (_definition.Prefab == null)
            {
                Fail($"'{_definition.name}' tanımında prefab yok — ölçülecek bir gövde yok.");
                return;
            }

            _holder = BuildMeasurementTarget(_definition);
            _head = ResolveHead();

            SetStepHud();
            SetWaitingHud();
            WriteHint("Kafa izlemesi bekleniyor…");
        }

        private void OnDestroy()
        {
            // Kalibrasyon bitti: poz sürücüsü normal davranışına döner. Bırakılmazsa aynı oturumda
            // açılan bir sonraki sahnede eller idle'a hiç oturmaz.
            HandGripPoser.Suspended = false;
        }

        /// <summary>Kurulum başarısızsa HUD'a yaz ve bileşeni kapat — yarım koşan bir ölçüm
        /// aracı, hiç koşmayandan daha yanıltıcıdır.</summary>
        private void Fail(string message)
        {
            WriteCountdown("!", ErrorColor);
            WriteStep("KALİBRASYON BAŞLAMADI");
            WriteHint(message);
            Debug.LogError($"[WeaponGripCalibration] {message}");
            enabled = false;
        }

        /// <summary>
        /// Silahı ölçüm hedefi olarak kurar.
        /// <para>
        /// ⚠️ Silah burada bir OYUNCAK değil bir <b>ÖLÇÜ HEDEFİDİR</b>: kavranamaz, ateş etmez,
        /// çözülme efekti oynamaz, ses çıkarmaz. Bu yüzden tüm davranış silinir ve geriye yalnız
        /// çizim kalır — bileşenlerden biri bile hayatta kalsaydı silah ölçüm sırasında kendi
        /// pozunu yazmaya kalkardı (<c>Weapon.ApplyCanonicalGrip</c>) ve payda kayardı.
        /// </para>
        /// <para>
        /// ⚠️ Prefab <b>PASİF bir kuluçka kökünün altında</b> örneklenir: <c>Instantiate</c> aktif
        /// bir ağaçta <c>Awake</c>/<c>OnEnable</c>'ı ANINDA koşturur, yani bileşenleri silmeye
        /// fırsat bulamadan silah kendi kurulumunu yapmış olurdu.
        /// </para>
        /// <para>
        /// ⚠️ Silme <c>DestroyImmediate</c> iledir, <c>Destroy</c> ile DEĞİL: <c>Destroy</c> kare
        /// sonuna ertelenir ve aynı karede yapılan <c>SetActive(true)</c>, silinmeyi bekleyen
        /// bileşenlerin <c>OnEnable</c>'ını koştururdu. (Dosya editör içi olduğu için
        /// <c>DestroyImmediate</c> burada meşrudur.)
        /// </para>
        /// </summary>
        // ⚠️ Adı "BuildTarget" DEĞİL: bu dosya UnityEditor'ı using'liyor ve orada aynı adda bir
        // enum var — okuyanı yanıltacak bir gölgeleme.
        private Transform BuildMeasurementTarget(WeaponDefinition definition)
        {
            var holder = new GameObject("[GripCalibrationWeapon]");
            holder.SetActive(false);

            GameObject go = Instantiate(definition.Prefab, holder.transform);

            // Çerçeve (VA_WeaponFrame) ölçü hedefinin önünde duran bir KARTTIR: silahı gizler ve
            // eli kabzaya götürmeyi zorlaştırır. Bileşen birazdan silineceği için önce bulunur.
            var frame = go.GetComponentInChildren<WeaponFrame>(true);
            if (frame != null)
            {
                frame.gameObject.SetActive(false);
            }

            // ⚠️ Sıra önemli: davranış (MonoBehaviour) → fizik (Rigidbody/Collider) → ses.
            // Hepsi (true) ile pasif çocuklar dahil taranır; çerçeve gibi kapatılmış dallarda da
            // bileşen kalmamalı.
            StripAll<MonoBehaviour>(go);
            StripAll<Collider>(go);
            StripAll<Rigidbody>(go);
            StripAll<AudioSource>(go);

            // ℹ️ Çözülme efekti için ayrıca bir "tam katı" yazımı GEREKMEZ: SimpleWeaponDissolve
            // materyali yalnız silah ele geldiğinde (Weapon.HeldChanged) takıyor ve _Dissolve'u
            // MaterialPropertyBlock ile sürüyor. Burada Awake hiç koşmadığı ve silah hiç
            // tutulmadığı için renderer'lar prefabtaki özgün materyalleriyle kalır; property
            // block'lar da prefaba serialize edilmez. Yani bileşeni silmek silahı yarı çözülmüş
            // bırakmaz.

            holder.SetActive(true);
            return holder.transform;
        }

        /// <summary>Verilen tipteki TÜM bileşenleri (pasif çocuklar dahil) anında siler.</summary>
        private static void StripAll<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (component != null)
                {
                    DestroyImmediate(component);
                }
            }
        }

        /// <summary>Kafa referansı: rig varsa göz merkezi, yoksa ana kamera (editörde gözlüksüz
        /// deneme).</summary>
        private static Transform ResolveHead()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                return rig.centerEyeAnchor;
            }

            return Camera.main != null ? Camera.main.transform : null;
        }

        // ------------------------------------------------------------------ döngü

        private void Update()
        {
            if (!_placed)
            {
                TickPlacement();
                return;
            }

            if (_stage == Stage.Done)
            {
                return;
            }

            switch (_phase)
            {
                case Phase.WaitingPinch:
                    TickWaitingPinch();
                    break;

                case Phase.CountingDown:
                    TickCountdown();
                    break;

                case Phase.Saved:
                    TickSaved();
                    break;
            }
        }

        /// <summary>
        /// Silahı BİR KEZ karşıya yerleştirir ve dondurur.
        /// <para>
        /// ⚠️ Yerleştirme <c>Start</c>'ta yapılmaz: ilk karelerde HMD pozu henüz akmadığı için kafa
        /// dünya orijinindedir ve silah oyuncunun ayaklarının dibinde kalırdı. Kapı ya kafanın
        /// orijinden anlamlı biçimde ayrılmasını bekler ya da <see cref="PlacementMaxFrames"/>
        /// karede pes eder (gözlüksüz deneme).
        /// </para>
        /// <para>Yerleşim <b>tek seferliktir</b>: sonrasında oyuncu silaha fiziksel olarak
        /// yaklaşır — ölçünün paydası kıpırdamamalı.</para>
        /// </summary>
        private void TickPlacement()
        {
            _frames++;

            if (_head == null)
            {
                _head = ResolveHead();
            }

            bool headReady = _head != null && _head.position.magnitude > PlacementMinHeadDistance;
            if (!headReady && _frames < PlacementMaxFrames)
            {
                return;
            }

            Vector3 headPosition = _head != null ? _head.position : Vector3.zero;
            Vector3 headForward = _head != null ? _head.forward : Vector3.forward;

            // Yatay bileşen: silah göz hizasının altında ama DÜZ dursun; kafa aşağı bakıyorken
            // yerleştirilirse silah zemine gömülürdü.
            Vector3 forward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;

            _holder.SetPositionAndRotation(
                headPosition + forward * PlacementForward - Vector3.up * PlacementDrop,
                Quaternion.LookRotation(forward, Vector3.up));

            _placed = true;
            SetWaitingHud();
        }

        /// <summary>Pinch bekler. Tetik <b>yükselen kenardır</b>: aşamaya elin pinch'i basılı
        /// hâlde girmesi (bir önceki kaydın pinch'i) yeni bir sayaç başlatmasın.</summary>
        private void TickWaitingPinch()
        {
            Step step = Steps[(int)_stage];

            SyntheticHand hand = HandGripPoser.GetSynthetic(step.RightHand);
            if (hand == null || !hand.IsConnected)
            {
                _pinchWasDown = false;
                WriteHint("El izlenmiyor — kumandaları bırakın");
                return;
            }

            WriteHint("Elini silahın üstünde tutacağın yere getir, sonra pinch yap");

            bool pinching = hand.GetIndexFingerIsPinching();
            bool rising = pinching && !_pinchWasDown;
            _pinchWasDown = pinching;

            if (!rising)
            {
                return;
            }

            _phase = Phase.CountingDown;
            _timer = CountdownSeconds;
        }

        /// <summary>
        /// Geri sayım.
        /// <para>
        /// ⚠️ <b>Pinch bırakılınca sayaç İPTAL OLMAZ</b> ve böyle bir iptal eklenmez: oyuncu
        /// pinch'ten sonra elini açıp kabzayı sarmak ZORUNDADIR — sayacın var olma sebebi tam
        /// olarak budur. İptal eklenirse ölçü, elin kabzayı henüz sarmadığı pinch anına donar.
        /// </para>
        /// </summary>
        private void TickCountdown()
        {
            _timer -= Time.deltaTime;

            if (_timer > 0f)
            {
                WriteCountdown(Mathf.CeilToInt(_timer).ToString(), CountColor);
                WriteHint("Elini kabzaya oturt — sayaç bitince yakalanacak");
                return;
            }

            Capture();
        }

        /// <summary>Kayıt onayı: kısa bir bekleme. Aynı zamanda pinch kenarını temizler.</summary>
        private void TickSaved()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _pinchWasDown = false;
            _stage = (Stage)((int)_stage + 1);

            if (_stage == Stage.Done)
            {
                WriteCountdown("✓", SavedColor);
                WriteStep("TAMAMLANDI — 4/4");
                WriteHint(string.Empty);
                Debug.Log($"[WeaponGripCalibration] '{_definition.name}' kavrama kalibrasyonu " +
                          "tamamlandı (4/4) — ana kabza ve ön kabza, iki el.");
                return;
            }

            _phase = Phase.WaitingPinch;
            SetStepHud();
            SetWaitingHud();
        }

        // ------------------------------------------------------------------ yakalama

        /// <summary>
        /// Elin bileğini silahın YEREL uzayında ölçer ve tanıma yazar.
        /// <para>
        /// ⚠️ Konum farkı <b>elle</b> alınır (<c>Transform.InverseTransformPoint</c> DEĞİL): ölçü
        /// METREdir ve silahın görsel ölçeğiyle (<c>WPN_*</c> köklerinin 0.8'i) büyüyüp
        /// küçülmemelidir. Projede tekrarlanan kural; ölçekli bileşim kavramayı silahın ölçeği
        /// kadar kaydırır.
        /// </para>
        /// <para>Bilek okunamazsa (izleme koptu) ölçü YAZILMAZ ve aşama beklemeye döner: yanlış
        /// bir kavrama, hiç kavrama olmamasından kötüdür.</para>
        /// </summary>
        private void Capture()
        {
            Step step = Steps[(int)_stage];

            if (!HandGripPoser.TryGetTrackedWrist(step.RightHand, out Pose wrist))
            {
                _phase = Phase.WaitingPinch;
                _pinchWasDown = false;
                WriteCountdown("!", ErrorColor);
                WriteHint("Bilek okunamadı — elini görüş alanında tut ve tekrar pinch yap");
                return;
            }

            Transform item = _holder;
            Quaternion inverse = Quaternion.Inverse(item.rotation);
            var local = new Pose(
                inverse * (wrist.position - item.position),
                inverse * wrist.rotation);

            _definition.EditorSetGrip(step.Kind, step.RightHand, local);
            EditorUtility.SetDirty(_definition);
            AssetDatabase.SaveAssets();

            Debug.Log($"[WeaponGripCalibration] '{_definition.name}' · {step.Label} yakalandı — " +
                      $"pos {local.position:F4} · euler {local.rotation.eulerAngles:F1}");

            _phase = Phase.Saved;
            _timer = SavedSeconds;
            WriteCountdown("✓", SavedColor);
            WriteHint("KAYDEDİLDİ ✓");
        }

        // ------------------------------------------------------------------ HUD

        private void SetStepHud()
        {
            WriteStep(Steps[(int)_stage].Label);
        }

        private void SetWaitingHud()
        {
            WriteCountdown("PINCH", WaitColor);
            WriteHint("Elini silahın üstünde tutacağın yere getir, sonra pinch yap");
        }

        // ⚠️ HUD yazıları null-güvenli tek kapıdan geçer (`?.` ile DEĞİL): `?.` Unity'nin kendi
        // "yok edilmiş nesne" kontrolünü atlar ve sahne HUD'suz kurulduğunda aracı sessizce
        // patlatabilirdi. HUD bir kolaylıktır — yoksa ölçüm yine alınır, sonuç konsola düşer.
        private void WriteCountdown(string text, Color color)
        {
            if (hud != null)
            {
                hud.SetCountdown(text, color);
            }
        }

        private void WriteStep(string text)
        {
            if (hud != null)
            {
                hud.SetStep(text);
            }
        }

        private void WriteHint(string text)
        {
            if (hud != null)
            {
                hud.SetHint(text);
            }
        }
    }
}
#endif
