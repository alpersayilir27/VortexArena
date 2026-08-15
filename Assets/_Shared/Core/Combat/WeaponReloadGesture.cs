using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bel-altı reload jesti: elde tutulan silah aşağı doğrultulup bel hizasının altına
    /// indirilip kısa süre orada tutulunca <c>Weapon.TryStartReload</c> çağrılır. Reload'ın
    /// gerçekten başlayıp başlamayacağı (dolu şarjör, yedek yok, ölü oyuncu...) tamamen
    /// Weapon.TryStartReload'un kuralıdır — bu bileşen yalnız jesti TANIR.
    /// <para>
    /// ⚠️ <b>Ölçülen nokta ELDİR</b> (kumanda anchor'ı), silahın kökü DEĞİL: kök avuçtan
    /// <c>gripPosition</c> kadar namlu boyunca ileridedir (silahtan silaha 0–34 cm) ve o ofset
    /// silahın açısıyla dikey yönde süzülür — kökü ölçmek, aynı jesti bir silahta göbek
    /// hizasında bir başkasında diz hizasında yakalamak demekti.
    /// </para>
    /// <para>
    /// ⚠️ <b>Hiçbir eşik metre cinsinden SABİT değildir</b>: ölçü elin <b>GÖZE göre düşüşüdür</b>
    /// ve eşiği <b>ayakta göz yüksekliğinin</b> oranıdır (<see cref="WaistDropRatio"/>) — dik
    /// duran oyuncuda tam olarak kemer hizası. Kısa oyuncu kolunu az, uzun oyuncu çok indirir ama
    /// ikisi de "beline kadar" indirir. Kafadan sabit düşüş (<c>headY − 0.62</c>) bu yüzden terk
    /// edildi.
    /// </para>
    /// <para>
    /// ⚠️ Ölçünün iki parçası da bilinçlidir ve <b>karıştırılamaz</b>: referans nokta <b>CANLI</b>
    /// gözdür (kafayla birlikte iner), ölçek ise <b>AYAKTA</b> boydur
    /// (<see cref="StandingHeightState"/>, duruştan bağımsız). Zemine göre sabit bir çizgi
    /// çizilseydi eğilen oyuncunun sarkan kolu çizginin altında kalır ve reload kendiliğinden
    /// başlardı; ölçek canlı göz yüksekliği olsaydı eşik oyuncu eğildikçe onunla birlikte iner ve
    /// aynı şey olurdu. Bu hâliyle eğilmek elin göze göre düşüşünü DEĞİŞTİRMEZ.
    /// </para>
    /// <para>
    /// İkinci koşul <b>silahın aşağı doğrultulmasıdır</b> ve bu <b>KUMANDADAN</b> okunur
    /// (<c>anchor.forward</c>): silah zaten kumanda doğrultusundadır, silahın kendi
    /// transformuna bakmak aynı bilgiyi model başına sapan bir yoldan öğrenmek olurdu.
    /// ⚠️ Ölçü dünya uzayındadır, yani <b>kafa dönüşünden bağımsızdır</b>: aşağı bakmak bu koşulu
    /// ne kolaylaştırır ne zorlaştırır.
    /// </para>
    /// <para>
    /// Silah kavrandıktan sonra el bir kez göğüs hizasına çıkmadan jest kurulmaz (armed) —
    /// yerden/kılıftan almada yanlış tetikleme böyle önlenir.
    /// </para>
    /// <para>
    /// Reload <b>gerçekten başladıysa</b> tek darbelik haptik onay verilir. ⚠️ Jest tanındığı için
    /// değil, <c>TryStartReload</c> kabul ettiği için titrer: reddedilen jestin titremesi oyuncuya
    /// "doldu" derdi ve o yanlış bilgi ancak tetiği çekince ortaya çıkardı.
    /// </para>
    /// <para>
    /// Rig çözülemediğinde (admin gözlemci, editör oturumu, el tanınmadı) jest HİÇ tanınmaz:
    /// eli olmayan oturumda "el nerede" sorusunun doğru cevabı yoktur, uydurmak yerine susar.
    /// </para>
    /// </summary>
    public class WeaponReloadGesture : MonoBehaviour
    {
        /// <summary>
        /// Bel çizgisi: elin GÖZÜN bu kadar altına inmesi gerekir — ayakta göz yüksekliğinin oranı
        /// olarak. Dik duran oyuncuda "zemin ile gözün %62'si"nin tam karşılığıdır (1 − 0.62):
        /// antropometride kemer ≈ boyun %60'ı, göz ≈ %93'ü → kemer/göz ≈ 0.64. Daha küçük bir
        /// düşüş kemeri değil göbeği gösterir ve jest kendiliğinden tetiklenmeye başlar.
        /// </summary>
        private const float WaistDropRatio = 0.38f;

        /// <summary>
        /// Jestin yeniden kurulması için elin bel çizgisinin bu kadar ÜSTÜNE çıkması gerekir
        /// (aynı ölçek). ⚠️ Bağımsız bir oran DEĞİL, <see cref="WaistDropRatio"/>'dan türetilir:
        /// iki eşik ayrı ayrı yazılsaydı bel yükseltilirken kurulma bandı unutulur ve band ters
        /// dönebilirdi.
        /// </summary>
        private const float RearmMargin = 0.10f;

        /// <summary>Kumandanın ileri ekseni yatayın en az bu kadar altına bakmalı: sin(25°).
        /// Elini kemerine indiren oyuncunun bileği nötrken namlu ~20–30° aşağıdadır; daha dik bir
        /// eşik jesti bileği kırmaya bağlar.</summary>
        private const float MinDownSin = 0.423f;

        /// <summary>Jestin sürmesi gereken süre (saniye).</summary>
        private const float DwellSeconds = 0.15f;

        /// <summary>
        /// Koşul bozulduğunda sayaç <b>sıfırlanmaz, sızar</b> — bu, sızıntının süreye oranıdır.
        /// ⚠️ Sıfırlamak eşiğin kenarındaki tek karelik izleme gürültüsünü jesti öldürecek kadar
        /// büyütür; sızıntıyla o gürültü yutulur, duruştan gerçekten çıkan oyuncunun sayacı ise
        /// <c>DwellSeconds / DwellDecayRate</c> saniyede temizlenir.
        /// </summary>
        private const float DwellDecayRate = 2f;

        [SerializeField] private Weapon weapon;

        private bool _armed;
        private float _dwell;

        private void Update()
        {
            if (weapon == null || !weapon.IsHeld)
            {
                Disarm();
                return;
            }

            // Elin KENDİSİ: silahın kökü değil, silahı tutan kumandanın anchor'ı.
            Transform anchor = WeaponGranter.ResolveHandAnchor(weapon.MainHand);
            if (anchor == null ||
                !WeaponGranter.TryResolveEyeAndFloor(out float eyeY, out float _) ||
                !StandingHeightState.TryGet(out float standingEye))
            {
                Disarm();
                return;
            }

            // Elin GÖZE göre düşüşü: referans nokta kafayla birlikte iner, ölçeği ise ayakta boydur.
            float drop = eyeY - anchor.position.y;

            if (!_armed)
            {
                // Jest ancak el bir kez göğüs hizasına çıktıktan sonra kurulur.
                if (drop < standingEye * (WaistDropRatio - RearmMargin))
                {
                    _armed = true;
                }

                return;
            }

            bool belowWaist = drop > standingEye * WaistDropRatio;
            bool pointingDown = -anchor.forward.y >= MinDownSin;

            if (belowWaist && pointingDown)
            {
                _dwell += Time.deltaTime;
                if (_dwell >= DwellSeconds)
                {
                    // Dönüş değeri YALNIZ haptik onay içindir: Weapon reddedebilir (dolu şarjör,
                    // yedek yok, ölü oyuncu) ve reddedilen jestin titremesi "oldu" der. Jest her
                    // hâlde sıfırlanır, oyuncu tekrar dener.
                    if (weapon.TryStartReload())
                    {
                        ControllerHaptics.PulseBoth(this, 1);
                    }

                    Disarm();
                }

                return;
            }

            // ⚠️ Sıfırlama DEĞİL sızıntı (gerekçe DwellDecayRate).
            _dwell = Mathf.Max(0f, _dwell - Time.deltaTime * DwellDecayRate);
        }

        private void Disarm()
        {
            _armed = false;
            _dwell = 0f;
        }
    }
}
