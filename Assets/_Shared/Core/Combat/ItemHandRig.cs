using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah prefabındaki <b>el modellerinin</b> yerinin ve adının TEK tanımı — kavramanın
    /// <b>elle yazılan</b> tek kaynağı bu düğümlerdir.
    /// <para>
    /// Yerleşim iki seviyedir: <c>&lt;silah kökü&gt;/Hands/Hand_&lt;Kind&gt;</c>. Aynı desen
    /// <see cref="ItemGripPoses"/>'ta da var ve gerekçesi aynı: üretici (editör aracı) ile
    /// tüketici kendi string'ini taşısaydı bir harflik sapma hata üretmez, yalnız düğüm sessizce
    /// <b>bulunamaz</b> olurdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yalnız SAĞ el yazılır.</b> Sol el bake sırasında aynalanarak üretilir
    /// (<c>HandPose.CopyFrom(…, mirrorHandedness: true)</c>) — ayna matematiği elle yazılmaz.
    /// İki eli ayrı ayrı yazmak, aynı kavramanın iki kez tarif edilmesi olurdu ve ikisi zamanla
    /// kaçınılmaz olarak birbirinden saparadı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu düğümler OYUNDA HİÇ ÇİZİLMEZ.</b> Bake bittiğinde kapatılırlar ve kapalı kalırlar:
    /// silah sahnede de duruyor (raf/masa, <c>WeaponFrame</c>, <c>VA_WeaponCanvas</c>) ve uzak
    /// avatarın elinde de — açık kalan bir el modeli arenada havada el olarak görünürdü. Oyuncunun
    /// gördüğü el rig'in kendi elidir; bu düğümler yalnız <b>ne yazılacağını</b> tarif eder.
    /// </para>
    /// <para>
    /// ⚠️ <b>Runtime bu sınıfı OKUMAZ ve okumamalı.</b> Çalışma anında kullanılan veri bake
    /// çıktısıdır (<c>ItemDefinition</c> kavrama alanları + <c>GripPoses/Pose_*</c>). Runtime'ı
    /// buradan okutmak, gizlenmiş bir modeli canlı tutmayı gerektirir ve bake'i anlamsız kılardı.
    /// Buradaki tek runtime kullanımı bir <b>emniyettir</b>: bake unutulmuşsa düğümü kapatmak.
    /// </para>
    /// </summary>
    public static class ItemHandRig
    {
        /// <summary>El modellerini toplayan düğümün adı (silah kökünün DOĞRUDAN çocuğu).</summary>
        public const string RootNodeName = "Hands";

        /// <summary>Yazılan elin tarafı — sol taraf bake'te aynalanır.</summary>
        public const bool AuthoredHandIsRight = true;

        /// <summary>Bir kavrama noktasının el düğümü adı: <c>Hand_Primary</c>, <c>Hand_Secondary</c>.</summary>
        public static string NodeName(GripSocketKind kind)
        {
            return $"Hand_{kind}";
        }

        /// <summary>
        /// Silahın altındaki el düğümünü bulur; yoksa <c>null</c>.
        /// <para>⚠️ Ağaç TARANMAZ (<c>GetComponentsInChildren</c> değil): arama tam iki seviye
        /// <see cref="Transform.Find"/> ile iner — aynı gerekçe <see cref="ItemGripPoses.Find"/>'da.
        /// <c>Transform.Find</c> pasif çocukları da bulur, yani gizlenmiş el düğümü de bulunur
        /// (bake'ten sonra düğüm kapalıdır ve yeniden düzenlenebilmelidir).</para>
        /// </summary>
        public static Transform Find(Transform itemRoot, GripSocketKind kind)
        {
            if (itemRoot == null)
            {
                return null;
            }

            Transform root = itemRoot.Find(RootNodeName);
            return root != null ? root.Find(NodeName(kind)) : null;
        }

        /// <summary>
        /// El düğümlerini kapatır — <b>emniyet</b>. Bake zaten kapatıyor; bu, bake'i unutulmuş ya da
        /// düzenleme için açık bırakılmış bir prefabın arenada havada el göstermesini engeller.
        /// </summary>
        /// <returns>Kapatılan düğüm sayısı (sıfır = zaten kapalıydı ya da hiç yok).</returns>
        public static int HideAll(Transform itemRoot)
        {
            int hidden = 0;
            hidden += Hide(Find(itemRoot, GripSocketKind.Primary));
            hidden += Hide(Find(itemRoot, GripSocketKind.Secondary));
            return hidden;
        }

        private static int Hide(Transform node)
        {
            if (node == null || !node.gameObject.activeSelf)
            {
                return 0;
            }

            node.gameObject.SetActive(false);
            return 1;
        }
    }
}
