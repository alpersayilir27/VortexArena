using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Dev araç setinin editör kancaları: Play'e basıldığında doğru sahneden başlatır, Play
    /// çıkışında test botlarını toplar, editör kapanırken her şeyi öldürür ve rol değiştirmek
    /// için <c>Ctrl+Alt+R</c> kısayolunu kurar.
    ///
    /// <para><b>Play başlangıç sahnesi:</b> "Boot'tan başla" seçiliyken
    /// <see cref="EditorSceneManager.playModeStartScene"/> Boot sahnesine ayarlanır — böylece
    /// hangi sahne açık olursa olsun Play gerçek akıştan (Boot → rol yönlendirmesi) başlar.
    /// Sahne asset'i <b>Build Settings'ten</b> aranır (dosya adı <c>Boot</c>); sabit yol gömmüyoruz
    /// ki sahne taşındığında araç kırılmasın.</para>
    ///
    /// <para><b>Play çıkışında yalnız BOTLAR ölür, sunucu KASITLI olarak yaşamaya devam eder.</b>
    /// Botlar teste özgüdür (her Play kendi sentetik oyuncularını doğurur), oysa sunucu üretimde
    /// de ayrı bir makinede sürekli açık durur. Her Play çıkışında sunucuyu öldürmek geliştiriciyi
    /// yorar (yeniden başlat + yeniden bağlan + roster'ı bekle) ve sunucunun uzun ömürlü olduğu
    /// gerçek topolojiden uzaklaşır. Sunucuyu bilinçli kapatmak için pencerede "Durdur" /
    /// "Hepsini Durdur" var; editör kapanırken de <see cref="DevProcesses.StopAll"/> çağrılır.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class DevBootstrap
    {
        static DevBootstrap()
        {
            // Domain reload sonrası çift abonelik olmasın diye önce çıkarıp sonra ekliyoruz.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
        }

        // ------------------------------------------------------------- play kancaları

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    ApplyPlayModeStartScene();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // Botlar teste özgü: her Play sonunda ölürler. Sunucu bilinçli olarak yaşar
                    // (sınıf dokümanındaki gerekçe).
                    DevProcesses.StopBots();
                    break;
            }
        }

        private static void OnEditorQuitting()
        {
            DevProcesses.StopAll();
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
