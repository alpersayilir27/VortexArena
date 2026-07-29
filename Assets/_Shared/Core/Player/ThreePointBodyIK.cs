using Meta.XR.Movement.Retargeting.IK;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Üç noktalı pozdan (kafa + iki el) humanoid bir iskeleti çözer — uzak oyuncu
    /// avatarlarının sürücüsü.
    /// <para>
    /// <b>Neden IK gerekiyor:</b> ağdan gelen tek şey kafa ve iki elin pozu (§ <c>pose</c>
    /// mesajı, 92 B). Gövde, kollar ve bacaklar bu üç noktadan TÜRETİLİR; ağ tarafına tek bayt
    /// eklenmez. Dirsek/omuz yönü bir tahmindir — gerçek body tracking değildir.
    /// </para>
    /// <para>
    /// <b>Kemikler isimle DEĞİL <see cref="Animator.GetBoneTransform"/> ile bulunur:</b> karakter
    /// humanoid rig'e sahip olduğu için eşleme Avatar'da yaşar; model değiştiğinde (Mixamo'dan
    /// başka bir karaktere geçilse bile) bu bileşende tek satır değişmez ve prefabda elle
    /// bağlanacak Transform kalmaz.
    /// </para>
    /// <para>
    /// <b>Bacaklar tamamen prosedüreldir</b> (adım döngüsü + ayak IK): projede yürüme animasyon
    /// klibi yoktur ve free-roam'da oyuncu fiziksel yürüdüğü için kök hareketi zaten gerçektir —
    /// tek eksik ayakların nereye basacağıdır.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ThreePointBodyIK : MonoBehaviour
    {
        [Header("Gövde")]
        [Tooltip("Kalçanın kafa merkezinden aşağı ofseti (metre).")]
        [SerializeField] private float hipsDropMeters = 0.72f;

        [Tooltip("Kalçanın kafa yönünün GERİSİNDE durma payı — öne eğilince gövde yatar.")]
        [SerializeField] private float hipsBackOffsetMeters = 0.06f;

        [Tooltip("Gövdenin kafa yaw'ını takip hızı (derece/sn). Düşük değer omuz-kafa farkı bırakır.")]
        [SerializeField] private float torsoYawFollowSpeed = 360f;

        [Tooltip("Gövdenin kafaya göre en fazla sapabileceği açı — bu aşılırsa gövde anında yetişir.")]
        [SerializeField] private float torsoMaxYawLagDegrees = 70f;

        [Header("Kollar")]
        [Tooltip("El hedefine kabul edilen yakınlık (metre).")]
        [SerializeField] private float armTolerance = 0.01f;

        [Tooltip("Kol IK yineleme sayısı.")]
        [SerializeField] private int armIterations = 8;

        [Header("Bacaklar")]
        [Tooltip("İki ayağın duruş genişliğinin yarısı (metre).")]
        [SerializeField] private float stanceHalfWidth = 0.11f;

        [Tooltip("Ayak, olması gereken yerden bu kadar uzaklaşınca adım başlar (metre).")]
        [SerializeField] private float stepTriggerDistance = 0.28f;

        [Tooltip("Bir adımın süresi (saniye).")]
        [SerializeField] private float stepDuration = 0.22f;

        [Tooltip("Adım yayının tepe yüksekliği (metre).")]
        [SerializeField] private float stepArcHeight = 0.09f;

        private Animator _animator;
        private bool _bonesResolved;

        // Gövde
        private Transform _hips;
        private Transform _spine;
        private Transform _chest;
        private Transform _upperChest;
        private Transform _neck;
        private Transform _head;

        // Kollar — CCD zincirleri her karede yeniden ayrılmasın diye alanda tutulur.
        private Transform[] _leftArmChain;
        private Transform[] _rightArmChain;
        private Transform _leftHand;
        private Transform _rightHand;

        // Bacaklar
        private Transform[] _leftLegChain;
        private Transform[] _rightLegChain;

        private Quaternion _torsoYaw = Quaternion.identity;
        private bool _yawInitialized;

        /// <summary>Ayağın şu an bastığı (dünya) nokta.</summary>
        private Vector3 _leftFootPlanted;
        private Vector3 _rightFootPlanted;

        /// <summary>Adım başlangıç noktası ve ilerleme [0..1]; 1 = adım bitti.</summary>
        private Vector3 _leftFootFrom;
        private Vector3 _rightFootFrom;
        private float _leftStepProgress = 1f;
        private float _rightStepProgress = 1f;

        private bool _feetInitialized;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            ResolveBones();
        }

        /// <summary>
        /// Bir karelik çözüm. Pozlar DÜNYA uzayındadır (çağıran arena→dünya dönüşümünü yapmıştır).
        /// <see cref="LateUpdate"/> yerine dışarıdan çağrılır: sürücü <c>RemoteAvatar</c>'dır ve
        /// pozu ancak kayıt defterinden okuduktan sonra verebilir.
        /// </summary>
        public void Solve(in Pose head, in Pose handL, in Pose handR)
        {
            if (!_bonesResolved || _hips == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Quaternion yaw = SolveTorsoYaw(head.rotation, deltaTime);

            PlaceRoot(head.position, yaw);
            PlaceTorso(head, yaw);
            SolveArm(_leftArmChain, _leftHand, handL);
            SolveArm(_rightArmChain, _rightHand, handR);
            SolveLegs(yaw, deltaTime);
        }

        // ------------------------------------------------------------------- gövde

        /// <summary>
        /// Gövde yaw'ı kafayı GECİKMELİ takip eder (anında yapışsaydı avatar her bakışta
        /// bütün gövdesiyle dönerdi). Fark <see cref="torsoMaxYawLagDegrees"/>'i aşınca gövde
        /// yetişir — sırtı dönük duran bir avatar oluşmasın diye.
        /// </summary>
        private Quaternion SolveTorsoYaw(Quaternion headRotation, float deltaTime)
        {
            Vector3 forward = headRotation * Vector3.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-6f)
            {
                // Kafa tam yukarı/aşağı bakıyor — yaw'ı kafanın tepesinden türet.
                forward = headRotation * Vector3.up;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-6f)
                {
                    return _torsoYaw;
                }
            }

            Quaternion target = Quaternion.LookRotation(forward.normalized, Vector3.up);

            if (!_yawInitialized)
            {
                _yawInitialized = true;
                _torsoYaw = target;
                return _torsoYaw;
            }

            _torsoYaw = Quaternion.RotateTowards(_torsoYaw, target, torsoYawFollowSpeed * deltaTime);

            float lag = Quaternion.Angle(_torsoYaw, target);
            if (lag > torsoMaxYawLagDegrees)
            {
                _torsoYaw = Quaternion.RotateTowards(_torsoYaw, target, lag - torsoMaxYawLagDegrees);
            }

            return _torsoYaw;
        }

        /// <summary>Kökü zemine oturtur: yatay konum kafadan, yükseklik ARENA ZEMİNİNDEN gelir.
        /// <para>Zemin kafadan sabit bir boy çıkararak bulunmaz — oyuncu eğildiğinde avatar yere
        /// gömülürdü.</para></summary>
        private void PlaceRoot(Vector3 headPosition, Quaternion yaw)
        {
            float groundY = ArenaSpace.HasOrigin
                ? ArenaSpace.ArenaToWorld(Vector3.zero).y
                : 0f;

            transform.SetPositionAndRotation(
                new Vector3(headPosition.x, groundY, headPosition.z), yaw);
        }

        /// <summary>
        /// Kalça + omurga + kafa. Omurga eğimi kalçadan kafaya giden vektörden gelir ve zincire
        /// PAYLAŞTIRILIR (tek kemiğe verilseydi bel kırılır gibi görünürdü).
        /// </summary>
        private void PlaceTorso(in Pose head, Quaternion yaw)
        {
            Vector3 hipsPosition = head.position
                                   - Vector3.up * hipsDropMeters
                                   - (yaw * Vector3.forward) * hipsBackOffsetMeters;

            _hips.SetPositionAndRotation(hipsPosition, yaw);

            Vector3 spineDirection = head.position - hipsPosition;
            if (spineDirection.sqrMagnitude > 1e-6f)
            {
                Quaternion lean = Quaternion.FromToRotation(Vector3.up, spineDirection.normalized);

                // Eğim omurga kemiklerine eşit paylaştırılır; kaç kemik olduğu karaktere göre değişir.
                int segments = 0;
                if (_spine != null) segments++;
                if (_chest != null) segments++;
                if (_upperChest != null) segments++;

                if (segments > 0)
                {
                    Quaternion share = Quaternion.Slerp(Quaternion.identity, lean, 1f / segments);
                    ApplyLean(_spine, share);
                    ApplyLean(_chest, share);
                    ApplyLean(_upperChest, share);
                }
            }

            if (_neck != null)
            {
                // Boyun kafayı taşır ama kafanın kendi rotasyonu aşağıda birebir yazılır.
                _neck.rotation = Quaternion.Slerp(_neck.rotation, head.rotation, 0.5f);
            }

            if (_head != null)
            {
                _head.SetPositionAndRotation(head.position, head.rotation);
            }
        }

        private static void ApplyLean(Transform bone, Quaternion share)
        {
            if (bone != null)
            {
                bone.rotation = share * bone.rotation;
            }
        }

        // -------------------------------------------------------------------- kol

        /// <summary>
        /// Kolu ele ulaştırır (CCD), sonra elin KENDİ rotasyonunu birebir yazar: silah tutuşu
        /// elin yönünden okunuyor, çözücünün bulduğu yönelim yeterli değil.
        /// </summary>
        private void SolveArm(Transform[] chain, Transform hand, in Pose handPose)
        {
            if (chain == null || hand == null)
            {
                return;
            }

            IKUtilities.SolveCCDIK(chain, handPose.position, armTolerance, armIterations);
            hand.SetPositionAndRotation(handPose.position, handPose.rotation);
        }

        // ----------------------------------------------------------------- bacaklar

        /// <summary>
        /// Prosedürel adım: her ayağın bastığı nokta saklanır; gövde yeterince uzaklaşınca o ayak
        /// yeni yerine bir yay çizerek gider. AYNI ANDA iki ayak birden adım atmaz — atsaydı
        /// avatar zıplıyormuş gibi görünürdü.
        /// </summary>
        private void SolveLegs(Quaternion yaw, float deltaTime)
        {
            if (_leftLegChain == null || _rightLegChain == null)
            {
                return;
            }

            Vector3 root = transform.position;
            Vector3 right = yaw * Vector3.right;
            Vector3 leftTarget = root - right * stanceHalfWidth;
            Vector3 rightTarget = root + right * stanceHalfWidth;

            if (!_feetInitialized)
            {
                _feetInitialized = true;
                _leftFootPlanted = _leftFootFrom = leftTarget;
                _rightFootPlanted = _rightFootFrom = rightTarget;
            }

            bool leftStepping = _leftStepProgress < 1f;
            bool rightStepping = _rightStepProgress < 1f;

            // Adım başlatma — sırayla, hiç değilse bir ayak yerde kalsın.
            if (!leftStepping && !rightStepping)
            {
                float leftError = Vector3.Distance(_leftFootPlanted, leftTarget);
                float rightError = Vector3.Distance(_rightFootPlanted, rightTarget);

                if (leftError > stepTriggerDistance && leftError >= rightError)
                {
                    _leftFootFrom = _leftFootPlanted;
                    _leftStepProgress = 0f;
                    leftStepping = true;
                }
                else if (rightError > stepTriggerDistance)
                {
                    _rightFootFrom = _rightFootPlanted;
                    _rightStepProgress = 0f;
                    rightStepping = true;
                }
            }

            Vector3 leftFoot = AdvanceStep(
                ref _leftStepProgress, ref _leftFootPlanted, _leftFootFrom, leftTarget, leftStepping, deltaTime);
            Vector3 rightFoot = AdvanceStep(
                ref _rightStepProgress, ref _rightFootPlanted, _rightFootFrom, rightTarget, rightStepping, deltaTime);

            IKUtilities.SolveCCDIK(_leftLegChain, leftFoot, armTolerance, armIterations);
            IKUtilities.SolveCCDIK(_rightLegChain, rightFoot, armTolerance, armIterations);
        }

        /// <summary>Adımı bir kare ilerletir; adım yoksa ayak bastığı yerde kalır.</summary>
        private Vector3 AdvanceStep(
            ref float progress, ref Vector3 planted, Vector3 from, Vector3 target, bool stepping, float deltaTime)
        {
            if (!stepping)
            {
                return planted;
            }

            progress = stepDuration > 0f
                ? Mathf.Min(1f, progress + deltaTime / stepDuration)
                : 1f;

            Vector3 position = Vector3.Lerp(from, target, progress);
            position.y += Mathf.Sin(progress * Mathf.PI) * stepArcHeight;

            if (progress >= 1f)
            {
                planted = target;
                return target;
            }

            return position;
        }

        // ----------------------------------------------------------------- kurulum

        /// <summary>Humanoid Avatar'dan kemikleri toplar. Omuz/üst göğüs gibi İSTEĞE BAĞLI
        /// kemikler karakterde olmayabilir — zincirler o duruma göre kurulur.</summary>
        private void ResolveBones()
        {
            if (_animator == null || !_animator.isHuman)
            {
                Debug.LogWarning(
                    "[ThreePointBodyIK] Animator humanoid değil; IK devre dışı. " +
                    "Karakterin import ayarında Animation Type = Humanoid olmalı.", this);
                return;
            }

            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);

            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);

            _leftArmChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                _leftHand);
            _rightArmChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                _animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                _rightHand);

            _leftLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.LeftFoot));
            _rightLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.RightFoot));

            _bonesResolved = _hips != null;

            if (!_bonesResolved)
            {
                Debug.LogWarning("[ThreePointBodyIK] Hips kemiği bulunamadı; IK devre dışı.", this);
            }
        }

        /// <summary>Üç kemikten zincir kurar; biri bile eksikse zincir kurulmaz (CCD en az iki
        /// kemik ister ve yarım zincir sessizce yanlış poz üretir).</summary>
        private static Transform[] BuildChain(Transform a, Transform b, Transform c)
        {
            if (a == null || b == null || c == null)
            {
                return null;
            }

            return new[] { a, b, c };
        }
    }
}
