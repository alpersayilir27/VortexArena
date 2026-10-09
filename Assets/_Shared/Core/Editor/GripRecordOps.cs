using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.GripCapture;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Every write that touches a grip record goes through here — the capture applier, the studio
    /// window and the capture scene's Inspector share one path.
    /// <para>⚠️ A second writer would mean describing twice which of the four fields is which hand
    /// AND repeating the post-save sync; a record is not a product on its own (the socket
    /// indicator, the WPN prefabs and the item catalog derive from it), so "I saved but nothing
    /// changed in game" was a silent step.</para>
    /// </summary>
    internal static class GripRecordOps
    {
        internal const string LOG = "[GripCapture]";

        /// <summary>How long a reverse prefab→definition lookup stays valid (s).</summary>
        private const double LookupRefreshSeconds = 2d;

        private static string _lookupPath;
        private static ItemDefinition _lookupDefinition;
        private static bool _lookupAmbiguous;
        private static double _lookupTime;

        // ------------------------------------------------------------------ mutations

        /// <summary>
        /// Writes a Play session's captures into the definition.
        /// <para>The session rule: a captured kind's UNCAPTURED hand keeps its old record, but
        /// capturing only the primary means "this item is one-handed" — the secondary records are
        /// dropped, otherwise a stale foregrip from an earlier session would keep pulling the second
        /// hand onto a grip the user just decided against.</para>
        /// </summary>
        /// <returns>Short Turkish summary for the log/window.</returns>
        internal static string ApplyCapture(ItemDefinition def, GripCaptureBuffer buffer)
        {
            if (def == null)
            {
                return "Eşya yok — hiçbir şey yazılmadı.";
            }

            if (buffer == null || buffer.entries == null || buffer.entries.Count == 0)
            {
                return "Yakalama boş — hiçbir şey yazılmadı.";
            }

            Undo.RecordObject(def, "Kavrama yakalamasını uygula");

            var written = new List<string>();
            for (int i = 0; i < buffer.entries.Count; i++)
            {
                GripCaptureEntry entry = buffer.entries[i];
                def.EditorSetGrip(entry.kind, entry.rightHand, entry.pose.position,
                    entry.pose.Wrist, entry.pose.fingerJoints);
                written.Add(GripSlotGui.SlotLabel(entry.kind, entry.rightHand));
            }

            bool twoHand = buffer.HasKind(GripSocketKind.Secondary);
            if (twoHand)
            {
                def.EditorSetHoldMode(ItemHoldMode.TwoHand);
            }
            else
            {
                def.EditorClearGrip(GripSocketKind.Secondary, true);
                def.EditorClearGrip(GripSocketKind.Secondary, false);
                def.EditorSetHoldMode(ItemHoldMode.OneHand);
            }

            Commit(def);
            return $"{def.name}: {string.Join(" · ", written)} yazıldı → " +
                   $"{(twoHand ? "çift" : "tek")} kavramalı.";
        }

        /// <summary>
        /// Mirrors one hand's record onto the other across the item's YZ plane.
        /// <para><c>p=(x,y,z) → (−x,y,z)</c> and <c>q=(x,y,z,w) → (x,−y,−z,w)</c> (reflection whose
        /// normal is X). The finger rig is COPIED joint by joint: ISDK's left and right skeletons
        /// share one joint convention, so the same local rotations give the mirrored pose.</para>
        /// <para>⚠️ A STARTING POINT, not the final word: a grip is roughly symmetric but trigger,
        /// magazine and charging handle are one-sided.</para>
        /// </summary>
        internal static bool Mirror(ItemDefinition def, GripSocketKind kind, bool fromRight)
        {
            if (def == null || !def.HasGrip(kind, fromRight))
            {
                Debug.LogWarning($"{LOG} Aynalanacak kavrama yok " +
                                 $"({GripSlotGui.SlotLabel(kind, fromRight)}).");
                return false;
            }

            ItemGripPose src = def.GetGrip(kind, fromRight);
            Pose wrist = ItemGripAuthority.ResolveAnchorToWrist(src, fromRight);

            var mirroredPosition = new Vector3(-src.position.x, src.position.y, src.position.z);
            var mirroredWrist = new Pose(
                new Vector3(-wrist.position.x, wrist.position.y, wrist.position.z),
                new Quaternion(wrist.rotation.x, -wrist.rotation.y, -wrist.rotation.z,
                    wrist.rotation.w));

            HandJointRotation[] fingers = src.fingerJoints == null
                ? Array.Empty<HandJointRotation>()
                : (HandJointRotation[])src.fingerJoints.Clone();

            Undo.RecordObject(def, "Kavramayı aynala");
            def.EditorSetGrip(kind, !fromRight, mirroredPosition, mirroredWrist, fingers);
            Commit(def);

            Debug.Log($"{LOG} {def.name}: {GripSlotGui.SlotLabel(kind, fromRight)} → " +
                      $"{GripSlotGui.SlotLabel(kind, !fromRight)} aynalandı.", def);
            return true;
        }

        internal static void ClearSlot(ItemDefinition def, GripSocketKind kind, bool rightHand)
        {
            if (def == null)
            {
                return;
            }

            Undo.RecordObject(def, "Kavramayı sil");
            def.EditorClearGrip(kind, rightHand);
            Commit(def);

            Debug.Log($"{LOG} {def.name}: {GripSlotGui.SlotLabel(kind, rightHand)} silindi.", def);
        }

        /// <summary>Drops both foregrip records and switches the item to one-handed — the two belong
        /// together (an unread foregrip record is worse than none: see
        /// <see cref="ItemDefinition.HasSecondaryGrip"/>).</summary>
        internal static void MakeOneHanded(ItemDefinition def)
        {
            if (def == null)
            {
                return;
            }

            Undo.RecordObject(def, "Tek kavramalı yap");
            def.EditorClearGrip(GripSocketKind.Secondary, true);
            def.EditorClearGrip(GripSocketKind.Secondary, false);
            def.EditorSetHoldMode(ItemHoldMode.OneHand);
            Commit(def);

            Debug.Log($"{LOG} {def.name}: 2. kavrama silindi, eşya tek kavramalı.", def);
        }

        /// <summary>Dirty + save + the post-save sync, after the caller's <c>Undo.RecordObject</c>.</summary>
        private static void Commit(ItemDefinition def)
        {
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            // ⚠️ delayCall: the kit rewrites the WPN prefab on disk; in the MIDDLE of OnGUI the
            // caller would keep drawing destroyed objects.
            EditorApplication.delayCall += () => PostSaveSync(def);
        }

        // ----------------------------------------------------------------- post-save sync

        /// <summary>Sync run right after a write so the record becomes visible without opening
        /// <c>Configure All Build Elements</c> by hand.
        /// <para>A weapon needs the whole weapon kit (the WPN prefabs, the catalog); any other item
        /// needs only the <c>netItemId</c> → definition catalog. ⚠️ The weapon kit is NOT run for a
        /// non-weapon: it looks for the <c>WPN_*</c> contract and would report its absence as a
        /// problem.</para>
        /// <para>⚠️ The exception is swallowed and only logged (same reason as
        /// <c>BuildElementsConfigurator.SyncWeaponKit</c>): a slip in the sync must not make an
        /// already-written record look like a failed save.</para>
        /// <para>⚠️ No dialog and no progress bar: a modal blocks Unity's main thread and times out
        /// under CLI.</para></summary>
        internal static void PostSaveSync(ItemDefinition def)
        {
            if (def == null)
            {
                return;
            }

            try
            {
                if (def is WeaponDefinition)
                {
                    WeaponKitBuilder.BuildAll();
                    return;
                }

                Debug.Log($"{LOG} Eşya kataloğu eşitlendi — {NetItemIdGuard.Rebuild()}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG} Kavrama kaydı yazıldı ama eşitleme hata verdi — kayıt diskte, " +
                               "eşitleme yapılmadı (Tools > VortexArena > Build > Configure All " +
                               "Build Elements > Hepsini Çalıştır ile tekrar dene): " + e);
            }
        }

        // ------------------------------------------------------------------ target resolve

        /// <summary>Which definition does this prefab's grip belong to.
        /// <para>Order: (1) a component that carries the definition on the prefab
        /// (<see cref="IItemHolder"/>), (2) reverse lookup — the definition whose <c>Prefab</c> field
        /// points at this prefab.</para>
        /// <para>⚠️ Two definitions pointing at the same prefab is NOT resolved silently: the caller
        /// gets <c>null</c> + <paramref name="ambiguous"/> and must ask the user. "Take the first"
        /// would write the grip onto the wrong asset with no tell.</para></summary>
        internal static ItemDefinition ResolveDefinition(GameObject prefabRoot, out bool ambiguous)
        {
            ambiguous = false;
            if (prefabRoot == null)
            {
                return null;
            }

            var holder = prefabRoot.GetComponent<IItemHolder>();
            if (holder != null && holder.Definition != null)
            {
                return holder.Definition;
            }

            if (!PrefabUtility.IsPartOfPrefabAsset(prefabRoot))
            {
                return null;
            }

            return DefinitionForPrefab(AssetDatabase.GetAssetPath(prefabRoot), out ambiguous);
        }

        /// <summary>Definitions whose <c>Prefab</c> points at <paramref name="assetPath"/>, cached.
        /// <para>⚠️ Cached because it is asked from <c>OnGUI</c>: a project-wide <c>FindAssets</c>
        /// per repaint is pure waste. The cache expires on a timer so a definition created while the
        /// window is open shows up on its own.</para></summary>
        private static ItemDefinition DefinitionForPrefab(string assetPath, out bool ambiguous)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                ambiguous = false;
                return null;
            }

            if (assetPath != _lookupPath ||
                EditorApplication.timeSinceStartup - _lookupTime > LookupRefreshSeconds)
            {
                RebuildLookup(assetPath);
            }

            ambiguous = _lookupAmbiguous;
            return _lookupDefinition;
        }

        private static void RebuildLookup(string assetPath)
        {
            _lookupPath = assetPath;
            _lookupTime = EditorApplication.timeSinceStartup;
            _lookupDefinition = null;
            _lookupAmbiguous = false;

            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (definition == null || definition.Prefab == null)
                {
                    continue;
                }

                if (!string.Equals(AssetDatabase.GetAssetPath(definition.Prefab), assetPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_lookupDefinition != null)
                {
                    _lookupAmbiguous = true;
                    _lookupDefinition = null;
                    return;
                }

                _lookupDefinition = definition;
            }
        }
    }
}
