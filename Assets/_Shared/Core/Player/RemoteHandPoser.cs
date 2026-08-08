using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak avatarın elini <b>elindeki eşyaya oturtur</b>: parmakları eşyanın duruşundan sürer
    /// (§6.9 — parmaklar telde gitmez) ve kolu, bileği eşyanın kavrama noktasına götürecek biçimde
    /// çözer (<see cref="TwoBoneIk"/>).
    /// <para>
    /// <b>Kolun neden sürülmesi gerekiyor:</b> eşya, ana elin <b>kumanda anchor'ı</b> pozundan
    /// çiziliyor (<c>RemoteAvatar.ApplyItemPoses</c>); uzakta çizilen el ise retarget edilmiş
    /// <b>anatomik bilek</b>. İki nokta aynı yer değil (aradaki fark <see cref="HandGripPivot"/>'un
    /// henüz ölçülmemiş avuç ofseti + retarget hatasıdır) ve arada onları birleştiren hiçbir şey
    /// yoktu — belirti "herkesin silahı elinin biraz ilerisinde duruyor" oluyordu. Yerelde aynı
    /// boşluk görünmüyor çünkü <c>HandGripPoser</c> sentetik bileği kavrama pozuna <b>sert
    /// kilitliyor</b>; burası onun uzak aynasıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bileğin KONUMU yazılmaz, kol döndürülür.</b> Bileği doğrudan taşımak kemik uzunluğunu
    /// değiştirir ve <see cref="SkeletonPoseMirror"/> kırmızı takım gövdesine yalnız
    /// <c>localRotation</c> kopyaladığı için ikinci gövde takip edemezdi (gerekçenin tamamı
    /// <see cref="TwoBoneIk"/>'te).
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçek (§10.8) burada ayrıca ele alınMAZ ve alınmamalı:</b> hedef eşyanın dünya
    /// pozudur, kol ise ölçeklenmiş iskeletin kendi kemikleriyle çözülür — yani boy çarpanı ne
    /// olursa olsun el eşyanın üstüne gelir. İkinci bir ölçek terimi eklemek düzeltmeyi iki kez
    /// uygulamak olurdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Execution order 100 ile 30100 ARASINDA olmak zorunda.</b> Alt sınır SDK'dır
    /// (<c>NetworkCharacterHandler</c>, 100): iskeleti o yazıyor, ondan önce yazılan parmak/kol aynı
    /// karede eziliyordu. Üst sınır <see cref="SkeletonPoseMirror"/>'dır (30100): kırmızı takım
    /// gövdesi karakterin <c>localRotation</c>'larını kopyalıyor, yani ondan ÖNCE yazmak ikinci
    /// gövdeyi bedavaya doğru yapar — ayrı bir el/kol kurulumu gerekmez. Aynısı hayalet gövde için
    /// de geçerlidir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sahneye/prefaba KONMAZ</b>, <see cref="RemoteAvatar"/> onu <c>Awake</c>'te
    /// ekler. Sebep kurulum kolaylığı değil <b>zamanlama</b>: parmak eksenleri ve bilek düzeltmesi
    /// bind pozunda ölçülmek zorunda (<see cref="HandFingerRig"/>) ve prefaba konmuş bir bileşenin
    /// kendi <c>Awake</c>'inin iskelet sürülmeden önce koştuğu garanti değil.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30050)]
    public class RemoteHandPoser : MonoBehaviour
    {
        private RemoteAvatar _avatar;
        private HandFingerRig _left;
        private HandFingerRig _right;
        private TwoBoneIk _leftArm;
        private TwoBoneIk _rightArm;

        /// <summary>
        /// Karakterin parmak zincirlerini bind pozunda çözer. Çözülemezse bileşen kendini kapatır:
        /// yarım sürülen bir el, hiç sürülmeyenden daha kötü teşhis edilir.
        /// </summary>
        internal void Bind(RemoteAvatar avatar, Transform bodyRoot)
        {
            _avatar = avatar;
            _left = HandFingerRig.TryBuildFromBody(bodyRoot, false);
            _right = HandFingerRig.TryBuildFromBody(bodyRoot, true);

            if (_left == null || _right == null)
            {
                enabled = false;
                Debug.LogWarning(
                    $"[RemoteHandPoser] Parmak zinciri çözülemedi ('{HandFingerRig.LeftWristBoneName}' / " +
                    $"'{HandFingerRig.RightWristBoneName}' altında Thumb/Index/Middle/Pinky 1-4). " +
                    "Uzak eller bind pozunda (düz) kalacak. Karakter modeli değiştiyse kemik adı " +
                    "sabitlerini HandFingerRig'de güncelle.", avatar);
                return;
            }

            // ⚠️ Kol zinciri bilekten YUKARI çıkılarak bulunur, adla aranmaz: bilek zaten adıyla
            // çözüldü ve humanoid'de onun iki üstü daima ön kol + üst koldur. İkinci bir ad sabiti
            // açmak, karakter değiştiğinde güncellenmeyi unutulacak ikinci bir yer olurdu.
            _leftArm = TwoBoneIk.TryBuild(_left.Wrist);
            _rightArm = TwoBoneIk.TryBuild(_right.Wrist);

            if (_leftArm == null || _rightArm == null)
            {
                Debug.LogWarning(
                    "[RemoteHandPoser] Kol zinciri çözülemedi (bileğin iki üstünde kemik yok ya da " +
                    "çok kısa). Parmaklar sürülecek ama el eşyaya OTURMAYACAK: silah, çizilen elin " +
                    "birkaç santim ilerisinde durur.", avatar);
            }
        }

        /// <summary>
        /// ⚠️ Kapı <see cref="_avatar"/> ile sınırlı DEĞİL, parmak zincirlerini de kapsar:
        /// <see cref="Bind"/> zinciri çözemediğinde kendini <c>enabled = false</c> ile kapatıyor,
        /// ama bu bileşen <see cref="RemoteAvatar"/>'ın <c>Awake</c>'inde <c>AddComponent</c> ile
        /// ekleniyor ve o anda yazılan <c>enabled</c> her zaman tutmuyor. Tutmadığında burası
        /// kare başına <c>NullReferenceException</c> basar (saniyede ~90 satır) ve istisna
        /// <see cref="ApplyGrip"/>'ten ÖNCE atıldığı için uzak eller silaha da hiç oturmaz —
        /// yani "kapandı" sanılan bileşen sessizce değil, gürültüyle ve iki işi birden bozarak
        /// koşar. Alanların kendisi kontrol edilince kapanmanın tutup tutmaması önemsizleşir.
        /// </summary>
        private void LateUpdate()
        {
            if (_avatar == null || _left == null || _right == null)
            {
                return;
            }

            _left.Apply(_avatar.ResolveHandPose(false));
            _right.Apply(_avatar.ResolveHandPose(true));

            ApplyGrip(_left, _leftArm, false);
            ApplyGrip(_right, _rightArm, true);
        }

        /// <summary>
        /// Bir eli eşyanın kavrama noktasına oturtur: kol IK ile hedefe uzanır, sonra bileğin
        /// yönü kavramadan yazılır.
        /// <para>
        /// ⚠️ <b>Sıra önemlidir</b> — önce kol, sonra bilek. IK üst kolu döndürdüğü için bileğin
        /// dünya rotasyonu da değişiyor; bileği önce yazmak onu bir sonraki satırda ezerdi.
        /// </para>
        /// <para>
        /// ⚠️ <b>Elde eşya yoksa kola HİÇ DOKUNULMAZ</b> (hedef yok demek "sıfıra git" demek
        /// değildir): boştaki kol retargeting'in bulduğu yerde kalır, yalnız parmakları idle
        /// duruşundadır.
        /// </para>
        /// </summary>
        private void ApplyGrip(HandFingerRig rig, TwoBoneIk arm, bool rightHand)
        {
            if (rig == null || rig.Wrist == null || !_avatar.TryResolveGripPalm(rightHand, out Pose palm))
            {
                return;
            }

            arm?.Solve(palm.position);
            rig.Wrist.rotation = palm.rotation * rig.WristCorrection;
        }
    }
}
