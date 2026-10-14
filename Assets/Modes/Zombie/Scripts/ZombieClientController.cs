using System;
using TMPro;
using UnityEngine;
using VortexArena.Core.Audio;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>PRESENTATION component at the root of the Zombie HUD prefab; adds only what is Zombie's:
    /// the wave cells, the team strip, the big between-waves countdown around the base's centre notice,
    /// the short toasts and the downed-mate prompt.</summary>
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
        [Tooltip("KALAN hücresi — dalganın bitmemiş zombisi (sahadaki + doğmayı bekleyen).")]
        [SerializeField] private TMP_Text remainingText;
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

        [Header("Zombi — takım şeridi")]
        [Tooltip("Şeridin kökü — tek başına oynanan maçta kapalı durur.")]
        [SerializeField] private GameObject teamStripRoot;
        [Tooltip("Satır kökleri (en çok 8) — boş satır kapatılır.")]
        [SerializeField] private GameObject[] teamRows = new GameObject[TeamRowCount];
        [Tooltip("Satırdaki oyuncu adı.")]
        [SerializeField] private TMP_Text[] teamNameTexts = new TMP_Text[TeamRowCount];
        [Tooltip("Satırdaki durum: AYAKTA · DÜŞTÜ · KALDIRILIYOR %NN.")]
        [SerializeField] private TMP_Text[] teamStateTexts = new TMP_Text[TeamRowCount];

        /// <summary>The HUD instance on this headset, or <c>null</c> where there is none (admin).</summary>
        /// <remarks>⚠️ The HUD prefab is the ONLY instance per headset, so a static handle is honest;
        /// it exists because the weapon locks live on the arena's boards and have no path to the HUD
        /// otherwise (the toast must not be re-implemented there).</remarks>
        internal static ZombieClientController Live { get; private set; }

        /// <summary>Team strip rows the prefab carries — the OTHER players, capped at the roster size
        /// the arena is built for.</summary>
        internal const int TeamRowCount = 8;

        /// <summary>Seconds the "DALGA N BAŞLADI" toast stays up.</summary>
        private const float WaveToastSeconds = 2f;

        /// <summary>Seconds the "DALGA TEMİZLENDİ" toast stays up.</summary>
        private const float ClearToastSeconds = 2.5f;

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

        /// <summary>Zombies on the floor right now (<c>left</c>).</summary>
        private int _standing;

        /// <summary>Zombies of the running wave the server has not spawned yet (<c>rem</c>).</summary>
        private int _notSpawned;

        /// <summary>Zombies of the wave that are not dealt with yet: standing + not spawned.</summary>
        private int _remaining;

        private int _nextWaveSeconds;

        /// <summary>Outcome token (<c>survived|rescued|lost</c>); empty while the run is on.</summary>
        private string _over = string.Empty;

        private bool _playing;

        /// <summary>Wave the start toast was last fired for; 0 = none yet.</summary>
        private int _toastedWave;

        /// <summary>Wave the "DALGA TEMİZLENDİ" toast was last fired for; 0 = none yet.</summary>
        private int _clearedWave;

        /// <summary>Unscaled time the toast closes at; negative = no toast up.</summary>
        private float _toastHideAt = -1f;

        /// <summary>Last roster (§10.2) — the team strip's names and alive flags.</summary>
        private PlayerInfo[] _roster = Array.Empty<PlayerInfo>();

        /// <summary>Last text written into a strip row; a per-frame assignment on an unchanged row
        /// would re-mesh the text for nothing.</summary>
        private readonly string[] _rowNameCache = new string[TeamRowCount];
        private readonly string[] _rowStateCache = new string[TeamRowCount];

        /// <summary>The window predicate THIS instance installed; compared before clearing, so a HUD
        /// that is torn down after the next one was built does not silence the live one.</summary>
        private Func<bool> _reviveWindow;

        /// <summary>2D source for the wave stings; created on first use (most runs have no clips).</summary>
        private AudioSource _sting;

        /// <summary>World-space markers over downed mates; owned by this HUD (one per headset).</summary>
        private ZombieDownedMarkers _markers;

        /// <summary>Prompt last written through <see cref="PlayerCombatState.SetModePrompt"/>; the
        /// field is shared with every other writer, so it is only cleared when it is still ours.</summary>
        private string _prompt = string.Empty;

        /// <summary>The counter cells are not the base's fields, so clearing them on return to lobby lives
        /// here too (same pattern as the other co-op HUD).</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            Live = this;
            NetEvents.OnReturnToLobby += HandleReturnToLobbyLocal;

            // The mode owns the revive WINDOW, the core owns the revive flow (§10.4): between waves
            // everyone who is down gets up, and only this HUD knows when that gap is.
            _reviveWindow = IsWaveBreak;
            PlayerCombatState.ModeReviveWindow = _reviveWindow;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Live == this)
            {
                Live = null;
            }

            NetEvents.OnReturnToLobby -= HandleReturnToLobbyLocal;

            if (PlayerCombatState.ModeReviveWindow == _reviveWindow)
            {
                PlayerCombatState.ModeReviveWindow = null;
            }

            _reviveWindow = null;
            ClearPrompt();
        }

        protected override void Start()
        {
            base.Start();

            // Separate root: the markers live in the arena, this HUD is head-locked.
            var go = new GameObject("[ZombieDownedMarkers]");
            _markers = go.AddComponent<ZombieDownedMarkers>();
            _markers.SetRoster(_roster);
        }

        private void OnDestroy()
        {
            if (_markers != null)
            {
                Destroy(_markers.gameObject);
                _markers = null;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (_toastHideAt >= 0f && Time.unscaledTime >= _toastHideAt)
            {
                HideToast();
            }

            RefreshDeadPrompt();
            RefreshTeamStrip();
        }

        /// <summary>Is the squad in the gap between two waves? That is the window the core revives
        /// everyone who is down in (<see cref="PlayerCombatState.ModeReviveWindow"/>).</summary>
        private bool IsWaveBreak() =>
            _playing && _over.Length == 0 && _wave >= 1 && _nextWaveSeconds > 0;

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

        /// <summary>Wave sting (2D, Müzik kanalı). Optional clips: a missing one is silence, not a
        /// warning — the toast already carries the message.</summary>
        private void PlaySting(AudioClip clip)
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (clip == null || sounds == null)
            {
                return;
            }

            if (_sting == null)
            {
                _sting = gameObject.AddComponent<AudioSource>();
                _sting.playOnAwake = false;
                _sting.loop = false;

                // 2D like the mode music: through the spatializer the sting would seem to turn with
                // the player's head.
                _sting.spatialBlend = 0f;
                _sting.spatialize = false;
            }

            _sting.PlayOneShot(clip, sounds.MusicVolume * AudioMix.Of(AudioChannel.Music));
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
            _remaining = 0;
            _nextWaveSeconds = 0;
            _over = string.Empty;
            _playing = false;
            _toastedWave = 0;
            _clearedWave = 0;
            _roster = Array.Empty<PlayerInfo>();
            ClearPrompt();
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

        /// <summary>No winner in a co-op run (§10.5): the honest headline is how it ended — the squad
        /// held the clock, the Giant went down, or everyone did.</summary>
        protected override string WinnerLine(MatchEndMsg msg)
        {
            HideToast();
            SetClockAlarm(false);
            SetCenterNotice("");
            ClearPrompt();

            int wave = Mathf.Max(1, _wave);
            if (string.Equals(_over, ZombieKinds.OverSurvived, StringComparison.Ordinal))
            {
                return $"DAYANDINIZ — dalga {wave}";
            }

            if (string.Equals(_over, ZombieKinds.OverRescued, StringComparison.Ordinal))
            {
                return $"KURTULDUNUZ — dalga {wave}";
            }

            if (string.Equals(_over, ZombieKinds.OverLost, StringComparison.Ordinal))
            {
                return $"YENİLDİNİZ — dalga {wave}";
            }

            // No outcome token (an aborted match): inventing a win or a loss would be a lie.
            return $"OYUN BİTTİ — dalga {wave}";
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

        /// <summary>Roster refreshed: own kills live here (§10.2), and the team strip's names and
        /// alive flags come from the same list.</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            PlayerInfo self = FindSelf(msg);
            _selfScore = self != null ? self.score : 0;
            _roster = msg?.players ?? Array.Empty<PlayerInfo>();

            if (_markers != null)
            {
                _markers.SetRoster(_roster);
            }

            SetText(scoreText, BuildScoreLine());
        }

        /// <summary>Feeds the wave cells, the clock alarm and the between-waves countdown from
        /// <c>modeState</c>.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            int previousWave = _wave;
            int previousNext = _nextWaveSeconds;
            ParseState(msg.modeState);

            SetText(waveText, _wave.ToString());
            SetText(remainingText, _remaining.ToString());

            bool playing = msg.phase == ArenaProtocol.PHASE_PLAYING;
            _playing = playing;
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
                PlaySting(ZombieSounds.Live != null ? ZombieSounds.Live.WaveStartSting : null);
            }

            // A break OPENING after a wave is the "you cleared it" moment; the countdown before the
            // FIRST wave is not (wave 0), and a finished run says it with the result screen.
            if (playing && _over.Length == 0 && _wave >= 1 && previousNext == 0
                && _nextWaveSeconds > 0 && _wave > _clearedWave)
            {
                _clearedWave = _wave;
                ShowToast("", "DALGA TEMİZLENDİ", ClearToastSeconds);
                PlaySting(ZombieSounds.Live != null ? ZombieSounds.Live.WaveClearSting : null);
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

        /// <summary><c>"w:3;left:5;next:0;rem:2"</c> (+ optional <c>over:…</c>) → the cached cells.</summary>
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
                    _over = raw;
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
                else if (string.Equals(key, ZombieKinds.StateRemaining, StringComparison.Ordinal))
                {
                    _notSpawned = value;
                }
            }

            // KALAN = what the wave still owes the squad: on the floor plus not yet spawned.
            _remaining = _standing + _notSpawned;
        }

        // ------------------------------------------------------------- dead player prompt

        /// <summary>The downed player's only instruction. ⚠️ Written through the core's mode prompt,
        /// which OVERRIDES the generic death text: with <c>reviveAnchor:"teammate"</c> that text would
        /// send them to a base that cannot revive them.</summary>
        private void RefreshDeadPrompt()
        {
            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat == null)
            {
                return;
            }

            // Alone on the map the core's own stand-still rule applies — a "get a mate" line would
            // ask for something impossible.
            if (combat.IsAlive || !TeammateRevive.HasOtherCalibratedPlayer)
            {
                ClearPrompt();
                return;
            }

            float progress = TeammateRevive.LocalProgress;
            SetPrompt(progress > 0f
                ? $"KALDIRILIYORSUN %{Percent(progress)}"
                : "ARKADAŞIN SENİ KALDIRSIN · dalga arasında kalkacaksın");
        }

        private void SetPrompt(string prompt)
        {
            if (string.Equals(_prompt, prompt, StringComparison.Ordinal))
            {
                return;
            }

            _prompt = prompt;
            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat != null)
            {
                combat.SetModePrompt(prompt);
            }
        }

        private void ClearPrompt()
        {
            if (_prompt.Length == 0)
            {
                return;
            }

            _prompt = string.Empty;
            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat != null)
            {
                combat.SetModePrompt("");
            }
        }

        private static int Percent(float progress01) =>
            Mathf.Clamp(Mathf.RoundToInt(progress01 * 100f), 0, 100);

        // ----------------------------------------------------------------- team strip

        /// <summary>Name + state of the OTHER players: who is standing, who is down and how far their
        /// pick-up is. ⚠️ Only calibrated participants are listed: an uncalibrated headset is not a
        /// player the squad can count on (it cannot be revived or counted for defeat either).</summary>
        private void RefreshTeamStrip()
        {
            if (teamRows == null)
            {
                return;
            }

            int self = LocalPlayerId;
            int rows = Mathf.Min(teamRows.Length, TeamRowCount);
            int row = 0;

            for (int i = 0; i < _roster.Length && row < rows; i++)
            {
                PlayerInfo info = _roster[i];
                if (!IsSquadMate(info, self))
                {
                    continue;
                }

                float progress = info.alive ? 0f : TeammateRevive.AllyProgress(info.playerId);
                string state = info.alive
                    ? "AYAKTA"
                    : progress > 0f
                        ? $"KALDIRILIYOR %{Percent(progress)}"
                        : "DÜŞTÜ";

                SetRow(row, info.name, state, info.alive, progress > 0f);
                row++;
            }

            for (int i = row; i < teamRows.Length; i++)
            {
                SetRowActive(i, false);
            }

            // One player on the map has no squad to read.
            bool any = row > 0;
            if (teamStripRoot != null && teamStripRoot.activeSelf != any)
            {
                teamStripRoot.SetActive(any);
            }
        }

        private static bool IsSquadMate(PlayerInfo info, int self)
        {
            return info != null
                   && info.playerId != self
                   && info.role != "admin" // no protocol constant for the role strings (§2)
                   && info.inMatch
                   && info.calibrated
                   && info.connection != ArenaProtocol.CONNECTION_LEFT;
        }

        private void SetRow(int index, string name, string state, bool alive, bool reviving)
        {
            SetRowActive(index, true);

            if (teamNameTexts != null && index < teamNameTexts.Length
                && !string.Equals(_rowNameCache[index], name, StringComparison.Ordinal))
            {
                _rowNameCache[index] = name;
                SetText(teamNameTexts[index], name);
            }

            if (teamStateTexts == null || index >= teamStateTexts.Length
                || string.Equals(_rowStateCache[index], state, StringComparison.Ordinal))
            {
                return;
            }

            _rowStateCache[index] = state;
            TMP_Text target = teamStateTexts[index];
            SetText(target, state);

            if (target != null)
            {
                target.color = alive ? Kan.Bone : reviving ? Kan.Alive : Kan.BloodHi;
            }
        }

        private void SetRowActive(int index, bool active)
        {
            GameObject row = index < teamRows.Length ? teamRows[index] : null;
            if (row != null && row.activeSelf != active)
            {
                row.SetActive(active);
            }
        }
    }
}
