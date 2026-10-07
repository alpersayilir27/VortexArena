using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.UI
{
    /// <summary>Ammo panel ON the weapon (magazine rounds + spare mags) in its own world-space canvas.
    /// <para><b>Lives on the canvas root</b>, not on <c>WPN_*</c>: <c>AmmoCanvas</c> is ONE prefab
    /// nested into every weapon, so the text bindings are set up once there. On the weapon root they
    /// would be per-weapon and silently empty on a new one. Finds its weapon via
    /// <see cref="Component.GetComponentInParent{T}(bool)"/>.</para>
    /// <para>⚠️ <b>Shown ONLY while the weapon is held</b> (<see cref="Weapon.IsHeld"/>): an unheld
    /// visible weapon is one frozen in a rack frame, and its ammo is not the player's. Hidden via the
    /// <see cref="Canvas"/> component, NOT the GameObject — deactivating it would drop the event
    /// subscriptions and the panel would never come back.</para>
    /// <para>⚠️ <b>Look comes from the PREFAB</b> (size, position, alignment, divider, icons, COLOR):
    /// this class writes only the two texts and drives the low-ammo highlight. The normal color is
    /// read from the prefab in <see cref="Awake"/> — hardcoding it would silently revert an
    /// Inspector color change on the first shot. Divider ("/") and icons are static prefab content
    /// with no code counterpart.</para>
    /// <para>Refreshed by events ONLY (<see cref="Weapon.AmmoChanged"/>, reload/held events), no
    /// per-frame work; <see cref="TMP_Text.SetText(string, float)"/> allocates no string per shot.</para></summary>
    [RequireComponent(typeof(Canvas))]
    public class WeaponAmmoPanel : MonoBehaviour
    {
        /// <summary>At or below this ammo count the text turns to the highlight color.</summary>
        private const int LowAmmoThreshold = 5;

        /// <summary>Low ammo / reload highlight.</summary>
        private static readonly Color LowAmmoColor = new Color(1f, 0.32f, 0.26f);

        [Tooltip("Şarjördeki mermi. Konumu/puntosu/rengi prefabta ayarlanır.")]
        [SerializeField] private TMP_Text ammoText;

        [Tooltip("Yedek şarjör sayısı (fişek havuzlu silahta kalan fişek). Konumu/puntosu " +
                 "prefabta ayarlanır.")]
        [SerializeField] private TMP_Text magText;

        private Weapon _weapon;
        private Canvas _canvas;
        private Color _normalColor = Color.white;

        private void Awake()
        {
            // ⚠️ includeInactive: the panel must find its weapon even while the model is off (frame
            // clone hidden) — a miss is never retried and the panel would stay dead.
            _weapon = GetComponentInParent<Weapon>(true);
            _canvas = GetComponent<Canvas>();

            if (ammoText != null)
            {
                _normalColor = ammoText.color;
            }
        }

        private void OnEnable()
        {
            if (_weapon == null)
            {
                return;
            }

            _weapon.AmmoChanged += Refresh;
            _weapon.ReloadStarted += HandleReloadStarted;
            _weapon.ReloadCompleted += Refresh;
            _weapon.HeldChanged += HandleHeldChanged;

            // The reserve rule can flip while the weapon is held (staging → match).
            ModeRuntime.Changed += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            if (_weapon == null)
            {
                return;
            }

            _weapon.AmmoChanged -= Refresh;
            _weapon.ReloadStarted -= HandleReloadStarted;
            _weapon.ReloadCompleted -= Refresh;
            _weapon.HeldChanged -= HandleHeldChanged;
            ModeRuntime.Changed -= Refresh;
        }

        // Reload carries a duration, held carries a flag: both only mean "refresh".
        private void HandleReloadStarted(float duration) => Refresh();

        private void HandleHeldChanged(bool held) => Refresh();

        private void Refresh()
        {
            if (_weapon == null)
            {
                return;
            }

            bool held = _weapon.IsHeld;
            _canvas.enabled = held;
            if (!held)
            {
                return;
            }

            WriteAmmo();
            WriteReserve();
        }

        /// <summary>Magazine rounds; a waiting mark instead of the count while reloading.</summary>
        private void WriteAmmo()
        {
            if (ammoText == null)
            {
                return;
            }

            if (_weapon.IsReloading)
            {
                ammoText.color = LowAmmoColor;
                ammoText.SetText("···");
                return;
            }

            ammoText.color = _weapon.CurrentAmmo <= LowAmmoThreshold ? LowAmmoColor : _normalColor;
            ammoText.SetText("{0}", _weapon.CurrentAmmo);
        }

        /// <summary>"∞" under an infinite reserve (§10.5 <c>limitedReserve:false</c>); otherwise spare
        /// MAGAZINES, or spare ROUNDS on a <see cref="WeaponReserveMode.PoolRounds"/> weapon —
        /// dividing a round pool into mags would read "0 spare" with 6 rounds in hand.
        /// <para>⚠️ "∞" is not in the static <c>LiberationSans SDF</c> atlas; it comes from the
        /// dynamic fallback — keep that fallback on the font.</para></summary>
        private void WriteReserve()
        {
            if (magText == null)
            {
                return;
            }

            if (_weapon.HasInfiniteReserve)
            {
                magText.SetText("∞");
                return;
            }

            WeaponDefinition definition = _weapon.Definition;
            bool pooled = definition != null && definition.ReserveMode == WeaponReserveMode.PoolRounds;

            magText.SetText("{0}", pooled ? _weapon.ReserveRounds : _weapon.SpareMagazineCount);
        }
    }
}
