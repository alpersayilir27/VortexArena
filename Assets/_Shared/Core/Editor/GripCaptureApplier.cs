using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.GripCapture;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Writes a Play session's captures into the item when Play EXITS.
    /// <para>⚠️ The write cannot happen during Play: the record goes to disk via
    /// <c>AssetDatabase</c> and a value written in Play becomes ambiguous at the next domain
    /// reload — hence the on-disk buffer (<see cref="GripCaptureBuffer"/>) in between.</para>
    /// <para>⚠️ Deliberately NOT applied on editor load / domain reload: a buffer left behind by a
    /// crash would silently overwrite an item days later. A leftover file is shown in
    /// <c>Kavrama Pozu Stüdyosu</c> instead, to be applied or dropped by hand.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class GripCaptureApplier
    {
        static GripCaptureApplier()
        {
            // Remove before add: a domain reload would otherwise double-subscribe.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && GripCaptureBuffer.Exists)
            {
                Apply();
            }
        }

        /// <summary>Applies the buffer and deletes it; the file is KEPT when the item cannot be
        /// resolved (a renamed/moved asset must not lose the session).</summary>
        /// <returns>Turkish result line, also logged.</returns>
        internal static string Apply()
        {
            GripCaptureBuffer buffer = GripCaptureBuffer.Load();
            if (buffer == null)
            {
                string unreadable = "Yakalama tamponu okunamadı — hiçbir şey yazılmadı.";
                Debug.LogWarning($"{GripRecordOps.LOG} {unreadable}");
                return unreadable;
            }

            string path = AssetDatabase.GUIDToAssetPath(buffer.definitionGuid ?? "");
            ItemDefinition def = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def == null)
            {
                string missing = "Yakalamanın eşyası bulunamadı (taşındı/silindi?) — tampon " +
                                 "silinmedi, stüdyoda elle uygulayabilirsin.";
                Debug.LogWarning($"{GripRecordOps.LOG} {missing}");
                return missing;
            }

            string result = GripRecordOps.ApplyCapture(def, buffer);
            GripCaptureBuffer.Delete();
            Debug.Log($"{GripRecordOps.LOG} {result}", def);
            return result;
        }
    }
}
