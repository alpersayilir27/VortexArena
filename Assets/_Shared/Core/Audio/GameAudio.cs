using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Audio
{
    /// <summary>The ONLY place that plays map-independent announcement/feedback sounds. Clips come
    /// from <see cref="GameSoundBank"/>; which event triggers which sound lives here, not in a scene
    /// or HUD.</summary>
    /// <remarks>
    /// Self-bootstrapping persistent singleton; NO component is placed in the scene — an arena setup
    /// step would eventually be forgotten on a new arena.
    /// <para>The sound is 2D: an announcement is heard inside the player's head, it has no place in
    /// the arena.</para>
    /// <para>Player-specific sounds (kill/death/revive) are skipped silently when the local player id
    /// cannot be resolved (admin spectator, not yet connected); phase sounds play for the operator
    /// too. The match RESULT announcement is not player-specific and plays identically for
    /// everyone, admin included.</para>
    /// <para>Adding a sound: append to <see cref="GameSoundId"/> + a field in
    /// <see cref="GameSoundBank"/> + a <see cref="Play"/> call at the trigger.</para>
    /// <para>Sounds that vary by mode/map come from <see cref="ModeAudioRegistry"/> instead, via
    /// <see cref="PlayModeEvent"/>. When both are filled for the same moment the registry overrides
    /// the bank so they do not overlap.</para>
    /// <para>Announcements play SEQUENTIALLY on one channel (from bank or registry alike): they are
    /// spoken lines and two overlapping ones are unintelligible. An announcement arriving on a busy
    /// channel does NOT interrupt, it queues (see <see cref="Announce"/>). The exception is
    /// <see cref="IsInstant"/>: cues whose meaning is their timing (the countdown beep) do not
    /// wait.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        /// <summary>Queue depth; on overflow the NEWEST is dropped.
        /// <para>Dropping the oldest would be wrong: the queued announcement describes an event that
        /// already happened (a kill, a round end) and skipping it leaves the player uninformed. The
        /// cap is a safety net — a normal match never queues more than two.</para></summary>
        private const int AnnouncementQueueLimit = 4;

        /// <summary>How long an announcement may wait in the queue (s); past that it never plays.
        /// <para>A late announcement is a wrong announcement — hearing "enemy eliminated" in the
        /// middle of the between-rounds gathering makes the player think it just
        /// happened.</para></summary>
        private const float AnnouncementTtlSeconds = 4f;

        /// <summary>Breathing gap between two lines (s): back-to-back sentences are heard as
        /// one.</summary>
        private const float AnnouncementGapSeconds = 0.1f;

        /// <summary>Catalog asset name under Resources — same path as <see cref="ModeRuntime"/>.</summary>
        private const string CatalogResourceName = "GameCatalog";

        /// <summary>A single queued announcement: clip, volume and expiry.
        /// <para>The volume is computed on enqueue (with the <see cref="AudioMix.Voiceover"/> of that
        /// moment): the announcement belongs to the event's moment, not to a knob the operator turns
        /// while it waits.</para></summary>
        private readonly struct PendingAnnouncement
        {
            public readonly AudioClip Clip;
            public readonly float Volume;
            public readonly float ExpiresAt;

            public PendingAnnouncement(AudioClip clip, float volume, float expiresAt)
            {
                Clip = clip;
                Volume = volume;
                ExpiresAt = expiresAt;
            }
        }

        public static GameAudio Instance { get; private set; }

        /// <summary>Catalog cache; the flag is separate so a missing asset is not looked up on every
        /// sound.</summary>
        private static GameCatalog _catalog;
        private static bool _catalogLoaded;

        private AudioSource _source;

        /// <summary>Queued announcements (FIFO). Order = arrival order, which is correct: the server
        /// already sends events causally ordered (<c>kill_event</c> first, then the
        /// <c>match_state</c> of the round that death ended) and WS preserves order. A priority table
        /// would restate that ordering from a second place.</summary>
        private readonly Queue<PendingAnnouncement> _announcements = new Queue<PendingAnnouncement>();

        /// <summary>When the announcement channel frees up (<see cref="Time.unscaledTime"/>).
        /// <para>Busy-ness is measured from clip length, NOT <c>AudioSource.isPlaying</c>: instant
        /// cues (beeps) play on the same source and <c>isPlaying</c> would count them, delaying the
        /// next line after every beep.</para></summary>
        private float _channelFreeAt;

        /// <summary>Last known phase — <c>match_state</c> repeats, so playing only ON TRANSITION
        /// depends on this.</summary>
        private string _lastPhase = "";

        /// <summary>Previous <c>match_state</c>'s remaining time; <c>-1</c> = no sample yet. Kept so
        /// the warning fires on the sample where the threshold is CROSSED.</summary>
        private float _lastTimeRemaining = -1f;

        /// <summary>Has the time warning fired for this round/match — reset on every <c>playing</c>
        /// transition.</summary>
        private bool _warningFired;

        /// <summary>Last roster (§5.3) — its only consumer is the victim's team
        /// (<see cref="IsTeammate"/>). <c>kill_event</c> carries no team and never will: the team
        /// already arrives via <c>lobby_state</c>, and a second channel would be a second source of
        /// truth.
        /// <para>⚠️ The roster is republished in full on every change (team changes included), so
        /// this copy cannot go stale.</para></summary>
        private LobbyStateMsg _roster;

        /// <summary>Is a spoken line occupying the announcement channel right now.</summary>
        /// <remarks>Read by whoever plays UNDER the announcements (the operator's music bed) to duck
        /// while a line is running. Measured from clip length like <see cref="_channelFreeAt"/>, so
        /// an instant cue (the countdown beep) does not count as speech.
        /// <para>⚠️ READ ONLY, and it stays that way: nothing outside may hold the channel. Who
        /// speaks when is decided here and nowhere else.</para></remarks>
        public static bool Announcing =>
            Instance != null && Time.unscaledTime < Instance._channelFreeAt;

        /// <summary>Plays the sound. A no-op when the singleton does not exist yet or the clip is
        /// unassigned — the caller needs no guard.</summary>
        public static void Play(GameSoundId id, float volumeScale = 1f)
        {
            if (Instance != null)
            {
                Instance.PlayInternal(id, volumeScale);
            }
        }

        /// <summary>Plays the mode/map-specific sound: if <see cref="ModeAudioRegistry"/> has a rule
        /// for that trigger under the active mode + scene, one of its clips is picked at random.
        /// <para><c>false</c> = no rule/clip for that moment (the caller may fall back to the shared
        /// bank); also <c>false</c> when the singleton does not exist yet.</para></summary>
        public static bool PlayModeEvent(ModeAudioEvent trigger)
        {
            return Instance != null &&
                   TryResolve(trigger, out ModeAudioRegistry.Rule rule) &&
                   Instance.PlayRule(rule);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[GameAudio]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.spatialize = false;

            // Persistent singleton: subscribe in Awake/OnDestroy so events are not missed if the
            // object is deactivated (PlayerCombatState pattern).
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnRespawn += HandleRespawn;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnRespawn -= HandleRespawn;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            Instance = null;
        }

        private void PlayInternal(GameSoundId id, float volumeScale)
        {
            GameSoundBank bank = GameSoundBank.Load();
            if (bank == null || _source == null)
            {
                return;
            }

            AudioClip clip = bank.Clip(id);
            if (clip == null)
            {
                return;
            }

            float volume = Mathf.Clamp01(bank.Volume * ChannelScale(id) * Mathf.Max(0f, volumeScale));

            if (IsInstant(id))
            {
                PlayInstant(clip, volume);
                return;
            }

            Announce(clip, volume);
        }

        /// <summary>Plays a cue OUTSIDE the announcement channel: no queue, no wait, and it does not
        /// push back a waiting line (<see cref="_channelFreeAt"/> is left alone).</summary>
        private void PlayInstant(AudioClip clip, float volume)
        {
            if (clip != null && _source != null)
            {
                _source.PlayOneShot(clip, volume);
            }
        }

        /// <summary>Local mix level for the sound.</summary>
        /// <remarks>⚠️ <see cref="GameSoundId.AdminViolation"/> is EXEMPT from the voiceover channel:
        /// a physical violation cue is the operator's safety warning and already has its own switch
        /// (<c>AdminSession.ViolationSound</c>) — "I turned the game voiceover down" must not silence
        /// it.</remarks>
        private static float ChannelScale(GameSoundId id)
        {
            return id == GameSoundId.AdminViolation ? 1f : AudioMix.Voiceover;
        }

        /// <summary>Is this an instant cue whose meaning is its timing, rather than a spoken line?
        /// Instant cues never queue, never wait and never delay a waiting line.
        /// <para>⚠️ The default is "announcement" (<c>false</c>): a sound appended to
        /// <see cref="GameSoundId"/> queues by itself. The other way round, every new line would
        /// silently cut the previous one.</para>
        /// <para>The criterion is not "is it short" but "would it be a lie if played late": the
        /// countdown beep IS the second and would mark the wrong one if it waited for a line to
        /// finish. The violation cue likewise tells the operator where to look RIGHT
        /// NOW.</para></summary>
        private static bool IsInstant(GameSoundId id)
        {
            return id == GameSoundId.CountdownTick || id == GameSoundId.AdminViolation;
        }

        /// <summary>Hands the announcement to the channel: plays immediately when free, otherwise
        /// QUEUES — it never interrupts what is playing.
        /// <para>⚠️ Not interrupting is deliberate: announcements form a causal chain ("enemy
        /// eliminated" → "round over, return to your positions"), and cutting the first link means
        /// never telling the player WHY the round ended. In 1v1 the server closes the round ~100 ms
        /// after processing the death, so a last-wins rule cut the kill line on its first
        /// syllable.</para>
        /// <para><c>false</c> = the sound will never play (no clip, or the queue overflowed); the
        /// caller may fall back.</para></summary>
        private bool Announce(AudioClip clip, float volume)
        {
            if (clip == null || _source == null)
            {
                return false;
            }

            float now = Time.unscaledTime;
            if (_announcements.Count == 0 && now >= _channelFreeAt)
            {
                StartAnnouncement(clip, volume, now);
                return true;
            }

            if (_announcements.Count >= AnnouncementQueueLimit)
            {
                return false;
            }

            _announcements.Enqueue(new PendingAnnouncement(clip, volume, now + AnnouncementTtlSeconds));
            return true;
        }

        /// <summary>Starts the announcement and marks the channel busy for the clip length (+ the
        /// breathing gap).</summary>
        private void StartAnnouncement(AudioClip clip, float volume, float now)
        {
            _source.PlayOneShot(clip, volume);
            _channelFreeAt = now + clip.length + AnnouncementGapSeconds;
        }

        /// <summary>Queue pump: starts the next line when the channel frees up, drops stale ones.
        /// </summary>
        /// <remarks>⚠️ The queue only advances here — deactivating the object would stop all
        /// announcements (nothing does today: the singleton owns its persistent object).</remarks>
        private void Update()
        {
            if (_announcements.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _channelFreeAt)
            {
                return;
            }

            while (_announcements.Count > 0)
            {
                PendingAnnouncement next = _announcements.Dequeue();
                if (next.Clip == null || now > next.ExpiresAt)
                {
                    continue;
                }

                StartAnnouncement(next.Clip, next.Volume, now);
                return;
            }
        }

        /// <summary>Clears the queue and cuts the playing line — when the session ends.</summary>
        /// <remarks>The only legitimate place to interrupt: after a disconnect the queued
        /// announcement describes a match that no longer exists.</remarks>
        private void ClearAnnouncements()
        {
            _announcements.Clear();
            _channelFreeAt = 0f;

            if (_source != null)
            {
                _source.Stop();
            }
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            int local = ArenaCombat.LocalPlayerId;
            if (msg == null || local <= 0)
            {
                return;
            }

            if (msg.victimId == local)
            {
                Play(GameSoundId.LocalDeath);
                return;
            }

            // killerId == victimId: suicide/environmental death (§10.9) — no killer, no announcement.
            if (msg.killerId == local && msg.killerId != msg.victimId)
            {
                Play(IsTeammate(msg.victimId)
                    ? GameSoundId.TeammateEliminated
                    : GameSoundId.EnemyEliminated);
            }
        }

        /// <summary>Whether the victim is a TEAMMATE of the local player — picks which announcement
        /// plays when friendly fire is on (§10.5 <c>set_friendly_fire</c>).</summary>
        /// <remarks>
        /// ⚠️ The question is NOT "is friendly fire on": with it off the server never records
        /// teammate damage, so such a <c>kill_event</c> never exists. Gating on
        /// <see cref="ModeRuntime.FriendlyFire"/> would flip the announcement to the wrong side on
        /// any skew between the operator switch and the event's arrival.
        /// <para>⚠️ When unknown it says "enemy" (teamless mode, local team still
        /// <see cref="Team.Neutral"/>, victim missing from the roster): announcing a friendly fire
        /// that did not happen misleads more than staying silent on a real kill.</para>
        /// </remarks>
        private bool IsTeammate(int victimId)
        {
            if (ModeRuntime.IsTeamless)
            {
                return false;
            }

            Team local = ArenaCombat.LocalTeam;
            return local != Team.Neutral && RosterTeam(victimId) == local;
        }

        /// <summary>Team of a roster player; <see cref="Team.Neutral"/> when absent.</summary>
        /// <remarks>Anything other than <c>"red"</c>/<c>"blue"</c> (empty included) is
        /// <see cref="Team.Neutral"/> — in teamless modes the server CLEARS teams (§10.5).</remarks>
        private Team RosterTeam(int playerId)
        {
            if (_roster?.players == null)
            {
                return Team.Neutral;
            }

            for (int i = 0; i < _roster.players.Length; i++)
            {
                PlayerInfo info = _roster.players[i];
                if (info == null || info.playerId != playerId)
                {
                    continue;
                }

                if (string.Equals(info.team, "red", StringComparison.OrdinalIgnoreCase))
                {
                    return Team.Red;
                }

                return string.Equals(info.team, "blue", StringComparison.OrdinalIgnoreCase)
                    ? Team.Blue
                    : Team.Neutral;
            }

            return Team.Neutral;
        }

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg != null && msg.players != null)
            {
                _roster = msg;
            }
        }

        /// <summary>Drops the roster on disconnect: a new session hands out player ids from scratch
        /// and a stale copy would put the victim on the wrong team. Pending announcements go too —
        /// they describe a match that no longer exists.</summary>
        private void HandleDisconnected()
        {
            _roster = null;
            ClearAnnouncements();
        }

        /// <summary>A new match was loaded (operator <c>start_match</c>): the previous match's
        /// announcements are dropped.</summary>
        /// <remarks>
        /// ⚠️ "Start" pressed over a running match also comes through here, which is the real
        /// reason: the round-end line can be queued or playing while the operator reloads, and it
        /// then describes a round that no longer exists. The phase gate
        /// (<see cref="IsRoundEnd"/>) cannot catch that — the sound was born earlier, when the round
        /// really ended.
        /// <para>Phase history is reset too. The first <c>match_state</c> of a new match is always a
        /// pause (<c>loading</c>), so this delays no start sound; it only makes reading a transition
        /// across two SEPARATE matches structurally impossible.</para>
        /// </remarks>
        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            ResetForNewMatch();
        }

        /// <summary>Returned to the lobby (§10.7) — no match, nothing for a queued announcement to
        /// describe.</summary>
        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            ResetForNewMatch();
        }

        /// <summary>Prepares the announcement channel and phase history for a new match.</summary>
        private void ResetForNewMatch()
        {
            ClearAnnouncements();
            _lastPhase = "";
            _lastTimeRemaining = -1f;
            _warningFired = false;
        }

        private void HandleRespawn(RespawnMsg msg)
        {
            int local = ArenaCombat.LocalPlayerId;
            if (msg != null && local > 0 && msg.playerId == local)
            {
                Play(GameSoundId.LocalRespawn);
            }
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string phase = msg.phase ?? "";
            bool playing = string.Equals(phase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal);

            if (!string.Equals(phase, _lastPhase, StringComparison.Ordinal))
            {
                // ⚠️ No sound on the first message (_lastPhase empty): a headset joining a running
                // match must not hear "match started".
                bool started = _lastPhase.Length > 0 && playing;
                bool roundEnded = IsRoundEnd(_lastPhase, phase, msg.phaseReason);
                _lastPhase = phase;

                if (started)
                {
                    // Round based modes pass here every round → the warning is armed per round.
                    _warningFired = false;
                    _lastTimeRemaining = -1f;

                    // Mode/map specific intro OVERRIDES the shared bank so they do not overlap.
                    if (!PlayModeEvent(ModeAudioEvent.RoundStart))
                    {
                        Play(GameSoundId.MatchStart);
                    }
                }
                else if (roundEnded)
                {
                    // No shared bank counterpart: round end belongs to round based modes, staying
                    // silent without a rule is correct.
                    PlayModeEvent(ModeAudioEvent.RoundEnd);
                }
            }

            if (playing)
            {
                TickTimeWarning(msg.timeRemaining);
            }
        }

        /// <summary>Whether this transition is a round end: <c>playing</c> → <c>paused</c> +
        /// <c>phaseReason == "mode"</c>.</summary>
        /// <remarks>
        /// ⚠️ The criterion is the phase, NOT <c>modeId</c> — no <c>if (modeId == …)</c> chain on the
        /// client (§10.5). "The mode asked for a pause" is the core's only between-rounds signal;
        /// which mode uses it is stated by the <see cref="ModeAudioRegistry"/> rule, not by code.
        /// <para>⚠️ <c>modeState</c> is not parsed: it is a free string the core does not interpret
        /// (§10.1) — a mode rewording its text must not silence the sound.</para>
        /// <para>⚠️ The reason is checked because of the operator: the mode is not the only source of
        /// a pause. <c>start_match</c> over a running match (<c>loading</c>), a manual pause
        /// (<c>operator</c>) and <c>abort_match</c>/<c>return_to_lobby</c> (<c>lobby</c>) are all
        /// <c>playing</c> → <c>paused</c>, and in none of them did the round end naturally; a
        /// phase-only gate would announce "round over" for all three.</para>
        /// <para>Never fires on the first <c>match_state</c> (empty previous phase): a headset
        /// joining between rounds must not hear the announcement of a round it missed.</para>
        /// </remarks>
        private static bool IsRoundEnd(string previousPhase, string phase, string phaseReason)
        {
            return string.Equals(previousPhase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal) &&
                   string.Equals(phase, ArenaProtocol.PHASE_PAUSED, StringComparison.Ordinal) &&
                   string.Equals(phaseReason, ArenaProtocol.PAUSE_REASON_MODE, StringComparison.Ordinal);
        }

        /// <summary>Plays the sound once when the remaining time crosses the warning
        /// threshold.</summary>
        /// <remarks>The time is server authoritative and arrives at 1 Hz with <c>match_state</c>;
        /// it is only read here, no client side countdown.</remarks>
        private void TickTimeWarning(float timeRemaining)
        {
            float previous = _lastTimeRemaining;
            _lastTimeRemaining = timeRemaining;

            // The first sample never counts as a crossing: a headset joining in the final seconds
            // must not hear the time warning out of nowhere.
            if (_warningFired || previous < 0f)
            {
                return;
            }

            if (!TryResolveWarning(out ModeAudioRegistry.Rule rule))
            {
                return;
            }

            // At 1 Hz the threshold second arrives as ~N.0; the half second margin keeps that sample
            // from being missed and the warning from firing a second late.
            float threshold = rule.WarningSeconds + 0.5f;
            if (previous > threshold && timeRemaining <= threshold)
            {
                _warningFired = true;
                PlayRule(rule);
            }
        }

        /// <summary>Time warning rule: round first, then match.</summary>
        /// <remarks>Whether a mode is round based is stated by the REGISTRY, not by
        /// <c>modeState</c> — the core does not interpret a mode's internal state
        /// (Docs/ArenaNet-Protokol.md §10.1).</remarks>
        private static bool TryResolveWarning(out ModeAudioRegistry.Rule rule)
        {
            return TryResolve(ModeAudioEvent.RoundEndWarning, out rule) ||
                   TryResolve(ModeAudioEvent.MatchEndWarning, out rule);
        }

        /// <summary>Resolves the rule for the active mode + scene + game type.</summary>
        private static bool TryResolve(ModeAudioEvent trigger, out ModeAudioRegistry.Rule rule)
        {
            ModeAudioRegistry registry = ModeAudioRegistry.Load();
            if (registry == null)
            {
                rule = null;
                return false;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            return registry.TryResolve(trigger, ModeRuntime.ModeId, sceneName,
                ActiveGameType(sceneName), out rule);
        }

        /// <summary>Game type (family) of the moment, read from the catalog.</summary>
        /// <remarks>The scene decides first: a lobby standing in a children's venue is part of that
        /// family too. Only without a map does the mode answer, and without either the default is
        /// competitive play.
        /// <para>⚠️ The family comes from the CATALOG, not from the wire — this is presentation
        /// only, no server authority is involved.</para></remarks>
        private static GameType ActiveGameType(string sceneName)
        {
            if (!_catalogLoaded)
            {
                _catalogLoaded = true;
                _catalog = Resources.Load<GameCatalog>(CatalogResourceName);
            }

            if (_catalog == null)
            {
                return GameType.QuickBattle;
            }

            MapDefinition map = _catalog.FindMap(sceneName);
            if (map != null)
            {
                return map.GameType;
            }

            ModeDefinition mode = _catalog.FindMode(ModeRuntime.ModeId);
            return mode != null ? mode.GameType : GameType.QuickBattle;
        }

        /// <summary>Hands one of the rule's clips to the announcement channel; <c>false</c> when
        /// there is no clip (or the queue overflowed).</summary>
        /// <remarks>⚠️ A registry announcement has no privilege over a bank one: both enter the same
        /// queue in arrival order (<see cref="Announce"/>). "Registry overrides bank" is a SELECTION
        /// rule — it picks between two clips for the same moment, it is not permission to cut what
        /// is playing.
        /// <para><c>true</c> means "will play", not necessarily "playing now"; for the caller's
        /// fallback decision the difference is irrelevant.</para></remarks>
        private bool PlayRule(ModeAudioRegistry.Rule rule)
        {
            AudioClip clip = rule != null ? rule.PickClip() : null;
            return clip != null && Announce(clip, Mathf.Clamp01(rule.Volume * AudioMix.Voiceover));
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            _lastPhase = ArenaProtocol.PHASE_FINISHED;

            // ⚠️ The result announcement belongs to the match, not to a player, so it does not vary
            // by listener and needs no local player id: it plays on the admin spectator too.
            if (!string.IsNullOrEmpty(msg.winnerTeam))
            {
                if (string.Equals(msg.winnerTeam, "red", StringComparison.OrdinalIgnoreCase))
                {
                    Play(GameSoundId.TeamRedWon);
                }
                else if (string.Equals(msg.winnerTeam, "blue", StringComparison.OrdinalIgnoreCase))
                {
                    Play(GameSoundId.TeamBlueWon);
                }

                return;
            }

            if (msg.winnerPlayerId > 0)
            {
                // Individually scored mode (ffa): the winner is a PLAYER, no team announcement
                // applies — that mode's result is read from the end-of-match screen.
                return;
            }

            Play(GameSoundId.MatchDraw);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg == null || msg.seconds <= 0)
            {
                return;
            }

            // The registry owns the countdown when a rule matches; the shared tick is the fallback
            // for everyone else.
            if (!PlayCountdownRule(msg.seconds))
            {
                Play(GameSoundId.CountdownTick);
            }
        }

        /// <summary>Plays the countdown clip of that second; <c>false</c> = no rule, so the shared
        /// bank tick takes over.</summary>
        /// <remarks>⚠️ A matching rule WITHOUT a clip for that second returns <c>true</c>: the row
        /// owns the countdown and chose silence there. Falling back to the tick would mix two
        /// countdowns into one.
        /// <para>The clip plays instantly, off the announcement queue: like the tick, it IS the
        /// second and would mark the wrong one if it waited for a line to finish.</para></remarks>
        private bool PlayCountdownRule(int seconds)
        {
            if (!TryResolve(ModeAudioEvent.Countdown, out ModeAudioRegistry.Rule rule))
            {
                return false;
            }

            AudioClip clip = rule.ClipForSecond(seconds);
            if (clip != null)
            {
                PlayInstant(clip, Mathf.Clamp01(rule.Volume * AudioMix.Voiceover));
            }

            return true;
        }
    }
}
