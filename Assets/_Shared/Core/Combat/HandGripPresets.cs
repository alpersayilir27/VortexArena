using Oculus.Interaction.Input;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Parmak preset'lerinin (<see cref="HandGripPreset"/>) tek dağıtım kapısı: oranları
    /// (<see cref="HandPoseProfile"/>), ISDK sentetik eli için eklem dönüşlerini ve o elin
    /// serbestlik dizisini üretir.
    /// <para>
    /// <b>Neden tek kapı:</b> aynı üç duruş üç ayrı iskelette çiziliyor (yerel sentetik el,
    /// stüdyodaki hayalet el, uzak avatarın Mixamo eli). Her biri kendi eksenini kendi bind
    /// pozundan ölçüyor ama "ne kadar kapanacağı" tek yerden geliyor — stüdyoda görülen el ile
    /// oyunda görülen elin aynı olmasının şartı bu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Eklem dönüşleri SABİT YAZILMAZ, ISDK'nın kendi iskeletinden ÖLÇÜLÜR</b>
    /// (<see cref="HandSkeleton.DefaultLeftSkeleton"/> / <c>DefaultRightSkeleton</c> — ISDK'nın
    /// <c>HandPose</c> kurucusu da açık el için tam bu iskeletin dönüşlerini kullanıyor, baz odur).
    /// SDK iskeleti değiştirdiğinde burada tek satır değişmesin diye; projede tekrarlanan
    /// "sabit yazma, ölç" kuralı (<see cref="HandFingerRig"/> ile aynı gerekçe).
    /// </para>
    /// <para>
    /// ⚠️ <b>Avuç (volar) yönü de VERİDEN çıkarılır:</b> gevşek iskeletin parmakları zaten avuca
    /// doğru hafif kıvrık, yani "avuç hangi tarafta" sorusunun cevabı iskeletin kendisinde duruyor.
    /// Başparmak/avuç-normali sözleşmesine dayanmak (sol/sağ çapraz çarpım sırası) işaret hatasına
    /// açıktır ve hatanın belirtisi "parmaklar el sırtına doğru kırılıyor" olurdu.
    /// </para>
    /// <para>
    /// ⚠️ OpenXR dalında <c>SyntheticHand.AmendMetacarpalRotation</c>, <c>Index1/Middle1/Ring1</c>
    /// için verilen dönüşü <b>bilek uzayında</b> bekliyor (metakarpın dönüşünü geri alıyor).
    /// Varsayılan iskelette bu üç metakarpın bind dönüşü KİMLİK olduğu için "metakarpa yerel" ile
    /// "bileğe göreli" aynı şeydir ve burada ek bir çevirim gerekmiyor — iskelet bir gün metakarpa
    /// dönüş verirse bakılacak yer burasıdır.
    /// </para>
    /// </summary>
    public static class HandGripPresets
    {
        /// <summary>Parmak sayısı (ISDK garantisi).</summary>
        private const int FingerCount = 5;

        /// <summary>
        /// Tam kapanmada sürülebilir eklem başına derece — <see cref="HandFingerRig"/>'deki tabloyla
        /// AYNI sayılar. Başparmak ayrı: anatomik olarak daha az kıvrılır ve kabzayı sarmak yerine
        /// üstüne yatar.
        /// </summary>
        private static readonly float[] FingerMaxAngles = { 50f, 60f, 40f };
        private static readonly float[] ThumbMaxAngles = { 25f, 35f, 30f };

        /// <summary>Yön vektörünün "anlamlı" sayılması için gereken en küçük kare uzunluk.</summary>
        private const float MinDirectionSqr = 1e-8f;

        /// <summary>
        /// Toplam sapma bunun altındaysa avuç yönü veriden çıkarılamaz (parmaklar tam düz bir
        /// iskelet) ve <see cref="HandGripConvention"/> bazına düşülür.
        /// </summary>
        private const float MinVolarMagnitude = 1e-4f;

        /// <summary>İşaret öz-denetiminde kullanılan küçük deneme açısı (derece).</summary>
        private const float SignProbeDegrees = 10f;

        /// <summary>
        /// Önbellek: [preset, el]. Diziler <b>paylaşılır</b> — üretimi ucuz değil (iskelet çözümü +
        /// eksen ölçümü) ve kare başına iki el için çağrılıyor.
        /// </summary>
        private static readonly Quaternion[][] Cache = new Quaternion[3 * 2][];

        /// <summary>Preset'in beş kapanma oranı.</summary>
        public static HandPoseProfile Profile(HandGripPreset preset)
        {
            switch (preset)
            {
                case HandGripPreset.Firing: return HandPoseProfile.Firing;
                case HandGripPreset.Grip: return HandPoseProfile.Grip;
                default: return HandPoseProfile.Idle;
            }
        }

        /// <summary>
        /// Preset'in parmak serbestlik dizisi (uzunluk 5, <see cref="HandFinger"/> sırasında).
        /// <para>
        /// <b><see cref="HandGripPreset.Firing"/>'de işaret parmağı <see cref="JointFreedom.Free"/>
        /// kalır</b>: tetik parmağının kıvrımı yazılmış bir duruş değil, kumandanın analog
        /// girdisidir — kilitlenirse oyuncu ateş ederken parmağının kıpırdamadığını görür. Kalan
        /// dört parmak kabzayı sardığı için kilitlidir.
        /// </para>
        /// <para>⚠️ Her çağrıda <b>YENİ dizi</b> döner: serbestlik seviyesi sentetik elde kalıcıdır
        /// ve çağıranlardan biri paylaşılan diziyi düzeltseydi öteki elin duruşu sessizce
        /// değişirdi.</para>
        /// </summary>
        public static JointFreedom[] Freedom(HandGripPreset preset)
        {
            var freedom = new JointFreedom[FingerCount];
            for (int i = 0; i < FingerCount; i++)
            {
                freedom[i] = JointFreedom.Locked;
            }

            if (preset == HandGripPreset.Firing)
            {
                freedom[(int)HandFinger.Index] = JointFreedom.Free;
            }

            return freedom;
        }

        /// <summary>
        /// Preset'in ISDK sentetik eline yazılacak <b>yerel</b> eklem dönüşleri —
        /// <see cref="FingersMetadata.HAND_JOINT_IDS"/> sırasında
        /// (<c>SyntheticHand.OverrideAllJoints</c>'in beklediği biçim).
        /// <para>⚠️ Dönen dizi <b>ÖNBELLEKLİDİR ve paylaşılır</b>: çağıran onu DEĞİŞTİRMEZ.
        /// Sürülmeyen eklemler (metakarpallar) bind dönüşünü taşır.</para>
        /// </summary>
        public static Quaternion[] JointRotations(HandGripPreset preset, bool rightHand)
        {
            int slot = (int)preset * 2 + (rightHand ? 1 : 0);
            if (slot < 0 || slot >= Cache.Length)
            {
                slot = rightHand ? 1 : 0;
            }

            return Cache[slot] ??= Build(Profile(preset), rightHand);
        }

        /// <summary>
        /// Bir kavrama noktasının, preset yazılmamışsa kullanılacak duruşu: ön kabza saran el
        /// (<see cref="HandGripPreset.Grip"/>), ana kabza tetiği olan el
        /// (<see cref="HandGripPreset.Firing"/>).
        /// </summary>
        public static HandGripPreset DefaultFor(GripSocketKind kind)
        {
            return kind == GripSocketKind.Secondary ? HandGripPreset.Grip : HandGripPreset.Firing;
        }

        /// <summary>Editör arayüzünde gösterilen ad.</summary>
        public static string Label(HandGripPreset preset)
        {
            switch (preset)
            {
                case HandGripPreset.Firing: return "Sıkma (işaret tetikte)";
                case HandGripPreset.Grip: return "Kabza (sarma)";
                default: return "Boşta (hafif açık)";
            }
        }

        // ------------------------------------------------------------------ ölçüm

        /// <summary>
        /// Duruşu ISDK'nın varsayılan iskeletinden üretir: iskelet el uzayında bileştirilir, avuç
        /// yönü veriden çıkarılır, her sürülebilir eklem kendi kemik yönüne dik bir menteşe
        /// etrafında oranın istediği kadar bükülür.
        /// </summary>
        private static Quaternion[] Build(in HandPoseProfile profile, bool rightHand)
        {
            HandSkeleton skeleton = rightHand
                ? HandSkeleton.DefaultRightSkeleton
                : HandSkeleton.DefaultLeftSkeleton;

            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            var result = new Quaternion[ids.Length];

            HandSkeletonJoint[] joints = skeleton != null ? skeleton.joints : null;
            if (joints == null)
            {
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = Quaternion.identity;
                }

                return result;
            }

            // Başlangıç: her eklem kendi bind dönüşünde. Sürülmeyenler (metakarpallar) böyle kalır.
            for (int i = 0; i < ids.Length; i++)
            {
                int raw = (int)ids[i];
                result[i] = raw >= 0 && raw < joints.Length
                    ? joints[raw].pose.rotation
                    : Quaternion.identity;
            }

            ResolveHandSpace(joints, out Vector3[] handPos, out Quaternion[] handRot);

            if (!TryResolveVolar(joints, handPos, handRot, rightHand, out Vector3 volar))
            {
                // Avuç yönü çıkarılamadı: bükülme yönü tanımsız, bind duruşu (açık el) döner.
                return result;
            }

            for (int finger = 0; finger < FingerCount; finger++)
            {
                HandJointId[] chain = ChainOf(finger);
                if (chain == null || chain.Length < 2)
                {
                    continue;
                }

                float curl = Mathf.Clamp01(profile.Get(finger));
                float[] maxAngles = finger == (int)HandFinger.Thumb ? ThumbMaxAngles : FingerMaxAngles;
                int drivable = 0;

                // Son eleman UÇTUR: döndürülmez ama bir önceki eklemin kemik yönünü tanımlar.
                for (int j = 0; j < chain.Length - 1; j++)
                {
                    int raw = (int)chain[j];
                    int next = (int)chain[j + 1];
                    int slot = FingersMetadata.HandJointIdToIndex(chain[j]);
                    if (slot < 0 || slot >= result.Length || raw < 0 || raw >= joints.Length)
                    {
                        continue;
                    }

                    // ⚠️ Metakarpallar SÜRÜLMEZ (HAND_JOINT_CAN_MOVE): bilekle birlikte hareket
                    // ederler, döndürülürlerse el yelpaze gibi açılır. Sürülebilir eklem sayacı da
                    // bu yüzden metakarpı ATLAR — açı tablosu proksimalden başlar.
                    if (!FingersMetadata.HAND_JOINT_CAN_MOVE[slot])
                    {
                        continue;
                    }

                    Vector3 bone = handPos[next] - handPos[raw];
                    if (bone.sqrMagnitude < MinDirectionSqr)
                    {
                        drivable++;
                        continue;
                    }

                    bone = bone.normalized;
                    Vector3 axis = Vector3.Cross(bone, volar);
                    if (axis.sqrMagnitude < MinDirectionSqr)
                    {
                        drivable++;
                        continue;
                    }

                    axis = axis.normalized;

                    // ⚠️ İşaret ÖZ-DENETİMLE sabitlenir, Unity'nin dönüş yönü sözleşmesi TAHMİN
                    // EDİLMEZ: eksen etrafında küçük bir pozitif dönüş kemiği avuca doğru
                    // götürmüyorsa eksen terstir. Tahmin edilseydi hatanın belirtisi "parmaklar el
                    // sırtına doğru kırılıyor" olurdu ve tersini denemekten başka teşhisi olmazdı.
                    Vector3 probed = Quaternion.AngleAxis(SignProbeDegrees, axis) * bone;
                    if (Vector3.Dot(probed - bone, volar) <= 0f)
                    {
                        axis = -axis;
                    }

                    int parent = joints[raw].parent;
                    Quaternion parentRotation = parent >= 0 && parent < handRot.Length
                        ? handRot[parent]
                        : Quaternion.identity;

                    // Eksen EBEVEYN çerçevesine çevrilir: yerel dönüş ebeveyne göre yazılıyor ve
                    // menteşe ekseni eklemde sabittir, ebeveyn döndükçe onunla döner.
                    Vector3 axisLocal = Quaternion.Inverse(parentRotation) * axis;

                    int angleSlot = Mathf.Min(drivable, maxAngles.Length - 1);
                    float angle = curl * maxAngles[angleSlot];
                    result[slot] = Quaternion.AngleAxis(angle, axisLocal) * joints[raw].pose.rotation;
                    drivable++;
                }
            }

            return result;
        }

        /// <summary>
        /// İskeletin her ekleminin EL uzayındaki pozunu üstten aşağı bileştirir (kök = kendi pozu,
        /// varsayılan iskelette bilek kimliktedir).
        /// </summary>
        private static void ResolveHandSpace(HandSkeletonJoint[] joints,
            out Vector3[] positions, out Quaternion[] rotations)
        {
            positions = new Vector3[joints.Length];
            rotations = new Quaternion[joints.Length];
            var resolved = new bool[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                Resolve(joints, positions, rotations, resolved, i);
            }
        }

        /// <summary>Bir eklemi (gerekiyorsa önce üstünü) el uzayına çözer.</summary>
        private static void Resolve(HandSkeletonJoint[] joints, Vector3[] positions,
            Quaternion[] rotations, bool[] resolved, int index)
        {
            if (index < 0 || index >= joints.Length || resolved[index])
            {
                return;
            }

            // Döngüye karşı: kendini çözmeden önce işaretlemek, bozuk bir ebeveyn zincirinde
            // sonsuz özyinelemeyi engeller (kök gibi davranır).
            resolved[index] = true;

            Pose local = joints[index].pose;
            int parent = joints[index].parent;

            if (parent < 0 || parent >= joints.Length)
            {
                positions[index] = local.position;
                rotations[index] = local.rotation;
                return;
            }

            Resolve(joints, positions, rotations, resolved, parent);
            positions[index] = positions[parent] + rotations[parent] * local.position;
            rotations[index] = rotations[parent] * local.rotation;
        }

        /// <summary>
        /// Avuç (volar) yönü — parmakların KAPANDIĞI yön, el uzayında.
        /// <para>
        /// Ölçü: dört parmakta (işaret…serçe) proksimal kemiğin yönü <c>m</c>, bir sonraki boğumun
        /// yönü <c>p</c>; <c>p</c>'nin <c>m</c>'ye dik bileşeni zaten avuca doğrudur (gevşek iskelet
        /// hafif kıvrıktır). Dördünün toplamı yönü verir.
        /// </para>
        /// <para>
        /// Toplam dejenereyse (tam düz bir iskelet) <see cref="HandGripConvention"/>'ın anatomik
        /// bazına düşülür ve <b>işareti yine veriden</b> seçilir: başparmak gevşekken avuca doğru
        /// sarkar, yani onun boğum sapması hangi tarafa bakıyorsa avuç orasıdır. Baz tek başına
        /// yeterli değil çünkü ondaki sol/sağ çapraz çarpım sırası tam da burada işaret hatasına
        /// açık olan şey.
        /// </para>
        /// </summary>
        private static bool TryResolveVolar(HandSkeletonJoint[] joints, Vector3[] handPos,
            Quaternion[] handRot, bool rightHand, out Vector3 volar)
        {
            volar = Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int finger = (int)HandFinger.Index; finger <= (int)HandFinger.Pinky; finger++)
            {
                if (TryMeasureBend(handPos, ChainOf(finger), out Vector3 bend))
                {
                    sum += bend;
                }
            }

            if (sum.magnitude >= MinVolarMagnitude)
            {
                volar = sum.normalized;
                return true;
            }

            return TryVolarFromConvention(joints, handPos, handRot, rightHand, out volar);
        }

        /// <summary>
        /// Bir parmak zincirinin "kıvrım sapması": ikinci kemiğin, birinci kemiğe DİK olan bileşeni.
        /// Zincirin ilk üç eklemi gerekir (metakarp/proksimal + iki boğum).
        /// </summary>
        private static bool TryMeasureBend(Vector3[] handPos, HandJointId[] chain, out Vector3 bend)
        {
            bend = Vector3.zero;

            if (chain == null || chain.Length < 3)
            {
                return false;
            }

            int a = (int)chain[0];
            int b = (int)chain[1];
            int c = (int)chain[2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= handPos.Length || b >= handPos.Length || c >= handPos.Length)
            {
                return false;
            }

            Vector3 first = handPos[b] - handPos[a];
            Vector3 second = handPos[c] - handPos[b];
            if (first.sqrMagnitude < MinDirectionSqr || second.sqrMagnitude < MinDirectionSqr)
            {
                return false;
            }

            first = first.normalized;
            second = second.normalized;
            bend = second - first * Vector3.Dot(second, first);
            return true;
        }

        /// <summary>
        /// Yedek avuç yönü: anatomik bazın <c>+Y</c> ekseni doğrultusu, işareti başparmağın kıvrım
        /// sapmasından. Gerekçe <see cref="TryResolveVolar"/>'da.
        /// </summary>
        private static bool TryVolarFromConvention(HandSkeletonJoint[] joints, Vector3[] handPos,
            Quaternion[] handRot, bool rightHand, out Vector3 volar)
        {
            volar = Vector3.zero;

            int root = RootIndex(joints);
            if (root < 0)
            {
                return false;
            }

            HandJointId[] middle = ChainOf((int)HandFinger.Middle);
            HandJointId[] thumb = ChainOf((int)HandFinger.Thumb);
            if (middle == null || middle.Length < 4 || thumb == null || thumb.Length < 1)
            {
                return false;
            }

            // ⚠️ Orta parmağın PROKSİMALİ istenir, metakarpı değil: zincir daima
            // "… proksimal, orta boğum, uç boğum, uç" ile bittiği için yeri sondan dördüncüdür
            // (metakarpı olan ve olmayan ISDK dallarında da doğru eleman gelsin).
            int middleProximal = (int)middle[middle.Length - 4];
            int thumbFirst = (int)thumb[0];
            if (middleProximal < 0 || middleProximal >= handPos.Length ||
                thumbFirst < 0 || thumbFirst >= handPos.Length)
            {
                return false;
            }

            // ⚠️ Yönler KÖKÜN çerçevesinde verilir (baz orada tanımlı) ve sonuç aşağıda el uzayına
            // geri çevrilir: varsayılan iskelette bilek kimlikte olduğu için ikisi bugün aynı, ama
            // ikisini karıştırmak iskelet bir gün döndürülmüş bir bilekle gelirse sessizce sapardı.
            Quaternion toRootLocal = Quaternion.Inverse(handRot[root]);
            Vector3 fingerDirection = toRootLocal * (handPos[middleProximal] - handPos[root]);
            Vector3 thumbDirection = toRootLocal * (handPos[thumbFirst] - handPos[root]);

            if (!HandGripConvention.TryMeasureBoneBasis(
                    fingerDirection, thumbDirection, rightHand, out Quaternion basis))
            {
                return false;
            }

            Vector3 candidate = handRot[root] * (basis * Vector3.up);
            if (candidate.sqrMagnitude < MinDirectionSqr)
            {
                return false;
            }

            candidate = candidate.normalized;

            if (TryMeasureBend(handPos, thumb, out Vector3 thumbBend) &&
                thumbBend.sqrMagnitude >= MinDirectionSqr &&
                Vector3.Dot(candidate, thumbBend) < 0f)
            {
                candidate = -candidate;
            }

            volar = candidate;
            return true;
        }

        /// <summary>Parmağın eklem zinciri (uç DAHİL) — sıra ISDK'nın kendi tablosundan gelir.</summary>
        private static HandJointId[] ChainOf(int finger)
        {
            var list = HandJointUtils.FingerToJointList;
            return finger >= 0 && finger < list.Count ? list[finger] : null;
        }

        /// <summary>Üstü olmayan eklem (bilek kökü).</summary>
        private static int RootIndex(HandSkeletonJoint[] joints)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].parent < 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
