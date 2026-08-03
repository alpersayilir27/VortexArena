using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Build sırasında ölçü maketinin <b>görsel</b> dalını (<c>Plane</c> + <c>Columns</c>) atar;
    /// kök ve kalibrasyon işaretçileri build'e girer.
    /// <para>
    /// ⚠️ <b>Maketin tamamı EditorOnly etiketlenemez:</b> kalibrasyon işaretçileri onun altındadır
    /// ve çalışma anında gerekir (<see cref="ArenaCalibrator"/> hizalamayı onlara göre yapar) —
    /// kökü damgalamak arenayı sahada sessizce hizalanamaz kılardı.
    /// </para>
    /// <para>
    /// ⚠️ Görsel dalın atılma sebebi boyut değil <b>bağımlılık</b>: <c>Plane</c> ve kolonlar
    /// <c>ProBuilderMesh</c> taşır, o da build'e <c>Unity.ProBuilder</c> runtime derlemesini
    /// sokardı. ProBuilder bu projede yalnız editör tarafıdır; asmdef grafiği onu runtime'a
    /// bağlamaz ve sahnedeki bir bileşen o kuralı arkadan delmemelidir.
    /// </para>
    /// <para>
    /// Sahne dosyası DEĞİŞMEZ: Unity bu kancayı build'e giren <b>geçici kopya</b> üzerinde
    /// çalıştırır. Editörde maket olduğu gibi durur (kurulum aracıdır); Play kipinde de durur,
    /// yalnız görseli <see cref="ArenaDimensionMesh"/> tarafından gizlenir — bu yüzden
    /// <paramref name="report"/> boşken (Play kipine giriş) hiçbir şey yapılmaz.
    /// </para>
    /// </summary>
    internal sealed class DimensionMeshBuildStripper : IProcessSceneWithReport
    {
        /// <summary>Diğer kancalardan sonra koşmasının bir sebebi yok; varsayılan sıra.</summary>
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // report == null → Play kipine giriş. Maket editörde olduğu gibi kalmalı.
            if (report == null || !scene.IsValid())
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ArenaDimensionMesh[] meshes =
                    roots[i].GetComponentsInChildren<ArenaDimensionMesh>(true);
                for (int k = 0; k < meshes.Length; k++)
                {
                    StripVisual(meshes[k], ArenaDimensionMesh.PlaneName);
                    StripVisual(meshes[k], ArenaDimensionMesh.ColumnsGroupName);
                }
            }
        }

        /// <summary>
        /// Maketin bir görsel dalını siler. Ad üzerinden gidilir çünkü üretim aracı da bu iki adı
        /// kullanıyor (<see cref="ArenaDimensionMesh.PlaneName"/> /
        /// <see cref="ArenaDimensionMesh.ColumnsGroupName"/>) — ikinci bir seçim kuralı yazmak
        /// araçla buranın sessizce sapmasına açık olurdu.
        /// </summary>
        private static void StripVisual(ArenaDimensionMesh mesh, string childName)
        {
            Transform child = mesh.transform.Find(childName);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
