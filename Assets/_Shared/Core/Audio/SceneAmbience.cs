using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Audio
{
    /// <summary>The map's ambience (ambience + game music): starts as soon as the scene loads, loops
    /// and never stops until the map changes — match start, match end and returning to the lobby do
    /// not touch it. The clip comes from the scene's
    /// <see cref="MapDefinition.AmbienceClip"/>.</summary>
    /// <remarks>
    /// Self-bootstrapping persistent singleton; NO component is placed in the scene
    /// (<c>WeaponGranter</c> pattern): a manual setup step per arena would silently leave a scene
    /// music-less when forgotten. Adding ambience to a new arena is just dragging a clip into its
    /// <see cref="MapDefinition"/>.
    /// <para>
    /// <b>Shared phase — the music is at the same position on every headset.</b> The server sends how
    /// long the scene has been staged (<c>sceneElapsed</c>, Docs/ArenaNet-Protokol.md §5.3) with every
    /// scene message (<c>welcome.match</c> · <c>load_match</c> · <c>return_to_lobby</c>). The client
    /// turns it into a local time epoch and opens the clip at <c>elapsed mod clipLength</c>: everyone
    /// hears the same spot, a late joiner joins mid-clip, and a scene older than the clip has wrapped
    /// by itself. With no server (editor sandbox) there is no epoch and the clip starts at 0.
    /// </para>
    /// <para>
    /// The epoch is anchored to WHEN THE MESSAGE ARRIVED, not to scene load: load time varies per
    /// headset, so anchoring to it would leave slow loaders behind. The remaining clock drift (audio
    /// card ↔ system clock) is checked every <see cref="DriftCheckSeconds"/> and corrected only past
    /// <see cref="MaxDriftSeconds"/> — correcting on every check would produce an audible jump.
    /// </para>
    /// <para>Scene changes crossfade. When two scenes share the SAME clip (e.g. two venues' lobbies)
    /// the sound does not restart and keeps playing.</para>
    /// <para>The sound is 2D (<c>spatialBlend = 0</c>): ambience has no place in the arena, and
    /// through the spatializer the source would seem to rotate as the player turns their head.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class SceneAmbience : MonoBehaviour
    {
        /// <summary>Crossfade duration on scene change (s).</summary>
        private const float CrossfadeSeconds = 1.5f;

        /// <summary>Ambience plays continuously and must never be culled → stronger priority than the
        /// default (128). Still not 0: shot sounds come before it.</summary>
        private const int SourcePriority = 64;

        /// <summary>Interval of the shared-phase drift check (s).</summary>
        private const float DriftCheckSeconds = 15f;

        /// <summary>Drift beyond this is corrected. Below it nobody can hear the difference, while
        /// correcting on every check would produce an audible jump.</summary>
        private const float MaxDriftSeconds = 0.35f;

        public static SceneAmbience Instance { get; private set; }

        private AudioSource _active;
        private AudioSource _fading;
        private float _clipVolume;
        private float _masterVolume = 1f;

        private GameCatalog _catalog;
        private bool _catalogLoaded;

        /// <summary>Scene the epoch belongs to — loading another scene invalidates it.</summary>
        private string _epochScene = "";

        /// <summary>LOCAL equivalent of the moment the scene was staged: <c>realtime - sceneElapsed</c>.</summary>
        private float _epochRealtime;
        private bool _hasEpoch;
        private float _nextDriftCheck;

        /// <summary>Shared multiplier for all ambience (0..1) — the ducking/mute gate. It changes
        /// neither clip selection nor the shared phase; at 0 the clip keeps playing and comes back at
        /// the same position as the other headsets.</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Clamp01(value);
        }

        /// <summary>The clip currently playing; null when silent.</summary>
        public AudioClip CurrentClip => _active != null ? _active.clip : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[SceneAmbience]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SceneAmbience>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _active = CreateSource();
            _fading = CreateSource();

            // Persistent singleton: subscribe in Awake/OnDestroy so events are not missed if the
            // object is deactivated (PlayerCombatState pattern).
            SceneManager.sceneLoaded += HandleSceneLoaded;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;

            ApplyScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;

            Instance = null;
        }

        /// <summary>Audio device or configuration changed (speaker plugged/unplugged, operator picked
        /// another output). Unity rebuilds the audio engine and STOPS every playing
        /// <c>AudioSource</c>.
        /// <para>⚠️ Ambience does not come back on its own and the silence goes unnoticed: the drift
        /// check returns early on "not playing" (<see cref="CorrectDrift"/>) and a new clip is only
        /// picked on map change — the sound would stay muted for the whole match. Since the clip is
        /// still assigned, playback is resumed here and re-seated on the shared phase (like a late
        /// joiner: it skips in, it does not restart).</para></summary>
        private void HandleAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (_active == null || _active.clip == null)
            {
                return;
            }

            if (!_active.isPlaying)
            {
                _active.Play();
            }

            SeekToEpoch(_active);
            _nextDriftCheck = Time.realtimeSinceStartup + DriftCheckSeconds;
        }

        private void Update()
        {
            // ⚠️ unscaledDeltaTime so the crossfade does not freeze if timeScale is touched (death
            // screen, pause).
            float step = Time.unscaledDeltaTime / CrossfadeSeconds;
            float target = _active != null && _active.clip != null ? _clipVolume * _masterVolume : 0f;

            if (_active != null)
            {
                _active.volume = Mathf.MoveTowards(_active.volume, target, step);
            }

            if (_fading != null)
            {
                _fading.volume = Mathf.MoveTowards(_fading.volume, 0f, step);
                if (_fading.volume <= 0f && _fading.isPlaying)
                {
                    _fading.Stop();
                    _fading.clip = null;
                }
            }

            CorrectDrift();
        }

        /// <summary>Changes the ambience by hand (null clip = silence). Normally not needed: the clip
        /// is picked from the map definition on scene load.</summary>
        public void Play(AudioClip clip, float volume)
        {
            _clipVolume = Mathf.Clamp01(volume);

            if (_active != null && _active.clip == clip)
            {
                // ⚠️ Same clip → do not restart and do not touch its phase. As long as the map does
                // not change the music is uninterrupted; match start/end come through here.
                if (clip != null && !_active.isPlaying)
                {
                    _active.Play();
                    SeekToEpoch(_active);
                }

                return;
            }

            // Swap roles: the new clip rises from zero on the free source, the old one fades in Update.
            AudioSource previous = _active;
            _active = _fading;
            _fading = previous;

            _active.volume = 0f;
            _active.clip = clip;

            if (clip != null)
            {
                _active.Play();
                SeekToEpoch(_active);
                _nextDriftCheck = Time.realtimeSinceStartup + DriftCheckSeconds;
            }
            else
            {
                _active.Stop();
            }
        }

        private AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.spatialize = false;
            source.volume = 0f;
            source.priority = SourcePriority;
            return source;
        }

        // --------------------------------------------------------------- shared phase

        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match != null)
            {
                SetEpoch(msg.match.sceneName, msg.match.sceneElapsed);
            }
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg != null)
            {
                SetEpoch(msg.sceneName, msg.sceneElapsed);
            }
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            if (msg != null)
            {
                SetEpoch(msg.sceneName, msg.sceneElapsed);
            }
        }

        /// <summary>Turns the server's "this scene has been open for N seconds" into a local time
        /// epoch. Anchored to WHEN THE MESSAGE ARRIVED — scene load time varies per headset, so it
        /// cannot be anchored to scene load.</summary>
        private void SetEpoch(string sceneName, float elapsedSeconds)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            _epochScene = sceneName;
            _epochRealtime = Time.realtimeSinceStartup - Mathf.Max(0f, elapsedSeconds);
            _hasEpoch = true;

            // If the announced scene is already open (new match on the same map, return to lobby) the
            // clip does not change — only the reference is refreshed and alignment is verified on the
            // next check.
            if (string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal))
            {
                _nextDriftCheck = 0f;
            }
        }

        /// <summary>The clip's position in the shared phase: the scene's open time modulo the clip
        /// length. Returns 0 with no epoch (server-less session).</summary>
        private float EpochOffset(AudioClip clip)
        {
            if (!_hasEpoch || clip == null)
            {
                return 0f;
            }

            float length = clip.length;
            if (length <= 0f)
            {
                return 0f;
            }

            float elapsed = Time.realtimeSinceStartup - _epochRealtime;
            if (elapsed <= 0f)
            {
                return 0f;
            }

            // ⚠️ A value too close to the end wraps immediately together with Play() → leave a margin.
            return Mathf.Clamp(Mathf.Repeat(elapsed, length), 0f, Mathf.Max(0f, length - 0.05f));
        }

        private void SeekToEpoch(AudioSource source)
        {
            if (source != null && source.clip != null)
            {
                source.time = EpochOffset(source.clip);
            }
        }

        /// <summary>Checks the drift between the audio clock and the shared phase infrequently and
        /// corrects only past the threshold. Two headsets in the same room play through open
        /// speakers, so a few hundred ms of drift is heard as an echo; smaller gaps are
        /// inaudible.</summary>
        private void CorrectDrift()
        {
            if (!_hasEpoch || _active == null || _active.clip == null || !_active.isPlaying ||
                Time.realtimeSinceStartup < _nextDriftCheck)
            {
                return;
            }

            _nextDriftCheck = Time.realtimeSinceStartup + DriftCheckSeconds;

            float length = _active.clip.length;
            if (length <= 0f)
            {
                return;
            }

            float expected = EpochOffset(_active.clip);
            float diff = Mathf.Abs(expected - _active.time);

            // Ring distance: the end of the clip is adjacent to its start.
            if (diff > length * 0.5f)
            {
                diff = length - diff;
            }

            if (diff > MaxDriftSeconds)
            {
                _active.time = expected;
            }
        }

        // ---------------------------------------------------------------------- scene

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // An additive scene does not change the map → it must not touch the ambience.
            if (mode == LoadSceneMode.Additive)
            {
                return;
            }

            ApplyScene(scene.name);
        }

        private void ApplyScene(string sceneName)
        {
            // If the epoch belongs to another scene, the shared phase for this one is unknown
            // (server-less session or the message has not arrived) → the clip starts at 0.
            if (!string.Equals(_epochScene, sceneName, StringComparison.Ordinal))
            {
                _hasEpoch = false;
            }

            MapDefinition map = FindMap(sceneName);
            Play(map != null ? map.AmbienceClip : null, map != null ? map.AmbienceVolume : 0f);
        }

        /// <summary>Resolves the scene's map definition from the catalog. The catalog is loaded from
        /// <c>Resources</c> once; returns null (silence) for scenes without a map definition, such as
        /// Boot.</summary>
        private MapDefinition FindMap(string sceneName)
        {
            if (!_catalogLoaded)
            {
                _catalogLoaded = true;
                _catalog = Resources.Load<GameCatalog>("GameCatalog");

                if (_catalog == null)
                {
                    Debug.LogWarning(
                        "[SceneAmbience] GameCatalog bulunamadı (Resources/GameCatalog) — ortam sesi çalmayacak.");
                }
            }

            return _catalog != null ? _catalog.FindMap(sceneName) : null;
        }
    }
}
