using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Dev araç setinin editör kancaları: Play'e basıldığında doğru sahneden başlatır ve rol
    /// değiştirmek için <c>Ctrl+Alt+R</c> kısayolunu kurar.
    ///
    /// <para><b>Play başlangıç sahnesi:</b> "Boot'tan başla" seçiliyken
    /// <see cref="EditorSceneManager.playModeStartScene"/> Boot sahnesine ayarlanır — böylece
    /// hangi sahne açık olursa olsun Play gerçek akıştan (Boot → rol yönlendirmesi) başlar.
    /// Sahne asset'i <b>Build Settings'ten</b> aranır (dosya adı <c>Boot</c>); sabit yol gömmüyoruz
    /// ki sahne taşındığında araç kırılmasın.</para>
    ///
    /// <para><b>Hiçbir süreç öldürülmez — sunucuya HİÇ dokunulmaz.</b> Sunucu üretimde de ayrı
    /// bir makinede sürekli açık durur ve bu projede <b>tamamen elle</b> yönetilir: editör
    /// sunucuyu ne başlatır ne durdurur, editör kapanırken de öldürmez. Aksi hâlde elle
    /// başlatılmış bir sunucu beklenmedik anda ölürdü.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class DevBootstrap
    {
        static DevBootstrap()
        {
            // Domain reload sonrası çift abonelik olmasın diye önce çıkarıp sonra ekliyoruz.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // ------------------------------------------------------------- play kancaları

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ApplyPlayModeStartScene();
            }
        }

        /// <summary>
        /// Seçime göre <see cref="EditorSceneManager.playModeStartScene"/> ayarlar: Boot'tan
        /// başlanacaksa Boot sahne asset'i, aksi hâlde <c>null</c> (açık sahneden başla).
        /// </summary>
        private static void ApplyPlayModeStartScene()
        {
            if (!DevSession.Enabled || !DevSession.StartFromBoot)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            SceneAsset boot = FindBootSceneAsset();
            if (boot == null)
            {
                Debug.LogWarning(
                    $"[DevBootstrap] Build Settings'te '{AppSession.SceneBoot}' adlı sahne " +
                    "bulunamadı — Play açık sahneden başlayacak. Boot sahnesini Build Settings'e " +
                    "ekleyin ya da dev penceresinde \"Açık sahneden\" seçin.");
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            EditorSceneManager.playModeStartScene = boot;
        }

        /// <summary>
        /// Build Settings girdileri içinde dosya adı <c>Boot</c> olanı bulur (açık girdi
        /// önceliklidir). Sabit yol gömülmez — sahne taşınırsa araç kırılmasın.
        /// </summary>
        private static SceneAsset FindBootSceneAsset()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            string fallbackPath = null;

            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene entry = scenes[i];
                if (entry == null || string.IsNullOrEmpty(entry.path))
                {
                    continue;
                }

                if (!string.Equals(Path.GetFileNameWithoutExtension(entry.path), AppSession.SceneBoot,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.enabled)
                {
                    return AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path);
                }

                fallbackPath ??= entry.path;
            }

            return fallbackPath != null ? AssetDatabase.LoadAssetAtPath<SceneAsset>(fallbackPath) : null;
        }

        // ----------------------------------------------------------------- kısayol

        /// <summary>
        /// <c>Ctrl+Alt+R</c> — rolü player ↔ admin arasında çevirir. Kısayol kimliği ASCII
        /// tutulur (Shortcut Manager kimlikleri kullanıcı ayarlarına yazılır).
        /// </summary>
        [Shortcut("VortexArena/Dev: Rol Degistir", null, KeyCode.R,
            ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        private static void ToggleRole()
        {
            DevSession.Role = DevSession.Role == AppSession.RolePlayer
                ? AppSession.RoleAdmin
                : AppSession.RolePlayer;

            string message = $"Rol: {DevSession.Role}";

            // Görünür geri bildirim: sahne görünümünde bildirim + konsol satırı (kısayola
            // basıldığı konsoldan da anlaşılsın).
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(message));
            Debug.Log($"[DevBootstrap] {message} (Ctrl+Alt+R). Seçim: {DevSession.Summary}");

            RepaintOpenDevWindows();
        }

        /// <summary>Açık dev pencerelerini tazeler; pencere kapalıysa hiçbir şey yapmaz.</summary>
        private static void RepaintOpenDevWindows()
        {
            DevWindow[] windows = Resources.FindObjectsOfTypeAll<DevWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                {
                    windows[i].Repaint();
                }
            }
        }
    }
}
