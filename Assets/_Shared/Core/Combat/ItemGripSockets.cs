using Oculus.Interaction;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Eşyanın kavrama noktalarını <b>SOKET</b> yapar: el yaklaşınca soket belirginleşir, el soketin
    /// üstüne gelince kavrama izni doğar. Eşya prefabının KÖKÜNE konur (WPN'lerde
    /// <c>WeaponKitBuilder</c> bağlar).
    /// <para>
    /// <b>Kapı ISDK'nın kendi uzatma noktasıdır</b>: bu bileşen bir
    /// <see cref="IGameObjectFilter"/>'dır ve <c>Interactable&lt;,&gt;._interactorFilters</c>
    /// listesine yazılır — <c>CanBeSelectedBy</c> her filtreyi sorar. Yani kavramanın ALGISI
    /// ISDK'da kalır (Select olayı <c>Weapon.HandlePointerEvent</c>'i, o da <c>HeldItems</c>
    /// üzerinden teli besler); biz yalnız "izin var mı" sorusuna cevap veriyoruz. Filtre
    /// reddederse Select hiç doğmaz, dolayısıyla ağa da hiçbir şey yazılmaz.
    /// ⚠️ Bu yüzden <b>kendi grip tuşu okuma yolu YAZILMAZ</b>: ikinci bir kavrama algısı
    /// ISDK'nın durumundan sapar ve elde tutulan eşya telde yanlış görünürdü.
    /// </para>
    /// <para>
    /// <b>Uzak avatarlarda soket ÇİZİLMEZ</b> ve bunun için bir şey yapmak gerekmiyor:
    /// <c>RemoteAvatar.SterilizeVisual</c> kopyadaki tüm MonoBehaviour'ları yok ediyor —
    /// gösterge yalnız yerel oyuncunun elindeki/sahnedeki eşyada yaşar.
    /// </para>
    /// <para>
    /// ⚠️ <b>Editördeki iki gizmo bilerek FARKLI renktedir:</b> sarı dolu küre
    /// (<see cref="GripSocketMarker"/>) işaretçinin — yani SO'ya yazılmayı bekleyen değerin — yeri,
    /// camgöbeği tel küre (bu sınıf) <b>SO'nun gerçekte ne dediği</b>, yani oyunun kullandığı yer.
    /// İkisi üst üste oturmuyorsa <b>SO'ya henüz yazmadın</b>
    /// (<c>Tools &gt; VortexArena &gt; Weapons &gt; Write Grip Sockets To Definition</c>). İki temsilin sapması
    /// böylece gözle görünür bir kontrole dönüşür — sessiz bir işaret/uzay hatası olarak kalmaz.
    /// </para>
    /// </summary>
    public class ItemGripSockets : MonoBehaviour, IGameObjectFilter
    {
        // İki yarıçap = kullanıcı tarifinin birebir karşılığı: "yaklaşınca belirginleşecek" (hover)
        // + "elini oraya götürmesini bekleyecek" (grab). Tek yarıçap olsaydı soket ancak
        // kavranabildiği anda görünür, yani oyuncuyu hiç YÖNLENDİRMEZDİ.

        /// <summary>
        /// Soketin GÖRÜNÜR olduğu mesafe (m) — playtest değeri, TÜM eşyalarda aynı.
        /// <para>⚠️ Kavrama yarıçapı ise eşya başınadır (<see cref="ItemDefinition.PrimaryGripRadius"/>)
        /// ve bu sabiti AŞABİLİR. O yüzden etkin hover her yerde
        /// <c>Mathf.Max(HoverRadius, socketRadius)</c> ile hesaplanır: yarıçap hover'ı geçerse
        /// "önce ipucu görünür, sonra kavranır" sırası tersine döner — oyuncu soketi hiç GÖRMEDEN
        /// kavramış olur ve gösterge işlevsiz kalır.</para>
        /// </summary>
        private const float HoverRadius = 0.30f;

        /// <summary>Prosedürel halka yedeğinin yarıçapı (m) — playtest değeri.</summary>
        private const float RingRadius = 0.035f;

        /// <summary>Halka köşe sayısı (yumuşaklık ↔ vertex bütçesi).</summary>
        private const int RingSegments = 20;

        /// <summary>Halka çizgisinin kalınlığı (m).</summary>
        private const float RingWidth = 0.004f;

        /// <summary>Hover durumunda alfa: soket "burada bir yer var" der, "şimdi bas" demez.</summary>
        private const float HoverAlpha = 0.35f;

        /// <summary>Ready durumunda ölçek: büyüme "şimdi bas" okumasını gözle ayırır.</summary>
        private const float ReadyScale = 1.35f;

        /// <summary>Hover rengi (mavi): "burada bir yer var".</summary>
        private static readonly Color HoverColor = new Color(0.45f, 0.85f, 1f, 1f);

        /// <summary>Ready rengi (yeşil): "şimdi bas". ⚠️ Renk ayrımı alfa/ölçek farkının
        /// ÜSTÜNE gelir, onun yerine değil — VR'da yalnız parlaklık değişimi kabul mesafesine
        /// girildiğini yeterince okutmuyor.</summary>
        private static readonly Color ReadyColor = new Color(0.35f, 0.95f, 0.45f, 1f);

        /// <summary>Halka materyalinin shader arama zinciri (ilk bulunan kullanılır).</summary>
        // ⚠️ Zincir ShotTracer'daki ile BİREBİR aynı ve "Sprites/Default" başta duruyor: o shader
        // Graphics Settings'in *Always Included Shaders* listesinde varsayılan olarak bulunur →
        // build'de kesin paketlenir (çalışma anında Shader.Find ile bulunan, hiçbir materyalde
        // referanslanmayan shader STRIPLENİR ve gösterge sahada sessizce çizilmez). Vertex rengini
        // çarptığı için LineRenderer.startColor de işler.
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        // Halka köşeleri TÜM örnekler için bir kez üretilir: kare başına dizi ayırmak Quest'te
        // doğrudan GC dikeni demek (sahnedeki her silahta bir bileşen var).
        private static Vector3[] _ringPoints;

        // Fail-open uyarısı OTURUM başına bir kez (örnek başına değil): sahnedeki her silahta bir
        // bileşen var, örnek başına olsaydı aynı teşhis satırı onlarca kez düşerdi.
        private static bool _warnedFailOpen;

        [Tooltip("Weapon taşımayan eşyalar (bomba vb.) için tanım yedeği. Weapon varsa ONUN " +
                 "tanımı kazanır — ikinci bir doğruluk kaynağı olmasın.")]
        [SerializeField] private ItemDefinition definition;

        /// <summary>Bir soketin görsel örneği; tembel üretilir (sahnedeki silahlar bedavaya durur).</summary>
        private sealed class SocketVisual
        {
            public Transform Root;

            /// <summary>Prosedürel yedekte dolu; prefab yolunda null.</summary>
            public LineRenderer Line;

            /// <summary>Prefab yolunda rengi sürülecek materyal (yoksa yalnız ölçek/aktiflik değişir).</summary>
            public Material PrefabMaterial;
        }

        private Weapon _weapon;
        private bool _weaponProbed;
        private SocketVisual _primary;
        private SocketVisual _secondary;
        private Material _ringMaterial;
        private bool _warnedNoDefinition;
        private bool _warnedNoShader;

        // Bu karenin avuç pozları (Update'te bir kez çözülür; bkz. NearestOpenHandDistance).
        private Pose _leftPalm;
        private Pose _rightPalm;
        private bool _hasLeftPalm;
        private bool _hasRightPalm;

        /// <summary>
        /// Kullanılan tanım: <see cref="Weapon"/> varsa ONUN tanımı, yoksa serialize edilen yedek.
        /// Sıra bilinçli — silahta tanım zaten zorunlu, iki kaynak birbirinden sapardı.
        /// </summary>
        private ItemDefinition Definition
        {
            get
            {
                // ⚠️ Tembel arama, çünkü Awake EDİT KİPİNDE koşmaz ve gizmo edit kipinde çizilir:
                // _weapon null kalsaydı editörde tanım hiç bulunamaz, camgöbeği soket de hiç
                // görünmezdi (yani araç tam ihtiyaç duyulduğu anda sessiz kalırdı). Bir kez
                // arayıp önbelleğe alıyoruz: sonuç null olsa da (bomba gibi Weapon'suz eşya)
                // _weaponProbed sayesinde kare başına GetComponent yapılmaz.
                if (!_weaponProbed)
                {
                    _weaponProbed = true;
                    _weapon = GetComponent<Weapon>();
                }

                return _weapon != null && _weapon.Definition != null ? _weapon.Definition : definition;
            }
        }

        private void Awake()
        {
            _weapon = GetComponent<Weapon>();
            _weaponProbed = true;
        }

        private void OnDisable()
        {
            Hide(_primary);
            Hide(_secondary);
        }

        private void OnDestroy()
        {
            if (_ringMaterial != null)
            {
                Destroy(_ringMaterial);
                _ringMaterial = null;
            }
        }

        private void Update()
        {
            ItemDefinition def = Definition;
            if (def == null)
            {
                WarnNoDefinition();
                HideAll();
                return;
            }

            // ⚠️ Mesafe ANCHOR'dan değil AVUÇtan ölçülür (HandGripPivot): oyuncunun gördüğü şey
            // kumanda değil sentetik eldir, anchor'a göre ölçülen kabul mesafesi elin birkaç santim
            // ötesinde başlar ve soket "elimin içinde ama yeşile dönmüyor" diye okunur.
            _hasLeftPalm = WeaponGranter.TryResolvePalm(OVRInput.Controller.LTouch, out _leftPalm);
            _hasRightPalm = WeaponGranter.TryResolvePalm(OVRInput.Controller.RTouch, out _rightPalm);
            if (!_hasLeftPalm && !_hasRightPalm)
            {
                // Rig yok (admin gözlemci, editör oturumu, sahne henüz yüklenmemiş) — sessizce hiçbir şey.
                HideAll();
                return;
            }

            Vector3 primaryWorld = PrimarySocketWorld(def);
            Vector3 secondaryWorld = SecondarySocketWorld(def);

            // ⚠️ Verilen silahta ANA soket kendiliğinden gizlenir (IsSocketOpen: silah tutuluyor →
            // ana soket kapalı) ama İKİNCİL soket ÇİZİLİR: yoksa FFA'da eline tüfek verilen oyuncu
            // ön kabzayı nereden tutacağını hiç göremezdi.
            float primaryDistance = NearestOpenHandDistance(primaryWorld, false);
            float secondaryDistance = NearestOpenHandDistance(secondaryWorld, true);

            // Yarıçap eşya başına (SO'dan) gelir; kapı ile "ready" eşiği AYNI değeri kullanır —
            // ikisi ayrışsa oyuncuya "şimdi bas" diye büyütülen soket sessizce reddedilebilirdi.
            float primaryRadius = def.PrimaryGripRadius;
            float secondaryRadius = def.SecondaryGripRadius;

            if (primaryDistance > Mathf.Max(HoverRadius, primaryRadius) &&
                secondaryDistance > Mathf.Max(HoverRadius, secondaryRadius))
            {
                // Erken çıkış: uzaktaki eşyalar hiçbir görsel üretmez/güncellemez.
                HideAll();
                return;
            }

            _primary = UpdateSocket(_primary, "[GripSocket_Primary]", primaryWorld, primaryDistance, primaryRadius);
            _secondary = UpdateSocket(_secondary, "[GripSocket_Secondary]", secondaryWorld, secondaryDistance, secondaryRadius);
        }

        // ------------------------------------------------------------------ kapı

        /// <summary>
        /// ISDK kapısı: bu interactor şu an eşyayı seçebilir mi (bkz. sınıf notu).
        /// <para>
        /// ⚠️ <b>El çözülemezse FAIL-OPEN</b> (izin verilir). Bu bir emniyet kapısı değil bir HİS
        /// kapısıdır: editör oturumunda kontrolcü çözülemez ve fail-close olsaydı silah editörde
        /// hiç kavranamaz, yani sahne testi imkânsız hale gelirdi.
        /// </para>
        /// <para>
        /// ⚠️ <b>Kapı yalnız SAHNE silahını ilgilendirir.</b> VERİLEN silahta (verildi ya da
        /// çerçeveden çağrıldı) kavrama bileşenleri kapalıdır
        /// (<c>WeaponGranter.PrepareSummonedClone</c> / <c>DetachFromPhysicsAndGrab</c>), yani ISDK
        /// bu filtreyi hiç sormaz — orada ikinci eli granter'ın grip yoklaması çözer. Kural yine de
        /// SİLİNMEZ: kuralın kendisi (hangi soket kime açık) çizimle paylaşılıyor.
        /// </para>
        /// </summary>
        public bool Filter(GameObject interactorGameObject)
        {
            ItemDefinition def = Definition;
            if (def == null)
            {
                // Kapıyı kapatıp silahı hiç kavranamaz yapmaktan iyidir — tanım eksikliğini
                // Weapon.Awake zaten hata olarak basıyor.
                return true;
            }

            OVRInput.Controller hand = WeaponGranter.ResolveControllerFromGameObject(interactorGameObject);
            if (hand == OVRInput.Controller.None)
            {
                WarnFailOpen();
                return true;
            }

            if (!WeaponGranter.TryResolvePalm(hand, out Pose palm))
            {
                return true; // rig yok → kapı anlamsız
            }

            Vector3 handPosition = palm.position;

            // Kapı eşiği = SO'daki yarıçap (eşya başına): tabanca kabzasıyla tüfek ön kabzası aynı
            // büyüklükte değil, tek global sabit ikisinden birini yanlış boyutta bırakıyordu.
            float primaryRadius = def.PrimaryGripRadius;
            float secondaryRadius = def.SecondaryGripRadius;

            if (IsSocketOpen(false, hand) &&
                (handPosition - PrimarySocketWorld(def)).sqrMagnitude <= primaryRadius * primaryRadius)
            {
                return true;
            }

            if (IsSocketOpen(true, hand) &&
                (handPosition - SecondarySocketWorld(def)).sqrMagnitude <= secondaryRadius * secondaryRadius)
            {
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------- soket kuralı

        /// <summary>
        /// Soketin dünya konumu. ⚠️ <c>TransformPoint</c> DEĞİL: kavrama ofseti METRE cinsindendir
        /// ve transform ölçeğiyle büyütülmemeli (<c>Weapon.ApplyCanonicalGrip</c> ile
        /// <c>RemoteAvatar.ApplySecondaryGripSnap</c> de aynı sebeple elle bileşim yapıyor).
        /// </summary>
        private Vector3 PrimarySocketWorld(ItemDefinition def)
        {
            return transform.position + transform.rotation * def.PrimaryGripPointOnItem;
        }

        /// <summary>Ön kabza soketinin dünya konumu (bkz. <see cref="PrimarySocketWorld"/> uyarısı).</summary>
        private Vector3 SecondarySocketWorld(ItemDefinition def)
        {
            return transform.position + transform.rotation * def.SecondaryGripPosition;
        }

        /// <summary>
        /// Soket o an bu ele AÇIK mı — <b>çizim ve kapı AYNI kuralı kullanır</b> (iki kural olsaydı
        /// oyuncuya "buradan tut" diye gösterilen bir soket sessizce reddedilebilirdi).
        /// <para>
        /// <b>Ana soket:</b> eşya tutulmuyorsa her iki ele açık, tutuluyorsa kapalı.
        /// <b>Ön kabza:</b> yalnız eşya tutuluyorsa, çift elliyse ve soran el ana el değilse açık.
        /// </para>
        /// <para>⚠️ "Önce ana soket" kuralı bilinçlidir: kanonik kavramada eşyanın pozu ANA elden
        /// türetilir (§6.6), dolayısıyla ön kabzadan BAŞLAYAN bir kavrayışın tanımı yoktur.</para>
        /// <para>⚠️ Tek elli eşyada (tabanca) ön kabza HİÇ açılmaz — bu aynı zamanda ikinci elin
        /// aynı tabancayı kavramasını engeller: engellenmezse <c>Weapon.IsTwoHanded</c> true olur,
        /// telde <c>GRIP_LINKED</c> yazılır ve uzak taraf boş eli sıfır olan <c>secondaryGrip</c>'e
        /// yapıştırıp iki eli üst üste bindirirdi.</para>
        /// <para>
        /// ⚠️ <b>VERİLEN silah için (Disposable/Persistent) EK KOD YOKTUR</b> ve gerekmez: verilen
        /// silah tanım gereği tutuluyordur, yani ana soket zaten kapalı, ön kabza ise ana el olmayan
        /// ele açık çıkar — "silah her zaman 1. soketten tutulur, 2. soket aynı şekilde işler"
        /// kuralı kendiliğinden karşılanır. Ayrı bir dal aynı kuralın ikinci bir kopyası olurdu.
        /// </para>
        /// </summary>
        private bool IsSocketOpen(bool secondary, OVRInput.Controller hand)
        {
            bool held = _weapon != null && _weapon.IsHeld;

            if (!secondary)
            {
                return !held;
            }

            ItemDefinition def = Definition;
            return held && def != null && def.IsTwoHanded &&
                   (_weapon == null || _weapon.MainHand != hand);
        }

        /// <summary>Sokete AÇIK olan avuçlar arasından en yakınının mesafesi; hiçbiri açık değilse
        /// sonsuz. Avuç pozları <see cref="Update"/>'te bir kez çözülür (kare başına dört rig
        /// aramasından kaçınmak için alanlarda tutulur).</summary>
        private float NearestOpenHandDistance(Vector3 socketWorld, bool secondary)
        {
            float best = float.PositiveInfinity;

            if (_hasLeftPalm && IsSocketOpen(secondary, OVRInput.Controller.LTouch))
            {
                best = Mathf.Min(best, Vector3.Distance(_leftPalm.position, socketWorld));
            }

            if (_hasRightPalm && IsSocketOpen(secondary, OVRInput.Controller.RTouch))
            {
                best = Mathf.Min(best, Vector3.Distance(_rightPalm.position, socketWorld));
            }

            return best;
        }

        // ----------------------------------------------------------------- görsel

        /// <summary>
        /// Soket görselini duruma göre sürer: kapalı (menzil dışı) → gizli, hover → sönük ve normal
        /// ölçek, ready (grip kabul mesafesi) → parlak ve büyük. Görsel yalnız ihtiyaç doğduğunda
        /// üretilir; döndürülen örnek çağıranın alanına yazılır.
        /// <para><paramref name="socketRadius"/> SO'dan gelir (eşya başına) ve "ready" eşiğidir —
        /// kapının kullandığı değerin AYNISI. Etkin hover ondan küçük olamaz (bkz.
        /// <see cref="HoverRadius"/>).</para>
        /// </summary>
        private SocketVisual UpdateSocket(SocketVisual visual, string nodeName, Vector3 world, float distance,
            float socketRadius)
        {
            if (distance > Mathf.Max(HoverRadius, socketRadius))
            {
                Hide(visual);
                return visual;
            }

            visual = visual ?? CreateVisual(nodeName);
            if (visual == null || visual.Root == null)
            {
                return null;
            }

            bool ready = distance <= socketRadius;

            visual.Root.gameObject.SetActive(true);
            visual.Root.SetPositionAndRotation(world, SocketRotation(visual));
            visual.Root.localScale = Vector3.one * (ready ? ReadyScale : 1f);

            Color color = ready ? ReadyColor : HoverColor;
            color.a = ready ? 1f : HoverAlpha;

            if (visual.Line != null)
            {
                visual.Line.startColor = color;
                visual.Line.endColor = color;
            }
            else if (visual.PrefabMaterial != null)
            {
                visual.PrefabMaterial.color = color;
            }

            return visual;
        }

        /// <summary>
        /// Göstergenin dünya dönüşü — <b>iki yol bilerek farklıdır</b>:
        /// <para>
        /// <b>Prosedürel halka KAMERAYA çevrilir.</b> Halka köşeleri yerel XY düzleminde duruyor,
        /// yani kök eşyanın dönüşünü alsaydı halkanın DÜZLEMİ de eşyaya bağlı kalır ve o düzleme
        /// kenarından bakan oyuncu halka yerine bir ÇİZGİ görürdü. ⚠️
        /// <see cref="LineAlignment.View"/> bunu çözmez: o yalnız şeridin KALINLIĞINI kameraya
        /// çevirir, geometrinin düzlemini döndürmez.
        /// </para>
        /// <para>
        /// <b>Prefab yolunda EŞYANIN dönüşü kullanılır:</b> tasarlanmış bir gösterge (ör. elin hangi
        /// açıyla gireceğini gösteren bir işaret) yönünü eşyadan almalıdır — kameraya çevrilse
        /// anlattığı şey yalan olurdu. Yani yön kararı "düz bir grafik mi, yoksa uzamsal bir
        /// işaret mi" ayrımından geliyor, keyfi değil.
        /// </para>
        /// </summary>
        private Quaternion SocketRotation(SocketVisual visual)
        {
            if (visual.Line == null)
            {
                return transform.rotation;
            }

            Camera camera = Camera.main;
            return camera != null
                ? Quaternion.LookRotation(camera.transform.forward, camera.transform.up)
                : transform.rotation;
        }

        private static void Hide(SocketVisual visual)
        {
            if (visual != null && visual.Root != null && visual.Root.gameObject.activeSelf)
            {
                visual.Root.gameObject.SetActive(false);
            }
        }

        private void HideAll()
        {
            Hide(_primary);
            Hide(_secondary);
        }

        /// <summary>
        /// Görsel örneğini üretir: katalogda prefab varsa o, yoksa prosedürel halka yedeği
        /// (aynı desen <c>RemoteShotFx.CreateNode</c>'da var — sunum prefabı eksik olduğunda
        /// sistem sessizce görünmez kalmasın).
        /// </summary>
        private SocketVisual CreateVisual(string nodeName)
        {
            WeaponCatalog catalog = WeaponCatalog.Load();
            GameObject prefab = catalog != null ? catalog.GripSocketPrefab : null;

            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab, transform);
                instance.name = nodeName;
                StripPhysics(instance);

                var visual = new SocketVisual { Root = instance.transform };

                // Materyal örneği BİR KEZ burada alınır (Update'te .material çağırmak kare başına
                // yeni bir materyal örneği demek olurdu). Renk özelliği yoksa sessizce geçilir:
                // gösterge yalnız ölçek/aktiflikle konuşur.
                var renderer = instance.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    Material material = renderer.material;
                    if (material != null && (material.HasProperty("_BaseColor") || material.HasProperty("_Color")))
                    {
                        visual.PrefabMaterial = material;
                    }
                }

                return visual;
            }

            Material ringMaterial = EnsureRingMaterial();
            if (ringMaterial == null)
            {
                return null;
            }

            var go = new GameObject(nodeName);
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = ringMaterial;
            // Yerel uzay + loop: halka köşeleri bir kez yazılır, sonra yalnız kök taşınır.
            line.useWorldSpace = false;
            line.loop = true;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            // Şeridin KALINLIĞI kameraya baksın (çizgi ince bir bant, yandan yok olmasın).
            // ⚠️ Halkanın kenarından bakılması bununla ÇÖZÜLMEZ — onu kökün kameraya
            // çevrilmesi çözüyor (bkz. SocketRotation).
            line.alignment = LineAlignment.View;
            line.startWidth = RingWidth;
            line.endWidth = RingWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Vector3[] points = EnsureRingPoints();
            line.positionCount = points.Length;
            line.SetPositions(points);

            return new SocketVisual { Root = go.transform, Line = line };
        }

        /// <summary>
        /// Göstergeden fizik/çarpışma söker. ⚠️ Collider bırakılırsa gösterge hem ateş ışınına hem
        /// kavramaya takılır — yani oyuncuya yardım etmesi gereken şey nişanı ve kavramayı bozardı.
        /// </summary>
        private static void StripPhysics(GameObject instance)
        {
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }

            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Destroy(bodies[i]);
            }
        }

        /// <summary>Halka köşeleri (XY düzleminde, yerel) — tüm örnekler için bir kez üretilir.</summary>
        private static Vector3[] EnsureRingPoints()
        {
            if (_ringPoints != null)
            {
                return _ringPoints;
            }

            var points = new Vector3[RingSegments];
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                points[i] = new Vector3(Mathf.Cos(angle) * RingRadius, Mathf.Sin(angle) * RingRadius, 0f);
            }

            _ringPoints = points;
            return _ringPoints;
        }

        /// <summary>Halka materyali (renk LineRenderer'ın vertex renginden gelir).</summary>
        private Material EnsureRingMaterial()
        {
            if (_ringMaterial != null)
            {
                return _ringMaterial;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _ringMaterial = new Material(shader) { name = "M_GripSocket(runtime)" };
                    return _ringMaterial;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[ItemGripSockets] Soket halkası için shader bulunamadı (Sprites/Default dahil) — " +
                    "kavrama soketi çizilmeyecek (kavrama kapısı çalışmaya devam eder). Graphics " +
                    "Settings > Always Included Shaders listesini kontrol et.", this);
            }

            return null;
        }

        /// <summary>
        /// Fail-open bir kez loglanır. <b>Neden loglanıyor:</b> el çözülemediğinde soket kapısı
        /// tümüyle devre dışı kalır ve silah her yerden kavranır — yani özellik <i>çalışıyor gibi
        /// görünüp</i> hiçbir şey yapmaz. Sessiz bırakılsa sahada teşhis edilemezdi (kapının kendisi
        /// hata basmaz, çünkü izin vermek onun güvenli tarafı).
        /// <para>Editör oturumunda BEKLENEN durumdur (kumanda yok); başlıkta görülüyorsa BB
        /// kontrolcü rig'inin <c>InteractorControllerDecorator</c> kurulumu eksiktir.</para>
        /// </summary>
        private static void WarnFailOpen()
        {
            if (_warnedFailOpen)
            {
                return;
            }

            _warnedFailOpen = true;
            Debug.LogWarning("[ItemGripSockets] Interactor'dan el çözülemedi — kavrama soketi kapısı " +
                             "AÇIK bırakıldı (silah her noktadan kavranabilir). Editörde normaldir; " +
                             "başlıkta görülüyorsa rig'in InteractorControllerDecorator kurulumuna bak.");
        }

        private void WarnNoDefinition()
        {
            if (_warnedNoDefinition)
            {
                return;
            }

            _warnedNoDefinition = true;
            Debug.LogWarning($"[ItemGripSockets] '{name}' için tanım yok (ne Weapon.definition ne yedek " +
                             "alan) — soket çizilmez ve kavrama kapısı herkese AÇIK kalır.", this);
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>
        /// SO'nun GERÇEKTE söylediği soketleri çizer (camgöbeği TEL küre) — kavrama ayarlanırken
        /// <see cref="GripSocketMarker"/>'ın sarı DOLU küresiyle karşılaştırılacak referans budur
        /// (renk ayrımının gerekçesi sınıf notunda).
        /// <para>Edit kipinde de çalışır: tanım <see cref="Definition"/>'ın tembel aramasından
        /// gelir (<c>Awake</c> edit kipinde koşmaz).</para>
        /// </summary>
        private void OnDrawGizmos()
        {
            ItemDefinition def = Definition;
            if (def == null)
            {
                // Gizmo'da UYARI BASILMAZ: OnDrawGizmos kare başına çağrılır, uyarı konsolu boğardı
                // (eksik tanımı Weapon.Awake ve WarnNoDefinition zaten bildiriyor).
                return;
            }

            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(PrimarySocketWorld(def), def.PrimaryGripRadius);

            // Tek elli eşyada ön kabza soketi HİÇ açılmaz (bkz. IsSocketOpen) — çizmek de yanlış
            // olurdu: sıfır olan secondaryGrip yüzünden kabzanın üstünde hayalet bir küre görünürdü.
            if (def.IsTwoHanded)
            {
                Gizmos.DrawWireSphere(SecondarySocketWorld(def), def.SecondaryGripRadius);
            }
        }
    }
}
