using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak avatarın parmaklarını <b>elindeki eşyaya göre</b> sürer (§6.9 — parmaklar telde
    /// gitmez).
    /// <para>
    /// ⚠️ <b>Execution order 100 ile 30100 ARASINDA olmak zorunda.</b> Alt sınır SDK'dır
    /// (<c>NetworkCharacterHandler</c>, 100): iskeleti o yazıyor, ondan önce yazılan parmak aynı
    /// karede eziliyordu. Üst sınır <see cref="SkeletonPoseMirror"/>'dır (30100): kırmızı takım
    /// gövdesi karakterin <c>localRotation</c>'larını kopyalıyor, yani parmakları ondan ÖNCE
    /// yazmak ikinci gövdeyi bedavaya doğru yapar — ayrı bir parmak kurulumu gerekmez. Aynısı
    /// hayalet gövde için de geçerlidir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sahneye/prefaba KONMAZ</b>, <see cref="RemoteAvatar"/> onu <c>Awake</c>'te
    /// ekler. Sebep kurulum kolaylığı değil <b>zamanlama</b>: parmak eksenleri bind pozunda
    /// ölçülmek zorunda (<see cref="HandFingerRig"/>) ve prefaba konmuş bir bileşenin kendi
    /// <c>Awake</c>'inin iskelet sürülmeden önce koştuğu garanti değil.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30050)]
    public class RemoteHandPoser : MonoBehaviour
    {
        private RemoteAvatar _avatar;
        private HandFingerRig _left;
        private HandFingerRig _right;

        /// <summary>
        /// Karakterin parmak zincirlerini bind pozunda çözer. Çözülemezse bileşen kendini kapatır:
        /// yarım sürülen bir el, hiç sürülmeyenden daha kötü teşhis edilir.
        /// </summary>
        internal void Bind(RemoteAvatar avatar, Transform bodyRoot)
        {
            _avatar = avatar;
            _left = HandFingerRig.TryBuildFromBody(bodyRoot, false);
            _right = HandFingerRig.TryBuildFromBody(bodyRoot, true);

            if (_left != null && _right != null)
            {
                return;
            }

            enabled = false;
            Debug.LogWarning(
                $"[RemoteHandPoser] Parmak zinciri çözülemedi ('{HandFingerRig.LeftWristBoneName}' / " +
                $"'{HandFingerRig.RightWristBoneName}' altında Thumb/Index/Middle/Ring/Pinky 1-4). " +
                "Uzak eller bind pozunda (düz) kalacak. Karakter modeli değiştiyse kemik adı " +
                "sabitlerini HandFingerRig'de güncelle.", avatar);
        }

        private void LateUpdate()
        {
            if (_avatar == null)
            {
                return;
            }

            _left.Apply(_avatar.ResolveHandPose(false));
            _right.Apply(_avatar.ResolveHandPose(true));
        }
    }
}
