using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Audio;

namespace VortexArena.Core.Editor
{
    /// <summary>Inspector for <see cref="ModeAudioRegistry"/>: picks mode/map from the catalog
    /// instead of typed ids and flags rules that would stay silent.</summary>
    /// <remarks>
    /// ⚠️ No default drawing: mode and map are free strings, so a typo compiles fine and the rule
    /// merely never matches. The catalog popup makes that impossible; without a catalog the fields
    /// fall back to plain text so the asset stays editable.
    /// <para>The catalog is loaded via <c>Resources.Load</c>, the same path as at runtime — no
    /// second reference and no second source of truth.</para>
    /// <para>No audio preview button: the only way to play a clip in the editor is reflection into
    /// internal <c>UnityEditor.AudioUtil</c>, which breaks silently on a version bump.</para>
    /// </remarks>
    [CustomEditor(typeof(ModeAudioRegistry))]
    internal sealed class ModeAudioRegistryEditor : UnityEditor.Editor
    {
        private const string CATALOG_RESOURCE = "GameCatalog";

        /// <summary>Empty selection = "no restriction"; index 0 of both lists.</summary>
        private const string ANY_MODE_LABEL = "(her mod)";
        private const string ANY_MAP_LABEL = "(her harita)";

        /// <summary>Popup labels of <see cref="ModeAudioGameType"/>, in enum value order.</summary>
        private static readonly string[] GAME_TYPE_LABELS =
        {
            "(her tip)", "Hızlı Savaş", "Çocuk Oyunları"
        };

        private GameCatalog _catalog;

        // Values (element 0 always the empty string) and their popup labels.
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
                "Eşleşen kurallar arasından en spesifik olan kazanır (mod 4 puan, harita 2, " +
                "oyun tipi 1); eşitlikte listedeki İLK kural çalar. Boş mod = her mod, boş harita = " +
                "her harita, her tip = kısıt yok.",
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

        /// <summary>Draws one rule box; <c>true</c> when the delete button was pressed.</summary>
        private bool DrawRule(int index)
        {
            SerializedProperty rule = _rules.GetArrayElementAtIndex(index);
            SerializedProperty modeId = rule.FindPropertyRelative("modeId");
            SerializedProperty sceneName = rule.FindPropertyRelative("sceneName");
            SerializedProperty gameType = rule.FindPropertyRelative("gameType");
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
            DrawGameTypeField(gameType);

            EditorGUILayout.PropertyField(trigger, new GUIContent("Tetikleyici"));

            // The threshold is read only by the two warning triggers; drawing it elsewhere would
            // suggest a setting that does nothing.
            if (IsWarningTrigger(trigger.enumValueIndex))
            {
                EditorGUILayout.PropertyField(warningSeconds, new GUIContent("Eşik (sn)"));
            }

            volume.floatValue = EditorGUILayout.Slider("Seviye", volume.floatValue, 0f, 1f);
            EditorGUILayout.PropertyField(clips, new GUIContent(ClipsLabel(trigger)), true);

            DrawRuleDiagnostics(index, modeId, sceneName, gameType, trigger, clips);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);

            return remove;
        }

        /// <summary>Game type popup with Turkish labels.</summary>
        /// <remarks>A plain enum field would show the C# names; the label order follows
        /// <see cref="ModeAudioGameType"/> value by value.</remarks>
        private static void DrawGameTypeField(SerializedProperty gameType)
        {
            if (gameType == null)
            {
                return;
            }

            int index = Mathf.Clamp(gameType.enumValueIndex, 0, GAME_TYPE_LABELS.Length - 1);
            int picked = EditorGUILayout.Popup("Oyun tipi", index, GAME_TYPE_LABELS);
            if (picked != gameType.enumValueIndex)
            {
                gameType.enumValueIndex = picked;
            }
        }

        /// <summary>Only the two warning triggers read <c>warningSeconds</c>.</summary>
        private static bool IsWarningTrigger(int trigger)
        {
            return trigger == (int)ModeAudioEvent.RoundEndWarning ||
                   trigger == (int)ModeAudioEvent.MatchEndWarning;
        }

        /// <summary>Clip list label — the countdown list is indexed by second, not random.</summary>
        private static string ClipsLabel(SerializedProperty trigger)
        {
            return trigger.enumValueIndex == (int)ModeAudioEvent.Countdown
                ? "Klipler (saniyeye göre: [0]=1 sn, [1]=2 sn …)"
                : "Klipler (rastgele biri)";
        }

        /// <summary>Catalog popup, or a plain text field without a catalog.</summary>
        /// <remarks>A stored value missing from the list (stale/broken rule) is appended as a
        /// temporary option; otherwise the popup would silently snap it to the first option and
        /// hide what was lost.</remarks>
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
                // Unknown value goes to the END so existing indices keep their meaning.
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

        /// <summary>Inline checks: silent rule, unplayable mode/map pair, dead duplicate.</summary>
        private void DrawRuleDiagnostics(int index, SerializedProperty modeId,
            SerializedProperty sceneName, SerializedProperty gameType, SerializedProperty trigger,
            SerializedProperty clips)
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

            DrawGameTypeConflicts(modeId, sceneName, gameType);

            if (HasEarlierTwin(index, modeId.stringValue, sceneName.stringValue,
                    gameType.enumValueIndex, trigger.enumValueIndex))
            {
                EditorGUILayout.HelpBox(
                    "Aynı mod/harita/oyun tipi/tetikleyici dörtlüsü yukarıda da var: eşit " +
                    "spesiflikte listedeki İLK kural kazanır, bu satır ölüdür.",
                    MessageType.Warning);
            }
        }

        /// <summary>Warns when the selected mode/map belongs to a different game type than the
        /// filter — the two narrowings cancel out and the rule can never match.</summary>
        private void DrawGameTypeConflicts(SerializedProperty modeId, SerializedProperty sceneName,
            SerializedProperty gameType)
        {
            if (_catalog == null || gameType.enumValueIndex == (int)ModeAudioGameType.Any)
            {
                return;
            }

            GameType filter = gameType.enumValueIndex == (int)ModeAudioGameType.Kids
                ? GameType.Kids
                : GameType.QuickBattle;

            if (!string.IsNullOrEmpty(modeId.stringValue))
            {
                ModeDefinition mode = _catalog.FindMode(modeId.stringValue);
                if (mode != null && mode.GameType != filter)
                {
                    EditorGUILayout.HelpBox(
                        "Bu mod o oyun tipinde değil, kural hiç tetiklenmez.",
                        MessageType.Info);
                }
            }

            if (!string.IsNullOrEmpty(sceneName.stringValue))
            {
                MapDefinition map = _catalog.FindMap(sceneName.stringValue);
                if (map != null && map.GameType != filter)
                {
                    EditorGUILayout.HelpBox(
                        "Bu harita o oyun tipinde değil, kural hiç tetiklenmez.",
                        MessageType.Info);
                }
            }
        }

        /// <summary>Initializes every field of the new row.</summary>
        /// <remarks><c>InsertArrayElementAtIndex</c> copies the previous element, so without this
        /// the new rule would silently be born carrying the old clips.</remarks>
        private void AddRule()
        {
            int index = _rules.arraySize;
            _rules.InsertArrayElementAtIndex(index);

            SerializedProperty rule = _rules.GetArrayElementAtIndex(index);
            rule.FindPropertyRelative("modeId").stringValue = "";
            rule.FindPropertyRelative("sceneName").stringValue = "";
            rule.FindPropertyRelative("gameType").enumValueIndex = (int)ModeAudioGameType.Any;
            rule.FindPropertyRelative("trigger").enumValueIndex = (int)ModeAudioEvent.RoundStart;
            rule.FindPropertyRelative("clips").arraySize = 0;
            rule.FindPropertyRelative("volume").floatValue = 1f;
            rule.FindPropertyRelative("warningSeconds").floatValue = 5f;
        }

        private bool HasEarlierTwin(int index, string modeId, string sceneName, int gameType,
            int trigger)
        {
            for (int i = 0; i < index; i++)
            {
                SerializedProperty other = _rules.GetArrayElementAtIndex(i);
                if (other.FindPropertyRelative("trigger").enumValueIndex == trigger &&
                    other.FindPropertyRelative("gameType").enumValueIndex == gameType &&
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
                    // Lobby-profile modes are listed too: the lobby can have announcements.
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
                return 0; // empty = "no restriction", head of the list
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
