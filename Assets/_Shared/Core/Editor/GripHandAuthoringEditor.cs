using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <see cref="GripHandAuthoring"/>'in Inspector yüzü: elin kimliği ve üç düğme (parmakları
    /// sıfırla / aynala / eli kaldır).
    /// <para>
    /// ⚠️ <b>Kaydet düğmesi burada YOKTUR ve geri eklenmez:</b> yazan tek düğme stüdyo
    /// penceresindedir (<see cref="GripPoseStudio"/>). İkinci bir kaydet düğmesi aynı işi iki
    /// yerden koştururdu; kayıt artık silah kitini de tetiklediği için (prefab kipi içeriği yeniden
    /// yüklenir) hangi düğmenin neyi koşturduğu belirsizleşirdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Parmak eklemi seçicisi de burada DEĞİL stüdyo penceresindedir.</b> Sebep basit:
    /// bir eklem seçildiği anda Inspector artık bu bileşeni değil o eklemin
    /// <see cref="Transform"/>'unu gösterir — seçici burada olsaydı ilk tıklamada kendi kendini
    /// kapatır ve ikinci eklemi seçmenin yolu kalmazdı. Pencere seçimden bağımsız açık kalır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Parmak slider'ı / sayısal eklem alanı YOKTUR ve eklenmez.</b> Duruş elin kemiklerinde
    /// yaşıyor ve Scene View'da çevrilerek rigleniyor; aynı duruşu ikinci kez sayı olarak yazmak
    /// "hangisi geçerli" sorusunu doğururdu. Kayda giren şey her zaman kemiklerin o anki hâlidir.
    /// </para>
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
            EditorGUILayout.LabelField("Parmak rigi", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Eklem seçicisi stüdyo penceresindedir (Kavrama Pozu Stüdyosu). Seçtiğin eklemi " +
                "Scene View'da döndürme aracıyla çevir; kayda giren şey kemiklerin o anki hâlidir.",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Parmakları Sıfırla (boş el duruşu)", GUILayout.Height(22f)))
            {
                // null = kayıt yok → boş elin duruşu (HandPoseLibrary.IdleJointRotations).
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
                    // ⚠️ Obje yok edildi: bu karede Inspector çizimine devam etmek yok edilmiş
                    // hedefe erişmektir (MissingReferenceException).
                    return;
                }
            }

            EditorGUILayout.HelpBox(
                "Scene'de kumanda kökünün YERİNİ ayarlarsın (dönüş yok — silah oyunda her zaman " +
                "kumandayla hizalıdır, kök silahla hizalı tutulur), parmakları ise elin kemiklerini " +
                "çevirerek riglersin. Ön kabzada el silaha yapışır, silah ikinci ele göre dönmez. " +
                "Kaydet stüdyo penceresindedir.",
                MessageType.None);
        }
    }
}
