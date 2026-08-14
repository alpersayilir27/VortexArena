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
    /// <b>Duruş telde gitmez</b> (Docs/ArenaNet-Protokol.md §6.6): eşyanın ele göre konumu/dönüşü
    /// buradaki kavrama alanlarından, yani her istemcinin APK'sından gelir. Ön koşulu kanonik
    /// kavramadır — serbest kavrama (keyfi ofset) uzak tarafta yanlış duruş demektir.
    /// </para>
    /// </summary>
    public abstract class ItemDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [SerializeField] private string displayName = "";

        // ⚠️ [Range(1,255)] KOYULMAZ. Unity'nin Range drawer'ı IntSlider ile çizerken 0'ı sessizce
        // min'e (1) clamp'ler VE asset'i dirty yapar — Inspector'da açılan her tanım netItemId=1
        // olurdu, yani tüm silahlar birbiriyle çakışırdı. Sınır denetimi HasNetItemId'de ve asıl
        // koruma editör bekçisindedir (Tools > VortexArena > Weapons > Rebuild Net Item Catalog).
        [Tooltip("Ağ kimliği (1-255; 0 = atanmamış). Snapshot'ta bu bayt gider; katalog dizi " +
                 "indeksi DEĞİLDİR — elle, kararlı verilir. Çakışmayı bekçi yakalar.")]
        [SerializeField] private int netItemId = 0;

        [Header("Sunum")]
        [Tooltip("Eşya prefabı (loadout kurulumları + uzak çizim için).")]
        [SerializeField] private GameObject prefab;

        [Tooltip("OneHand (tabanca/bomba) / TwoHand (tüfek).")]
        [SerializeField] private ItemHoldMode holdMode = ItemHoldMode.OneHand;

        // ⚠️ DÖRT KAYIT DA AYNI UZAYDADIR: her biri elin (ISDK bileğinin) EŞYAYA göre yerel pozudur
        // (eşya → el). Eski "ana kavrama el → eşya, ikincil eşya → el" asimetrisi kalktı; yakalama
        // tek yönde ölçüldüğü için ikinci bir uzay tarif etmek yalnız işaret hatası üretirdi.
        // ⚠️ Kayıt EL BAŞINADIR: kabza simetrik olmadığı için iki elin bileği eşyanın farklı
        // yerlerine düşer — tek kayıt tutup aynalamak sol eli silahın içine sokardı.
        // Buradaki YARIÇAPLAR duruşun parçası DEĞİL, KAPI ölçüsüdür: kavramanın nerede kabul
        // edildiğini söylerler, eşyanın elde nasıl duracağını değil.
        [Header("Kavrama (kanonik)")]
        [Tooltip("SAĞ elin ana kabzadaki yakalanmış pozu (eşyaya göre yerel).")]
        [SerializeField] private ItemGripCapture primaryGripRight;

        [Tooltip("SOL elin ana kabzadaki yakalanmış pozu (eşyaya göre yerel).")]
        [SerializeField] private ItemGripCapture primaryGripLeft;

        [Tooltip("SAĞ elin ön kabzadaki yakalanmış pozu — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private ItemGripCapture secondaryGripRight;

        [Tooltip("SOL elin ön kabzadaki yakalanmış pozu — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private ItemGripCapture secondaryGripLeft;

        // ⚠️ [Range] KOYULMAZ — dosyanın başındaki netItemId uyarısındaki tuzağın aynısı: Range
        // drawer'ı değeri kendi varsayılan sınırlarına sessizce clamp'ler VE asset'i dirty yapar,
        // yani Inspector'da açılan her tanım farkında olmadan başka bir yarıçapla commit'lenir.
        // Alt sınır property'lerde uygulanıyor.
        [Tooltip("Ana kavrama soketinin yarıçapı (m): grip basışının KABUL edildiği mesafe. " +
                 "Silah başına ayarlanır — tabanca kabzası ile tüfek ön kabzası aynı büyüklükte değil.")]
        [SerializeField] private float primaryGripRadius = 0.12f;

        [Tooltip("Ön kabza soketinin yarıçapı (m) — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private float secondaryGripRadius = 0.12f;

        // ⚠️ Eşya başına PARMAK DURUŞU ALANI YOKTUR ve eklenmez: parmaklar artık authored bir veri
        // değil, izlemeden/kumandadan gelen canlı bir ölçüdür (yerelde SyntheticHand serbest
        // bırakılır). Uzak avatar parmakları telde almadığı için yine sentezler, ama kaynağı eşya
        // başına yazılmış bir profil değil tek bir kavrama duruşudur (HandPoseProfile.DefaultGrip):
        // yazılan bir profil, yakalanan kavramayla sessizce çelişebilecek ikinci bir tarif olurdu.

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
        /// (<c>Tools &gt; VortexArena &gt; Weapons &gt; Rebuild Net Item Catalog</c>): çakışan/eksik id derlemede
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
        /// İstenen kavrama noktasının, istenen elin <b>yakalanmış</b> kaydı.
        /// <para>⚠️ İstenen el yazılmamışsa <b>ÖTEKİ elin kaydına düşülür</b> (ikisi de yoksa
        /// <c>default</c>): tek elle yakalanmış bir silah, öteki elde silahı orijine yapıştırmak
        /// yerine yaklaşık ama makul bir duruşta tutulsun. ⚠️ Düşme yalnız <b>okuma</b> içindir —
        /// "yazılmış mı" sorusunun cevabı <see cref="HasGrip"/>'tir ve o DÜŞMEZ, yoksa eksik el
        /// hiçbir raporda görünmezdi.</para>
        /// </summary>
        public ItemGripCapture GetGrip(GripSocketKind kind, bool rightHand)
        {
            bool secondary = kind == GripSocketKind.Secondary;
            ItemGripCapture own = secondary
                ? (rightHand ? secondaryGripRight : secondaryGripLeft)
                : (rightHand ? primaryGripRight : primaryGripLeft);

            if (own.IsCaptured)
            {
                return own;
            }

            ItemGripCapture other = secondary
                ? (rightHand ? secondaryGripLeft : secondaryGripRight)
                : (rightHand ? primaryGripLeft : primaryGripRight);

            return other.IsCaptured ? other : default;
        }

        /// <summary>Bu kavrama noktası <b>bu el için</b> yakalanmış mı (öteki ele DÜŞMEZ —
        /// gerekçe <see cref="GetGrip"/>'te).</summary>
        public bool HasGrip(GripSocketKind kind, bool rightHand)
        {
            if (kind == GripSocketKind.Secondary)
            {
                return (rightHand ? secondaryGripRight : secondaryGripLeft).IsCaptured;
            }

            return (rightHand ? primaryGripRight : primaryGripLeft).IsCaptured;
        }

        /// <summary>
        /// Eşyanın ana el anchor'ına göre dönüşü — <b>her zaman KİMLİK</b>.
        /// <para>⚠️ <b>Ayarlanabilir bir kavrama dönüşü alanı YOKTUR ve eklenmez:</b> eşyanın
        /// eksenleri kumanda anchor'ının eksenleriyle birebir aynıdır — kumanda nereye bakıyorsa
        /// namlu oraya bakar. Aksi hâlde nişan alma, elle ayarlanmış (ya da yakalama anında
        /// oyuncunun bileği ne kadar eğikse o kadar eğik çıkan) bir sayıya bağlı hale gelirdi.</para>
        /// <para>⚠️ Aynı gerekçe <b>ELİN dönüşü</b> için de geçerlidir ve orada da uygulanır:
        /// <c>HandGripPoser</c> sentetik elin yalnız konumunu kilitler, bileğin dönüşü kumandayla
        /// birlikte serbest döner. El ile eşya aynı dönüşte olduğu için ikisi tek parça gibi durur;
        /// yakalamadan gelen bir bilek açısı ise eli kumandadan koparır.</para>
        /// </summary>
        public static Quaternion PrimaryGripRotation => Quaternion.identity;

        /// <summary>
        /// <b>EŞYANIN</b> ana el anchor'ına göre yerel konumu (metre): <c>itemPos =
        /// palm.pos + palm.rot * bu değer</c>.
        /// <para>Türetme (bilek ≡ anchor varsayımıyla): yakalama <c>handPos = itemPos + itemRot * H</c>
        /// dediğine göre <c>itemPos = handPos - itemRot * H</c>, yani ofset <c>-H</c>'dir.
        /// Canlı bilek deltası ölçülebiliyorsa aynı ölçü <see cref="ItemGripAuthority"/>'de
        /// deltayla düzeltilir; burası o yolun fallback'idir.</para>
        /// </summary>
        public Vector3 PrimaryGripPosition(bool rightHand)
        {
            return -GetGrip(GripSocketKind.Primary, rightHand).position;
        }

        /// <summary>
        /// Ana kavrama noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — soket çiziminin ve
        /// yakınlık ölçümünün ihtiyacı budur.
        /// <para>⚠️ <b>Ayrı bir alan DEĞİL, yakalanan kaydın kendisidir:</b> yakalama zaten
        /// "bilek eşyanın neresinde" sorusunu cevaplıyor. İkinci bir serialize alan açılırsa aynı
        /// nokta iki yerde yaşar ve biri güncellenip diğeri unutulur.</para>
        /// </summary>
        public Vector3 PrimaryGripPointOnItem(bool rightHand)
        {
            return GetGrip(GripSocketKind.Primary, rightHand).position;
        }

        /// <summary>
        /// Ön kabza noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — yalnız
        /// <see cref="IsTwoHanded"/> iken anlamlı. İkinci elin dünya hedefi
        /// <c>item.position + item.rotation * bu değer</c> ile bulunur (⚠️ <c>TransformPoint</c>
        /// DEĞİL: ölçü metredir, eşyanın görsel ölçeğiyle büyümez).
        /// </summary>
        public Vector3 SecondaryGripPosition(bool rightHand)
        {
            return GetGrip(GripSocketKind.Secondary, rightHand).position;
        }

        /// <summary>İkinci elin ön kabzadaki bilek dönüşü, <b>eşyaya göre yerel</b>. Eşyayı
        /// döndürmez (bkz. <see cref="ItemGripCapture"/>), yalnız o elin modelini oturtur.</summary>
        public Quaternion SecondaryGripRotation(bool rightHand)
        {
            return GetGrip(GripSocketKind.Secondary, rightHand).Rotation;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Yakalanan kavramayı ilgili alana yazar — <b>kalibrasyon aracının tek yazma kapısı</b>
        /// (alanlar private kalsın diye vardır; ikinci bir yazıcı, dört alanın hangisinin hangi el
        /// olduğunu ikinci kez tarif etmek olurdu).
        /// <para>⚠️ <c>EditorUtility.SetDirty</c>/<c>SaveAssets</c> ÇAĞRILMAZ: çağıran genelde
        /// birden çok alanı arka arkaya yazıyor ve kaydı tek Undo/tek dirty adımında toplamak
        /// istiyor.</para>
        /// </summary>
        /// <param name="itemLocal">Bileğin EŞYAYA göre yerel pozu (metre, ölçeksiz).</param>
        public void EditorSetGrip(GripSocketKind kind, bool rightHand, in Pose itemLocal)
        {
            ItemGripCapture capture = ItemGripCapture.From(itemLocal);

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
#endif

        /// <summary>
        /// Ana kavrama soketinin yarıçapı (metre): grip basışının KABUL edildiği mesafe.
        /// <para>⚠️ <b>Alt sınır 1 cm'dir ve öyle kalmalı:</b> sıfır (ya da eksi) yarıçap soketi
        /// matematiksel olarak kavranamaz yapar — sahada bu bir hata olarak DEĞİL "silah alınamıyor"
        /// olarak görünür, yani teşhisi pahalı. Ayarlanmamış/sıfırlanmış bir asset bu sayede yine
        /// çalışır kalır.</para>
        /// </summary>
        public float PrimaryGripRadius => Mathf.Max(0.01f, primaryGripRadius);

        /// <summary>
        /// Ön kabza soketinin yarıçapı (metre) — yalnız <see cref="IsTwoHanded"/> iken anlamlı.
        /// Alt sınır gerekçesi <see cref="PrimaryGripRadius"/>'te.
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
