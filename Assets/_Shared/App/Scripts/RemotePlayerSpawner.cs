using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Manages remote player avatars (placed in Lobby/arena scenes): spawns/despawns the
    /// RemoteAvatar prefab on RemotePlayerRegistry join/leave and feeds name/team from lobby_state.
    /// RemoteAvatar reads the poses from the registry itself.
    /// </summary>
    public class RemotePlayerSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private RemoteAvatar avatarPrefab;

        private readonly Dictionary<int, RemoteAvatar> _avatars = new Dictionary<int, RemoteAvatar>();
        private readonly List<int> _idScratch = new List<int>();

        private LobbyStateMsg _lastLobbyState;

        private bool _subscribed;
        private bool _prefabWarned;

        private void Start()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                // ArenaClient's bootstrap runs before the scene — this does not happen in practice.
                Debug.LogWarning("[RemotePlayerSpawner] RemotePlayerRegistry yok; spawner devre dışı.");
                enabled = false;
                return;
            }

            registry.OnRemoteJoined += Spawn;
            registry.OnRemoteLeft += Despawn;
            NetEvents.OnLobbyState += HandleLobbyState;
            // The body variant depends on the mode shape and on the scene's bodySeed, both of which can
            // change under LIVE avatars (staging a new scene does not respawn them).
            ModeRuntime.Changed += RefreshBodyVariants;
            ModeSelection.Changed += RefreshBodyVariants;
            _subscribed = true;

            // Late scene load: spawn the already active remote players retroactively.
            registry.GetActivePlayerIds(_idScratch);
            for (int i = 0; i < _idScratch.Count; i++)
            {
                Spawn(_idScratch[i]);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry != null)
                {
                    registry.OnRemoteJoined -= Spawn;
                    registry.OnRemoteLeft -= Despawn;
                }

                NetEvents.OnLobbyState -= HandleLobbyState;
                ModeRuntime.Changed -= RefreshBodyVariants;
                ModeSelection.Changed -= RefreshBodyVariants;
                _subscribed = false;
            }

            foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
            {
                if (kv.Value != null)
                {
                    Destroy(kv.Value.gameObject);
                }
            }

            _avatars.Clear();
        }

        // ------------------------------------------------------------ event handlers

        private void Spawn(int playerId)
        {
            if (_avatars.ContainsKey(playerId))
            {
                return;
            }

            // Double safety: never create an avatar for ourselves, even if our id shows up in a snapshot.
            if (ArenaClient.Instance != null && ArenaClient.Instance.PlayerId == playerId)
            {
                return;
            }

            if (avatarPrefab == null)
            {
                if (!_prefabWarned)
                {
                    _prefabWarned = true;
                    Debug.LogWarning("[RemotePlayerSpawner] avatarPrefab atanmadı; uzak oyuncular görselleştirilemiyor.");
                }

                return;
            }

            RemoteAvatar avatar = Instantiate(avatarPrefab);
            avatar.name = $"RemoteAvatar_{playerId}";
            avatar.Initialize(playerId);
            ApplyLobbyInfo(avatar);
            _avatars.Add(playerId, avatar);
        }

        private void Despawn(int playerId)
        {
            if (!_avatars.TryGetValue(playerId, out RemoteAvatar avatar))
            {
                return;
            }

            _avatars.Remove(playerId);
            if (avatar != null)
            {
                Destroy(avatar.gameObject);
            }
        }

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            _lastLobbyState = msg;

            foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
            {
                ApplyLobbyInfo(kv.Value);
            }
        }

        // ------------------------------------------------------------------ helper

        /// <summary>Applies name/team/calibration from the last lobby_state, or the defaults.</summary>
        private void ApplyLobbyInfo(RemoteAvatar avatar)
        {
            if (avatar == null)
            {
                return;
            }

            string displayName = $"Oyuncu {avatar.PlayerId}";
            // Before the roster arrives the number is NOT invented (0 = not printed): it is the
            // distinguishing field, so a wrong one confuses players with repeated names.
            int number = 0;
            string team = "";
            // A player missing from the roster counts as CALIBRATED (§10.6) — alarming on an unknown
            // state would just highlight every newcomer as noise.
            bool calibrated = true;
            // §10.8: 0 = not measured → RemoteAvatar applies 1. No invented scale before the roster.
            float bodyScale = 0f;

            if (_lastLobbyState != null && _lastLobbyState.players != null)
            {
                for (int i = 0; i < _lastLobbyState.players.Length; i++)
                {
                    PlayerInfo info = _lastLobbyState.players[i];
                    if (info == null || info.playerId != avatar.PlayerId)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(info.name))
                    {
                        displayName = info.name;
                    }

                    number = info.number;
                    team = info.team ?? "";
                    calibrated = info.calibrated;
                    bodyScale = info.bodyScale;
                    break;
                }
            }

            // BEFORE SetInfo: SetInfo runs SelectBody, so setting the variant first rebuilds the body once.
            int variant = BodyVariant(avatar.PlayerId);
            if (variant >= 0)
            {
                avatar.SetBodyVariant(variant);
            }

            avatar.SetInfo(displayName, number, team);
            avatar.SetCalibrated(calibrated);
            avatar.SetBodyScale(bodyScale);
        }

        /// <summary>Re-applies the body variants of the live avatars (mode shape or scene seed changed).
        /// Only the variant: name/team/calibration did not move.</summary>
        private void RefreshBodyVariants()
        {
            foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
            {
                RemoteAvatar avatar = kv.Value;
                if (avatar == null)
                {
                    continue;
                }

                int variant = BodyVariant(avatar.PlayerId);
                if (variant >= 0)
                {
                    avatar.SetBodyVariant(variant);
                }
            }
        }

        /// <summary>Teamless body variant (0/1), or <c>-1</c> = leave it as it is (unknown).
        /// <para>Two sources, and the gate is whether the mode brings a body of its own: a mode with
        /// bodies splits the roster EVENLY (<c>altBodyPrefab</c>, equal numbers of each model), while a
        /// teamless mode with no bodies draws from the scene's <c>bodySeed</c> — there the two bodies
        /// are the DEFAULT ones, nobody authored a pairing, and a fresh draw per staging is what keeps
        /// the arena from looking the same every round.</para></summary>
        private int BodyVariant(int playerId)
        {
            ModeDefinition mode = RemoteAvatar.ResolveBodyMode(out bool teamless);
            if (teamless && (mode == null || mode.BodyPrefab == null))
            {
                return SeedBodyVariant(ModeRuntime.BodySeed, playerId);
            }

            return RosterBodyVariant(playerId);
        }

        /// <summary>Body variant from the scene seed + player id. Deterministic on every client
        /// (players and admin) and for a late joiner too, because both inputs travel on the wire.
        /// <para>⚠️ Explicit integer mixing (murmur3 <c>fmix32</c>) on purpose:
        /// <c>string.GetHashCode</c> is seeded per process and <c>UnityEngine.Random</c> is not shared,
        /// so either would draw a DIFFERENT body on each headset. The id is folded in with the golden
        /// ratio constant so neighbouring ids do not land on the same body.</para></summary>
        private static int SeedBodyVariant(int seed, int playerId)
        {
            uint h = (uint)seed ^ ((uint)playerId * 0x9E3779B1u);
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return (int)(h & 1u);
        }

        /// <summary>Teamless body variant (0/1) from the roster, or <c>-1</c> when the player has no
        /// roster row yet (variant left as it is — never guessed).
        /// <para>The key is the player's RANK among the player rows ordered by <c>playerId</c>, not the
        /// id's parity: ids have gaps (admins, rejoins), and parity would pile everyone onto one body.
        /// Counting rank by "how many player rows have a smaller id" needs no sorting, so an unordered
        /// roster gives the same answer. ⚠️ <c>left</c>/<c>reconnecting</c> rows are COUNTED: they stay
        /// listed, so bodies do not swap mid-match when somebody drops — but a player removed from the
        /// roster entirely does shift the ranks after them.</para></summary>
        private int RosterBodyVariant(int playerId)
        {
            if (_lastLobbyState == null || _lastLobbyState.players == null)
            {
                return -1;
            }

            bool found = false;
            int rank = 0;
            for (int i = 0; i < _lastLobbyState.players.Length; i++)
            {
                PlayerInfo info = _lastLobbyState.players[i];
                if (info == null || info.role == AppSession.RoleAdmin)
                {
                    continue;
                }

                if (info.playerId == playerId)
                {
                    found = true;
                }
                else if (info.playerId < playerId)
                {
                    rank++;
                }
            }

            return found ? rank % 2 : -1;
        }
    }
}
