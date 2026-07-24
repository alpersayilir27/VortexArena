using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Admin üstten taktik görünüm (UGUI, dashboard paneline eklenir): her uzak
    /// oyuncu için runtime'da kurulan nokta + bakış yönü çubuğu + ad etiketini
    /// mapArea üzerinde sürer. Pozlar zaten arena uzayında geldiği için dünya
    /// dönüşümü YAPILMAZ; arena x/z doğrudan harita u/v'ye ölçeklenir.
    /// mapArea pivot/anchor'ının merkezde olduğu varsayılır.
    /// </summary>
    public class TacticalView : MonoBehaviour
    {
        [Header("Harita")]
        [SerializeField] private RectTransform mapArea;
        [Tooltip("Arena genişliği (x ekseni, metre).")]
        [SerializeField] private float arenaWidth = 10f;
        [Tooltip("Arena uzunluğu (z ekseni, metre).")]
        [SerializeField] private float arenaLength = 10f;

        // RemoteAvatar ile aynı takım renkleri.
        private static readonly Color TeamRedColor = new Color(0.85f, 0.20f, 0.20f);
        private static readonly Color TeamBlueColor = new Color(0.20f, 0.40f, 0.90f);
        private static readonly Color NeutralColor = new Color(0.6f, 0.6f, 0.6f);

        /// <summary>Ölü oyuncu noktasının solma oranı (alpha + parlaklık).</summary>
        private const float DeadFade = 0.35f;

        /// <summary>Oyuncu başına runtime'da kurulan harita işareti.</summary>
        private class Marker
        {
            public RectTransform root;
            public Image dot;
            public Image heading;
            public RectTransform headingRect;
            public TMP_Text label;
            public Color baseColor;   // takım rengi (canlı hâli)
            public bool alive = true; // son uygulanan yaşam durumu
            public bool aliveKnown;   // ilk kez uygulanana dek false
        }

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();
        private readonly List<int> _idScratch = new List<int>();

        private LobbyStateMsg _lastLobbyState;
        private bool _subscribed;

        private void Start()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                Debug.LogWarning("[TacticalView] RemotePlayerRegistry yok; taktik görünüm devre dışı.");
                enabled = false;
                return;
            }

            registry.OnRemoteJoined += HandleRemoteJoined;
            registry.OnRemoteLeft += HandleRemoteLeft;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnDisconnected += HandleDisconnected;
            _subscribed = true;

            // Panel geç açıldıysa zaten aktif oyuncular için geriye dönük işaret kur.
            registry.GetActivePlayerIds(_idScratch);
            for (int i = 0; i < _idScratch.Count; i++)
            {
                HandleRemoteJoined(_idScratch[i]);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry != null)
                {
                    registry.OnRemoteJoined -= HandleRemoteJoined;
                    registry.OnRemoteLeft -= HandleRemoteLeft;
                }

                NetEvents.OnLobbyState -= HandleLobbyState;
                NetEvents.OnDisconnected -= HandleDisconnected;
                _subscribed = false;
            }

            ClearMarkers();
        }

        private void Update()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null || mapArea == null)
            {
                return;
            }

            Rect rect = mapArea.rect;

            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                Marker marker = kv.Value;
                if (marker.root == null)
                {
                    continue;
                }

                if (!registry.GetInterpolatedPose(kv.Key, out Pose head, out _, out _))
                {
                    // Henüz örneksiz — noktayı gizle.
                    if (marker.root.gameObject.activeSelf)
                    {
                        marker.root.gameObject.SetActive(false);
                    }

                    continue;
                }

                if (!marker.root.gameObject.activeSelf)
                {
                    marker.root.gameObject.SetActive(true);
                }

                // Ölü oyuncunun noktası solar, bakış çubuğu gizlenir.
                bool alive = registry.IsAlive(kv.Key);
                if (!marker.aliveKnown || marker.alive != alive)
                {
                    marker.alive = alive;
                    marker.aliveKnown = true;
                    ApplyAliveVisual(marker);
                }

                // Taktik harita zaten arena uzayında — dünya dönüşümü YOK.
                float u = Mathf.Clamp01(head.position.x / arenaWidth + 0.5f);
                float v = Mathf.Clamp01(head.position.z / arenaLength + 0.5f);
                marker.root.anchoredPosition = new Vector2((u - 0.5f) * rect.width, (v - 0.5f) * rect.height);

                // Yaw: bakış yönü çubuğu döner, ad etiketi düz kalır.
                Vector3 forward = head.rotation * Vector3.forward;
                float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                if (marker.headingRect != null)
                {
                    marker.headingRect.localEulerAngles = new Vector3(0f, 0f, -yaw);
                }
            }
        }

        // -------------------------------------------------------- olay işleyiciler

        private void HandleRemoteJoined(int playerId)
        {
            if (mapArea == null || _markers.ContainsKey(playerId))
            {
                return;
            }

            _markers.Add(playerId, CreateMarker(playerId));
            ApplyLobbyInfo(playerId, _markers[playerId]);
        }

        private void HandleRemoteLeft(int playerId)
        {
            if (!_markers.TryGetValue(playerId, out Marker marker))
            {
                return;
            }

            _markers.Remove(playerId);
            if (marker.root != null)
            {
                Destroy(marker.root.gameObject);
            }
        }

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            _lastLobbyState = msg;

            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                ApplyLobbyInfo(kv.Key, kv.Value);
            }
        }

        private void HandleDisconnected()
        {
            _lastLobbyState = null;
            ClearMarkers();
        }

        // ------------------------------------------------------------ işaret kurma

        /// <summary>
        /// Nokta UI'ını runtime'da kurar (prefab/sprite bağımlılığı yok): 18x18 düz
        /// renk nokta, noktanın merkezinden yukarı uzanan 4x14 "Heading" çubuğu ve
        /// noktanın 14 px üstünde düz duran TMP ad etiketi.
        /// </summary>
        private Marker CreateMarker(int playerId)
        {
            var marker = new Marker();

            var rootGo = new GameObject($"Oyuncu_{playerId}", typeof(RectTransform));
            marker.root = (RectTransform)rootGo.transform;
            marker.root.SetParent(mapArea, false);
            marker.root.anchorMin = new Vector2(0.5f, 0.5f);
            marker.root.anchorMax = new Vector2(0.5f, 0.5f);
            marker.root.pivot = new Vector2(0.5f, 0.5f);
            marker.root.sizeDelta = new Vector2(18f, 18f);

            marker.dot = rootGo.AddComponent<Image>();
            marker.dot.raycastTarget = false;

            var headingGo = new GameObject("Heading", typeof(RectTransform));
            marker.headingRect = (RectTransform)headingGo.transform;
            marker.headingRect.SetParent(marker.root, false);
            marker.headingRect.anchorMin = new Vector2(0.5f, 0.5f);
            marker.headingRect.anchorMax = new Vector2(0.5f, 0.5f);
            marker.headingRect.pivot = new Vector2(0.5f, 0f); // pivot altta → nokta merkezinden yukarı
            marker.headingRect.anchoredPosition = Vector2.zero;
            marker.headingRect.sizeDelta = new Vector2(4f, 14f);

            marker.heading = headingGo.AddComponent<Image>();
            marker.heading.raycastTarget = false;

            var labelGo = new GameObject("Ad", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(marker.root, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 14f); // noktanın 14 px üstünde
            labelRect.sizeDelta = new Vector2(120f, 18f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 14f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            marker.label = label;

            // İlk poz örneği gelene dek gizli.
            rootGo.SetActive(false);
            return marker;
        }

        /// <summary>Son lobby_state'ten ad/takım uygular; bulunamazsa "Oyuncu {id}" + gri.</summary>
        private void ApplyLobbyInfo(int playerId, Marker marker)
        {
            string displayName = $"Oyuncu {playerId}";
            string team = "";

            if (_lastLobbyState != null && _lastLobbyState.players != null)
            {
                for (int i = 0; i < _lastLobbyState.players.Length; i++)
                {
                    PlayerInfo info = _lastLobbyState.players[i];
                    if (info == null || info.playerId != playerId)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(info.name))
                    {
                        displayName = info.name;
                    }

                    team = info.team ?? "";
                    break;
                }
            }

            if (marker.label != null)
            {
                marker.label.text = displayName;
            }

            marker.baseColor = team == "red" ? TeamRedColor : team == "blue" ? TeamBlueColor : NeutralColor;
            ApplyAliveVisual(marker);
        }

        /// <summary>Canlı: tam takım rengi + bakış çubuğu. Ölü: soluk nokta, çubuk gizli.</summary>
        private static void ApplyAliveVisual(Marker marker)
        {
            Color color = marker.baseColor;
            if (!marker.alive)
            {
                color = new Color(color.r * DeadFade, color.g * DeadFade, color.b * DeadFade, DeadFade);
            }

            if (marker.dot != null)
            {
                marker.dot.color = color;
            }

            if (marker.heading != null)
            {
                marker.heading.color = color;
                if (marker.heading.gameObject.activeSelf != marker.alive)
                {
                    marker.heading.gameObject.SetActive(marker.alive);
                }
            }

            if (marker.label != null)
            {
                Color labelColor = marker.label.color;
                labelColor.a = marker.alive ? 1f : DeadFade;
                marker.label.color = labelColor;
            }
        }

        /// <summary>
        /// Taktik haritanın metre ölçeğini seçili arenaya eşitler (MapDefinition.Size).
        /// Noktalar bir sonraki Update'te yeni ölçekle yeniden konumlanır.
        /// </summary>
        public void SetArenaSize(float width, float length)
        {
            if (width > 0.01f)
            {
                arenaWidth = width;
            }

            if (length > 0.01f)
            {
                arenaLength = length;
            }
        }

        private void ClearMarkers()
        {
            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                if (kv.Value.root != null)
                {
                    Destroy(kv.Value.root.gameObject);
                }
            }

            _markers.Clear();
        }
    }
}
