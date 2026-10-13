using System;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Audio;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie run's two-layer music: a tension layer between waves and a wave layer while
    /// a wave is out, crossfaded into each other.</summary>
    /// <remarks>Self-bootstrapping singleton, no component in any scene (<see cref="SceneAmbience"/>
    /// pattern): a per-arena setup step would silently leave a zombie map music-less.
    /// <para>⚠️ Separate from <see cref="SceneAmbience"/> on purpose: that one plays the MAP's loops and
    /// must not restart on a wave change, while this one is the MODE's layer and lives only as long as a
    /// zombie run does. Both go through <see cref="AudioChannel.Music"/>, so the operator's single music
    /// slider still rules them.</para>
    /// <para>The layers run only while the rules are zombie's and a run is staged
    /// (<see cref="ZombieWeaponUnlocks"/> gating): the lobby, another mode and the finished phase fade
    /// it out.</para>
    /// <para>Both sources loop continuously and only their LEVELS cross; starting and stopping a layer
    /// on every wave would cut the loop mid-bar.</para></remarks>
    [DisallowMultipleComponent]
    internal class ZombieMusic : MonoBehaviour
    {
        /// <summary>Equal-power crossfade duration (s).</summary>
        private const float CrossfadeSeconds = 2f;

        /// <summary>Fade of the whole layer pair when the run ends (s).</summary>
        private const float StopFadeSeconds = 2f;

        /// <summary>Music must not be culled by nearby one-shots, but still comes after them.</summary>
        private const int SourcePriority = 72;

        private static ZombieMusic _instance;

        private AudioSource _tension;
        private AudioSource _wave;

        private ZombieSounds _sounds;

        /// <summary>0 = tension only, 1 = wave only.</summary>
        private float _blend;

        /// <summary>Level of the whole pair (0 while no run is on).</summary>
        private float _master;

        /// <summary>Last phase the server announced; <c>finished</c> silences the music.</summary>
        private string _phase = ArenaProtocol.PHASE_PAUSED;

        /// <summary>A run is staged on this machine (a zombie <c>load_match</c> arrived and we have not
        /// gone back to the lobby).</summary>
        private bool _staged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[ZombieMusic]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ZombieMusic>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _sounds = ZombieSounds.Live;

            _tension = CreateSource();
            _wave = CreateSource();

            if (_sounds != null)
            {
                _tension.clip = _sounds.MusicTension;
                _wave.clip = _sounds.MusicWave;
            }

            // Persistent singleton: subscribe in Awake/OnDestroy, so nothing is missed while the object
            // is inactive.
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnDisconnected -= HandleDisconnected;

            _instance = null;
        }

        private AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;

            // 2D: music has no place in the arena, and through the spatializer it would seem to turn
            // with the player's head.
            source.spatialBlend = 0f;
            source.spatialize = false;
            source.volume = 0f;
            source.priority = SourcePriority;
            return source;
        }

        // -------------------------------------------------------------------- run gating

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            _staged = true;
            _phase = ArenaProtocol.PHASE_PAUSED;
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg != null && !string.IsNullOrEmpty(msg.phase))
            {
                _phase = msg.phase;
            }
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            _staged = false;
        }

        private void HandleDisconnected()
        {
            _staged = false;
        }

        /// <summary>Is a zombie run on? The countdown before the first wave counts as well — the tension
        /// layer belongs exactly there. The finished phase does not.</summary>
        private bool ShouldPlay =>
            _staged &&
            string.Equals(ModeRuntime.ModeId, ZombieKinds.Zombie, StringComparison.Ordinal) &&
            !string.Equals(_phase, ArenaProtocol.PHASE_FINISHED, StringComparison.Ordinal);

        /// <summary>Wave layer while a wave is OUT: <c>next == 0</c> and the first wave has started.
        /// Before wave 1 and in the gap between waves it is the tension layer.</summary>
        private static bool WaveLayerWanted => ZombieWaveState.Wave >= 1 && ZombieWaveState.Next <= 0;

        private void Update()
        {
            if (_sounds == null)
            {
                return;
            }

            // ⚠️ unscaledDeltaTime: the crossfade must not freeze if timeScale is touched.
            float dt = Time.unscaledDeltaTime;
            bool play = ShouldPlay;

            _master = Mathf.MoveTowards(_master, play ? 1f : 0f, dt / Mathf.Max(0.01f, StopFadeSeconds));
            _blend = Mathf.MoveTowards(_blend, WaveLayerWanted ? 1f : 0f,
                dt / Mathf.Max(0.01f, CrossfadeSeconds));

            if (_master <= 0f)
            {
                StopLayers();
                return;
            }

            StartLayers();

            float level = _sounds.MusicVolume * AudioMix.Of(AudioChannel.Music) * _master;

            // Equal power: linear levels would dip audibly in the middle of the crossfade.
            _tension.volume = Mathf.Sqrt(1f - _blend) * level;
            _wave.volume = Mathf.Sqrt(_blend) * level;
        }

        private void StartLayers()
        {
            if (_tension.clip != null && !_tension.isPlaying)
            {
                _tension.Play();
            }

            if (_wave.clip != null && !_wave.isPlaying)
            {
                _wave.Play();
            }
        }

        private void StopLayers()
        {
            _tension.volume = 0f;
            _wave.volume = 0f;

            if (_tension.isPlaying)
            {
                _tension.Stop();
            }

            if (_wave.isPlaying)
            {
                _wave.Stop();
            }
        }
    }
}
