using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kanonik kavramanın matematiği — <b>yerel ve uzak uçta koşan TEK çözücü</b>.
    /// <para>
    /// <b>Neden ayrı ve saf bir sınıf:</b> aynı duruşu iki taraf da hesaplamak zorunda (duruş telde
    /// gitmez, §6.6). İki ayrı uygulama olsaydı biri düzeltilip öteki unutulur ve aynı silah kendi
    /// ekranında başka, karşı ekranda başka görünürdü. Burada sahne/bileşen bağımlılığı YOKTUR:
    /// girdi ana elin avuç (kumanda anchor) pozu + tanım, çıktı eşyanın dünya pozu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Eşyanın DÖNÜŞÜ HER ZAMAN ana kumandanın dönüşüdür — başka hiçbir şey onu döndürmez.</b>
    /// Ne kavrama kaydı (yalnız KONUM taşır, <see cref="ItemGripPose"/>), ne ikinci elin konumu/dönüşü,
    /// ne bileğin duruşu. İkinci el ön kabzaya yalnız GÖRSEL olarak yapışır (<c>HandGripPoser</c>) ve
    /// saçılım/geri tepmeyi düşürür (<c>Weapon</c>); silahın eksenini ikinci ele çeviren bir "iki elli
    /// nişan" YOKTUR ve eklenmez — ikinci el kabzayı tuttuğu anda silah kumandadan sapardı ve oyuncu
    /// bunu "el konumuna göre silah bozuk geliyor" olarak yaşar.
    /// </para>
    /// <para>
    /// Denklem: <c>itemRot = palm.rot</c>, <c>itemPos = palm.pos + palm.rot * (−kayıt.position)</c> —
    /// kayıt kumandanın eşyaya göre yerel konumu olduğu için eşya, kumandayı o noktaya oturtacak
    /// biçimde geri kaydırılır. Yazılmamış kayıtta sıfır: eşya kumandanın tam üstünde.
    /// </para>
    /// </summary>
    public static class ItemGripSolver
    {
        /// <summary>
        /// Eşyanın dünya pozunu çözer.
        /// </summary>
        /// <param name="def">Eşya tanımı (kavrama konumunun kaynağı); <c>null</c> ise eşya avuca yapışır.</param>
        /// <param name="primaryRight">Ana el SAĞ mı — kayıt el başına yazıldığı için zorunlu.</param>
        /// <param name="primaryPalm">Ana elin AVUÇ pozu (<c>HandGripPivot.Resolve</c> çıktısı = kumanda anchor'ı).</param>
        public static void Solve(ItemDefinition def, bool primaryRight, in Pose primaryPalm,
                                 out Vector3 itemPosition, out Quaternion itemRotation)
        {
            // Tanımsız eşyanın kavrama ofseti de yoktur: eşyayı avuca yapıştırmak, dünya orijinine
            // düşürmekten iyidir (eksikliği Weapon.Awake zaten hata olarak basıyor).
            Vector3 offset = def != null ? def.PrimaryGripPosition(primaryRight) : Vector3.zero;

            itemRotation = primaryPalm.rotation;
            itemPosition = primaryPalm.position + primaryPalm.rotation * offset;
        }
    }
}
