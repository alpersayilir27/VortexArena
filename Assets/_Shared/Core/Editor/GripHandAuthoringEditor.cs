using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <see cref="GripHandAuthoring"/>'in Inspector yüzü: parmak duruşu preset'i ve iki düğme
    /// (aynala / eli kaldır).
    /// <para>
    /// ⚠️ <b>Kaydet düğmesi burada YOKTUR ve geri eklenmez:</b> yazan tek düğme stüdyo
    /// penceresindedir (<see cref="GripPoseStudio"/>). İkinci bir kaydet düğmesi aynı işi iki
    /// yerden koştururdu; kayıt artık silah kitini de tetiklediği için (prefab kipi içeriği yeniden
    /// yüklenir) hangi düğmenin neyi koşturduğu belirsizleşirdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Undo kaydı TUTULMAZ.</b> Bu objeler <see cref="HideFlags.DontSave"/>'dir ve pencere,
    /// stage ya da Play geçişinde yok edilirler; onlara yazılan bir Undo adımı sahne geçmişine ölü
    /// bir kayıt bırakır ve kullanıcı Ctrl+Z'lediğinde "missing" bir objeyi geri almaya çalışır.
    /// Kaybolacak tek şey ayarlanmamış bir el duruşudur; kalıcı olan her şey Kaydet'ten geçer.
    /// </para>
    /// <para>
    /// ⚠️ Parmak slider'ı / eklem ince ayarı YOKTUR ve eklenmez: tezgâhta ayarlanabilen ama kayda
    /// giremeyen bir duruş, oyunda hiç görülmeyecek bir ince ayar olurdu. Parmakların tek kaynağı
    /// <see cref="HandGripPresets"/>'tir ve stüdyodaki el ile oyundaki sentetik el aynı diziyi
    /// uygular.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(GripHandAuthoring))]
    internal sealed class GripHandAuthoringEditor : UnityEditor.Editor
    {
        // Etiketler HandGripPresets'ten okunur: enum adlarını burada ikinci kez yazmak, yeni bir
        // preset eklendiğinde sessizce eskimiş bir liste bırakırdı.
        private static readonly HandGripPreset[] Presets =
            (HandGripPreset[])System.Enum.GetValues(typeof(HandGripPreset));

        public override void OnInspectorGUI()
        {
            var hand = (GripHandAuthoring)target;

            EditorGUILayout.LabelField(
                $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")} el", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Bu objenin transformu KUMANDA (anchor) çerçevesidir — Scene'de yalnız TAŞI (dönüş " +
                "kaydedilmez, silah her zaman kumandayla hizalıdır); kumanda modeli ve hayalet el " +
                "köke bağlı çizilir.",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();

            string[] labels = new string[Presets.Length];
            int current = 0;
            for (int i = 0; i < Presets.Length; i++)
            {
                labels[i] = HandGripPresets.Label(Presets[i]);
                if (Presets[i] == hand.Preset)
                {
                    current = i;
                }
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("Parmak duruşu", current, labels);
            if (EditorGUI.EndChangeCheck())
            {
                // Setter preset'i kemiklere uyguluyor; Scene View kendiliğinden tazelenmediği için
                // burada elle tazelenir (yoksa seçim değişir, el aynı durur).
                hand.Preset = Presets[picked];
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
                    // ⚠️ Obje yok edildi: bu karede Inspector çizimine devam etmek yok edilmiş
                    // hedefe erişmektir (MissingReferenceException).
                    return;
                }
            }

            EditorGUILayout.HelpBox(
                "Scene'de yalnız kumanda kökünün YERİNİ ayarlarsın (dönüş yok — silah oyunda her zaman " +
                "kumandayla hizalıdır, kök silahla hizalı tutulur); parmaklar preset'ten gelir. Ön " +
                "kabzada el silaha yapışır, silah ikinci ele göre dönmez. Kaydet stüdyo " +
                "penceresindedir.",
                MessageType.None);
        }
    }
}
