using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// Authoring-tool Play flag (e.g. grip capture): while it is on nothing connects or discovers and
    /// no game singleton installs. Raised before scene load by the editor dev hooks and again by the
    /// tool scene's <see cref="IAuthoringSession"/> root in Awake.
    /// </summary>
    public static class AuthoringSession
    {
        /// <summary>
        /// <c>SessionState</c> key the editor writes on ExitingEditMode — survives the domain reload,
        /// so BeforeSceneLoad code can read it before any scene Awake.
        /// </summary>
        public const string EditorStateKey = "VortexArena.AuthoringSession";

        /// <summary>True while this Play session is an authoring tool, not a game session.</summary>
        public static bool Active;

        // Statics outlive Play when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = false;
        }
    }
}
