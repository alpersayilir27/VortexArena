using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Runs <see cref="GrabArbiter.Resolve"/> once a frame, after every
    /// <see cref="NetObjectGrabBridge"/> has offered its press.
    /// <para>⚠️ <b>The execution order is the point.</b> The claimants offer from <c>LateUpdate</c> at
    /// the default order and <c>HandGripPoser</c> locks the hand at 100; resolving in between grabs the
    /// winner while the poser can still see it in the same frame. Outside that window the grab lands a
    /// frame late and the object visibly jumps into the hand.</para>
    /// <para>⚠️ <b>40, not 50:</b> <c>BurgerCarrier</c> sits at 50, and two components sharing an order
    /// value run in an undefined sequence — a carrier that sees the hand before the grab is committed
    /// places its cargo against last frame's answer.</para>
    /// <para>It is in no scene: grabbable objects exist in every arena, so the pump bootstraps itself
    /// (the <c>ModeRuntimePump</c> pattern) instead of becoming one more thing to remember per scene.</para>
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class GrabArbiterPump : MonoBehaviour
    {
        private static GrabArbiterPump _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[GrabArbiterPump]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GrabArbiterPump>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Claims can linger from the previous session while domain reload is off.
            GrabArbiter.Reset();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            GrabArbiter.Reset();
            _instance = null;
        }

        private void LateUpdate()
        {
            GrabArbiter.Resolve();
        }
    }
}
