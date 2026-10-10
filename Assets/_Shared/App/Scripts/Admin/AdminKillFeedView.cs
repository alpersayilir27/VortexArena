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
    /// <para>⚠️ Redraw is gated on <see cref="AdminRoster.KillFeedVersion"/> or a plate expiring, not
    /// on the HUD's 4 Hz tick: every bind re-measures eight variable-width plates.</para>
    /// <para>Plates leave <see cref="PlateSeconds"/> after arrival: the feed is the last moments of
    /// the match, not a log — a quiet match must not keep old kills on screen.</para>
    /// </summary>
    public class AdminKillFeedView : MonoBehaviour
    {
        /// <summary>Life of a plate (s), fade-out included.</summary>
        private const float PlateSeconds = 8f;

        /// <summary>Fade-out at the end of a plate's life (s).</summary>
        private const float FadeSeconds = 1f;

        [Tooltip("Satır şablonu — prefabda PASİF durur, havuz bundan çoğaltılır.")]
        [SerializeField] private AdminKillFeedRow template;

        [Tooltip("Ekranda tutulan en fazla satır; roster zaten AdminRoster.KillFeedMaxLines ile sınırlı.")]
        [SerializeField] private int maxRows = 8;

        [Tooltip("İki satır arasındaki boşluk (px).")]
        [SerializeField] private float rowGap = 6f;

        private readonly List<AdminKillFeedRow> _rows = new List<AdminKillFeedRow>();

        /// <summary>Last drawn feed version; <see cref="int.MinValue"/> forces the first draw.</summary>
        private int _version = int.MinValue;

        /// <summary>The roster's live list (oldest first); kept so expiry can run between binds.</summary>
        private IReadOnlyList<AdminKillFeedEntry> _feed;

        /// <summary>Plates drawn by the last redraw.</summary>
        private int _drawn;

        public void Bind(IReadOnlyList<AdminKillFeedEntry> feed, int version)
        {
            _feed = feed;
            if (version == _version)
            {
                return;
            }

            _version = version;
            Redraw(Time.unscaledTime);
        }

        private void Update()
        {
            if (template == null)
            {
                return; // nothing can be drawn; Redraw would warn every frame
            }

            // ⚠️ Expiry runs HERE: the version only moves on a new kill, so a quiet feed would
            // otherwise never age out.
            float now = Time.unscaledTime;
            if (CountFresh(now) != _drawn)
            {
                Redraw(now);
                return;
            }

            for (int i = 0; i < _drawn; i++)
            {
                AdminKillFeedEntry entry = _feed[_feed.Count - 1 - i];
                if (now - entry.time > PlateSeconds - FadeSeconds)
                {
                    _rows[i].SetAlpha(Alpha(i) * Life(entry, now));
                }
            }
        }

        /// <summary>Plates still within their life. The roster appends in time order, so the fresh
        /// ones are the newest — the walk stops at the first stale one.</summary>
        private int CountFresh(float now)
        {
            int limit = _feed != null ? Mathf.Min(_feed.Count, Mathf.Max(0, maxRows)) : 0;
            int count = 0;
            while (count < limit && now - _feed[_feed.Count - 1 - count].time < PlateSeconds)
            {
                count++;
            }

            return count;
        }

        private void Redraw(float now)
        {
            int count = CountFresh(now);
            EnsurePool(count);
            count = Mathf.Min(count, _rows.Count);

            float top = 0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= count)
                {
                    _rows[i].gameObject.SetActive(false);
                    continue;
                }

                // The roster appends, so the newest entry is the LAST one.
                AdminKillFeedEntry entry = _feed[_feed.Count - 1 - i];
                bool newest = i == 0;

                _rows[i].gameObject.SetActive(true);
                _rows[i].Bind(entry, WeaponLabel(entry), newest, Alpha(i) * Life(entry, now), top);
                top += (newest ? AdminKillFeedRow.NewHeight : AdminKillFeedRow.Height) + rowGap;
            }

            _drawn = count;
        }

        /// <summary>Expiry fade: 1 for most of the plate's life, down to 0 over the last
        /// <see cref="FadeSeconds"/>.</summary>
        private static float Life(AdminKillFeedEntry entry, float now)
        {
            return Mathf.Clamp01((PlateSeconds - (now - entry.time)) / FadeSeconds);
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
