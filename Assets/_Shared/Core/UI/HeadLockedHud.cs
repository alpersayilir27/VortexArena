using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Pins a HUD panel to the head: the panel keeps a FIXED spot in the field of view and follows
    /// every head movement immediately.
    ///
    /// <para><b>Deliberate exception to <see cref="HudFollow"/>.</b> The mode HUD loosely follows the
    /// head on purpose, because a head-locked panel is tiring and blocks the view while aiming. Health
    /// is the one readout the player must find without searching for it, so this single strip is
    /// locked and nothing else is. Widening the exception brings back exactly the problem
    /// <see cref="HudFollow"/> exists to avoid.</para>
    ///
    /// <para>⚠️ The object stays a <b>child of the mode HUD canvas</b> — only its world pose is
    /// overwritten. Reparenting it to the head would take it out of that canvas, and
    /// <see cref="GameplayHudGate"/> (which hides the in-game HUDs behind the match result screen)
    /// would no longer reach it: the bar would hang over the result screen.</para>
    /// </summary>
    // Runs after HudFollow: that one moves the PARENT, and a parent moved afterwards would drag this
    // panel with it for one frame — visible as jitter on every head turn.
    [DefaultExecutionOrder(200)]
    public class HeadLockedHud : MonoBehaviour
    {
        [Header("Kafa")]
        [Tooltip("Kafa transformu (CenterEyeAnchor). Boşsa Camera.main kullanılır.")]
        [SerializeField] private Transform head;

        [Header("Yerleşim")]
        [Tooltip("Panelin gözden uzaklığı (m). Açıdan bağımsızdır: panel her açıda aynı büyüklükte görünür.")]
        [SerializeField] private float distance = 1f;
        [Tooltip("Bakış ekseninden yukarı açı (derece; negatif = aşağıda).")]
        [SerializeField] private float pitchDegrees = 40f;
        [Tooltip("Bakış ekseninden sağa açı (derece; negatif = solda).")]
        [SerializeField] private float yawDegrees;

        [Header("Kilit")]
        [Tooltip("Kapalıysa panel yalnız yaw'ı izler: başını eğdiğinde panel yatay kalır.")]
        [SerializeField] private bool lockPitch = true;
        [Tooltip("Panel göze dönük durur. Kapatılırsa bakış eksenine dik kalır ve yüksek açılarda basık görünür.")]
        [SerializeField] private bool tiltToEye = true;

        private void LateUpdate()
        {
            Transform reference = ResolveHead();
            if (reference == null)
            {
                return;
            }

            Quaternion basis = lockPitch
                ? reference.rotation
                : Quaternion.Euler(0f, reference.eulerAngles.y, 0f);

            // Unity pitches DOWN on +X, so the up angle is negated.
            Quaternion aim = Quaternion.Euler(-pitchDegrees, yawDegrees, 0f);

            transform.SetPositionAndRotation(
                reference.position + basis * (aim * Vector3.forward * distance),
                tiltToEye ? basis * aim : basis);
        }

        private Transform ResolveHead()
        {
            if (head != null)
            {
                return head;
            }

            Camera main = Camera.main;
            if (main != null)
            {
                head = main.transform;
            }

            return head;
        }
    }
}
