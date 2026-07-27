using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Gözlemci kamerasının üç kipi. <see cref="AdminSession.CameraMode"/> hangi kipin sürdüğünü
    /// söyler; kip başına girdi ve konumlandırma burada.
    /// <list type="bullet">
    /// <item><b>POV:</b> seçili oyuncunun BAŞ pozu (arena uzayı → dünya). Poz gelmiyorsa son
    /// konumda kalır (kamerayı origin'e zıplatmak operatörü şaşırtır).</item>
    /// <item><b>Serbest:</b> WASD düzlemde, Q/E alçal/yüksel, <b>sağ tuş basılı</b> fareyle bakış,
    /// Shift ×3, tekerlek taban hızı. İmleç KİLİTLENMEZ — operatörün tek ekranı var ve HUD
    /// düğmelerine erişmesi gerekir.</item>
    /// <item><b>Kuş bakışı:</b> ortografik, arena merkezinin üstünde, arena yaw'ına hizalı.
    /// Kadraj sırayla <see cref="ArenaBoundary"/> → <c>MapDefinition.Size</c> → 10x10
    /// varsayılanından gelir; tekerlek zoom. Sahnede <see cref="ArenaRoof"/> varsa bu kipe
    /// girerken çatı gizlenir (tercih <c>AdminSession.Roof</c>), çıkarken geri gelir.</item>
    /// </list>
    /// <para>Poz okuması <c>LateUpdate</c>'te yapılır: <c>RemoteAvatar</c> de aynı karede aynı
    /// kayıtçıdan okuyor, kamera bir kare geriden gitmesin.</para>
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AdminSpectatorCamera : MonoBehaviour
    {
        /// <summary>Fare hassasiyeti (derece / piksel).</summary>
        private const float LookSensitivity = 0.12f;

        /// <summary>Shift ile hız çarpanı.</summary>
        private const float BoostMultiplier = 3f;

        /// <summary>Serbest kipte zeminin altına inilmesin.</summary>
        private const float MinHeight = 0.2f;

        /// <summary>Kuş bakışı kamera yüksekliği (m) — ortografikte yalnız kırpma için anlamlı.</summary>
        private const float TopDownHeight = 20f;

        /// <summary>Kuş bakışı kadraj payı (arena kenarı ekrana yapışmasın).</summary>
        private const float TopDownMargin = 1.08f;

        /// <summary>Kuş bakışı zoom sınırları (1 = tam arena).</summary>
        private const float ZoomMin = 0.4f;
        private const float ZoomMax = 1.6f;

        /// <summary>Arena sınırı bulunamazsa varsayılan yarı ölçü (m).</summary>
        private static readonly Vector2 DefaultHalfExtents = new Vector2(5f, 5f);

        private Camera _camera;
        private AdminCameraMode _appliedMode = (AdminCameraMode)(-1);

        // Serbest kip durumu.
        private float _yaw;
        private float _pitch;

        // Kuş bakışı durumu.
        private float _zoom = 1f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>Yeni sahne devralındı: kipi baştan uygula (arena sınırı değişti).</summary>
        public void OnSceneAdopted()
        {
            _appliedMode = (AdminCameraMode)(-1);
        }

        private void LateUpdate()
        {
            AdminCameraMode mode = AdminSession.CameraMode;
            if (mode != _appliedMode)
            {
                EnterMode(mode);
                _appliedMode = mode;
            }

            switch (mode)
            {
                case AdminCameraMode.Pov:
                    DrivePov();
                    break;
                case AdminCameraMode.Free:
                    DriveFree();
                    break;
                default:
                    DriveTopDown();
                    break;
            }
        }

        /// <summary>Kipe girişte bir kez: projeksiyon, çatı görünürlüğü ve başlangıç açıları.</summary>
        private void EnterMode(AdminCameraMode mode)
        {
            _camera.orthographic = mode == AdminCameraMode.TopDown;

            // Kuş bakışına girerken çatı kalkar, çıkarken geri gelir (tercih: AdminSession.Roof).
            AdminSpectator.RefreshRoof();

            if (mode == AdminCameraMode.Free)
            {
                // Serbest kipe hangi açıyla girdiysek oradan devam (ani sıçrama olmasın).
                Vector3 euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = NormalizePitch(euler.x);
            }
        }

        // ------------------------------------------------------------------- POV

        private void DrivePov()
        {
            int playerId = AdminSession.SelectedPlayerId;
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (playerId == 0 || registry == null ||
                !registry.GetInterpolatedPose(playerId, out Pose head, out _, out _))
            {
                return; // poz yok: son konumda kal (HUD "poz yok" yazar)
            }

            Pose world = ArenaSpace.ArenaToWorld(head);
            transform.SetPositionAndRotation(world.position, world.rotation);
        }

        // --------------------------------------------------------------- serbest

        private void DriveFree()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (mouse != null)
            {
                // Bakış YALNIZ sağ tuş basılıyken; imleç serbest kalır (HUD tıklanabilir).
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _yaw += delta.x * LookSensitivity;
                    _pitch = Mathf.Clamp(_pitch - delta.y * LookSensitivity, -89f, 89f);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f))
                {
                    // Tekerlek: taban hızını kademeli değiştirir (tercihe yazılır, kalıcı).
                    AdminSession.FreeSpeed += Mathf.Sign(scroll) * 0.5f;
                }
            }

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (keyboard == null)
            {
                return;
            }

            var input = Vector3.zero;
            if (keyboard.wKey.isPressed) input += transform.forward;
            if (keyboard.sKey.isPressed) input -= transform.forward;
            if (keyboard.dKey.isPressed) input += transform.right;
            if (keyboard.aKey.isPressed) input -= transform.right;
            if (keyboard.eKey.isPressed) input += Vector3.up;
            if (keyboard.qKey.isPressed) input -= Vector3.up;

            if (input.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float speed = AdminSession.FreeSpeed *
                          (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                              ? BoostMultiplier
                              : 1f);

            Vector3 position = transform.position + input.normalized * (speed * Time.unscaledDeltaTime);
            position.y = Mathf.Max(MinHeight, position.y);
            transform.position = position;
        }

        // ----------------------------------------------------------- kuş bakışı

        private void DriveTopDown()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f))
                {
                    _zoom = Mathf.Clamp(_zoom - Mathf.Sign(scroll) * 0.1f, ZoomMin, ZoomMax);
                }
            }

            ResolveArena(out Vector3 center, out float yaw, out Vector2 halfExtents);

            float aspect = _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            float sizeFromZ = halfExtents.y;
            float sizeFromX = halfExtents.x / aspect;
            _camera.orthographicSize = Mathf.Max(sizeFromZ, sizeFromX) * TopDownMargin * _zoom;

            transform.SetPositionAndRotation(
                center + Vector3.up * TopDownHeight,
                Quaternion.Euler(90f, yaw, 0f));
        }

        /// <summary>
        /// Arena merkezi/yönü/yarı ölçüsü. Sıra: sahnedeki <see cref="ArenaBoundary"/> (gerçek
        /// duvarlar) → aktif haritanın <c>MapDefinition.Size</c>'ı → 10x10 varsayılanı (Lobby
        /// gibi sınırsız sahneler).
        /// </summary>
        private void ResolveArena(out Vector3 center, out float yaw, out Vector2 halfExtents)
        {
            ArenaBoundary boundary = AdminSpectator.Instance != null
                ? AdminSpectator.Instance.Boundary
                : null;

            if (boundary != null)
            {
                Transform origin = boundary.transform;
                center = origin.position;
                yaw = origin.eulerAngles.y;
                halfExtents = boundary.HalfExtents;
                return;
            }

            center = Vector3.zero;
            yaw = 0f;
            halfExtents = DefaultHalfExtents;

            // Yüklü sahnenin adı (sunucunun bildirdiği değil): harita önizlemesinde de doğru olsun.
            MapDefinition map = AdminContent.FindMap(SceneManager.GetActiveScene().name);
            if (map != null)
            {
                halfExtents = new Vector2(Mathf.Max(1f, map.Size.x * 0.5f), Mathf.Max(1f, map.Size.y * 0.5f));
            }
        }

        private static float NormalizePitch(float pitch)
        {
            // eulerAngles 0..360 verir; -89..89 aralığına indir.
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
