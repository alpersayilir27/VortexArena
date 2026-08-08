using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <see cref="GripHandAuthoring"/>'in Inspector yüzü: parmak kıvrımı/açıklığı, eklem ince ayarı,
    /// parmak serbestliği ve iki düğme (aynala / sıfırla).
    /// <para>
    /// ⚠️ <b>Undo kaydı TUTULMAZ.</b> Bu objeler <see cref="HideFlags.DontSave"/>'dir ve pencere,
    /// stage ya da Play geçişinde yok edilirler; onlara yazılan bir Undo adımı sahne geçmişine ölü
    /// bir kayıt bırakır ve kullanıcı Ctrl+Z'lediğinde "missing" bir objeyi geri almaya çalışır.
    /// Kaybolacak tek şey ayarlanmamış bir el duruşudur; kalıcı olan her şey Kaydet'ten geçer.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(GripHandAuthoring))]
    internal sealed class GripHandAuthoringEditor : UnityEditor.Editor
    {
        private static readonly string[] FingerLabels =
            { "Baş parmak", "İşaret", "Orta", "Yüzük", "Serçe" };

        [SerializeField] private bool _advanced;

        public override void OnInspectorGUI()
        {
            var hand = (GripHandAuthoring)target;

            EditorGUILayout.LabelField(
                $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")} el", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Bu objenin transformu ISDK BİLEK çerçevesidir — Scene'de sürükleyip çevir.",
                EditorStyles.miniLabel);

            if (!hand.HasBindBoneBasis)
            {
                EditorGUILayout.HelpBox(
                    "Elin anatomik bazı ölçülemedi: parmak slider'ları çalışmaz ve Kaydet bu elden " +
                    "WD kavrama alanlarını yazmaz (poz düğümü yine yazılır).",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parmaklar", EditorStyles.boldLabel);
            _advanced = EditorGUILayout.Foldout(_advanced, "Eklem ince ayarını göster", true);

            bool changed = false;
            GripHandAuthoring.Finger[] fingers = hand.Fingers;

            for (int f = 0; f < fingers.Length && f < FingerLabels.Length; f++)
            {
                changed |= DrawFinger(FingerLabels[f], fingers[f]);
            }

            if (changed)
            {
                hand.ApplyFingers();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Karşı Ele Aynala", GUILayout.Height(24f)))
                {
                    GripPoseStudio.MirrorToOpposite(hand);
                }

                if (GUILayout.Button("Parmakları Sıfırla", GUILayout.Height(24f)))
                {
                    hand.ResetFingers();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.HelpBox(
                "Slider'lar kolaylıktır: Kaydet HER ZAMAN kemiklerin o andaki gerçek duruşunu okur, " +
                "yani parmakları Hierarchy'den elle de bükebilirsin.",
                MessageType.None);
        }

        private bool DrawFinger(string label, GripHandAuthoring.Finger finger)
        {
            bool changed = false;

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            finger.Curl = EditorGUILayout.Slider("Kıvrım", finger.Curl, 0f, 1f);
            finger.Spread = EditorGUILayout.Slider("Açıklık", finger.Spread, -1f, 1f);
            changed |= EditorGUI.EndChangeCheck();

            // Serbestlik kemiğe yazılmaz, yalnız kayda gider — değişimi ApplyFingers tetiklemez.
            finger.Freedom = (JointFreedom)EditorGUILayout.EnumPopup("Serbestlik", finger.Freedom);

            if (_advanced && finger.FineDegrees != null)
            {
                EditorGUI.indentLevel++;
                for (int j = 0; j < finger.FineDegrees.Length; j++)
                {
                    EditorGUI.BeginChangeCheck();
                    finger.FineDegrees[j] = EditorGUILayout.Slider(
                        $"Eklem {j + 1}", finger.FineDegrees[j], -45f, 45f);
                    changed |= EditorGUI.EndChangeCheck();
                }

                EditorGUI.indentLevel--;
            }

            return changed;
        }
    }
}
