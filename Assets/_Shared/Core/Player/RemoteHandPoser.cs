using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak avatarın elini <b>elindeki eşyaya oturtur</b>: parmakları eşyanın duruşundan sürer
    /// (§6.9 — parmaklar telde gitmez; boşta <c>Idle</c>, eşya tutarken slotun preset'i, ikisi
    /// arasında <see cref="HandPoseLibrary.TransitionSeconds"/>'lik yumuşak geçiş) ve kolu, bileği
    /// eşyanın kavrama noktasına götürecek biçimde çözer (<see cref="TwoBoneIk"/>).
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
        /// <summary>
        /// Bir elin parmak duruşu geçişi — yerel elin (<c>HandGripPoser</c>) uzak aynası: hedef
        /// duruş değişince o anki gösterilen duruştan yenisine
        /// <see cref="HandPoseLibrary.TransitionSeconds"/> boyunca gidilir. Uzak avatarın parmakları
        /// da böylece silah alınınca kapanır, bırakınca açılır — anında zıplamaz ve yerel elle aynı
        /// hızda hareket eder.
        /// </summary>
        private struct PoseBlend
        {
            private HandPoseProfile _from;
            private HandPoseProfile _target;
            private HandPoseProfile _shown;
            private float _progress;
            private bool _started;

            /// <summary>Hedefi verir, bir karelik ilerlemeyi uygular ve bu karede çizilecek duruşu döner.</summary>
            public HandPoseProfile Step(in HandPoseProfile target, float deltaTime)
            {
                if (!_started)
                {
                    // İlk kare: geçiş yok, doğrudan hedefte başla (avatar doğduğunda parmaklar
                    // boştan kapanıyormuş gibi görünmesin).
                    _started = true;
                    _from = target;
                    _target = target;
                    _shown = target;
                    _progress = 1f;
                    return _shown;
                }

                if (!target.Approximately(_target))
                {
                    _from = _shown;
                    _target = target;
                    _progress = 0f;
                }

                if (_progress < 1f)
                {
                    _progress = HandPoseLibrary.TransitionSeconds > 0f
                        ? Mathf.Min(1f, _progress + deltaTime / HandPoseLibrary.TransitionSeconds)
                        : 1f;
                    _shown = HandPoseProfile.Lerp(_from, _target, HandPoseLibrary.Ease(_progress));
                }
                else
                {
                    _shown = _target;
                }

                return _shown;
            }
        }

        private RemoteAvatar _avatar;
        private HandFingerRig _left;
        private HandFingerRig _right;
        private TwoBoneIk _leftArm;
        private TwoBoneIk _rightArm;
        private PoseBlend _leftBlend;
        private PoseBlend _rightBlend;

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

            float dt = Time.deltaTime;
            _left.Apply(_leftBlend.Step(_avatar.ResolveHandPose(false), dt));
            _right.Apply(_rightBlend.Step(_avatar.ResolveHandPose(true), dt));

            ApplyGrip(_left, _leftArm, false);
            ApplyGrip(_right, _rightArm, true);
        }

        /// <summary>
        /// Bir eli eşyanın kavrama noktasına oturtur: kol IK ile hedefe uzanır, sonra bileğin
        /// yönü kavramadan yazılır.
        /// <para>
        /// ⚠️ <b>Hedef, kaydın BİLEK pozudur</b> (<c>RemoteAvatar.TryResolveGripWrist</c>), kumanda
        /// anchor'ının kendisi değil: elin o kabzanın üstünde nasıl durduğu (yandan mı alttan mı)
        /// kaydın el yarısında yazılı ve <b>yerel el de tam olarak onu okuyor</b>
        /// (<c>HandGripPoser</c>). Anchor'ı doğrudan hedeflemek, aynı silahı gözlükte bir türlü
        /// gözlemci ekranında başka türlü tutmak demekti — bilek ön kola dönük kalınca da belirti
        /// "uzak oyuncunun eli yok" oluyordu. <see cref="HandFingerRig.WristCorrection"/> ondan
        /// SONRA gelir ve ayrı bir iş yapar: kavrama uzayındaki bileği humanoid kemiğe çevirir.
        /// </para>
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
            if (rig == null || rig.Wrist == null || !_avatar.TryResolveGripWrist(rightHand, out Pose wrist))
            {
                return;
            }

            arm?.Solve(wrist.position);
            rig.Wrist.rotation = wrist.rotation * rig.WristCorrection;
        }
    }
}
