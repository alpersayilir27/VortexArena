using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>Hierarchy context menu: adds <see cref="ArenaRoof"/> to the selection and stamps
    /// <see cref="ArenaRoof.LayerName"/> on every Renderer below it.</summary>
    /// <remarks>The layer is only for visibility/filtering in the Scene view; hiding is driven by
    /// the component's Renderer list and works even without the stamp. Prefab assets are skipped
    /// (scene objects only); objects that already have the component keep it but get the layer
    /// refreshed, which helps roofs that gained meshes later.</remarks>
    internal static class ArenaRoofMenu
    {
        private const string MENU_PATH = "GameObject/VortexArena/Arena Roof";

        [MenuItem(MENU_PATH, false, 31)]
        private static void AddArenaRoof(MenuCommand command)
        {
            // Unity invokes the context menu command once PER SELECTED OBJECT; we handle the whole
            // selection here, so only the first invocation is let through.
            GameObject[] selection = Selection.gameObjects;
            if (command.context != null && selection.Length > 1 && command.context != selection[0])
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Arena Roof");

            int added = 0;
            int stamped = 0;
            int skipped = 0;

            for (int i = 0; i < selection.Length; i++)
            {
                GameObject go = selection[i];
                if (go == null || !go.scene.IsValid())
                {
                    skipped++;
                    continue; // prefab asset, not a scene object
                }

                ArenaRoof roof = go.GetComponent<ArenaRoof>();
                if (roof == null)
                {
                    roof = Undo.AddComponent<ArenaRoof>(go);
                    added++;
                }

                roof.StampLayer(); // records its own Undo
                stamped++;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (LayerMask.NameToLayer(ArenaRoof.LayerName) < 0)
            {
                Debug.LogWarning(
                    $"[ArenaRoof] '{ArenaRoof.LayerName}' katmanı projede tanımlı değil " +
                    "(ProjectSettings > Tags and Layers). Gizleme çalışır, yalnız sahnede " +
                    "süzme kolaylığı kaybolur.");
            }

            Debug.Log($"[ArenaRoof] {added} bileşen eklendi, {stamped} objede katman tazelendi, " +
                      $"{skipped} atlandı (prefab asset).");
        }

        [MenuItem(MENU_PATH, true)]
        private static bool ValidateAddArenaRoof()
        {
            return Selection.gameObjects.Length > 0;
        }
    }
}
