using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>Project-wide sceneId pass: opens each given scene, repairs 0/colliding
    /// <see cref="NetIdentity.SceneId"/> values, saves the scenes that changed and refreshes every
    /// scene's exported object list. The caller's scene setup is restored afterwards.</summary>
    /// <remarks>
    /// <b>Why it exists:</b> <see cref="SceneIdGuard"/> only runs on SAVE and on entering Play. A scene
    /// file merged in git (two branches each adding an object with the same id) or copied on disk is
    /// consistent nowhere until someone opens and saves it — the only field symptom is "the object does
    /// not break". This pass is that open-and-save for every arena at once.
    /// <para>⚠️ Opens no dialog (CLI timeout trap, same rule as <c>ServerConfigExporter</c>): a dirty
    /// open scene REFUSES the run instead of asking; an untitled scene cannot be restored and is
    /// replaced by a fresh one.</para>
    /// <para>Unchanged scenes are NOT saved (no .unity diff); only their object list is rewritten,
    /// which is byte-stable when nothing changed.</para>
    /// </remarks>
    public static class SceneIdBatchRepair
    {
        /// <summary>Runs the pass over <paramref name="scenePaths"/> (asset-relative); returns a one
        /// line summary for the caller's report, details go to the console.</summary>
        public static string Run(IReadOnlyList<string> scenePaths)
        {
            if (scenePaths == null || scenePaths.Count == 0)
            {
                return "sahne kimlikleri: taranacak sahne yok.";
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "sahne kimlikleri: Play kipinde sahne açılmaz — çıkıp tekrar dene.";
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    return $"sahne kimlikleri: '{open.name}' kaydedilmemiş — önce açık sahneleri kaydet, " +
                           "hiçbir sahne açılmadı.";
                }
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            int scanned = 0;
            int repairedScenes = 0;
            int repairedIds = 0;
            int skipped = 0;

            try
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    string path = scenePaths[i];
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        Debug.LogWarning($"[VortexArena] Sahne bulunamadı, atlandı: '{path}'.");
                        skipped++;
                        continue;
                    }

                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        Debug.LogWarning($"[VortexArena] Sahne açılamadı, atlandı: '{path}'.");
                        skipped++;
                        continue;
                    }

                    scanned++;

                    if (SceneIdUtility.RepairScene(scene, out int fixedCount))
                    {
                        // Save runs SceneIdGuard again (no-op now) and writes the object list.
                        EditorSceneManager.SaveScene(scene);
                        repairedScenes++;
                        repairedIds += fixedCount;
                        Debug.Log($"[VortexArena] '{scene.name}': {fixedCount} NetIdentity sceneId onarıldı, sahne kaydedildi.");
                        continue;
                    }

                    // Clean scene: refresh only the list (a merged list can lag behind a clean scene).
                    SceneObjectExporter.WriteForScene(scene, path);
                }
            }
            finally
            {
                RestoreSetup(setup);
            }

            return $"sahne kimlikleri: {scanned} sahne tarandı, {repairedScenes} sahnede {repairedIds} kimlik onarıldı" +
                   (skipped > 0 ? $", {skipped} sahne atlandı (konsol)" : "") + "; obje listeleri tazelendi.";
        }

        /// <summary>Brings back the scenes the user had open. Untitled scenes cannot be restored — with
        /// none left, a fresh default scene is opened.</summary>
        private static void RestoreSetup(SceneSetup[] setup)
        {
            var kept = new List<SceneSetup>();
            bool hasActive = false;

            for (int i = 0; setup != null && i < setup.Length; i++)
            {
                if (setup[i] == null || string.IsNullOrEmpty(setup[i].path))
                {
                    continue;
                }

                kept.Add(setup[i]);
                hasActive |= setup[i].isActive && setup[i].isLoaded;
            }

            if (kept.Count == 0)
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }

            if (!hasActive)
            {
                // RestoreSceneManagerSetup needs exactly one LOADED active scene.
                for (int i = 0; i < kept.Count; i++)
                {
                    kept[i].isActive = false;
                }

                kept[0].isActive = true;
                kept[0].isLoaded = true;
            }

            EditorSceneManager.RestoreSceneManagerSetup(kept.ToArray());
        }
    }
}
