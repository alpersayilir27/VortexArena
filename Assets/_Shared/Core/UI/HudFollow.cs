using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// HUD panelini kafaya KİLİTLEMEDEN "tembel takip" ettirir: panel oyuncunun önünde,
    /// göz hizasının biraz altında durur; kafa küçük açılarla dönerken YERİNDE kalır,
    /// ölü bölge aşılınca yumuşak biçimde yeni yöne kayar.
    ///
    /// Neden: Meta tasarım kılavuzu head-locked HUD'ı açıkça önermez
    /// ("Avoid locking HUD style content to the user's head movements. Anchor information
    /// and digital content to a space, or loosely follow the user using smoothing animation"
    /// — developers.meta.com/horizon/design/mr-design-guideline). Free-roam PvP'de kafaya
    /// yapışık panel hem yorucu hem de nişan alırken görüşü kapatır.
    /// Mesafe ~1 m + hafif aşağı ofset de aynı kılavuzun obje yerleşimi önerisidir.
    /// </summary>
    public class HudFollow : MonoBehaviour
    {
        [Header("Takip")]
        [Tooltip("Kafa transformu (CenterEyeAnchor). Boşsa Camera.main kullanılır.")]
        [SerializeField] private Transform head;
        [Tooltip("Panelin kafadan yatay uzaklığı (m).")]
        [SerializeField] private float distance = 1.1f;
        [Tooltip("Göz hizasına göre dikey ofset (m; negatif = aşağıda).")]
        [SerializeField] private float verticalOffset = -0.32f;
        [Tooltip("Konum yumuşatma süresi (s).")]
        [SerializeField] private float positionSmoothTime = 0.35f;
        [Tooltip("Bu açıdan küçük kafa dönüşlerinde panel yerinde kalır (derece).")]
        [SerializeField] private float yawDeadZoneDegrees = 18f;
        [Tooltip("Ölü bölge aşılınca yöne oturma hızı (derece/sn ölçeği).")]
        [SerializeField] private float yawSmoothTime = 0.35f;

        private Vector3 _positionVelocity;
        private float _yawVelocity;
        private float _currentYaw;
        private float _targetYaw;
        private bool _initialized;

        private void OnEnable()
        {
            _initialized = false; // sahneye/ölüm ekranına ilk girişte panel doğrudan yerine otursun
        }

        private void LateUpdate()
        {
            Transform reference = ResolveHead();
            if (reference == null)
            {
                return;
            }

            float headYaw = reference.eulerAngles.y;

            if (!_initialized)
            {
                _currentYaw = headYaw;
                _targetYaw = headYaw;
                transform.position = TargetPosition(reference, _currentYaw);
                transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
                _positionVelocity = Vector3.zero;
                _yawVelocity = 0f;
                _initialized = true;
                return;
            }

            // Ölü bölge: kafa yeterince dönmedikçe panelin hedef yönü değişmez.
            if (Mathf.Abs(Mathf.DeltaAngle(_targetYaw, headYaw)) > yawDeadZoneDegrees)
            {
                _targetYaw = headYaw;
            }

            _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVelocity, yawSmoothTime);
            transform.position = Vector3.SmoothDamp(transform.position, TargetPosition(reference, _currentYaw),
                ref _positionVelocity, positionSmoothTime);
            // Panel kullanıcıya bakar; eğim yok (yalnız yaw) — okunurluk için billboard davranışı.
            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        }

        private Vector3 TargetPosition(Transform reference, float yaw)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return reference.position + forward * distance + Vector3.up * verticalOffset;
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
