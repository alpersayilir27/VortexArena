using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Dev</c> — geliştirici kontrol paneli: rol, sunucu hedefi ve
    /// Play başlangıcı.
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

        [MenuItem("Tools/VortexArena/Dev")]
        private static void Open()
        {
            var window = GetWindow<DevWindow>(false, "Dev", true);
            window.minSize = new Vector2(430f, 260f);
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
                    "keşif zincirinden gelir). Aşağıdaki seçim alanları devre dışı.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!injectionEnabled))
            {
                DrawSelection();
            }

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
