using UnityEngine;
using UnityEngine.Serialization;

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

        // ⚠️ İKİ KAVRAMA ALANI İKİ AYRI UZAYDA — karıştırmak sessiz bir işaret hatası üretir:
        //   primaryGrip   = EŞYANIN, ana elin anchor'ına göre yerel pozu  (el → eşya)
        //   secondaryGrip = ÖN KABZA NOKTASININ, eşyaya göre yerel pozu   (eşya → el)
        // Sebep asimetri değil, yazılabilirlik: eşyanın dünya pozu ana elden türetilir, ön kabza
        // ise eşyanın ÜSTÜNDE sabit bir noktadır ("namlu boyunca 25 cm ileride"). İkincisini de
        // "ele göre" ifade etmek aynı bilgiyi ters bileşimle yazdırmak olurdu.
        // Buradaki YARIÇAPLAR duruşun parçası DEĞİL, KAPI ölçüsüdür: kavramanın nerede kabul
        // edildiğini söylerler, eşyanın elde nasıl duracağını değil.
        [Header("Kavrama (kanonik)")]
        [Tooltip("EŞYANIN ana el anchor'ına göre yerel konumu (m). Prefabın kökü zaten kabza " +
                 "hizasındaysa sıfır bırakılır; VR'da rahat duruş için burada ince ayar yapılır.")]
        [FormerlySerializedAs("grantedHoldPosition")]
        [SerializeField] private Vector3 primaryGripPosition = Vector3.zero;

        [Tooltip("Ana elin kavrama dönüşü (derece, Euler).")]
        [FormerlySerializedAs("grantedHoldEuler")]
        [SerializeField] private Vector3 primaryGripEuler = Vector3.zero;

        [Tooltip("Ön kabza noktasının EŞYAYA göre yerel konumu (m) — yalnız TwoHand'de anlamlı. " +
                 "İkinci elin gideceği yer burasıdır.")]
        [SerializeField] private Vector3 secondaryGripPosition = Vector3.zero;

        [Tooltip("İkinci elin ön kabzadaki dönüşü, EŞYAYA göre yerel (derece, Euler).")]
        [SerializeField] private Vector3 secondaryGripEuler = Vector3.zero;

        // ⚠️ [Range] KOYULMAZ — dosyanın başındaki netItemId uyarısındaki tuzağın aynısı: Range
        // drawer'ı değeri kendi varsayılan sınırlarına sessizce clamp'ler VE asset'i dirty yapar,
        // yani Inspector'da açılan her tanım farkında olmadan başka bir yarıçapla commit'lenir.
        // Alt sınır property'lerde uygulanıyor.
        [Tooltip("Ana kavrama soketinin yarıçapı (m): grip basışının KABUL edildiği mesafe. " +
                 "Silah başına ayarlanır — tabanca kabzası ile tüfek ön kabzası aynı büyüklükte değil.")]
        [SerializeField] private float primaryGripRadius = 0.12f;

        [Tooltip("Ön kabza soketinin yarıçapı (m) — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private float secondaryGripRadius = 0.12f;

        // Parmak duruşu tabanda durur çünkü tabanın ölçütü "ağın + UZAK ÇİZİMİN ihtiyacı"dır ve
        // bu tam olarak uzak çizim verisidir: parmaklar telde gitmiyor (§6.9), uzak avatarı çizen
        // taraf duruşu ELDEKİ EŞYADAN çözüyor. Beş sayı tutulmasının gerekçesi HandPoseProfile'da.
        [Tooltip("Bu eşya tutulurken parmakların kapanma oranı (uzak avatarda çizilir). " +
                 "Tümü 0 = yazılmamış → genel kavrama duruşu kullanılır.")]
        [SerializeField] private HandPoseProfile handPose;

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

        /// <summary><b>EŞYANIN</b> ana el anchor'ına göre yerel konumu (metre): el → eşya.</summary>
        /// <summary>
        /// Bu eşya tutulurken elin parmak duruşu (§6.9 — telde gitmez, uzak uç kendi sürer).
        /// <para>Yazılmamışsa <see cref="HandPoseProfile.DefaultGrip"/> döner: alanı hiç
        /// görmemiş eski bir tanım, elin tahta gibi düz kalmasına değil makul bir kavramaya
        /// düşsün.</para>
        /// </summary>
        public HandPoseProfile HandPose => handPose.IsEmpty ? HandPoseProfile.DefaultGrip : handPose;

        public Vector3 PrimaryGripPosition => primaryGripPosition;

        /// <summary><b>EŞYANIN</b> ana el anchor'ına göre yerel dönüşü: el → eşya.</summary>
        public Quaternion PrimaryGripRotation => Quaternion.Euler(primaryGripEuler);

        /// <summary>
        /// <b>ÖN KABZA NOKTASININ EŞYAYA göre</b> yerel konumu (metre): eşya → el. Yalnız
        /// <see cref="IsTwoHanded"/> iken anlamlı.
        /// <para>⚠️ <see cref="PrimaryGripPosition"/> ile <b>uzayı terstir</b> — ikinci elin dünya
        /// pozu <c>eşyaTransformu.TransformPoint(SecondaryGripPosition)</c> ile bulunur, ters
        /// bileşimle DEĞİL. Ön kabza eşyanın üstünde sabit bir noktadır; bu yüzden eşya-yereldir.</para>
        /// </summary>
        public Vector3 SecondaryGripPosition => secondaryGripPosition;

        /// <summary>
        /// Ana kavrama noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — soket çiziminin ve
        /// yakınlık ölçümünün ihtiyacı budur.
        /// <para>⚠️ <b>Bu bir TÜRETİLMİŞ değerdir, ayrı bir alan DEĞİL</b> ve öyle kalmalı:
        /// <see cref="PrimaryGripPosition"/> "el → eşya" yönünde ifade edilir (kanonik kavramanın
        /// ihtiyacı o), soket ise eşyanın üstünde bir noktadır — aynı ölçünün ters yönü. İkinci bir
        /// serialize alan açılırsa aynı nokta iki yerde yaşar ve biri güncellenip diğeri unutulur.</para>
        /// <para>Türetme: <c>item.TransformPoint(s) == hand.position</c> koşulundan
        /// <c>s = Inverse(R) * (-P)</c>.</para>
        /// </summary>
        public Vector3 PrimaryGripPointOnItem =>
            Quaternion.Inverse(PrimaryGripRotation) * (-primaryGripPosition);

        /// <summary>İkinci elin ön kabzadaki dönüşü, <b>eşyaya göre yerel</b>
        /// (bkz. <see cref="SecondaryGripPosition"/> uyarısı).</summary>
        public Quaternion SecondaryGripRotation => Quaternion.Euler(secondaryGripEuler);

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
