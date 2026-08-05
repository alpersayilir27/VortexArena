using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Oyuncu başına dünya-uzayı işaretçisi: <b>ayaklarının etrafında halka</b> ve
    /// <b>altında ad etiketi</b> (kullanıcının kuş bakışı isteği).
    /// <para>
    /// <c>RemoteAvatar</c>'a DOKUNMAZ — oyuncu tarafı görselleri admin için değişmemeli. Kendi
    /// nesnelerini <see cref="AdminSpectator"/>'ın kalıcı kökü altında tutar, böylece sahne
    /// değişimlerinde yeniden kurulmaları gerekmez (konum her karede arena uzayından türetilir).
    /// </para>
    /// <para>
    /// <b>Halka zeminde durur:</b> oyuncunun BAŞ pozunun x/z'si alınıp arena uzayında y=0.02'ye
    /// indirilir, sonra dünyaya çevrilir — böylece oyuncu eğildiğinde halka yerinde kalır.
    /// <b>Ad etiketi</b> kameraya döner ve ekran uzayında halkanın ALTINA gelmesi için kameranın
    /// yukarı vektörünün tersine kaydırılır: kuş bakışında da serbest kipte de "dairenin altında"
    /// okunur.
    /// </para>
    /// </summary>
    public class AdminPlayerMarkers : MonoBehaviour
    {
        /// <summary>Halka çapı (m) — omuz genişliğinden biraz geniş.</summary>
        private const float RingDiameter = 0.9f;

        /// <summary>Halka canvas'ı piksel boyu; dünya ölçeği bununla çapı verir.</summary>
        private const float RingPixels = 300f;

        /// <summary>Ad etiketinin halkadan ekran-uzayı uzaklığı (m).</summary>
        private const float LabelOffset = 0.62f;

        /// <summary>Zeminden yükseklik (m) — zemine gömülmesin (z-fighting).</summary>
        private const float FloorLift = 0.02f;

        /// <summary>Seçili oyuncunun halkası bu kadar büyür.</summary>
        private const float SelectedScale = 1.18f;

        /// <summary>Ölü işaretçinin renk çarpanı (RemoteAvatar ile aynı).</summary>
        private const float DeadColorScale = 0.35f;

        private class Marker
        {
            public GameObject root;
            public AdminPlayerMarker view;
            public Transform ring;
            public Image ringImage;
            public Transform label;
            public TextMeshProUGUI labelText;
        }

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();

        /// <summary>İşaretçi prefabı (<c>Resources</c>'tan bir kez yüklenir). Bu bileşen sahneye
        /// değil koddan eklendiği için <c>[SerializeField]</c> ile bağlanamaz.</summary>
        private AdminPlayerMarker _markerPrefab;

        private bool _prefabMissingLogged;
        private readonly List<int> _idScratch = new List<int>();
        private bool _subscribed;

        private void Start()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                // ArenaClient bootstrap'ı bizden önce koşar — pratikte olmaz.
                Debug.LogWarning("[AdminPlayerMarkers] RemotePlayerRegistry yok; işaretçiler devre dışı.");
                enabled = false;
                return;
            }

            registry.OnRemoteJoined += Spawn;
            registry.OnRemoteLeft += Despawn;
            _subscribed = true;

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

                _subscribed = false;
            }

            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                if (kv.Value.root != null)
                {
                    Destroy(kv.Value.root);
                }
            }

            _markers.Clear();
        }

        private void LateUpdate()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            Camera camera = AdminSpectator.Instance != null ? AdminSpectator.Instance.Camera : null;
            if (registry == null || camera == null)
            {
                return;
            }

            bool ringsVisible = AdminSession.MarkersVisibleNow();
            bool labelsVisible = ringsVisible && AdminSession.Nameplates;
            AdminRoster roster = AdminRoster.Instance;
            int selectedId = AdminSession.SelectedPlayerId;

            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                Marker marker = kv.Value;
                if (marker.root == null)
                {
                    continue;
                }

                if (!ringsVisible ||
                    !registry.GetInterpolatedPose(kv.Key, out Pose head, out _, out _))
                {
                    if (marker.root.activeSelf)
                    {
                        marker.root.SetActive(false);
                    }

                    continue;
                }

                if (!marker.root.activeSelf)
                {
                    marker.root.SetActive(true);
                }

                // Ayak izi: baş pozunun x/z'si, arena zemininin hemen üstü.
                var floorArena = new Pose(
                    new Vector3(head.position.x, FloorLift, head.position.z), Quaternion.identity);
                Vector3 floorWorld = ArenaSpace.ArenaToWorld(floorArena).position;

                bool selected = kv.Key == selectedId;
                AdminPlayerView view = roster != null ? roster.Find(kv.Key) : null;
                bool alive = view != null ? view.alive : registry.IsAlive(kv.Key);
                Color color = ResolveColor(view, selected, alive);

                // Halka: zeminde yatar (x=90). Daire olduğu için yaw önemsiz.
                marker.ring.SetPositionAndRotation(floorWorld, Quaternion.Euler(90f, 0f, 0f));
                marker.ring.localScale = Vector3.one *
                    (RingDiameter / RingPixels * (selected ? SelectedScale : 1f));

                if (marker.ringImage != null)
                {
                    marker.ringImage.color = color;
                }

                if (marker.view != null)
                {
                    marker.view.SetSelected(selected);
                }

                if (marker.label == null)
                {
                    continue;
                }

                if (marker.label.gameObject.activeSelf != labelsVisible)
                {
                    marker.label.gameObject.SetActive(labelsVisible);
                }

                if (!labelsVisible)
                {
                    continue;
                }

                // Ekran uzayında halkanın ALTI: kameranın yukarı vektörünün tersi.
                Transform cameraTransform = camera.transform;
                Vector3 labelPosition = floorWorld - cameraTransform.up * LabelOffset;
                Vector3 toCamera = labelPosition - cameraTransform.position;
                marker.label.SetPositionAndRotation(
                    labelPosition,
                    toCamera.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(toCamera, cameraTransform.up)
                        : marker.label.rotation);

                if (marker.labelText != null)
                {
                    marker.labelText.color = ResolveLabelColor(view, alive);
                    marker.labelText.text = BuildLabel(kv.Key, view, alive);
                }
            }
        }

        // ---------------------------------------------------------------- kurulum

        private void Spawn(int playerId)
        {
            if (_markers.ContainsKey(playerId))
            {
                return;
            }

            if (_markerPrefab == null)
            {
                _markerPrefab = Resources.Load<AdminPlayerMarker>(AdminPlayerMarker.ResourcePath);
                if (_markerPrefab == null)
                {
                    if (!_prefabMissingLogged)
                    {
                        _prefabMissingLogged = true;
                        Debug.LogError(
                            $"[AdminPlayerMarkers] '{AdminPlayerMarker.ResourcePath}' prefabı " +
                            "bulunamadı — oyuncu halkaları çizilemeyecek.");
                    }

                    return;
                }
            }

            AdminPlayerMarker instance = Instantiate(_markerPrefab, transform, false);
            instance.name = $"[AdminMarker_{playerId}]";

            _markers.Add(playerId, new Marker
            {
                root = instance.gameObject,
                view = instance,
                ring = instance.Ring,
                ringImage = instance.RingImage,
                label = instance.Label,
                labelText = instance.LabelText
            });
        }

        private void Despawn(int playerId)
        {
            if (!_markers.TryGetValue(playerId, out Marker marker))
            {
                return;
            }

            _markers.Remove(playerId);
            if (marker.root != null)
            {
                Destroy(marker.root);
            }
        }

        // ---------------------------------------------------------------- görünüm

        /// <summary>
        /// Ad etiketi rengi: <b>daima takım rengi</b> (ölüde karartılmış). Seçim vurgusu HALKANIN
        /// işidir — halka zaten büyüyor ve sprite değiştiriyor; ismi de vurguya boyamak, operatörün
        /// bir bakışta "bu hangi takım" sorusunu cevaplamasını her seferinde bir oyuncuda bozardı.
        /// </summary>
        private static Color ResolveLabelColor(AdminPlayerView view, bool alive)
        {
            Color team = UiKit.TeamColor(view != null ? view.team : "");
            return alive ? team : UiKit.Dim(team, DeadColorScale);
        }

        private static Color ResolveColor(AdminPlayerView view, bool selected, bool alive)
        {
            if (selected)
            {
                return alive ? UiKit.Accent : UiKit.Dim(UiKit.Accent, DeadColorScale);
            }

            Color team = UiKit.TeamColor(view != null ? view.team : "");
            return alive ? team : UiKit.Dim(team, DeadColorScale);
        }

        private static string BuildLabel(int playerId, AdminPlayerView view, bool alive)
        {
            string name = view != null && !string.IsNullOrEmpty(view.name)
                ? view.name
                : $"Oyuncu {playerId}";

            if (!alive)
            {
                return $"{name} (ölü)";
            }

            return view != null
                ? $"{name}  {Mathf.RoundToInt(view.hp)}"
                : name;
        }
    }
}
