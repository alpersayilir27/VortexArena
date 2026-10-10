using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Violation feed on the HUD's left side (CSS <c>.vfeed</c>) — the operator's to-do list,
    /// newest first.
    /// <para>⚠️ Separate from the kill feed by design (§10.9): that one is the match story, this one
    /// is work. Merged into one list, neither stays readable.</para>
    /// <para>A LIVE row stays as long as the violation does; every other row leaves
    /// <see cref="CalmRowSeconds"/> after it arrived — a finished violation is a note, not work.</para>
    /// <para>Pooled from an inactive prefab template, same reason as
    /// <see cref="AdminKillFeedView"/>.</para>
    /// </summary>
    public class AdminViolationFeedView : MonoBehaviour
    {
        /// <summary>Life of a calm (non-live) row (s).</summary>
        private const float CalmRowSeconds = 10f;

        /// <summary>Lines classified per walk — two bits each must fit the int mask; the roster keeps
        /// far fewer (<see cref="AdminRoster.ViolationFeedMaxLines"/>).</summary>
        private const int MaxClassified = 16;

        [Tooltip("Satır şablonu — prefabda PASİF durur, havuz bundan çoğaltılır.")]
        [SerializeField] private AdminViolationFeedRow template;

        [Tooltip("Ekranda tutulan en fazla satır.")]
        [SerializeField] private int maxRows = 8;

        [Tooltip("İki satır arasındaki boşluk (px).")]
        [SerializeField] private float rowGap = 6f;

        private readonly List<AdminViolationFeedRow> _rows = new List<AdminViolationFeedRow>();

        /// <summary>Player ids already seen while walking the feed newest-first; see <see cref="Classify"/>.</summary>
        private readonly HashSet<int> _seen = new HashSet<int>();

        /// <summary>The roster's live list (oldest first); kept so expiry can run between binds.</summary>
        private IReadOnlyList<AdminViolationFeedEntry> _feed;

        private int _version = int.MinValue;
        private int _visible;

        /// <summary><see cref="Classify"/> result of the last redraw.</summary>
        private int _drawnMask = -1;

        public void Bind(IReadOnlyList<AdminViolationFeedEntry> feed, int version)
        {
            _feed = feed;
            if (version == _version)
            {
                return;
            }

            _version = version;
            Redraw();
        }

        private void Update()
        {
            // ⚠️ Re-classified HERE, not only on Bind: the version moves only on a new line, while a
            // row must also leave when it ages out or its violation ends without an end line (the
            // server closes a dropped player's violation silently).
            if (template != null && Classify(Time.unscaledTime) != _drawnMask)
            {
                Redraw();
            }

            for (int i = 0; i < _visible; i++)
            {
                _rows[i].TickBlink();
            }
        }

        /// <summary>
        /// Walks the feed newest-first; bit <c>2i</c> = line i is shown, bit <c>2i+1</c> = it is live.
        /// The same walk drives the change check and the redraw, so they cannot disagree.
        /// <para>⚠️ "Live" is the player's NEWEST line being a start AND that kind still violating
        /// now: the feed is a log, so an older start whose end already arrived is history. The id is
        /// recorded even for an end line — short-circuiting here would relight that start.</para>
        /// </summary>
        private int Classify(float now)
        {
            _seen.Clear();
            int count = _feed != null ? _feed.Count : 0;
            int mask = 0;

            for (int i = 0; i < count && i < MaxClassified; i++)
            {
                AdminViolationFeedEntry entry = _feed[count - 1 - i];
                bool newest = _seen.Add(entry.playerId);
                bool live = entry.active && newest && StillViolating(entry);
                bool shown = live || now - entry.time < CalmRowSeconds;

                if (shown)
                {
                    mask |= 1 << (2 * i);
                }

                if (live)
                {
                    mask |= 2 << (2 * i);
                }
            }

            return mask;
        }

        /// <summary>The snapshot bit of the line's kind (same source as the ring). An unknown kind has
        /// no bit to ask, so it stays live until its end line — a kind the client does not understand
        /// must not be dropped.</summary>
        private static bool StillViolating(AdminViolationFeedEntry entry)
        {
            AdminViolationKind kind = AdminViolations.Kind(entry.kind);
            return kind == AdminViolationKind.None || AdminViolations.IsLive(entry.playerId, kind);
        }

        private void Redraw()
        {
            int mask = Classify(Time.unscaledTime);
            _drawnMask = mask;

            int lines = _feed != null ? Mathf.Min(_feed.Count, MaxClassified) : 0;
            int shown = 0;
            for (int i = 0; i < lines; i++)
            {
                if ((mask & (1 << (2 * i))) != 0)
                {
                    shown++;
                }
            }

            int count = Mathf.Min(shown, Mathf.Max(0, maxRows));
            EnsurePool(count);
            count = Mathf.Min(count, _rows.Count);

            float top = 0f;
            _visible = 0;
            for (int i = 0; i < lines && _visible < count; i++)
            {
                if ((mask & (1 << (2 * i))) == 0)
                {
                    continue;
                }

                bool live = (mask & (2 << (2 * i))) != 0;
                AdminViolationFeedEntry entry = _feed[_feed.Count - 1 - i];
                AdminViolationFeedRow row = _rows[_visible];

                row.gameObject.SetActive(true);
                row.Bind(entry, live, top);
                top += (live ? AdminViolationFeedRow.LiveHeight : AdminViolationFeedRow.Height) + rowGap;
                _visible++;
            }

            for (int i = _visible; i < _rows.Count; i++)
            {
                _rows[i].gameObject.SetActive(false);
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
