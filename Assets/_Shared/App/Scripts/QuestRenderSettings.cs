using UnityEngine;

namespace VortexArena.App
{
    /// <summary>Quest GPU settings the XR runtime leaves off by default (foveated rendering).</summary>
    public static class QuestRenderSettings
    {
        // Upper bound only: with dynamic foveation the runtime raises the level under GPU load.
        private const OVRManager.FoveatedRenderingLevel MaxFoveation = OVRManager.FoveatedRenderingLevel.High;

        private static bool _installed;

        /// <summary>Called once from AppBoot; no-op outside the Quest player build.</summary>
        public static void Install()
        {
            // Runtime check, not #if: keeps the code inside the editor compile.
            if (_installed || Application.platform != RuntimePlatform.Android)
            {
                return;
            }
            _installed = true;

            Apply();
            // ⚠️ SDK default is level 0 (off) and a call made before the XR session exists is lost:
            // re-apply every time the session regains focus.
            OVRManager.InputFocusAcquired += Apply;
            Application.focusChanged += focused =>
            {
                if (focused)
                {
                    Apply();
                }
            };
        }

        private static void Apply()
        {
            OVRManager.foveatedRenderingLevel = MaxFoveation;
            OVRManager.useDynamicFoveatedRendering = true;
            Debug.Log($"[QuestRender] Foveation üst sınırı {MaxFoveation}, dinamik.");
        }
    }
}
