using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Editor hooks of the dev toolset: Play starts from the right scene, <c>Ctrl+Alt+R</c>
    /// switches the role.
    ///
    /// <para><b>Play start scene:</b> with "start from Boot" selected,
    /// <see cref="EditorSceneManager.playModeStartScene"/> points at Boot so Play always follows the
    /// real flow (Boot → role routing). The asset is looked up <b>from Build Settings</b> by file
    /// name (<c>Boot</c>) rather than a hardcoded path, so moving the scene does not break it.</para>
    ///
    /// <para><b>No process is killed — the server is NEVER touched.</b> It is managed entirely by
    /// hand; the editor must not kill a manually started server at an unexpected moment.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class DevBootstrap
    {
        static DevBootstrap()
        {
            // Remove before add: a domain reload would otherwise double-subscribe.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // ---------------------------------------------------------------- play hooks

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ApplyPlayModeStartScene();
            }
        }

        /// <summary>
        /// Sets <see cref="EditorSceneManager.playModeStartScene"/>: the Boot asset, or <c>null</c>
        /// to start from the open scene.
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
        /// Finds the Build Settings entry named <c>Boot</c> (an enabled entry wins). No hardcoded
        /// path, so moving the scene does not break the tool.
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

        // ---------------------------------------------------------------- shortcut

        /// <summary>
        /// <c>Ctrl+Alt+R</c> — toggles player ↔ admin. ⚠️ The shortcut id stays ASCII: Shortcut
        /// Manager ids are written into the user settings.
        /// </summary>
        [Shortcut("VortexArena/Dev: Rol Degistir", null, KeyCode.R,
            ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        private static void ToggleRole()
        {
            DevSession.Role = DevSession.Role == AppSession.RolePlayer
                ? AppSession.RoleAdmin
                : AppSession.RolePlayer;

            string message = $"Rol: {DevSession.Role}";

            // Visible feedback: scene view notification + console line.
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(message));
            Debug.Log($"[DevBootstrap] {message} (Ctrl+Alt+R). Seçim: {DevSession.Summary}");

            RepaintOpenDevWindows();
        }

        /// <summary>Refreshes the open dev windows; does nothing when no window is open.</summary>
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
