using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// El düğümü ↔ kavrama alanları dönüşümünün <b>TEK</b> uygulaması: bake bu yönü, "El Ekle"
    /// tohumlaması ters yönü kullanır.
    /// <para>
    /// ⚠️ <b>İki kavrama noktasının uzayı TERSTİR</b> (<see cref="ItemDefinition"/>): <c>primaryGrip</c>
    /// "el → eşya" (eşyanın avuca göre pozu), <c>secondaryGrip</c> ise "eşya → el" (ön kabza
    /// noktasının eşyaya göre pozu). Yani ana elde ters bileşim gerekir, ikincil elde gerekmez.
    /// Bu asimetri iki ayrı yerde elle yazılsaydı biri er geç ters yazılır ve belirtisi
    /// "silah elde ters duruyor" olurdu — sebebi bulunması en zor hatalardan biri.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçek dönüşümü KULLANILMAZ</b> (<c>InverseTransformPoint</c> değil, elle bileşim):
    /// kavrama ofsetleri METRE cinsindendir ve araya giren bir transformun ölçeği onlara
    /// bulaşmamalı — projede tekrarlanan kural (<c>ItemGripSockets</c>, <c>GripPoseStudio</c>).
    /// </para>
    /// </summary>
    public static class ItemHandGripBake
    {
        /// <summary>
        /// El bileğinin DÜNYA pozundan, o kavrama noktasının tanım alanlarını üretir (bake yönü).
        /// </summary>
        /// <param name="itemRoot">Silahın kökü — ölçünün referansı.</param>
        /// <param name="wrist">El modelinin bilek eklemi.</param>
        /// <param name="kind">Hangi kavrama noktası (uzay yönünü bu belirler).</param>
        public static void FromWrist(Transform itemRoot, Transform wrist, GripSocketKind kind,
            out Vector3 gripPosition, out Vector3 gripEuler)
        {
            // Bileğin EŞYAYA göre pozu (ölçeksiz bileşim).
            Quaternion itemInverse = Quaternion.Inverse(itemRoot.rotation);
            Vector3 handPosition = itemInverse * (wrist.position - itemRoot.position);
            Quaternion handRotation = itemInverse * wrist.rotation;

            if (kind == GripSocketKind.Secondary)
            {
                // "eşya → el": ön kabza noktası zaten eşya-yereldir, ters çevrilmez.
                gripPosition = handPosition;
                gripEuler = handRotation.eulerAngles;
                return;
            }

            // "el → eşya": tanım eşyanın AVUCA göre pozunu ister, yani bunun tersi.
            Quaternion itemInHand = Quaternion.Inverse(handRotation);
            gripPosition = itemInHand * -handPosition;
            gripEuler = itemInHand.eulerAngles;
        }

        /// <summary>
        /// Tanım alanlarından bileğin EŞYAYA göre pozunu üretir ("El Ekle" tohumlaması) — mevcut
        /// altı silahın kavraması sıfırdan yazılmasın diye <see cref="FromWrist"/>'in tersi.
        /// </summary>
        public static void ToWristLocal(ItemDefinition definition, GripSocketKind kind,
            out Vector3 localPosition, out Quaternion localRotation)
        {
            if (kind == GripSocketKind.Secondary)
            {
                localPosition = definition.SecondaryGripPosition;
                localRotation = definition.SecondaryGripRotation;
                return;
            }

            localRotation = Quaternion.Inverse(definition.PrimaryGripRotation);
            localPosition = localRotation * -definition.PrimaryGripPosition;
        }

    }
}
