using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Client-side mirror of the state <b>shared</b> between admins (§5.3 <c>admin_state</c>):
    /// next match's mode/map selection, the last admin action's notice and the online admin count.
    /// <para>
    /// <b>Authority is on the server.</b> The pickers change the server-side selection via
    /// <c>set_selection</c>, which is broadcast back to every admin — that is why two operators see
    /// the same screen.
    /// </para>
    /// <para>
    /// ⚠ <b>View preferences do NOT belong here</b> (camera mode/speed, selected player, rings, name
    /// labels, roof transparency): they are per-screen and stay in <see cref="AdminSession"/>
    /// (<c>PlayerPrefs</c>). Tying two operators' cameras together makes management impossible.
    /// </para>
    /// <para>
    /// State and event are <b>static</b> (the <see cref="AdminSession"/> pattern) so listeners need
    /// not know when the component was installed; the component is only a network event pump,
    /// installed by <see cref="AdminSpectator"/>.
    /// </para>
    /// </summary>
    public class AdminSelection : MonoBehaviour
    {
        /// <summary>Shared selection, notice or admin count changed (main thread).</summary>
        public static event Action Changed;

        /// <summary>Shared selection: mode id from the server (empty = never selected).</summary>
        public static string ModeId { get; private set; } = "";

        /// <summary>Shared selection: map scene name from the server (empty = never selected).</summary>
        public static string SceneName { get; private set; } = "";

        /// <summary>Shared selection: next match's duration (s); <c>0</c> = the mode's default (§5.2).</summary>
        public static int RoundSeconds { get; private set; }

        /// <summary>Shared selection: next match's score/round limit; <c>0</c> = the mode's default,
        /// <c>ArenaProtocol.SCORE_LIMIT_UNLIMITED</c> = <b>unlimited</b> (§5.2). ⚠️ Three-valued —
        /// readers ask <c>!= 0</c>, not <c>&gt; 0</c>.</summary>
        public static int ScoreLimit { get; private set; }

        /// <summary>Shared selection: countdown length (s); <c>0</c> = protocol default
        /// (<c>COUNTDOWN_SECONDS</c>). Also the between-rounds countdown in round-based modes (§5.2).</summary>
        public static int CountdownSeconds { get; private set; }

        /// <summary>
        /// Friendly fire's EFFECTIVE value (§5.2) — not a "selection": it applies to the running
        /// match immediately, so it is exempt from the selection lock
        /// (<c>AdminRoster.CanChangeSelection</c>).
        /// </summary>
        public static bool FriendlyFire { get; private set; }

        /// <summary>
        /// Calibration mode's EFFECTIVE value (<c>ArenaProtocol.CALIB_MODE_*</c>, §5.2) — like
        /// <see cref="FriendlyFire"/>, a live state exempt from the selection lock. Empty = no
        /// <c>admin_state</c> yet.
        /// </summary>
        public static string CalibrationMode { get; private set; } = "";

        /// <summary>Online admin count reported by the server (including ourselves).</summary>
        public static int AdminCount { get; private set; }

        /// <summary>Last admin action's notice ("&lt;name&gt;: &lt;action&gt;"); may be empty.</summary>
        public static string LastNotice { get; private set; } = "";

        /// <summary>Venue opened by the server this session (§11); empty when there is no venue split.</summary>
        public static string VenueId { get; private set; } = "";

        /// <summary>
        /// Scene names playable in this venue — the map picker's filter.
        /// <para>⚠️ <b>Empty = no filtering</b> (no venue split, or no admin_state yet). The server
        /// decides which arenas are playable, so the list is never produced locally.</para>
        /// </summary>
        public static IReadOnlyList<string> VenueScenes => _venueScenes;

        /// <summary>
        /// Venue filter version — bumped whenever <see cref="VenueScenes"/> changes.
        /// <para>
        /// The map picker re-filters by watching this. Needed because the list is built <b>before
        /// the connection</b> (panel <c>Initialize</c>) when the filter is still empty, and "did the
        /// selection change" does not catch it — venue info arrives independently of the selection.
        /// </para>
        /// </summary>
        public static int VenueVersion { get; private set; }

        private static string[] _venueScenes = Array.Empty<string>();

        /// <summary>Is the scene playable in this venue; an empty list passes everything.</summary>
        public static bool IsInVenue(string sceneName)
        {
            if (_venueScenes.Length == 0 || string.IsNullOrEmpty(sceneName))
            {
                return true;
            }

            for (int i = 0; i < _venueScenes.Length; i++)
            {
                if (string.Equals(_venueScenes[i], sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnEnable()
        {
            NetEvents.OnAdminState += HandleAdminState;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDisable()
        {
            NetEvents.OnAdminState -= HandleAdminState;
            NetEvents.OnDisconnected -= HandleDisconnected;
        }

        private static void HandleAdminState(AdminStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string modeId = msg.modeId ?? "";
            string sceneName = msg.sceneName ?? "";
            string calibrationMode = msg.calibrationMode ?? "";
            bool changed = modeId != ModeId || sceneName != SceneName ||
                           calibrationMode != CalibrationMode ||
                           msg.roundSeconds != RoundSeconds || msg.scoreLimit != ScoreLimit ||
                           msg.countdownSeconds != CountdownSeconds ||
                           msg.friendlyFire != FriendlyFire ||
                           msg.adminCount != AdminCount;

            string venueId = msg.venueId ?? "";
            string[] venueScenes = msg.venueScenes ?? Array.Empty<string>();
            bool venueChanged = venueId != VenueId || !SameScenes(venueScenes, _venueScenes);
            changed |= venueChanged;
            if (venueChanged)
            {
                VenueVersion++;
            }

            ModeId = modeId;
            SceneName = sceneName;
            RoundSeconds = msg.roundSeconds;
            ScoreLimit = msg.scoreLimit;
            CountdownSeconds = msg.countdownSeconds;
            FriendlyFire = msg.friendlyFire;
            CalibrationMode = calibrationMode;
            AdminCount = msg.adminCount;
            VenueId = venueId;
            _venueScenes = venueScenes;

            // Shown on the sending admin too, so "who did what" stays in one line (same place as
            // AdminCommands.Status: the server-confirmed text overwrites the local "sent" text).
            if (!string.IsNullOrEmpty(msg.notice))
            {
                LastNotice = msg.notice;
                AdminCommands.Note(msg.notice);
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        private static void HandleDisconnected()
        {
            // Selection values are not invented while disconnected (the last known ones stay, the
            // panel says "not connected"); only counter/notice are cleared. ⚠️ The venue filter is
            // KEPT too — otherwise a dropped connection would show other venues' arenas and take
            // them back on reconnect.
            AdminCount = 0;
            LastNotice = "";
            Changed?.Invoke();
        }

        private static bool SameScenes(string[] a, string[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
