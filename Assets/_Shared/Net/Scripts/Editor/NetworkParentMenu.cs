using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// Hiyerarşi sağ-tık menüsü: <c>GameObject &gt; VortexArena &gt; Network Parent</c>.
    /// Seçili TÜM sahne objelerine <see cref="NetIdentity"/> ekler ve sahne bazında benzersiz
    /// sceneId (o sahnedeki max + 1) atar. Tüm değişiklikler TEK adımda geri alınabilir
    /// (Undo grubu), dokunulan sahneler dirty işaretlenir.
    /// <para>
    /// Prefab ASSET'leri atlanır: sceneId sahneye özgüdür, projedeki prefab'a bake edilirse
    /// her örnek aynı id ile gelir. Zaten kimliği olan (sceneId ≠ 0) obje de es geçilir.
    /// </para>
    /// </summary>
    internal static class NetworkParentMenu
    {
        private const string MENU_PATH = "GameObject/VortexArena/Network Parent";

        [MenuItem(MENU_PATH, false, 30)]
        private static void AddNetworkParent(MenuCommand command)
        {
            // Hiyerarşi bağlam menüsünde Unity bu komutu SEÇİLEN HER OBJE için bir kez çağırır;
            // seçimin tamamını burada işlediğimiz için yalnız ilk çağrıyı geçiriyoruz.
            GameObject[] selection = Selection.gameObjects;
            if (command.context != null && selection.Length > 1 && command.context != selection[0])
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Network Parent");

            // Sahne başına tek tarama: liste bileşen REFERANSI tuttuğu için atanan her yeni id
            // bir sonraki NextFreeId hesabına anında yansır (sayaç sahne bazında ilerler).
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
                    continue; // zaten bake'li — id'ye dokunma
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
        /// Obje SAHNE objesi mi: proje penceresindeki prefab/asset'ler (persistent) elenir,
        /// geçerli bir sahneye ait olması aranır.
        /// </summary>
        private static bool IsSceneObject(GameObject go)
        {
            return go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid();
        }
    }
}
