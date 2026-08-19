using System;
using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary><c>Tools &gt; VortexArena &gt; Weapons &gt; Kavrama Pozu Stüdyosu</c> — a bench for
    /// authoring how a weapon sits in the hand WITHOUT putting on a headset. It exists for feedback
    /// time: a grip is not a number with one right value but the answer to "does the palm touch the
    /// grip, does the index finger reach the trigger", and asking that via an APK build took minutes
    /// per attempt.
    /// <para>Flow: open a <c>WPN_*</c> prefab in PREFAB MODE → the window picks up the stage →
    /// <b>Elleri Oluştur</b> → controller roots appear (controller model + ghost hand beneath) → MOVE
    /// the roots onto the grips, then RIG the fingers per weapon (pick a joint in the window's finger
    /// list, rotate it in Scene View) → optionally <b>Karşı Ele Aynala</b> → <b>Kaydet</b>.</para>
    /// <para>⚠️ There are no finger PRESETS: the pose is authored bone by bone for THIS weapon,
    /// because a shared "trigger/wrap" table cannot fit grips whose geometry differs — on some
    /// weapons it left the fingers inside the body. The record holds the joints
    /// (<see cref="HandJointRotation"/>); the only shared pose left is the idle hand.</para>
    /// <para>⚠️ The root the user drags is the CONTROLLER (anchor) frame: <c>[VA El_*]</c> represents
    /// where <c>OVRCameraRig.left/rightHandAnchor</c> sits on the weapon, and the record is that
    /// root's POSITION relative to the item (<see cref="ItemGripPose"/>: anchor space, no rotation).
    /// In game the weapon is ALWAYS aligned with the controller, so the roots are kept aligned
    /// (<see cref="KeepRootsAligned"/>) and only MOVED.</para>
    /// <para>Two LOCKED visual children hang under the root: the Quest 3 controller model (identity
    /// pose — exactly how it sits under the anchor in game, so it is the real alignment reference)
    /// and the ISDK ghost hand (at the anchor→wrist pose: the measured
    /// <see cref="HandGripConvention.AnchorToWrist"/> if present, otherwise estimated from the
    /// ghost's own skeleton — <see cref="ResolveGhostOffset"/>). ⚠️ The ghost never enters the
    /// record; an estimate only shifts the visual slightly.</para>
    /// <para>⚠️ If the record carried a rotation, anyone rotating the root would skew the weapon off
    /// the controller in game. The front grip carries none either: the second hand's controller is
    /// taken as weapon-aligned and the synthetic wrist locks its delta beyond it.</para>
    /// <para>⚠️ Hands never go INSIDE the prefab: each is a separate ROOT object of the prefab stage
    /// scene (<see cref="HideFlags.DontSave"/>), because prefab mode writes only the tree under
    /// <c>prefabContentsRoot</c>. Parented under the prefab, the first save would embed a hand model
    /// in the weapon and it would float in the arena.</para>
    /// <para>⚠️ NOTHING is written into the prefab contents (pose node, hand rig, marker): the record
    /// lives only in <c>WD_*.asset</c>. A second description in the prefab would raise "which one
    /// applies" for everyone who opens it.</para>
    /// <para>⚠️ No dialogs (same reason as <c>WeaponKitBuilder</c>): a modal blocks Unity's main
    /// thread and times out under CLI/pipeline. Results go to <see cref="Debug.Log"/>.</para>
    /// </summary>
    internal sealed class GripPoseStudio : EditorWindow
    {
        internal const string LOG = "[GripPoseStudio]";

        /// <summary>Name prefix of the hand roots. ⚠️ The name is a KEY: hands live in the SCENE,
        /// not the window (a domain reload clears both window and fields), and are found again by
        /// name. The brackets keep them apart from the user's own objects.</summary>
        internal const string HAND_ROOT_PREFIX = "[VA El_";

        /// <summary>ISDK hand model provider for the <b>OpenXR</b> skeleton.
        /// <para>⚠️ The path is hardcoded and ISDK's <c>HandGhostProviderUtils</c> is NOT used: that
        /// class lives in the <c>Oculus.Interaction.Editor</c> asmdef, and referencing it would bind
        /// this tool to ISDK's editor assembly — a link that breaks on package upgrades and the core
        /// never needs.</para></summary>
        private const string GHOST_PROVIDER_PATH_OPENXR =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/OpenXRGhostProvider.asset";

        /// <summary>Same provider for the <b>OVR</b> skeleton.</summary>
        private const string GHOST_PROVIDER_PATH_OVR =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/GhostProvider.asset";

        /// <summary>Source of the CONTROLLER MODEL placed under the root: Meta core SDK's controller
        /// prefab and its Quest 3 (Touch Plus) models. In game it sits at identity under the anchor
        /// (<c>OVRControllerHelper</c> applies no offset), so the bench places it at identity too —
        /// what you see is exactly the tracked controller, and the weapon aligns to it.</summary>
        private const string CONTROLLER_PREFAB_PATH = "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";
        private const string CONTROLLER_MODEL_RIGHT = "MetaQuestTouchPlus_Right";
        private const string CONTROLLER_MODEL_LEFT = "MetaQuestTouchPlus_Left";

        /// <summary>Names of the two visual children under the root (identity, for re-finding).</summary>
        private const string GHOST_NAME = "Hand";
        private const string CONTROLLER_NAME = "Controller";

        /// <summary>Warn only once when the controller model is missing.</summary>
        private static bool _controllerModelWarned;

        /// <summary>Name keys for the grip node (case-insensitive, tried IN ORDER). ⚠️ Order matters:
        /// the more SPECIFIC key comes first, or a broad one (<c>guard</c>) grabs the wrong part —
        /// which is why <c>guard</c> is last in the front-grip list.</summary>
        private static readonly string[] GRIP_KEYS = { "pistolgrip", "grip", "handle" };

        /// <summary>Name keys for the front-grip node (the forward hand in a two-handed hold).</summary>
        private static readonly string[] FOREGRIP_KEYS =
            { "handguard", "barrelguard", "foregrip", "forend", "guard" };

        /// <summary>Finger names in <c>HandFinger</c> order — the order
        /// <c>FingersMetadata.FINGER_TO_JOINTS</c> is indexed by.</summary>
        private static readonly string[] FINGER_LABELS =
            { "Başparmak", "İşaret", "Orta", "Yüzük", "Serçe" };

        /// <summary>Target picked while no stage is open (only used by "open prefab").</summary>
        [SerializeField] private GameObject _prefab;

        /// <summary>Were there LIVE hands last frame — for the "hands disappeared" notice.
        /// ⚠️ Not serialized: it only describes a frame-to-frame delta, and persisting it would warn
        /// on every startup after a domain reload (hands are <c>DontSave</c> and die there).</summary>
        [NonSerialized] private bool _handsSeen;

        /// <summary>Hands vanished for a reason outside the window — the notice stays until they are
        /// rebuilt.</summary>
        [NonSerialized] private bool _handsLost;

        private static HandGhostProvider _ghostProvider;

        /// <summary>Path of the loaded provider — shown in the diagnostic line.</summary>
        private static string _ghostProviderPath;

        /// <summary>Warn only once when no provider is found. ⚠️ This path can run from
        /// <c>OnGUI</c> every frame, so without the flag a missing asset would flood the
        /// console.</summary>
        private static bool _ghostProviderWarned;

        // ---------------------------------------------------------------------------- window

        // The ONLY item under the Weapons menu: the weapon kit and net item catalog run inside
        // Configure All Build Elements' sync. The studio stays in the menu because a grip is
        // authored by eye.
        [MenuItem("Tools/VortexArena/Weapons/Kavrama Pozu Stüdyosu", false, 20)]
        private static void Open()
        {
            GripPoseStudio window = GetWindow<GripPoseStudio>();
            window.titleContent = new GUIContent("Kavrama Tezgâhı");
            window.minSize = new Vector2(340f, 360f);
            window.Show();
        }

        /// <summary>Cleanup hooks are installed independently of the window.
        /// <para>⚠️ They cannot hang off the window's <c>OnEnable</c>: the user can close the window
        /// and keep working in prefab mode, leaving the hands behind. They are
        /// <see cref="HideFlags.DontSave"/> so they never hit disk, but they would sit in the middle
        /// of a running game as two hand models.</para></summary>
        [InitializeOnLoadMethod]
        private static void InstallCleanupHooks()
        {
            PrefabStage.prefabStageClosing += stage => DestroyHands(stage.scene);
            // Clicking the ghost mesh in Scene View selects the CHILD, but the thing to drag is the
            // controller root. Selection is redirected so the handles appear on the root — moving a
            // child does NOT change the record and would produce "I saved but nothing changed".
            Selection.selectionChanged += RedirectSelectionToHandRoot;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode)
                {
                    DestroyAllHands();
                }
            };
            // ⚠️ While SAVING prefab mode (Auto Save included) Unity flips EVERY extra root in the
            // preview scene to HideAndDontSave, whatever its starting flags. HideInHierarchy drops
            // the hand out of the hierarchy while it keeps living and rendering, and NotEditable
            // locks it on top. The flip runs at several points in the save flow and delayCall does
            // not fire while the editor is unfocused, so a one-shot event hook is not enough: the
            // guard lives in update and restores the hands every tick (writing only when needed).
            EditorApplication.update += RestoreHandFlags;
            // ⚠️ Cleanup applies ONLY to the opened scene (not DestroyAllHands): a sweep that also
            // hit the open stage would delete the hands ourselves while its contents reload. The
            // open stage's own scene is cleaned in prefabStageClosing.
            EditorSceneManager.sceneOpened += (scene, mode) =>
            {
                // ⚠️ Do not trust the stage reference to exclude the preview scene (its scene field
                // can be stale for a moment while contents reload), nor assume its path is empty:
                // a preview scene's path is the PREFAB'S ASSET PATH. A genuinely opened scene is
                // always a .unity file — that is the distinguishing property.
                PrefabStage stage = CurrentStage();
                bool previewScene = string.IsNullOrEmpty(scene.path) ||
                                    scene.path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                    (stage != null && stage.scene == scene);
                if (previewScene)
                {
                    return;
                }

                DestroyHands(scene);
            };
        }

        /// <summary>Moves a selection that landed on a hand's CHILD (ghost mesh, controller model) up
        /// to the controller root — the only transform whose POSE gets recorded (rationale in
        /// <see cref="InstallCleanupHooks"/>).
        /// <para>⚠️ Drivable finger joints are the ONE exception and the selection stays on them: the
        /// finger rig is authored by rotating exactly those bones, and their rotations are read back
        /// at save time (<see cref="CaptureFingers"/>). Everything else under the root is still
        /// bounced, so "dragged thing == recorded thing" holds for the placement too.</para></summary>
        private static void RedirectSelectionToHandRoot()
        {
            GameObject active = Selection.activeGameObject;
            if (active == null || active.GetComponent<GripHandAuthoring>() != null)
            {
                return;
            }

            var owner = active.GetComponentInParent<GripHandAuthoring>();
            if (owner == null || IsDrivableJoint(owner, active.transform))
            {
                return;
            }

            Selection.activeGameObject = owner.gameObject;
        }

        /// <summary>Is this transform one of the hand's riggable finger joints.</summary>
        private static bool IsDrivableJoint(GripHandAuthoring hand, Transform candidate)
        {
            List<HandJointMap> joints = hand.DrivableJoints();
            for (int i = 0; i < joints.Count; i++)
            {
                if (joints[i].transform == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Restores hands that Unity's prefab save hid (rationale in
        /// <see cref="InstallCleanupHooks"/>) and keeps the roots weapon-aligned
        /// (<see cref="KeepRootsAligned"/>). Runs every editor tick, so it must stay cheap: it never
        /// writes to an intact hand and only repaints the hierarchy when it actually fixed
        /// something.</summary>
        private static void RestoreHandFlags()
        {
            PrefabStage stage = CurrentStage();
            if (stage == null)
            {
                return;
            }

            List<GripHandAuthoring> hands = FindHands(stage.scene);
            Transform weaponRoot = StageWeaponRoot(stage);
            if (weaponRoot != null)
            {
                KeepRootsAligned(weaponRoot, hands);
            }

            bool restored = false;
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] == null)
                {
                    continue;
                }

                GameObject go = hands[i].gameObject;
                if ((go.hideFlags & (HideFlags.HideInHierarchy | HideFlags.NotEditable)) == 0)
                {
                    continue;
                }

                MarkDontSave(hands[i]);
                HideIsdkComponents(GhostOf(hands[i]));
                // If the flip reached component flags too, keep the two components the user needs
                // (Transform + the authoring component) unlocked and visible in the Inspector.
                hands[i].hideFlags &= ~(HideFlags.HideInInspector | HideFlags.NotEditable);
                hands[i].transform.hideFlags &= ~(HideFlags.HideInInspector | HideFlags.NotEditable);
                restored = true;
            }

            if (restored)
            {
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        /// <summary>Auto-fills the target when a <c>WPN_*</c> prefab is selected in the Project
        /// window (for the button that opens prefab mode).</summary>
        private void OnSelectionChange()
        {
            GameObject candidate = Selection.activeGameObject;
            if (candidate != null &&
                PrefabUtility.IsPartOfPrefabAsset(candidate) &&
                candidate.GetComponent<Weapon>() != null)
            {
                _prefab = candidate;
            }

            Repaint();
        }

        // ------------------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kavrama Tezgâhı", EditorStyles.boldLabel);

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Play kipinde kavrama yazılmaz — kayıt diske AssetDatabase ile iniyor ve Play " +
                    "oturumunda yazılan her değer bir sonraki domain reload'da belirsizleşir.",
                    MessageType.Info);
                return;
            }

            PrefabStage stage = CurrentStage();
            Transform weaponRoot = StageWeaponRoot(stage);

            if (weaponRoot == null)
            {
                DrawNoStageGui(stage);
                return;
            }

            DrawStageGui(stage, weaponRoot);
        }

        /// <summary>With prefab mode closed there is one job: open the target in prefab mode.
        /// <para>⚠️ The old scene-based bench does not come back: hand placement is measured against
        /// the weapon, and prefab mode already provides that reference (the prefab root) at the
        /// weapon's own scale. A second weapon copy in a scene would blur which instance the record
        /// was taken from.</para></summary>
        private void DrawNoStageGui(PrefabStage stage)
        {
            _prefab = (GameObject)EditorGUILayout.ObjectField(
                "Silah prefabı", _prefab, typeof(GameObject), false);

            bool usable = _prefab != null && ResolveDefinition(_prefab) != null;

            using (new EditorGUI.DisabledScope(!usable))
            {
                if (GUILayout.Button("Prefabı Prefab Kipinde Aç", GUILayout.Height(26f)))
                {
                    AssetDatabase.OpenAsset(_prefab);
                }
            }

            if (stage != null)
            {
                EditorGUILayout.HelpBox(
                    "Açık prefab kipinde Weapon bileşeni yok — bu bir silah prefabı değil.",
                    MessageType.Warning);
            }
            else if (_prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Hedef yok: proje penceresinden bir WPN_* prefabı seç (ya da yukarıdaki alana " +
                    "sürükle), sonra prefab kipinde aç.",
                    MessageType.Info);
            }
            else if (!usable)
            {
                EditorGUILayout.HelpBox(
                    "Bu prefabın Weapon bileşeni ya da tanımı (WeaponDefinition) yok — " +
                    "kaydedilecek asset bulunamaz.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Akış: prefabı prefab kipinde aç → Elleri Oluştur → kumanda köklerini kabzalara oturt " +
                "(hayalet el köke bağlı çizilir) → parmakları bu silaha göre rigle (eklemi listeden " +
                "seç, Scene View'da çevir) → (istersen) Aynala → Kaydet.",
                MessageType.None);

            DrawGhostSourceSection();
        }

        private void DrawStageGui(PrefabStage stage, Transform weaponRoot)
        {
            WeaponDefinition definition = ResolveDefinition(weaponRoot.gameObject);

            EditorGUILayout.LabelField("Hedef", weaponRoot.name);

            if (definition == null)
            {
                EditorGUILayout.HelpBox(
                    "Prefabın Weapon bileşeninde tanım (WeaponDefinition) yok — kavrama alanları " +
                    "yazılamaz.",
                    MessageType.Warning);
                return;
            }

            List<GripHandAuthoring> hands = FindHands(stage.scene);
            int live = CountLive(hands);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ana Kabza Ellerini Oluştur", GUILayout.Height(24f)))
                {
                    CreateHandPair(stage, weaponRoot, definition, GripSocketKind.Primary);
                }

                using (new EditorGUI.DisabledScope(!definition.IsTwoHanded))
                {
                    if (GUILayout.Button("Ön Kabza Ellerini Oluştur", GUILayout.Height(24f)))
                    {
                        CreateHandPair(stage, weaponRoot, definition, GripSocketKind.Secondary);
                    }
                }
            }

            DrawHandList(hands);

            // ⚠️ State updates ONLY on the Layout event: OnGUI runs at least twice per frame
            // (Layout + Repaint) and drawing a DIFFERENT number of controls in the two passes breaks
            // Unity's layout matching. A stable flag draws the box identically in both.
            if (Event.current.type == EventType.Layout)
            {
                if (live > 0)
                {
                    _handsSeen = true;
                    _handsLost = false;
                }
                else if (_handsSeen)
                {
                    _handsSeen = false;
                    _handsLost = true;
                }
            }

            if (_handsLost)
            {
                // Last-resort notice: hands were on the bench and vanished for a reason outside the
                // window (a save that reloads the stage contents). Staying silent would read as
                // "the tool lost my hands"; the fix is one button and an authored grip restores the
                // hand to the same place.
                EditorGUILayout.HelpBox(
                    "Eller tezgâhtan kalktı (prefab kipi içeriği yeniden yüklenmiş olabilir). " +
                    "Yeniden oluştur: kavraması yazılmış bir silahta eller kayıttan aynı yere gelir.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(live == 0))
                {
                    if (GUILayout.Button("Kaydet", GUILayout.Height(26f)))
                    {
                        SaveHands(weaponRoot, definition, hands);
                    }

                    if (GUILayout.Button("Elleri Temizle", GUILayout.Height(26f)))
                    {
                        DestroyHands(stage.scene);
                        Debug.Log($"{LOG} Eller silindi (kaydedilmemiş değişiklikler atıldı).");
                    }
                }
            }

            EditorGUILayout.Space();
            DrawLiveValues(weaponRoot, definition, hands);

            DrawGhostSourceSection();

            EditorGUILayout.HelpBox(
                "Yazan tek düğme Kaydet'tir; kayıttan hemen sonra silah kitini de kendisi eşitler " +
                "(Configure All Build Elements'a gitmene gerek yok). Kit prefabı yeniden yazdığı " +
                "için tezgâhtaki eller kalkabilir — kayıt diskte, Elleri Oluştur onları aynı yere " +
                "geri getirir. Sürüklediğin kök KUMANDADIR — altındaki kumanda modeli " +
                "oyunda izlenen kumandanın ta kendisidir. Silah oyunda HER ZAMAN kumandayla hizalıdır: " +
                "kökü yalnız TAŞI (döndürmek bir şey değiştirmez, kök silahla hizalı tutulur), kumandayı " +
                "kabzada gerçekte durduğu yere koy. Ön kabzada el silaha yapışır, silah ikinci ele göre " +
                "dönmez. Hayalet elin gövdesi ve kumanda modeli kilitlidir (taşınmaz) — konum kökten " +
                "okunur. PARMAKLAR bu silaha özel riglenir: eli seç, listeden eklemi seç, Scene View'da " +
                "çevir; Kaydet parmakları kemiklerden okur.",
                MessageType.None);

            if (AnyGhostEstimated(hands))
            {
                EditorGUILayout.HelpBox(
                    "Hayalet el TAHMİNLE çizildi (anchor→bilek sabiti ölçülmemiş: " +
                    "HandGripConvention.*AnchorToWrist = kimlik). Elin kumandaya göre duruşu yaklaşıktır, " +
                    "kumanda modeli ise kesindir — yerleşimi ona göre yap. Kayıt bundan etkilenmez. Tam el " +
                    "için HandGripPoser'ın başlıkta bastığı iki satırı (editör Play'i ya da APK'da " +
                    "adb logcat -s Unity) sabite yapıştır.",
                    MessageType.Info);
            }
        }

        /// <summary>Is any live hand using an estimated (unmeasured) ghost offset.</summary>
        private static bool AnyGhostEstimated(List<GripHandAuthoring> hands)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null && !hands[i].GhostOffsetMeasured)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawHandList(List<GripHandAuthoring> hands)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tezgâhtaki eller", EditorStyles.boldLabel);

            if (CountLive(hands) == 0)
            {
                EditorGUILayout.LabelField("(yok — yukarıdan oluştur)", EditorStyles.miniLabel);
                return;
            }

            // Which hand's finger rig to expand: the one the current selection belongs to (the root
            // itself or one of its bones). ⚠️ The picker is drawn for ONE hand only — four hands ×
            // five fingers would push the save button off the window.
            GameObject active = Selection.activeGameObject;
            GripHandAuthoring focused = active != null
                ? active.GetComponentInParent<GripHandAuthoring>()
                : null;

            for (int i = 0; i < hands.Count; i++)
            {
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")}",
                        GUILayout.Width(140f));

                    if (GUILayout.Button("Seç"))
                    {
                        Selection.activeGameObject = hand.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                }

                if (hand == focused)
                {
                    DrawFingerRig(hand);
                }
            }
        }

        /// <summary>Finger rig picker for ONE hand: a button per riggable joint that selects the bone
        /// and switches the Scene View to the rotate tool.
        /// <para>⚠️ It lives in the WINDOW, not the hand's Inspector: selecting a joint replaces the
        /// Inspector with that bone's Transform, so an Inspector-side picker would close itself on
        /// the first click and there would be no way to reach the second joint.</para>
        /// <para>⚠️ It only SELECTS — no numeric field, no slider. The pose lives in the bones and is
        /// read back from them at save time (<see cref="CaptureFingers"/>); a second numeric
        /// description of the same rotation would raise "which one is current".</para></summary>
        private static void DrawFingerRig(GripHandAuthoring hand)
        {
            List<HandJointMap> drivable = hand.DrivableJoints();

            using (new EditorGUI.IndentLevelScope())
            {
                if (drivable.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        "(hayalet elin eklemleri okunamadı — eli yeniden oluştur)",
                        EditorStyles.miniLabel);
                    return;
                }

                EditorGUILayout.LabelField(
                    "Parmak rigi — eklemi seç, Scene View'da çevir (kayda o hâli girer)",
                    EditorStyles.miniLabel);

                HandJointId[][] fingers = FingersMetadata.FINGER_TO_JOINTS;
                for (int finger = 0; finger < fingers.Length && finger < FINGER_LABELS.Length; finger++)
                {
                    DrawFingerRow(FINGER_LABELS[finger], fingers[finger], drivable);
                }

                if (GUILayout.Button("Parmakları Sıfırla (boş el duruşu)"))
                {
                    // null = kayıt yok → boş elin duruşu.
                    hand.ApplyPose(null);
                    SceneView.RepaintAll();
                }
            }
        }

        /// <summary>One finger's row: its name plus a numbered button per riggable joint (1 =
        /// closest to the wrist). Metacarpals never show up — they are not riggable.</summary>
        private static void DrawFingerRow(string label, HandJointId[] chain,
            List<HandJointMap> drivable)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90f));

                int shown = 0;
                for (int j = 0; chain != null && j < chain.Length; j++)
                {
                    HandJointMap map = FindMap(drivable, chain[j]);
                    if (map == null)
                    {
                        continue;
                    }

                    shown++;
                    bool selected = Selection.activeGameObject == map.transform.gameObject;
                    Color previous = GUI.backgroundColor;
                    if (selected)
                    {
                        GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
                    }

                    var content = new GUIContent(shown.ToString(), chain[j].ToString());
                    if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(26f)))
                    {
                        Selection.activeGameObject = map.transform.gameObject;
                        // ⚠️ Switch to the rotate tool: a bone dragged with the move tool tears the
                        // hand apart, and the record carries rotations only.
                        Tools.current = Tool.Rotate;
                        Tools.pivotRotation = PivotRotation.Local;
                        SceneView.RepaintAll();
                    }

                    GUI.backgroundColor = previous;
                }

                if (shown == 0)
                {
                    EditorGUILayout.LabelField("(eklem yok)", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>The joint map for one id in a hand's riggable set; <c>null</c> if it has none
        /// (a metacarpal, or a joint the branch does not expose).</summary>
        private static HandJointMap FindMap(List<HandJointMap> drivable, HandJointId id)
        {
            for (int i = 0; i < drivable.Count; i++)
            {
                if (drivable[i].id == id)
                {
                    return drivable[i];
                }
            }

            return null;
        }

        /// <summary>Shows the numbers that will be saved, live, next to the on-disk state.
        /// <para>⚠️ Read-only and stays that way: editable numbers would describe the same grip in
        /// two places (a field and a transform), which drift apart silently.</para></summary>
        private void DrawLiveValues(Transform weaponRoot, WeaponDefinition definition,
            List<GripHandAuthoring> hands)
        {
            if (CountLive(hands) == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("Kaydedilecek (salt okunur)", EditorStyles.boldLabel);

            for (int i = 0; i < hands.Count; i++)
            {
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    // ⚠️ The list was gathered at the START of this frame; a hand may have been
                    // destroyed since (any path that refreshes the stage externally). Drawing a dead
                    // entry throws MissingReferenceException and blanks the whole window.
                    continue;
                }

                Vector3 local = AnchorInItem(weaponRoot, hand.transform);
                string state = definition.HasGrip(hand.Kind, hand.RightHand)
                    ? "yazılmış"
                    : "yazılmamış";
                int joints = CaptureFingers(hand).Length;

                EditorGUILayout.LabelField(
                    $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")}",
                    $"{Format(local)}  {joints} eklem  ({state})");
            }
        }

        private static string Format(Vector3 value)
        {
            return $"({value.x:0.####}, {value.y:0.####}, {value.z:0.####})";
        }

        // ----------------------------------------------------------------- stage / hands

        private static PrefabStage CurrentStage()
        {
            return PrefabStageUtility.GetCurrentPrefabStage();
        }

        /// <summary>Root of the weapon in the open prefab stage; <c>null</c> if it is not a weapon.</summary>
        private static Transform StageWeaponRoot(PrefabStage stage)
        {
            GameObject root = stage != null ? stage.prefabContentsRoot : null;
            if (root == null || root.GetComponent<Weapon>() == null)
            {
                return null;
            }

            return root.transform;
        }

        internal static string HandRootName(GripSocketKind kind, bool rightHand)
        {
            return $"{HAND_ROOT_PREFIX}{kind}_{(rightHand ? "R" : "L")}]";
        }

        private static List<GripHandAuthoring> FindHands(Scene scene)
        {
            var found = new List<GripHandAuthoring>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return found;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || !roots[i].name.StartsWith(HAND_ROOT_PREFIX))
                {
                    continue;
                }

                var authoring = roots[i].GetComponent<GripHandAuthoring>();
                if (authoring != null)
                {
                    found.Add(authoring);
                }
            }

            return found;
        }

        /// <summary>Number of LIVE hands in the list. ⚠️ <c>Count</c> is not enough: the list is
        /// gathered at frame start and its objects can be destroyed before the frame ends, which
        /// would leave the save button enabled over an empty list.</summary>
        private static int CountLive(List<GripHandAuthoring> hands)
        {
            int live = 0;
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null)
                {
                    live++;
                }
            }

            return live;
        }

        private static GripHandAuthoring FindHand(List<GripHandAuthoring> hands, GripSocketKind kind,
            bool rightHand)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null && hands[i].Kind == kind && hands[i].RightHand == rightHand)
                {
                    return hands[i];
                }
            }

            return null;
        }

        private static void DestroyHands(Scene scene)
        {
            List<GripHandAuthoring> hands = FindHands(scene);
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null)
                {
                    DestroyImmediate(hands[i].gameObject);
                }
            }

            SceneView.RepaintAll();
        }

        /// <summary>Destroys hands in every open scene, prefab mode included.</summary>
        private static void DestroyAllHands()
        {
            PrefabStage stage = CurrentStage();
            if (stage != null)
            {
                DestroyHands(stage.scene);
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                DestroyHands(SceneManager.GetSceneAt(s));
            }
        }

        // --------------------------------------------------------------- building hands

        private static void CreateHandPair(PrefabStage stage, Transform weaponRoot,
            WeaponDefinition definition, GripSocketKind kind)
        {
            // ⚠️ Both hands are attempted INDEPENDENTLY: letting a failed right hand also skip the
            // left would look like "the left hand is never added" and hide the real error. Each
            // failure already logs its own line.
            GripHandAuthoring right = EnsureHand(stage, weaponRoot, definition, kind, true);
            GripHandAuthoring left = EnsureHand(stage, weaponRoot, definition, kind, false);

            GripHandAuthoring focus = right != null ? right : left;
            if (focus != null)
            {
                Selection.activeGameObject = focus.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            NotifyHandsChanged();
        }

        /// <summary>Tells open studio windows that the hands changed.
        /// <para>⚠️ <c>GetWindow</c> is NOT used: it opens a new window when none exists and steals
        /// focus — build/mirror can be triggered from the Scene view, where a focus jump would break
        /// the user's drag.</para></summary>
        private static void NotifyHandsChanged()
        {
            GripPoseStudio[] windows = Resources.FindObjectsOfTypeAll<GripPoseStudio>();
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].Repaint();
            }
        }

        /// <summary>Builds the hand for one grip point (returns the existing one if present).
        /// <para>⚠️ The root is the CONTROLLER frame and the ghost hand is its CHILD: what gets
        /// recorded is the root's pose (<see cref="AnchorInItem"/>), and the ghost hangs off it by
        /// <see cref="HandGripConvention.AnchorToWrist"/>. Dragged thing == recorded thing; a
        /// dragged child is redirected to the root
        /// (<see cref="RedirectSelectionToHandRoot"/>).</para>
        /// <para>⚠️ Local scale is pinned to 1: the hand is a scene root, not under the prefab, so
        /// the weapon's own 0.8 scale never leaks into it — otherwise "does the palm wrap the grip"
        /// would be answered at a 25% wrong ratio, which is the tool's whole job.</para></summary>
        private static GripHandAuthoring EnsureHand(PrefabStage stage, Transform weaponRoot,
            WeaponDefinition definition, GripSocketKind kind, bool rightHand)
        {
            GripHandAuthoring existing = FindHand(FindHands(stage.scene), kind, rightHand);
            if (existing != null)
            {
                // A hand hidden by Unity's save side effect returns to the hierarchy here, so
                // "Elleri Oluştur" also revives lost hands. The visual children are re-seated too: a
                // hand-dragged child never enters the record but misleads the user. A hand with no
                // offset written (identity + unmeasured) has it re-resolved from the prototype.
                if (!existing.GhostOffsetMeasured && existing.GhostOffset.Equals(Pose.identity) &&
                    TryGetGhostProvider(out HandGhostProvider existingProvider))
                {
                    HandGhost prototypeForExisting = existingProvider.GetHand(existing.Handedness);
                    Pose offset = ResolveGhostOffset(prototypeForExisting, rightHand, out bool measureds);
                    existing.SetGhostOffset(offset, measureds);
                }

                ApplyGhostOffset(existing);
                EnsureControllerModel(existing.gameObject, existing.RightHand);
                MarkDontSave(existing);
                HideIsdkComponents(GhostOf(existing));
                // ⚠️ The finger rig is NOT re-applied here: an existing hand may be mid-rig, and
                // resetting its bones to the on-disk pose would read as "I rig and it keeps
                // snapping back". A hand that has left the bench comes back through the branch
                // below, which does apply the record.
                return existing;
            }

            if (!TryGetGhostProvider(out HandGhostProvider provider))
            {
                return null;
            }

            Handedness handedness = rightHand ? Handedness.Right : Handedness.Left;
            HandGhost prototype = provider.GetHand(handedness);
            if (prototype == null)
            {
                Debug.LogWarning($"{LOG} El sağlayıcısında {handedness} el yok — el kurulamadı.");
                return null;
            }

            var root = new GameObject(HandRootName(kind, rightHand));
            SceneManager.MoveGameObjectToScene(root, stage.scene);
            root.transform.localScale = Vector3.one;

            var authoring = root.AddComponent<GripHandAuthoring>();
            if (authoring == null)
            {
                // ⚠️ Never leave a half-built hand: FindHands cannot see a component-less root (the
                // identity lives in the component), so it is neither listed nor cleanable and lingers
                // as an orphan until the next stage/Play transition. Destroying it immediately never
                // creates that ghost.
                DestroyImmediate(root);
                Debug.LogError($"{LOG} {kind}/{(rightHand ? "sağ" : "sol")} el kurulamadı: " +
                               "GripHandAuthoring eklenemedi. Sınıf RUNTIME asmdef'inde " +
                               "(VortexArena.Core, #if UNITY_EDITOR sarmalında) olmalı — Unity " +
                               "editör asmdef'inde derlenen bir MonoBehaviour'ı AddComponent ile " +
                               "kabul etmez ve null döner.");
                return null;
            }

            // Ghost hand: child of the root at the anchor→wrist pose (ResolveGhostOffset: measured
            // constant, else estimated from the skeleton). Visual only, never affects the record.
            HandGhost ghost = Instantiate(prototype, root.transform);
            GameObject handGo = ghost.gameObject;
            handGo.name = GHOST_NAME;
            handGo.transform.localScale = Vector3.one;

            HandPuppet puppet = ghost.GetComponent<HandPuppet>();

            // The offset is measured from the PROTOTYPE (bind pose): the estimate reads the thumb
            // root, and on a live instance the preset would fold that bone into the measurement.
            Pose ghostOffset = ResolveGhostOffset(prototype, rightHand, out bool measured);
            authoring.SetGhostOffset(ghostOffset, measured);
            ApplyGhostOffset(authoring);
            EnsureControllerModel(root, rightHand);

            ItemGripPose recorded = definition.GetGrip(kind, rightHand);

            // The root is placed directly, not via the puppet's SetRootPose (that writes the ghost's
            // own transform, but the ghost is a child with a fixed local offset).
            // ⚠️ The finger pose goes through the puppet, NOT ISDK's HandGhost.SetPose: SetPose wants
            // a HandPose object whose joint array would be a second finger source — the only source
            // is the record itself (and an empty record means the idle hand).
            Pose start = ResolveStartPose(weaponRoot, definition, kind, rightHand);
            root.transform.SetPositionAndRotation(start.position, start.rotation);

            authoring.Resolve(puppet, kind, rightHand);
            authoring.ApplyPose(recorded.fingerJoints);
            HideIsdkComponents(handGo);
            MarkDontSave(authoring);
            NotifyHandsChanged();
            return authoring;
        }

        /// <summary>The hand's ISDK ghost object (child of the root); <c>null</c> if absent.</summary>
        private static GameObject GhostOf(GripHandAuthoring hand)
        {
            if (hand == null)
            {
                return null;
            }

            var ghost = hand.GetComponentInChildren<HandGhost>(true);
            return ghost != null ? ghost.gameObject : null;
        }

        /// <summary>Local pose of the ghost hand relative to the controller root (anchor→wrist).
        /// <para>Uses the measured constant when present
        /// (<see cref="HandGripConvention.AnchorToWrist"/>, if not identity): the value
        /// <c>HandGripPoser</c> logs on the headset is the only truth. Otherwise it ESTIMATES —
        /// drawing the hand at the root's own axes would show a sideways hand (the ISDK wrist frame
        /// is not controller-aligned) and the user would place the weapon against that. The estimate
        /// has two parts: rotation = hand anatomy in anchor space
        /// (<see cref="HandGripConvention.AnchorBasis"/>, the same convention the remote avatar uses)
        /// composed with the bone basis measured from the ghost's OWN skeleton
        /// (<see cref="HandGripConvention.Correction"/>); position = palm centre taken to be on the
        /// controller (<c>HandGripPivot</c>: palm ≡ anchor), where palm centre is the OpenXR
        /// definition (half of wrist→middle-finger root).</para>
        /// <para>⚠️ The estimate never enters the record — that is the root's pose; this is only
        /// where the ghost is drawn. Once the constant is measured and pasted in, the estimate is
        /// never read.</para></summary>
        private static Pose ResolveGhostOffset(HandGhost prototype, bool rightHand, out bool measured)
        {
            Pose constant = HandGripConvention.AnchorToWrist(rightHand);
            measured = !constant.Equals(Pose.identity);
            if (measured)
            {
                return constant;
            }

            // Measured on the PROTOTYPE (asset, bind pose) — a live instance may be folded by a preset.
            HandPuppet puppet = prototype != null ? prototype.GetComponent<HandPuppet>() : null;
            Transform ghostRoot = prototype != null ? prototype.transform : null;
            if (puppet == null || ghostRoot == null ||
                !TryFindJoint(puppet, HandJointId.HandMiddle1, out Transform middleProximal) ||
                !TryFindJoint(puppet, HandJointId.HandThumb2, out Transform thumbProximal) ||
                !HandGripConvention.TryMeasureBoneBasis(ghostRoot, middleProximal, thumbProximal, rightHand,
                    out Quaternion boneBasis))
            {
                return Pose.identity;
            }

            Quaternion rotation = HandGripConvention.Correction(rightHand, boneBasis);
            Vector3 middleLocal = ghostRoot.InverseTransformPoint(middleProximal.position);
            Vector3 palmLocal = middleLocal * 0.5f;
            return new Pose(-(rotation * palmLocal), rotation);
        }

        private static bool TryFindJoint(HandPuppet puppet, HandJointId id, out Transform joint)
        {
            joint = null;
            List<HandJointMap> maps = puppet != null ? puppet.JointMaps : null;
            for (int i = 0; maps != null && i < maps.Count; i++)
            {
                if (maps[i] != null && maps[i].id == id && maps[i].transform != null)
                {
                    joint = maps[i].transform;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Seats the ghost hand at its stored offset under the root (restoring it if it was
        /// dragged by hand).</summary>
        private static void ApplyGhostOffset(GripHandAuthoring hand)
        {
            GameObject ghost = GhostOf(hand);
            if (ghost == null)
            {
                return;
            }

            Pose offset = hand.GhostOffset;
            ghost.transform.localPosition = offset.position;
            ghost.transform.localRotation = offset.rotation;
            ghost.transform.localScale = Vector3.one;
        }

        /// <summary>Places the Quest 3 controller model under the root (if missing) at identity: that
        /// is exactly how it sits under the anchor in game, making it the REAL alignment reference —
        /// unlike the ghost hand, which may be drawn from an estimate.
        /// <para>The model's <c>Animator</c> is stripped (button animation is meaningless here);
        /// mesh and scale stay as in the prefab. If not found it warns once and the hand is still
        /// built.</para></summary>
        private static void EnsureControllerModel(GameObject root, bool rightHand)
        {
            if (root == null || root.transform.Find(CONTROLLER_NAME) != null)
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONTROLLER_PREFAB_PATH);
            Transform source = prefab != null
                ? prefab.transform.Find(rightHand ? CONTROLLER_MODEL_RIGHT : CONTROLLER_MODEL_LEFT)
                : null;
            if (source == null)
            {
                if (!_controllerModelWarned)
                {
                    _controllerModelWarned = true;
                    Debug.LogWarning($"{LOG} Kumanda modeli bulunamadı ('{CONTROLLER_PREFAB_PATH}' → " +
                                     $"{CONTROLLER_MODEL_RIGHT}/{CONTROLLER_MODEL_LEFT}); kök yalnız gizmo ve " +
                                     "hayalet elle çizilir.");
                }

                return;
            }

            GameObject model = Instantiate(source.gameObject, root.transform);
            model.name = CONTROLLER_NAME;
            model.transform.localPosition = source.localPosition;
            model.transform.localRotation = source.localRotation;
            model.transform.localScale = source.localScale;

            var animator = model.GetComponent<Animator>();
            if (animator != null)
            {
                DestroyImmediate(animator);
            }
        }

        /// <summary>Starting pose of the controller root — position from three sources IN ORDER:
        /// (1) the record in the definition, (2) roughly the centre of the grip part, (3) slightly
        /// above the weapon. Rotation is ALWAYS the weapon's (the record carries none).
        /// <para>⚠️ (1) is the exact inverse of the record: "build hands → touch nothing → save"
        /// must not change the stored value. If that identity breaks, one of the space directions is
        /// flipped and the only places to look are <see cref="AnchorInItem"/> and this
        /// recomposition.</para>
        /// <para>⚠️ The composition is UNSCALED (no <c>TransformPoint</c>): the record is in METRES
        /// and <c>WPN_*</c> roots are 0.8 scaled, so a scaled composition puts the root 1/0.8 too far
        /// from the weapon.</para></summary>
        private static Pose ResolveStartPose(Transform weaponRoot, WeaponDefinition definition,
            GripSocketKind kind, bool rightHand)
        {
            if (definition.HasGrip(kind, rightHand))
            {
                Vector3 local = definition.GetGrip(kind, rightHand).position;
                return new Pose(weaponRoot.position + weaponRoot.rotation * local, weaponRoot.rotation);
            }

            // No cache needed: this scan runs ONCE per grip point while building a hand.
            Renderer part = SearchWeaponPart(weaponRoot,
                kind == GripSocketKind.Primary ? GRIP_KEYS : FOREGRIP_KEYS);

            Vector3 position = part != null
                ? part.bounds.center
                : weaponRoot.position + Vector3.up * 0.1f;

            return new Pose(position, weaponRoot.rotation);
        }

        /// <summary>Local POSITION of the controller root relative to the item — the ONLY way the
        /// record is computed (<see cref="ItemGripPose"/>: anchor space, no rotation).
        /// <para>⚠️ <see cref="Transform.InverseTransformPoint"/> is NOT used: the record is in
        /// METRES and must not be shrunk by the item's visual scale (0.8 on <c>WPN_*</c> roots). The
        /// recomposition in <see cref="ResolveStartPose"/> mirrors this, keeping both ends on one
        /// contract.</para></summary>
        private static Vector3 AnchorInItem(Transform weaponRoot, Transform handRoot)
        {
            return Quaternion.Inverse(weaponRoot.rotation) * (handRoot.position - weaponRoot.position);
        }

        /// <summary>Keeps the roots weapon-aligned: the record carries no rotation, so rotating a
        /// root has no in-game meaning and it is snapped back, keeping bench and game in sync. Runs
        /// every editor tick and writes only to a root that drifted.</summary>
        private static void KeepRootsAligned(Transform weaponRoot, List<GripHandAuthoring> hands)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    continue;
                }

                if (Quaternion.Angle(hand.transform.rotation, weaponRoot.rotation) > 0.01f)
                {
                    hand.transform.rotation = weaponRoot.rotation;
                }
            }
        }

        /// <summary>⚠️ <see cref="HideFlags.DontSave"/> is written across the whole subtree so no
        /// hand part reaches the file on a prefab save; the flag is per GameObject, the root alone
        /// is not enough.
        /// <para>Everything BELOW the root also gets <see cref="HideFlags.NotEditable"/> — EXCEPT the
        /// riggable finger joints. The ghost mesh and the controller model are visual children while
        /// the record is the root's pose, so dragging one of THOSE would produce "I saved but nothing
        /// changed"; the finger bones are the opposite case — they ARE the record's finger half and
        /// must stay selectable and rotatable.</para></summary>
        private static void MarkDontSave(GripHandAuthoring hand)
        {
            if (hand == null)
            {
                return;
            }

            GameObject root = hand.gameObject;
            var editable = new HashSet<Transform>();
            List<HandJointMap> joints = hand.DrivableJoints();
            for (int i = 0; i < joints.Count; i++)
            {
                editable.Add(joints[i].transform);
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform node = all[i];
                bool open = node.gameObject == root || editable.Contains(node);
                node.gameObject.hideFlags = open
                    ? HideFlags.DontSave
                    : HideFlags.DontSave | HideFlags.NotEditable;

                if (open)
                {
                    // Unity's save-time flip can also land on the COMPONENT flags; a joint whose
                    // Transform stays NotEditable is selectable but its handles do nothing.
                    node.hideFlags &= ~(HideFlags.HideInInspector | HideFlags.NotEditable);
                }
            }
        }

        /// <summary>Hides the ISDK components (<c>HandGhost</c>, <c>HandPuppet</c> …) on the ghost
        /// object from the Inspector so the user sees only Transform +
        /// <see cref="GripHandAuthoring"/> instead of a hundred-line Joint Maps list. Silent on
        /// <c>null</c>.
        /// <para>⚠️ Hiding is VISUAL only: the components stay, and the <c>_puppet</c> reference and
        /// ISDK's pose application keep working. Deleting them would break the hand.</para></summary>
        private static void HideIsdkComponents(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is GripHandAuthoring)
                {
                    continue;
                }

                component.hideFlags |= HideFlags.HideInInspector;
            }
        }

        // ------------------------------------------------------------------- mirroring

        /// <summary>Mirrors a hand's placement to the OPPOSITE hand (across the item-space YZ plane),
        /// building it if missing. The finger rig is COPIED joint by joint: ISDK's left and right
        /// ghosts are mirror duplicates of one skeleton, so the same local rotations produce the
        /// mirrored pose — which is why this is a copy and not a negation.
        /// <para>⚠️ ISDK's <c>MirrorHandGrabPose</c> is NOT used: with no surface to mirror against
        /// (a <c>Grabbable</c> collider) it applies an arbitrary "best guess" rotation, and this
        /// bench has no surface at all. The maths here is one verifiable line:
        /// <c>p=(x,y,z) → (−x,y,z)</c>, no rotation, since the root is already weapon-aligned.</para>
        /// <para>⚠️ Mirroring is a STARTING POINT, not the final word: a grip is roughly symmetric
        /// in the sagittal plane but trigger, magazine and charging handle are one-sided.</para>
        /// </summary>
        internal static bool MirrorToOpposite(GripHandAuthoring source)
        {
            if (source == null)
            {
                return false;
            }

            PrefabStage stage = CurrentStage();
            Transform weaponRoot = StageWeaponRoot(stage);
            if (weaponRoot == null)
            {
                Debug.LogWarning($"{LOG} Aynalama için prefab kipi açık olmalı (referans silahın " +
                                 "kökü).");
                return false;
            }

            WeaponDefinition definition = ResolveDefinition(weaponRoot.gameObject);
            if (definition == null)
            {
                return false;
            }

            Vector3 local = AnchorInItem(weaponRoot, source.transform);
            var mirroredPosition = new Vector3(-local.x, local.y, local.z);

            GripHandAuthoring opposite = EnsureHand(stage, weaponRoot, definition,
                source.Kind, !source.RightHand);
            if (opposite == null)
            {
                return false;
            }

            opposite.transform.SetPositionAndRotation(
                weaponRoot.position + weaponRoot.rotation * mirroredPosition,
                weaponRoot.rotation);
            opposite.transform.localScale = Vector3.one;
            opposite.ApplyPose(CaptureFingers(source));

            Selection.activeGameObject = opposite.gameObject;
            SceneView.RepaintAll();
            NotifyHandsChanged();
            return true;
        }

        // ----------------------------------------------------------------------- saving

        /// <summary>Turns the bench placement into persistent data: for every live hand, the
        /// controller root's item-local position + the ghost's riggable finger joints →
        /// <c>WD_*.asset</c>.
        /// <para>⚠️ This is the ONLY writer of the finger rig — the hand's own Inspector has no save
        /// button (<see cref="GripHandAuthoringEditor"/>). Whatever the bones look like at this
        /// moment is what lands on disk, so there is no "which of the two is current" question.</para>
        /// <para>⚠️ Nothing is written into the prefab contents and the prefab is not saved: the
        /// record lives only in the definition, and the hands are separate stage-scene roots anyway.</para>
        /// <para>⚠️ Never writes in Play mode: the record goes to disk via <c>AssetDatabase</c> and a
        /// value written during Play becomes ambiguous at the next domain reload.</para>
        /// <para>A successful write also runs the weapon kit (<see cref="RunWeaponKit"/>): the record
        /// is not a product on its own — the socket indicator, the WPN prefabs and the catalog derive
        /// from it, and leaving that to a second tool made "I saved but nothing changed in game" a
        /// silent step.</para></summary>
        private static bool SaveHands(Transform weaponRoot, WeaponDefinition definition,
            List<GripHandAuthoring> hands)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning($"{LOG} Play kipinde kavrama yazılmaz.");
                return false;
            }

            Undo.RecordObject(definition, "Kavrama pozunu yaz");

            int written = 0;
            for (int i = 0; i < hands.Count; i++)
            {
                // Dead entries are skipped silently: the list can go stale within the frame, and an
                // error line would send the user hunting for a hand that no longer exists.
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    continue;
                }

                Vector3 local = AnchorInItem(weaponRoot, hand.transform);
                definition.EditorSetGrip(hand.Kind, hand.RightHand, local, CaptureFingers(hand));
                written++;
            }

            if (written == 0)
            {
                Debug.LogWarning($"{LOG} Tezgâhta el yok — hiçbir şey yazılmadı.");
                return false;
            }

            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG} '{weaponRoot.name}' kavraması yazıldı: {written} el → " +
                      $"{definition.name}.asset", definition);

            // ⚠️ delayCall: kit WPN prefabını diske yeniden yazar, açık prefab kipi de içeriğini
            // yeniden yükler. Bu OnGUI'nin ORTASINDA olursa pencere yok edilmiş bir weaponRoot'u
            // çizmeye devam eder (MissingReferenceException) — kit, kare bittikten sonra koşar.
            EditorApplication.delayCall += RunWeaponKit;
            return true;
        }

        /// <summary>Reads the hand's riggable finger joints off the ghost skeleton, in the form the
        /// record stores (joint NAME + local rotation — <see cref="HandJointRotation"/>).
        /// <para>⚠️ The rotation taken is <c>HandJointMap.TrackedRotation</c>, i.e. the bone's local
        /// rotation with the map's own <c>RotationOffset</c> UNDONE. That is the space
        /// <c>HandPuppet.SetJointRotations</c> and <c>SyntheticHand.OverrideAllJoints</c> both
        /// consume; storing the raw <c>localRotation</c> would apply the offset a second time on the
        /// way back and the bench pose would not reproduce in game.</para>
        /// <para>⚠️ Metacarpals are excluded (<see cref="HandPoseLibrary.IsDrivable"/>) — rationale in
        /// <see cref="HandPoseLibrary"/>. Returns an empty array when the ghost has no puppet, which
        /// the record reads as "not rigged" and the hand falls back to the idle pose.</para></summary>
        private static HandJointRotation[] CaptureFingers(GripHandAuthoring hand)
        {
            List<HandJointMap> joints = hand != null
                ? hand.DrivableJoints()
                : new List<HandJointMap>();

            var captured = new HandJointRotation[joints.Count];
            for (int i = 0; i < joints.Count; i++)
            {
                captured[i] = HandJointRotation.From(
                    joints[i].id.ToString(), joints[i].TrackedRotation);
            }

            return captured;
        }

        /// <summary>Weapon kit sync run right after a save, so the user does not have to open
        /// <c>Configure All Build Elements</c> and press "Yalnız Senkronize Et" by hand: the record
        /// only becomes visible in game through the kit (<c>VA_GripSocket</c>, the WPN prefabs, the
        /// catalog).
        /// <para>⚠️ The exception is swallowed and only logged (same reason as
        /// <c>BuildElementsConfigurator.SyncWeaponKit</c>): a slip in the kit must not make an
        /// already-written record look like a failed save.</para>
        /// <para>⚠️ The bench empties: the kit rewrites the open <c>WPN_*</c> prefab, the prefab
        /// stage reloads its contents and the <c>DontSave</c> hand roots die with it. This is not a
        /// loss — the record is on disk and <i>Elleri Oluştur</i> brings the hands back to the very
        /// same place (the window says so in its notice).</para>
        /// <para>⚠️ No dialog and no progress bar here: a modal blocks Unity's main thread and times
        /// out under CLI. The kit writes its own summary line to the console.</para></summary>
        private static void RunWeaponKit()
        {
            try
            {
                WeaponKitBuilder.BuildAll();
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG} Kavrama kaydı yazıldı ama silah kiti eşitlemesi hata verdi — " +
                               "kayıt diskte, kit eşitlenmedi (Tools > VortexArena > Build > " +
                               "Configure All Build Elements > Yalnız Senkronize Et ile tekrar " +
                               "dene): " + e);
            }
        }

        private static WeaponDefinition ResolveDefinition(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var weapon = root.GetComponent<Weapon>();
            return weapon == null ? null : weapon.Definition;
        }

        // -------------------------------------------------------------------- hand branch

        /// <summary>Which ISDK hand branch was compiled — detected from the EXISTENCE of an enum
        /// member: <c>HandJointId.HandPalm</c> only exists in the OpenXR branch's table.
        /// <para>⚠️ Why a probe and not <c>#if</c>: <c>ISDK_OPENXR_HAND</c> is produced by the
        /// package's own asmdef via <c>versionDefines</c> and is defined ONLY in that assembly, so
        /// in our compilation it is always false and every <c>#if</c> line would silently pick the
        /// wrong branch. The enum member instead comes from the active branch's type.</para>
        /// </summary>
        private static bool IsOpenXrHandBranch =>
            Enum.IsDefined(typeof(HandJointId), "HandPalm");

        /// <summary>Loads the model provider for the active hand branch, falling back to the other.
        /// ⚠️ The provider belongs to a BRANCH: pairing an OpenXR skeleton with the OVR model
        /// deforms the hand (joint count and order differ).</summary>
        private static bool TryGetGhostProvider(out HandGhostProvider provider)
        {
            string wanted = IsOpenXrHandBranch
                ? GHOST_PROVIDER_PATH_OPENXR
                : GHOST_PROVIDER_PATH_OVR;

            if (_ghostProvider == null || _ghostProviderPath != wanted)
            {
                _ghostProvider = AssetDatabase.LoadAssetAtPath<HandGhostProvider>(wanted);
                _ghostProviderPath = wanted;

                if (_ghostProvider == null)
                {
                    string fallback = wanted == GHOST_PROVIDER_PATH_OPENXR
                        ? GHOST_PROVIDER_PATH_OVR
                        : GHOST_PROVIDER_PATH_OPENXR;

                    _ghostProvider = AssetDatabase.LoadAssetAtPath<HandGhostProvider>(fallback);
                    if (_ghostProvider != null)
                    {
                        _ghostProviderPath = fallback;
                    }
                }
            }

            provider = _ghostProvider;
            if (provider != null)
            {
                _ghostProviderWarned = false;
                return true;
            }

            if (!_ghostProviderWarned)
            {
                _ghostProviderWarned = true;
                Debug.LogWarning($"{LOG} ISDK el sağlayıcısı iki dalda da bulunamadı " +
                                 $"({GHOST_PROVIDER_PATH_OPENXR} / {GHOST_PROVIDER_PATH_OVR}) — " +
                                 "paket sürümü değiştiyse yolları bu dosyadaki sabitlerden güncelle.");
            }

            return false;
        }

        /// <summary>One line stating which branch's provider is in use. ⚠️ Diagnostics, not
        /// decoration: with the wrong branch the hand still builds but its skeleton does not hold,
        /// and the cause would be visible nowhere.</summary>
        private void DrawGhostSourceSection()
        {
            string asset = string.IsNullOrEmpty(_ghostProviderPath)
                ? "(henüz yüklenmedi)"
                : System.IO.Path.GetFileName(_ghostProviderPath);

            string branch = IsOpenXrHandBranch ? "OpenXR" : "OVR";

            EditorGUILayout.HelpBox($"El dalı: {branch} · el modeli: {asset}", MessageType.None);
        }

        // ------------------------------------------------------------------- part search

        /// <summary>First <see cref="Renderer"/> in the weapon's subtree whose name contains one of
        /// the keys, tried IN ORDER (specific keys come first).
        /// <para>Only consumer is <see cref="ResolveStartPose"/>: on a weapon with no record, the
        /// rough centre of the grip / front-grip part decides where the hand opens.</para></summary>
        private static Renderer SearchWeaponPart(Transform weaponRoot, string[] keywords)
        {
            Renderer found = null;
            for (int i = 0; i < keywords.Length && found == null; i++)
            {
                found = SearchPartRenderer(weaponRoot, keywords[i]);
            }

            return found;
        }

        /// <summary>Depth-first search by name, skipping branches flagged by
        /// <see cref="IsPartSearchNoise"/>.</summary>
        private static Renderer SearchPartRenderer(Transform node, string keyword)
        {
            if (IsPartSearchNoise(node))
            {
                return null;
            }

            if (node.name.ToLowerInvariant().Contains(keyword))
            {
                var renderer = node.GetComponent<Renderer>();
                if (renderer != null)
                {
                    return renderer;
                }
            }

            for (int i = 0; i < node.childCount; i++)
            {
                Renderer hit = SearchPartRenderer(node.GetChild(i), keyword);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>Branches the part search must skip: leftover hand models and the grab frame.
        /// ⚠️ Without this filter the tool can mistake WHAT IT BUILT for a grip — those subtrees also
        /// carry Renderers whose names collide with the search keys, so a new hand would open on top
        /// of its own old one.</summary>
        private static bool IsPartSearchNoise(Transform node)
        {
            if (node.name == ItemHandRig.RootNodeName || node.name.StartsWith(HAND_ROOT_PREFIX))
            {
                return true;
            }

            return node.GetComponent<WeaponFrame>() != null;
        }
    }
}
