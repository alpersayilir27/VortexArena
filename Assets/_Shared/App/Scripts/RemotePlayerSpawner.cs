using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Uzak oyuncu avatarlarını yönetir (Lobby/arena sahnelerine konur):
    /// RemotePlayerRegistry'nin katılım/ayrılış olaylarına göre RemoteAvatar
    /// prefab'ını yaratır/yok eder; lobby_state'ten ad ve takım bilgisini besler.
    /// Avatar pozlarını RemoteAvatar'ın kendisi registry'den okur.
    /// </summary>
    public class RemotePlayerSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private RemoteAvatar avatarPrefab;

        private readonly Dictionary<int, RemoteAvatar> _avatars = new Dictionary<int, RemoteAvatar>();
        private readonly List<int> _idScratch = new List<int>();

        private LobbyStateMsg _lastLobbyState;

        /// <summary>YEREL oyuncunun takımı — uzak avatarların dost göstergesi buna göre açılır.
        /// Admin gözlemcide (ve takımsız modlarda) boştur, o zaman hiçbir avatar dost işaretlenmez.</summary>
        private string _localTeam = "";
        private bool _subscribed;
        private bool _prefabWarned;

        private void Start()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                // ArenaClient bootstrap'ı sahneden önce koşar — pratikte olmaz.
                Debug.LogWarning("[RemotePlayerSpawner] RemotePlayerRegistry yok; spawner devre dışı.");
                enabled = false;
                return;
            }

            registry.OnRemoteJoined += Spawn;
            registry.OnRemoteLeft += Despawn;
            NetEvents.OnLobbyState += HandleLobbyState;
            _subscribed = true;

            // Sahne geç yüklendiyse zaten aktif olan uzak oyuncuları geriye dönük spawn et.
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

        // -------------------------------------------------------- olay işleyiciler

        private void Spawn(int playerId)
        {
            if (_avatars.ContainsKey(playerId))
            {
                return;
            }

            // Çift emniyet: registry zaten kendi id'mizi atlar ama snapshot'ta
            // görünürse bile kendimize avatar yaratma.
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
            _localTeam = ResolveLocalTeam(msg);

            foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
            {
                ApplyLobbyInfo(kv.Value);
            }
        }

        /// <summary>Roster'da KENDİ id'mizi bulup takımımızı okur (bulunamazsa boş).</summary>
        private static string ResolveLocalTeam(LobbyStateMsg msg)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || msg == null || msg.players == null)
            {
                return "";
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info != null && info.playerId == client.PlayerId)
                {
                    return info.team ?? "";
                }
            }

            return "";
        }

        // ---------------------------------------------------------------- yardımcı

        /// <summary>Son lobby_state'ten ad/takım/kalibrasyon bilgisini uygular; bulunamazsa varsayılan.</summary>
        private void ApplyLobbyInfo(RemoteAvatar avatar)
        {
            if (avatar == null)
            {
                return;
            }

            string displayName = $"Oyuncu {avatar.PlayerId}";
            // Roster henüz gelmemişken numara UYDURULMAZ (0 = etikette basılmaz): forma numarası
            // ayırt edici alandır, yanlış bir sayı adı tekrarlı oyuncularda doğrudan karışıklık olur.
            int number = 0;
            string team = "";
            // Roster'da bulunamayan oyuncu KALİBRELİ sayılır (§10.6): bilinmeyen durumu alarm gibi
            // göstermek — henüz roster'ı gelmemiş yeni oyuncuyu parlatmak — gürültü üretir.
            bool calibrated = true;

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
                    break;
                }
            }

            avatar.SetInfo(displayName, number, team);
            avatar.SetCalibrated(calibrated);

            // Takımsız modda (FFA) ve admin gözlemcide _localTeam boştur → kimse dost işaretlenmez.
            avatar.SetFriendly(!string.IsNullOrEmpty(_localTeam) && team == _localTeam);
        }
    }
}
