using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// "Kumanda anchor'ı verildiğinde oyuncunun AVUCU nerede duruyor" sorusunun TEK cevabı.
    /// <para>
    /// <b>Neden gerekli:</b> kavramanın referansı bugüne kadar <c>OVRCameraRig.leftHandAnchor</c> /
    /// <c>rightHandAnchor</c> idi, oysa oyuncunun gördüğü şey kumanda değil <b>sentetik eldir</b> —
    /// anchor ile avuç arasındaki birkaç santimlik fark, silahı elin içinden geçmiş ya da havada
    /// duruyor gösteriyor. Bu sınıf o farkı tek yerde tanımlar; anchor'a doğrudan bakan her tüketici
    /// (<see cref="VortexArena.Core.Combat.Weapon"/>, <c>WeaponGranter</c>, <c>ItemGripSockets</c>,
    /// <c>WeaponFrame</c>, <see cref="RemoteAvatar"/>) buradan geçer.
    /// </para>
    /// <para>
    /// ⚠️ <b>Uzak taraf da buradan geçmek ZORUNDA</b>: telde giden el pozu anchor pozudur (§6.6),
    /// ofset iki uçta da aynı yerden uygulanmazsa aynı silah iki ekranda iki ayrı duruşta çizilir.
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="HandGripConvention"/> ile karıştırma:</b> o "anchor uzayında el hangi YÖNE
    /// bakıyor" sorusunu (humanoid bileğe köprü) cevaplıyor, burası "anchor'a göre avuç NEREDE"
    /// sorusunu. İkisi ayrı sabitlerdir ve ayrı kalmalıdır — birleştirilirse uzak gövdenin bileği
    /// ile silahın duruşu tek sayıya bağlanır, biri ayarlanınca öteki bozulur.
    /// </para>
    /// </summary>
    public static class HandGripPivot
    {
        /// <summary>
        /// Kumanda anchor'ından SOL avuca ofset — anchor uzayında, METRE. <b>SIFIRDIR:</b> avuç,
        /// kumanda anchor'ının ta kendisi sayılır.
        /// <para>
        /// ⚠️ <b>Buraya tahmin edilmiş bir sayı YAZILMAZ.</b> Burada duran değer ölçülmemiş bir
        /// ergonomi tahminiydi ve iki zarar veriyordu: (1) silahın yerini kumandanın gerçek
        /// pozundan koparıyor, yani "silah tam elimde durmuyor" şikâyetine ikinci, gizli bir terim
        /// ekliyordu; (2) altı silahın kavrama verisi zaten bu ofsetin ÜSTÜNE ayarlandığı için iki
        /// ayrı düğme aynı şeyi ayarlıyordu ve hangisinin bozuk olduğu ayırt edilemiyordu.
        /// Silahın elde nerede durduğu tek yerden gelir: <c>WD_*.asset</c>'e yakalanmış kavrama
        /// kaydı (<see cref="VortexArena.Core.Combat.ItemGripCapture"/>).
        /// </para>
        /// <para>
        /// Gerçekten ölçülmüş bir bilek ofseti gerekirse (<see cref="HandGripCalibrationProbe"/>
        /// başlıkta çalıştırılıp okunur) yeri yine burasıdır — sınıf tam bu yüzden duruyor.
        /// Kimlik dönüşümün arkasında bir kapı bırakmak <c>ArenaSpace</c> ile aynı desendir.
        /// </para>
        /// </summary>
        public static readonly Vector3 LeftPalmOffset = Vector3.zero;

        /// <summary>Kumanda anchor'ından SAĞ avuca ofset — gerekçe
        /// <see cref="LeftPalmOffset"/>'te.</summary>
        public static readonly Vector3 RightPalmOffset = Vector3.zero;

        /// <summary>El başına avuç ofseti (anchor uzayı, metre).</summary>
        public static Vector3 PalmOffset(bool rightHand)
        {
            return rightHand ? RightPalmOffset : LeftPalmOffset;
        }

        /// <summary>
        /// Anchor pozundan avuç pozunu türetir.
        /// <para>
        /// ⚠️ <b>Rotasyon BİLEREK anchor'ın kendisidir</b> ve
        /// <see cref="HandGripConvention.AnchorBasis"/> buraya karıştırılmaz: o baz uzak gövdenin
        /// bileğini humanoid eksene köprülemek için var. İkisi tek sabite bağlanırsa bileği
        /// düzeltmek silahın duruşunu bozar (ve tersi). Ayrıca yakalanan kavrama kayıtları anchor
        /// ekseninde ölçülmüştür — dönüşü değiştirmek her silahın kavramasını bir anda geçersiz
        /// kılardı.
        /// </para>
        /// <para>⚠️ <c>Transform.TransformPoint</c> DEĞİL elle bileşim: ofset METRE cinsindendir,
        /// rig'in ölçeği 1 olmasa bile büyütülmemeli (projede tekrarlanan kural).</para>
        /// </summary>
        public static Pose Resolve(in Pose anchor, bool rightHand)
        {
            return new Pose(
                anchor.position + anchor.rotation * PalmOffset(rightHand),
                anchor.rotation);
        }

        /// <summary>
        /// <see cref="Resolve(in Pose, bool)"/>'un <see cref="Transform"/> kolaylığı.
        /// <para>⚠️ <c>null</c> denetimi YOKTUR ve eklenmez: tüm çağıranlar anchor'ı zaten
        /// "rig var mı" sorusuyla çözüyor (<c>WeaponGranter.ResolveHandAnchor</c>) ve burada
        /// sessizce <c>default</c> dönmek silahı dünya orijinine yapıştırırdı.</para>
        /// </summary>
        public static Pose Resolve(Transform anchor, bool rightHand)
        {
            return Resolve(new Pose(anchor.position, anchor.rotation), rightHand);
        }

        /// <summary>
        /// Kontrolcü sağ el mi. <c>None</c> (çözülemeyen el) SAĞ sayılır —
        /// <c>Weapon.IsMainHandRight</c> ile AYNI kural: telde "bilinmeyen el" diye bir değer yok.
        /// </summary>
        public static bool IsRight(OVRInput.Controller hand)
        {
            return hand != OVRInput.Controller.LTouch;
        }
    }
}
