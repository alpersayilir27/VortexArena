using System.Collections.Generic;
using UnityEngine;
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

            avatar.SetInfo(displayName, number, team);
            avatar.SetCalibrated(calibrated);
            avatar.SetBodyScale(bodyScale);
        }
    }
}
