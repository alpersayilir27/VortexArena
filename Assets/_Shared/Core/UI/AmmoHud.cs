using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// CS tarzı cephane göstergesi: tutulan silah(lar)ın adı + şarjördeki mermi +
    /// yedek şarjör çubukları, görüş alanının SAĞ ALTINA düşen küçük bir metinde.
    /// Panel <see cref="HudFollow"/> ile taşınır (Meta kılavuzu gereği kafaya sert
    /// kilitli DEĞİL, tembel takipli); metin child'ı +X ofsetiyle sağa kaydırılır.
    /// <para>
    /// Kendini önyükler (sahne kurulumu gerektirmez, RemoteShotFx kalıbı) ve YALNIZ
    /// olaylarla yenilenir: <see cref="Weapon.ActiveChanged"/> abonelik listesini,
    /// silah başına Ammo/Reload olayları metni tazeler — frame başına iş yapmaz.
    /// Hiç silah tutulmuyorsa metin gizlidir.
    /// </para>
    /// </summary>
    public class AmmoHud : MonoBehaviour
    {
        private const float LowAmmoThreshold = 5f;
        private static readonly Color NormalColor = Color.white;

        /// <summary>Düşük mermi/reload vurgusu (WeaponAmmoDisplay'in eski kırmızısı).</summary>
        private static readonly Color LowAmmoColor = new Color(1f, 0.32f, 0.26f);

        private static AmmoHud _instance;

        private TMP_Text _label;
        private readonly List<Weapon> _subscribed = new List<Weapon>();
        private readonly StringBuilder _builder = new StringBuilder(96);

        /// <summary>
        /// Her sahne yüklemesinde bir kez: HUD yoksa yaratır (DDOL — sahneler arası yaşar).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[AmmoHud]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AmmoHud>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Panel: HudFollow kökü taşır; metin sağ-alt hissi için +X'e kaydırılmış child'da.
            gameObject.AddComponent<HudFollow>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0.34f, -0.10f, 0f);

            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.fontSize = 0.55f;
            tmp.alignment = TextAlignmentOptions.Right;
            tmp.rectTransform.sizeDelta = new Vector2(0.8f, 0.3f);
            tmp.rectTransform.pivot = new Vector2(1f, 0.5f);
            tmp.color = NormalColor;
            tmp.text = "";
            _label = tmp;
            _label.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            Weapon.ActiveChanged += HandleActiveChanged;
            HandleActiveChanged();
        }

        private void OnDisable()
        {
            Weapon.ActiveChanged -= HandleActiveChanged;
            UnsubscribeAll();
        }

        // ------------------------------------------------------------ abonelikler

        /// <summary>Silah listesi/tutulma durumu değişti: abonelikleri ve metni tazele.</summary>
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

        // ------------------------------------------------------------------ metin

        private void Refresh()
        {
            if (_label == null)
            {
                return;
            }

            _builder.Length = 0;
            bool any = false;

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
