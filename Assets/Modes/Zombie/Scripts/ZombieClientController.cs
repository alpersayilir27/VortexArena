using System;
using TMPro;
using UnityEngine;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>PRESENTATION component at the root of the Zombie HUD prefab; adds only what is Zombie's:
    /// the wave cells, the big between-waves countdown around the base's centre notice and the short
    /// toasts.</summary>
    /// <remarks>⚠️ <b>Every field below is written by the builder</b>
    /// (<c>GirdapUiBuilder.ZombieHud.cs</c>, menu <i>Yalnız Zombi HUD</i>) — a hand-dragged reference is
    /// lost on the next generation, and a renamed field silently unbinds.
    /// <para>Score is <c>scoring:"shared"</c> (§10.5): the squad total rides
    /// <c>match_state.scoreRed</c> (<c>scoreBlue</c> always 0) and your own kills ride
    /// <c>lobby_state → PlayerInfo.score</c> (§10.2) — there is no winner.</para>
    /// <para>The wave state rides <c>modeState</c> (<c>"w:3;left:5;next:0"</c>); the core never
    /// interprets that string (§10.1), the mode both writes and reads it.</para></remarks>
    public class ZombieClientController : ModeHudBase
    {
        [Header("Zombi — şerit hücreleri")]
        [Tooltip("DALGA hücresinin sayısı.")]
        [SerializeField] private TMP_Text waveText;
        [Tooltip("AYAKTA hücresi — şu an ayakta zombi.")]
        [SerializeField] private TMP_Text standingText;
        [Tooltip("EKİP hücresi — ortak leş toplamı.")]
        [SerializeField] private TMP_Text squadKillsText;
        [Tooltip("SEN hücresi — kendi öldürme sayın.")]
        [SerializeField] private TMP_Text selfKillsText;
        [Tooltip("Saatin son 10 saniye plakası (kırmızı). Atanmazsa hiç açılmaz.")]
        [SerializeField] private GameObject clockAlarmPlate;

        [Header("Zombi — dalga arası geri sayım")]
        [Tooltip("Geri sayımın üstündeki başlık (\"DALGA 5\"). Sayının kendisi tabanın merkez bildirimidir.")]
        [SerializeField] private TMP_Text noticeTitleText;
        [Tooltip("Geri sayımın altındaki alt satır (\"HAZIRLAN\").")]
        [SerializeField] private TMP_Text noticeSubText;

        [Header("Zombi — kısa bildirim")]
        [Tooltip("Bildirim kökü — boşken kapalı durur.")]
        [SerializeField] private GameObject toastRoot;
        [Tooltip("Bildirimin küçük üst satırı (\"YENİ SİLAH AÇILDI\").")]
        [SerializeField] private TMP_Text toastSmallText;
        [Tooltip("Bildirimin büyük satırı.")]
        [SerializeField] private TMP_Text toastBigText;
        [Tooltip("Küçük satır VARken büyük satıra verilen üst boşluk (px) — plakanın ölçüsü builder'dan gelir.")]
        [SerializeField] private float toastSmallBand = 44f;

        /// <summary>The HUD instance on this headset, or <c>null</c> where there is none (admin).</summary>
        /// <remarks>⚠️ The HUD prefab is the ONLY instance per headset, so a static handle is honest;
        /// it exists because the weapon locks live on the arena's boards and have no path to the HUD
        /// otherwise (the toast must not be re-implemented there).</remarks>
        internal static ZombieClientController Live { get; private set; }

        /// <summary>Seconds the "DALGA N BAŞLADI" toast stays up.</summary>
        private const float WaveToastSeconds = 2f;

        /// <summary>Seconds the weapon-unlock toast stays up.</summary>
        private const float WeaponToastSeconds = 3f;

        /// <summary>Below this the clock turns red (same threshold the diner HUD uses).</summary>
        private const float ClockAlarmSeconds = 10f;

        /// <summary>Own kills from the last <c>lobby_state</c>; cached so the 1 Hz <c>match_state</c> can
        /// redraw the score cells without a roster.</summary>
        private int _selfScore;

        /// <summary>Squad total from the last <c>match_state</c>; cached so a roster refresh can redraw
        /// the same cells.</summary>
        private int _sharedTotal;

        private int _wave;
        private int _standing;
        private int _nextWaveSeconds;
        private bool _lost;

        /// <summary>Wave the start toast was last fired for; 0 = none yet.</summary>
        private int _toastedWave;

        /// <summary>Unscaled time the toast closes at; negative = no toast up.</summary>
        private float _toastHideAt = -1f;

        /// <summary>The counter cells are not the base's fields, so clearing them on return to lobby lives
        /// here too (same pattern as the other co-op HUD).</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            Live = this;
            NetEvents.OnReturnToLobby += HandleReturnToLobbyLocal;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Live == this)
            {
                Live = null;
            }

            NetEvents.OnReturnToLobby -= HandleReturnToLobbyLocal;
        }

        protected override void Update()
        {
            base.Update();

            if (_toastHideAt >= 0f && Time.unscaledTime >= _toastHideAt)
            {
                HideToast();
            }
        }

        // ------------------------------------------------------------------- toasts

        /// <summary>Short notice above the view centre. <paramref name="small"/> may be empty (single
        /// line). Calling it again replaces whatever is up — the newer message is the urgent one.</summary>
        public void ShowToast(string small, string big, float seconds)
        {
            if (toastRoot == null || toastBigText == null)
            {
                return;
            }

            bool hasSmall = !string.IsNullOrEmpty(small);
            if (toastSmallText != null)
            {
                SetText(toastSmallText, small);
                toastSmallText.gameObject.SetActive(hasSmall);
            }

            // The big line is centred in the whole plate; with a small line above it the band is
            // reserved as a top margin instead of a second rect (no layout group here).
            SetText(toastBigText, big);
            toastBigText.margin = new Vector4(0f, hasSmall ? toastSmallBand : 0f, 0f, 0f);

            toastRoot.SetActive(true);
            _toastHideAt = Time.unscaledTime + Mathf.Max(0.1f, seconds);
        }

        /// <summary>Weapon progression toast, raised by <see cref="ZombieWeaponUnlocks"/> when a wave
        /// opens new weapons on the boards.</summary>
        public void ShowWeaponUnlockedToast(string weaponName)
        {
            ShowToast("YENİ SİLAH AÇILDI", Girdap.Upper(weaponName), WeaponToastSeconds);
        }

        private void HideToast()
        {
            _toastHideAt = -1f;
            if (toastRoot != null)
            {
                toastRoot.SetActive(false);
            }
        }

        // -------------------------------------------------------------- base contract

        private void HandleReturnToLobbyLocal(ReturnToLobbyMsg _)
        {
            _selfScore = 0;
            _sharedTotal = 0;
            _wave = 0;
            _standing = 0;
            _nextWaveSeconds = 0;
            _lost = false;
            _toastedWave = 0;
            HideToast();
            SetClockAlarm(false);
            SetCenterNotice("");
            BuildScoreLine();
        }

        protected override string ScoreLine(MatchStateMsg msg)
        {
            _sharedTotal = msg.scoreRed;
            return BuildScoreLine();
        }

        protected override string EndScoreLine(MatchEndMsg msg)
        {
            _sharedTotal = msg.scoreRed;
            return BuildScoreLine();
        }

        /// <summary>No winner in a co-op run (§10.5): the honest headline is how it ended — the clock was
        /// survived, or the whole squad went down.</summary>
        protected override string WinnerLine(MatchEndMsg msg)
        {
            HideToast();
            SetClockAlarm(false);
            SetCenterNotice("");
            return _lost ? "KAYBEDİLDİ — tüm oyuncular düştü" : $"DAYANILDI — dalga {Mathf.Max(1, _wave)}";
        }

        /// <summary>"DALGA 3" instead of "MAÇ" while playing; other phases are left to the base so the
        /// phase/reason vocabulary stays in one place.</summary>
        protected override string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING && _wave > 0)
            {
                return $"DALGA {_wave}";
            }

            return base.PhaseLabel(phase, phaseReason, modeState);
        }

        /// <summary>The mode pauses itself when there is no object host and writes the REASON into
        /// <c>modeState</c> as plain Turkish (§10.5) — it is shown as it is, since it is written for the
        /// person reading it.</summary>
        protected override string ModeStateLabel(string modeState)
        {
            return string.IsNullOrEmpty(modeState) || modeState.IndexOf(':') >= 0
                ? base.ModeStateLabel(modeState)
                : modeState;
        }

        /// <summary>Roster refreshed: own kills live here (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            PlayerInfo self = FindSelf(msg);
            _selfScore = self != null ? self.score : 0;
            SetText(scoreText, BuildScoreLine());
        }

        /// <summary>Feeds the wave cells, the clock alarm and the between-waves countdown from
        /// <c>modeState</c>.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            int previousWave = _wave;
            ParseState(msg.modeState);

            SetText(waveText, _wave.ToString());
            SetText(standingText, _standing.ToString());

            bool playing = msg.phase == ArenaProtocol.PHASE_PLAYING;
            SetClockAlarm(playing && msg.timeRemaining > 0f
                          && msg.timeRemaining <= ClockAlarmSeconds);

            // Between waves the big centre number is the only thing that explains why nothing is
            // coming; the title/subtitle around it ride OnCenterNoticeRefreshed.
            SetCenterNotice(playing && _nextWaveSeconds > 0 ? _nextWaveSeconds.ToString() : "");

            // A wave starts when its number goes up AND nothing is counting down any more.
            if (playing && _wave > previousWave && _nextWaveSeconds == 0 && _wave > _toastedWave)
            {
                _toastedWave = _wave;
                ShowToast("", $"DALGA {_wave} BAŞLADI", WaveToastSeconds);
            }
        }

        /// <summary>Dresses the base's single centre text: the number is the base's, the wave title and
        /// the "HAZIRLAN" line are this mode's.</summary>
        protected override void OnCenterNoticeRefreshed(string text, bool countdown)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Both cases count down to the SAME thing (the next wave starting), so one formula: at
            // match start the wave is still 0 and the countdown belongs to wave 1.
            SetText(noticeTitleText, $"DALGA {Mathf.Max(1, _wave + 1)}");
            SetText(noticeSubText, "HAZIRLAN");
        }

        // ---------------------------------------------------------------- internals

        private void SetClockAlarm(bool on)
        {
            if (clockAlarmPlate != null && clockAlarmPlate.activeSelf != on)
            {
                clockAlarmPlate.SetActive(on);
            }
        }

        private string BuildScoreLine()
        {
            SetText(squadKillsText, _sharedTotal.ToString());
            SetText(selfKillsText, _selfScore.ToString());

            // Two lines on purpose: one line does not fit the HUD's score box.
            return $"ÖLDÜRDÜĞÜN: {_selfScore}\nEKİP: {_sharedTotal}";
        }

        /// <summary><c>"w:3;left:5;next:0"</c> (+ optional <c>over:lost</c>) → the cached cells.</summary>
        /// <remarks>⚠️ Tolerant on purpose: an empty, partial or malformed string must leave the counters
        /// where they were rather than break the HUD — the mode's contract can gain tokens later and an
        /// unknown token is skipped, not treated as an error. The pause reason rides the same field as
        /// free text, which simply parses to nothing.</remarks>
        private void ParseState(string modeState)
        {
            if (string.IsNullOrEmpty(modeState))
            {
                return;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int sep = token.IndexOf(':');
                if (sep <= 0 || sep == token.Length - 1)
                {
                    continue;
                }

                string key = token.Substring(0, sep).Trim();
                string raw = token.Substring(sep + 1).Trim();

                if (string.Equals(key, ZombieKinds.StateOver, StringComparison.Ordinal))
                {
                    _lost = string.Equals(raw, ZombieKinds.OverLost, StringComparison.Ordinal);
                    continue;
                }

                if (!int.TryParse(raw, out int value) || value < 0)
                {
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateWave, StringComparison.Ordinal))
                {
                    _wave = value;
                }
                else if (string.Equals(key, ZombieKinds.StateLeft, StringComparison.Ordinal))
                {
                    _standing = value;
                }
                else if (string.Equals(key, ZombieKinds.StateNext, StringComparison.Ordinal))
                {
                    _nextWaveSeconds = value;
                }
            }
        }
    }
}
