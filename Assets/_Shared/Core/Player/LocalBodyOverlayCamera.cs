using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel oyuncunun gövdesini (bu bileşenin altındaki tüm Renderer'lar) "LocalBody"
    /// katmanına alır ve ana kameradan hariç tutar; onun yerine daha büyük near-clip'li
    /// bir overlay kamera (URP camera stack) çizer. Amaç: aşağı bakınca ana kameranın
    /// near-clip'i (tipik 0.1 m) göğüs/omuz geometrisine girip içini göstermesin — bkz.
    /// <see cref="LocalAvatarHeadHider"/> (aynı sorunun kafa için çözümü, farklı yöntemle).
    /// <para>
    /// Projede "LocalBody" adlı bir Layer bulunmalı (ProjectSettings &gt; Tags and Layers).
    /// Yalnız yerel oyuncunun kendi görüşünü etkiler — uzak oyuncular bu avatarı hiç
    /// görmez (kendi <c>RemoteAvatar</c> temsilleri ayrı bir prefab/sistemdir).
    /// </para>
    /// <para>
    /// <b>Neden Update'te yeniden dener:</b> bu avatar, VR kamerası (OVR/OpenXR)
    /// <c>MainCamera</c> olarak tam etiketlenmeden ÖNCE sahneye gelebilir — o anda
    /// <c>Camera.main</c> null döner. Tek seferlik <c>Start</c> denemesi bu durumda
    /// sessizce başarısız kalır; kamera hazır olana kadar her karede tekrar denenir,
    /// bulununca kurulum bir daha çalışmaz.
    /// </para>
    /// </summary>
    public class LocalBodyOverlayCamera : MonoBehaviour
    {
        [Tooltip("Boşsa Camera.main kullanılır.")]
        [SerializeField] private Camera baseCamera;
        [Tooltip("Gövdenin taşınacağı katman adı.")]
        [SerializeField] private string bodyLayerName = "LocalBody";
        [Tooltip("Overlay kameranın near-clip'i — ana kameradan (tipik 0.1 m) büyük olmalı.")]
        [SerializeField] private float overlayNearClip = 0.15f;

        private bool setupDone;

        private void Start()
        {
            TrySetup();
        }

        private void Update()
        {
            if (!setupDone)
            {
                TrySetup();
            }
        }

        private void TrySetup()
        {
            int bodyLayer = LayerMask.NameToLayer(bodyLayerName);
            if (bodyLayer < 0)
            {
                Debug.LogWarning($"[LocalBodyOverlayCamera] '{bodyLayerName}' katmanı yok " +
                                  "(ProjectSettings > Tags and Layers'a eklenmeli) — kurulum atlandı.", this);
                setupDone = true; // katman hiç yoksa tekrar denemenin anlamı yok
                return;
            }

            Camera cam = baseCamera != null ? baseCamera : Camera.main;
            if (cam == null)
            {
                return; // ana kamera henüz hazır değil — sonraki Update'te tekrar denenir
            }

            baseCamera = cam;
            setupDone = true;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                r.gameObject.layer = bodyLayer;
            }

            // Ana kamera bu katmanı ASLA çizmesin — yakın-klip sorunu tam da onda oluşuyor.
            baseCamera.cullingMask &= ~(1 << bodyLayer);

            var overlayGo = new GameObject("LocalBodyOverlayCamera");
            overlayGo.transform.SetParent(baseCamera.transform, false);

            var overlayCam = overlayGo.AddComponent<Camera>();
            overlayCam.CopyFrom(baseCamera);
            overlayCam.cullingMask = 1 << bodyLayer;
            overlayCam.nearClipPlane = overlayNearClip;
            overlayCam.clearFlags = CameraClearFlags.Nothing;
            overlayCam.depth = baseCamera.depth + 1;

            var overlayData = overlayGo.AddComponent<UniversalAdditionalCameraData>();
            overlayData.renderType = CameraRenderType.Overlay;

            UniversalAdditionalCameraData baseData = baseCamera.GetComponent<UniversalAdditionalCameraData>();
            if (baseData == null)
            {
                baseData = baseCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            baseData.cameraStack.Add(overlayCam);

            Debug.Log($"[LocalBodyOverlayCamera] Kuruldu: {renderers.Length} renderer '{bodyLayerName}' " +
                      $"katmanına alındı, '{baseCamera.name}' kamerasına overlay eklendi (near-clip {overlayNearClip}).", this);
        }
    }
}
