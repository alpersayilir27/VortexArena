using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Meta Building Blocks'un kamera rigine BİRDEN FAZLA yerde (hem
    /// <c>[BuildingBlock] Controller Tracking Left/Right</c> hem de ayrı bir
    /// <c>[BuildingBlock] OVRComprehensiveInteractionRig</c> altında) kendi GÖRSEL
    /// temsillerini yerleştirdiği doğrulandı: fiziksel Touch controller 3D modeli
    /// (<c>questController_animrig</c>, İKİ farklı dalda tekrarlı) ve "controller-driven"
    /// sentetik/şeffaf el overlay'i (<c>HandVisual</c> bileşenli <c>OVRHandVisualLeft/Right</c>
    /// + distance-grab önizlemesi için ikinci bir kopya, <c>OVRLeftHandVisual</c>/
    /// <c>OVRRightHandVisual</c>). Proje kendi eldiven/el modelini (<c>PlayerBodyAvatar</c>,
    /// tamamen ayrı bir hiyerarşi) kullandığı için bunlardan HİÇBİRİ istenmiyor.
    /// <para>
    /// <b>Neden tüm kamera rigini tarıyoruz, tek tek anchor değil:</b> önceki sürüm yalnız
    /// <c>LeftControllerAnchor</c>/<c>RightControllerAnchor</c> altına bakıyordu — bu,
    /// "Controller Tracking" bloğunun kopyasını buluyordu ama "OVRComprehensiveInteractionRig"
    /// altındaki AYRI kopyayı (ve el görsellerini) KAÇIRIYORDU. İsim eşleşmesi ("questController_
    /// animrig" / "HandVisual") RAY/RETİKÜL gibi tutulması gereken etkileşim görsellerine
    /// (ör. "ReticleLineHand") dokunmayacak kadar SPESİFİK.
    /// </para>
    /// <para>
    /// Girdi <c>OVRInput</c>'tan okunduğu için tamamen kozmetiktir. Her karede taranır
    /// (LateUpdate): bu görseller kontrolcü bırakılıp tutulduğunda Meta tarafından yeniden
    /// AKTİFLEŞTİRİLİYOR — tek seferlik bir gizleme kalıcı olmuyor.
    /// </para>
    /// </summary>
    public class ControllerModelHider : MonoBehaviour
    {
        [Tooltip("Tüm kontrolcü/el görsellerinin ortak atası.")]
        [SerializeField] private string rigRootName = "[BuildingBlock] Camera Rig";
        [Tooltip("Bu parçalardan HERHANGİ biri adında geçen kökler gizlenir.")]
        [SerializeField] private string[] modelNameContains = { "questController_animrig", "HandVisual" };

        private Transform rigRoot;
        private readonly List<Transform> scanBuffer = new List<Transform>(256);
        private readonly HashSet<Transform> hidden = new HashSet<Transform>();

        private void LateUpdate()
        {
            if (rigRoot == null)
            {
                GameObject go = GameObject.Find(rigRootName);
                if (go == null)
                {
                    return; // rig henüz sahnede değil — sonraki karede tekrar denenir
                }

                rigRoot = go.transform;
            }

            rigRoot.GetComponentsInChildren(true, scanBuffer);
            for (int i = 0; i < scanBuffer.Count; i++)
            {
                Transform t = scanBuffer[i];
                if (!t.gameObject.activeSelf || hidden.Contains(t))
                {
                    continue;
                }

                if (!MatchesAny(t.name))
                {
                    continue;
                }

                t.gameObject.SetActive(false);
                hidden.Add(t);
                string parentName = t.parent != null ? t.parent.name : "(kök)";
                Debug.Log($"[ControllerModelHider] Gizlendi: '{t.name}' ({parentName} altında).", this);
            }
        }

        private bool MatchesAny(string name)
        {
            for (int i = 0; i < modelNameContains.Length; i++)
            {
                if (name.IndexOf(modelNameContains[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
