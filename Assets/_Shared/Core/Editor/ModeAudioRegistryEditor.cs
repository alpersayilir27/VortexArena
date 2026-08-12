using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Audio;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <see cref="ModeAudioRegistry"/>'nin Inspector yüzü: kural satırlarını modId/sahne adı
    /// yazdırmak yerine <b>katalogdan</b> seçtirir ve satır başına sessiz kalacak kuralları
    /// uyarıyla işaretler.
    /// <para>
    /// ⚠️ Varsayılan çizim KULLANILMAZ: bu kaydın iki alanı (mod, harita) serbest string'dir ve
    /// elle yazılan bir harf hatası derlemeyi kırmaz — kural yalnızca hiç eşleşmez. Katalog
    /// açılır listesi o hatayı imkânsız kılar; katalog bulunamazsa alanlar yine düz metin olarak
    /// çizilir (kaydı düzenlemek editörsüz de mümkün kalsın).
    /// </para>
    /// <para>
    /// Katalog burada da <c>Resources.Load</c> ile alınır — çalışma anındaki yolun aynısı, ikinci
    /// bir referans (ve onunla birlikte ikinci bir doğruluk kaynağı) açılmaz.
    /// </para>
    /// <para>
    /// Ses önizleme düğmesi YOKTUR ve eklenmez: editörde klip çalmanın tek yolu
    /// <c>UnityEditor.AudioUtil</c> refleksiyonudur, o da internal olduğu için sürüm atlayınca
    /// sessizce kırılır.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(ModeAudioRegistry))]
    internal sealed class ModeAudioRegistryEditor : UnityEditor.Editor
    {
        private const string CATALOG_RESOURCE = "GameCatalog";

        /// <summary>Boş seçim = "kısıt yok"; her iki listenin de 0. sırasında durur.</summary>
        private const string ANY_MODE_LABEL = "(her mod)";
        private const string ANY_MAP_LABEL = "(her harita)";

        private GameCatalog _catalog;

        // Değerler (0. eleman daima boş string) ve onlara karşılık gelen açılır liste etiketleri.
        private string[] _modeIds;
        private string[] _modeLabels;
        private string[] _mapIds;
        private string[] _mapLabels;

        private SerializedProperty _rules;

        private void OnEnable()
        {
            _rules = serializedObject.FindProperty("rules");
            _catalog = Resources.Load<GameCatalog>(CATALOG_RESOURCE);
            BuildOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Eşleşen kurallar arasından en spesifik olan kazanır (mod 2 puan, harita 1); " +
                "eşitlikte listedeki İLK kural çalar. Boş mod = her mod, boş harita = her harita.",
                MessageType.None);

            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "GameCatalog bulunamadı (Resources/GameCatalog). Mod ve harita alanları elle " +
                    "yazılıyor — sahne/mod adını BİREBİR yaz, yanlış yazılan kural sessizce hiç " +
                    "eşleşmez.",
                    MessageType.Warning);
            }

            if (_rules == null)
            {
                EditorGUILayout.HelpBox("'rules' alanı bulunamadı.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();

            int removeIndex = -1;
            for (int i = 0; i < _rules.arraySize; i++)
            {
                if (DrawRule(i))
                {
                    removeIndex = i;
                }
            }

            if (removeIndex >= 0)
            {
                _rules.DeleteArrayElementAtIndex(removeIndex);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Kural Ekle", GUILayout.Height(24f)))
            {
                AddRule();
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Tek bir kural kutusunu çizer; kullanıcı Sil'e bastıysa <c>true</c> döner.</summary>
        private bool DrawRule(int index)
        {
            SerializedProperty rule = _rules.GetArrayElementAtIndex(index);
            SerializedProperty modeId = rule.FindPropertyRelative("modeId");
            SerializedProperty sceneName = rule.FindPropertyRelative("sceneName");
            SerializedProperty trigger = rule.FindPropertyRelative("trigger");
            SerializedProperty clips = rule.FindPropertyRelative("clips");
            SerializedProperty volume = rule.FindPropertyRelative("volume");
            SerializedProperty warningSeconds = rule.FindPropertyRelative("warningSeconds");

            bool remove = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                string triggerName = TriggerName(trigger);
                EditorGUILayout.LabelField($"#{index} — {triggerName}", EditorStyles.boldLabel);
                if (GUILayout.Button("Sil", GUILayout.Width(50f)))
                {
                    remove = true;
                }
            }

            DrawIdField("Mod", modeId, _modeIds, _modeLabels,
                "Katalogda bu modId yok — kural hiç eşleşmez.");
            DrawIdField("Harita", sceneName, _mapIds, _mapLabels,
                "Katalogda bu sahne adı yok — kural hiç eşleşmez.");

            EditorGUILayout.PropertyField(trigger, new GUIContent("Tetikleyici"));

            // RoundStart'ta "bitmesine kaç saniye kaldı" diye bir soru yok: eşik yalnız uyarı
            // tetikleyicilerinde okunuyor, orada çizmek yanlış bir ayar izlenimi verirdi.
            if (trigger.enumValueIndex != (int)ModeAudioEvent.RoundStart)
            {
                EditorGUILayout.PropertyField(warningSeconds, new GUIContent("Eşik (sn)"));
            }

            volume.floatValue = EditorGUILayout.Slider("Seviye", volume.floatValue, 0f, 1f);
            EditorGUILayout.PropertyField(clips, new GUIContent("Klipler (rastgele biri)"), true);

            DrawRuleDiagnostics(index, modeId, sceneName, trigger, clips);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);

            return remove;
        }

        /// <summary>
        /// Katalogdan seçtiren açılır liste; katalog yoksa düz metin alanı. Kayıtta duran değer
        /// listede yoksa (eski/bozuk kayıt) seçeneklere geçici olarak eklenir — aksi hâlde popup
        /// onu sessizce ilk seçeneğe düşürür ve kullanıcı ne kaybettiğini göremez.
        /// </summary>
        private static void DrawIdField(string label, SerializedProperty property,
            string[] values, string[] labels, string unknownWarning)
        {
            if (values == null || labels == null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                return;
            }

            string current = property.stringValue;
            int selected = IndexOf(values, current);
            bool unknown = selected < 0;

            string[] shown = labels;
            if (unknown)
            {
                // Bilinmeyen değer listenin SONUNA eklenir: mevcut indekslerin anlamı kaymasın.
                shown = new string[labels.Length + 1];
                labels.CopyTo(shown, 0);
                shown[labels.Length] = current;
                selected = labels.Length;
            }

            int picked = EditorGUILayout.Popup(label, selected, shown);
            if (picked != selected)
            {
                property.stringValue = picked >= 0 && picked < values.Length ? values[picked] : "";
            }

            if (unknown)
            {
                EditorGUILayout.HelpBox(unknownWarning, MessageType.Warning);
            }
        }

        /// <summary>Satır-içi denetimler: sessiz kural, oynanmayan mod/harita eşleşmesi, ölü kopya.</summary>
        private void DrawRuleDiagnostics(int index, SerializedProperty modeId,
            SerializedProperty sceneName, SerializedProperty trigger, SerializedProperty clips)
        {
            if (!HasClip(clips))
            {
                EditorGUILayout.HelpBox(
                    "Klip listesi boş (ya da tüm girdileri boş): bu kural eşleşse bile sessiz kalır.",
                    MessageType.Warning);
            }

            if (_catalog != null &&
                !string.IsNullOrEmpty(modeId.stringValue) &&
                !string.IsNullOrEmpty(sceneName.stringValue))
            {
                MapDefinition map = _catalog.FindMap(sceneName.stringValue);
                if (map != null && !map.SupportsMode(modeId.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        "Bu harita o modda oynanmıyor (MapDefinition.supportedModeIds), " +
                        "kural hiç tetiklenmez.",
                        MessageType.Info);
                }
            }

            if (HasEarlierTwin(index, modeId.stringValue, sceneName.stringValue, trigger.enumValueIndex))
            {
                EditorGUILayout.HelpBox(
                    "Aynı mod/harita/tetikleyici üçlüsü yukarıda da var: eşit spesiflikte listedeki " +
                    "İLK kural kazanır, bu satır ölüdür.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Yeni satırın alanları TEK TEK kurulur: <c>InsertArrayElementAtIndex</c> bir öncekinin
        /// değerlerini kopyalar, kurulmazsa yeni kural eski klipleri taşıyarak sessizce yanlış doğar.
        /// </summary>
        private void AddRule()
        {
            int index = _rules.arraySize;
            _rules.InsertArrayElementAtIndex(index);

            SerializedProperty rule = _rules.GetArrayElementAtIndex(index);
            rule.FindPropertyRelative("modeId").stringValue = "";
            rule.FindPropertyRelative("sceneName").stringValue = "";
            rule.FindPropertyRelative("trigger").enumValueIndex = (int)ModeAudioEvent.RoundStart;
            rule.FindPropertyRelative("clips").arraySize = 0;
            rule.FindPropertyRelative("volume").floatValue = 1f;
            rule.FindPropertyRelative("warningSeconds").floatValue = 5f;
        }

        private bool HasEarlierTwin(int index, string modeId, string sceneName, int trigger)
        {
            for (int i = 0; i < index; i++)
            {
                SerializedProperty other = _rules.GetArrayElementAtIndex(i);
                if (other.FindPropertyRelative("trigger").enumValueIndex == trigger &&
                    Same(other.FindPropertyRelative("modeId").stringValue, modeId) &&
                    Same(other.FindPropertyRelative("sceneName").stringValue, sceneName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasClip(SerializedProperty clips)
        {
            if (clips == null || !clips.isArray)
            {
                return false;
            }

            for (int i = 0; i < clips.arraySize; i++)
            {
                if (clips.GetArrayElementAtIndex(i).objectReferenceValue != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static string TriggerName(SerializedProperty trigger)
        {
            string[] names = trigger.enumDisplayNames;
            int i = trigger.enumValueIndex;
            return i >= 0 && i < names.Length ? names[i] : "?";
        }

        private void BuildOptions()
        {
            if (_catalog == null)
            {
                _modeIds = null;
                _modeLabels = null;
                _mapIds = null;
                _mapLabels = null;
                return;
            }

            var modeIds = new List<string> { "" };
            var modeLabels = new List<string> { ANY_MODE_LABEL };
            ModeDefinition[] modes = _catalog.Modes;
            if (modes != null)
            {
                for (int i = 0; i < modes.Length; i++)
                {
                    // Lobi profilli modlar da listelenir: lobide de duyuru sesi kurulabiliyor.
                    ModeDefinition mode = modes[i];
                    if (mode == null || string.IsNullOrEmpty(mode.ModeId))
                    {
                        continue;
                    }

                    modeIds.Add(mode.ModeId);
                    modeLabels.Add(Label(mode.ModeId, mode.DisplayName));
                }
            }

            var mapIds = new List<string> { "" };
            var mapLabels = new List<string> { ANY_MAP_LABEL };
            MapDefinition[] maps = _catalog.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Length; i++)
                {
                    MapDefinition map = maps[i];
                    if (map == null || string.IsNullOrEmpty(map.SceneName))
                    {
                        continue;
                    }

                    mapIds.Add(map.SceneName);
                    mapLabels.Add(Label(map.SceneName, map.DisplayName));
                }
            }

            _modeIds = modeIds.ToArray();
            _modeLabels = modeLabels.ToArray();
            _mapIds = mapIds.ToArray();
            _mapLabels = mapLabels.ToArray();
        }

        private static string Label(string id, string displayName)
        {
            return string.IsNullOrEmpty(displayName) ? id : $"{id} — {displayName}";
        }

        private static int IndexOf(string[] values, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0; // boş = "kısıt yok", listenin başı
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (Same(values[i], value))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
