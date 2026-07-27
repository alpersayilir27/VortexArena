using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Hiyerarşi sağ-tık menüsü: <c>GameObject &gt; VortexArena &gt; Arena Roof</c>.
    /// Seçili objelere <see cref="ArenaRoof"/> ekler ve altındaki tüm Renderer'lara
    /// <see cref="ArenaRoof.LayerName"/> katmanını damgalar — admin kuş bakışında gizlenecek
    /// geometri sahne görünümündeki Layers süzgecinden tek bakışta ayırt edilsin.
    /// <para>
    /// Katman yalnız görünürlük/ayıklama içindir: gizleme davranışı bileşenin Renderer
    /// listesinden gelir, damga unutulsa da çalışır (bkz. <see cref="ArenaRoof"/>).
    /// </para>
    /// <para>
    /// Prefab ASSET'lerine dokunulmaz (sahne objesi bekler). Zaten bileşeni olan obje atlanır
    /// ama katmanı yine tazelenir — sonradan mesh eklenmiş çatılar için pratik.
    /// </para>
    /// </summary>
    internal static class ArenaRoofMenu
    {
        private const string MENU_PATH = "GameObject/VortexArena/Arena Roof";

        [MenuItem(MENU_PATH, false, 31)]
        private static void AddArenaRoof(MenuCommand command)
        {
            // Hiyerarşi bağlam menüsünde Unity bu komutu SEÇİLEN HER OBJE için bir kez çağırır;
            // seçimin tamamını burada işlediğimiz için yalnız ilk çağrıyı geçiriyoruz.
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
                    continue; // prefab asset'i: sahne bileşeni değil
                }

                ArenaRoof roof = go.GetComponent<ArenaRoof>();
                if (roof == null)
                {
                    roof = Undo.AddComponent<ArenaRoof>(go);
                    added++;
                }

                roof.StampLayer(); // kendi içinde Undo kaydı tutar
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
