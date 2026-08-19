using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Elde tutulabilen her şeyin (silah, bomba) ortak tabanı.
    /// <para>
    /// <b>Bu taban bilinçli olarak DARDIR: içine yalnız ağın ve uzak çizimin ihtiyacı girer</b>
    /// (<c>netItemId</c>, prefab, kaç elle tutulduğu, kavrama pozları). Hasar/şarjör/menzil/fitil
    /// gibi <i>davranış</i> alanları buraya GİRMEZ — onlar türetilmiş sınıfta yaşar
    /// (<see cref="WeaponDefinition"/>). Gerekçe: <c>RemoteAvatar</c> uzak oyuncunun elindeki
    /// eşyayı, o eşyanın ne YAPTIĞINI bilmeden çizer. Bu, Net katmanının "oyun bilgisi içermez"
    /// ilkesinin sunumdaki karşılığıdır; taban genişlerse uzak çizim yolu farkında olmadan oyun
    /// kurallarına bağımlı hale gelir.
    /// </para>
    /// <para>
    /// <b>Duruş telde gitmez</b> (Docs/ArenaNet-Protokol.md §6.6): eşyanın ele göre konumu buradaki
    /// kavrama alanlarından, yani her istemcinin APK'sından gelir; dönüşü her zaman ana kumandanın
    /// dönüşüdür (<see cref="ItemGripSolver"/>). Ön koşulu kanonik kavramadır — serbest kavrama
    /// (keyfi ofset) uzak tarafta yanlış duruş demektir.
    /// </para>
    /// </summary>
    public abstract class ItemDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [SerializeField] private string displayName = "";

        // ⚠️ [Range(1,255)] KOYULMAZ. Unity'nin Range drawer'ı IntSlider ile çizerken 0'ı sessizce
        // min'e (1) clamp'ler VE asset'i dirty yapar — Inspector'da açılan her tanım netItemId=1
        // olurdu, yani tüm silahlar birbiriyle çakışırdı. Sınır denetimi HasNetItemId'de ve asıl
        // koruma editör bekçisindedir (her Configure All Build Elements eşitlemesinde koşar).
        [Tooltip("Ağ kimliği (1-255; 0 = atanmamış). Snapshot'ta bu bayt gider; katalog dizi " +
                 "indeksi DEĞİLDİR — elle, kararlı verilir. Çakışmayı bekçi yakalar.")]
        [SerializeField] private int netItemId = 0;

        [Header("Sunum")]
        [Tooltip("Eşya prefabı (loadout kurulumları + uzak çizim için).")]
        [SerializeField] private GameObject prefab;

        [Tooltip("OneHand (tabanca/bomba) / TwoHand (tüfek).")]
        [SerializeField] private ItemHoldMode holdMode = ItemHoldMode.OneHand;

        // ⚠️ DÖRT KAYIT DA AYNI UZAYDADIR: her biri elin KUMANDA ANCHOR'ININ EŞYAYA göre yerel
        // KONUMUDUR (eşya → anchor; ItemGripPose — anchor kaydının dönüşü yoktur, silah her zaman
        // kumandayla hizalıdır). Tek yönde yazıldıkları için ikinci bir uzay tarif etmek yalnız
        // işaret hatası üretirdi. Anchor = telde giden el pozu = çözücünün bildiği poz, yani hiçbir
        // okuyucu delta ölçmek zorunda değildir.
        // ⚠️ Kaydın İKİNCİ yarısı elin GÖRSELİDİR (bilek yerleşimi + parmak rigi) ve eşyanın pozuna
        // hiç karışmaz: el silaha göre yan/alttan durabilirken silah kumandayla hizalı kalır.
        // ⚠️ Kayıt EL BAŞINADIR: kabza simetrik olmadığı için iki elin kumandası eşyanın farklı
        // yerlerine düşer — tek kayıt tutup aynalamak sol eli silahın içine sokardı.
        // ⚠️ Kayıtlar stüdyoda yazılır (editör), gözlükle yakalanmaz.
        // Buradaki YARIÇAPLAR duruşun parçası DEĞİL, KAPI ölçüsüdür: kavramanın nerede kabul
        // edildiğini söylerler, eşyanın elde nasıl duracağını değil.
        [Header("Kavrama (kanonik)")]
        [Tooltip("SAĞ elin kumanda anchor'ının ana kabzadaki pozu (eşyaya göre yerel) + riglenmiş parmak duruşu.")]
        [SerializeField] private ItemGripPose primaryGripRight;

        [Tooltip("SOL elin kumanda anchor'ının ana kabzadaki pozu (eşyaya göre yerel) + riglenmiş parmak duruşu.")]
        [SerializeField] private ItemGripPose primaryGripLeft;

        [Tooltip("SAĞ elin kumanda anchor'ının ön kabzadaki pozu — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private ItemGripPose secondaryGripRight;

        [Tooltip("SOL elin kumanda anchor'ının ön kabzadaki pozu — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private ItemGripPose secondaryGripLeft;

        // ⚠️ [Range] KOYULMAZ — dosyanın başındaki netItemId uyarısındaki tuzağın aynısı: Range
        // drawer'ı değeri kendi varsayılan sınırlarına sessizce clamp'ler VE asset'i dirty yapar,
        // yani Inspector'da açılan her tanım farkında olmadan başka bir yarıçapla commit'lenir.
        // Alt sınır property'de uygulanıyor.
        // ⚠️ ANA kabza için yarıçap YOKTUR: silah ana ele verilerek/çağrılarak geliyor, oyuncunun
        // elini ana kabzaya götürmesi diye bir adım yok — okuyanı olmayan ölçü bayatlar.
        [Tooltip("Ön kabza SOKETİNİN yarıçapı (m): boş elin kumanda anchor'ı bu kürenin içindeyken grip'e " +
                 "basılınca ikinci el ön kabzaya bağlanır; oyuncunun gördüğü küre de tam bu yarıçapla " +
                 "çizilir (0.10 = 20 cm çap). Yalnız TwoHand'de anlamlı. Silah başına ayarlanır.")]
        [SerializeField] private float secondaryGripRadius = 0.10f;

        // ⚠️ Parmak duruşu için AYRI bir alan YOKTUR ve açılmaz: duruş kavrama kaydının PARÇASIDIR
        // (ItemGripPose.fingerJoints), yani slot başına yaşar. Ayrı bir alan olsaydı "bu elin pozu"
        // ile "bu elin parmakları" iki ayrı yerde durur ve biri güncellenip öteki unutulurdu — oysa
        // ön kabzayı saran el ile tetiği tutan el tanım gereği farklı duruştadır.

        // ⚠️ Slot başına ÖNBELLEK ([kind, el] = 4 giriş): riglenmiş duruşun ISDK eklem dizisine
        // çevrilmesi de humanoid el için kapanma oranına indirgenmesi de tahsis eder ve iki yol da
        // KARE BAŞINA okunuyor (HandGripPoser / RemoteHandPoser). Serialize EDİLMEZ: türetilmiş
        // veridir, asset'te ikinci bir kopyası olsaydı rig değişip önbellek unutulduğunda oyunda
        // eski duruş çizilirdi. Editörde yazma kapıları önbelleği düşürür (InvalidateGripCache).
        [NonSerialized] private Quaternion[][] _gripJointCache;
        [NonSerialized] private HandPoseProfile[] _gripCurlCache;
        [NonSerialized] private bool[] _gripCurlResolved;

        // Tracer görünümü tabanda durur çünkü tabanın ölçütü "ağın + UZAK ÇİZİMİN ihtiyacı"dır
        // ve tracer tam olarak uzak çizim verisidir: uzak atışı çizen taraf (RemoteShotFx) olayın
        // itemId'sinden başka hiçbir şey bilmez — hasarı, şarjörü, menzili bilmeden mermi izini
        // çizmek zorundadır. Davranış alanı DEĞİL, sunum parametresi.
        [Header("Tracer (uzak sunum)")]
        [Tooltip("Mermi izinin rengi (alfa dahil).")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.4f, 0.9f);

        [Tooltip("Mermi izinin kalınlığı (metre).")]
        [SerializeField] private float tracerWidth = 0.02f;

        // ⚠️ Bu süre "iz ne kadar TAM PARLAK durur" değil, "doğuşundan tümden kaybolmasına kadar
        // geçen süre"dir: çizgi ömrü boyunca sönerek gider (ShotTracer.FadeAlphaAt). Ayrı bir
        // "sönme süresi" alanı bilerek YOK — iki sayı olsaydı hangisinin diğerini kestiği
        // (sönme ömürden uzunsa iz yarıda kesilir) sessiz bir tuzak olurdu.
        [Tooltip("Mermi izinin doğuşundan tümden sönmesine kadar geçen süre (saniye). " +
                 "İz bu süre boyunca sönerek kaybolur, sonunda pat diye kapanmaz.")]
        [SerializeField] private float tracerLifetime = 0.1f;

        // ⚠️ HER MERMİYE TRACER ÇİZİLMEZ. İki katlı gerekçe:
        // (a) Gerçek silahlarda da öyle değildir — her mermide çizmek lazer ışını gibi durur ve
        //     atıcının konumunu gereğinden fazla ifşa eder.
        // (b) Bütçe: tam ateşte 16 oyuncu ~160 atış/sn üretir, üçte biri ~53/sn. Asıl maliyet
        //     BAYT değil GC/draw call — telde zaten olay başına 9 B gidiyor, pahalı olan çizim.
        [Tooltip("Kaçta bir mermiye tracer çizilir. 1 = her mermi, 0/negatif = tracer kapalı.")]
        [SerializeField] private int tracerEveryNthRound = 3;

        /// <summary>Arayüzde gösterilen ad.</summary>
        public string DisplayName => displayName;

        /// <summary>
        /// Telde giden eşya kimliği (§6.6). <c>0</c> "el boş" için REZERVEDİR, yani atanmamış
        /// bir tanım geçersizdir — <see cref="HasNetItemId"/> ile kontrol edilir.
        /// </summary>
        public byte NetItemId => (byte)netItemId;

        /// <summary>
        /// Kimlik atanmış mı. ⚠️ Asıl korumayı bu property DEĞİL editör bekçisi sağlar
        /// (<c>Configure All Build Elements</c> eşitlemesinin net eşya kataloğu koşusu): çakışan/eksik id derlemede
        /// patlamaz, sahada "elinde yanlış eşya çizildi" olarak görünür.
        /// </summary>
        public bool HasNetItemId => netItemId >= 1 && netItemId <= 255;

        /// <summary>Eşya prefabı (atanmamış olabilir).</summary>
        public GameObject Prefab => prefab;

        /// <summary>Kaç elle tutulduğu.</summary>
        public ItemHoldMode HoldMode => holdMode;

        /// <summary>Çift elli mi (kısayol).</summary>
        public bool IsTwoHanded => holdMode == ItemHoldMode.TwoHand;

        /// <summary>
        /// İstenen kavrama noktasının, istenen elin <b>yazılmış</b> kaydı.
        /// <para>⚠️ İstenen el yazılmamışsa <b>ÖTEKİ elin kaydına düşülür</b> (ikisi de yoksa
        /// <c>default</c>): tek eli yazılmış bir silah, öteki elde silahı orijine yapıştırmak
        /// yerine yaklaşık ama makul bir duruşta tutulsun. ⚠️ Düşme yalnız <b>okuma</b> içindir —
        /// "yazılmış mı" sorusunun cevabı <see cref="HasGrip"/>'tir ve o DÜŞMEZ, yoksa eksik el
        /// hiçbir raporda görünmezdi.</para>
        /// </summary>
        public ItemGripPose GetGrip(GripSocketKind kind, bool rightHand)
        {
            bool secondary = kind == GripSocketKind.Secondary;
            ItemGripPose own = secondary
                ? (rightHand ? secondaryGripRight : secondaryGripLeft)
                : (rightHand ? primaryGripRight : primaryGripLeft);

            if (own.IsAuthored)
            {
                return own;
            }

            ItemGripPose other = secondary
                ? (rightHand ? secondaryGripLeft : secondaryGripRight)
                : (rightHand ? primaryGripLeft : primaryGripRight);

            return other.IsAuthored ? other : default;
        }

        /// <summary>Bu kavrama noktası <b>bu el için</b> yazılmış mı (öteki ele DÜŞMEZ —
        /// gerekçe <see cref="GetGrip"/>'te).</summary>
        public bool HasGrip(GripSocketKind kind, bool rightHand)
        {
            if (kind == GripSocketKind.Secondary)
            {
                return (rightHand ? secondaryGripRight : secondaryGripLeft).IsAuthored;
            }

            return (rightHand ? primaryGripRight : primaryGripLeft).IsAuthored;
        }

        /// <summary>
        /// Bu slotun riglenmiş parmak duruşu, <b>yerel sentetik elin</b> (ISDK) beklediği eklem
        /// dizisi olarak — <c>SyntheticHand.OverrideAllJoints</c> biçiminde. Parmakları riglenmemiş
        /// bir slot boş elin dizisine düşer.
        /// <para>Duruş kaydın PARÇASIDIR: ana kabzayı tutan el tetikte, ön kabzayı saran el
        /// kapalıdır — ikisi tek bir "eşyanın duruşu" alanına sığmaz.</para>
        /// <para>⚠️ Dönen dizi <b>ÖNBELLEKLİ ve PAYLAŞIMLIDIR</b> (kare başına okunuyor): çağıran
        /// onu DEĞİŞTİRMEZ, yalnız okur.</para>
        /// </summary>
        public Quaternion[] GripJointRotations(GripSocketKind kind, bool rightHand)
        {
            _gripJointCache ??= new Quaternion[4][];

            int slot = GripSlot(kind, rightHand);
            Quaternion[] cached = _gripJointCache[slot];
            if (cached != null)
            {
                return cached;
            }

            ItemGripPose grip = GetGrip(kind, rightHand);
            cached = grip.HasFingers
                ? HandPoseLibrary.BuildJointRotations(grip.fingerJoints, rightHand)
                : HandPoseLibrary.IdleJointRotations(rightHand);

            _gripJointCache[slot] = cached;
            return cached;
        }

        /// <summary>
        /// Aynı slotun <b>uzak avatarın humanoid (Mixamo) eli</b> için karşılığı: parmak başına
        /// kapanma oranı, riglenmiş duruştan ÖLÇÜLEREK
        /// (<see cref="HandPoseLibrary.MeasureCurl"/>).
        /// <para>⚠️ Ham eklem dönüşleri humanoid kemiğe yazılamaz (iki iskeletin eksenleri aynı
        /// değil — projenin bir kez öğrendiği kural); köprü bu orandır. Oran <b>asset'te
        /// saklanmaz</b>: türetilmiş veri ikinci bir doğruluk kaynağı olurdu.</para>
        /// </summary>
        public HandPoseProfile GripFingerCurl(GripSocketKind kind, bool rightHand)
        {
            _gripCurlCache ??= new HandPoseProfile[4];
            _gripCurlResolved ??= new bool[4];

            int slot = GripSlot(kind, rightHand);
            if (_gripCurlResolved[slot])
            {
                return _gripCurlCache[slot];
            }

            ItemGripPose grip = GetGrip(kind, rightHand);
            HandPoseProfile profile = grip.HasFingers
                ? HandPoseLibrary.MeasureCurl(grip.fingerJoints, rightHand)
                : HandPoseProfile.Idle;

            _gripCurlCache[slot] = profile;
            _gripCurlResolved[slot] = true;
            return profile;
        }

        /// <summary>Önbellek yeri: [kavrama noktası, el] → <c>0..3</c>.</summary>
        private static int GripSlot(GripSocketKind kind, bool rightHand)
        {
            return (kind == GripSocketKind.Secondary ? 2 : 0) + (rightHand ? 1 : 0);
        }

        /// <summary>
        /// Türetilmiş parmak önbelleklerini düşürür — kayıt her değiştiğinde çağrılır.
        /// <para>⚠️ Dört slotun tamamı düşer: bir elin kaydı silinince öteki el ONA düşebiliyor
        /// (<see cref="GetGrip"/>), yani tek slotu tazelemek komşusunu bayat bırakırdı.</para>
        /// </summary>
        private void InvalidateGripCache()
        {
            _gripJointCache = null;
            _gripCurlCache = null;
            _gripCurlResolved = null;
        }

        /// <summary>
        /// <b>EŞYANIN</b> ana el anchor'ına göre yerel konumu (metre): <c>itemPos =
        /// palm.pos + palm.rot * bu değer</c>; dönüş her zaman anchor'ın kendisidir
        /// (<see cref="ItemGripSolver"/>).
        /// <para>Türetme: kayıt anchor'ın eşyaya göre konumudur, aranan da onun tersidir (eksi işaret —
        /// eşya kumandayla hizalı olduğu için başka dönüşüm yok). Yazılmamış kayıtta sıfır: eşya
        /// kumandanın tam üstünde durur.</para>
        /// </summary>
        public Vector3 PrimaryGripPosition(bool rightHand)
        {
            return -GetGrip(GripSocketKind.Primary, rightHand).position;
        }

        /// <summary>
        /// Ana kavrama noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — uzak elin anchor hedefi
        /// (<c>RemoteAvatar.TryResolveGripPalm</c>) bunu okur.
        /// <para>⚠️ <b>Ayrı bir alan DEĞİL, kaydın kendisidir:</b> kayıt zaten "kumanda eşyanın
        /// neresinde" sorusunu cevaplıyor. İkinci bir serialize alan açılırsa aynı nokta iki yerde
        /// yaşar ve biri güncellenip diğeri unutulur.</para>
        /// </summary>
        public Vector3 PrimaryGripPointOnItem(bool rightHand)
        {
            return GetGrip(GripSocketKind.Primary, rightHand).position;
        }

        /// <summary>
        /// Ön kabza kaydı <b>en az bir el için yazılmış</b> mı (yalnız <see cref="IsTwoHanded"/> iken
        /// anlamlı; tek elli eşyada daima <c>false</c>). Ön kabzayı okuyan HER yol
        /// (<c>Weapon</c> soketi ve kapısı, <c>HandGripPoser</c>, <c>RemoteAvatar</c>) önce buna bakar.
        /// <para>⚠️ <b>Yazılmamış ön kabza EŞYANIN KÖKÜDÜR:</b> <see cref="GetGrip"/> iki el de yoksa
        /// <c>default</c> (sıfır poz) döner, yani <see cref="SecondaryGripPosition"/> kökü verir. O nokta
        /// çoğu silahta ana elin bileğinin dibinde durur — kapı burada açık kalsaydı soket küresi ana
        /// elin üstünde belirir, ikinci el "kabzada" tutamaz ve hata olarak değil "gösterge yanlış
        /// yerde çıkıyor" olarak görünürdü. Bu yüzden yazılmamış ön kabza <b>yoktur</b>: soket
        /// çizilmez, ikinci el bağlanmaz ve <c>Weapon</c> bunu bir kez uyarır.
        /// Kaydı yazan tek yer stüdyodur (<c>Kavrama Pozu Stüdyosu</c>).</para>
        /// </summary>
        public bool HasSecondaryGrip =>
            IsTwoHanded && (secondaryGripRight.IsAuthored || secondaryGripLeft.IsAuthored);

        /// <summary>
        /// Ön kabza noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — yalnız
        /// <see cref="HasSecondaryGrip"/> iken anlamlı (yazılmamışsa sıfır = eşyanın kökü döner;
        /// çağıran önce o kapıya bakar). İkinci elin dünya hedefi
        /// <c>item.position + item.rotation * bu değer</c> ile bulunur (⚠️ <c>TransformPoint</c>
        /// DEĞİL: ölçü metredir, eşyanın görsel ölçeğiyle büyümez).
        /// </summary>
        public Vector3 SecondaryGripPosition(bool rightHand)
        {
            return GetGrip(GripSocketKind.Secondary, rightHand).position;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Kavramayı ilgili alana yazar — <b>stüdyonun tek yazma kapısı</b> (alanlar private kalsın
        /// diye vardır; ikinci bir yazıcı, dört alanın hangisinin hangi el olduğunu ikinci kez
        /// tarif etmek olurdu).
        /// <para>⚠️ <c>EditorUtility.SetDirty</c>/<c>SaveAssets</c> ÇAĞRILMAZ: çağıran genelde
        /// birden çok alanı arka arkaya yazıyor ve kaydı tek Undo/tek dirty adımında toplamak
        /// istiyor.</para>
        /// </summary>
        /// <param name="anchorInItem">Kumanda anchor'ının EŞYAYA göre yerel konumu (metre, ölçeksiz).</param>
        /// <param name="wristInAnchor">El modelinin kumanda anchor'ına göre yerel pozu (metre,
        /// ölçeksiz) — elin silaha göre yan/alttan durmasını bu taşır.</param>
        /// <param name="fingerJoints">O slotta riglenmiş parmak eklemleri (boş olabilir — el o zaman
        /// boşta duruşunda kalır).</param>
        public void EditorSetGrip(GripSocketKind kind, bool rightHand, in Vector3 anchorInItem,
            in Pose wristInAnchor, HandJointRotation[] fingerJoints)
        {
            ItemGripPose capture = ItemGripPose.From(anchorInItem, wristInAnchor, fingerJoints);
            InvalidateGripCache();

            if (kind == GripSocketKind.Secondary)
            {
                if (rightHand)
                {
                    secondaryGripRight = capture;
                }
                else
                {
                    secondaryGripLeft = capture;
                }

                return;
            }

            if (rightHand)
            {
                primaryGripRight = capture;
            }
            else
            {
                primaryGripLeft = capture;
            }
        }

        /// <summary>
        /// Bir kavrama kaydını <b>yazılmamış</b> hale döndürür (<c>authored = false</c>) —
        /// <see cref="EditorSetGrip"/> ile aynı kapının silme yönü.
        /// <para>⚠️ Alanı sıfır poza çekmek YETMEZ: sıfır poz geçerli bir kavramadır
        /// (<see cref="ItemGripPose"/>), yani "hepsi sıfır = yazılmamış" kestirmesi burada
        /// sessizce yanlış olurdu — bayrağın kendisi düşürülür ki okuma yolu öteki elin kaydına
        /// düşebilsin ve araçlar eksik kavramayı raporlayabilsin.</para>
        /// </summary>
        public void EditorClearGrip(GripSocketKind kind, bool rightHand)
        {
            ItemGripPose empty = default;
            InvalidateGripCache();

            if (kind == GripSocketKind.Secondary)
            {
                if (rightHand)
                {
                    secondaryGripRight = empty;
                }
                else
                {
                    secondaryGripLeft = empty;
                }

                return;
            }

            if (rightHand)
            {
                primaryGripRight = empty;
            }
            else
            {
                primaryGripLeft = empty;
            }
        }

        /// <summary>
        /// Inspector'dan (ya da bir Undo/Revert'ten) gelen her değişiklikte türetilmiş parmak
        /// önbelleklerini düşürür.
        /// <para>⚠️ Yazma kapıları önbelleği zaten düşürüyor; bu kapı onların ATLANDIĞI yolları
        /// kapatır (Undo, prefab revert, asset'i elle düzenleme). Kavrama alanları Inspector'da
        /// görünür olduğu için "yalnız stüdyo yazar" bir sözleşme değil, bir alışkanlıktır.</para>
        /// </summary>
        private void OnValidate()
        {
            InvalidateGripCache();
        }
#endif

        /// <summary>
        /// Ön kabza soketinin yarıçapı (metre): boş elin kumanda ANCHOR'I bu kürenin içindeyken grip basışı
        /// ikinci eli ön kabzaya bağlar (<c>Weapon.IsHandOnSecondaryGrip</c>) ve oyuncunun gördüğü
        /// soket küresi tam bu yarıçapla çizilir (görsel = kabul hacmi) — yalnız
        /// <see cref="IsTwoHanded"/> iken anlamlı.
        /// <para>⚠️ <b>Alt sınır 1 cm'dir ve öyle kalmalı:</b> sıfır (ya da eksi) yarıçap ön kabzayı
        /// matematiksel olarak kavranamaz yapar — sahada bu bir hata olarak DEĞİL "ikinci el
        /// tutmuyor" olarak görünür, yani teşhisi pahalı. Ayarlanmamış/sıfırlanmış bir asset bu
        /// sayede yine çalışır kalır.</para>
        /// </summary>
        public float SecondaryGripRadius => Mathf.Max(0.01f, secondaryGripRadius);

        /// <summary>Uzak atışta çizilen mermi izinin rengi.</summary>
        public Color TracerColor => tracerColor;

        /// <summary>Mermi izinin kalınlığı (metre).</summary>
        public float TracerWidth => tracerWidth;

        /// <summary>Mermi izinin ömrü (saniye).</summary>
        public float TracerLifetime => tracerLifetime;

        /// <summary>
        /// Kaçta bir mermiye tracer çizilir (<c>1</c> = her mermi, <c>0</c>/negatif = kapalı).
        /// <para>⚠️ Bu bir <b>playtest ayarıdır</b> ve burada, SO'da yaşar: doğru sayı sahada
        /// gözle bulunur. Her mermide çizmek lazer ışını gibi durur, konumu fazla ifşa eder ve
        /// yoğun ateşte çizim/GC bütçesini yer (asıl maliyet bayt değil draw call).</para>
        /// </summary>
        public int TracerEveryNthRound => tracerEveryNthRound;
    }
}
