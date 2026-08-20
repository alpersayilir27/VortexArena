using UnityEngine;

namespace VortexArena.App
{
    /// <summary>
    /// <b>SINGLE install point</b> for the session-long App singletons.
    ///
    /// <para>
    /// <b>Why one place:</b> these singletons spawn themselves (<c>DontDestroyOnLoad</c>), not from
    /// the scene. With per-class <c>RuntimeInitializeOnLoadMethod</c>s, "which ones does this
    /// session need" would be scattered over N files and a missed one would fail silently. Here a
    /// new session type is a <b>single-line</b> condition.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>The singletons' own <c>Install</c>s stay unconditional</b> — if they answer "am I
    /// needed", the gate scatters again. Only a singleton's own <i>existence</i> condition is
    /// exempt (e.g. <c>AdminSpectator</c> never spawns on Android: a platform fact, not a session
    /// decision).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Order is IRRELEVANT and must stay so:</b> singletons subscribe to events instead of
    /// calling each other during <c>Install</c>; a spawn-order dependency would become a hidden
    /// rule here.
    /// </para>
    ///
    /// <para>
    /// <b>Scope:</b> only <c>VortexArena.App</c> singletons — Core's own ones do NOT belong here
    /// (Core does not reference App and does not know the role).
    /// </para>
    ///
    /// <para>
    /// Every session type installs the same list today, hence the unconditional gate; a future
    /// server-less type adds its condition <b>here</b>, not in the singletons. The role is certain
    /// at this point (<c>DevSession</c>/<c>AppBoot</c> write it in <c>BeforeSceneLoad</c>).
    /// </para>
    /// </summary>
    internal static class AppSingletons
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            InstallNetworkSingletons();
        }

        /// <summary>
        /// Singletons of a server-connected session: connection/loading/match-result cards,
        /// kick shutdown, scene routing, admin spectator.
        /// </summary>
        private static void InstallNetworkSingletons()
        {
            ConnectionOverlay.Install();
            LoadingOverlay.Install();
            MatchResultOverlay.Install();
            KickedShutdown.Install();
            SceneRouter.Install();
            Admin.AdminSpectator.Install();
        }
    }
}
