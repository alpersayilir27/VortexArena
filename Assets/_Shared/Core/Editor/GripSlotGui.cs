using System;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>Slot labels and the four-row state list, shared by the studio window and the capture
    /// scene's Inspector — two drawings of the same state would drift apart.</summary>
    internal static class GripSlotGui
    {
        /// <summary>Capture usage, shown in both GUIs.</summary>
        internal const string UsageText =
            "A/X = 1. kavrama, B/Y = 2. kavrama. SOL kumandada basmak SAĞ eli, SAĞ kumandada " +
            "basmak SOL eli kaydeder. Çubuk eşyayı döndürür, çubuğa basmak eşyayı önüne getirir. " +
            "Play'den çıkınca yakalananlar eşyaya yazılır.";

        internal static readonly GripSocketKind[] Kinds =
            { GripSocketKind.Primary, GripSocketKind.Secondary };

        internal static string KindLabel(GripSocketKind kind)
        {
            return kind == GripSocketKind.Secondary ? "2. kavrama" : "1. kavrama";
        }

        internal static string SlotLabel(GripSocketKind kind, bool rightHand)
        {
            return $"{KindLabel(kind)} · {(rightHand ? "Sağ" : "Sol")}";
        }

        internal static string HoldModeLabel(ItemDefinition def)
        {
            return def != null && def.IsTwoHanded ? "Çift kavramalı" : "Tek kavramalı";
        }

        /// <summary>One slot's state. ⚠️ <c>GetGrip</c> is read only when the slot is authored FOR
        /// THIS HAND: it falls back to the other hand's record, which would show a missing slot as
        /// written.</summary>
        internal static string SlotState(ItemDefinition def, GripSocketKind kind, bool rightHand)
        {
            if (def == null || !def.HasGrip(kind, rightHand))
            {
                return "— yok";
            }

            ItemGripPose grip = def.GetGrip(kind, rightHand);
            int joints = grip.HasFingers ? grip.fingerJoints.Length : 0;
            return joints > 0 ? $"✔ yazılı ({joints} eklem)" : "✔ yazılı (parmak yok)";
        }

        /// <summary>Draws the hold mode + the four slot rows; <paramref name="onDelete"/>
        /// <c>null</c> = read-only list (no "Sil" button).</summary>
        internal static void DrawSlots(ItemDefinition def, Action<GripSocketKind, bool> onDelete)
        {
            EditorGUILayout.LabelField("Tutuş", HoldModeLabel(def));

            for (int k = 0; k < Kinds.Length; k++)
            {
                GripSocketKind kind = Kinds[k];
                for (int h = 0; h < 2; h++)
                {
                    bool rightHand = h == 0;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(SlotLabel(kind, rightHand),
                            SlotState(def, kind, rightHand));
                        if (onDelete == null)
                        {
                            continue;
                        }

                        using (new EditorGUI.DisabledScope(
                                   def == null || !def.HasGrip(kind, rightHand)))
                        {
                            if (GUILayout.Button("Sil", GUILayout.Width(44f)))
                            {
                                onDelete(kind, rightHand);
                            }
                        }
                    }
                }
            }
        }
    }
}
