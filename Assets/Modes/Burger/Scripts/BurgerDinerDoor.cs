using UnityEngine;

namespace VortexArena.Modes.Burger
{
    /// <summary>Swinging diner door: both leaves swing into the room while a customer is near and
    /// close after. Look only, evaluated on every headset from the customers it already draws — no
    /// network traffic, no collider (customers are kinematic path walkers).</summary>
    [DisallowMultipleComponent]
    public sealed class BurgerDinerDoor : MonoBehaviour
    {
        [Tooltip("Sol kanat (menteşe pivotlu). Açılınca +açı ile döner.")]
        [SerializeField] private Transform leftLeaf;

        [Tooltip("Sağ kanat (menteşe pivotlu). Açılınca −açı ile döner.")]
        [SerializeField] private Transform rightLeaf;

        [Tooltip("Müşteri kapı merkezine bu mesafeden (m) yakınsa kapı açık durur.")]
        [SerializeField] private float openRadius = 1.6f;

        [Tooltip("Tam açıklık açısı (derece).")]
        [SerializeField] private float openAngle = 80f;

        [Tooltip("Açılma/kapanma hızı (derece/sn).")]
        [SerializeField] private float swingSpeed = 240f;

        [Tooltip("Kanat açılırken / kapanırken çalan tek seferlik ses (isteğe bağlı).")]
        [SerializeField] private AudioSource swingSound;

        private Quaternion _leftClosed;
        private Quaternion _rightClosed;
        private float _angle;
        private bool _wasOpen;

        private void Awake()
        {
            if (leftLeaf != null) _leftClosed = leftLeaf.localRotation;
            if (rightLeaf != null) _rightClosed = rightLeaf.localRotation;
        }

        private void Update()
        {
            bool open = AnyCustomerNear();
            if (open != _wasOpen && swingSound != null)
            {
                swingSound.Play();
            }

            _wasOpen = open;
            float target = open ? openAngle : 0f;
            if (Mathf.Approximately(_angle, target))
            {
                return;
            }

            _angle = Mathf.MoveTowards(_angle, target, swingSpeed * Time.deltaTime);
            if (leftLeaf != null) leftLeaf.localRotation = _leftClosed * Quaternion.Euler(0f, _angle, 0f);
            if (rightLeaf != null) rightLeaf.localRotation = _rightClosed * Quaternion.Euler(0f, -_angle, 0f);
        }

        private bool AnyCustomerNear()
        {
            Vector3 c = transform.position;
            float r2 = openRadius * openRadius;
            var all = BurgerCustomer.All;
            for (int i = 0; i < all.Count; i++)
            {
                Vector3 d = all[i].transform.position - c;
                d.y = 0f;
                if (d.sqrMagnitude < r2)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
