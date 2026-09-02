using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// The sceneId guard: on every scene SAVE it repairs <see cref="NetIdentity.SceneId"/> values left
    /// at 0 or COLLIDING — a plain version of Mirror's sceneId bake pattern.
    /// <para>
    /// It exists for SCENE COPYING: a "Save As" or duplicated scene comes out with ids identical to the
    /// source's. Separating collisions at save time means nobody manages ids by hand and a copied arena
    /// is consistent out of the box.
    /// </para>
    /// <para>Second net on ENTERING PLAY: an object duplicated and played before the scene is saved
    /// would register twice in <c>NetObjectRegistry</c>. That repair lives in memory only — the server
    /// reads ids from the list written on save, so the log asks for a save.</para>
    /// Completely silent when the scene has no NetIdentity (most of today's scenes).
    /// <para>Right after the repair it publishes the scene's NetObject list through
    /// <see cref="SceneObjectExporter"/> — that is what lets the server config export stay
    /// scene-free.</para>
    /// <para>Project-wide pass (after a merge / disk copy): <see cref="SceneIdBatchRepair"/>.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneIdGuard
    {
        static SceneIdGuard()
        {
            // The static ctor can run again outside a domain reload, so we unsubscribe first:
            // a double subscription = double repair + double log.
            EditorSceneManager.sceneSaving -= HandleSceneSaving;
            EditorSceneManager.sceneSaving += HandleSceneSaving;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandleSceneSaving(Scene scene, string path)
        {
            if (SceneIdUtility.RepairScene(scene, out int fixedCount))
            {
                Debug.Log($"[VortexArena] '{scene.name}': {fixedCount} NetIdentity sceneId onarıldı (0/çakışma).");
            }

            // ⚠️ ORDER: strictly AFTER the repair — otherwise the file would carry pre-repair ids.
            // Unconditional: a NetObject's kind may have changed without any id moving.
            SceneObjectExporter.WriteForScene(scene, path);
        }

        /// <summary>Repairs every loaded scene right before Play; prefab stage scenes are not in the
        /// SceneManager list, so a prefab never gets a scene id here.</summary>
        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !SceneIdUtility.RepairScene(scene, out int fixedCount))
                {
                    continue;
                }

                // Warning, not log: until the scene is saved the exported list still carries the old
                // ids and the server answers these objects with "unknown netId".
                Debug.LogWarning(
                    $"[VortexArena] '{scene.name}': Play öncesi {fixedCount} NetIdentity sceneId onarıldı (0/çakışma). " +
                    "Sahne kaydedilmedi — sunucunun bu kimlikleri görmesi için Play'den çıkınca sahneyi kaydet.");
            }
        }
    }
}
