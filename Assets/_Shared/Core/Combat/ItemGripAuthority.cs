using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// "Bu eşya bu elde NASIL durur" sorusunun <b>etkin</b> cevabı — iki uzay arasındaki köprü.
    /// <para>
    /// <b>İki uzay:</b> kavrama kaydı (<see cref="ItemGripPose"/>) bileğin eşyaya göre pozudur, yani
    /// <b>BİLEK</b> uzayında yazılır. Eşyanın dünya pozunu çözen taraf (<see cref="ItemGripSolver"/>)
    /// ve tel (§6.6) ise elin <b>ANCHOR</b> pozunu biliyor. Aradaki fark elin donanımdaki
    /// anchor→bilek deltasıdır ve bu sınıf kaydı o deltayla anchor uzayına çevirir.
    /// </para>
    /// <para>
    /// <b>Delta üç basamakta çözülür</b> (<see cref="ResolveAnchorToWrist"/>): canlı ölçüm
    /// (<see cref="HandGripPoser.TryGetAnchorToWrist"/>) → ölçülmüş sabit
    /// (<see cref="HandGripConvention.AnchorToWrist"/>) → kimlik. Delta kumanda sürümlü el
    /// pozlarından türediği için deterministiktir: aynı kumanda, aynı SDK, aynı sonuç — yani her
    /// başlıkta AYNI değer ölçülür.
    /// </para>
    /// <para>
    /// <b>Artık DÖNÜŞ de kayıttan gelir</b> (eskiden kimlikti): eşya ele göre durur
    /// (<c>item = bilek ∘ Inverse(kayıt)</c>), yani el nasıl tutuyorsa silah öyle durur. Bileğin
    /// kendisi ana elde KİLİTLENMEZ — el izlemeden/kumandadan gelir, silah ona uyar.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçek tuzağı burada YOKTUR:</b> kayıt zaten ölçeksiz metredir
    /// (<see cref="ItemGripPose"/>), yani hiçbir yerde eşyanın görsel ölçeğiyle (<c>WPN_*</c>
    /// kökleri 0.8) çarpılmaz ve ölçekli/ölçeksiz bileşim ayrımı yapmak gerekmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sol ve sağ AYRI çözülür.</b> Kayıt el başınadır; kabza simetrik olmadığı için iki elin
    /// bilek pozu eşyanın farklı yerlerine düşer.
    /// </para>
    /// <para>
    /// ⚠️ <b>Protokolde karşılığı YOKTUR ve tel formatı değişmez</b> (§6.6): duruş yine telde
    /// gitmiyor, iki uç da aynı tanımdan aynı kaydı okuyup aynı deltayı ölçüyor. Uzak uçta da bu
    /// sınıf koşar (<c>RemoteAvatar</c>), yoksa aynı silah iki ekranda iki ayrı duruşta çizilirdi;
    /// rig'i olmayan izleyici (admin gözlemci) ölçülmüş sabite düşer.
    /// </para>
    /// </summary>
    public static class ItemGripAuthority
    {
        /// <summary>
        /// Ana kavramanın etkin ofsetini çözer: çıktı <b>EŞYANIN ANCHOR'a göre</b> pozudur
        /// (<see cref="ItemGripSolver"/>'ın beklediği anlam) — yani
        /// <c>itemPos = palm.pos + palm.rot * gripPosition</c>,
        /// <c>itemRot = palm.rot * gripRotation</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Denklem: kayıt bileğin eşyaya göre pozudur, tersi (<see cref="ItemGripPose.InverseLocalPose"/>)
        /// eşyanın BİLEĞE göre pozudur; onu deltayla önden çarpmak eşyayı ANCHOR'a göre verir.
        /// </para>
        /// <para>
        /// ⚠️ Eşyanın kendi transformu OKUNMAZ (parametre olarak da alınmaz): ölçünün iki ucu da
        /// eşya-yerel/anchor-yereldir, yani eşya nerede durursa dursun sonuç aynıdır. Transformu
        /// işin içine sokmak, çağıranı "silahı taşımadan önce mi sonra mı sorayım" sorusuyla baş
        /// başa bırakırdı.
        /// </para>
        /// <para>
        /// <b><c>false</c> döndüğünde</b> çağıran tanımın kendi ölçüsüne düşer
        /// (<see cref="ItemDefinition.PrimaryGripPosition(bool)"/>): kavraması yazılmamış eşya bu
        /// yoldan geçer.
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

            Pose itemInWrist = definition.GetGrip(GripSocketKind.Primary, rightHand).InverseLocalPose;
            Pose delta = ResolveAnchorToWrist(rightHand);

            gripRotation = delta.rotation * itemInWrist.rotation;
            gripPosition = delta.position + delta.rotation * itemInWrist.position;
            return true;
        }

        /// <summary>
        /// Kumanda anchor'ından ISDK bileğine delta (anchor uzayında, metre): canlı ölçüm varsa o,
        /// yoksa ölçülmüş sabit, o da yoksa kimlik.
        /// <para>
        /// ⚠️ <b>Sıra iki uçta da aynı olmak ZORUNDA:</b> yerel oyuncu canlı ölçümü kullanırken uzak
        /// izleyici sabiti kullanır ve ikisi aynı değere yakınsadığı sürece silah her ekranda aynı
        /// durur. Sabit ölçülmemişse (kimlik) fark yalnız birkaç santimdir — açık bir bozulma değil,
        /// kabul edilmiş bir yaklaşıklık.
        /// </para>
        /// </summary>
        public static Pose ResolveAnchorToWrist(bool rightHand)
        {
            return HandGripPoser.TryGetAnchorToWrist(rightHand, out Pose live)
                ? live
                : HandGripConvention.AnchorToWrist(rightHand);
        }

        /// <summary>
        /// Bir DÜNYA bilek pozunu, aynı elin ANCHOR pozuna çevirir (<c>anchor = wrist ∘ Inverse(delta)</c>).
        /// <para>
        /// ⚠️ <b>Uzak avatarın el hedefleri bu çeviriden geçmek ZORUNDA:</b> <c>RemoteHandPoser</c>
        /// aldığı pozu anchor çerçevesi sayıyor (<c>HandFingerRig.WristCorrection</c> anchor uzayı
        /// için ölçüldü), oysa kavrama kaydı bilek uzayındadır. Çeviri atlanırsa uzak el, delta
        /// kadar kaymış ve dönmüş çizilir.
        /// </para>
        /// </summary>
        public static Pose WristToAnchor(bool rightHand, in Pose wristWorld)
        {
            Pose delta = ResolveAnchorToWrist(rightHand);
            Quaternion anchorRotation = wristWorld.rotation * Quaternion.Inverse(delta.rotation);
            return new Pose(wristWorld.position - anchorRotation * delta.position, anchorRotation);
        }

        /// <summary>
        /// Ana kavrama noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — aynı ölçünün ters yönü.
        /// <para>
        /// <see cref="ItemDefinition.PrimaryGripPointOnItem(bool)"/>'ın parametreli ikizidir ve türetmesi
        /// birebir aynıdır (<c>item.TransformPoint(s) == hand.position</c> koşulundan
        /// <c>s = Inverse(R) * (-P)</c>). ⚠️ Ters yön hesapları (uzak elin avuç hedefi)
        /// etkin kavrama çözüldüğünde <b>bunu</b> kullanmak zorundadır: biri canlı deltadan, öteki
        /// tanımın ham kaydından beslenirse el ile silah aynı karede birbirinden ayrışır.
        /// </para>
        /// </summary>
        public static Vector3 GripPointOnItem(in Vector3 gripPosition, in Quaternion gripRotation)
        {
            return Quaternion.Inverse(gripRotation) * (-gripPosition);
        }
    }
}
