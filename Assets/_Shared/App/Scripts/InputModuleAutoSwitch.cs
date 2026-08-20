using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace VortexArena.App
{
    /// <summary>
    /// Picks the active input module on the scene's <see cref="EventSystem"/>: ISDK's
    /// <c>PointableCanvasModule</c> while an XR device is active, <c>InputSystemUIInputModule</c>
    /// (mouse) otherwise. Both sit on the same object; <b>exactly one</b> is enabled.
    ///
    /// <para>
    /// <b>Why exactly one:</b> <see cref="EventSystem"/> processes the <b>FIRST</b> module that
    /// says <c>ShouldActivateModule()</c>. With both enabled the winner depends on OnEnable order,
    /// which can silently change when the scene is re-serialized.
    /// </para>
    /// <para>
    /// <b>The criterion is the active XR device, NOT the platform.</b>
    /// <c>RuntimePlatform.Android</c> is always false in the editor, so over Quest Link the
    /// controller ray would never reach the UI. <see cref="XRSettings.isDeviceActive"/> covers Link,
    /// the simulator and on-device builds. Android is still asked separately: there is no mouse
    /// there even if XR failed to start.
    /// </para>
    /// <para>
    /// Polled every frame because Link can connect/drop mid-session and the XR stack may be late;
    /// the write happens only when the decision changes.
    /// </para>
    /// <para>
    /// ⚠️ <b>Disable the loser first, then enable the winner.</b> ISDK's
    /// <c>PointableCanvasModule</c> runs "exclusive mode" and disables sibling modules in
    /// <c>UpdateModule()</c> — in the reverse order the desktop module would be killed in the frame
    /// it was enabled and the mouse would die.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)] // decide before EventSystem picks its first module
    public class InputModuleAutoSwitch : MonoBehaviour
    {
        [Tooltip("XR aygıtı etkinken kullanılacak modül (ISDK PointableCanvasModule).")]
        [SerializeField] private BaseInputModule vrModule;

        [Tooltip("XR aygıtı yokken kullanılacak modül (InputSystemUIInputModule — fare).")]
        [SerializeField] private BaseInputModule desktopModule;

        /// <summary>The applied decision; <c>null</c> = never applied yet.</summary>
        private bool? _vrActive;

        private void Awake()
        {
            Apply(ShouldUseVr());
        }

        private void Update()
        {
            Apply(ShouldUseVr());
        }

        private static bool ShouldUseVr()
        {
            return XRSettings.isDeviceActive || Application.platform == RuntimePlatform.Android;
        }

        private void Apply(bool vr)
        {
            if (_vrActive.HasValue && _vrActive.Value == vr)
            {
                return; // decision unchanged — leave the modules alone
            }

            _vrActive = vr;

            // Order matters (see the class note): the loser is silenced first.
            BaseInputModule loser = vr ? desktopModule : vrModule;
            BaseInputModule winner = vr ? vrModule : desktopModule;

            if (loser != null)
            {
                loser.enabled = false;
            }

            if (winner != null)
            {
                winner.enabled = true;
            }

            if (winner == null)
            {
                // Do not fail silently: an unwired module leaves the UI unclickable with no visible cause.
                Debug.LogError($"[InputModuleAutoSwitch] {(vr ? "vrModule" : "desktopModule")} " +
                               "bağlanmamış — bu sahnede arayüz tıklanamaz.", this);
                return;
            }

            Debug.Log($"[InputModuleAutoSwitch] Girdi modülü: {winner.GetType().Name} " +
                      $"(XR aygıtı {(XRSettings.isDeviceActive ? "etkin" : "yok")}).", this);
        }
    }
}
