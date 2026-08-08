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
    /// oyunda o kadar dönük çıkar — çeviriyi <b>çağıran</b> yapar
    /// (<see cref="GripPoseStudio"/> kaydederken, elin bind duruşunda ölçülmüş bazından
    /// <c>HandGripConvention.Correction</c> ile), bu sınıf saf uzay bileşimidir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu alanların tüketicisi daraldı ve öyle kalır:</b> yerel oyuncunun eli/silahı artık
    /// <c>GripPoses/Pose_*</c> düğümünden sürülüyor. Buradaki sayılar kavrama <b>soketinin</b> yerini
    /// ve rig'i olmayan uçların (admin gözlemci, uzak avatar çizimi) fallback duruşunu besliyor —
    /// yani hâlâ yazılır, ama "silah elde ters duruyor" belirtisinin ilk bakılacak yeri artık poz
    /// düğümüdür.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ana kavramanın ROTASYONU artık hiçbir yerden TÜRETİLMEZ:</b> <c>primaryGripEuler</c>
    /// elle ayarlanan tek düğmedir (kimlik = eşya eksenleri kumanda anchor'ıyla birebir) ve
    /// <see cref="FromWrist"/>'in ana-el dalını kimse çağırmaz; kayıt yalnız
    /// <see cref="PrimaryPositionFromWrist"/> ile pozisyonu yazar. Eşyanın rotasyonunu elden
    /// türetmek, tezgâhtaki elin açısını namlunun yönüne bağlamak demekti
    /// (<see cref="VortexArena.Core.Combat.ItemGripAuthority"/> ile aynı sözleşme).
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
            FromWrist(itemRoot, new Pose(palm.position, palm.rotation), kind,
                out gripPosition, out gripEuler);
        }

        /// <summary>
        /// Aynı dönüşümün <see cref="Pose"/> alan biçimi — avucun DÜNYA pozu bir transformdan
        /// gelmiyorsa (türetilmiş anchor-proxy) kullanılır.
        /// <para>⚠️ Var olma sebebi: kavrama tezgâhında elin kökü artık <b>bilek</b> çerçevesindedir
        /// ve kumanda anchor'ı ondan <see cref="VortexArena.Core.Player.HandGripConvention"/> ile
        /// TÜRETİLİR — yani ortada bir transform yoktur. Yalnız bu imza için sahneye geçici bir
        /// GameObject açmak, ölçünün referansını görünmez bir yan etkiye (o objenin ölçeği/ebeveyni)
        /// bağlamak olurdu.</para>
        /// </summary>
        public static void FromWrist(Transform itemRoot, in Pose palm, GripSocketKind kind,
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
        /// Ana kavramanın <c>primaryGripPosition</c> alanını elin bilek NOKTASINDAN türetir:
        /// eşya öyle konumlansın ki bileğin oturduğu kabza noktası avuca (anchor'a) gelsin —
        /// <c>gripPosition + Q·bilekEşyada = 0</c> eşitliğinden.
        /// <para>
        /// ⚠️ Çalışma anındaki <see cref="VortexArena.Core.Combat.ItemGripAuthority"/> aynı denklemi
        /// canlı ölçülen anchor→bilek deltasıyla çözer; burada rig olmadığı için delta sıfır alınır
        /// (birkaç santimlik fark yalnız fallback uçlarını etkiler). Rotasyon parametredir ve
        /// yazılMAZ — tek yazarı <c>WD_*</c>'daki elle ayar.
        /// </para>
        /// </summary>
        /// <param name="gripRotation">Tanımın MEVCUT <c>PrimaryGripRotation</c>'ı (anchor uzayı).</param>
        public static Vector3 PrimaryPositionFromWrist(Transform itemRoot,
            in Vector3 wristWorldPosition, in Quaternion gripRotation)
        {
            // Bileğin eşyaya göre konumu — ölçeksiz bileşim (metre kuralı, sınıf başındaki uyarı).
            Vector3 wristOnItem = Quaternion.Inverse(itemRoot.rotation) *
                                  (wristWorldPosition - itemRoot.position);
            return -(gripRotation * wristOnItem);
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
