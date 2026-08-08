using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// "Bu eşya bu elde NASIL durur" sorusunun <b>etkin</b> cevabı — iki kaynağın bilinçli
    /// bileşimi:
    /// <para>
    /// <b>ROTASYON tanımın sabitinden gelir</b> (<see cref="ItemDefinition.PrimaryGripRotation"/>,
    /// anchor uzayı; varsayılan kimlik = eşyanın eksenleri kumanda anchor'ıyla BİREBİR aynı).
    /// Kavrama poz düğümünün rotasyonu eşyaya HİÇ karışmaz: düğümdeki el, EL MODELİNİN eşya
    /// üstündeki duruşudur (<c>HandGripPoser</c> bileği ona kilitler) — eşyayı da o elden döndürmek
    /// "kumandayı uzatınca namlu başka yöne bakıyor" demekti. Kumanda nereye, namlu oraya;
    /// gerekirse ince ayar tek yerden, <c>WD_*</c>'daki euler'den yapılır.
    /// </para>
    /// <para>
    /// <b>POZİSYON düğümden gelir:</b> düğümün bileği eşyanın neresindeyse, eşya öyle ötelenir ki
    /// o nokta oyuncunun CANLI bileğine (<see cref="HandGripPoser.TryGetAnchorToWrist"/> deltasının
    /// konumu) otursun — yani el kabzanın üstünde durur, eşya elin içinden kaymaz.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sol ve sağ AYRI çözülür.</b> Poz düğümleri el başınadır; kabza simetrik olmadığı için
    /// iki elin bilek noktası eşyanın farklı yerlerine düşer.
    /// </para>
    /// <para>
    /// ⚠️ <b>Protokolde karşılığı YOKTUR ve tel formatı değişmez</b> (§6.6): duruş yine telde
    /// gitmiyor, iki uç da aynı prefabtan aynı düğümü okuyup aynı deltayı ölçüyor. Uzak uçta da bu
    /// sınıf koşar (<c>RemoteAvatar</c>), yoksa aynı silah iki ekranda iki ayrı duruşta çizilirdi.
    /// </para>
    /// <para>
    /// <b>Çözülemediğinde <c>false</c> döner ve çağıran ESKİ yola (WD alanları) düşer</b>: poz
    /// yazılmamış silah, rig'i olmayan oturum (admin gözlemci, editör) ve ilk kare bu yoldan geçer.
    /// Fallback bugünkü davranışın birebir aynısıdır — yeni yol yalnız <i>iyileştirir</i>, hiçbir
    /// durumu bozmaz.
    /// </para>
    /// </summary>
    public static class ItemGripAuthority
    {
        /// <summary>
        /// Ana kavramanın etkin ofsetini çözer: çıktı, <see cref="ItemDefinition.PrimaryGripPosition"/>
        /// / <see cref="ItemDefinition.PrimaryGripRotation"/> ile <b>AYNI anlamdadır</b> (anchor
        /// uzayında, metre) — yani <c>itemPos = palm.pos + palm.rot * gripPosition</c>,
        /// <c>itemRot = palm.rot * gripRotation</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Denklem: rotasyon sabittir (<c>Q = PrimaryGripRotation</c>); pozisyon, düğümün bilek
        /// NOKTASI canlı bilek noktasına çakışacak şekilde çözülür —
        /// <c>gripPosition + Q * bilekEşyada = delta.pozisyon</c> eşitliğinden.
        /// Düğümün ve deltanın ROTASYONU eşyaya girmez (el modeline girer, buraya değil).
        /// </para>
        /// <para>
        /// ⚠️ <b>Bileğin eşyaya göre pozu ISDK'nın ÖLÇEKLİ bileşimiyle okunur</b>
        /// (<c>PoseUtils.GlobalPoseScaled</c>, <see cref="HandGrabPose.RelativePose"/> zaten
        /// <c>DeltaScaled</c> ile üretiliyor): <c>WPN_*</c> kökleri 0.8 ölçekli, ölçeksiz bir geri
        /// bileşim bileği eşyadan 1/0.8 kadar uzağa koyardı. <see cref="HandGripPoser"/> de aynı
        /// yardımcıyı çağırıyor — sözleşmenin iki ucu tek satırdan geçsin diye.
        /// </para>
        /// <para>
        /// ⚠️ Ölçekli dünya pozu alındıktan SONRA eşyanın köküne göre fark <b>elle</b> alınır
        /// (<c>InverseTransformPoint</c> DEĞİL): oradan sonrası metredir ve eşyanın görsel ölçeğiyle
        /// bir daha büyümemelidir. İki adımın karıştırılması ölçeği ya iki kez uygular ya hiç.
        /// </para>
        /// </remarks>
        public static bool TryResolvePrimaryGrip(ItemDefinition definition, Transform itemRoot,
            bool rightHand, out Vector3 gripPosition, out Quaternion gripRotation)
        {
            gripPosition = Vector3.zero;
            gripRotation = Quaternion.identity;

            if (definition == null || itemRoot == null)
            {
                return false;
            }

            // Yerleştirilmemiş / poz verisi taşımayan düğümü Find zaten null sayıyor — yani
            // "araç düğümleri açtı ama kavrama daha yazılmadı" hâli buradan geçmez.
            HandGrabPose node = ItemGripPoses.Find(itemRoot, GripSocketKind.Primary, rightHand);
            if (node == null)
            {
                return false;
            }

            if (!HandGripPoser.TryGetAnchorToWrist(rightHand, out Pose anchorToWrist))
            {
                return false;
            }

            Transform reference = node.RelativeTo != null ? node.RelativeTo : itemRoot;
            Pose wristWorld = PoseUtils.GlobalPoseScaled(reference, node.RelativePose);

            // Bileğin EŞYAYA göre KONUMU (metre). Eşya nerede durursa dursun sabittir: wristWorld
            // da eşyanın kendi hiyerarşisinden çıkıyor, yani bu satırlar eşyanın dünya pozundan
            // bağımsızdır — çağıran istediği anda, silahı taşıdıktan sonra bile aynı sonucu alır.
            Vector3 wristOnItem = Quaternion.Inverse(itemRoot.rotation) *
                                  (wristWorld.position - itemRoot.position);

            gripRotation = definition.PrimaryGripRotation;
            gripPosition = anchorToWrist.position - gripRotation * wristOnItem;
            return true;
        }

        /// <summary>
        /// Ana kavrama noktasının <b>EŞYAYA göre</b> yerel konumu (metre) — aynı ölçünün ters yönü.
        /// <para>
        /// <see cref="ItemDefinition.PrimaryGripPointOnItem"/>'ın parametreli ikizidir ve türetmesi
        /// birebir aynıdır (<c>item.TransformPoint(s) == hand.position</c> koşulundan
        /// <c>s = Inverse(R) * (-P)</c>). ⚠️ Ters yön hesapları (uzak elin avuç hedefi, soket çizimi)
        /// etkin kavrama çözüldüğünde <b>bunu</b> kullanmak zorundadır: biri düğümden, öteki WD
        /// alanlarından beslenirse el ile silah aynı karede birbirinden ayrışır.
        /// </para>
        /// </summary>
        public static Vector3 GripPointOnItem(in Vector3 gripPosition, in Quaternion gripRotation)
        {
            return Quaternion.Inverse(gripRotation) * (-gripPosition);
        }
    }
}
