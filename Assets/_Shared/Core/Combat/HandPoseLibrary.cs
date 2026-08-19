using System;
using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Parmak duruşunun tek dağıtım kapısı: boş elin duruşu, silaha özel riglenmiş duruşun ISDK
    /// eklem dizisine çevrilmesi, o duruşun humanoid el için kapanma oranına indirgenmesi ve
    /// duruşlar arası geçişin süresi/eğrisi.
    /// <para>
    /// <b>İki tüketici, iki biçim.</b> Yerel sentetik el (ISDK) eklem eklem <b>ham dönüş</b> alır —
    /// oyuncu kendi elini burnunun dibinde gördüğü için ince ayar oradadır ve stüdyoda riglenen ne
    /// ise oyunda görülen odur. Uzak avatarın <b>Mixamo</b> eli ham dönüş ALAMAZ (iki iskeletin
    /// kemik eksenleri aynı değil; projede bir kez öğrenilmiş kural: izleme/ağ uzayından gelen
    /// rotasyon humanoid kemiğe doğrudan yazılmaz) — ona riglenmiş duruştan <b>ölçülen</b> beş
    /// kapanma oranı gider (<see cref="MeasureCurl"/>, <see cref="HandPoseProfile"/>). Oran ikinci
    /// bir doğruluk kaynağı değildir: asset'te saklanmaz, riglenmiş duruştan türetilir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Parmaklar HİÇBİR ZAMAN donanımdan sürülmez</b> — ne kumandanın tetiğinden/kabzasından
    /// ne el izlemesinden. Bir elin duruşu her karede ya boş elin duruşudur ya da tuttuğu eşyanın o
    /// slot için riglenmiş duruşu; ikisi arası <see cref="TransitionSeconds"/> içinde yumuşatılır.
    /// Serbest bırakılan bir parmak (<c>JointFreedom.Free</c>) diye bir kavram YOKTUR ve geri
    /// gelmez: tek bir parmağı bile donanıma bırakmak, stüdyoda görülen el ile oyunda görülen elin
    /// o parmakta ayrışması demektir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bind duruşu ve menteşe eksenleri SABİT YAZILMAZ, ISDK'nın kendi iskeletinden ÖLÇÜLÜR</b>
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
    /// ⚠️ <b>METAKARPALLAR SÜRÜLMEZ ve kayda GİRMEZ</b> (<c>FingersMetadata.HAND_JOINT_CAN_MOVE</c>).
    /// Yalnız "bilekle beraber hareket ederler, döndürülürse el yelpaze gibi açılır" diye değil:
    /// OpenXR dalında <c>SyntheticHand.AmendMetacarpalRotation</c>, <c>Index1/Middle1/Ring1</c> için
    /// verilen dönüşü <b>bilek uzayında</b> bekliyor ve metakarpın dönüşünü geri alıyor. Varsayılan
    /// iskelette o üç metakarpın bind dönüşü KİMLİK olduğu için bugün "metakarpa yerel" ile "bileğe
    /// göreli" aynı şeydir — metakarpı riglemeye açmak tam bu eşitliği bozar ve sonuç tezgâhta
    /// doğru, oyunda kaymış bir el olurdu (tezgâh ile oyun arasındaki kimlik bu aracın bütün işi).
    /// </para>
    /// </summary>
    public static class HandPoseLibrary
    {
        /// <summary>Parmak sayısı (ISDK garantisi).</summary>
        private const int FingerCount = 5;

        /// <summary>
        /// Tam kapanmada sürülebilir eklem başına derece — <see cref="HandFingerRig"/>'deki tabloyla
        /// AYNI sayılar. Başparmak ayrı: anatomik olarak daha az kıvrılır ve kabzayı sarmak yerine
        /// üstüne yatar. Riglenmiş duruşun kapanma oranına indirgenmesi de (<see cref="MeasureCurl"/>)
        /// bu tabloyu payda olarak kullanır — "tam kapalı" iki uçta aynı şeyi göstersin.
        /// </summary>
        private static readonly float[] FingerMaxAngles = { 50f, 60f, 40f };
        private static readonly float[] ThumbMaxAngles = { 25f, 35f, 30f };

        /// <summary>Yön vektörünün "anlamlı" sayılması için gereken en küçük kare uzunluk.</summary>
        private const float MinDirectionSqr = 1e-8f;

        /// <summary>
        /// Toplam sapma bunun altındaysa avuç yönü veriden çıkarılamaz (parmakları tam düz bir
        /// iskelet) ve <see cref="HandGripConvention"/> bazına düşülür.
        /// </summary>
        private const float MinVolarMagnitude = 1e-4f;

        /// <summary>İşaret öz-denetiminde kullanılan küçük deneme açısı (derece).</summary>
        private const float SignProbeDegrees = 10f;

        /// <summary>
        /// Bir sürülebilir eklemin ölçülmüş menteşesi: dizideki yeri, hangi parmağa ait olduğu,
        /// EBEVEYN çerçevesindeki dönme ekseni, bind dönüşü ve tam kapanmadaki açısı.
        /// <para>Tablo hem duruş ÜRETİMİNDE (oran → dönüş) hem duruş ÖLÇÜMÜNDE (dönüş → oran)
        /// kullanılır; iki yön tek geometriden çıksın diye tek yerde durur.</para>
        /// </summary>
        private struct Hinge
        {
            public int Slot;
            public int Finger;
            public Vector3 AxisLocal;
            public Quaternion Bind;
            public float MaxAngle;
        }

        /// <summary>Önbellekler el başına ([0] = sol, [1] = sağ); üretimleri iskelet çözümü +
        /// eksen ölçümü içerdiği için ucuz değil ve kare başına okunuyorlar.</summary>
        private static readonly Quaternion[][] BindCache = new Quaternion[2][];
        private static readonly Hinge[][] HingeCache = new Hinge[2][];
        private static readonly Quaternion[][] IdleCache = new Quaternion[2][];

        /// <summary>
        /// Bir duruştan ötekine geçişin süresi (saniye) — boş elin kavrama duruşuna kapanması ve
        /// bırakınca geri açılması bu kadar sürer.
        /// <para>
        /// ⚠️ <b>Tek sayı, iki uç:</b> yerel sentetik el (<see cref="HandGripPoser"/>) ve uzak
        /// avatarın eli (<c>RemoteHandPoser</c>) aynı süreyi kullanır — aynı kavrama iki ekranda
        /// farklı hızda kapanmasın. Süre kısa tutulur: silah zaten ele anında geliyor
        /// (<c>Weapon.ApplyCanonicalGrip</c>), parmaklar ondan görünür biçimde geç kalırsa el bir
        /// an silahın içinden geçer.
        /// </para>
        /// </summary>
        public const float TransitionSeconds = 0.15f;

        /// <summary>
        /// Geçiş eğrisi: <c>0..1</c> ilerlemeyi yumuşatılmış (smoothstep) karışım oranına çevirir.
        /// İki uç da aynı eğriyi kullanır (<see cref="TransitionSeconds"/> ile aynı gerekçe).
        /// </summary>
        public static float Ease(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------ boş el

        /// <summary>
        /// <b>Boş elin</b> eklem dizisi — <see cref="FingersMetadata.HAND_JOINT_IDS"/> sırasında
        /// (<c>SyntheticHand.OverrideAllJoints</c>'in beklediği biçim).
        /// <para>Eşya bırakılınca el buna döner ve kavraması riglenmemiş bir slot da buraya düşer:
        /// projede hiçbir el "tahta" kalmaz.</para>
        /// <para>⚠️ Dönen dizi <b>ÖNBELLEKLİDİR ve paylaşılır</b>: çağıran onu DEĞİŞTİRMEZ.</para>
        /// </summary>
        public static Quaternion[] IdleJointRotations(bool rightHand)
        {
            int hand = rightHand ? 1 : 0;
            return IdleCache[hand] ??= BuildProfile(HandPoseProfile.Idle, rightHand);
        }

        // --------------------------------------------------------- riglenmiş duruş

        /// <summary>
        /// Silaha özel riglenmiş duruşu ISDK eklem dizisine çevirir: taban her eklemin <b>bind</b>
        /// dönüşüdür, üstüne kaydın adıyla eşleşen eklemler yazılır.
        /// <para>⚠️ Dönen dizi <b>YENİ</b>dir (paylaşılan önbellek değil): çağıran onu kendi
        /// tarafında önbelleklemelidir — kare başına çağrılacak bir yol değildir
        /// (<see cref="ItemDefinition.GripJointRotations"/> bunu yapar).</para>
        /// <para>Kayıt boşsa boş elin dizisinin <b>kopyası</b> döner.</para>
        /// </summary>
        public static Quaternion[] BuildJointRotations(HandJointRotation[] joints, bool rightHand)
        {
            if (joints == null || joints.Length == 0)
            {
                return Copy(IdleJointRotations(rightHand));
            }

            Quaternion[] result = Copy(Bind(rightHand));

            for (int i = 0; i < joints.Length; i++)
            {
                int slot = SlotOf(joints[i].joint);
                if (slot < 0 || slot >= result.Length)
                {
                    // ⚠️ Sessiz atlama BİLİNÇLİ: ISDK dalı değişince tanınmayan eklem adı normaldir
                    // (bkz. HandJointRotation) ve burası kare başına değil, önbellek ıskasında koşar
                    // — log basmanın tek etkisi konsolu doldurmak olurdu.
                    continue;
                }

                result[slot] = joints[i].rotation;
            }

            return result;
        }

        /// <summary>
        /// Riglenmiş duruşun <b>humanoid el için</b> karşılığı: parmak başına kapanma oranı.
        /// <para>
        /// Ölçü, üretimin tersidir: her sürülebilir eklemde bind'den authored'a olan dönüş, o eklemin
        /// menteşe ekseni üzerine <b>işaretli</b> izdüşürülür; parmağın toplam sapması aynı parmağın
        /// tam kapanma toplamına bölünür. İşaretli olması şart — geriye kırılmış bir parmak, mutlak
        /// açı alınsaydı uzakta öne kapalı görünürdü.
        /// </para>
        /// <para>Kayıt boşsa boş elin oranları döner.</para>
        /// </summary>
        public static HandPoseProfile MeasureCurl(HandJointRotation[] joints, bool rightHand)
        {
            if (joints == null || joints.Length == 0)
            {
                return HandPoseProfile.Idle;
            }

            Quaternion[] pose = BuildJointRotations(joints, rightHand);
            Hinge[] hinges = Hinges(rightHand);

            var signed = new float[FingerCount];
            var total = new float[FingerCount];

            for (int i = 0; i < hinges.Length; i++)
            {
                Hinge hinge = hinges[i];
                if (hinge.Slot < 0 || hinge.Slot >= pose.Length ||
                    hinge.Finger < 0 || hinge.Finger >= FingerCount)
                {
                    continue;
                }

                (pose[hinge.Slot] * Quaternion.Inverse(hinge.Bind))
                    .ToAngleAxis(out float angle, out Vector3 axis);

                // ToAngleAxis 0..360 döndürür; 180'in ötesi ters yöndeki küçük bir dönüştür.
                if (angle > 180f)
                {
                    angle -= 360f;
                }

                if (float.IsNaN(angle) || float.IsInfinity(angle) || float.IsNaN(axis.x))
                {
                    angle = 0f;
                }

                signed[hinge.Finger] += angle * (Vector3.Dot(axis, hinge.AxisLocal) >= 0f ? 1f : -1f);
                total[hinge.Finger] += hinge.MaxAngle;
            }

            return new HandPoseProfile
            {
                thumb = Ratio(signed[0], total[0]),
                index = Ratio(signed[1], total[1]),
                middle = Ratio(signed[2], total[2]),
                ring = Ratio(signed[3], total[3]),
                pinky = Ratio(signed[4], total[4]),
            };
        }

        /// <summary>
        /// Bu eklem <b>riglenebilir</b> mi (metakarpallar hariç — gerekçe sınıf uyarısında).
        /// Stüdyo hem seçime açtığı eklemleri hem kayda aldıklarını buradan süzer.
        /// </summary>
        public static bool IsDrivable(HandJointId id)
        {
            int slot = FingersMetadata.HandJointIdToIndex(id);
            return slot >= 0 && slot < FingersMetadata.HAND_JOINT_CAN_MOVE.Length &&
                   FingersMetadata.HAND_JOINT_CAN_MOVE[slot];
        }

        private static float Ratio(float signed, float total)
        {
            return total > 0f ? Mathf.Clamp01(signed / total) : 0f;
        }

        private static Quaternion[] Copy(Quaternion[] source)
        {
            var result = new Quaternion[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        /// <summary>Eklem adının (<c>HandJointId</c>) dizideki yeri; tanınmıyorsa <c>-1</c>.</summary>
        private static int SlotOf(string jointName)
        {
            if (string.IsNullOrEmpty(jointName) ||
                !Enum.TryParse(jointName, false, out HandJointId id))
            {
                return -1;
            }

            return FingersMetadata.HandJointIdToIndex(id);
        }

        // ------------------------------------------------------------------ ölçüm

        /// <summary>Elin bind (açık iskelet) dönüşleri — paylaşılan önbellek, değiştirilmez.</summary>
        private static Quaternion[] Bind(bool rightHand)
        {
            EnsureTables(rightHand);
            return BindCache[rightHand ? 1 : 0];
        }

        /// <summary>Elin sürülebilir eklem menteşeleri — paylaşılan önbellek, değiştirilmez.</summary>
        private static Hinge[] Hinges(bool rightHand)
        {
            EnsureTables(rightHand);
            return HingeCache[rightHand ? 1 : 0];
        }

        /// <summary>
        /// Bir elin bind dizisini ve menteşe tablosunu ISDK'nın varsayılan iskeletinden ölçer:
        /// iskelet el uzayında bileştirilir, avuç yönü veriden çıkarılır, her sürülebilir eklem için
        /// kendi kemik yönüne dik bir menteşe ekseni kurulur.
        /// </summary>
        private static void EnsureTables(bool rightHand)
        {
            int hand = rightHand ? 1 : 0;
            if (BindCache[hand] != null)
            {
                return;
            }

            HandSkeleton skeleton = rightHand
                ? HandSkeleton.DefaultRightSkeleton
                : HandSkeleton.DefaultLeftSkeleton;

            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            var bind = new Quaternion[ids.Length];
            HandSkeletonJoint[] joints = skeleton != null ? skeleton.joints : null;

            if (joints == null)
            {
                for (int i = 0; i < bind.Length; i++)
                {
                    bind[i] = Quaternion.identity;
                }

                BindCache[hand] = bind;
                HingeCache[hand] = Array.Empty<Hinge>();
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                int raw = (int)ids[i];
                bind[i] = raw >= 0 && raw < joints.Length
                    ? joints[raw].pose.rotation
                    : Quaternion.identity;
            }

            BindCache[hand] = bind;

            ResolveHandSpace(joints, out Vector3[] handPos, out Quaternion[] handRot);

            if (!TryResolveVolar(joints, handPos, handRot, rightHand, out Vector3 volar))
            {
                // Avuç yönü çıkarılamadı: bükülme yönü tanımsız, menteşe tablosu boş kalır ve
                // üretilen tek duruş bind'in (açık elin) kendisidir.
                HingeCache[hand] = Array.Empty<Hinge>();
                return;
            }

            var hinges = new List<Hinge>(ids.Length);

            for (int finger = 0; finger < FingerCount; finger++)
            {
                HandJointId[] chain = ChainOf(finger);
                if (chain == null || chain.Length < 2)
                {
                    continue;
                }

                float[] maxAngles = finger == (int)HandFinger.Thumb ? ThumbMaxAngles : FingerMaxAngles;
                int drivable = 0;

                // Son eleman UÇTUR: döndürülmez ama bir önceki eklemin kemik yönünü tanımlar.
                for (int j = 0; j < chain.Length - 1; j++)
                {
                    int raw = (int)chain[j];
                    int next = (int)chain[j + 1];
                    int slot = FingersMetadata.HandJointIdToIndex(chain[j]);
                    if (slot < 0 || slot >= bind.Length || raw < 0 || raw >= joints.Length)
                    {
                        continue;
                    }

                    // ⚠️ Metakarpallar SÜRÜLMEZ (gerekçe sınıf uyarısında). Sürülebilir eklem sayacı
                    // da bu yüzden metakarpı ATLAR — açı tablosu proksimalden başlar.
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
                    hinges.Add(new Hinge
                    {
                        Slot = slot,
                        Finger = finger,
                        AxisLocal = Quaternion.Inverse(parentRotation) * axis,
                        Bind = joints[raw].pose.rotation,
                        MaxAngle = maxAngles[Mathf.Min(drivable, maxAngles.Length - 1)],
                    });

                    drivable++;
                }
            }

            HingeCache[hand] = hinges.ToArray();
        }

        /// <summary>Beş orandan eklem dizisi üretir (boş elin duruşu buradan çıkar).</summary>
        private static Quaternion[] BuildProfile(in HandPoseProfile profile, bool rightHand)
        {
            Quaternion[] result = Copy(Bind(rightHand));
            Hinge[] hinges = Hinges(rightHand);

            for (int i = 0; i < hinges.Length; i++)
            {
                Hinge hinge = hinges[i];
                if (hinge.Slot < 0 || hinge.Slot >= result.Length)
                {
                    continue;
                }

                float curl = Mathf.Clamp01(profile.Get(hinge.Finger));
                result[hinge.Slot] = Quaternion.AngleAxis(curl * hinge.MaxAngle, hinge.AxisLocal) *
                                     hinge.Bind;
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
