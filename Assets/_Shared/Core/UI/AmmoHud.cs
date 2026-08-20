using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// CS-style ammo indicator: the name of the held weapon(s) + rounds in the magazine +
    /// spare magazine bars, in a small text that sits at the BOTTOM RIGHT of the field of view.
    /// The panel is carried by <see cref="HudFollow"/> (per the Meta guideline it is NOT hard-locked
    /// to the head, it lazily follows); the text child is shifted right with a +X offset.
    /// <para>
    /// It bootstraps itself (needs no scene setup, the RemoteShotFx pattern) and refreshes ONLY on
    /// events: <see cref="Weapon.ActiveChanged"/> refreshes the subscription list, and the per-weapon
    /// Ammo/Reload events refresh the text — it does no per-frame work.
    /// If no weapon is held the text is hidden.
    /// </para>
    /// </summary>
    public class AmmoHud : MonoBehaviour
    {
        private const float LowAmmoThreshold = 5f;
        private static readonly Color NormalColor = Color.white;

        /// <summary>Low-ammo/reload highlight (WeaponAmmoDisplay's former red).</summary>
        private static readonly Color LowAmmoColor = new Color(1f, 0.32f, 0.26f);

        /// <summary>The prefab's path inside <c>Resources</c> (without extension).</summary>
        public const string ResourcePath = "UI/AmmoHud";

        private static AmmoHud _instance;

        // ⚠️ The look comes FROM THE PREFAB (`_Shared/App/Resources/UI/AmmoHud.prefab`): font size,
        // position and alignment are edited there. This class only writes the text and drives the color.
        [Tooltip("Cephane metni — konumu/puntosu prefabta ayarlanır.")]
        [SerializeField] private TMP_Text _label;
        private readonly List<Weapon> _subscribed = new List<Weapon>();
        private readonly StringBuilder _builder = new StringBuilder(96);

        /// <summary>
        /// Once per scene load: creates the HUD if it does not exist (DDOL — it lives across scenes).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var prefab = Resources.Load<AmmoHud>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[AmmoHud] '{ResourcePath}' prefabı bulunamadı — cephane " +
                               "göstergesi çizilemeyecek.");
                return;
            }

            AmmoHud hud = Instantiate(prefab);
            hud.name = "[AmmoHud]";
            DontDestroyOnLoad(hud.gameObject);
            _instance = hud;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (_label != null)
            {
                _label.gameObject.SetActive(false); // hidden while no weapon is held
            }
        }

        private void OnEnable()
        {
            Weapon.ActiveChanged += HandleActiveChanged;
            GameplayHudGate.HiddenChanged += HandleHudGate;
            HandleActiveChanged();
        }

        private void OnDisable()
        {
            Weapon.ActiveChanged -= HandleActiveChanged;
            GameplayHudGate.HiddenChanged -= HandleHudGate;
            UnsubscribeAll();
        }

        /// <summary>The match result overlay opened/closed (<see cref="GameplayHudGate"/>): the ammo
        /// indicator is an in-game HUD too, so it yields the screen to it.</summary>
        private void HandleHudGate(bool hidden)
        {
            Refresh();
        }

        // ----------------------------------------------------------- subscriptions

        /// <summary>The weapon list/held state changed: refresh the subscriptions and the text.</summary>
        private void HandleActiveChanged()
        {
            UnsubscribeAll();

            foreach (Weapon weapon in Weapon.Active)
            {
                weapon.AmmoChanged += Refresh;
                weapon.ReloadStarted += HandleReloadStarted;
                weapon.ReloadCompleted += Refresh;
                _subscribed.Add(weapon);
            }

            Refresh();
        }

        private void UnsubscribeAll()
        {
            foreach (Weapon weapon in _subscribed)
            {
                if (weapon == null)
                {
                    continue;
                }

                weapon.AmmoChanged -= Refresh;
                weapon.ReloadStarted -= HandleReloadStarted;
                weapon.ReloadCompleted -= Refresh;
            }

            _subscribed.Clear();
        }

        private void HandleReloadStarted(float duration)
        {
            Refresh();
        }

        // ------------------------------------------------------------------- text

        private void Refresh()
        {
            if (_label == null)
            {
                return;
            }

            _builder.Length = 0;
            bool any = false;

            if (GameplayHudGate.Hidden)
            {
                _label.gameObject.SetActive(false);
                return;
            }

            foreach (Weapon weapon in Weapon.Active)
            {
                if (weapon == null || !weapon.IsHeld)
                {
                    continue;
                }

                if (any)
                {
                    _builder.Append('\n');
                }

                AppendLine(weapon);
                any = true;
            }

            _label.gameObject.SetActive(any);
            if (any)
            {
                _label.text = _builder.ToString();
            }
        }

        private void AppendLine(Weapon weapon)
        {
            bool low = !weapon.IsReloading && weapon.CurrentAmmo <= LowAmmoThreshold;
            if (low)
            {
                _builder.Append("<color=#FF5242>");
            }

            _builder.Append(weapon.WeaponName).Append("  ");

            if (weapon.IsReloading)
            {
                _builder.Append("···");
            }
            else
            {
                _builder.Append(weapon.CurrentAmmo);
            }

            WeaponDefinition def = weapon.Definition;
            if (def != null && def.ReserveMode == WeaponReserveMode.PoolRounds)
            {
                _builder.Append('|').Append(weapon.ReserveRounds);
            }
            else
            {
                int spare = weapon.SpareMagazineCount;
                if (spare > 0)
                {
                    _builder.Append(' ');
                    for (int i = 0; i < spare; i++)
                    {
                        _builder.Append('|');
                    }
                }
            }

            if (low)
            {
                _builder.Append("</color>");
            }
        }
    }
}
