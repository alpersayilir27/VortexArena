using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// Hierarchy right-click menu: <c>GameObject &gt; VortexArena &gt; Network Parent</c>.
    /// Adds <see cref="NetIdentity"/> to ALL selected scene objects with a per-scene unique sceneId
    /// (that scene's max + 1), undoable in ONE step, marking touched scenes dirty.
    /// <para>
    /// Prefab ASSETS are skipped: sceneId is scene-specific and baked into a project prefab every
    /// instance would share it. Objects that already have an id (sceneId ≠ 0) are skipped too.
    /// </para>
    /// </summary>
    internal static class NetworkParentMenu
    {
        private const string MENU_PATH = "GameObject/VortexArena/Network Parent";

        [MenuItem(MENU_PATH, false, 30)]
        private static void AddNetworkParent(MenuCommand command)
        {
            // In the hierarchy context menu Unity calls this command once for EVERY SELECTED OBJECT;
            // since we process the whole selection here, only the first call is let through.
            GameObject[] selection = Selection.gameObjects;
            if (command.context != null && selection.Length > 1 && command.context != selection[0])
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Network Parent");

            // One scan per scene: the list holds component REFERENCES, so each new id is visible to the
            // next NextFreeId immediately (the counter advances per scene).
            var perScene = new Dictionary<Scene, List<NetIdentity>>();
            int added = 0;
            int assigned = 0;
            int skipped = 0;

            for (int i = 0; i < selection.Length; i++)
            {
                GameObject go = selection[i];
                if (!IsSceneObject(go))
                {
                    skipped++;
                    continue;
                }

                Scene scene = go.scene;
                if (!perScene.TryGetValue(scene, out List<NetIdentity> identities))
                {
                    identities = SceneIdUtility.CollectInScene(scene);
                    perScene.Add(scene, identities);
                }

                NetIdentity identity = go.GetComponent<NetIdentity>();
                if (identity == null)
                {
                    identity = Undo.AddComponent<NetIdentity>(go);
                    identities.Add(identity);
                    added++;
                }
                else if (identity.SceneId != 0u)
                {
                    skipped++;
                    continue; // already baked — do not touch the id
                }

                Undo.RecordObject(identity, "Assign Scene Id");
                if (SceneIdUtility.AssignId(identity, SceneIdUtility.NextFreeId(identities)))
                {
                    assigned++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
            }

            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log(
                $"[VortexArena] Network Parent: {added} NetIdentity eklendi, " +
                $"{assigned} sceneId atandı, {skipped} obje atlandı.");
        }

        [MenuItem(MENU_PATH, true, 30)]
        private static bool ValidateAddNetworkParent()
        {
            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (IsSceneObject(selection[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Is this a SCENE object: project-window prefabs/assets (persistent) are filtered out and it
        /// must belong to a valid scene.
        /// </summary>
        private static bool IsSceneObject(GameObject go)
        {
            return go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid();
        }
    }
}
