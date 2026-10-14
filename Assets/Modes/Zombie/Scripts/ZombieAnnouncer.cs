using System;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Audio;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The run's MATCH-LEVEL cues: the wave cleared sting and line, the new weapons line and
    /// its unlock cue, the between-waves ticks, the wave start sting, the Last Stand and the Dev.</summary>
    /// <remarks>Self-bootstrapping singleton with no component in any scene
    /// (<see cref="ZombieMusic"/> pattern): it must sound on the ADMIN too, where there is no HUD
    /// prefab at all — the operator follows the run by ear.
    /// <para>⚠️ Split from <see cref="ZombieClientController"/> along one line: what the whole hall
    /// hears belongs here, what is PERSONAL (a mate going down, a pick-up, your own result) stays in
    /// the HUD. A cue in both places plays twice on a headset.</para>
    /// <para>Every moment is an EDGE of <see cref="ZombieWaveState"/> and never a re-reading of the
    /// state: <c>match_state</c> repeats at 1 Hz.</para></remarks>
    [DisallowMultipleComponent]
    internal class ZombieAnnouncer : MonoBehaviour
    {
        /// <summary>Delay (s) between the clear sting and the weapon-unlock cue: played together they
        /// mask each other and the squad hears neither.</summary>
        private const float UnlockCueDelay = 1.2f;

        /// <summary>From this second of the between-waves countdown on, each second gets a tick.</summary>
        private const int CountdownTickFrom = 5;

        private static ZombieAnnouncer _instance;

        /// <summary>2D source for the stings; the voices ride the announcement channel instead.</summary>
        private AudioSource _sting;

        /// <summary>Unscaled time the unlock cue is due at; negative = nothing pending.</summary>
        private float _unlockCueAt = -1f;

        /// <summary>Countdown second the tick was last played for; 0 = not counting.</summary>
        private int _tickedSecond;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[ZombieAnnouncer]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ZombieAnnouncer>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy, so nothing is missed while the
            // object is inactive.
            ZombieWaveState.ClearStarted += HandleClearStarted;
            ZombieWaveState.WaveStarted += HandleWaveStarted;
            ZombieWaveState.FinalStandChanged += HandleFinalStandChanged;
            ZombieWaveState.GiantAppeared += HandleGiantAppeared;
            ZombieWaveState.StateApplied += HandleStateApplied;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            ZombieWaveState.ClearStarted -= HandleClearStarted;
            ZombieWaveState.WaveStarted -= HandleWaveStarted;
            ZombieWaveState.FinalStandChanged -= HandleFinalStandChanged;
            ZombieWaveState.GiantAppeared -= HandleGiantAppeared;
            ZombieWaveState.StateApplied -= HandleStateApplied;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnDisconnected -= HandleDisconnected;

            _instance = null;
        }

        private void Update()
        {
            if (_unlockCueAt < 0f || Time.unscaledTime < _unlockCueAt)
            {
                return;
            }

            _unlockCueAt = -1f;
            PlaySting(ZombieSounds.Live != null ? ZombieSounds.Live.WeaponUnlockClip : null);
        }

        /// <summary>Are the zombie rules the ones running? The state edges already carry the phase, so
        /// only the mode itself is asked here.</summary>
        private static bool ZombieRunning =>
            string.Equals(ModeRuntime.ModeId, ZombieKinds.Zombie, StringComparison.Ordinal);

        // ------------------------------------------------------------------ moments

        /// <summary>Clear phase opened: the sting, "dalga tamamlandı", then the new weapons line and
        /// the unlock cue of the boards.</summary>
        private void HandleClearStarted(int wave)
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (!ZombieRunning || sounds == null)
            {
                return;
            }

            PlaySting(sounds.WaveClearSting);
            PlayVoice(sounds.WaveClearVoice);

            // Only when the next wave really opens something: announcing weapons nobody got is worse
            // than silence.
            if (!ZombieArsenal.Live.OpensAt(wave + 1))
            {
                _unlockCueAt = -1f;
                return;
            }

            PlayVoice(sounds.NewWeaponsVoice);
            _unlockCueAt = Time.unscaledTime + UnlockCueDelay;
        }

        /// <summary>The wave is on the floor — the start sting lands HERE, at the end of the countdown,
        /// and nothing before it may sound like a start.</summary>
        private void HandleWaveStarted(int wave)
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (!ZombieRunning || sounds == null)
            {
                return;
            }

            PlaySting(sounds.WaveStartSting);

            // Every wave is a round start for the ear. ⚠️ Wave 1 is skipped: GameAudio already played
            // it on countdown → playing, and both would land on the same moment.
            if (wave >= 2 && !GameAudio.PlayModeEvent(ModeAudioEvent.RoundStart))
            {
                GameAudio.Play(GameSoundId.MatchStart);
            }
        }

        private void HandleFinalStandChanged()
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (!ZombieRunning || sounds == null || !ZombieWaveState.FinalStand ||
                !ZombieWaveState.Playing || ZombieWaveState.Over.Length > 0)
            {
                return;
            }

            // Falling back to the wave sting: silence would drop the loudest moment of the run.
            PlaySting(sounds.FinalStandSting != null ? sounds.FinalStandSting : sounds.WaveStartSting);
            PlayVoice(sounds.FinalStandVoice);
        }

        private void HandleGiantAppeared()
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (ZombieRunning && sounds != null)
            {
                PlayVoice(sounds.GiantIncomingVoice);
            }
        }

        /// <summary>One tick per remaining second of the between-waves countdown. ⚠️ Keyed to the
        /// VALUE: <c>match_state</c> arrives at 1 Hz and a repeated state must not tick twice.</summary>
        private void HandleStateApplied()
        {
            int next = ZombieWaveState.Next;
            bool ticking = ZombieRunning && ZombieWaveState.Playing && !ZombieWaveState.FinalStand &&
                           ZombieWaveState.Over.Length == 0 && next > 0 && next <= CountdownTickFrom;
            if (!ticking)
            {
                _tickedSecond = 0;
                return;
            }

            if (next == _tickedSecond)
            {
                return;
            }

            _tickedSecond = next;
            PlaySting(ZombieSounds.Live != null ? ZombieSounds.Live.CountdownTick : null);
        }

        private void HandleLoadMatch(LoadMatchMsg msg) => ResetRun();

        private void HandleReturnToLobby(ReturnToLobbyMsg msg) => ResetRun();

        private void HandleDisconnected() => ResetRun();

        private void ResetRun()
        {
            _unlockCueAt = -1f;
            _tickedSecond = 0;
        }

        // ------------------------------------------------------------------- output

        /// <summary>A 2D cue of the mode (Müzik kanalı — the mix has no UI/SFX channel). A missing clip
        /// is silence, not a warning.</summary>
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

        /// <summary>A spoken line of the mode, on the ONE announcement channel so it never talks over
        /// the core's lines.</summary>
        /// <remarks>⚠️ <see cref="GameAudio.NeverExpires"/>: these describe the PHASE the squad is in
        /// ("dalga tamamlandı" → "yeni silahlar"), and the default wait drops the second line while the
        /// first is still speaking.</remarks>
        private static void PlayVoice(AudioClip clip)
        {
            ZombieSounds sounds = ZombieSounds.Live;
            if (clip != null && sounds != null)
            {
                GameAudio.PlayAnnouncement(clip, sounds.VoiceVolume, GameAudio.NeverExpires);
            }
        }
    }
}
