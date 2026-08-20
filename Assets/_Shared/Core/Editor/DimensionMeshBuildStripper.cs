using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>Strips the dimension mesh's <b>visual</b> branch (<c>Plane</c> + <c>Columns</c>)
    /// at build time; the root and the calibration anchors ship.</summary>
    /// <remarks>
    /// ⚠️ The whole mesh cannot be tagged EditorOnly: the calibration anchors sit under it and are
    /// needed at runtime (<see cref="ArenaCalibrator"/> aligns to them) — stamping the root would
    /// silently make the arena unalignable on site.
    /// <para>⚠️ The visual branch goes for a <b>dependency</b> reason, not size: <c>Plane</c> and
    /// columns carry <c>ProBuilderMesh</c>, which would drag the <c>Unity.ProBuilder</c> runtime
    /// assembly into the build. ProBuilder is editor-only here and a scene component must not
    /// breach that from behind.</para>
    /// <para>The scene file is untouched — Unity runs this hook on the temporary build copy. In
    /// the editor and in Play mode the mesh stays (its visual is hidden by
    /// <see cref="ArenaDimensionMesh"/>), hence the no-op when the build report is null.</para>
    /// </remarks>
    internal sealed class DimensionMeshBuildStripper : IProcessSceneWithReport
    {
        /// <summary>No ordering need against other hooks; default order.</summary>
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // report == null → entering Play mode; the mesh must stay as-is in the editor.
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

        /// <summary>Deletes one visual branch of the dimension mesh.</summary>
        /// <remarks>Matched by name because the generator uses the same two names
        /// (<see cref="ArenaDimensionMesh.PlaneName"/> /
        /// <see cref="ArenaDimensionMesh.ColumnsGroupName"/>); a second selection rule would be
        /// free to drift from the tool.</remarks>
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
