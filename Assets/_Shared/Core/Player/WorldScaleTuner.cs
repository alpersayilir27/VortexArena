using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// <b>Ölçek denemesi</b> — dünyanın kaç kat büyük hissedildiğini gözler arası mesafeden ayarlar.
    ///
    /// <para>VR'da büyüklük algısının kadranı FOV değil <b>IPD</b>'dir: iki gözün ayrımı daraldıkça
    /// beyin sahneyi orantılı olarak devleştirir. Göz ayrımı OpenXR runtime'ından geldiği için
    /// doğrudan yazılamaz; dokunulabilen tek yer rig'in ölçeğidir. Rig <c>s = 1 / magnification</c>
    /// ile küçültülür, gözler de aynı oranda birbirine yaklaşır.</para>
    ///
    /// <para>⚠️ Rig'i küçültmek <b>yürüyüşü de</b> küçültürdü (fiziksel 1 m sanalda <c>s</c> m olurdu)
    /// ve free-roam'da bu arena sınırını da ağa giden pozu da kaydırırdı. Bu yüzden bileşen her karede
    /// <c>TrackingSpace</c>'i ters yönde kaydırıp <b>kafanın dünya konumunu 1:1 sabit tutar</b>:
    /// küçülen tek şey göz ayrımı ve gövde oranıdır, konum değil. Kafa yüksekliği de sabit kaldığı
    /// için boy ölçümü (<see cref="BodyScaleState"/>) etkilenmez.</para>
    ///
    /// <para>Kabul edilen bedel: eller kafaya <c>s</c> oranında yaklaşır — "küçüldüm" hissinin
    /// parçasıdır, ama ağa giden el pozları da aynı oranda sıkışır.</para>
    ///
    /// <para><c>magnification = 1</c> bileşeni tamamen etkisiz kılar; bileşen kapatıldığında rig
    /// ölçeği ve tracking offset'i eski değerine döner.</para>
    /// </summary>
    [DefaultExecutionOrder(32000)]
    [RequireComponent(typeof(OVRCameraRig))]
    public class WorldScaleTuner : MonoBehaviour
    {
        [Tooltip("Dünya kaç kat büyük hissedilsin. 1 = kapalı (gerçek ölçek), 1.15 = %15 daha dev.")]
        [SerializeField] [Range(MinMagnification, MaxMagnification)] private float magnification = 1.15f;

        [Tooltip("Başlıktayken sağ kumandanın çubuğuyla canlı ayar (sağ = büyüt, sol = küçült), B ile sıfırlama.")]
        [SerializeField] private bool liveTuning = true;

        private const float MinMagnification = 1f;
        private const float MaxMagnification = 2f;

        /// <summary>Çubuk tam yatırıkken saniyede bu kadar büyütme değişir.</summary>
        private const float TuningStepPerSecond = 0.2f;

        private const float ThumbstickDeadzone = 0.5f;

        /// <summary>Log gürültüsü olmasın diye: değer bu kadar sapmadan yeniden yazılmaz.</summary>
        private const float LogThreshold = 0.01f;

        private OVRCameraRig _rig;
        private Vector3 _trackingSpaceBase;
        private bool _baseCaptured;
        private float _loggedMagnification = float.NaN;

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
            Restore();
        }

        private void LateUpdate()
        {
            if (liveTuning)
            {
                ReadLiveTuning();
            }

            Apply();
        }

        /// <summary>
        /// Rig'in anchor'ları render'dan hemen önce bir kez daha tazeleniyor (OVRCameraRig kendini
        /// <c>onBeforeRender</c>'a bağlar), o yüzden telafi burada da tekrarlanır — yoksa hızlı kafa
        /// hareketinde bir kare boyunca konum <c>s</c> oranında kayar.
        /// </summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            Apply();
        }

        private void Apply()
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

            if (!_baseCaptured)
            {
                _trackingSpaceBase = space.localPosition;
                _baseCaptured = true;
            }

            float m = Mathf.Clamp(magnification, MinMagnification, MaxMagnification);
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
        }

        private void Restore()
        {
            if (_rig == null || !_baseCaptured)
            {
                return;
            }

            _rig.transform.localScale = Vector3.one;
            if (_rig.trackingSpace != null)
            {
                _rig.trackingSpace.localPosition = _trackingSpaceBase;
            }
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
                Debug.Log($"[WorldScaleTuner] magnification = {magnification:F2} (rig scale {1f / magnification:F3})");
            }
        }
    }
}
