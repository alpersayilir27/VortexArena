using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Press DURATION gate on a <see cref="Button"/>: a tap sends one command, a long press a
    /// heavier one. The single calibration-reset button uses it — tap voids the current alignment,
    /// holding also wipes the anchor saved on the headset (§10.6).
    /// <para>⚠️ <b>Attached at RUNTIME</b> (<see cref="Attach"/>), never placed in a prefab: the
    /// same button exists in four row/panel prefabs and a component added by hand would be
    /// forgotten in one of them — a reset that silently never wipes.</para>
    /// <para>⚠️ <b>The button's own <c>onClick</c> is CLEARED and must stay unused:</b> it fires on
    /// pointer-up whatever the duration, so a completed hold would send both commands.</para>
    /// <para>The hold IS the confirmation — that is why the reset button has no two-step window:
    /// a second of deliberate pressure is stronger friction than a second click, and stacking both
    /// would put two interaction grammars on one button.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>Hold length (s) of the destructive variant — long enough that no click reaches
        /// it by accident, short enough to stay a gesture rather than a wait.</summary>
        public const float DefaultHoldSeconds = 1f;

        private Button _button;
        private float _holdSeconds = DefaultHoldSeconds;
        private Action _onTap;
        private Action _onHold;

        private float _pressedAt = -1f;
        private bool _fired;

        /// <summary>0..1 while pressed, 0 otherwise. The caller paints it — this class owns timing
        /// only, look stays where the rest of the row's look is.</summary>
        public float HoldProgress => _pressedAt < 0f
            ? 0f
            : Mathf.Clamp01((Time.unscaledTime - _pressedAt) / _holdSeconds);

        public bool IsPressed => _pressedAt >= 0f;

        /// <summary>Wires (or re-wires) the gate onto <paramref name="button"/>. Safe to call on
        /// every rebind: pooled rows hand the same button a new target player.</summary>
        public static HoldButton Attach(Button button, Action onTap, Action onHold,
            float holdSeconds = DefaultHoldSeconds)
        {
            if (button == null)
            {
                return null;
            }

            HoldButton hold = button.GetComponent<HoldButton>();
            if (hold == null)
            {
                hold = button.gameObject.AddComponent<HoldButton>();
            }

            hold._button = button;
            hold._holdSeconds = Mathf.Max(0.05f, holdSeconds);
            hold._onTap = onTap;
            hold._onHold = onHold;
            hold.Cancel();

            button.onClick.RemoveAllListeners();
            return hold;
        }

        /// <summary>Drops a press in flight — called on rebind and whenever the row is hidden, so a
        /// half-finished hold cannot land on the next player bound to the pooled row.</summary>
        public void Cancel()
        {
            _pressedAt = -1f;
            _fired = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button != null && !_button.interactable)
            {
                return;
            }

            _pressedAt = Time.unscaledTime;
            _fired = false;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_pressedAt < 0f)
            {
                return;
            }

            bool fired = _fired;
            Cancel();
            if (!fired)
            {
                _onTap?.Invoke();
            }
        }

        /// <summary>Sliding off the button cancels: the escape hatch for a hold started by
        /// mistake, and the reason the hold is the confirmation.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            Cancel();
        }

        private void Update()
        {
            if (_pressedAt < 0f || _fired)
            {
                return;
            }

            if (_button != null && !_button.interactable)
            {
                Cancel();
                return;
            }

            if (Time.unscaledTime - _pressedAt < _holdSeconds)
            {
                return;
            }

            // Fires AT the threshold, not on release: the operator must see the result while still
            // pressing, otherwise "did it take?" is answered only by letting go.
            _fired = true;
            _onHold?.Invoke();
        }

        private void OnDisable()
        {
            Cancel();
        }
    }
}
