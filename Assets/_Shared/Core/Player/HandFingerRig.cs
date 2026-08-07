using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Bir elin parmak zincirlerini <b>bind pozunda bir kez</b> çözüp, sonra
    /// <see cref="HandPoseProfile"/>'daki kapanma oranlarını o iskeletin KENDİ eksenlerinde
    /// uygulayan sürücü.
    /// <para>
    /// ⚠️ <b>Bükülme ekseni SABİT YAZILMAZ, ölçülür</b> — <c>HandGripConvention</c> ile aynı
    /// gerekçe: model değiştiğinde (başka bir Mixamo karakteri, başka bir rig) burada tek satır
    /// değişmesin. Eksen = avuç normali × kemik yönü, ikisi de bind pozunda okunur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Avuç normalinin EL FARKI buradan gelmez</b>: <see cref="HandGripConvention"/> zaten
    /// sol/sağ çapraz çarpım sırasını tanımlıyor ve o kuralın projedeki tek uygulaması orasıdır.
    /// Burada yalnız onun döndürdüğü bazdan avuç normali okunur — sıra kuralı kopyalanmaz.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kurulum bind pozunda yapılmalıdır</b> (retargeter kemiklere yazmadan önce, yani
    /// <c>Awake</c>): duruşu bozulmuş bir iskelette ölçülen eksen o karenin kıvrımını içerir ve
    /// parmaklar kalıcı olarak yanlış yöne bükülür.
    /// </para>
    /// </summary>
    public class HandFingerRig
    {
        /// <summary>Parmak başına sürülen eklem sayısı (uç kemiği döndürülmez).</summary>
        private const int JointsPerFinger = 3;

        private const int FingerCount = 5;

        /// <summary>Kemik adlarındaki parmak ekleri — sıra <see cref="HandPoseProfile.Get"/> ile aynı.</summary>
        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        /// <summary>Bilek kemiklerinin adları (Mixamo humanoid). ⚠️ Parmak kemikleri bu adın
        /// ÜSTÜNE ek alır (<c>…LeftHandIndex1</c>), yani arama tam eşleşme olmak zorundadır —
        /// "başlıyorsa" araması bileği ilk parmakla karıştırırdı.</summary>
        public const string LeftWristBoneName = "mixamorig:LeftHand";

        /// <inheritdoc cref="LeftWristBoneName"/>
        public const string RightWristBoneName = "mixamorig:RightHand";

        /// <summary>
        /// Tam kapanmada eklem başına derece. Başparmak ayrı: anatomik olarak daha az kıvrılır ve
        /// kabzayı sarmak yerine üstüne yatar.
        /// </summary>
        private static readonly float[] FingerMaxAngles = { 50f, 60f, 40f };
        private static readonly float[] ThumbMaxAngles = { 25f, 35f, 30f };

        private readonly Transform[] _bones = new Transform[FingerCount * JointsPerFinger];
        private readonly Quaternion[] _bindLocalRotations = new Quaternion[FingerCount * JointsPerFinger];
        private readonly Vector3[] _bendAxes = new Vector3[FingerCount * JointsPerFinger];

        /// <summary>
        /// Gövde kökünden bileği adıyla bulup <see cref="TryBuild"/>'e verir — kemik adı bilgisi
        /// bu sınıfın dışına sızmasın diye.
        /// </summary>
        public static HandFingerRig TryBuildFromBody(Transform bodyRoot, bool rightHand)
        {
            if (bodyRoot == null)
            {
                return null;
            }

            string wanted = rightHand ? RightWristBoneName : LeftWristBoneName;
            Transform[] all = bodyRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == wanted)
                {
                    return TryBuild(all[i], rightHand);
                }
            }

            return null;
        }

        /// <summary>
        /// Bileğin altındaki parmak zincirlerini çözer ve bükülme eksenlerini ölçer.
        /// <para>En az bir eklem çözülemezse <c>null</c> döner — yarım kurulmuş bir el, sessizce
        /// bazı parmakları oynatıp bazılarını dondurmaktan iyidir.</para>
        /// </summary>
        /// <param name="wrist">Bilek kemiği (<c>mixamorig:LeftHand</c> gibi).</param>
        /// <param name="rightHand">Sağ el mi (avuç normalinin işareti için).</param>
        public static HandFingerRig TryBuild(Transform wrist, bool rightHand)
        {
            if (wrist == null)
            {
                return null;
            }

            var rig = new HandFingerRig();
            var chains = new Transform[FingerCount][];

            for (int finger = 0; finger < FingerCount; finger++)
            {
                chains[finger] = ResolveChain(wrist, FingerNames[finger]);
                if (chains[finger] == null)
                {
                    return null;
                }
            }

            // Avuç normali: orta parmak ve başparmak KÖK eklemlerinden ölçülür (bind pozunda).
            if (!HandGripConvention.TryMeasureBoneBasis(
                    wrist, chains[2][0], chains[0][0], rightHand, out Quaternion boneBasis))
            {
                return null;
            }

            Vector3 palmNormalWorld = wrist.TransformDirection(boneBasis * Vector3.up);
            if (palmNormalWorld.sqrMagnitude < 1e-8f)
            {
                return null;
            }

            palmNormalWorld = palmNormalWorld.normalized;

            for (int finger = 0; finger < FingerCount; finger++)
            {
                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    Transform bone = chains[finger][joint];
                    Transform next = chains[finger][joint + 1];
                    int slot = finger * JointsPerFinger + joint;

                    Vector3 boneDirection = next.position - bone.position;
                    if (boneDirection.sqrMagnitude < 1e-8f || bone.parent == null)
                    {
                        return null;
                    }

                    // ⚠️ Sıra Cross(avuç normali, kemik yönü)'dür ve TERSİ DEĞİLDİR: bu eksen
                    // etrafında pozitif dönüş parmak ucunu avuç normalinin TERSİNE, yani avucun
                    // İÇİNE taşır. Ters yazılırsa parmaklar el sırtına doğru kırılır.
                    Vector3 axisWorld = Vector3.Cross(palmNormalWorld, boneDirection.normalized);
                    if (axisWorld.sqrMagnitude < 1e-8f)
                    {
                        return null;
                    }

                    rig._bones[slot] = bone;
                    rig._bindLocalRotations[slot] = bone.localRotation;

                    // Eksen EBEVEYN çerçevesinde saklanır: menteşe ekseni eklemde sabittir ve
                    // ebeveyn döndükçe onunla birlikte döner — tam olarak bir parmak eklemi gibi.
                    rig._bendAxes[slot] = bone.parent.InverseTransformDirection(axisWorld).normalized;
                }
            }

            return rig;
        }

        /// <summary>Duruşu uygular. Kare başına çağrılır; ölçüm yapılmaz, yalnız yazılır.</summary>
        public void Apply(in HandPoseProfile profile)
        {
            for (int finger = 0; finger < FingerCount; finger++)
            {
                float curl = Mathf.Clamp01(profile.Get(finger));
                float[] maxAngles = finger == 0 ? ThumbMaxAngles : FingerMaxAngles;

                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    int slot = finger * JointsPerFinger + joint;
                    Transform bone = _bones[slot];
                    if (bone == null)
                    {
                        continue; // sahne değişiminde yıkılmış olabilir
                    }

                    bone.localRotation =
                        Quaternion.AngleAxis(curl * maxAngles[joint], _bendAxes[slot]) *
                        _bindLocalRotations[slot];
                }
            }
        }

        /// <summary>
        /// <c>&lt;bilek&gt;/…Thumb1/…Thumb2/…Thumb3/…Thumb4</c> zincirini çözer.
        /// <para>Dört kemik istenir: son kemik döndürülmez ama <b>yön ölçmek için</b> gerekir —
        /// bir eklemin bükülme ekseni ancak kendisinden sonraki noktayla tanımlıdır.</para>
        /// <para>⚠️ Ad araması iki seviye <c>Find</c> ile DEĞİL, tam ad eşleşmesiyle alt ağaçtan
        /// yapılır: Mixamo parmak kemikleri bileğin doğrudan çocuğu olsa da ara düğüm ekleyen
        /// modeller var ve o durumda sessizce poz uygulanmaması, teşhis edilmesi en zor sonuçtur.
        /// </para>
        /// </summary>
        private static Transform[] ResolveChain(Transform wrist, string fingerName)
        {
            string prefix = wrist.name + fingerName;
            var chain = new Transform[JointsPerFinger + 1];
            Transform[] all = wrist.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < chain.Length; i++)
            {
                string wanted = prefix + (i + 1).ToString();
                for (int b = 0; b < all.Length; b++)
                {
                    if (all[b] != null && all[b].name == wanted)
                    {
                        chain[i] = all[b];
                        break;
                    }
                }

                if (chain[i] == null)
                {
                    return null;
                }
            }

            return chain;
        }
    }
}
