using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Player;
using VortexArena.Protocol;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Development &gt; Dev</c> — developer control panel: role, server
    /// target, Play entry point and the <b>serverless sandbox</b> mode.
    ///
    /// <para><b>Never touches the server</b> — does not start, stop or build it: the server is
    /// always run by hand.</para>
    ///
    /// <para><b>Nothing is committed:</b> every selection goes to <c>EditorPrefs</c> via
    /// <see cref="DevSession"/> (personal), while the target list is READ from the repo's
    /// <c>dev-targets.json</c> (shared) — <see cref="DevTargets"/>. No change made here dirties a
    /// scene or asset.</para>
    ///
    /// <para><b>NO modal dialogs:</b> <c>EditorUtility.DisplayDialog</c> locks Unity's main thread
    /// here and breaks Unity CLI verification ("Main thread operation timed out"). Feedback is
    /// console logs + in-window <c>HelpBox</c> only.</para>
    /// </summary>
    public class DevWindow : EditorWindow
    {
        /// <summary>ASCII sentinel written to <see cref="DevSession.TargetName"/> for the custom
        /// option. Matching no saved target, it keeps the popup on custom across reopens.</summary>
        private const string CustomTargetName = "Ozel";

        private const string CustomTargetLabel = "Özel…";

        private static readonly string[] RoleLabels = { "Player", "Admin" };
        private static readonly string[] StartLabels = { "Boot'tan", "Açık sahneden" };

        [SerializeField] private Vector2 scroll;

        // Caches — built in OnEnable/refresh to avoid a file read on every OnGUI.
        [NonSerialized] private DevTarget[] targetList = Array.Empty<DevTarget>();
        [NonSerialized] private string[] targetLabels = Array.Empty<string>();

        /// <summary>Source of the sandbox mode picker: modIds from <c>GameCatalog</c>. The lobby
        /// profile is listed too (unlike the admin picker) — a legitimate sandbox choice for trying
        /// lobby weapons.</summary>
        [NonSerialized] private string[] modeIds = Array.Empty<string>();

        [MenuItem("Tools/VortexArena/Development/Dev", false, 80)]
        private static void Open()
        {
            var window = GetWindow<DevWindow>(false, "Dev", true);
            window.minSize = new Vector2(430f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Dev");
            RefreshCaches();
            BootstrapSelection();
        }

        // --------------------------------------------------------------------- cache

        private void RefreshCaches()
        {
            IReadOnlyList<DevTarget> targets = DevTargets.Targets;

            targetList = new DevTarget[targets.Count];
            targetLabels = new string[targets.Count + 1];
            for (int i = 0; i < targets.Count; i++)
            {
                targetList[i] = targets[i];
                targetLabels[i] = targets[i].Label;
            }

            targetLabels[targets.Count] = CustomTargetLabel;

            RefreshModeCache();
        }

        /// <summary>Reads modIds from the catalog. The catalog lives under <c>Resources</c> and is
        /// loaded the same way in the editor — no second lookup path (AssetDatabase), so we see the
        /// exact runtime list.</summary>
        private void RefreshModeCache()
        {
            var catalog = Resources.Load<GameCatalog>("GameCatalog");
            ModeDefinition[] modes = catalog != null ? catalog.Modes : null;
            if (modes == null)
            {
                modeIds = Array.Empty<string>();
                return;
            }

            var ids = new List<string>(modes.Length);
            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i] != null && !string.IsNullOrEmpty(modes[i].ModeId))
                {
                    ids.Add(modes[i].ModeId);
                }
            }

            modeIds = ids.ToArray();
        }

        /// <summary>
        /// Applies <c>dev-targets.json</c> defaults on first open (no target selected yet). Later
        /// opens keep the personal selection — even the custom one, thanks to the
        /// <see cref="CustomTargetName"/> sentinel.
        /// </summary>
        private void BootstrapSelection()
        {
            // No sandbox mode picked yet → first catalog mode: an empty picker would mean a
            // "no mode selected" warning on the first Play.
            if (string.IsNullOrEmpty(DevSession.SandboxModeId) && modeIds.Length > 0)
            {
                DevSession.SandboxModeId = modeIds[0];
            }

            if (!string.IsNullOrEmpty(DevSession.TargetName))
            {
                return;
            }

            if (DevTargets.TryFind(DevTargets.DefaultTargetName, out DevTarget target))
            {
                ApplyTarget(target);
            }

            if (!EditorPrefs.HasKey(DevSession.KeyRole))
            {
                DevSession.Role = DevTargets.DefaultRole;
            }
        }

        // --------------------------------------------------------------------- draw

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUI.BeginChangeCheck();
            bool injectionEnabled = EditorGUILayout.ToggleLeft("Dev enjeksiyonu açık", DevSession.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                DevSession.Enabled = injectionEnabled;
            }

            if (!injectionEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Dev enjeksiyonu KAPALI: üretim yolu birebir koşar (rol AppBoot'tan, adres " +
                    "keşif zincirinden gelir). Aşağıdaki seçim alanları devre dışı.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!injectionEnabled))
            {
                DrawSelection();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seçim: " + DevSession.Summary, EditorStyles.miniLabel);

            DrawObstacleDiagnostics();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Live read-out of the obstacle violation probe (Play mode only).
        /// <para><b>Why:</b> the penalty surfaces as a server <c>health_update</c> — the answer to
        /// "why am I taking damage" is in the PROBE, not the player. Without seeing which rule fired
        /// and which obstacle answered, a false positive is indistinguishable from a real violation.
        /// </para>
        /// <para>⚠️ Read-only; the measurement itself lives in <c>ObstacleViolationProbe</c>
        /// (self-bootstrapping singleton, independent of this window).</para>
        /// </summary>
        private void DrawObstacleDiagnostics()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Engel ihlali (canlı)", EditorStyles.boldLabel);

            if (ObstacleViolationProbe.Instance == null)
            {
                EditorGUILayout.LabelField("Ölçüm yok (rol admin ya da rig henüz doğmadı).",
                    EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(
                $"İhlal: {(ObstacleViolationProbe.IsViolating ? "EVET" : "hayır")}   " +
                $"durum: {ObstacleViolationProbe.LastTrigger}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"Kafa kabuğu içeride: {ObstacleViolationProbe.HeadInsideLevel:P0}   " +
                $"karartma: {ObstacleViolationProbe.FadeAlpha:P0}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"Ateş kapısı (kafa/el): {(ObstacleViolationProbe.IsBodyBlocked ? "KAPALI" : "açık")}",
                EditorStyles.miniLabel);

            string collider = ObstacleViolationProbe.LastTriggerCollider;
            EditorGUILayout.LabelField(
                "Son 'içeride' diyen engel: " + (string.IsNullOrEmpty(collider) ? "—" : collider),
                EditorStyles.miniLabel);

            // Probe runs at 20 Hz; the window is event-driven so repaint manually.
            Repaint();
        }

        private void DrawSelection()
        {
            EditorGUILayout.Space();

            // ---- role
            EditorGUI.BeginChangeCheck();
            int roleIndex = RadioRow("Rol", RoleLabels,
                DevSession.Role == AppSession.RolePlayer ? 0 : 1, "kısayol: Ctrl+Alt+R");
            if (EditorGUI.EndChangeCheck())
            {
                DevSession.Role = roleIndex == 0 ? AppSession.RolePlayer : AppSession.RoleAdmin;
            }

            // ---- serverless sandbox
            EditorGUI.BeginChangeCheck();
            bool sandbox = EditorGUILayout.ToggleLeft(
                new GUIContent("Sunucusuz sandbox",
                    "Sunucuya hiç bağlanmaz: admin'den harita seçmek ve elle kalibrasyon gerekmez"),
                DevSession.Sandbox);
            if (EditorGUI.EndChangeCheck())
            {
                DevSession.Sandbox = sandbox;
                if (sandbox)
                {
                    // Sandbox only makes sense when playing from a content scene: started from Boot
                    // the shell scene drives the flow and LobbyController tries to connect.
                    DevSession.StartFromBoot = false;
                }
            }

            if (DevSession.Sandbox)
            {
                DrawSandbox();
            }

            // ---- target (meaningless in sandbox: nothing connects)
            using (new EditorGUI.DisabledScope(DevSession.Sandbox))
            {
                DrawTarget();
            }

            // ---- Play entry point
            EditorGUI.BeginChangeCheck();
            int startIndex = RadioRow("Başlangıç", StartLabels, DevSession.StartFromBoot ? 0 : 1, null);
            if (EditorGUI.EndChangeCheck())
            {
                DevSession.StartFromBoot = startIndex == 0;
            }

            if (DevSession.Sandbox && DevSession.StartFromBoot)
            {
                EditorGUILayout.HelpBox(
                    "Sandbox açık ama başlangıç \"Boot'tan\" — sandbox UYGULANMAZ (Boot → Lobby " +
                    "akışını kabuk sahnesi sürer ve sunucuya bağlanmaya çalışır). Başlangıcı " +
                    "\"Açık sahneden\" yapın.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Sandbox settings — a single choice: <b>mode</b> (the key the weapon loadout is read from).
        /// Weapon source is not selectable; sandbox always hands out the loadout in order.
        /// </summary>
        private void DrawSandbox()
        {
            if (modeIds.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "GameCatalog okunamadı ya da içinde mod yok — sandbox'ta silah gelmez. " +
                    "Katalog 'Resources/GameCatalog' yolunda olmalı.",
                    MessageType.Warning);
            }
            else
            {
                int currentMode = Array.IndexOf(modeIds, DevSession.SandboxModeId);

                EditorGUI.BeginChangeCheck();
                int pickedMode = EditorGUILayout.Popup(
                    new GUIContent("Mod", "Silah loadout'u bu moddan okunur (GameCatalog.FindMode)"),
                    Mathf.Max(0, currentMode), modeIds);
                if (EditorGUI.EndChangeCheck())
                {
                    DevSession.SandboxModeId = modeIds[pickedMode];
                }

                if (currentMode < 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Kayıtlı mod '{DevSession.SandboxModeId}' katalogda yok — yukarıdan " +
                        "yeniden seçin.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.HelpBox(
                "Sunucuya BAĞLANILMAZ: harita seçen admin ve elle kalibrasyon gerekmez, serbest " +
                "atış açılır. Silahlar modun loadout'undan SIRAYLA gelir — grip'e her basışta bir " +
                "sonraki (bırakınca yok olur). Hasar/skor/faz YOKTUR — üçünün de otoritesi " +
                "sunucudadır. Test edeceğiniz arena (ya da mekan lobisi) sahnesini açıp Play'e basın.",
                MessageType.Info);

            EditorGUILayout.Space();
        }

        /// <summary>Server target selection (a named target, or custom IP/Port).</summary>
        private void DrawTarget()
        {
            int customIndex = targetList.Length;
            int currentIndex = CurrentTargetIndex();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int pickedIndex = EditorGUILayout.Popup(
                new GUIContent("Hedef", "dev-targets.json'dan gelir (commit'li); seçim EditorPrefs'te kişisel kalır"),
                currentIndex, targetLabels);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyTargetIndex(pickedIndex);
            }

            if (GUILayout.Button("Tazele", GUILayout.Width(60f)))
            {
                DevTargets.Reload();
                RefreshCaches();
                customIndex = targetList.Length;
                pickedIndex = CurrentTargetIndex();
            }

            EditorGUILayout.EndHorizontal();

            if (!DevTargets.FileFound)
            {
                EditorGUILayout.HelpBox(
                    $"'{DevTargets.FilePath}' bulunamadı — gömülü varsayılan hedefler kullanılıyor. " +
                    "Kalıcı hedef listesi için dosyayı repo köküne ekleyip \"Tazele\"ye basın.",
                    MessageType.Info);
            }

            if (pickedIndex >= customIndex)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                string ip = EditorGUILayout.TextField(
                    new GUIContent("IP", "Boş bırakılırsa adres YAZILMAZ; keşif zinciri (PlayerPrefs > beacon > arena.json) devralır"),
                    DevSession.Ip);
                int port = EditorGUILayout.IntField("Port", DevSession.Port);
                if (EditorGUI.EndChangeCheck())
                {
                    DevSession.Ip = ip;
                    DevSession.Port = port > 0 ? port : ArenaProtocol.CONTROL_PORT;
                }

                EditorGUI.indentLevel--;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Label + horizontal radio button row. Returns the new index when an unselected button is
        /// pressed (the <c>index != i</c> guard is required against two toggles both returning true
        /// in the same frame).
        /// </summary>
        private static int RadioRow(string label, string[] options, int index, string suffix)
        {
            int result = index;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth - 4f));

            for (int i = 0; i < options.Length; i++)
            {
                if (GUILayout.Toggle(index == i, options[i], EditorStyles.radioButton, GUILayout.Width(110f)) &&
                    index != i)
                {
                    result = i;
                }
            }

            if (!string.IsNullOrEmpty(suffix))
            {
                EditorGUILayout.LabelField(suffix, EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>Popup index for the saved target name; the custom index when nothing matches.</summary>
        private int CurrentTargetIndex()
        {
            string name = DevSession.TargetName;
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < targetList.Length; i++)
                {
                    if (string.Equals(targetList[i].name, name, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return targetList.Length; // "Özel…"
        }

        private void ApplyTargetIndex(int index)
        {
            if (index < 0 || index >= targetList.Length)
            {
                // Custom: IP/Port left to the user, current values preserved.
                DevSession.TargetName = CustomTargetName;
                return;
            }

            ApplyTarget(targetList[index]);
        }

        private static void ApplyTarget(DevTarget target)
        {
            if (target == null)
            {
                return;
            }

            DevSession.TargetName = target.name;
            DevSession.Ip = target.ip ?? string.Empty;
            DevSession.Port = target.port > 0 ? target.port : ArenaProtocol.CONTROL_PORT;
        }
    }
}
