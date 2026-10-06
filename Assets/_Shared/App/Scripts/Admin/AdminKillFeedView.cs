using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Kill feed on the HUD's right side (CSS <c>.kfeed</c>): newest plate on top, older ones fading
    /// out below it.
    /// <para>⚠️ <b>Pooled:</b> rows are cloned once from an inactive prefab template and reused —
    /// Instantiate/Destroy per kill would churn GC during the busiest seconds of a match.</para>
    /// <para>⚠️ Redraw is gated on <see cref="AdminRoster.KillFeedVersion"/>, not on the HUD's 4 Hz
    /// tick: every bind re-measures eight variable-width plates.</para>
    /// </summary>
    public class AdminKillFeedView : MonoBehaviour
    {
        [Tooltip("Satır şablonu — prefabda PASİF durur, havuz bundan çoğaltılır.")]
        [SerializeField] private AdminKillFeedRow template;

        [Tooltip("Ekranda tutulan en fazla satır; roster zaten AdminRoster.KillFeedMaxLines ile sınırlı.")]
        [SerializeField] private int maxRows = 8;

        [Tooltip("İki satır arasındaki boşluk (px).")]
        [SerializeField] private float rowGap = 6f;

        private readonly List<AdminKillFeedRow> _rows = new List<AdminKillFeedRow>();

        /// <summary>Last drawn feed version; <see cref="int.MinValue"/> forces the first draw.</summary>
        private int _version = int.MinValue;

        public void Bind(IReadOnlyList<AdminKillFeedEntry> feed, int version)
        {
            if (version == _version)
            {
                return;
            }

            _version = version;
            Redraw(feed);
        }

        private void Redraw(IReadOnlyList<AdminKillFeedEntry> feed)
        {
            int count = feed != null ? Mathf.Min(feed.Count, Mathf.Max(0, maxRows)) : 0;
            EnsurePool(count);

            float top = 0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= count)
                {
                    _rows[i].gameObject.SetActive(false);
                    continue;
                }

                // The roster appends, so the newest entry is the LAST one.
                AdminKillFeedEntry entry = feed[feed.Count - 1 - i];
                bool newest = i == 0;

                _rows[i].gameObject.SetActive(true);
                _rows[i].Bind(entry, WeaponLabel(entry), newest, Alpha(i), top);
                top += (newest ? AdminKillFeedRow.NewHeight : AdminKillFeedRow.Height) + rowGap;
            }
        }

        private void EnsurePool(int count)
        {
            while (_rows.Count < count)
            {
                if (template == null)
                {
                    Debug.LogWarning("[AdminKillFeedView] Satır şablonu atanmadı; öldürme akışı çizilemiyor.");
                    return;
                }

                _rows.Add(Instantiate(template, transform));
            }
        }

        /// <summary>CSS <c>.kf:nth-child</c> age fade — the feed reads newest-first without the
        /// operator having to check timestamps.</summary>
        private static float Alpha(int index)
        {
            switch (index)
            {
                case 0:
                case 1: return 1f;
                case 2: return 0.88f;
                case 3: return 0.74f;
                case 4: return 0.64f;
                default: return 0.54f;
            }
        }

        /// <summary>Weapon plate text. Falls back to the raw <c>weaponId</c>: a catalog gap must not
        /// erase "what killed this player".</summary>
        private static string WeaponLabel(AdminKillFeedEntry entry)
        {
            if (entry.kind != AdminKillKind.Kill || string.IsNullOrEmpty(entry.weaponId))
            {
                return "";
            }

            WeaponCatalog catalog = WeaponCatalog.Load();
            WeaponDefinition definition = catalog != null ? catalog.FindByWeaponId(entry.weaponId) : null;
            return definition != null && !string.IsNullOrEmpty(definition.DisplayName)
                ? definition.DisplayName
                : entry.weaponId;
        }
    }
}
