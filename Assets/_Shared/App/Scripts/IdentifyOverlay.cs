using TMPro;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// identify komutu geldiğinde bu cihazda birkaç saniye büyük kimlik yazısı
    /// gösterir (playerId + ad) — adminin fiziksel cihaz eşlemesi için. Kalıcı
    /// dinleyici kendini önyükler; overlay göz hizasında world-space canvas,
    /// her kare kameraya bakar, son saniyede CanvasGroup ile söner.
    /// </summary>
    public class IdentifyOverlay : MonoBehaviour
    {
        private const float ShowSeconds = 4f;
        private const float FadeSeconds = 1f;
        private const float DistanceMeters = 1.5f;

        private static IdentifyOverlay _instance;

        private string _ownName = "";
        private GameObject _display;
        private CanvasGroup _group;
        private Camera _camera;
        private float _expireTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[IdentifyOverlay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<IdentifyOverlay>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void OnEnable()
        {
            NetEvents.OnIdentify += HandleIdentify;
            NetEvents.OnLobbyState += HandleLobbyState;
        }

        private void OnDisable()
        {
            NetEvents.OnIdentify -= HandleIdentify;
            NetEvents.OnLobbyState -= HandleLobbyState;
        }

        private void Update()
        {
            if (_display == null)
            {
                return;
            }

            if (_camera == null || Time.unscaledTime >= _expireTime)
            {
                Destroy(_display);
                _display = null;
                _group = null;
                return;
            }

            PlaceInFront();

            float remaining = _expireTime - Time.unscaledTime;
            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01(remaining / FadeSeconds);
            }
        }

        // -------------------------------------------------------- olay işleyiciler

        /// <summary>Kendi adımızı roster'dan önbelleğe al (overlay metni için).</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null || ArenaClient.Instance == null)
            {
                return;
            }

            int myId = ArenaClient.Instance.PlayerId;
            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo p = msg.players[i];
                if (p != null && p.playerId == myId)
                {
                    _ownName = p.name;
                    return;
                }
            }
        }

        private void HandleIdentify(IdentifyMsg msg)
        {
            int myId = ArenaClient.Instance != null ? ArenaClient.Instance.PlayerId : 0;

            // Sunucu identify'ı yalnız hedef cihaza yollar (alansız); playerId yine de
            // doluysa emniyet için süz (başka oyuncunun kimliği bizde görünmesin).
            if (msg != null && msg.playerId != 0 && msg.playerId != myId)
            {
                return;
            }

            Show(myId, _ownName);
        }

        // ---------------------------------------------------------------- overlay

        private void Show(int playerId, string playerName)
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogWarning("[IdentifyOverlay] Camera.main yok; overlay atlandı.");
                return;
            }

            if (_display != null)
            {
                Destroy(_display);
            }

            _display = new GameObject("[IdentifyDisplay]");
            _display.transform.SetParent(transform, false);

            var canvas = _display.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000; // her şeyin üstünde

            _group = _display.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var rect = _display.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(800f, 400f);
            _display.transform.localScale = Vector3.one * 0.002f; // 800px → 1.6 m

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_display.transform, false);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            string line2 = playerId > 0
                ? $"Oyuncu {playerId}" + (string.IsNullOrEmpty(playerName) ? "" : $" — {playerName}")
                : "";
            tmp.text = string.IsNullOrEmpty(line2) ? "SEN BUSUN" : $"SEN BUSUN\n{line2}";
            tmp.fontSize = 96f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 0.92f, 0.2f, 1f); // parlak sarı

            var textRect = tmp.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            _expireTime = Time.unscaledTime + ShowSeconds;
            PlaceInFront();
        }

        private void PlaceInFront()
        {
            _display.transform.position = _camera.transform.position + _camera.transform.forward * DistanceMeters;

            // Canvas ön yüzü +Z'den okunur → +Z kameradan UZAĞA bakmalı.
            Vector3 dir = _display.transform.position - _camera.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                _display.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }
    }
}
