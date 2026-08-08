using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// "Bu eşya bu elde NASIL durur" sorusunun <b>etkin</b> cevabı: ölçüyü prefabtaki kavrama poz
    /// düğümünden (<c>GripPoses/Pose_&lt;Kind&gt;_&lt;L|R&gt;</c>) türetir ve
    /// <see cref="ItemGripSolver"/>'ın beklediği anchor-uzayı sözleşmesine çevirir.
    /// <para>
    /// <b>Neden düğümden türetiliyor:</b> kavramanın ELLE yazıldığı tek yer Kavrama Pozu
    /// Stüdyosu'dur ve o, eli silahın üstüne oturtarak <i>bileğin silaha göre pozunu</i> yazar.
    /// <c>WD_*.asset</c>'teki <c>primaryGrip*</c> alanları aynı bilginin <b>anchor uzayına
    /// çevrilmiş</b> kopyasıdır ve o çeviri ölçülmemiş sabitlere dayanıyordu — sonuç silahın elde
    /// yatık durmasıydı. Burada çeviri sabitten değil <b>canlı ölçümden</b> gelir
    /// (<see cref="HandGripPoser.TryGetAnchorToWrist"/>), yani yazılan poz ile çizilen duruş aynı
    /// tek kaynaktan çıkar.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sol ve sağ AYRI çözülür.</b> Poz düğümleri el başınadır; tek bir tanımı iki ele aynen
    /// uygulamak (WD alanlarının yaptığı) sol eli kaçınılmaz olarak yanlış tutturur — kabza
    /// simetrik değildir.
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
        /// Denklem: poz düğümünün tarif ettiği bilek, canlı bileğe çakışmalı.
        /// <c>bilekDünya = eşyaDünya ∘ bilekEşyaya</c> ve <c>bilekDünya = anchor ∘ delta</c>
        /// eşitlenip eşya pozu yalnız bırakılır.
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

            // Bileğin EŞYAYA göre pozu (metre). Eşya nerede durursa dursun sabittir: wristWorld de
            // eşyanın kendi hiyerarşisinden çıkıyor, yani bu iki satır eşyanın dünya pozundan
            // bağımsızdır — çağıran istediği anda, silahı taşıdıktan sonra bile aynı sonucu alır.
            Quaternion inverseItem = Quaternion.Inverse(itemRoot.rotation);
            Vector3 wristOnItem = inverseItem * (wristWorld.position - itemRoot.position);
            Quaternion wristRotationOnItem = inverseItem * wristWorld.rotation;

            gripRotation = anchorToWrist.rotation * Quaternion.Inverse(wristRotationOnItem);
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
