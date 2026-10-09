using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.GripCapture;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// The capture scene root's Inspector: picks WHICH item is being captured and shows what the
    /// item already has.
    /// <para>The picker lives here as well as in the studio window because this is what the user
    /// has in front of them when they open the scene; both read the same EditorPrefs key, not a
    /// scene field — picking an item must not dirty a committed scene.</para>
    /// </summary>
    [CustomEditor(typeof(GripCaptureSession))]
    internal sealed class GripCaptureSessionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ItemDefinition target = GripCaptureSettings.LoadTarget();

            // The target cannot change mid-session: the buffer carries one item's guid.
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                var picked = (ItemDefinition)EditorGUILayout.ObjectField(
                    "Eşya", target, typeof(ItemDefinition), false);
                if (picked != target)
                {
                    GripCaptureSettings.SaveTarget(picked);
                    target = picked;
                }
            }

            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "Eşya seçilmedi — yakalanacak eşya tanımını (WD_*/ID_*) yukarıdaki alana ver.",
                    MessageType.Warning);
                return;
            }

            if (target.Prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Bu eşya tanımının Prefab alanı boş — sahnede gösterilecek eşya yok.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            GripSlotGui.DrawSlots(target, null);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(GripSlotGui.UsageText, MessageType.None);

            if (GUILayout.Button("Kavrama Pozu Stüdyosu'nu aç"))
            {
                GripPoseStudio.Open();
            }
        }
    }
}
