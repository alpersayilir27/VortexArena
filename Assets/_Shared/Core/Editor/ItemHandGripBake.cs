using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Avuç düğümü ↔ kavrama alanları dönüşümünün <b>TEK</b> uygulaması: <c>Kaydet</c> bu yönü,
    /// tezgâhın kurulumu ters yönü kullanır (<see cref="GripPoseStudio"/>).
    /// <para>
    /// ⚠️ <b>Referans AVUÇTUR, el modelinin bileği DEĞİLDİR.</b> Kavrama alanları kumanda anchor'ı
    /// çerçevesinde tanımlı (<c>ItemGripSolver</c> onları <c>HandGripPivot.Resolve</c> çıktısıyla
    /// bileştiriyor); ISDK el modelinin kök transformu ise bilek çerçevesindedir ve ikisi arasında
    /// sabit bir dönüş vardır. Buraya bilek verilirse o dönüş sessizce tanıma yazılır ve silah
    /// oyunda o kadar dönük çıkar — çeviriyi tezgâh el modelini kurarken
    /// <c>HandGripConvention.Correction</c> ile bir kez yapar, bu sınıf saf uzay bileşimidir.
    /// </para>
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
        /// Avucun (kumanda anchor'ının) DÜNYA pozundan, o kavrama noktasının tanım alanlarını
        /// üretir (kayıt yönü).
        /// </summary>
        /// <param name="itemRoot">Silahın kökü — ölçünün referansı.</param>
        /// <param name="palm">Avuç düğümü: <c>ItemGripSolver</c>'ın <c>primaryPalm</c> olarak
        /// aldığı pozun ta kendisi. ⚠️ El modelinin bileği DEĞİL (sınıf başındaki uyarı).</param>
        /// <param name="kind">Hangi kavrama noktası (uzay yönünü bu belirler).</param>
        public static void FromWrist(Transform itemRoot, Transform palm, GripSocketKind kind,
            out Vector3 gripPosition, out Vector3 gripEuler)
        {
            // Avucun EŞYAYA göre pozu (ölçeksiz bileşim).
            Quaternion itemInverse = Quaternion.Inverse(itemRoot.rotation);
            Vector3 handPosition = itemInverse * (palm.position - itemRoot.position);
            Quaternion handRotation = itemInverse * palm.rotation;

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
        /// Tanım alanlarından avucun EŞYAYA göre pozunu üretir (tezgâh kurulumu) —
        /// <see cref="FromWrist"/>'in birebir tersi.
        /// <para>⚠️ Bu iki yönün tersi olması, "tezgâhı aç → hiç dokunma → Kaydet" kimliğinin
        /// dayanağıdır: değer değişiyorsa uzay yönlerinden biri terstir ve bakılacak tek yer
        /// burasıdır.</para>
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
