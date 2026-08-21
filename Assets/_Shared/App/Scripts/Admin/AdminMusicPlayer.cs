using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using VortexArena.Core.Audio;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>Transport state of the operator's music player.</summary>
    public enum AdminMusicState
    {
        Stopped = 0,
        Playing = 1,
        Paused = 2
    }

    /// <summary>
    /// Background music read from a FOLDER ON THE ADMIN PC — the venue's own playlist, played on the
    /// operator's speakers. The panel's SES tab drives it (<see cref="AdminPreferencesPanel"/>) and
    /// the stored level/mute live in <see cref="AdminSession"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Purely LOCAL — nothing goes on the wire.</b> The clips are files on the operator's disk;
    /// the headsets have neither the files nor the bandwidth. The music is the room's sound, not the
    /// match's, so it has no protocol message and no shared phase (unlike
    /// <see cref="SceneAmbience"/>, whose two layers ARE synchronised on every headset).
    /// <para><b>Its own <see cref="AudioSource"/>, so it never cuts anything.</b> Announcements
    /// (<see cref="GameAudio"/>), weapon SFX and the map's ambience/music each own their sources and
    /// keep playing untouched. To stay intelligible under a spoken line the music DUCKS while the
    /// announcement channel is busy (<see cref="GameAudio.Announcing"/>) — attenuation, never a
    /// stop: a background track that pauses at every kill is noticed, one that dips is not.</para>
    /// <para><b>Match start</b> (<c>match_state</c> → <see cref="ArenaProtocol.PHASE_PLAYING"/>)
    /// starts a RANDOM track — but only when nothing is playing. Round based modes pass through
    /// <c>playing</c> every round, and restarting the track there would turn background music into a
    /// per-round jingle.</para>
    /// <para>⚠️ NOT a scene component and not in <c>AppSingletons</c>: it is added by
    /// <see cref="AdminSpectator"/> on activation, so it exists in the admin role only. On the VR
    /// build nothing reads a Windows folder.</para>
    /// <para>Clips are decoded from disk at runtime (<see cref="UnityWebRequestMultimedia"/>), so
    /// the playlist is the operator's own folder and needs no reimport, no asset and no rebuild.
    /// A file that cannot be decoded is skipped with a log, never retried in a loop.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class AdminMusicPlayer : MonoBehaviour
    {
        /// <summary>Playlist folder next to the admin exe — the venue install location.</summary>
        private const string InstallFolderName = "Muzik";

        /// <summary>Fallback playlist folder on the operator's desktop, used when the installed one
        /// does not exist (developer machine).</summary>
        private const string DesktopFolderName = "ActionMusics";

        /// <summary>How far the music drops while an announcement is on the channel.</summary>
        private const float DuckScale = 0.25f;

        /// <summary>Duck fade speeds (level units per second). Dropping is fast enough to be under
        /// the first syllable, coming back is slow enough not to be heard as a swell.</summary>
        private const float DuckAttackSpeed = 4f;
        private const float DuckReleaseSpeed = 0.6f;

        /// <summary>Music is a continuous bed and must never be culled → stronger priority than the
        /// default (128), one rung under the ambience loop.</summary>
        private const int SourcePriority = 72;

        public static AdminMusicPlayer Instance { get; private set; }

        /// <summary>Raised on any transport/playlist change (main thread). The preferences panel
        /// repaints from it. ⚠️ Volume ticks do NOT raise it — that would repaint every frame.</summary>
        public static event Action Changed;

        private AudioSource _source;

        /// <summary>Absolute paths of the playable files, sorted by name.</summary>
        private readonly List<string> _tracks = new List<string>();

        /// <summary>Cursor into <see cref="_tracks"/>; meaningless while the list is empty.</summary>
        private int _index;

        private AdminMusicState _state = AdminMusicState.Stopped;

        /// <summary>The clip decoded for the current track — destroyed on every switch. It is a
        /// runtime object, not an asset: leaving it to the GC would keep a whole decoded track in
        /// memory per skipped song.</summary>
        private AudioClip _clip;

        private Coroutine _loadRoutine;
        private bool _loading;

        /// <summary>Consecutive decode failures. A folder full of unreadable files would otherwise
        /// walk the playlist forever, one skip per frame.</summary>
        private int _failures;

        /// <summary>Current output level, moved towards the target by the duck fade.</summary>
        private float _volume;

        /// <summary>Last known phase — <c>match_state</c> repeats, so starting only ON TRANSITION
        /// depends on this (<see cref="GameAudio"/> pattern).</summary>
        private string _lastPhase = "";

        /// <summary>The playlist folder in effect: the one next to the exe when it exists, the
        /// operator's desktop folder otherwise. ⚠️ Resolved on every scan, not cached — the operator
        /// may create the folder while the app is running.</summary>
        public static string Folder
        {
            get
            {
                // Application.dataPath is `<exe dir>/<Product>_Data` in a build and `<project>/Assets`
                // in the editor; its parent is the install folder in both.
                string installed = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", InstallFolderName));
                if (Directory.Exists(installed))
                {
                    return installed;
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return string.IsNullOrEmpty(desktop)
                    ? installed
                    : Path.Combine(desktop, DesktopFolderName);
            }
        }

        /// <summary>Number of playable files found in the folder.</summary>
        public static int TrackCount => Instance != null ? Instance._tracks.Count : 0;

        /// <summary>Current track's file name without extension; <c>""</c> when the list is empty.</summary>
        public static string TrackName
        {
            get
            {
                if (Instance == null || Instance._tracks.Count == 0)
                {
                    return "";
                }

                return Path.GetFileNameWithoutExtension(Instance._tracks[Instance._index]);
            }
        }

        /// <summary>Cursor as a 1-based number for display; <c>0</c> when the list is empty.</summary>
        public static int TrackNumber =>
            Instance != null && Instance._tracks.Count > 0 ? Instance._index + 1 : 0;

        public static AdminMusicState State => Instance != null ? Instance._state : AdminMusicState.Stopped;

        /// <summary>Is a track being decoded right now (the row shows it as loading).</summary>
        public static bool Loading => Instance != null && Instance._loading;

        // ------------------------------------------------------------- transport (panel API)

        /// <summary>Play ↔ pause. From stopped it starts the track under the cursor.</summary>
        public static void TogglePlayPause()
        {
            Instance?.TogglePlayPauseInternal();
        }

        /// <summary>Stops and releases the decoded clip; the cursor stays where it was.</summary>
        public static void StopMusic()
        {
            Instance?.StopInternal();
        }

        public static void NextTrack()
        {
            Instance?.Step(1);
        }

        public static void PrevTrack()
        {
            Instance?.Step(-1);
        }

        /// <summary>Re-reads the folder. Called on panel open: the operator may drop a file in while
        /// the app is running, and a stale list would hide it until restart.</summary>
        public static void Rescan()
        {
            Instance?.RescanInternal();
        }

        // ------------------------------------------------------------- lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false; // the playlist advances instead: looping would trap one track
            _source.spatialBlend = 0f; // 2D — the operator's speakers, not a point in the arena
            _source.spatialize = false;
            _source.priority = SourcePriority;
            _source.volume = 0f;

            // Persistent singleton: subscribe in Awake/OnDestroy so events are not missed if the
            // object is deactivated (GameAudio pattern).
            NetEvents.OnMatchState += HandleMatchState;

            RescanInternal();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnMatchState -= HandleMatchState;
            ReleaseClip();
            Instance = null;
        }

        private void Update()
        {
            TickVolume();

            if (_state != AdminMusicState.Playing || _loading || _source.clip == null || _source.isPlaying)
            {
                return;
            }

            // The track ended on its own → the next one. The playlist wraps and never stops by
            // itself: an operator who wants silence presses DURDUR.
            Step(1);
        }

        /// <summary>Writes the output level, ducking under an announcement.</summary>
        /// <remarks>⚠️ Read from <see cref="AdminSession"/> EVERY frame instead of an "apply" call
        /// from the setter: the duck already needs a per-frame target, and a second write path would
        /// leave "who set the volume" with two answers.
        /// <para>Unscaled time: the admin never scales time, but a paused match must not freeze the
        /// fade half way.</para></remarks>
        private void TickVolume()
        {
            float target = AdminSession.EffectiveMusicPlayerLevel;
            if (GameAudio.Announcing)
            {
                target *= DuckScale;
            }

            float speed = target < _volume ? DuckAttackSpeed : DuckReleaseSpeed;
            _volume = Mathf.MoveTowards(_volume, target, speed * Time.unscaledDeltaTime);
            _source.volume = _volume;
        }

        // ------------------------------------------------------------- playlist

        private void RescanInternal()
        {
            _tracks.Clear();
            _failures = 0;

            string folder = Folder;
            try
            {
                if (Directory.Exists(folder))
                {
                    foreach (string path in Directory.GetFiles(folder))
                    {
                        if (TypeOf(path) != AudioType.UNKNOWN)
                        {
                            _tracks.Add(path);
                        }
                    }

                    // Name order, so PREV/NEXT match what the operator sees in Explorer.
                    _tracks.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception e)
            {
                // A missing/locked folder is not an error worth stopping for — the row says "boş".
                Debug.LogWarning($"[AdminMusicPlayer] Müzik klasörü okunamadı ({folder}): {e.Message}");
            }

            if (_index >= _tracks.Count)
            {
                _index = 0;
            }

            Raise();
        }

        /// <summary>Decoder for the extension; <see cref="AudioType.UNKNOWN"/> = not a playlist
        /// file. ⚠️ The filter is the extension, not a content sniff: everything else in the folder
        /// (cover art, playlists, notes) must be ignored silently.</summary>
        private static AudioType TypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".wav":
                    return AudioType.WAV;
                case ".mp3":
                    return AudioType.MPEG;
                default:
                    return AudioType.UNKNOWN;
            }
        }

        // ------------------------------------------------------------- transport

        private void TogglePlayPauseInternal()
        {
            if (_tracks.Count == 0)
            {
                RescanInternal(); // the folder may have been filled since startup
                if (_tracks.Count == 0)
                {
                    return;
                }
            }

            switch (_state)
            {
                case AdminMusicState.Playing:
                    _source.Pause();
                    _state = AdminMusicState.Paused;
                    Raise();
                    break;

                case AdminMusicState.Paused when _source.clip != null:
                    _source.UnPause();
                    _state = AdminMusicState.Playing;
                    Raise();
                    break;

                default:
                    PlayIndex(_index);
                    break;
            }
        }

        private void StopInternal()
        {
            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }

            _loading = false;
            _source.Stop();
            _source.clip = null;
            ReleaseClip();
            _state = AdminMusicState.Stopped;
            Raise();
        }

        /// <summary>Moves the cursor by <paramref name="direction"/> and plays that track. Wraps at
        /// both ends: the playlist is a ring, so NEXT on the last track is never a dead button.</summary>
        private void Step(int direction)
        {
            if (_tracks.Count == 0 || direction == 0)
            {
                return;
            }

            PlayIndex(_index + direction);
        }

        /// <summary>Starts a random track — the match-start entry point.</summary>
        /// <remarks>On a list of two or more the CURRENT track is excluded: "random" that replays
        /// the song just heard reads as a broken button.</remarks>
        private void PlayRandom()
        {
            int count = _tracks.Count;
            if (count == 0)
            {
                RescanInternal();
                count = _tracks.Count;
                if (count == 0)
                {
                    return;
                }
            }

            int pick = UnityEngine.Random.Range(0, count);
            if (count > 1 && pick == _index)
            {
                pick = (pick + 1) % count;
            }

            PlayIndex(pick);
        }

        private void PlayIndex(int index)
        {
            if (_tracks.Count == 0)
            {
                return;
            }

            _index = Wrap(index);

            if (_loadRoutine != null)
            {
                // ⚠️ The flag is cleared HERE too: a coroutine stopped mid-decode never reaches its
                // own cleanup, and a stuck "yükleniyor" would freeze the row and the auto-advance.
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
                _loading = false;
            }

            _loadRoutine = StartCoroutine(LoadAndPlay(_tracks[_index]));
        }

        private int Wrap(int index)
        {
            int count = _tracks.Count;
            return ((index % count) + count) % count;
        }

        private IEnumerator LoadAndPlay(string path)
        {
            _loading = true;
            _state = AdminMusicState.Playing; // the row reads "çalıyor" while the file decodes
            Raise();

            // A local path becomes a file:// url; Uri escapes the spaces and Turkish characters a
            // hand-built string would break on.
            string url = new Uri(path).AbsoluteUri;

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, TypeOf(path)))
            {
                if (request.downloadHandler is DownloadHandlerAudioClip handler)
                {
                    // Streamed: a full decode would hold the whole track as PCM.
                    handler.streamAudio = true;
                }

                yield return request.SendWebRequest();

                AudioClip clip = request.result == UnityWebRequest.Result.Success
                    ? DownloadHandlerAudioClip.GetContent(request)
                    : null;

                _loading = false;
                _loadRoutine = null;

                if (clip == null)
                {
                    Debug.LogWarning(
                        $"[AdminMusicPlayer] '{Path.GetFileName(path)}' çalınamadı: {request.error}");

                    _failures++;

                    // One broken file must not stall the playlist — but a folder of broken files
                    // must not loop through it forever either.
                    if (_failures < _tracks.Count)
                    {
                        Step(1);
                    }
                    else
                    {
                        StopInternal();
                    }

                    yield break;
                }

                _failures = 0;
                clip.name = Path.GetFileNameWithoutExtension(path);

                ReleaseClip();
                _clip = clip;
                _source.clip = clip;
                _source.time = 0f;
                _source.Play();
                Raise();
            }
        }

        private void ReleaseClip()
        {
            if (_clip == null)
            {
                return;
            }

            Destroy(_clip);
            _clip = null;
        }

        // ------------------------------------------------------------- match

        /// <summary>Starts a random track when the match goes live.</summary>
        /// <remarks>⚠️ Only when nothing is playing: round based modes re-enter <c>playing</c> every
        /// round, and restarting there would make the bed a per-round jingle. A paused or stopped
        /// player DOES start — the operator asked for background music, and a match starting in
        /// silence is the thing this exists to prevent.</remarks>
        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string phase = msg.phase ?? "";
            if (string.Equals(phase, _lastPhase, StringComparison.Ordinal))
            {
                return;
            }

            _lastPhase = phase;

            if (!string.Equals(phase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal))
            {
                return;
            }

            if (_state == AdminMusicState.Playing || _loading)
            {
                return;
            }

            PlayRandom();
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
