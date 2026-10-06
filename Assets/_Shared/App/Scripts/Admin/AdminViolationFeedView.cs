using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Violation feed on the HUD's left side (CSS <c>.vfeed</c>) — the operator's to-do list,
    /// newest first.
    /// <para>⚠️ Separate from the kill feed by design (§10.9): that one is the match story, this one
    /// is work. Merged into one list, neither stays readable.</para>
    /// <para>Pooled from an inactive prefab template, same reason as
    /// <see cref="AdminKillFeedView"/>.</para>
    /// </summary>
    public class AdminViolationFeedView : MonoBehaviour
    {
        [Tooltip("Satır şablonu — prefabda PASİF durur, havuz bundan çoğaltılır.")]
        [SerializeField] private AdminViolationFeedRow template;

        [Tooltip("Ekranda tutulan en fazla satır.")]
        [SerializeField] private int maxRows = 8;

        [Tooltip("İki satır arasındaki boşluk (px).")]
        [SerializeField] private float rowGap = 6f;

        private readonly List<AdminViolationFeedRow> _rows = new List<AdminViolationFeedRow>();

        /// <summary>Player ids already seen while walking the feed newest-first; see <see cref="Redraw"/>.</summary>
        private readonly HashSet<int> _seen = new HashSet<int>();

        private int _version = int.MinValue;
        private int _visible;

        public void Bind(IReadOnlyList<AdminViolationFeedEntry> feed, int version)
        {
            if (version == _version)
            {
                return;
            }

            _version = version;
            Redraw(feed);
        }

        private void Update()
        {
            for (int i = 0; i < _visible; i++)
            {
                _rows[i].TickBlink();
            }
        }

        private void Redraw(IReadOnlyList<AdminViolationFeedEntry> feed)
        {
            int count = feed != null ? Mathf.Min(feed.Count, Mathf.Max(0, maxRows)) : 0;
            EnsurePool(count);
            _seen.Clear();

            float top = 0f;
            _visible = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= count)
                {
                    _rows[i].gameObject.SetActive(false);
                    continue;
                }

                AdminViolationFeedEntry entry = feed[feed.Count - 1 - i];

                // ⚠️ "Live" is the player's NEWEST line being a start: the feed is a log, so an older
                // start line whose end already arrived is history and must not keep blinking. The id
                // is recorded even for an end line — short-circuiting here would relight that start.
                bool newest = _seen.Add(entry.playerId);
                bool live = entry.active && newest;

                _rows[i].gameObject.SetActive(true);
                _rows[i].Bind(entry, live, top);
                top += (live ? AdminViolationFeedRow.LiveHeight : AdminViolationFeedRow.Height) + rowGap;
                _visible++;
            }
        }

        private void EnsurePool(int count)
        {
            while (_rows.Count < count)
            {
                if (template == null)
                {
                    Debug.LogWarning("[AdminViolationFeedView] Satır şablonu atanmadı; ihlal akışı çizilemiyor.");
                    return;
                }

                _rows.Add(Instantiate(template, transform));
            }
        }
    }
}
