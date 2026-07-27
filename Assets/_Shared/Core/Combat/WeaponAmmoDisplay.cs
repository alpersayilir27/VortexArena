using TMPro;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah üstü world-space cephane göstergesi. Yalnız Weapon olaylarında yenilenir —
    /// frame başına iş yapmaz (Quest bütçesi). Reload sürerken sayı yerine
    /// <c>reloadText</c> gösterilir. Yedek gösterimi ReserveMode'a göre değişir:
    /// PoolRounds "12|48", DiscardMagazine "12 ||" (yedek şarjör başına bir çubuk;
    /// yedek yoksa yalnız sayı). Çubuk karakteri ASCII '|' — '▮' LiberationSans SDF
    /// fontunda yok, TMP eksik-karakter uyarısı basıyordu.
    /// </summary>
    public class WeaponAmmoDisplay : MonoBehaviour
    {
        [SerializeField] private Weapon weapon;
        [SerializeField] private TMP_Text label;
        [Tooltip("Bu sayıda ve altında etiket lowAmmoColor'a döner.")]
        [SerializeField] private int lowAmmoThreshold = 5;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color lowAmmoColor = new Color(1f, 0.32f, 0.26f);
        [Tooltip("Reload sürerken gösterilen metin.")]
        [SerializeField] private string reloadText = "···";

        private void OnEnable()
        {
            if (weapon == null)
            {
                return;
            }

            weapon.AmmoChanged += Refresh;
            weapon.ReloadStarted += HandleReloadStarted;
            weapon.ReloadCompleted += Refresh;
            weapon.HeldChanged += HandleHeldChanged;
        }

        private void OnDisable()
        {
            if (weapon == null)
            {
                return;
            }

            weapon.AmmoChanged -= Refresh;
            weapon.ReloadStarted -= HandleReloadStarted;
            weapon.ReloadCompleted -= Refresh;
            weapon.HeldChanged -= HandleHeldChanged;
        }

        private void Start()
        {
            Refresh();
        }

        private void HandleReloadStarted(float duration)
        {
            Refresh();
        }

        private void HandleHeldChanged(bool held)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (weapon == null || label == null)
            {
                return;
            }

            label.text = BuildText();
            label.color = weapon.CurrentAmmo <= lowAmmoThreshold ? lowAmmoColor : normalColor;
        }

        private string BuildText()
        {
            if (weapon.IsReloading)
            {
                return reloadText;
            }

            WeaponDefinition def = weapon.Definition;
            if (def != null && def.ReserveMode == WeaponReserveMode.PoolRounds)
            {
                return $"{weapon.CurrentAmmo}|{weapon.ReserveRounds}";
            }

            int spare = weapon.SpareMagazineCount;
            if (spare <= 0)
            {
                return weapon.CurrentAmmo.ToString();
            }

            return weapon.CurrentAmmo + " " + new string('|', spare);
        }
    }
}
