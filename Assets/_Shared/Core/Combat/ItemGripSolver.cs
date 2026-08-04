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
        /// İki elli çözümün açı tavanı (derece): kolun ulaşamayacağı bir hedef silahı katlamasın.
        /// <para>Tavan aşıldığında çözüm KAPANMAZ, aynı eksende clamp'lenir — kapanmak silahı
        /// tavanın iki yakasında zıplatırdı.</para>
        /// </summary>
        private const float MaxAimAngleDegrees = 75f;

        /// <summary>Ön kabza ekseni bundan kısaysa (1 cm) yön tanımsızdır: ikincil soket hiç
        /// yazılmamış demektir, iki el çözümü koşmaz.</summary>
        private const float MinAxisSqr = 1e-4f;

        /// <summary>İki avuç arası bundan kısaysa (5 cm) hedef yönü gürültüdür — eller üst üsteyken
        /// silah küçük titremelerle savrulurdu.</summary>
        private const float MinReachSqr = 0.0025f;

        /// <summary><c>ToAngleAxis</c> dejenere rotasyonda anlamsız/NaN eksen üretebilir; eksen
        /// bundan kısaysa dönüş uygulanmaz.</summary>
        private const float MinAxisDirectionSqr = 1e-6f;

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

            // Tek elli çözüm HER ZAMAN hesaplanır ve iki elli dalın emniyetleri düştüğünde
            // olduğu gibi döner — "iki el çözümü koşmadı" durumunun tanımlı bir sonucu olsun.
            Quaternion baseRotation = primaryPalm.rotation * def.PrimaryGripRotation;
            itemRotation = baseRotation;
            itemPosition = primaryPalm.position + primaryPalm.rotation * def.PrimaryGripPosition;

            float blend = Mathf.Clamp01(aimBlend);
            if (!hasSecondary || blend <= 0f)
            {
                return;
            }

            Vector3 axisLocal = def.SecondaryGripPosition - def.PrimaryGripPointOnItem;
            Vector3 to = secondaryPalmPosition - primaryPalm.position;
            if (axisLocal.sqrMagnitude < MinAxisSqr || to.sqrMagnitude < MinReachSqr)
            {
                return;
            }

            Quaternion full = Quaternion.FromToRotation(baseRotation * axisLocal, to);
            full.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.sqrMagnitude < MinAxisDirectionSqr || float.IsNaN(axis.x))
            {
                return;
            }

            Quaternion clamped = Quaternion.AngleAxis(
                Mathf.Min(angle, MaxAimAngleDegrees), axis.normalized);
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, clamped, blend);

            itemRotation = delta * baseRotation;

            // ⚠️ Konum dönüşten SONRA ve ters yönde kurulur: ana kavrama noktası ana avucun tam
            // üstünde kalsın diye. Yani iki elli çözüm yalnız YÖNELİMİ değiştirir, silahı ikinci
            // ele doğru KAYDIRMAZ (kaydırsaydı ana el silahı bırakmış gibi görünürdü).
            // Doğrulama kimliği: delta = identity iken bu satır
            // 'primaryPalm.position + primaryPalm.rotation * PrimaryGripPosition' ile ÖZDEŞTİR —
            // PrimaryGripPointOnItem tanım gereği Inverse(R) * (-P) olduğu için (ItemDefinition).
            itemPosition = primaryPalm.position - itemRotation * def.PrimaryGripPointOnItem;
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
