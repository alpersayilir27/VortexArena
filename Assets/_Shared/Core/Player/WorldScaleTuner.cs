using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// <b>Ölçek denemesi</b> — dünyanın kaç kat büyük hissedildiğini gözler arası mesafeden ayarlar.
    ///
    /// <para>VR'da büyüklük algısının kadranı FOV değil <b>IPD</b>'dir: iki gözün ayrımı daraldıkça
    /// beyin sahneyi orantılı olarak devleştirir. Göz ayrımı OpenXR runtime'ından geldiği için
    /// doğrudan yazılamaz; <see cref="WorldScaleMethod"/> iki dolaylı yolu ayırır.</para>
    ///
    /// <para>⚠️ Her iki yolda da <b>kafanın dünya konumu 1:1 korunur</b>: free-roam'da fiziksel adım
    /// sanal adıma eşittir, aksi hâlde arena sınırı ve ağa giden poz kayardı. Kafa yüksekliği de
    /// sabit kalır, yani boy ölçümü (<see cref="BodyScaleState"/>) etkilenmez.</para>
    ///
    /// <para>Kabul edilen bedel (yalnız <see cref="WorldScaleMethod.RigScale"/>'de): eller kafaya
    /// aynı oranda yaklaşır, ağa giden el pozları da o kadar sıkışır.</para>
    ///
    /// <para><c>magnification = 1</c> bileşeni tamamen etkisiz kılar; bileşen kapatıldığında rig
    /// ölçeği, tracking offset'i ve stereo matrisleri eski hâline döner.</para>
    /// </summary>
    [DefaultExecutionOrder(32000)]
    [RequireComponent(typeof(OVRCameraRig))]
    public class WorldScaleTuner : MonoBehaviour
    {
        [Tooltip("Dünya kaç kat büyük hissedilsin. 1 = kapalı (gerçek ölçek), 1.15 = %15 daha dev.")]
        [SerializeField] [Range(MinMagnification, MaxMagnification)] private float magnification = 1.15f;

        [Tooltip("Göz ayrımının hangi yoldan daraltılacağı.")]
        [SerializeField] private WorldScaleMethod method = WorldScaleMethod.ViewMatrix;

        [Tooltip("Başlıktayken sağ kumandanın çubuğuyla canlı ayar (sağ = büyüt, sol = küçült), B ile sıfırlama.")]
        [SerializeField] private bool liveTuning = true;

        [Tooltip("Saniyede bir ölçüleni konsola yazar: rig ölçeği ve gerçek göz ayrımı.")]
        [SerializeField] private bool logDiagnostics = true;

        private const float MinMagnification = 1f;
        private const float MaxMagnification = 2f;

        /// <summary>Çubuk tam yatırıkken saniyede bu kadar büyütme değişir.</summary>
        private const float TuningStepPerSecond = 0.2f;

        private const float ThumbstickDeadzone = 0.5f;

        /// <summary>Log gürültüsü olmasın diye: değer bu kadar sapmadan yeniden yazılmaz.</summary>
        private const float LogThreshold = 0.01f;

        private OVRCameraRig _rig;
        private Camera _headCamera;
        private Vector3 _trackingSpaceBase;
        private bool _baseCaptured;
        private bool _rigScaleApplied;
        private bool _viewMatrixApplied;
        private float _loggedMagnification = float.NaN;
        private float _nextDiagnosticTime;

        /// <summary>Runtime'ın verdiği ham göz ayrımı (m) — override'dan ÖNCE ölçülür.</summary>
        private float _rawSeparation;

        /// <summary>Bizim yazdığımız göz ayrımı (m). Ham değere eşit kalıyorsa yol tutmuyor demektir.</summary>
        private float _appliedSeparation;

        private void Awake()
        {
            _rig = GetComponent<OVRCameraRig>();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RestoreRigScale();
            RestoreViewMatrices();
        }

        private void LateUpdate()
        {
            if (liveTuning)
            {
                ReadLiveTuning();
            }

            if (method == WorldScaleMethod.RigScale)
            {
                ApplyRigScale();
            }
            else
            {
                RestoreRigScale();
            }

            if (logDiagnostics)
            {
                LogDiagnostics();
            }
        }

        /// <summary>
        /// URP kamerayı kurmadan hemen önceki tek nokta. <see cref="WorldScaleMethod.RigScale"/>'de
        /// telafi burada da tekrarlanır (OVRCameraRig anchor'ları render'dan hemen önce bir kez daha
        /// tazeliyor); <see cref="WorldScaleMethod.ViewMatrix"/>'te stereo matrisleri burada yazılır.
        /// </summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (method == WorldScaleMethod.RigScale)
            {
                ApplyRigScale();
                return;
            }

            ApplyViewMatrices(cam);
        }

        private void ApplyRigScale()
        {
            if (_rig == null)
            {
                return;
            }

            Transform space = _rig.trackingSpace;
            Transform head = _rig.centerEyeAnchor;
            if (space == null || head == null)
            {
                return;
            }

            CaptureBase(space);

            float m = Magnification;
            float scale = 1f / m;

            Transform rigRoot = _rig.transform;
            if (!Mathf.Approximately(rigRoot.localScale.x, scale))
            {
                rigRoot.localScale = new Vector3(scale, scale, scale);
            }

            // Kafanın dünya konumu = scale * (space + head). Bunun ölçeksiz hâle (base + head) eşit
            // kalması için space'i m ile açarız; kalan tek fark göz ayrımı ve gövde oranıdır.
            Vector3 headLocal = head.localPosition;
            space.localPosition = (_trackingSpaceBase + headLocal) * m - headLocal;
            _rigScaleApplied = true;
        }

        /// <summary>
        /// İki gözün view matrisini yeniden yazar: göz konumu kafa merkezine <c>1/m</c> oranında
        /// çekilir. <c>V' = V · T(p - p')</c> — rotasyon ve projeksiyon aynı kalır, yalnız göz
        /// konumu değişir.
        ///
        /// <para>⚠️ Önce <c>ResetStereoViewMatrices</c> çağrılır: yoksa bir sonraki kare kendi
        /// yazdığımız matrisi ham değer sanıp üstüne bir daha daraltır ve ayrım kare kare sıfıra
        /// doğru çöker.</para>
        /// </summary>
        private void ApplyViewMatrices(Camera cam)
        {
            if (_headCamera == null)
            {
                CacheHeadCamera();
            }

            if (cam == null || cam != _headCamera || !cam.stereoEnabled)
            {
                return;
            }

            cam.ResetStereoViewMatrices();

            Matrix4x4 leftView = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 rightView = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right);

            Vector3 leftEye = leftView.inverse.GetColumn(3);
            Vector3 rightEye = rightView.inverse.GetColumn(3);
            _rawSeparation = Vector3.Distance(leftEye, rightEye);

            float m = Magnification;
            if (Mathf.Approximately(m, MinMagnification))
            {
                _appliedSeparation = _rawSeparation;
                _viewMatrixApplied = false;
                return;
            }

            float scale = 1f / m;
            Vector3 center = (leftEye + rightEye) * 0.5f;

            cam.SetStereoViewMatrix(
                Camera.StereoscopicEye.Left,
                leftView * Matrix4x4.Translate((leftEye - center) * (1f - scale)));
            cam.SetStereoViewMatrix(
                Camera.StereoscopicEye.Right,
                rightView * Matrix4x4.Translate((rightEye - center) * (1f - scale)));

            Vector3 newLeft = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            Vector3 newRight = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            _appliedSeparation = Vector3.Distance(newLeft, newRight);
            _viewMatrixApplied = true;
        }

        private void CaptureBase(Transform space)
        {
            if (_baseCaptured)
            {
                return;
            }

            _trackingSpaceBase = space.localPosition;
            _baseCaptured = true;
        }

        private void CacheHeadCamera()
        {
            if (_rig != null && _rig.centerEyeAnchor != null)
            {
                _headCamera = _rig.centerEyeAnchor.GetComponent<Camera>();
            }
        }

        private void RestoreRigScale()
        {
            if (!_rigScaleApplied || _rig == null)
            {
                return;
            }

            _rig.transform.localScale = Vector3.one;
            if (_baseCaptured && _rig.trackingSpace != null)
            {
                _rig.trackingSpace.localPosition = _trackingSpaceBase;
            }

            _rigScaleApplied = false;
        }

        private void RestoreViewMatrices()
        {
            if (!_viewMatrixApplied || _headCamera == null)
            {
                return;
            }

            _headCamera.ResetStereoViewMatrices();
            _viewMatrixApplied = false;
        }

        private float Magnification => Mathf.Clamp(magnification, MinMagnification, MaxMagnification);

        /// <summary>
        /// Denemenin işe yarayıp yaramadığının tek kanıtı ölçülen göz ayrımıdır.
        /// <see cref="WorldScaleMethod.ViewMatrix"/>'te <c>ham</c> ve <c>yazılan</c> ayrı yazılır:
        /// ikisi farklıysa override tuttu; gözlükte buna rağmen fark yoksa URP kameranın stereo
        /// matrisini kullanmıyor demektir.
        /// </summary>
        private void LogDiagnostics()
        {
            if (Time.unscaledTime < _nextDiagnosticTime)
            {
                return;
            }

            _nextDiagnosticTime = Time.unscaledTime + 1f;

            if (_headCamera == null)
            {
                CacheHeadCamera();
            }

            if (_headCamera == null)
            {
                Debug.Log("[WorldScaleTuner] CenterEyeAnchor kamerası bulunamadı.");
                return;
            }

            if (!_headCamera.stereoEnabled)
            {
                Debug.Log($"[WorldScaleTuner] stereo KAPALI (XR aktif değil) — yöntem={method}");
                return;
            }

            if (method == WorldScaleMethod.ViewMatrix)
            {
                Debug.Log(
                    $"[WorldScaleTuner] {method} magnification={Magnification:F2} " +
                    $"ham={_rawSeparation * 1000f:F1}mm yazılan={_appliedSeparation * 1000f:F1}mm");
                return;
            }

            Vector3 leftEye = _headCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            Vector3 rightEye = _headCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);

            Debug.Log(
                $"[WorldScaleTuner] {method} magnification={Magnification:F2} " +
                $"rigScale={_rig.transform.localScale.x:F3} " +
                $"gözAyrımı={Vector3.Distance(leftEye, rightEye) * 1000f:F1}mm");
        }

        private void ReadLiveTuning()
        {
            float stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x;
            if (Mathf.Abs(stick) > ThumbstickDeadzone)
            {
                magnification = Mathf.Clamp(
                    magnification + stick * TuningStepPerSecond * Time.unscaledDeltaTime,
                    MinMagnification,
                    MaxMagnification);
            }

            if (OVRInput.GetDown(OVRInput.Button.Two))
            {
                magnification = MinMagnification;
            }

            if (float.IsNaN(_loggedMagnification) || Mathf.Abs(magnification - _loggedMagnification) >= LogThreshold)
            {
                _loggedMagnification = magnification;
                Debug.Log($"[WorldScaleTuner] magnification = {magnification:F2}");
            }
        }
    }
}
