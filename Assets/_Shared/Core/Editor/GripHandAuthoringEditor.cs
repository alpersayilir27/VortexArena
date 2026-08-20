using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>Inspector for <see cref="GripHandAuthoring"/>: hand identity and three buttons
    /// (reset fingers / mirror / remove hand).</summary>
    /// <remarks>
    /// ⚠️ There is no save button here and none is added back: the only writing button lives in the
    /// studio window (<see cref="GripPoseStudio"/>). A second one would run the same job from two
    /// places, and since saving also syncs the weapon kit (reloading prefab stage contents) it would
    /// blur which button does what.
    /// <para>⚠️ The finger joint picker is in the studio window too: selecting a joint makes the
    /// Inspector show that joint's <see cref="Transform"/> instead of this component, so a picker
    /// here would close itself on the first click. The window stays open regardless of
    /// selection.</para>
    /// <para>⚠️ No finger slider or numeric joint field, ever: the pose lives in the hand's bones and
    /// is rigged by rotating them in the Scene View; writing the same pose again as numbers would
    /// raise "which one wins". What gets saved is always the bones' current state.</para>
    /// <para>⚠️ No Undo is recorded: these objects are <see cref="HideFlags.DontSave"/> and die on a
    /// window, stage or Play transition, so an Undo step would leave a dead entry that tries to
    /// restore a "missing" object. The only thing lost is an unsaved hand pose; everything permanent
    /// goes through Save.</para>
    /// </remarks>
    [CustomEditor(typeof(GripHandAuthoring))]
    internal sealed class GripHandAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var hand = (GripHandAuthoring)target;

            EditorGUILayout.LabelField(
                $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")} el", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Bu objenin transformu KUMANDA (anchor) çerçevesidir — Scene'de yalnız TAŞI (dönüş " +
                "kaydedilmez, silah her zaman kumandayla hizalıdır). Elin kumanda üstündeki yeri ve " +
                "açısı AYRI yazılır: alttaki 'Hand' objesini taşı/çevir (silah kımıldamaz).",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parmak rigi", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Eklem seçicisi stüdyo penceresindedir (Kavrama Pozu Stüdyosu). Seçtiğin eklemi " +
                "Scene View'da döndürme aracıyla çevir; kayda giren şey kemiklerin o anki hâlidir.",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Parmakları Sıfırla (boş el duruşu)", GUILayout.Height(22f)))
            {
                // null = no record → idle hand pose (HandPoseLibrary.IdleJointRotations).
                hand.ApplyPose(null);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Karşı Ele Aynala", GUILayout.Height(24f)))
                {
                    GripPoseStudio.MirrorToOpposite(hand);
                }

                if (GUILayout.Button("Bu Eli Kaldır", GUILayout.Height(24f)))
                {
                    DestroyImmediate(hand.gameObject);
                    SceneView.RepaintAll();
                    // ⚠️ Object destroyed: drawing further this frame would touch a destroyed target
                    // (MissingReferenceException).
                    return;
                }
            }

            EditorGUILayout.HelpBox(
                "Scene'de kumanda kökünün YERİNİ ayarlarsın (dönüş yok — silah oyunda her zaman " +
                "kumandayla hizalıdır, kök silahla hizalı tutulur); el modelini kumandanın üstünde " +
                "taşıyıp ÇEVİRİRSİN (silahın duruşunu değiştirmez, silah başına yazılır); parmakları " +
                "da elin kemiklerini çevirerek riglersin. Ön kabzada el silaha yapışır, silah ikinci " +
                "ele göre dönmez. Kaydet stüdyo penceresindedir.",
                MessageType.None);
        }
    }
}
