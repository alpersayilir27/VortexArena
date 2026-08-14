#if UNITY_EDITOR
using UnityEditor;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama kalibrasyonunda ölçülecek silahın seçimi.
    /// <para>
    /// Seçim <b>kişiseldir</b> ve <see cref="EditorPrefs"/>'te durur: rol/hedef seçiminde olduğu
    /// gibi, hiçbir sahneyi ya da asset'i kirletmez. Sahneye/prefaba serialize edilmiş bir "şu an
    /// kalibre edilen silah" alanı olsaydı her silah denemesi bir dosya değişikliği üretirdi.
    /// </para>
    /// <para>
    /// ⚠️ Anahtar BURADA, tek yerde tanımlıdır çünkü iki ayrı assembly okuyor: dev penceresi
    /// (<c>VortexArena.App.Editor</c>) yazar, kalibrasyon bileşeni (<c>VortexArena.Core</c>) okur.
    /// İki yerde yazılmış bir string, biri değiştirilip diğeri unutulduğunda "seçtim ama gelmedi"
    /// biçiminde görünür — teşhisi pahalı bir sapma.
    /// </para>
    /// <para>
    /// Dosyanın tamamı editör içidir: kalibrasyon bir üretim özelliği değil, bir yazma aracıdır
    /// (yazdığı yer <c>WD_*.asset</c>'tir ve asset yazmak yalnız editörde mümkündür).
    /// </para>
    /// </summary>
    public static class WeaponGripCalibrationSession
    {
        /// <summary>Seçilen silahın proje yolunun <see cref="EditorPrefs"/> anahtarı.</summary>
        public const string KeySelectedWeapon = "VortexArena.Dev.WeaponAsset";

        /// <summary>
        /// Seçilen <c>WD_*.asset</c>'in proje yolu (<c>Assets/…</c>); seçilmemişse boş string.
        /// </summary>
        public static string SelectedWeaponPath
        {
            get => EditorPrefs.GetString(KeySelectedWeapon, string.Empty);
            set => EditorPrefs.SetString(KeySelectedWeapon, value ?? string.Empty);
        }

        /// <summary>
        /// Seçili silah tanımını yükler; seçim yoksa ya da yol artık bir
        /// <see cref="WeaponDefinition"/>'a çözülmüyorsa <c>null</c>.
        /// <para>⚠️ Yol taşınmış/silinmiş olabilir (EditorPrefs asset takibi yapmaz), bu yüzden
        /// çağıran <c>null</c> ihtimalini her zaman karşılamak zorundadır.</para>
        /// </summary>
        public static WeaponDefinition LoadSelected()
        {
            string path = SelectedWeaponPath;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
        }

        /// <summary>Arayüzde/HUD'da gösterilecek ad: dosya adı ya da "(silah seçilmedi)".</summary>
        public static string SelectedDisplayName
        {
            get
            {
                string path = SelectedWeaponPath;
                return string.IsNullOrEmpty(path)
                    ? "(silah seçilmedi)"
                    : System.IO.Path.GetFileNameWithoutExtension(path);
            }
        }
    }
}
#endif
