using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;
using VortexArena.Core.GripCapture;

namespace VortexArena.Core.Editor
{
    /// <summary><c>Tools &gt; VortexArena &gt; Items &gt; Kavrama Pozu Stüdyosu</c> — the editor side
    /// of grip capture: picks the item, opens the capture scene, and keeps the records afterwards
    /// (mirror, delete, make one-handed).
    /// <para>Flow: pick the item → <b>Yakalama sahnesini aç</b> → Play with the headset (Quest Link)
    /// and capture each grip with the hand holding the item → leaving Play writes the records into
    /// the definition.</para>
    /// <para>⚠️ The grip itself is NOT authored here and no bench comes back: whether the palm
    /// touches the grip and the index finger reaches the trigger is only answerable with a tracked
    /// hand, and a mouse-posed ghost hand answered it wrong.</para>
    /// <para>⚠️ The target lives in EditorPrefs (<see cref="GripCaptureSettings"/>), shared with the
    /// capture scene's Inspector — two selections would raise "which item is being captured".</para>
    /// <para>⚠️ No dialogs in the write paths (same reason as <c>WeaponKitBuilder</c>): a modal
    /// blocks Unity's main thread and times out under CLI. Results go to <see cref="Debug.Log"/>;
    /// the only confirm is dropping a pending capture, which is destructive.</para>
    /// </summary>
    internal sealed class GripPoseStudio : EditorWindow
    {
        /// <summary>How long the on-disk buffer snapshot shown in the GUI stays valid (s) —
        /// re-reading the file on every repaint (10/s in Play) would be pure waste.</summary>
        private const double BufferRefreshSeconds = 0.5d;

        private GripCaptureBuffer _buffer;
        private double _bufferTime;

        // Lives under Items, not Weapons: any item's grip is captured here.
        [MenuItem("Tools/VortexArena/Items/Kavrama Pozu Stüdyosu", false, 20)]
        internal static void Open()
        {
            GripPoseStudio window = GetWindow<GripPoseStudio>();
            window.titleContent = new GUIContent("Kavrama Stüdyosu");
            window.minSize = new Vector2(340f, 300f);
            window.Show();
        }

        /// <summary>Auto-fills the target from the Project window: a definition asset, or an item
        /// prefab that resolves to exactly one definition.</summary>
        private void OnSelectionChange()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            if (Selection.activeObject is ItemDefinition selected)
            {
                GripCaptureSettings.SaveTarget(selected);
            }
            else if (Selection.activeGameObject != null)
            {
                ItemDefinition resolved = GripRecordOps.ResolveDefinition(
                    Selection.activeGameObject, out bool ambiguous);
                if (resolved != null && !ambiguous)
                {
                    GripCaptureSettings.SaveTarget(resolved);
                }
            }

            Repaint();
        }

        /// <summary>Keeps the Play-mode capture list live (the buffer grows on disk, not in the
        /// editor's object graph, so nothing else repaints the window).</summary>
        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        // ------------------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kavrama Stüdyosu", EditorStyles.boldLabel);

            ItemDefinition target = GripCaptureSettings.LoadTarget();
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

            if (EditorApplication.isPlaying)
            {
                DrawPlayGui();
                return;
            }

            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "Hedef yok: proje penceresinden bir eşya tanımı (ya da tanımı çözülen bir eşya " +
                    "prefabı) seç, veya yukarıdaki alana sürükle.",
                    MessageType.Info);
            }
            else
            {
                DrawRecordsGui(target);
            }

            DrawPendingGui(target);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(GripSlotGui.UsageText, MessageType.None);
        }

        private void DrawRecordsGui(ItemDefinition target)
        {
            if (target.Prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Bu eşya tanımının Prefab alanı boş — yakalama sahnesinde gösterilecek eşya yok.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            GripSlotGui.DrawSlots(target, (kind, rightHand) =>
                GripRecordOps.ClearSlot(target, kind, rightHand));

            EditorGUILayout.Space();
            for (int k = 0; k < GripSlotGui.Kinds.Length; k++)
            {
                GripSocketKind kind = GripSlotGui.Kinds[k];
                EditorGUILayout.LabelField(GripSlotGui.KindLabel(kind), EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!target.HasGrip(kind, true)))
                    {
                        if (GUILayout.Button("Sağ → Sol aynala"))
                        {
                            GripRecordOps.Mirror(target, kind, true);
                        }
                    }

                    using (new EditorGUI.DisabledScope(!target.HasGrip(kind, false)))
                    {
                        if (GUILayout.Button("Sol → Sağ aynala"))
                        {
                            GripRecordOps.Mirror(target, kind, false);
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("2. kavramayı sil (tek kavramalı yap)"))
            {
                GripRecordOps.MakeOneHanded(target);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Yakalama sahnesini aç", GUILayout.Height(26f)))
            {
                OpenCaptureScene();
            }
        }

        /// <summary>In Play the window only reports: the records are written when Play exits.</summary>
        private void DrawPlayGui()
        {
            EditorGUILayout.HelpBox("Yakalama sürüyor — Play'den çıkınca eşyaya yazılır.",
                MessageType.Info);

            GripCaptureBuffer buffer = CachedBuffer();
            if (buffer == null || buffer.entries.Count == 0)
            {
                EditorGUILayout.LabelField("Henüz kayıt yok.");
                return;
            }

            for (int i = 0; i < buffer.entries.Count; i++)
            {
                GripCaptureEntry entry = buffer.entries[i];
                int joints = entry.pose.HasFingers ? entry.pose.fingerJoints.Length : 0;
                EditorGUILayout.LabelField(GripSlotGui.SlotLabel(entry.kind, entry.rightHand),
                    $"✔ {joints} eklem");
            }
        }

        /// <summary>A buffer left on disk (crash, or an item that could not be resolved) — applied
        /// or dropped BY HAND, never silently (it may be days old).</summary>
        private void DrawPendingGui(ItemDefinition target)
        {
            if (!GripCaptureBuffer.Exists)
            {
                return;
            }

            GripCaptureBuffer buffer = CachedBuffer();
            if (buffer == null)
            {
                return;
            }

            string itemName = ItemNameOf(buffer, target);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Uygulanmamış yakalama: {itemName} · {buffer.entries.Count} kayıt",
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Uygula"))
                {
                    GripCaptureApplier.Apply();
                    _bufferTime = 0d;
                }

                if (GUILayout.Button("Sil") &&
                    EditorUtility.DisplayDialog("Yakalamayı sil",
                        $"'{itemName}' için uygulanmamış {buffer.entries.Count} kayıt silinecek. " +
                        "Geri alınamaz.", "Sil", "Vazgeç"))
                {
                    GripCaptureBuffer.Delete();
                    _bufferTime = 0d;
                }
            }
        }

        private static string ItemNameOf(GripCaptureBuffer buffer, ItemDefinition target)
        {
            string path = AssetDatabase.GUIDToAssetPath(buffer.definitionGuid ?? "");
            var def = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def != null)
            {
                return def.name;
            }

            return target != null ? target.name : "(eşya bulunamadı)";
        }

        private GripCaptureBuffer CachedBuffer()
        {
            if (EditorApplication.timeSinceStartup - _bufferTime > BufferRefreshSeconds)
            {
                _buffer = GripCaptureBuffer.Load();
                _bufferTime = EditorApplication.timeSinceStartup;
            }

            return _buffer;
        }

        // ------------------------------------------------------------------- capture scene

        /// <summary>Opens the capture scene, creating it on first use, and selects its root so the
        /// Inspector shows the item picker.
        /// <para>⚠️ The scene is NOT a committed fixture built by hand: it holds a single root with
        /// the session component, and everything else (the item, the hands) is spawned at runtime —
        /// a hand-built scene would be a second description of the setup.</para></summary>
        internal static void OpenCaptureScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning($"{GripRecordOps.LOG} Play kipinde sahne açılmaz.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GripCaptureSettings.ScenePath) == null)
            {
                CreateCaptureScene();
            }
            else
            {
                EditorSceneManager.OpenScene(GripCaptureSettings.ScenePath);
            }

            var session = Object.FindFirstObjectByType<GripCaptureSession>();
            if (session != null)
            {
                Selection.activeGameObject = session.gameObject;
            }
            else
            {
                Debug.LogWarning($"{GripRecordOps.LOG} Yakalama sahnesinde oturum kökü yok — " +
                                 "sahneyi silip yeniden açtırabilirsin.");
            }
        }

        private static void CreateCaptureScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var root = new GameObject("[Kavrama Yakalama]");
            root.AddComponent<GripCaptureSession>();

            EditorSceneManager.SaveScene(scene, GripCaptureSettings.ScenePath);
            Debug.Log($"{GripRecordOps.LOG} Yakalama sahnesi oluşturuldu: " +
                      GripCaptureSettings.ScenePath);
        }
    }
}
