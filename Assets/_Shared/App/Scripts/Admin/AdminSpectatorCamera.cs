using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
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
    /// Kadrajın TEK kaynağı sahnedeki <see cref="ArenaBoundary"/>'dir (varsayılan ölçü YOKTUR);
    /// tekerlek zoom. Kameranın yüksekliği de oradan (boyut dosyasının <c>topViewHeight</c>'ı)
    /// gelir, yazılmamışsa <see cref="DefaultTopDownHeight"/>. Sahnede <see cref="ArenaRoof"/> varsa bu kipe girerken çatı gizlenir
    /// (tercih <c>AdminSession.Roof</c>), çıkarken geri gelir.</item>
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

        /// <summary>
        /// Kuş bakışı kamera yüksekliğinin VARSAYILANI (m) — ortografikte yalnız kırpma için
        /// anlamlı. Mekanın boyut dosyasında <c>topViewHeight</c> yazıyorsa o kazanır: yüksek
        /// tavanlı bir mekanda 20 m çatının altında kalabilir.
        /// </summary>
        private const float DefaultTopDownHeight = 20f;

        /// <summary>Kuş bakışı kadraj payı (arena kenarı ekrana yapışmasın).</summary>
        private const float TopDownMargin = 1.08f;

        /// <summary>Kuş bakışı zoom sınırları (1 = tam arena).</summary>
        private const float ZoomMin = 0.4f;
        private const float ZoomMax = 1.6f;

        private Camera _camera;
        private AdminCameraMode _appliedMode = (AdminCameraMode)(-1);

        /// <summary>Bu sahne için "ArenaBoundary yok" uyarısı verildi mi (kare başına bağırmasın).</summary>
        private bool _warnedMissingBoundary;

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
            _warnedMissingBoundary = false;
        }

        private void LateUpdate()
        {
            AdminCameraMode mode = AdminSession.CameraMode;
            if (mode != _appliedMode)
            {
                EnterMode(mode);
                _appliedMode = mode;
            }

            ApplyAudioFocus(mode);

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

        // ------------------------------------------------------------------- ses odağı

        /// <summary>
        /// Gözlemcinin kulağını kamerasıyla aynı yere bakar hâle getirir
        /// (<see cref="RemoteShotFx.SpectatorAudioFocus"/>).
        /// <para><b>Yalnız POV'da</b> odak vardır: izlenen oyuncunun silahı duyulur, sahadaki diğer
        /// oyuncuların atışları susar — hepsi birden çalınca operatör hangi sesin izlediği oyuncuya
        /// ait olduğunu ayırt edemez.</para>
        /// <para>⚠️ <b>Kuş bakışı ve serbest kipte filtre YOKTUR</b> (odak <c>null</c>): o kiplerde
        /// operatör sahanın tamamına bakıyor ve atış sesi "nerede çatışma var" sorusunun cevabıdır
        /// — susturmak kuş bakışını sağırlaştırırdı. Aynı sebeple POV'da <b>oyuncu seçilmemişse</b>
        /// (kamera son konumunda donuktur) filtre yine kurulmaz: susturacak bir odak yok.</para>
        /// <para>⚠️ Soru her karede sorulur, <c>AdminSession.Changed</c>'e abone olunarak DEĞİL:
        /// odağı besleyen iki değer de (kip + seçili oyuncu) koşan maçta değişiyor ve kaçırılan
        /// tek bir olay operatöre kalıcı olarak YANLIŞ oyuncunun silahını duyurur. Aynı gerekçe
        /// <c>RemoteAvatar</c> ad etiketlerinde de geçerli.</para>
        /// <para>⚠️ Yazan TEK yer burasıdır. Gözlemci kamerası yalnız admin rolünde kurulur, yani
        /// oyuncu istemcisinde odak hiç yazılmaz (null = filtre yok) ve orada her atış duyulur.</para>
        /// </summary>
        private static void ApplyAudioFocus(AdminCameraMode mode)
        {
            int selected = AdminSession.SelectedPlayerId;
            RemoteShotFx.SpectatorAudioFocus =
                mode == AdminCameraMode.Pov && selected != 0 ? selected : (int?)null;
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

            if (!TryResolveArena(out Vector3 center, out float yaw, out Vector2 halfExtents, out float height))
            {
                // Kadraj ölçüsü UYDURULMAZ: kamera dünya origin'inin üstünde aşağı bakar ve
                // ortografik ölçü olduğu gibi kalır (operatör tekerlekle ayarlar).
                WarnMissingBoundary();
                transform.SetPositionAndRotation(
                    Vector3.up * DefaultTopDownHeight,
                    Quaternion.Euler(90f, 0f, 0f));
                return;
            }

            float aspect = _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            float sizeFromZ = halfExtents.y;
            float sizeFromX = halfExtents.x / aspect;
            _camera.orthographicSize = Mathf.Max(sizeFromZ, sizeFromX) * TopDownMargin * _zoom;

            transform.SetPositionAndRotation(
                center + Vector3.up * height,
                Quaternion.Euler(90f, yaw, 0f));
        }

        /// <summary>
        /// Arena merkezi/yönü/yarı ölçüsü ve kamera yüksekliği — TEK kaynağı sahnedeki
        /// <see cref="ArenaBoundary"/>'dir. <b>Varsayılan ölçü YOKTUR:</b> uydurulan bir arena
        /// boyutu doğru sandığın yanlış bir kadraj üretir; her arena sahnesinde bu bileşen
        /// zorunludur.
        /// <para>
        /// Yükseklik istisnadır ve varsayılanı vardır (<see cref="DefaultTopDownHeight"/>):
        /// kadrajı etkilemediği için "yazılmamış" olması bir kurulum hatası değil, tercih
        /// yokluğudur.
        /// </para>
        /// </summary>
        private bool TryResolveArena(
            out Vector3 center,
            out float yaw,
            out Vector2 halfExtents,
            out float height)
        {
            ArenaBoundary boundary = AdminSpectator.Instance != null
                ? AdminSpectator.Instance.Boundary
                : null;

            if (boundary == null)
            {
                center = Vector3.zero;
                yaw = 0f;
                halfExtents = Vector2.zero;
                height = DefaultTopDownHeight;
                return false;
            }

            Transform origin = boundary.transform;

            // ⚠️ Merkez transformun KONUMU DEĞİL, sınırın yerel merkezidir: çokgen planlı
            // (yamuk/kırık duvarlı) arenalarda sınırlayıcı kutunun ortası transformun üstüne
            // düşmez — konumu merkez saymak kadrajı arenanın dışına kaydırır. Dikdörtgen
            // arenalarda LocalCenter sıfırdır, yani davranış değişmez.
            Vector2 localCenter = boundary.LocalCenter;
            center = origin.TransformPoint(new Vector3(localCenter.x, 0f, localCenter.y));
            yaw = origin.eulerAngles.y;
            halfExtents = boundary.HalfExtents;

            float fromPlan = boundary.TopDownHeight;
            height = fromPlan > 0f ? fromPlan : DefaultTopDownHeight;
            return true;
        }

        /// <summary>
        /// Arena sahnesinde <see cref="ArenaBoundary"/> eksikse sahne başına BİR KEZ uyarır —
        /// kurulum hatası sessizce "biraz kayık kadraj" olarak gizlenmesin. Lobide arena
        /// olmaması beklenen durumdur, orada susulur.
        /// </summary>
        private void WarnMissingBoundary()
        {
            if (_warnedMissingBoundary)
            {
                return;
            }

            _warnedMissingBoundary = true;

            string scene = SceneManager.GetActiveScene().name;
            if (scene == AppSession.SceneLobby)
            {
                return;
            }

            Debug.LogWarning($"[AdminSpectatorCamera] '{scene}' sahnesinde ArenaBoundary yok — " +
                             "kuş bakışı kadrajı ölçüsüz kaldı. Arena sahnesinde ArenaBoundary ZORUNLUDUR.");
        }

        private static float NormalizePitch(float pitch)
        {
            // eulerAngles 0..360 verir; -89..89 aralığına indir.
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
