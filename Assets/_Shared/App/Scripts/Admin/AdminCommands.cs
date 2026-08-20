using System;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Single outbound gate for admin commands (§5.2). Authority is on the SERVER: only requests
    /// leave here, the server accepts or rejects and logs the reason — the UI says "sent", never
    /// "done".
    /// <para><see cref="Status"/> is the human-readable result of the last operation, shown by the
    /// preferences panel.</para>
    /// </summary>
    public static class AdminCommands
    {
        /// <summary>Result of the last command/attempt (shown in the UI).</summary>
        public static string Status { get; private set; } = "";

        /// <summary>Raised when the status text changes.</summary>
        public static event Action StatusChanged;

        /// <summary>
        /// Starts the match. <paramref name="roundSeconds"/>/<paramref name="scoreLimit"/> are per
        /// match; <c>0</c> means the server uses the mode default (§5.2).
        /// <para><paramref name="scoreLimit"/> may also be <b>unlimited</b>
        /// (<see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>). ⚠️ Hence a negative value is NOT
        /// clamped to <c>0</c> — clamping would silently turn "unlimited" into "mode default".</para>
        /// </summary>
        public static void StartMatch(string modeId, string sceneName, int roundSeconds = 0,
            int scoreLimit = 0, int countdownSeconds = 0)
        {
            if (string.IsNullOrEmpty(modeId) || string.IsNullOrEmpty(sceneName))
            {
                SetStatus("Mod/harita seçilmedi; maç başlatılamadı.");
                return;
            }

            var msg = new StartMatchMsg
            {
                modeId = modeId,
                sceneName = sceneName,
                roundSeconds = Mathf.Max(0, roundSeconds),
                scoreLimit = ArenaProtocol.NormalizeScoreLimit(scoreLimit),
                countdownSeconds = Mathf.Max(0, countdownSeconds)
            };

            if (Send(msg))
            {
                string parameters = msg.roundSeconds > 0 || msg.scoreLimit != 0 || msg.countdownSeconds > 0
                    ? $" ({(msg.roundSeconds > 0 ? FormatDuration(msg.roundSeconds) : "mod süresi")}" +
                      $" · {(msg.scoreLimit != 0 ? "limit " + FormatScoreLimit(msg.scoreLimit) : "mod limiti")}" +
                      $"{(msg.countdownSeconds > 0 ? " · geri sayım " + msg.countdownSeconds + " sn" : "")})"
                    : "";
                SetStatus($"Maç isteği gönderildi: {modeId} · {sceneName}{parameters}");
            }
        }

        /// <summary>Formats the score/round limit for the operator. The only place the three cases
        /// are written — panel, stats summary and status line all read from here.</summary>
        public static string FormatScoreLimit(int scoreLimit) =>
            scoreLimit > 0 ? scoreLimit.ToString()
            : scoreLimit < 0 ? "sınırsız"
            : "mod varsayılanı";

        /// <summary>Formats seconds for the operator ("2.5 dk", "1 saat").</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
            {
                return "-";
            }

            if (seconds % 3600 == 0)
            {
                return $"{seconds / 3600} saat";
            }

            float minutes = seconds / 60f;
            return Mathf.Approximately(minutes, Mathf.Round(minutes))
                ? $"{Mathf.RoundToInt(minutes)} dk"
                : $"{minutes:0.#} dk";
        }

        /// <summary>
        /// <b>Shared</b> mode/map selection for the next match (§5.2 <c>set_selection</c>). Does not
        /// start it: the server holds the selection and broadcasts it via <c>admin_state</c>, so the
        /// UI never writes it locally — single source of truth, two operators cannot diverge.
        /// <para>No status line here; the server's own notice will arrive.</para>
        /// <para>Duration/limit ride the same channel: kept local, one operator's "5 min" match
        /// would start with the other's 30. <c>0</c> = "leave this field alone".</para>
        /// <para>⚠️ <paramref name="scoreLimit"/> is the exception: a negative value
        /// (<see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>) means <b>unlimited was chosen</b>,
        /// not "untouched" — so it is neither clamped nor counted as an empty command.</para>
        /// </summary>
        public static void SetSelection(string modeId, string sceneName, int roundSeconds = 0,
            int scoreLimit = 0, int countdownSeconds = 0)
        {
            int limit = ArenaProtocol.NormalizeScoreLimit(scoreLimit);
            if (string.IsNullOrEmpty(modeId) && string.IsNullOrEmpty(sceneName) &&
                roundSeconds <= 0 && limit == 0 && countdownSeconds <= 0)
            {
                return;
            }

            Send(new SetSelectionMsg
            {
                modeId = modeId ?? "",
                sceneName = sceneName ?? "",
                roundSeconds = Mathf.Max(0, roundSeconds),
                scoreLimit = limit,
                countdownSeconds = Mathf.Max(0, countdownSeconds)
            });
        }

        /// <summary>
        /// Friendly fire switch (§5.2 <c>set_friendly_fire</c>) — <b>a live command, not a
        /// selection</b>: it applies to a running match, so it does not ride <c>set_selection</c>
        /// (whose "empty/0 = leave alone" contract cannot express a <c>bool</c>) and is not blocked
        /// by the selection lock.
        /// <para>Never written locally: the server echoes it via <c>admin_state</c> — single source
        /// of truth.</para>
        /// </summary>
        public static void SetFriendlyFire(bool enabled)
        {
            Send(new SetFriendlyFireMsg { enabled = enabled });
        }

        /// <summary>
        /// How headsets align AT STARTUP (§5.2 <c>set_calibration_mode</c>) — same class as
        /// <see cref="SetFriendlyFire"/>: a live command, not a selection.
        /// <para>Never written locally: the server echoes it via <c>admin_state</c>.</para>
        /// <para>⚠️ <c>anchor_cloud</c> is never sent from here: it is reserved and the server
        /// rejects it — the UI keeps that option disabled.</para>
        /// </summary>
        public static void SetCalibrationMode(string mode)
        {
            if (Send(new SetCalibrationModeMsg { mode = mode ?? "" }))
            {
                SetStatus($"Kalibre modu gönderildi: {CalibrationModeLabel(mode)}");
            }
        }

        /// <summary>Operator-facing name of the calibration mode; unknown values pass through raw.</summary>
        public static string CalibrationModeLabel(string mode)
        {
            return mode == ArenaProtocol.CALIB_MODE_TWO_ANCHOR ? "2 Çapa"
                : mode == ArenaProtocol.CALIB_MODE_SAVED_ANCHOR ? "Eski Kalibre"
                : mode ?? "";
        }

        public static void AbortMatch()
        {
            if (Send(new AbortMatchMsg()))
            {
                SetStatus("Maç iptali gönderildi.");
            }
        }

        /// <summary>Freezes the running match (§5.2). The server only applies it in <c>playing</c>;
        /// in any other phase the command is silently dropped.</summary>
        public static void PauseMatch()
        {
            if (Send(new PauseMatchMsg()))
            {
                SetStatus("Duraklatma gönderildi.");
            }
        }

        /// <summary>Resumes an operator-paused match (§5.2). Does NOT lift a mode or countdown
        /// pause — the server rejects that.</summary>
        public static void ResumeMatch()
        {
            if (Send(new ResumeMatchMsg()))
            {
                SetStatus("Devam isteği gönderildi.");
            }
        }

        public static void ReturnToLobby()
        {
            if (Send(new ReturnToLobbyMsg()))
            {
                SetStatus("Lobiye dönme isteği gönderildi.");
            }
        }

        public static void SetTeam(int playerId, string team)
        {
            if (playerId <= 0 || (team != "red" && team != "blue"))
            {
                return;
            }

            if (Send(new SetTeamMsg { playerId = playerId, team = team }))
            {
                SetStatus($"Takım değişikliği gönderildi: {playerId} → {team}");
            }
        }

        /// <summary>Changes a player's name and/or shirt number (§5.1).
        /// <para>Empty <paramref name="name"/> and <c>0</c> <paramref name="number"/> leave that
        /// field alone (server convention), so "fix only the number" is one call. A number taken by
        /// someone online is rejected with a reason in <c>admin_state.notice</c>; ⚠️ no optimistic
        /// local update here — authority is on the server.</para></summary>
        public static void SetIdentity(int playerId, string name, int number)
        {
            if (playerId <= 0)
            {
                return;
            }

            string trimmed = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
            if (trimmed.Length == 0 && number == 0)
            {
                return; // both fields say "keep" — not worth a message
            }

            if (number != 0 && (number < ArenaProtocol.PLAYER_NUMBER_MIN || number > ArenaProtocol.PLAYER_NUMBER_MAX))
            {
                SetStatus($"Numara {ArenaProtocol.PLAYER_NUMBER_MIN}-{ArenaProtocol.PLAYER_NUMBER_MAX} aralığında olmalı.");
                return;
            }

            if (Send(new SetIdentityMsg { playerId = playerId, name = trimmed, number = number }))
            {
                string what = trimmed.Length > 0 && number != 0 ? $"{number} · {trimmed}"
                    : trimmed.Length > 0 ? trimmed
                    : $"numara {number}";
                SetStatus($"Kimlik gönderildi: oyuncu {playerId} → {what}");
            }
        }

        public static void Kick(int playerId)
        {
            if (playerId <= 0)
            {
                return;
            }

            if (Send(new KickMsg { playerId = playerId }))
            {
                SetStatus($"Atma isteği gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>
        /// Revives a dead player; <paramref name="playerId"/> <b>0 = EVERYONE currently dead</b>
        /// (§10.4). ⚠️ <c>0</c> is NOT filtered out — same bulk-target pattern as
        /// <see cref="ClearCalibration"/>.
        /// <para>Deliberately bypasses the mode's revive condition and respawn delay. An
        /// uncalibrated player or one inside an obstacle is still not revived; the server logs the
        /// reason — the UI says "sent", not "done".</para>
        /// </summary>
        public static void RevivePlayer(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new RevivePlayerMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Canlandırma gönderildi: tüm ölüler"
                    : $"Canlandırma gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>
        /// Clears a player's calibration; <paramref name="playerId"/> <b>0 = EVERYONE</b> (§10.6).
        /// A cleared player cannot fire, take damage or respawn and glows on other screens — ONLY
        /// the headset itself can re-enable alignment.
        /// <para><paramref name="keepSaved"/> picks the mode: <b>true = soft</b>, only the current
        /// alignment is invalidated and the anchor SAVED on the headset stays; <b>false = hard</b>,
        /// the saved anchor and UUID are wiped for good.</para>
        /// <para>⚠️ <b>A completely separate command from <see cref="ReloadCalibration"/>, not its
        /// inverse:</b> clearing takes the player out of the fight, reloading tries to restore
        /// alignment from the headset's record. Mixing them benches a player for no reason.</para>
        /// <para>⚠️ <b>HARD mode destroys what <see cref="ReloadCalibration"/> would read:</b>
        /// afterwards every headset answers "no saved calibration" and players must redo the A/B
        /// sequence by hand. So the <b>daily action is SOFT</b>; hard is venue maintenance, done
        /// only when the floor markers move.</para>
        /// </summary>
        public static void ClearCalibration(int playerId, bool keepSaved)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new ClearCalibrationMsg { playerId = playerId, keepSaved = keepSaved }))
            {
                SetStatus(playerId == 0
                    ? (keepSaved
                        ? "Tüm hizalamaların geçersiz kılınması gönderildi (cihaz kayıtları korunuyor)."
                        : "Tüm cihaz kalibrasyon kayıtlarının silinmesi gönderildi.")
                    : (keepSaved
                        ? $"Hizalama geçersiz kılma gönderildi: oyuncu {playerId} (cihaz kaydı korunuyor)"
                        : $"Cihaz kalibrasyon kaydının silinmesi gönderildi: oyuncu {playerId}"));
            }
        }

        /// <summary>
        /// Reloads a player's calibration from the anchor SAVED on the headset;
        /// <paramref name="playerId"/> <b>0 = ALL players</b>. The server computes nothing, it
        /// forwards the command; the headset restores alignment and answers with
        /// <c>calibration_result</c>.
        /// <para>⚠️ <b>A completely separate command from <see cref="ClearCalibration"/>, not its
        /// inverse:</b> clearing wipes calibration and benches the player; this one wipes nothing.
        /// Mixing them benches a playing player for no reason.</para>
        /// <para>Reloading is an <b>undoable attempt</b> — if it fails the player is left as before,
        /// hence no confirm window.</para>
        /// </summary>
        public static void ReloadCalibration(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new ReloadCalibrationMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Tüm kalibrasyonların yeniden yüklenmesi gönderildi."
                    : $"Kalibrasyon yeniden yükleme gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>
        /// Triggers a body measurement; <paramref name="playerId"/> <b>0 = EVERYONE</b> (§10.8).
        /// The server forwards only; the headset measures, answers <c>set_body_scale</c> and the
        /// result spreads through the roster.
        /// <para>⚠️ The player must be <b>standing upright</b> at that moment — only the operator
        /// knows when, hence a manual button. Uncalibrated players are rejected by the server, with
        /// the reason on the notice line.</para>
        /// </summary>
        public static void MeasureBodyScale(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new MeasureBodyScaleMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Tüm oyuncuların ölçülmesi gönderildi."
                    : $"Ölçüm gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>Manual reconnect — the only way back after a disconnect.</summary>
        public static void Reconnect()
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null)
            {
                SetStatus("İstemci hazır değil.");
                return;
            }

            if (!AppSession.HasServerEndpoint)
            {
                SetStatus($"Sunucu adresi yok. Uygulama launcher'dan başlatılmalı ({AppBoot.ArgServerIp} <ip>).");
                return;
            }

            client.Connect(AppSession.ServerIp, AppSession.ServerPort, AppSession.RoleAdmin);
            SetStatus($"Bağlanılıyor: {AppSession.ServerIp}:{AppSession.ServerPort}");
        }

        public static void Disconnect()
        {
            if (ArenaClient.Instance != null)
            {
                ArenaClient.Instance.Disconnect();
                SetStatus("Bağlantı kesildi.");
            }
        }

        /// <summary>
        /// Local-only notice (e.g. map preview) that never reaches the server. Shares the command
        /// status line so the operator has one place to look.
        /// </summary>
        public static void Note(string text)
        {
            SetStatus(text);
        }

        private static bool Send<T>(T msg) where T : class
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                SetStatus("Bağlantı yok; komut gönderilemedi.");
                return false;
            }

            client.Send(msg);
            return true;
        }

        private static void SetStatus(string text)
        {
            Status = text ?? "";
            Debug.Log($"[AdminCommands] {Status}");
            StatusChanged?.Invoke();
        }
    }
}
