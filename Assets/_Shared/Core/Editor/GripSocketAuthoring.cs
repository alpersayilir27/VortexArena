using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Kavrama noktalarını <b>sahnede sürükleyerek</b> ayarlama aracı. Üç menü öğesi:
    /// <list type="bullet">
    /// <item><c>GameObject &gt; VortexArena &gt; Grip Socket (Primary)</c> — ana el işaretçisi üretir</item>
    /// <item><c>GameObject &gt; VortexArena &gt; Grip Socket (Secondary)</c> — ön kabza işaretçisi</item>
    /// <item><c>Tools &gt; VortexArena &gt; Write Grip Sockets To Definition</c> — işaretçileri SO'ya yazar</item>
    /// </list>
    /// <para>
    /// Akış: işaretçiyi üret → silahın kabzasına sürükle ve el gibi DÖNDÜR → yarıçapı ayarla →
    /// SO'ya yaz. İşaretçi (<see cref="GripSocketMarker"/>) yalnız bir yazma aracıdır; oyun onu
    /// okumaz, dolayısıyla yazdıktan sonra silinebilir ya da bırakılabilir.
    /// </para>
    /// <para>
    /// <b>Aracın var olma sebebi ASİMETRİDİR:</b> iki kavrama alanı iki ayrı uzayda ifade edilir
    /// (<see cref="ItemDefinition"/> başındaki uyarı) — <c>primaryGrip</c> "el → eşya",
    /// <c>secondaryGrip</c> "eşya → el". Yani aynı sürüklenmiş poz, biri için TERS bileşimle,
    /// öteki için DÜZ yazılır. Elle yapıldığında bu fark sessiz bir işaret hatası üretiyordu
    /// (silah elde ters/uzakta duruyor, hiçbir yerde hata basılmıyor).
    /// </para>
    /// <para>
    /// ⚠️ <b>Dialog YOK</b> (<c>WeaponKitBuilder</c>'daki aynı gerekçe): modal dialog Unity ana
    /// thread'ini kilitler ve CLI/pipeline üzerinden çalıştırıldığında komut timeout verir. Sonuç
    /// <see cref="Debug.Log"/> ile bildirilir.
    /// </para>
    /// </summary>
    internal static class GripSocketAuthoring
    {
        private const string LOG = "[GripSocket]";

        private const string PRIMARY_NODE = "GripSocket_Primary";
        private const string SECONDARY_NODE = "GripSocket_Secondary";

        // ------------------------------------------------------------ işaretçi üretme

        [MenuItem("GameObject/VortexArena/Grip Socket (Primary)", false, 10)]
        private static void CreatePrimary(MenuCommand command)
        {
            CreateMarker(command, GripSocketKind.Primary);
        }

        [MenuItem("GameObject/VortexArena/Grip Socket (Secondary)", false, 11)]
        private static void CreateSecondary(MenuCommand command)
        {
            CreateMarker(command, GripSocketKind.Secondary);
        }

        /// <summary>
        /// İşaretçiyi silahın KÖKÜNÜN altına üretir ve <b>mevcut SO değerlerinden başlatır</b>
        /// (round trip): böylece araç ayarı sıfırlamaz, var olanı düzeltmeye izin verir.
        /// <para>Aynı türde işaretçi zaten varsa ikincisi ÜRETİLMEZ, var olan seçilir — iki işaretçi
        /// olsaydı "hangisi yazılacak" sorusunun cevabı sessizce sıralamaya kalırdı.</para>
        /// </summary>
        private static void CreateMarker(MenuCommand command, GripSocketKind kind)
        {
            // Hiyerarşi bağlam menüsünde Unity komutu SEÇİLEN HER OBJE için bir kez çağırır;
            // tek işaretçi ürettiğimiz için yalnız ilk çağrıyı geçiriyoruz (SpawnPointMenu'deki
            // aynı emniyet).
            GameObject[] selection = Selection.gameObjects;
            if (command.context != null && selection.Length > 1 && command.context != selection[0])
            {
                return;
            }

            GameObject context = command.context as GameObject ?? Selection.activeGameObject;
            if (context == null)
            {
                Debug.LogWarning($"{LOG} Seçim yok — işaretçi SİLAHIN altına konur: önce sahnedeki " +
                                 "silahı (ya da altındaki bir parçasını) seç.");
                return;
            }

            Weapon weapon = context.GetComponentInParent<Weapon>();
            if (weapon == null)
            {
                Debug.LogWarning($"{LOG} '{context.name}' üstünde ya da ebeveyninde Weapon yok — " +
                                 "işaretçi silahın altına konur (kavrama pozu silah köküne göre ölçülür).",
                    context);
                return;
            }

            Transform root = weapon.transform;
            string nodeName = kind == GripSocketKind.Primary ? PRIMARY_NODE : SECONDARY_NODE;

            GripSocketMarker existing = FindMarker(weapon, kind);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log($"{LOG} '{root.name}' altında {kind} işaretçisi zaten var — yenisi " +
                          "üretilmedi, var olan seçildi.", existing);
                return;
            }

            WeaponDefinition def = weapon.Definition;
            if (def == null)
            {
                Debug.LogWarning($"{LOG} '{root.name}' silahının tanımı (WeaponDefinition) atanmamış — " +
                                 "işaretçi varsayılan değerlerle üretildi ve SO'ya yazılamaz.", weapon);
            }

            var go = new GameObject(nodeName, typeof(GripSocketMarker));
            go.transform.SetParent(root, false);

            var marker = go.GetComponent<GripSocketMarker>();
            var markerSo = new SerializedObject(marker);
            markerSo.FindProperty("kind").enumValueIndex = (int)kind;

            if (def != null)
            {
                if (kind == GripSocketKind.Primary)
                {
                    // ⚠️ Primary'de SO ters uzayda duruyor ("el → eşya"), bu yüzden geri okuma da
                    // ters: nokta türetilmiş property'den, dönüş ise Inverse ile alınır.
                    go.transform.localPosition = def.PrimaryGripPointOnItem;
                    go.transform.localRotation = Quaternion.Inverse(def.PrimaryGripRotation);
                    markerSo.FindProperty("radius").floatValue = def.PrimaryGripRadius;
                }
                else
                {
                    // Secondary zaten eşya-yerel: düz okunur.
                    go.transform.localPosition = def.SecondaryGripPosition;
                    go.transform.localRotation = def.SecondaryGripRotation;
                    markerSo.FindProperty("radius").floatValue = def.SecondaryGripRadius;
                }
            }

            markerSo.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(go, $"VortexArena Grip Socket ({kind})");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            // holdMode sonradan TwoHand'e çevrilebilir — üretimi engellemek "önce tanımı düzelt,
            // sonra işaretçiyi koy" sırasını dayatırdı. Uyarı yeter.
            if (kind == GripSocketKind.Secondary && def != null && !def.IsTwoHanded)
            {
                Debug.LogWarning($"{LOG} '{def.name}' tek elli (holdMode = OneHand) — ön kabza soketi " +
                                 "oyunda HİÇ açılmaz. İşaretçi yine üretildi (holdMode sonradan " +
                                 "değişebilir), ama TwoHand yapılmadıkça etkisi olmaz.", def);
            }

            Debug.Log($"{LOG} {kind} işaretçisi '{root.name}' altında üretildi — kabzaya sürükle, elin " +
                      "gireceği açıyla döndür, yarıçapı ayarla; sonra " +
                      "Tools > VortexArena > Write Grip Sockets To Definition.", go);
        }

        // ------------------------------------------------------------------ SO'ya yazma

        [MenuItem("Tools/VortexArena/Write Grip Sockets To Definition")]
        private static void WriteToDefinition()
        {
            GameObject context = Selection.activeGameObject;
            if (context == null)
            {
                Debug.LogWarning($"{LOG} Seçim yok — yazmak için sahnedeki silahı (ya da altındaki " +
                                 "bir işaretçiyi) seç.");
                return;
            }

            Weapon weapon = context.GetComponentInParent<Weapon>();
            if (weapon == null)
            {
                Debug.LogWarning($"{LOG} '{context.name}' üstünde ya da ebeveyninde Weapon yok — " +
                                 "hangi tanıma yazılacağı belirlenemedi.", context);
                return;
            }

            WeaponDefinition def = weapon.Definition;
            if (def == null)
            {
                Debug.LogWarning($"{LOG} '{weapon.name}' silahının tanımı (WeaponDefinition) atanmamış — " +
                                 "yazılacak asset yok.", weapon);
                return;
            }

            GripSocketMarker primary = FindMarker(weapon, GripSocketKind.Primary);
            GripSocketMarker secondary = FindMarker(weapon, GripSocketKind.Secondary);

            if (primary == null && secondary == null)
            {
                Debug.LogWarning($"{LOG} '{weapon.name}' altında hiç GripSocketMarker yok — " +
                                 "GameObject > VortexArena > Grip Socket (Primary/Secondary) ile üret.",
                    weapon);
                return;
            }

            Transform root = weapon.transform;
            var so = new SerializedObject(def);
            var report = new StringBuilder();

            if (primary != null)
            {
                LocalPose(root, primary.transform, out Vector3 localPos, out Quaternion localRot);

                // ⚠️ TERS BİLEŞİM — primaryGrip "el → eşya" yönünde ifade edilir, işaretçi ise
                // "eşya → el" yönünde ölçülür (eşyanın üstünde bir nokta). Dönüşüm:
                //   R = Inverse(localRot)          (eşyanın ele göre dönüşü)
                //   P = -(R * localPos)            (eşyanın ele göre konumu)
                // Doğrulama kimliği: geri okuma Inverse(R) * (-P) tekrar localPos vermeli
                // (ItemDefinition.PrimaryGripPointOnItem tam olarak bunu hesaplıyor) — yani
                // CreateMarker'ın round trip'i işaretçiyi aynı yere geri koyar.
                Quaternion primaryRotation = Quaternion.Inverse(localRot);
                Vector3 primaryPosition = -(primaryRotation * localPos);

                so.FindProperty("primaryGripPosition").vector3Value = primaryPosition;
                so.FindProperty("primaryGripEuler").vector3Value = primaryRotation.eulerAngles;
                so.FindProperty("primaryGripRadius").floatValue = primary.Radius;

                report.AppendLine($"  Primary  pos={Fmt(primaryPosition)} euler={Fmt(primaryRotation.eulerAngles)} " +
                                  $"r={primary.Radius:0.###} (işaretçi eşya-yerel: {Fmt(localPos)})");
            }

            if (secondary != null)
            {
                LocalPose(root, secondary.transform, out Vector3 localPos, out Quaternion localRot);

                // DÜZ bileşim — secondaryGrip zaten eşya-yereldir ("eşya → el"), işaretçinin ölçtüğü
                // uzayla aynı. Primary ile bu farkı elle tutturmak aracın var olma sebebi.
                so.FindProperty("secondaryGripPosition").vector3Value = localPos;
                so.FindProperty("secondaryGripEuler").vector3Value = localRot.eulerAngles;
                so.FindProperty("secondaryGripRadius").floatValue = secondary.Radius;

                report.AppendLine($"  Secondary pos={Fmt(localPos)} euler={Fmt(localRot.eulerAngles)} " +
                                  $"r={secondary.Radius:0.###}");
            }

            // ⚠️ Yalnız BULUNAN işaretçilerin alanları yazılır: Secondary işaretçisi yoksa secondary
            // alanlarına dokunulmaz — yarısı ayarlanmış bir silahı sıfırlamak sessiz bir gerileme
            // olurdu (tüfek elde durur ama ön kabza kabzanın içine çöker).
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();

            if (primary == null)
            {
                report.AppendLine("  Secondary yalnız — primary alanlarına DOKUNULMADI.");
            }
            else if (secondary == null)
            {
                report.AppendLine("  Primary yalnız — secondary alanlarına DOKUNULMADI.");
            }

            Debug.Log($"{LOG} '{def.name}' güncellendi:\n{report}" +
                      "İşaretçileri silebilir ya da bırakabilirsin — oyun onları okumaz. " +
                      "Camgöbeği soket gizmo'su artık sarı küreyle ÇAKIŞMALI; çakışmıyorsa " +
                      "yazma beklendiği gibi gitmedi.", def);
        }

        // ------------------------------------------------------------------ yardımcı

        /// <summary>
        /// İşaretçinin silah köküne göre yerel pozu.
        /// <para>⚠️ <c>root.InverseTransformPoint</c> KULLANILMAZ: kavrama ofseti METRE cinsindendir
        /// ve araya giren transformların ölçeği ona bulaşmamalı (aynı gerekçe
        /// <c>Weapon.ApplyCanonicalGrip</c> ve <c>ItemGripSockets.PrimarySocketWorld</c>'de de
        /// elle bileşim yaptırıyor). Bu yüzden yalnız konum farkı + dönüş bileşimi.</para>
        /// </summary>
        private static void LocalPose(Transform root, Transform marker, out Vector3 localPos, out Quaternion localRot)
        {
            Quaternion invRoot = Quaternion.Inverse(root.rotation);
            localPos = invRoot * (marker.position - root.position);
            localRot = invRoot * marker.rotation;
        }

        /// <summary>Silahın altındaki ilk (inaktif dahil) belirtilen türde işaretçi; yoksa null.</summary>
        private static GripSocketMarker FindMarker(Weapon weapon, GripSocketKind kind)
        {
            GripSocketMarker[] markers = weapon.GetComponentsInChildren<GripSocketMarker>(true);
            GripSocketMarker found = null;

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i].Kind != kind)
                {
                    continue;
                }

                if (found == null)
                {
                    found = markers[i];
                    continue;
                }

                // Fazlalık sessizce yutulmaz: "hangisi yazıldı" sorusu konsolda görünsün.
                Debug.LogWarning($"{LOG} '{weapon.name}' altında birden çok {kind} işaretçisi var — " +
                                 $"'{found.name}' kullanılıyor, fazlalığı sil.", markers[i]);
            }

            return found;
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:0.####}, {v.y:0.####}, {v.z:0.####})";
        }
    }
}
