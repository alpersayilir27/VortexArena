using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// "Bu eşya bu elde NASIL durur" sorusunun <b>etkin</b> cevabı — iki kaynağın bilinçli
    /// bileşimi:
    /// <para>
    /// <b>ROTASYON kimliktir</b> (<see cref="ItemDefinition.PrimaryGripRotation"/>): eşyanın
    /// eksenleri kumanda anchor'ıyla birebir aynıdır. Yakalanan kavramanın rotasyonu eşyaya HİÇ
    /// karışmaz — o, EL MODELİNİN eşya üstündeki duruşudur (<see cref="HandGripPoser"/> bileği ona
    /// kilitler). Eşyayı da o dönüşle döndürmek "kumandayı uzatınca namlu başka yöne bakıyor"
    /// demekti; kumanda nereye, namlu oraya.
    /// </para>
    /// <para>
    /// <b>POZİSYON yakalamadan gelir:</b> yakalamanın bileği eşyanın neresindeyse, eşya öyle
    /// ötelenir ki o nokta oyuncunun CANLI bileğine
    /// (<see cref="HandGripPoser.TryGetAnchorToWrist"/> deltasının konumu) otursun — yani el
    /// kabzanın üstünde durur, eşya elin içinden kaymaz.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçek tuzağı burada YOKTUR:</b> yakalama zaten ölçeksiz metredir
    /// (<see cref="ItemGripCapture"/>), yani hiçbir yerde eşyanın görsel ölçeğiyle (<c>WPN_*</c>
    /// kökleri 0.8) çarpılmaz ve ölçekli/ölçeksiz bileşim ayrımı yapmak gerekmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sol ve sağ AYRI çözülür.</b> Yakalama el başınadır; kabza simetrik olmadığı için iki
    /// elin bilek noktası eşyanın farklı yerlerine düşer.
    /// </para>
    /// <para>
    /// ⚠️ <b>Protokolde karşılığı YOKTUR ve tel formatı değişmez</b> (§6.6): duruş yine telde
    /// gitmiyor, iki uç da aynı tanımdan aynı kaydı okuyup aynı deltayı ölçüyor. Uzak uçta da bu
    /// sınıf koşar (<c>RemoteAvatar</c>), yoksa aynı silah iki ekranda iki ayrı duruşta çizilirdi.
    /// </para>
    /// <para>
    /// <b>Çözülemediğinde <c>false</c> döner ve çağıran tanımın ham kaydına düşer</b>
    /// (<see cref="ItemDefinition.PrimaryGripPosition(bool)"/>): kavraması yakalanmamış eşya, rig'i
    /// olmayan oturum (admin gözlemci, editör) ve ilk kare bu yoldan geçer. Fark yalnız canlı bilek
    /// deltasının düzeltmesidir — fallback bileği anchor'la aynı yerde varsayar.
    /// </para>
    /// </summary>
    public static class ItemGripAuthority
    {
        /// <summary>
        /// Ana kavramanın etkin ofsetini çözer: çıktı, <see cref="ItemDefinition.PrimaryGripPosition(bool)"/>
        /// / <see cref="ItemDefinition.PrimaryGripRotation"/> ile <b>AYNI anlamdadır</b> (anchor
        /// uzayında, metre) — yani <c>itemPos = palm.pos + palm.rot * gripPosition</c>,
        /// <c>itemRot = palm.rot * gripRotation</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Denklem: rotasyon sabittir (kimlik); pozisyon, yakalamanın bilek NOKTASI canlı bilek
        /// noktasına çakışacak şekilde çözülür — <c>gripPosition + bilekEşyada = delta.pozisyon</c>
        /// eşitliğinden. Yakalamanın ve deltanın ROTASYONU eşyaya girmez (el modeline girer,
        /// buraya değil).
        /// </para>
        /// <para>
        /// ⚠️ Eşyanın kendi transformu OKUNMAZ (parametre olarak da alınmaz): ölçünün iki ucu da
        /// eşya-yerel/anchor-yereldir, yani eşya nerede durursa dursun sonuç aynıdır. Transformu
        /// işin içine sokmak, çağıranı "silahı taşımadan önce mi sonra mı sorayım" sorusuyla baş
        /// başa bırakırdı.
        /// </para>
        /// </remarks>
        public static bool TryResolvePrimaryGrip(ItemDefinition definition, bool rightHand,
            out Vector3 gripPosition, out Quaternion gripRotation)
        {
            gripPosition = Vector3.zero;
            gripRotation = Quaternion.identity;

            if (definition == null || !definition.HasGrip(GripSocketKind.Primary, rightHand))
            {
                return false;
            }

            if (!HandGripPoser.TryGetAnchorToWrist(rightHand, out Pose anchorToWrist))
            {
                return false;
            }

            gripRotation = Quaternion.identity;
            gripPosition = anchorToWrist.position -
                           definition.GetGrip(GripSocketKind.Primary, rightHand).position;
            return true;
        }

        /// <summary>
        /// Ana kavrama noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — aynı ölçünün ters yönü.
        /// <para>
        /// <see cref="ItemDefinition.PrimaryGripPointOnItem(bool)"/>'ın parametreli ikizidir ve türetmesi
        /// birebir aynıdır (<c>item.TransformPoint(s) == hand.position</c> koşulundan
        /// <c>s = Inverse(R) * (-P)</c>). ⚠️ Ters yön hesapları (uzak elin avuç hedefi, soket çizimi)
        /// etkin kavrama çözüldüğünde <b>bunu</b> kullanmak zorundadır: biri canlı deltadan, öteki
        /// ham yakalamadan beslenirse el ile silah aynı karede birbirinden ayrışır.
        /// </para>
        /// </summary>
        public static Vector3 GripPointOnItem(in Vector3 gripPosition, in Quaternion gripRotation)
        {
            return Quaternion.Inverse(gripRotation) * (-gripPosition);
        }
    }
}
