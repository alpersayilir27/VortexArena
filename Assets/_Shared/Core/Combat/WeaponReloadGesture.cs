using UnityEngine;

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
    /// ⚠️ <b>Hiçbir eşik metre cinsinden SABİT değildir</b>: bel çizgisi ZEMİN ile GÖZ arasının
    /// oranıdır (<see cref="WaistRatio"/>) — gövdenin ortasının biraz üstü. Zemin rig'in
    /// tracking space'idir (origin <c>Stage</c>), yani ölçü oyuncunun boyuna kendiliğinden uyar;
    /// kısa oyuncu kolunu az, uzun oyuncu çok indirir ama ikisi de "beline kadar" indirir.
    /// Kafadan sabit düşüş (<c>headY − 0.62</c>) bu yüzden terk edildi.
    /// </para>
    /// <para>
    /// İkinci koşul <b>silahın aşağı doğrultulmasıdır</b> ve bu <b>KUMANDADAN</b> okunur
    /// (<c>anchor.forward</c>): silah zaten kumanda doğrultusundadır, silahın kendi
    /// transformuna bakmak aynı bilgiyi model başına sapan bir yoldan öğrenmek olurdu.
    /// ⚠️ Sapmanın kaynağı <c>ItemDefinition.primaryGripEuler</c>'dir: sıfırdan farklı bir
    /// euler yazılmış silahta namlu kumandanın ilerisiyle çakışmaz (bugün yalnız AK47), o
    /// silahta jest o açı kadar erken/geç tanınır.
    /// </para>
    /// <para>
    /// Silah kavrandıktan sonra el bir kez göğüs hizasına çıkmadan jest kurulmaz (armed) —
    /// yerden/kılıftan almada yanlış tetikleme böyle önlenir.
    /// </para>
    /// <para>
    /// Rig çözülemediğinde (admin gözlemci, editör oturumu, el tanınmadı) jest HİÇ tanınmaz:
    /// eli olmayan oturumda "el nerede" sorusunun doğru cevabı yoktur, uydurmak yerine susar.
    /// </para>
    /// </summary>
    public class WeaponReloadGesture : MonoBehaviour
    {
        /// <summary>Bel çizgisi: zemin ile göz arasının bu oranı (0.5 = tam orta).</summary>
        private const float WaistRatio = 0.55f;

        /// <summary>Jestin yeniden kurulması için elin çıkması gereken oran (aynı ölçek).</summary>
        private const float RearmRatio = 0.72f;

        /// <summary>Kumandanın ileri ekseni yatayın en az bu kadar altına bakmalı: sin(35°).</summary>
        private const float MinDownSin = 0.574f;

        /// <summary>Jestin kesintisiz sürmesi gereken süre (saniye).</summary>
        private const float DwellSeconds = 0.2f;

        /// <summary>Göz yüksekliği bunun altındaysa ölçü henüz oturmamıştır (rig'in ilk kareleri).</summary>
        private const float MinEyeHeight = 0.5f;

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
                !WeaponGranter.TryResolveEyeAndFloor(out float eyeY, out float floorY))
            {
                Disarm();
                return;
            }

            float eyeHeight = eyeY - floorY;
            if (eyeHeight < MinEyeHeight)
            {
                Disarm();
                return;
            }

            float handY = anchor.position.y;

            if (!_armed)
            {
                // Jest ancak el bir kez göğüs hizasına çıktıktan sonra kurulur.
                if (handY > floorY + eyeHeight * RearmRatio)
                {
                    _armed = true;
                }

                return;
            }

            bool belowWaist = handY < floorY + eyeHeight * WaistRatio;
            bool pointingDown = -anchor.forward.y >= MinDownSin;

            if (belowWaist && pointingDown)
            {
                _dwell += Time.deltaTime;
                if (_dwell >= DwellSeconds)
                {
                    // Sonuç önemsenmez: Weapon reddederse jest sıfırlanır, oyuncu tekrar dener.
                    weapon.TryStartReload();
                    Disarm();
                }

                return;
            }

            _dwell = 0f;
        }

        private void Disarm()
        {
            _armed = false;
            _dwell = 0f;
        }
    }
}
