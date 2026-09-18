using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Arena
{
    /// <summary>Local floor + the roster mirror of every player's floor (multi-floor arenas).</summary>
    /// <remarks>
    /// <b>The floor is a VIRTUAL offset:</b> changing floors lifts the rig root by the level
    /// difference (<see cref="ArenaCalibrator.SetFloorLift"/>) — the player does not move physically,
    /// so arena space stays world space and the wire is unchanged.
    /// <para>The server keeps <c>floor</c> only as a LEDGER (<c>set_floor</c>, echoed in
    /// <c>lobby_state</c>): the hop is applied locally at once, then confirmed. If the echo never
    /// arrives the local value is reverted to the server's — a client-invented floor would draw this
    /// player metres off for everyone else.</para>
    /// <para>⚠️ The server resets the ledger on <c>hello</c> / <c>load_match</c> / lobby return, so a
    /// roster row differing from <see cref="Local"/> while nothing is pending is an ORDER, not a
    /// glitch (<see cref="HandleLobbyState"/>).</para>
    /// <para>Not placed in the scene: self-bootstrapping persistent singleton
    /// (<see cref="CalibrationState"/> pattern), so no arena gains a manual setup step.</para>
    /// </remarks>
    public class FloorState : MonoBehaviour
    {
        /// <summary><see cref="ScreenFade"/> source id.</summary>
        private const string FadeSourceId = "floor";

        public static FloorState Instance { get; private set; }

        /// <summary>Local player's floor index.</summary>
        public static int Local { get; private set; }

        /// <summary>False on the admin observer: it stands on no floor, so "my floor" comparisons
        /// (silhouettes, label suffixes) mean nothing there. Set by the App layer at startup —
        /// Core cannot see the role.</summary>
        public static bool ViewerHasFloor { get; set; } = true;

        /// <summary>Vertical lift currently applied to the rig for <see cref="Local"/> (m).</summary>
        public static float LiftMeters => ArenaFloors.HeightOf(Local);

        /// <summary>Raised when <see cref="Local"/> changes (main thread).</summary>
        public static event Action Changed;

        // Roster mirror: rebuilt from every lobby_state. Missing row = floor 0.
        private static readonly Dictionary<int, int> Floors = new Dictionary<int, int>();

        // Single instance so no DTO is allocated per hop (CalibrationState pattern).
        private static readonly SetFloorMsg FloorMsg = new SetFloorMsg();

        private static int _serverFloor;
        private static bool _pending;
        private static float _confirmUntil;

        // Running fade: the target is a field so a second request can RETARGET it before the black
        // moment; past black only a queued request can still be honoured.
        private static int _fadeTarget;
        private static bool _fadeSend;
        private static bool _fadeAtBlack;

        private static bool _hasQueued;
        private static int _queuedFloor;
        private static string _queuedReason;
        private static float _queuedOut;
        private static float _queuedHold;
        private static float _queuedIn;
        private static bool _queuedSend;

        private Coroutine _fadeRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[FloorState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FloorState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy rather than OnEnable/OnDisable so
            // events are not missed if the object is deactivated.
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            PlayerCombatState.LocalAliveChanged += HandleLocalAliveChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            PlayerCombatState.LocalAliveChanged -= HandleLocalAliveChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            Instance = null;
        }

        /// <summary>Roster floor of any player (self included); 0 when unknown.</summary>
        public static int PlayerFloor(int playerId)
        {
            return Floors.TryGetValue(playerId, out int floor) ? floor : 0;
        }

        /// <summary>Moves the local player to a floor immediately; <c>false</c> = rejected.</summary>
        /// <remarks><paramref name="reason"/> only names the call site in the log ("portal" | "ölüm").</remarks>
        public static bool Request(int floor, string reason)
        {
            if (!IsValidFloor(floor, reason))
            {
                return false;
            }

            if (floor == Local)
            {
                return true;
            }

            int previous = Local;
            ApplyLocal(floor);
            Debug.Log($"[Kat] {reason}: kat {previous} → {floor} (ofset {LiftMeters:F2} m)");
            SendCurrent();
            return true;
        }

        /// <summary>Floor change behind a black screen: fade out, move at full black, fade in;
        /// <c>false</c> = nothing will move.</summary>
        /// <remarks>A second call while one runs is NOT ignored: before the black moment it RETARGETS
        /// the running fade, after it the request is QUEUED and run when the fade ends — the fade is
        /// never restarted, which would show the player the arena from the wrong floor mid-blink.</remarks>
        public static bool MoveWithFade(
            int floor, string reason, float fadeOutSeconds, float holdSeconds, float fadeInSeconds)
        {
            return MoveWithFadeInternal(
                floor, reason, fadeOutSeconds, holdSeconds, fadeInSeconds, send: true);
        }

        /// <summary><paramref name="send"/> false = apply locally only, for floors the SERVER already
        /// wrote (echoing them back would ping-pong with its own reset).</summary>
        private static bool MoveWithFadeInternal(
            int floor, string reason, float outS, float holdS, float inS, bool send)
        {
            if (!IsValidFloor(floor, reason))
            {
                return false;
            }

            FloorState instance = Instance;
            if (instance == null)
            {
                // No singleton (edit-mode/teardown): the move still matters, just without the fade.
                if (send)
                {
                    Request(floor, reason);
                }
                else
                {
                    ApplyLocal(floor);
                }

                return true;
            }

            if (instance._fadeRoutine == null)
            {
                if (floor == Local)
                {
                    return false;
                }

                _fadeTarget = floor;
                _fadeSend = send;
                _fadeAtBlack = false;
                instance._fadeRoutine = instance.StartCoroutine(
                    instance.FadeMove(reason, outS, holdS, inS));
                return true;
            }

            if (!_fadeAtBlack)
            {
                _fadeTarget = floor;
                _fadeSend = send;
                Debug.Log($"[Kat] {reason}: sürmekte olan geçişin hedefi kat {floor} olarak güncellendi.");
                return true;
            }

            // Past black: only one request is kept — a newer one replaces the older.
            _hasQueued = true;
            _queuedFloor = floor;
            _queuedReason = reason;
            _queuedOut = outS;
            _queuedHold = holdS;
            _queuedIn = inS;
            _queuedSend = send;
            Debug.Log($"[Kat] {reason}: kat {floor} isteği kuyruğa alındı.");
            return true;
        }

        /// <summary>Range gate shared by <see cref="Request"/> and the fade path.</summary>
        private static bool IsValidFloor(int floor, string reason)
        {
            if (floor >= 0 && floor < ArenaFloors.Count && floor <= ArenaProtocol.MAX_FLOOR_INDEX)
            {
                return true;
            }

            Debug.LogWarning($"[Kat] {reason}: kat {floor} bu arenada yok (kat sayısı " +
                             $"{ArenaFloors.Count}) — istek yok sayıldı.");
            return false;
        }

        private void Update()
        {
            if (!_pending || Time.unscaledTime < _confirmUntil)
            {
                return;
            }

            _pending = false;

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected || _serverFloor == Local)
            {
                return; // link dropped (nothing to compare) or the echo agreed after all
            }

            Debug.LogWarning($"[Kat] sunucu kat {Local} isteğini yankılamadı — geri alındı.");
            MoveWithFadeInternal(_serverFloor, "geri alma", 0.25f, 0.1f, 0.4f, send: false);
        }

        /// <summary>Writes the floor locally: rig lift + event. Sends NOTHING — the callers decide.</summary>
        private static void ApplyLocal(int floor)
        {
            // The server keeps only a ledger and knows no geometry: a stale roster row must not leave
            // Local outside the arena.
            if (floor < 0 || floor >= ArenaFloors.Count)
            {
                Debug.LogWarning($"[Kat] kat {floor} bu arenada yok (kat sayısı {ArenaFloors.Count}) — " +
                                 "0. kata düşürüldü.");
                floor = 0;
            }

            Local = floor;
            ArenaCalibrator.SetFloorLift(ArenaFloors.HeightOf(floor));
            Changed?.Invoke();
        }

        private static void SendCurrent()
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // server-less session: nobody keeps the ledger
            }

            FloorMsg.floor = Local;
            client.Send(FloorMsg);
            _pending = true;
            _confirmUntil = Time.unscaledTime + ArenaProtocol.FLOOR_CONFIRM_SECONDS;
        }

        private IEnumerator FadeMove(
            string reason, float fadeOutSeconds, float holdSeconds, float fadeInSeconds)
        {
            // Unscaled throughout: the blackout is presentation and must finish even if the match is
            // paused with timeScale.
            float elapsed = 0f;
            while (elapsed < fadeOutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                ScreenFade.Report(FadeSourceId, Mathf.Clamp01(elapsed / fadeOutSeconds), Color.black);
                yield return null;
            }

            ScreenFade.Report(FadeSourceId, 1f, Color.black);

            // Target read HERE, not from a parameter: a request arriving during the fade-out
            // retargets this same blink instead of starting a second one.
            _fadeAtBlack = true;
            if (_fadeSend)
            {
                Request(_fadeTarget, reason);
            }
            else
            {
                ApplyLocal(_fadeTarget);
            }

            elapsed = 0f;
            while (elapsed < holdSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                ScreenFade.Report(FadeSourceId, 1f, Color.black);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < fadeInSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                ScreenFade.Report(FadeSourceId, 1f - Mathf.Clamp01(elapsed / fadeInSeconds), Color.black);
                yield return null;
            }

            // Explicit zero: a source that simply stops reporting only drops out after the arbiter's
            // timeout, which would hold the screen dark a quarter second longer.
            ScreenFade.Report(FadeSourceId, 0f, Color.black);
            _fadeRoutine = null;

            if (_hasQueued)
            {
                _hasQueued = false;
                MoveWithFadeInternal(
                    _queuedFloor, _queuedReason, _queuedOut, _queuedHold, _queuedIn, _queuedSend);
            }
        }

        private void HandleConnected(WelcomeMsg msg)
        {
            // The server resets the ledger on hello — if we are standing on an upper floor, say so
            // again (CalibrationState.HandleConnected pattern).
            _pending = false;
            _serverFloor = 0;
            if (Local != 0)
            {
                SendCurrent();
            }
        }

        /// <summary>Our own roster row is the truth for the floor ledger.</summary>
        /// <remarks>Three cases: the echo we are waiting for (confirm), a value the server wrote on
        /// its own while nothing is pending (reset → apply, do NOT send back, else the reset and the
        /// client would ping-pong), and agreement (nothing to do).</remarks>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null)
            {
                return;
            }

            Floors.Clear();
            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info != null)
                {
                    Floors[info.playerId] = info.floor;
                }
            }

            int selfId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
            if (selfId == 0 || !Floors.TryGetValue(selfId, out int serverFloor))
            {
                return;
            }

            _serverFloor = serverFloor;

            if (_pending)
            {
                if (_serverFloor == Local)
                {
                    _pending = false;
                }

                return;
            }

            if (_serverFloor == Local)
            {
                return;
            }

            Debug.Log($"[Kat] sunucu katı {_serverFloor} yazdı — rig o kata alındı.");
            MoveWithFadeInternal(_serverFloor, "sunucu", 0.25f, 0.1f, 0.4f, send: false);
        }

        /// <summary>On death the player is taken to their base's floor: reviving means walking into
        /// the base zone, and a corpse left on an upper floor could never reach it.</summary>
        private void HandleLocalAliveChanged(bool alive)
        {
            if (alive)
            {
                return;
            }

            int target = PlayerCombatState.Instance != null &&
                         PlayerCombatState.Instance.TryGetOpenBaseFloor(out int baseFloor)
                ? baseFloor
                : 0;

            // Never UP: floor 0 always carries a base (project rule), and lifting a corpse onto a
            // plate that may not be under it would hang it in the air.
            target = Mathf.Min(target, Local);

            if (target == Local)
            {
                return;
            }

            MoveWithFade(target, "ölüm", 0.3f, 0.2f, 0.5f);
        }

        /// <summary>A new arena starts on floor 0; the server resets its ledger on <c>load_match</c>,
        /// so nothing is sent here — the rig lift is simply dropped.</summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
            {
                return;
            }

            // A fade crossing a scene load would re-apply the OLD arena's floor after this reset.
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            _hasQueued = false;
            ScreenFade.Report(FadeSourceId, 0f, Color.black);

            _pending = false;
            Floors.Clear();
            _serverFloor = 0;

            if (Local != 0)
            {
                ApplyLocal(0);
                return;
            }

            ArenaCalibrator.SetFloorLift(0f);
        }
    }
}
