using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kanonik kavramanın matematiği — <b>yerel ve uzak uçta koşan TEK çözücü</b>.
    /// <para>
    /// <b>Neden ayrı ve saf bir sınıf:</b> aynı duruşu iki taraf da hesaplamak zorunda (duruş telde
    /// gitmez, §6.6). İki ayrı uygulama olsaydı biri düzeltilip öteki unutulur ve aynı silah kendi
    /// ekranında başka, karşı ekranda başka görünürdü. Burada sahne/bileşen bağımlılığı YOKTUR:
    /// girdi iki avuç pozu + tanım, çıktı eşyanın dünya pozu.
    /// </para>
    /// <para>
    /// <b>Tek el:</b> eşya ana avucun anchor ofsetinden sürülür (bugünkü davranışın birebir aynısı).
    /// <b>İki el:</b> tek elli çözümden başlanır ve eşyanın <i>ana kavrama → ön kabza</i> ekseni
    /// ikinci elin avucuna çevrilir. Yani ikinci el eşyayı TAŞIMAZ, yalnız NİŞANLAR — ana kavrama
    /// noktası her karede ana avucun tam üstünde kalır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yumuşatma buraya girmez</b> (<see cref="StepAimBlend"/> ayrıdır): çözücü saf kaldığı
    /// sürece iki uçta aynı fonksiyon koşabilir. Yerelde blend zaman sabitiyle sürülür, uzakta
    /// zaten telin kendi interpolasyonu var.
    /// </para>
    /// </summary>
    public static class ItemGripSolver
    {
        /// <summary>
        /// İki elli nişanın <b>tam takip</b> bandının üst sınırı (derece): buraya kadar silah ikinci
        /// eli birebir izler.
        /// <para><b>Kavramayla ilgisi YOKTUR</b>: ön kabza bağı yalnız grip tuşuna bakar
        /// (<c>WeaponGranter.ResolveSecondaryHand</c>), buradaki iki sabit silahın ne kadar
        /// DÖNECEĞİNİ söyler.</para>
        /// </summary>
        private const float AimFullAngleDegrees = 120f;

        /// <summary>
        /// İki elli nişanın <b>tümden bırakıldığı</b> açı (derece): bu açıdan sonra ikinci el yok
        /// sayılır, silah ana elin duruşunda kalır. Arada <see cref="ReachWeight"/> yumuşak iner.
        /// <para>
        /// ⚠️ <b>Sert bir tavan (clamp) buraya GERİ KONMAZ.</b> Tavan yalnız dönüşün büyüklüğünü
        /// sınırlar, oysa asıl sorun <see cref="Quaternion.FromToRotation"/>'ın iki vektör
        /// ters-paralele yaklaşırken <b>tanımsızlaşmasıdır</b>: dönüş ekseni <c>from × to</c>'dur ve
        /// o çarpım sıfıra giderken ekseninin YÖNÜ en küçük gürültüde işaret değiştirir — silah bir
        /// anda ters tarafa savrulur. Tavan bunu görmez, yalnız savrulmanın büyüklüğünü kırpar.
        /// </para>
        /// <para>
        /// Bant bunu <b>ağırlığı sıfırlayarak</b> çözer: tekillik bölgesine varıldığında uygulanan
        /// dönüşün ağırlığı zaten 0'dır, yani eksen orada gürültü olsa da hiçbir görsel etkisi
        /// kalmaz. Değer, iki elle fiziksel olarak tutulamayacak bir pozun ötesine konur — bandın
        /// içi bugünkü hissi olduğu gibi bırakır, dışı yumuşakça tek elli duruşa döner.
        /// </para>
        /// </summary>
        private const float AimFadeOutAngleDegrees = 160f;

        /// <summary>Ön kabza ekseni bundan kısaysa (1 cm) yön tanımsızdır: ikincil soket hiç
        /// yazılmamış demektir, iki el çözümü koşmaz.</summary>
        private const float MinAxisSqr = 1e-4f;

        /// <summary>İki avuç arası bundan kısaysa (5 cm) hedef yönü gürültüdür — eller üst üsteyken
        /// silah küçük titremelerle savrulurdu.</summary>
        private const float MinReachSqr = 0.0025f;

        /// <summary>İkinci el tutulup bırakılırken 0↔1 geçişinin zaman sabiti (saniye).</summary>
        private const float AimBlendSeconds = 0.08f;

        /// <summary>
        /// Eşyanın dünya pozunu çözer.
        /// </summary>
        /// <param name="def">Eşya tanımı (kavrama ofsetlerinin kaynağı).</param>
        /// <param name="primaryPalm">Ana elin AVUÇ pozu (<c>HandGripPivot.Resolve</c> çıktısı).</param>
        /// <param name="hasSecondary">İkinci el ön kabzada mı.</param>
        /// <param name="secondaryPalmPosition">İkinci elin avuç KONUMU (dönüşü kullanılmaz: roll ana
        /// elden gelir, <c>FromToRotation</c> en kısa yayı seçtiği için kendi başına roll üretmez).</param>
        /// <param name="aimBlend">0..1 — çağıranın yumuşatması; 0 iken sonuç tek elli çözümdür.</param>
        public static void Solve(ItemDefinition def, in Pose primaryPalm, bool hasSecondary,
                                 in Vector3 secondaryPalmPosition, float aimBlend,
                                 out Vector3 itemPosition, out Quaternion itemRotation)
        {
            if (def == null)
            {
                // Tanımsız eşyanın kavrama ofseti de yoktur: eşyayı avuca yapıştırmak, dünya
                // orijinine düşürmekten iyidir (eksikliği Weapon.Awake zaten hata olarak basıyor).
                itemPosition = primaryPalm.position;
                itemRotation = primaryPalm.rotation;
                return;
            }

            Solve(def, def.PrimaryGripPosition, def.PrimaryGripRotation, primaryPalm, hasSecondary,
                  secondaryPalmPosition, aimBlend, out itemPosition, out itemRotation);
        }

        /// <summary>
        /// <see cref="Solve(ItemDefinition, in Pose, bool, in Vector3, float, out Vector3, out Quaternion)"/>'un
        /// ana kavramayı <b>parametreden</b> alan biçimi: ölçü tanımdan değil, prefabtaki kavrama poz
        /// düğümünden çözülmüş olabilir (<see cref="ItemGripAuthority"/>).
        /// <para>
        /// ⚠️ <b>Etkin ofset ile ikincil eksen AYNI kaynaktan türer:</b> ön kabza ekseni ana kavrama
        /// noktasından ölçülüyor, o nokta hâlâ tanımdan okunsaydı düğümden gelen kavramada eksen
        /// sessizce kayar ve iki elli nişan silahı yamuk çevirirdi. <paramref name="def"/> yalnız
        /// <b>ikincil</b> soketin kaynağıdır (ön kabza için poz düğümü yolu bugün yok).
        /// </para>
        /// <para>⚠️ Sınıf yine SAFTIR: sahneye bakan tek şey ofseti çözen taraftır, çözücü değil.</para>
        /// </summary>
        /// <param name="primaryGripPosition">EŞYANIN ana el anchor'ına göre yerel konumu (m).</param>
        /// <param name="primaryGripRotation">EŞYANIN ana el anchor'ına göre yerel dönüşü.</param>
        public static void Solve(ItemDefinition def, in Vector3 primaryGripPosition,
                                 in Quaternion primaryGripRotation, in Pose primaryPalm,
                                 bool hasSecondary, in Vector3 secondaryPalmPosition, float aimBlend,
                                 out Vector3 itemPosition, out Quaternion itemRotation)
        {
            // Tek elli çözüm HER ZAMAN hesaplanır ve iki elli dalın emniyetleri düştüğünde
            // olduğu gibi döner — "iki el çözümü koşmadı" durumunun tanımlı bir sonucu olsun.
            Quaternion baseRotation = primaryPalm.rotation * primaryGripRotation;
            itemRotation = baseRotation;
            itemPosition = primaryPalm.position + primaryPalm.rotation * primaryGripPosition;

            float blend = Mathf.Clamp01(aimBlend);
            if (def == null || !hasSecondary || blend <= 0f)
            {
                return;
            }

            Vector3 gripPointOnItem = ItemGripAuthority.GripPointOnItem(primaryGripPosition, primaryGripRotation);
            Vector3 axisLocal = def.SecondaryGripPosition - gripPointOnItem;
            Vector3 to = secondaryPalmPosition - primaryPalm.position;
            if (axisLocal.sqrMagnitude < MinAxisSqr || to.sqrMagnitude < MinReachSqr)
            {
                return;
            }

            Vector3 from = baseRotation * axisLocal;

            // ⚠️ Açı Vector3.Angle ile ÖLÇÜLÜR, Quaternion.ToAngleAxis ile değil. İkincisi çift örtü
            // (q ile −q aynı dönüştür) yüzünden ters-paralele yaklaşırken 180°'yi AŞAN bir açı ve
            // İŞARETİ DÖNMÜŞ bir eksen döndürebilir; o eksende uygulanan her dönüş silahı olması
            // gerekenin tam TERSİ yönde savurur. Vector3.Angle her zaman [0,180]'dedir ve bu
            // belirsizliği hiç taşımaz.
            float reach = ReachWeight(Vector3.Angle(from, to));
            if (reach <= 0f)
            {
                // Hedef, kolun ulaşamayacağı yerde. Bu bir KOPMA değildir (ön kabza bağı grip
                // tuşuna bakar) ve zıplama üretmez: ağırlık bandın içinde zaten sıfıra inmişti.
                return;
            }

            Quaternion full = Quaternion.FromToRotation(from, to);
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, full, blend * reach);

            itemRotation = delta * baseRotation;

            // ⚠️ Konum dönüşten SONRA ve ters yönde kurulur: ana kavrama noktası ana avucun tam
            // üstünde kalsın diye. Yani iki elli çözüm yalnız YÖNELİMİ değiştirir, silahı ikinci
            // ele doğru KAYDIRMAZ (kaydırsaydı ana el silahı bırakmış gibi görünürdü).
            // Doğrulama kimliği: delta = identity iken bu satır
            // 'primaryPalm.position + primaryPalm.rotation * primaryGripPosition' ile ÖZDEŞTİR —
            // gripPointOnItem tanım gereği Inverse(R) * (-P) olduğu için (ItemGripAuthority).
            itemPosition = primaryPalm.position - itemRotation * gripPointOnItem;
        }

        /// <summary>
        /// Hedef yönün <b>ulaşılabilirlik</b> ağırlığı: 1 = ikinci el birebir izlenir, 0 = ikinci el
        /// yok sayılır (silah ana elin duruşunda kalır). İkisinin arasında <c>SmoothStep</c>.
        /// <para>
        /// <b>Neden ağırlık, neden tavan değil:</b> gerekçe <see cref="AimFadeOutAngleDegrees"/>'te.
        /// Kısaca: tavan savrulmanın büyüklüğünü kırpar, ağırlık savrulmanın kendisini görünmez
        /// kılar — ve süreklidir, yani bandın iki yakasında silah zıplamaz.
        /// </para>
        /// <para>
        /// ⚠️ <b>Yerelde ve uzakta AYNI fonksiyon koşar</b> (çözücünün saf olmasının sebebi budur):
        /// bant çağıranda hesaplansaydı aynı silah kendi ekranında düz, karşı ekranda ters
        /// görünürdü.
        /// </para>
        /// </summary>
        public static float ReachWeight(float angleDegrees)
        {
            if (angleDegrees <= AimFullAngleDegrees)
            {
                return 1f;
            }

            if (angleDegrees >= AimFadeOutAngleDegrees)
            {
                return 0f;
            }

            float t = (angleDegrees - AimFullAngleDegrees) /
                      (AimFadeOutAngleDegrees - AimFullAngleDegrees);
            return Mathf.SmoothStep(1f, 0f, t);
        }

        /// <summary>
        /// İki elli çözümün ağırlığını hedefe doğru bir adım sürer.
        /// <para>
        /// <b>Neden durum çağıranda:</b> çözücünün saf kalması, aynı fonksiyonun yerelde ve uzakta
        /// koşabilmesinin ön koşulu. Uzak uçta yumuşatma zaten telin interpolasyonundan geliyor,
        /// orada bu adım hiç çağrılmaz.
        /// </para>
        /// </summary>
        public static float StepAimBlend(float current, bool wantTwoHand, float deltaTime)
        {
            return Mathf.MoveTowards(current, wantTwoHand ? 1f : 0f, deltaTime / AimBlendSeconds);
        }
    }
}
