using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>Verifies that every <see cref="ItemDefinition"/> has an assigned and UNIQUE
    /// <c>netItemId</c> (§6.6), then rewrites <c>Resources/NetItemCatalog.asset</c> from the items
    /// FOUND in the project.</summary>
    /// <remarks>
    /// No separate menu item: <c>Configure All Build Elements</c> runs this on every sync
    /// (<c>BuildElementsConfigurator.SyncAll</c>) and the "Hazırlık" section shows its state.
    /// <para>The catalog is derived from the project, not from the weapon table:
    /// <c>WeaponKitBuilder</c> only knows rifles, so a throwable (or any other item type) would be
    /// missing silently and never drawn on remote players. <c>FindAssets("t:ItemDefinition")</c>
    /// covers all subtypes, so a new item TYPE needs no change here.</para>
    /// <para>⚠️ On failed validation the catalog is not written: a catalog built from conflicting
    /// ids would look like it works while drawing the wrong item — loud failure is better.</para>
    /// <para>⚠️ This guard, not the base class, is the real protection: a conflicting or unassigned
    /// id breaks no compile, throws nothing, shows no red Inspector — it surfaces in the field as
    /// "wrong (or no) item in the player's hand", and only on remote clients: the owner's own
    /// screen is correct.</para>
    /// <para>The id is a byte on the wire, so the catalog array index is deliberately not used
    /// (reordering would shift every item) — which also means nothing guarantees uniqueness
    /// automatically.</para>
    /// </remarks>
    internal static class NetItemIdGuard
    {
        private const string CatalogPath = "Assets/_Shared/Data/Resources/NetItemCatalog.asset";

        /// <summary>Validates and rewrites the catalog; returns a one line summary for the sync
        /// report.</summary>
        /// <remarks>⚠️ Opens no dialog: a dialog would lock the main thread on CLI/automation calls
        /// (same reason as <c>ServerConfigExporter.Export(false)</c>); details go to the
        /// console.</remarks>
        internal static string Rebuild()
        {
            // Subclasses (WeaponDefinition, later ThrowableDefinition) also match
            // t:ItemDefinition — no need to repeat the filter per derived type.
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");

            var byId = new Dictionary<byte, string>();
            var unassigned = new List<string>();
            var conflicts = new List<string>();
            int checkedCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null)
                {
                    continue;
                }

                checkedCount++;

                if (!def.HasNetItemId)
                {
                    unassigned.Add(path);
                    continue;
                }

                if (byId.TryGetValue(def.NetItemId, out string other))
                {
                    conflicts.Add($"netItemId {def.NetItemId} → '{other}' ve '{path}'");
                    continue;
                }

                byId[def.NetItemId] = path;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{checkedCount} ItemDefinition tarandı.");

            if (unassigned.Count == 0 && conflicts.Count == 0)
            {
                sb.AppendLine("Tüm netItemId'ler atanmış ve tekil.");
                sb.Append(RebuildCatalog(byId));
                Debug.Log($"[NetItemIdGuard] {sb}");
                return $"net eşya kataloğu: {checkedCount} eşya, kimlikler tekil — yazıldı.";
            }

            for (int i = 0; i < unassigned.Count; i++)
            {
                string msg = $"[NetItemIdGuard] netItemId ATANMAMIŞ (0 = boş el rezervi, geçersiz): " +
                             $"'{unassigned[i]}' — bu eşya uzak oyuncularda hiç çizilmez.";
                Debug.LogError(msg, AssetDatabase.LoadAssetAtPath<ItemDefinition>(unassigned[i]));
            }

            for (int i = 0; i < conflicts.Count; i++)
            {
                Debug.LogError($"[NetItemIdGuard] netItemId ÇAKIŞMASI: {conflicts[i]} — " +
                               "uzak oyuncularda yanlış eşya çizilir.");
            }

            if (unassigned.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Atanmamış ({unassigned.Count}):");
                for (int i = 0; i < unassigned.Count; i++)
                {
                    sb.AppendLine($"  • {unassigned[i]}");
                }
            }

            if (conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Çakışma ({conflicts.Count}):");
                for (int i = 0; i < conflicts.Count; i++)
                {
                    sb.AppendLine($"  • {conflicts[i]}");
                }
            }

            sb.AppendLine();
            sb.Append("⚠️ Katalog YAZILMADI — önce yukarıdakileri düzelt.");
            Debug.LogWarning($"[NetItemIdGuard] {sb}");

            return $"net eşya kataloğu YAZILMADI: {unassigned.Count} atanmamış, {conflicts.Count} çakışan " +
                   "netItemId (ayrıntı konsolda).";
        }

        /// <summary>Whether the catalog matches the project's items — <b>WRITES NOTHING</b> (read by
        /// the build readiness panel). Three criteria: every id assigned, ids unique, catalog entry
        /// count equal to the scanned item count.</summary>
        /// <remarks>⚠️ The count comparison is coarse but never stays silent: adding an item without
        /// running the tool leaves the catalog short, and the only symptom is "item not drawn on
        /// remote players".</remarks>
        internal static bool IsCatalogUpToDate(out string detail)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");

            var byId = new Dictionary<byte, string>();
            int unassigned = 0;
            int conflicts = 0;
            int checkedCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null)
                {
                    continue;
                }

                checkedCount++;

                if (!def.HasNetItemId)
                {
                    unassigned++;
                    continue;
                }

                if (byId.ContainsKey(def.NetItemId))
                {
                    conflicts++;
                    continue;
                }

                byId[def.NetItemId] = path;
            }

            if (unassigned > 0)
            {
                detail = $"{unassigned} eşyanın netItemId'si atanmamış — uzak oyuncularda çizilmezler.";
                return false;
            }

            if (conflicts > 0)
            {
                detail = $"{conflicts} netItemId çakışması — uzak oyuncularda yanlış eşya çizilir.";
                return false;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<NetItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                detail = $"'{CatalogPath}' YOK — uzak oyuncuların elinde hiçbir eşya çizilmez.";
                return false;
            }

            int recorded = 0;
            ItemDefinition[] items = catalog.Items;
            for (int i = 0; items != null && i < items.Length; i++)
            {
                if (items[i] != null)
                {
                    recorded++;
                }
            }

            if (recorded != checkedCount)
            {
                detail = $"katalogda {recorded} kayıt, projede {checkedCount} eşya.";
                return false;
            }

            detail = $"{checkedCount} eşya, kimlikler tekil.";
            return true;
        }

        /// <summary>Writes the validated id→path map into <c>NetItemCatalog.asset</c>.</summary>
        /// <remarks>Sorted by <c>netItemId</c> so the committed asset only diffs when ids are added
        /// or removed.</remarks>
        private static string RebuildCatalog(Dictionary<byte, string> byId)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<NetItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                // Never overwrite an asset of another type at this path: CreateAsset kills its GUID
                // and every reference to it breaks (same safeguard as in WeaponKitBuilder).
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CatalogPath)))
                {
                    return $"⚠️ '{CatalogPath}' yolunda NetItemCatalog olmayan bir asset var — dokunulmadı.";
                }

                catalog = ScriptableObject.CreateInstance<NetItemCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var ids = new List<byte>(byId.Keys);
            ids.Sort();

            var items = new ItemDefinition[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                items[i] = AssetDatabase.LoadAssetAtPath<ItemDefinition>(byId[ids[i]]);
            }

            var so = new SerializedObject(catalog);
            SerializedProperty prop = so.FindProperty("items");
            if (prop == null || !prop.isArray)
            {
                return "⚠️ NetItemCatalog'ta 'items' dizisi yok (sözleşme kayması?) — katalog yazılmadı.";
            }

            prop.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return $"NetItemCatalog yazıldı: {items.Length} eşya (kimlikler {(ids.Count > 0 ? ids[0] : 0)}–{(ids.Count > 0 ? ids[ids.Count - 1] : 0)}).";
        }
    }
}
