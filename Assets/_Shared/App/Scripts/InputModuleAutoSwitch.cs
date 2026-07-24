using UnityEngine;
using UnityEngine.EventSystems;

namespace VortexArena.App
{
    /// <summary>
    /// EventSystem üzerinde platforma göre input modülü seçer: Android (VR build) →
    /// ISDK PointableCanvasModule; masaüstü/Editor → InputSystemUIInputModule (fare).
    /// İkisi de aynı EventSystem objesinde durur, yalnız biri etkin kalır.
    /// </summary>
    public class InputModuleAutoSwitch : MonoBehaviour
    {
        [SerializeField] private BaseInputModule vrModule;
        [SerializeField] private BaseInputModule desktopModule;

        private void Awake()
        {
            bool vr = Application.platform == RuntimePlatform.Android;
            if (vrModule != null)
            {
                vrModule.enabled = vr;
            }

            if (desktopModule != null)
            {
                desktopModule.enabled = !vr;
            }
        }
    }
}
