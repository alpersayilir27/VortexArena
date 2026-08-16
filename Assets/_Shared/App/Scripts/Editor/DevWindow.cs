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
    /// <c>Tools &gt; VortexArena &gt; Development &gt; Dev</c> — geliştirici kontrol paneli: rol, sunucu hedefi,
    /// Play başlangıcı ve <b>sunucusuz sandbox</b> kipi.
    ///
    /// <para><b>Sunucuya hiç dokunmaz</b> — ne başlatır, ne durdurur, ne derler: sunucu her zaman
    /// elle çalıştırılır.</para>
    ///
    /// <para><b>Hiçbir şey commit'lenmez:</b> tüm seçim <see cref="DevSession"/> üzerinden
    /// <c>EditorPrefs</c>'e yazılır (kişisel), hedef listesi ise repo'daki
    /// <c>dev-targets.json</c>'dan OKUNUR (paylaşılan) — <see cref="DevTargets"/>. Bu pencerede
    /// yapılan hiçbir değişiklik sahne/asset kirletmez.</para>
    ///
    /// <para><b>Modal dialog YOK:</b> bu projede <c>EditorUtility.DisplayDialog</c> Unity ana
    /// thread'ini kilitleyip Unity CLI doğrulamasını ("Main thread operation timed out") bozuyor.
    /// Geri bildirim yalnız konsol logu + pencere içi <c>HelpBox</c> ile verilir.</para>
    /// </summary>
    public class DevWindow : EditorWindow
    {
        /// <summary>"Özel" seçildiğinde <see cref="DevSession.TargetName"/>'e yazılan ASCII sentinel.
        /// Kayıtlı hiçbir hedefe uymadığı için pencere yeniden açıldığında da "Özel"de kalır.</summary>
        private const string CustomTargetName = "Ozel";

        private const string CustomTargetLabel = "Özel…";

        private static readonly string[] RoleLabels = { "Player", "Admin" };
        private static readonly string[] StartLabels = { "Boot'tan", "Açık sahneden" };

        [SerializeField] private Vector2 scroll;

        // Önbellekler — her OnGUI'de dosya okuması yapmamak için OnEnable/"Tazele"de kurulur.
        [NonSerialized] private DevTarget[] targetList = Array.Empty<DevTarget>();
        [NonSerialized] private string[] targetLabels = Array.Empty<string>();

        /// <summary>Sandbox mod seçicisinin kaynağı: <c>GameCatalog</c>'daki modId'ler. Lobi
        /// profili de listelenir (admin seçicisinin aksine) — lobi silahlarını denemek için meşru bir
        /// sandbox seçimidir.</summary>
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

        // ------------------------------------------------------------------ önbellek

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

        /// <summary>Katalogdaki modId'leri okur. Katalog <c>Resources</c> altındadır ve editörde
        /// de aynı yoldan yüklenir — ikinci bir arama yolu (AssetDatabase) açmıyoruz ki çalışma
        /// anıyla aynı listeyi görelim.</summary>
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
        /// İlk açılışta (hiç hedef seçilmemişken) <c>dev-targets.json</c>'daki varsayılanları
        /// uygular. Sonraki açılışlarda kişisel seçim korunur — "Özel" bile
        /// (<see cref="CustomTargetName"/> sentinel'i sayesinde) sıfırlanmaz.
        /// </summary>
        private void BootstrapSelection()
        {
            // Sandbox modu hiç seçilmemişse katalogdaki ilk mod: seçicinin boş açılması, ilk
            // Play'de "mod seçilmedi" uyarısı demek olurdu.
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

        // -------------------------------------------------------------------- çizim

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
        /// Engel ihlali ölçümünün canlı okuması (yalnız Play kipinde).
        /// <para><b>Neden var:</b> ceza sunucudan gelen bir <c>health_update</c> olarak görünüyor —
        /// "neden hasar alıyorum" sorusunun cevabı oyuncuda değil, ÖLÇÜMDE. Hangi kuralın
        /// tetiklediği ve hangi engelin cevap verdiği görünmedikçe yalancı pozitif ile gerçek ihlal
        /// birbirinden ayırt edilemez.</para>
        /// <para>⚠️ Salt okunurdur ve hiçbir şeyi değiştirmez; ölçümün kendisi
        /// <c>ObstacleViolationProbe</c>'dadır (kendini önyükleyen tekil, bu pencereye bağlı
        /// değildir).</para>
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

            // Ölçüm 20 Hz; pencere olay tabanlı çizildiği için elle tazelenir.
            Repaint();
        }

        private void DrawSelection()
        {
            EditorGUILayout.Space();

            // ---- rol
            EditorGUI.BeginChangeCheck();
            int roleIndex = RadioRow("Rol", RoleLabels,
                DevSession.Role == AppSession.RolePlayer ? 0 : 1, "kısayol: Ctrl+Alt+R");
            if (EditorGUI.EndChangeCheck())
            {
                DevSession.Role = roleIndex == 0 ? AppSession.RolePlayer : AppSession.RoleAdmin;
            }

            // ---- sunucusuz sandbox
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
                    // Sandbox yalnız oynanan bir sahneden Play'de anlamlı: Boot'tan koşulursa
                    // akışı kabuk sahnesi sürer ve LobbyController bağlanmayı dener.
                    DevSession.StartFromBoot = false;
                }
            }

            if (DevSession.Sandbox)
            {
                DrawSandbox();
            }

            // ---- hedef (sandbox'ta bağlanılmadığı için anlamsız)
            using (new EditorGUI.DisabledScope(DevSession.Sandbox))
            {
                DrawTarget();
            }

            // ---- Play başlangıcı
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
        /// Sandbox ayarları — tek seçim: <b>mod</b> (silah loadout'unun okunduğu anahtar).
        /// Silah kaynağı seçilemez; sandbox her zaman loadout'u sırayla ele verir.
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

        /// <summary>Sunucu hedefi seçimi (adlandırılmış hedef ya da "Özel" IP/Port).</summary>
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

        // ---------------------------------------------------------------- yardımcı

        /// <summary>
        /// Etiket + yatay radyo düğmesi satırı. Seçili olmayan bir düğmeye basıldığında yeni
        /// indeks döner (aynı karede iki toggle'ın da true dönmesi tuzağına karşı
        /// <c>index != i</c> koşulu şart).
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

        /// <summary>Kayıtlı hedef adına karşılık gelen popup indeksi; uymuyorsa "Özel" indeksi.</summary>
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
                // "Özel": IP/Port kullanıcıya bırakılır, mevcut değerler korunur.
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
