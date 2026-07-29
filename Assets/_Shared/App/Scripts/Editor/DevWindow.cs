using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Protocol;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Dev</c> — geliştirici kontrol paneli: rol, sunucu hedefi,
    /// Play başlangıcı ve sentetik maç parametreleri.
    ///
    /// <para><b>Sunucu bu pencereden başlatılmaz/durdurulmaz</b> — her zaman elle çalıştırılır
    /// (gerekçe: <see cref="DevProcesses"/> sınıf dokümanı). Buradaki "Derle (dotnet build)"
    /// yalnız derler.</para>
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
        private static readonly string[] TeamLabels = { "Kırmızı", "Mavi" };

        [SerializeField] private Vector2 scroll;

        // Önbellekler — her OnGUI'de asset taraması yapmamak için OnEnable/"Tazele"de kurulur.
        [NonSerialized] private DevTarget[] targetList = Array.Empty<DevTarget>();
        [NonSerialized] private string[] targetLabels = Array.Empty<string>();
        [NonSerialized] private ModeDefinition[] modeDefs = Array.Empty<ModeDefinition>();
        [NonSerialized] private string[] modeIds = Array.Empty<string>();

        [MenuItem("Tools/VortexArena/Dev")]
        private static void Open()
        {
            var window = GetWindow<DevWindow>(false, "Dev", true);
            window.minSize = new Vector2(430f, 400f);
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

            modeDefs = CollectModes();
            modeIds = new string[modeDefs.Length];
            for (int i = 0; i < modeDefs.Length; i++)
            {
                modeIds[i] = modeDefs[i].ModeId;
            }
        }

        /// <summary>Katalogdaki tüm modlar (modId'ye göre Ordinal sıralı). Katalog yoksa boş dizi.</summary>
        private static ModeDefinition[] CollectModes()
        {
            var found = new List<ModeDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:GameCatalog");

            for (int i = 0; i < guids.Length; i++)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (catalog == null || catalog.Modes == null)
                {
                    continue;
                }

                for (int m = 0; m < catalog.Modes.Length; m++)
                {
                    ModeDefinition mode = catalog.Modes[m];
                    if (mode == null || string.IsNullOrWhiteSpace(mode.ModeId) ||
                        found.Exists(existing => existing.ModeId == mode.ModeId))
                    {
                        continue;
                    }

                    found.Add(mode);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.ModeId, b.ModeId));
            return found.ToArray();
        }

        /// <summary>Seçili modun katalog tanımı; katalogda yoksa null.</summary>
        private ModeDefinition SelectedModeDefinition()
        {
            string current = DevSession.ModeId ?? string.Empty;
            for (int i = 0; i < modeDefs.Length; i++)
            {
                if (modeDefs[i].ModeId == current)
                {
                    return modeDefs[i];
                }
            }

            return null;
        }

        /// <summary>
        /// İlk açılışta (hiç hedef seçilmemişken) <c>dev-targets.json</c>'daki varsayılanları
        /// uygular. Sonraki açılışlarda kişisel seçim korunur — "Özel" bile
        /// (<see cref="CustomTargetName"/> sentinel'i sayesinde) sıfırlanmaz.
        /// </summary>
        private void BootstrapSelection()
        {
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
                    "keşif zincirinden gelir, sentetik load_match gönderilmez). Aşağıdaki seçim " +
                    "alanları devre dışı; ortam düğmeleri çalışmaya devam eder.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!injectionEnabled))
            {
                DrawSelection();
            }

            EditorGUILayout.Space();
            DrawEnvironment();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seçim: " + DevSession.Summary, EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
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

            // ---- hedef
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

            // ---- Play başlangıcı
            EditorGUI.BeginChangeCheck();
            int startIndex = RadioRow("Başlangıç", StartLabels, DevSession.StartFromBoot ? 0 : 1, null);
            if (EditorGUI.EndChangeCheck())
            {
                DevSession.StartFromBoot = startIndex == 0;
            }

            // ---- sentetik maç (yalnız "Açık sahneden" iken anlamlı)
            bool matchBlockEnabled = !DevSession.StartFromBoot;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sentetik maç (yalnız \"Açık sahneden\")", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!matchBlockEnabled))
            {
                DrawModeField();
                bool teamless = DrawModeRulesPreview();

                if (matchBlockEnabled && DevProcesses.ActiveArenaSceneName() == null)
                {
                    EditorGUILayout.HelpBox(
                        "Aktif sahne kabuk sahnesi — takım yalnız arena sahnelerinde uygulanır.",
                        MessageType.Info);
                }

                // Takımsız modda takım seçimi anlamsız: sentetik load_match zaten boş takım
                // gönderir (DevSession), radyo düğmesi yanıltıcı olmasın.
                using (new EditorGUI.DisabledScope(teamless))
                {
                    EditorGUI.BeginChangeCheck();
                    int teamIndex = RadioRow("Takım", TeamLabels, DevSession.Team == "blue" ? 1 : 0,
                        teamless ? "takımsız mod — yok sayılır" : null);
                    if (EditorGUI.EndChangeCheck())
                    {
                        DevSession.Team = teamIndex == 1 ? "blue" : "red";
                    }
                }

                EditorGUI.BeginChangeCheck();
                int roundSeconds = EditorGUILayout.IntField("Raund sn", DevSession.RoundSeconds);
                int scoreLimit = EditorGUILayout.IntField("Skor limiti", DevSession.ScoreLimit);
                if (EditorGUI.EndChangeCheck())
                {
                    DevSession.RoundSeconds = roundSeconds;
                    DevSession.ScoreLimit = scoreLimit;
                }
            }
        }

        /// <summary>
        /// Seçili modun ŞEKLİNİ (§10.5) salt okunur gösterir: sentetik maçta kuralları sunucu
        /// göndermediği için <c>ModeRuntime</c> bunları <see cref="ModeDefinition"/>'dan okur ve
        /// editörde koşan davranış budur. <b>Sapmada sunucu kazanır</b> — sunucuda gerçek bir maç
        /// varsa onun <c>load_match.rules</c>'u bu değerleri ezer.
        /// </summary>
        /// <returns>Seçili mod takımsız mı (takım alanı gizlensin mi).</returns>
        private bool DrawModeRulesPreview()
        {
            ModeDefinition mode = SelectedModeDefinition();
            if (mode == null)
            {
                EditorGUILayout.HelpBox(
                    "Mod kuralları katalogda bulunamadı — sentetik maç varsayılan (takımlı TDM) " +
                    "kurallarıyla koşar.",
                    MessageType.None);
                return false;
            }

            bool teamless = mode.TeamMode == ModeTeamMode.None;
            EditorGUILayout.HelpBox(
                "Mod şekli (önizleme — otorite SUNUCUDADIR):\n" +
                $"Takım: {(teamless ? "yok" : "kırmızı/mavi")}   ·   " +
                $"Skor: {(mode.Scoring == ModeScoreKind.Player ? "bireysel" : "takım")}   ·   " +
                $"Dost ateşi: {(mode.FriendlyFire ? "açık" : "kapalı")}\n" +
                $"Canlanma: {(mode.Revive == ModeReviveAnchor.StandStill ? "sabit dur" : "kendi tabanı")}" +
                $" ({mode.RespawnDelay:0.#} sn)   ·   " +
                $"Silah: {(mode.Weapons == ModeWeaponSource.RandomGrant ? "rastgele" : "raf")}",
                MessageType.None);

            return teamless;
        }

        /// <summary>Mod seçimi: katalog varsa popup, yoksa serbest metin (pencere kırılmasın).</summary>
        private void DrawModeField()
        {
            if (modeIds.Length == 0)
            {
                EditorGUI.BeginChangeCheck();
                string typedMode = EditorGUILayout.TextField(
                    new GUIContent("Mod (modId)", "GameCatalog bulunamadı — modId elle yazılıyor"),
                    DevSession.ModeId);
                if (EditorGUI.EndChangeCheck())
                {
                    DevSession.ModeId = typedMode;
                }

                EditorGUILayout.HelpBox(
                    "GameCatalog bulunamadı (t:GameCatalog) — mod listesi çıkarılamadı, modId elle yazılıyor.",
                    MessageType.None);
                return;
            }

            string current = DevSession.ModeId ?? string.Empty;
            int index = Array.IndexOf(modeIds, current);

            // Katalogda olmayan bir modId seçiliyse onu SESSİZCE değiştirmiyoruz: listenin
            // sonuna ekleyip görünür kılıyoruz.
            string[] options = modeIds;
            if (index < 0)
            {
                options = new string[modeIds.Length + 1];
                Array.Copy(modeIds, options, modeIds.Length);
                options[modeIds.Length] = string.IsNullOrEmpty(current) ? "(boş)" : current + " (katalogda yok)";
                index = modeIds.Length;
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(new GUIContent("Mod", "GameCatalog'daki ModeDefinition'lar"),
                index, options);
            if (EditorGUI.EndChangeCheck() && picked < modeIds.Length)
            {
                DevSession.ModeId = modeIds[picked];
            }
        }

        /// <summary>
        /// Ortam bölümü — <b>yalnız sunucu çözümünü derler</b>. Sunucu bilinçli olarak buradan
        /// başlatılmaz/durdurulmaz: elle yönetilir (bkz. <see cref="DevProcesses"/> sınıf dokümanı).
        /// </summary>
        private void DrawEnvironment()
        {
            EditorGUILayout.LabelField("Ortam", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Sunucu editörden başlatılmaz/durdurulmaz — elle çalıştırın:\n" +
                "deploy\\server\\VortexArena.Server.App.exe  (ya da: " +
                "dotnet run --project Server/VortexArena.Server.App)",
                MessageType.Info);

            if (GUILayout.Button("Derle (dotnet build)"))
            {
                DevProcesses.BuildSolution();
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
