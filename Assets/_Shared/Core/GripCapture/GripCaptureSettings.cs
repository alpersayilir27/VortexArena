using UnityEditor;
using VortexArena.Core.Combat;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// Shared constants of the grip capture tool and its target selection.
    /// <para>The target lives in EditorPrefs, not in the scene: picking an item must not dirty a
    /// committed scene file.</para>
    /// </summary>
    public static class GripCaptureSettings
    {
        public const string ScenePath = "Assets/_Shared/Core/GripCapture/GripCapture.unity";

        public const string TargetPrefKey = "VortexArena.GripCapture.TargetGuid";

        /// <summary>The selected item, or <c>null</c>.</summary>
        public static ItemDefinition LoadTarget()
        {
            string guid = EditorPrefs.GetString(TargetPrefKey, "");
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        }

        public static void SaveTarget(ItemDefinition definition)
        {
            if (definition == null)
            {
                EditorPrefs.DeleteKey(TargetPrefKey);
                return;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(definition, out string guid, out long _))
            {
                EditorPrefs.SetString(TargetPrefKey, guid);
            }
        }
    }
}
