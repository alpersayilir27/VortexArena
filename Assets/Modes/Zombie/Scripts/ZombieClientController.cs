using System;
using System.Collections.Generic;
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
    /// interprets that string (§10.1), the mode both writes and reads it.</para>
    /// <para>⚠️ Only PERSONAL sounds live here (a mate going down, a pick-up, your own result): the
    /// run's match-level cues are <see cref="ZombieAnnouncer"/>'s, because this prefab does not exist
    /// on the admin. Playing one in both places doubles it on a headset.</para></remarks>
    public class ZombieClientController : ModeHudBase
    {
        [Header("Zombi — şerit hücreleri")]
        [Tooltip("DALGA hücresinin sayısı.")]
        [SerializeField] private TMP_Text waveText;
        [Tooltip("SÜRE hücresi — koşan dalganın kalan süresi (m:ss), dalga arasında sıradaki dalgaya kalan.")]
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
        [Tooltip("Geri sayımın altındaki alt satır (\"HAZIRLAN\" / \"YENİ SİLAHLAR\").")]
        [SerializeField] private TMP_Text noticeSubText;
        [Tooltip("Temizlik fazında açılan silahların adları (\" · \" ile dizili); başka zaman boş durur.")]
        [SerializeField] private TMP_Text noticeUnlockText;

        [Header("Zombi — kısa bildirim")]
        [Tooltip("Bildirim kökü — boşken kapalı durur.")]
        [SerializeField] private GameObject toastRoot;
        [Tooltip("Bildirimin küçük üst satırı (\"DALGA N\").")]
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

        [Header("Zombi — DEV barı")]
        [Tooltip("DEV barının kökü — DEV doğana kadar kapalı durur.")]
        [SerializeField] private GameObject bossRoot;
        [Tooltip("DEV'in can dolgusu (sprite'lı Filled Image).")]
        [SerializeField] private UnityEngine.UI.Image bossFill;
        [Tooltip("Barın başlığı (\"DEV\").")]
        [SerializeField] private TMP_Text bossTitleText;
        [Tooltip("Barın alt satırı: SON DİRENİŞ · kalan süre.")]
        [SerializeField] private TMP_Text bossTimeText;

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

        /// <summary>Centre-notice text during the clear phase: there is no countdown number then, but the
        /// notice root follows the base's "empty text = hidden" rule — a blank keeps the title and the
        /// weapon line up without drawing a digit.</summary>
        private const string ClearNoticeBlank = " ";

        /// <summary>Seconds the "SON DİRENİŞ" toast stays up.</summary>
        private const float FinalStandToastSeconds = 3f;

        /// <summary>Seconds the revive window stays open when the final stand opens — everyone who is
        /// down gets up for the last fight, and after that there is no break left to wait for.</summary>
        private const float FinalStandReviveSeconds = 3f;

        /// <summary>Below this the clock turns red (same threshold the diner HUD uses).</summary>
        private const float ClockAlarmSeconds = 10f;

        /// <summary>Shortest gap (s) between two "arkadaşın düştü" lines: a swarm can drop the whole
        /// squad in one second and the queue would then say it three times in a row.</summary>
        private const float MateDownedVoiceGap = 4f;

        /// <summary>Own kills from the last <c>lobby_state</c>; cached so the 1 Hz <c>match_state</c> can
        /// redraw the score cells without a roster.</summary>
        private int _selfScore;

        /// <summary>Squad total from the last <c>match_state</c>; cached so a roster refresh can redraw
        /// the same cells.</summary>
        private int _sharedTotal;

        private int _wave;

        /// <summary>Seconds left of the running wave's clock (<c>wl</c>); 0 = no wave is spawning.</summary>
        private int _waveLeft;

        /// <summary>Seconds to the squad rescue (<c>wp</c>); 0 = no wipe countdown.</summary>
        private int _wipeLeft;

        /// <summary>Seconds left of the squad rescue's revive window (<c>rv</c>); 0 = closed.</summary>
        private int _wipeReviveLeft;

        private int _nextWaveSeconds;

        /// <summary>Outcome token (<c>survived|rescued|lost</c>); empty while the run is on.</summary>
        private string _over = string.Empty;

        private bool _playing;

        /// <summary>Wave the start toast was last fired for; 0 = none yet.</summary>
        private int _toastedWave;

        /// <summary>Wave the clear-phase notice was last built for; 0 = none yet.</summary>
        private int _clearedWave;

        /// <summary>Seconds left of the clear phase (<c>clr</c>); 0 = not in it.</summary>
        private int _clearLeft;

        /// <summary>Names the next wave opens, resolved ONCE when the clear phase starts: the board
        /// scan behind it must not run on every <c>match_state</c>.</summary>
        private string _unlockNames = string.Empty;

        /// <summary>Unscaled time the toast closes at; negative = no toast up.</summary>
        private float _toastHideAt = -1f;

        /// <summary>Final stand running (<c>fs</c>): no more waves, the Giant is on the floor.</summary>
        private bool _finalStand;

        /// <summary>Unscaled time the final stand's revive window closes; negative = closed.</summary>
        private float _finalStandReviveUntil = -1f;

        /// <summary>Special wave token (<c>wx</c>); empty on a plain wave.</summary>
        private string _special = string.Empty;

        /// <summary>The Giant's <c>netId</c> (<c>dev</c>); 0 = not spawned.</summary>
        private int _devNetId;

        private float _devMaxHp;

        /// <summary>The run is over (result screen up): the boss bar must not come back on a late
        /// state.</summary>
        private bool _ended;

        /// <summary>Clock of the last <c>match_state</c> and the unscaled time it arrived: the boss bar
        /// counts down per frame, while the state is 1 Hz.</summary>
        private float _clockSeconds;
        private float _clockStamp = -1f;

        /// <summary>Last string written into the boss bar's sub line; a per-frame assignment would
        /// re-mesh the text for nothing.</summary>
        private string _bossTimeCache = string.Empty;

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

        /// <summary>2D looping source for the pick-up; separate from <see cref="_sting"/> because a
        /// loop would hold that source for the next one-shot.</summary>
        private AudioSource _reviveLoop;

        /// <summary>Was the local player down on the previous frame? The rise out of it is the cue.</summary>
        private bool _wasDowned;

        /// <summary>The outcome cue was played for this run. ⚠️ A latch, because the outcome is read in
        /// <see cref="WinnerLine"/>, which the base may ask for more than once.</summary>
        private bool _outcomeCuePlayed;

        /// <summary>Alive flag per squad mate from the last roster: the DROP (alive → down) is the cue,
        /// so a mate first seen already down must not trigger it.</summary>
        private readonly Dictionary<int, bool> _mateAlive = new Dictionary<int, bool>();

        /// <summary>Unscaled time the next mate-down line may play at.</summary>
        private float _mateDownedVoiceAt;

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
            _reviveWindow = IsReviveWindow;
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

            // A looping source on a disabled HUD would keep sounding with nothing left to update it.
            SetReviveLoop(null, null);
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
            RefreshBossBar();
            RefreshReviveAudio();
        }

        /// <summary>Is the squad in the gap between two waves? That is the window the core revives
        /// everyone who is down in (<see cref="PlayerCombatState.ModeReviveWindow"/>).</summary>
        private bool IsWaveBreak() =>
            _playing && _over.Length == 0 && _wave >= 1 && _nextWaveSeconds > 0;

        /// <summary>The window the core revives everyone who is down in: the gap between waves, the
        /// squad rescue's window (<c>rv</c>) and a short one when the final stand opens — after that
        /// there are no more gaps.</summary>
        private bool IsReviveWindow() =>
            IsWaveBreak() || (_wipeReviveLeft > 0 && _playing && _over.Length == 0) ||
            Time.unscaledTime < _finalStandReviveUntil;

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

        /// <summary>The PERSONAL 2D cues (2D, Müzik kanalı): the pick-up and your own result. Optional
        /// clips: a missing one is silence, not a warning — the toast already carries the
        /// message.</summary>
        /// <remarks>Müzik kanalı because the mix has no UI/SFX channel (<c>AudioChannel</c>): these
        /// cues belong with the stings rather than with the weapons or the ambience.</remarks>
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

        /// <summary>A PERSONAL voice line of the mode (a mate going down, your own result); the
        /// match-level lines belong to <see cref="ZombieAnnouncer"/>, which the admin hears too.
        /// Optional clip: a missing one is silence, not a warning.</summary>
        /// <remarks>⚠️ Through <see cref="GameAudio.PlayAnnouncement"/> and not
        /// <see cref="PlaySting"/>: there is ONE announcement channel, so this line queues behind the
        /// core's lines instead of talking over them. The stings stay on the mode's own source — they
        /// are meant to sound UNDER the words.</remarks>
        private static void PlayVoice(AudioClip clip,
            float maxWaitSeconds = GameAudio.DefaultMaxWaitSeconds)
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (clip != null && sounds != null)
            {
                GameAudio.PlayAnnouncement(clip, sounds.VoiceVolume, maxWaitSeconds);
            }
        }

        /// <summary>Headline of a special wave, or empty on a plain one.</summary>
        private static string SpecialTitle(string special)
        {
            if (string.Equals(special, ZombieKinds.SpecialSwarm, StringComparison.Ordinal))
            {
                return "KOŞUCU SÜRÜSÜ";
            }

            if (string.Equals(special, ZombieKinds.SpecialHeavy, StringComparison.Ordinal))
            {
                return "AĞIR ADIMLAR";
            }

            return string.Equals(special, ZombieKinds.SpecialScream, StringComparison.Ordinal)
                ? "ÇIĞLIK GECESİ"
                : string.Empty;
        }

        /// <summary>The pick-up loop and the "you are up" one-shot — both only for revives the LOCAL
        /// player is part of (being picked up, or picking a mate up).</summary>
        private void RefreshReviveAudio()
        {
            PlayerCombatState combat = PlayerCombatState.Instance;
            ZombieSounds sounds = ZombieSounds.Live;
            if (combat == null || sounds == null)
            {
                return;
            }

            bool running = _playing && !_ended && _over.Length == 0;
            bool reviving = running && (TeammateRevive.LocalProgress > 0f || IsRevivingMate());
            SetReviveLoop(reviving ? sounds.ReviveProgressClip : null, sounds);

            bool downed = !combat.IsAlive;
            if (_wasDowned && !downed && running)
            {
                PlaySting(sounds.ReviveCompleteClip);
            }

            _wasDowned = downed;
        }

        /// <summary>Are WE holding a downed mate? <see cref="TeammateRevive.AllyProgress"/> is the
        /// reviver-side estimate, so a non-zero one on this headset is our own hold.</summary>
        private bool IsRevivingMate()
        {
            int self = LocalPlayerId;
            for (int i = 0; i < _roster.Length; i++)
            {
                PlayerInfo info = _roster[i];
                if (IsSquadMate(info, self) && !info.alive && TeammateRevive.AllyProgress(info.playerId) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetReviveLoop(AudioClip clip, ZombieSounds sounds)
        {
            if (clip == null)
            {
                if (_reviveLoop != null && _reviveLoop.isPlaying)
                {
                    _reviveLoop.Stop();
                }

                return;
            }

            if (_reviveLoop == null)
            {
                _reviveLoop = gameObject.AddComponent<AudioSource>();
                _reviveLoop.playOnAwake = false;
                _reviveLoop.loop = true;
                _reviveLoop.spatialBlend = 0f;
                _reviveLoop.spatialize = false;
            }

            if (_reviveLoop.clip != clip)
            {
                _reviveLoop.clip = clip;
            }

            // Re-read per frame: the operator may pull the channel down mid-wave.
            _reviveLoop.volume = sounds.MusicVolume * AudioMix.Of(AudioChannel.Music);

            if (!_reviveLoop.isPlaying)
            {
                _reviveLoop.Play();
            }
        }

        /// <summary>Outcome cue, once per run: the result screen's sound.</summary>
        private void PlayOutcomeCue()
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (_outcomeCuePlayed || sounds == null)
            {
                return;
            }

            _outcomeCuePlayed = true;
            SetReviveLoop(null, sounds);

            // The sting is the result screen's sound, the line rides the announcement channel behind
            // it — the two are separate channels and do not mask each other.
            // The result line never expires: it describes the screen that stays up, so waiting behind
            // the last wave's line is right and being dropped is not.
            if (string.Equals(_over, ZombieKinds.OverSurvived, StringComparison.Ordinal))
            {
                PlaySting(sounds.VictoryClip);
                PlayVoice(sounds.SurvivedVoice, GameAudio.NeverExpires);
            }
            else if (string.Equals(_over, ZombieKinds.OverRescued, StringComparison.Ordinal))
            {
                PlaySting(sounds.RescueClip);
                PlayVoice(sounds.RescuedVoice, GameAudio.NeverExpires);
            }
            else if (string.Equals(_over, ZombieKinds.OverLost, StringComparison.Ordinal))
            {
                PlaySting(sounds.DefeatClip);
                PlayVoice(sounds.LostVoice, GameAudio.NeverExpires);
            }

            // No outcome token (an aborted match): no cue either — a victory or a defeat sound would
            // claim something that did not happen.
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
            _waveLeft = 0;
            _wipeLeft = 0;
            _wipeReviveLeft = 0;
            _nextWaveSeconds = 0;
            _over = string.Empty;
            _playing = false;
            _toastedWave = 0;
            _clearedWave = 0;
            _clearLeft = 0;
            _unlockNames = string.Empty;
            _finalStand = false;
            _finalStandReviveUntil = -1f;
            _special = string.Empty;
            _devNetId = 0;
            _devMaxHp = 0f;
            _clockStamp = -1f;
            _roster = Array.Empty<PlayerInfo>();
            _wasDowned = false;
            _outcomeCuePlayed = false;
            _mateAlive.Clear();
            _mateDownedVoiceAt = 0f;
            SetReviveLoop(null, null);
            ClearPrompt();
            HideToast();
            HideBossBar();
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
            HideBossBar();
            SetClockAlarm(false);
            SetCenterNotice("");
            ClearPrompt();

            // The run's last clear phase dies with the result screen: its notice and weapon list would
            // otherwise come back on the next centre-notice refresh.
            _clearLeft = 0;
            _clearedWave = 0;
            _unlockNames = string.Empty;
            PlayOutcomeCue();

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
            TrackMatesDowned();

            if (_markers != null)
            {
                _markers.SetRoster(_roster);
            }

            SetText(scoreText, BuildScoreLine());
        }

        /// <summary>"Arkadaşın düştü" line when a squad mate flips alive → down.</summary>
        /// <remarks>Read from the ROSTER and not from <c>kill_event</c>: a mate goes down to things
        /// that are not a kill of ours to hear about, and the alive flag is the one truth the strip and
        /// the markers already use.
        /// <para>The flags are updated for everyone even while the line is throttled or the run is not
        /// running — otherwise the next drop would compare against a stale frame.</para></remarks>
        private void TrackMatesDowned()
        {
            bool running = _playing && !_ended && _over.Length == 0;
            int self = LocalPlayerId;
            float now = Time.unscaledTime;
            bool said = false;

            for (int i = 0; i < _roster.Length; i++)
            {
                PlayerInfo info = _roster[i];
                if (!IsSquadMate(info, self))
                {
                    continue;
                }

                bool known = _mateAlive.TryGetValue(info.playerId, out bool wasAlive);
                _mateAlive[info.playerId] = info.alive;

                // A mate first seen already down is not a fresh drop: the line would announce the past.
                if (said || !running || !known || !wasAlive || info.alive || now < _mateDownedVoiceAt)
                {
                    continue;
                }

                said = true;
                _mateDownedVoiceAt = now + MateDownedVoiceGap;
                PlayVoice(ZombieSounds.Live != null ? ZombieSounds.Live.MateDownedVoice : null);
            }
        }

        /// <summary>Feeds the wave cells, the clock alarm and the between-waves countdown from
        /// <c>modeState</c>.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            int previousWave = _wave;
            bool previousFinalStand = _finalStand;
            ParseState(msg.modeState);

            _clockSeconds = msg.timeRemaining;
            _clockStamp = Time.unscaledTime;

            SetText(waveText, _wave.ToString());
            SetText(remainingText, WaveTimeCell());

            bool playing = msg.phase == ArenaProtocol.PHASE_PLAYING;
            _playing = playing;
            if (playing)
            {
                // A running match clears the latch the result screen set.
                _ended = false;
                _outcomeCuePlayed = false;
            }

            SetClockAlarm(playing && msg.timeRemaining > 0f
                          && msg.timeRemaining <= ClockAlarmSeconds);

            // The clear phase is the "you cleared it" screen; a finished run says it with the result
            // screen instead. The names are resolved ONCE here — the notice is redrawn every state.
            bool inClear = playing && !_finalStand && _over.Length == 0 && _clearLeft > 0 && _wave >= 1;
            if (inClear && _wave > _clearedWave)
            {
                _clearedWave = _wave;
                _unlockNames = ZombieWeaponUnlocks.UnlockedNamesAt(_wave + 1);
            }

            // Between waves the big centre number is the only thing that explains why nothing is
            // coming; the title/subtitle around it ride OnCenterNoticeRefreshed.
            // The final stand has no next wave: a leftover countdown there would promise one.
            // ⚠️ The wipe countdown comes FIRST: being down as a squad is the more urgent message and
            // it can run while a wave gap does not.
            SetCenterNotice(playing && _wipeLeft > 0 && _over.Length == 0
                ? _wipeLeft.ToString()
                : inClear
                    ? ClearNoticeBlank
                    : (playing && !_finalStand && _nextWaveSeconds > 0
                        ? _nextWaveSeconds.ToString()
                        : ""));

            // A wave starts when its number goes up AND nothing is counting down any more.
            if (playing && _wave > previousWave && _nextWaveSeconds == 0 && _wave > _toastedWave)
            {
                _toastedWave = _wave;
                string special = SpecialTitle(_special);
                if (special.Length > 0)
                {
                    ShowToast($"DALGA {_wave}", special, WaveToastSeconds);
                }
                else
                {
                    ShowToast("", $"DALGA {_wave} BAŞLADI", WaveToastSeconds);
                }
            }

            if (playing && _finalStand && !previousFinalStand)
            {
                ShowToast("BÜTÜN SİLAHLAR AÇIK", "SON DİRENİŞ", FinalStandToastSeconds);

                // The last fight starts with the squad on its feet; there is no break left to wait for.
                _finalStandReviveUntil = Time.unscaledTime + FinalStandReviveSeconds;
            }
        }

        /// <summary>Dresses the base's single centre text: the number is the base's, the wave title and
        /// the lines around it are this mode's — "dalga tamamlandı" + the new weapons in the clear
        /// phase, "DALGA N" + "HAZIRLAN" over the countdown after it.</summary>
        protected override void OnCenterNoticeRefreshed(string text, bool countdown)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // The squad is down: the number above is the rescue countdown, not a wave's.
            if (_wipeLeft > 0 && _playing && _over.Length == 0)
            {
                SetText(noticeTitleText, "EKİP DÜŞTÜ");
                SetText(noticeSubText, "SN SONRA HEP BERABER KALKIYORSUNUZ");
                SetText(noticeUnlockText, "");
                return;
            }

            // Same gate as the clear phase itself: a finished run says it with the result screen, and
            // a stale `clr` must not put "tamamlandı" over it.
            if (_clearLeft > 0 && !_finalStand && _playing && _over.Length == 0)
            {
                SetText(noticeTitleText, $"DALGA {_wave} TAMAMLANDI");
                SetText(noticeSubText, _unlockNames.Length > 0 ? "YENİ SİLAHLAR AÇILDI" : "");
                SetText(noticeUnlockText, _unlockNames);
                return;
            }

            // Both cases count down to the SAME thing (the next wave starting), so one formula: at
            // match start the wave is still 0 and the countdown belongs to wave 1.
            SetText(noticeTitleText, $"DALGA {Mathf.Max(1, _wave + 1)}");
            SetText(noticeSubText, "HAZIRLAN");
            SetText(noticeUnlockText, "");
        }

        // ---------------------------------------------------------------- internals

        /// <summary>SÜRE cell: the running wave's clock, or the countdown to the next wave between
        /// waves. Dash while neither counts (the wave's tail waiting for the last zombie, the final
        /// stand) — a frozen 0:00 would read as a stopped clock.</summary>
        private string WaveTimeCell()
        {
            if (_waveLeft > 0)
            {
                return WaveClock(_waveLeft);
            }

            return _nextWaveSeconds > 0 ? WaveClock(_nextWaveSeconds) : "—";
        }

        /// <summary><c>m:ss</c>, not the base's <c>mm:ss</c>: the cell is 150 px wide and the leading
        /// zero costs a glyph the value font does not have room for.</summary>
        private static string WaveClock(int seconds)
        {
            int total = Mathf.Max(0, seconds);
            return $"{total / 60}:{total % 60:00}";
        }

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

        /// <summary><c>"w:3;left:5;next:0;wl:88"</c> (+ optional <c>over:…</c>) → the cached cells.</summary>
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

            // `wx`, `clr`, `wp` and `rv` ride only while they run, so their absence is meaningful — but
            // only in a TOKEN state: the pause reason travels in the same field as free text.
            if (modeState.IndexOf(':') >= 0)
            {
                _special = string.Empty;
                _clearLeft = 0;
                _wipeLeft = 0;
                _wipeReviveLeft = 0;
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

                if (string.Equals(key, ZombieKinds.StateSpecial, StringComparison.Ordinal))
                {
                    _special = raw;
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateDev, StringComparison.Ordinal))
                {
                    ParseDev(raw);
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
                else if (string.Equals(key, ZombieKinds.StateNext, StringComparison.Ordinal))
                {
                    _nextWaveSeconds = value;
                }
                else if (string.Equals(key, ZombieKinds.StateClear, StringComparison.Ordinal))
                {
                    _clearLeft = value;
                }
                else if (string.Equals(key, ZombieKinds.StateWaveLeft, StringComparison.Ordinal))
                {
                    _waveLeft = value;
                }
                else if (string.Equals(key, ZombieKinds.StateWipe, StringComparison.Ordinal))
                {
                    _wipeLeft = value;
                }
                else if (string.Equals(key, ZombieKinds.StateWipeRevive, StringComparison.Ordinal))
                {
                    _wipeReviveLeft = value;
                }
                else if (string.Equals(key, ZombieKinds.StateFinalStand, StringComparison.Ordinal))
                {
                    _finalStand = value != 0;
                }
            }
        }

        /// <summary><c>dev:&lt;netId&gt;/&lt;maxHp&gt;</c> → the boss bar's object and scale. A malformed
        /// value leaves the bar closed rather than drawing an empty one.</summary>
        private void ParseDev(string raw)
        {
            int slash = raw.IndexOf('/');
            if (slash <= 0 || slash == raw.Length - 1
                || !int.TryParse(raw.Substring(0, slash), out int netId)
                || !float.TryParse(raw.Substring(slash + 1),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float maxHp))
            {
                return;
            }

            _devNetId = netId;
            _devMaxHp = maxHp;
        }

        // ---------------------------------------------------------------- boss bar

        /// <summary>The Giant's health and the clock left, while it is on the floor.</summary>
        private void RefreshBossBar()
        {
            if (bossRoot == null)
            {
                return;
            }

            bool show = _playing && !_ended && _over.Length == 0 && _devNetId != 0;
            if (bossRoot.activeSelf != show)
            {
                bossRoot.SetActive(show);
                if (show)
                {
                    SetText(bossTitleText, "DEV");
                }
            }

            if (!show)
            {
                return;
            }

            // A missing object reads as 0, not as full: a full bar on a Giant nobody can see is a lie.
            float ratio = 0f;
            if (_devMaxHp > 0f && NetObjectRegistry.TryGet(_devNetId, out NetObject dev) && dev != null)
            {
                ratio = Mathf.Clamp01(dev.Hp / _devMaxHp);
            }

            if (bossFill != null)
            {
                bossFill.fillAmount = ratio;
            }

            string line = $"SON DİRENİŞ · {FormatTime(ClockLeft)}";
            if (!string.Equals(_bossTimeCache, line, StringComparison.Ordinal))
            {
                _bossTimeCache = line;
                SetText(bossTimeText, line);
            }
        }

        /// <summary>Closes the bar and keeps it closed: the result screen is up and no state follows
        /// that would hide it on its own.</summary>
        private void HideBossBar()
        {
            _ended = true;
            _bossTimeCache = string.Empty;
            if (bossRoot != null && bossRoot.activeSelf)
            {
                bossRoot.SetActive(false);
            }
        }

        /// <summary>Seconds left on the match clock, ticked between the 1 Hz states.</summary>
        private float ClockLeft => _clockStamp < 0f
            ? 0f
            : Mathf.Max(0f, _clockSeconds - (Time.unscaledTime - _clockStamp));

        // ------------------------------------------------------------- dead player prompt

        /// <summary>The downed player's only instruction. ⚠️ Written through the core's mode prompt,
        /// which OVERRIDES the generic death text: with <c>reviveAnchor:"teammate"</c> that text would
        /// send them to a base that cannot revive them.</summary>
        private void RefreshDeadPrompt()
        {
            // The run is over: the result screen is up and "arkadaşın seni kaldırsın" would ask for a
            // pick-up that can no longer happen.
            if (_ended || _over.Length > 0)
            {
                ClearPrompt();
                return;
            }

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

            // The squad is down: nobody can lift anybody, the rescue is on its way.
            if (_wipeLeft > 0)
            {
                SetPrompt($"EKİP DÜŞTÜ · {_wipeLeft} sn sonra hep beraber kalkıyorsunuz");
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
