using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bel-altı reload jesti: elde tutulan silah bel hizasının altına indirilip kısa
    /// süre orada tutulunca <c>Weapon.TryStartReload</c> çağrılır. Reload'ın gerçekten
    /// başlayıp başlamayacağı (dolu şarjör, yedek yok, ölü oyuncu...) tamamen
    /// Weapon.TryStartReload'un kuralıdır — bu bileşen yalnız jesti TANIR.
    /// <para>
    /// Yükseklikler ARENA UZAYINDA karşılaştırılır (<c>ArenaSpace.WorldToArena(...).y</c>):
    /// zemin referanslı olduğu için kalibrasyon sonrası dünya origin'i nerede olursa
    /// olsun doğru kalır. Silah kavrandıktan sonra bir kez bel ÜSTÜNE çıkmadan jest
    /// kurulmaz (armed) — yerden/kılıftan almada yanlış tetikleme böyle önlenir.
    /// </para>
    /// </summary>
    public class WeaponReloadGesture : MonoBehaviour
    {
        /// <summary>Camera.main bulunamadığında yeniden deneme aralığı (her karede aranmaz).</summary>
        private const float CameraRetryIntervalSeconds = 1f;

        [SerializeField] private Weapon weapon;
        [Tooltip("Baş referansı; boşsa Camera.main kullanılır.")]
        [SerializeField] private Transform head;
        [Tooltip("Bel yüksekliği = baş yüksekliği × bu oran.")]
        [SerializeField] private float waistHeightRatio = 0.55f;
        [Tooltip("Jest için silahın bel çizgisinin ne kadar ALTINA inmesi gerektiği (metre).")]
        [SerializeField] private float belowOffsetMeters = 0.05f;
        [Tooltip("Jestin yeniden kurulması için bel çizgisinin ne kadar ÜSTÜNE çıkılacağı (metre).")]
        [SerializeField] private float rearmOffsetMeters = 0.15f;
        [Tooltip("Silahın bel altında kesintisiz kalması gereken süre (saniye).")]
        [SerializeField] private float dwellSeconds = 0.2f;

        // head alanı boşsa Camera.main tembel çözülür; null ise 1 sn'de bir yeniden denenir.
        private Transform _resolvedHead;
        private float _headRetryTimer;

        private bool _armed;
        private float _dwell;

        private void Update()
        {
            if (weapon == null || !weapon.IsHeld)
            {
                _armed = false;
                _dwell = 0f;
                return;
            }

            Transform headRef = ResolveHead();
            if (headRef == null)
            {
                return;
            }

            float headArenaY = ArenaSpace.WorldToArena(headRef.position).y;
            float weaponArenaY = ArenaSpace.WorldToArena(transform.position).y;
            float waistY = headArenaY * waistHeightRatio;

            if (!_armed)
            {
                // Jest ancak silah bir kez bel üstüne çıktıktan sonra kurulur.
                if (weaponArenaY > waistY + rearmOffsetMeters)
                {
                    _armed = true;
                }

                return;
            }

            if (weaponArenaY < waistY - belowOffsetMeters)
            {
                _dwell += Time.deltaTime;
                if (_dwell >= dwellSeconds)
                {
                    // Sonuç önemsenmez: Weapon reddederse jest sıfırlanır, oyuncu tekrar dener.
                    weapon.TryStartReload();
                    _armed = false;
                    _dwell = 0f;
                }

                return;
            }

            _dwell = 0f;
        }

        private Transform ResolveHead()
        {
            if (head != null)
            {
                return head;
            }

            if (_resolvedHead != null)
            {
                return _resolvedHead;
            }

            _headRetryTimer -= Time.deltaTime;
            if (_headRetryTimer > 0f)
            {
                return null;
            }

            _headRetryTimer = CameraRetryIntervalSeconds;
            Camera main = Camera.main;
            if (main != null)
            {
                _resolvedHead = main.transform;
            }

            return _resolvedHead;
        }
    }
}
