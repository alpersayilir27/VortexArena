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
    /// bağlanacak Transform kalmaz. Gövde ölçüleri (kalça düşüşü, ayak bileği yüksekliği) de
    /// aynı sebeple SABİT DEĞİL, karakterin bind pozundan ÖLÇÜLÜR.
    /// </para>
    /// <para>
    /// <b>Bacaklar tamamen prosedüreldir</b> (adım döngüsü + ayak IK): projede yürüme animasyon
    /// klibi yoktur ve free-roam'da oyuncu fiziksel yürüdüğü için kök hareketi zaten gerçektir —
    /// tek eksik ayakların nereye basacağıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Üç kural bozulursa avatar görsel olarak PATLAR</b> (uzamış/kopmuş uzuvlar):
    /// (1) <see cref="IKUtilities.SolveCCDIK"/> zinciri <b>UÇ→KÖK</b> sırasında bekler
    /// (effector = dizinin 0. elemanı); ters verilirse çözücü omzu/kalçayı ele doğru sürükler.
    /// (2) Her kare <b>bind pozuna dönülür</b>: bu bileşen kemiklere mutlak değil BİRİKİMLİ
    /// (<c>rotation = delta * rotation</c>) yazıyor ve sahnede pozu sıfırlayacak bir
    /// AnimatorController yok — sıfırlanmazsa gövde her karede biraz daha bükülür.
    /// (3) El/kafa kemiğine <b>konum yazılmaz, yalnız rotasyon</b>: konum yazmak kemiği
    /// ebeveyninden koparır (mesh gerilir), üstelik erişilemeyen hedefte bunu her kare yapar.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ThreePointBodyIK : MonoBehaviour
    {
        [Header("Gövde")]
        [Tooltip("Kalçanın kafa yönünün GERİSİNDE durma payı — öne eğilince gövde yatar.")]
        [SerializeField] private float hipsBackOffsetMeters = 0.06f;

        [Tooltip("Gövdenin kafa yaw'ını takip hızı (derece/sn). Düşük değer omuz-kafa farkı bırakır.")]
        [SerializeField] private float torsoYawFollowSpeed = 360f;

        [Tooltip("Gövdenin kafaya göre en fazla sapabileceği açı — bu aşılırsa gövde anında yetişir.")]
        [SerializeField] private float torsoMaxYawLagDegrees = 70f;

        [Header("Kollar")]
        [Tooltip("El/ayak hedefine kabul edilen yakınlık (metre).")]
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

        /// <summary>Avatar ölçeğinin alt/üst sınırı — bozuk bir poz ölçümü avatarı devleştirmesin.</summary>
        private const float MinScale = 0.75f;
        private const float MaxScale = 1.35f;

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
        // ⚠️ Sıra UÇ→KÖK: { el, önkol, üstkol } (bkz. sınıf özeti).
        private Transform[] _leftArmChain;
        private Transform[] _rightArmChain;
        private Transform _leftHand;
        private Transform _rightHand;

        // Bacaklar — aynı sıra: { ayak, alt bacak, üst bacak }.
        private Transform[] _leftLegChain;
        private Transform[] _rightLegChain;

        // Bind pozu: her kare buraya dönülür (birikimli yazımın panzehiri).
        private Transform[] _drivenBones;
        private Quaternion[] _bindLocalRotations;
        private Vector3[] _bindLocalPositions;

        /// <summary>Karakterin KENDİ ölçüleri (bind pozunda, kök uzayında; ölçek 1 iken).</summary>
        private float _modelHeadHeight;
        private float _modelHipsDrop;
        private float _modelAnkleHeight;

        /// <summary>Oyuncunun ölçülen ayakta kafa yüksekliği — avatar buna göre ölçeklenir.</summary>
        private float _standingHeadHeight;
        private float _scale = 1f;

        /// <summary>
        /// Çözücüye verilecek tolerans. ⚠️ <see cref="IKUtilities.SolveCCDIK"/> toleransı
        /// <b>KARESİYLE</b> karşılaştırır (<c>sqrMagnitude &gt; tolerance</c>) — metre cinsinden
        /// verilirse 0.01 m istenirken 0.1 m'de durur ve el hedefin bir karış uzağında kalır.
        /// </summary>
        private float SolverTolerance => armTolerance * armTolerance;

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
            float groundY = ArenaSpace.HasOrigin
                ? ArenaSpace.ArenaToWorld(Vector3.zero).y
                : 0f;

            RestoreBindPose();
            ApplyScale(head.position.y - groundY);

            Quaternion yaw = SolveTorsoYaw(head.rotation, deltaTime);

            PlaceRoot(head.position, yaw, groundY);
            PlaceTorso(head, yaw);
            SolveArm(_leftArmChain, _leftHand, handL);
            SolveArm(_rightArmChain, _rightHand, handR);
            SolveLegs(yaw, deltaTime, groundY);
        }

        // ------------------------------------------------------------ bind + ölçek

        /// <summary>
        /// Sürülen tüm kemikleri bind pozuna döndürür. Zorunludur: <see cref="ApplyLean"/> ve CCD
        /// kemiklere mevcut rotasyonun ÜSTÜNE yazıyor, sahnede de pozu sıfırlayan bir
        /// AnimatorController yok — sıfırlanmazsa hata her karede birikir ve gövde dakikalar
        /// içinde katlanır.
        /// </summary>
        private void RestoreBindPose()
        {
            for (int i = 0; i < _drivenBones.Length; i++)
            {
                Transform bone = _drivenBones[i];
                if (bone != null)
                {
                    bone.localRotation = _bindLocalRotations[i];
                    bone.localPosition = _bindLocalPositions[i];
                }
            }
        }

        /// <summary>
        /// Avatarı oyuncunun boyuna ölçekler. Model sabit boydadır (Ch15 ≈ 1.56 m kafa kemiği);
        /// ölçeklenmezse kısa kollar ele yetişemez, bacaklar zemine değmez — tam da "uzamış/kopuk
        /// uzuv" görüntüsü buradan çıkar.
        /// <para>Ölçü <b>en yüksek</b> gözlenen kafa yüksekliğinden alınır: eğilen/çömelen oyuncuda
        /// anlık yüksekliğe uyulsaydı avatar her çömelmede küçülürdü.</para>
        /// </summary>
        private void ApplyScale(float headHeightMeters)
        {
            if (_modelHeadHeight <= 0.01f || headHeightMeters <= 0.01f)
            {
                return;
            }

            if (headHeightMeters > _standingHeadHeight)
            {
                _standingHeadHeight = headHeightMeters;
            }

            float scale = Mathf.Clamp(_standingHeadHeight / _modelHeadHeight, MinScale, MaxScale);
            if (Mathf.Abs(scale - _scale) < 0.001f)
            {
                return;
            }

            _scale = scale;
            transform.localScale = new Vector3(scale, scale, scale);
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
        private void PlaceRoot(Vector3 headPosition, Quaternion yaw, float groundY)
        {
            transform.SetPositionAndRotation(
                new Vector3(headPosition.x, groundY, headPosition.z), yaw);
        }

        /// <summary>
        /// Kalça + omurga + kafa. Kalçanın kafadan düşüşü karakterin KENDİ bind pozundan ölçülür
        /// (ölçekle çarpılır) — sabit bir sayı yazılsaydı model değiştiğinde ya da oyuncu
        /// ölçeklendiğinde omurga gerilirdi.
        /// <para>Omurga eğimi kalçadan kafaya giden vektörden gelir ve zincire PAYLAŞTIRILIR
        /// (tek kemiğe verilseydi bel kırılır gibi görünürdü).</para>
        /// </summary>
        private void PlaceTorso(in Pose head, Quaternion yaw)
        {
            Vector3 hipsPosition = head.position
                                   - Vector3.up * (_modelHipsDrop * _scale)
                                   - (yaw * Vector3.forward) * (hipsBackOffsetMeters * _scale);

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
                // ⚠️ Yalnız ROTASYON: kafa kemiğine konum yazmak onu boyundan koparır (boyun mesh'i
                // gerilir). Konumu zaten kalça + omurga zinciri getiriyor.
                _head.rotation = head.rotation;
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
        /// <para>⚠️ El kemiğine KONUM yazılmaz. Hedef kolun erişemeyeceği kadar uzaktayken
        /// (kumanda pozu bileğin değil avucun ötesindedir) konum yazmak eli önkoldan koparır ve
        /// mesh gerilir; ulaşılamayan hedefte kol kısa kalsın, kopmasın.</para>
        /// </summary>
        private void SolveArm(Transform[] chain, Transform hand, in Pose handPose)
        {
            if (chain == null || hand == null)
            {
                return;
            }

            IKUtilities.SolveCCDIK(chain, handPose.position, SolverTolerance, armIterations);
            hand.rotation = handPose.rotation;
        }

        // ----------------------------------------------------------------- bacaklar

        /// <summary>
        /// Prosedürel adım: her ayağın bastığı nokta saklanır; gövde yeterince uzaklaşınca o ayak
        /// yeni yerine bir yay çizerek gider. AYNI ANDA iki ayak birden adım atmaz — atsaydı
        /// avatar zıplıyormuş gibi görünürdü.
        /// <para>Hedef yüksekliği zemin DEĞİL, zemin + ayak bileği yüksekliğidir: IK'nın ucu ayak
        /// kemiği (bilek) olduğu için zemine hizalanırsa ayak tabanı yere gömülür.</para>
        /// </summary>
        private void SolveLegs(Quaternion yaw, float deltaTime, float groundY)
        {
            if (_leftLegChain == null || _rightLegChain == null)
            {
                return;
            }

            Vector3 root = transform.position;
            root.y = groundY + _modelAnkleHeight * _scale;

            Vector3 right = yaw * Vector3.right;
            float halfWidth = stanceHalfWidth * _scale;
            Vector3 leftTarget = root - right * halfWidth;
            Vector3 rightTarget = root + right * halfWidth;

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
                float trigger = stepTriggerDistance * _scale;
                float leftError = Vector3.Distance(_leftFootPlanted, leftTarget);
                float rightError = Vector3.Distance(_rightFootPlanted, rightTarget);

                if (leftError > trigger && leftError >= rightError)
                {
                    _leftFootFrom = _leftFootPlanted;
                    _leftStepProgress = 0f;
                    leftStepping = true;
                }
                else if (rightError > trigger)
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

            IKUtilities.SolveCCDIK(_leftLegChain, leftFoot, SolverTolerance, armIterations);
            IKUtilities.SolveCCDIK(_rightLegChain, rightFoot, SolverTolerance, armIterations);
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
            position.y += Mathf.Sin(progress * Mathf.PI) * stepArcHeight * _scale;

            if (progress >= 1f)
            {
                planted = target;
                return target;
            }

            return position;
        }

        // ----------------------------------------------------------------- kurulum

        /// <summary>Humanoid Avatar'dan kemikleri toplar. Omuz/üst göğüs gibi İSTEĞE BAĞLI
        /// kemikler karakterde olmayabilir — zincirler o duruma göre kurulur.
        /// <para>Aynı geçişte karakterin bind pozu (her kare geri dönülecek referans) ve gövde
        /// ölçüleri (kalça düşüşü, ayak bileği yüksekliği) kaydedilir.</para></summary>
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

            // ⚠️ Zincirler UÇ→KÖK sıralanır: Meta'nın CCD çözücüsü effector olarak dizinin
            // 0. elemanını alır ve zincirin kökünü son elemanın EBEVEYNİNDEN bulur.
            _leftArmChain = BuildChain(
                _leftHand,
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
            _rightArmChain = BuildChain(
                _rightHand,
                _animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                _animator.GetBoneTransform(HumanBodyBones.RightUpperArm));

            _leftLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
            _rightLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.RightFoot),
                _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));

            _bonesResolved = _hips != null;

            if (!_bonesResolved)
            {
                Debug.LogWarning("[ThreePointBodyIK] Hips kemiği bulunamadı; IK devre dışı.", this);
                return;
            }

            CacheBindPose();
            MeasureModel();
        }

        /// <summary>Sürülen kemiklerin bind pozunu saklar — her kare buraya dönülür.</summary>
        private void CacheBindPose()
        {
            var bones = new System.Collections.Generic.List<Transform>
            {
                _hips, _spine, _chest, _upperChest, _neck, _head
            };

            AddChain(bones, _leftArmChain);
            AddChain(bones, _rightArmChain);
            AddChain(bones, _leftLegChain);
            AddChain(bones, _rightLegChain);

            bones.RemoveAll(b => b == null);

            _drivenBones = bones.ToArray();
            _bindLocalRotations = new Quaternion[_drivenBones.Length];
            _bindLocalPositions = new Vector3[_drivenBones.Length];

            for (int i = 0; i < _drivenBones.Length; i++)
            {
                _bindLocalRotations[i] = _drivenBones[i].localRotation;
                _bindLocalPositions[i] = _drivenBones[i].localPosition;
            }
        }

        private static void AddChain(System.Collections.Generic.List<Transform> target, Transform[] chain)
        {
            if (chain == null)
            {
                return;
            }

            for (int i = 0; i < chain.Length; i++)
            {
                if (!target.Contains(chain[i]))
                {
                    target.Add(chain[i]);
                }
            }
        }

        /// <summary>
        /// Karakterin bind pozundaki ölçüleri (kök uzayında, ölçek 1 iken). Sabit sayı yerine
        /// modelden okunur: başka bir karaktere geçildiğinde tek satır değişmesin diye.
        /// </summary>
        private void MeasureModel()
        {
            Vector3 hipsLocal = transform.InverseTransformPoint(_hips.position);

            if (_head != null)
            {
                Vector3 headLocal = transform.InverseTransformPoint(_head.position);
                _modelHeadHeight = headLocal.y;
                _modelHipsDrop = Mathf.Max(0.05f, headLocal.y - hipsLocal.y);
            }
            else
            {
                _modelHeadHeight = hipsLocal.y;
                _modelHipsDrop = 0f;
            }

            Transform foot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _modelAnkleHeight = foot != null
                ? Mathf.Max(0f, transform.InverseTransformPoint(foot.position).y)
                : 0f;

            _standingHeadHeight = _modelHeadHeight;
        }

        /// <summary>Üç kemikten zincir kurar; biri bile eksikse zincir kurulmaz (CCD en az iki
        /// kemik ister ve yarım zincir sessizce yanlış poz üretir).
        /// <para>⚠️ Sıra UÇ→KÖK verilir (<c>{ el, önkol, üstkol }</c>).</para></summary>
        private static Transform[] BuildChain(Transform tip, Transform middle, Transform root)
        {
            if (tip == null || middle == null || root == null)
            {
                return null;
            }

            return new[] { tip, middle, root };
        }
    }
}
