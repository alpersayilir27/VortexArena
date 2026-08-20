using UnityEngine;
using UnityEngine.XR.Management;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Releases XR in the admin role. On standalone XR auto-starts (needed for playing as a player
    /// over Quest Link) and grabs the idle HMD, drawing the admin's spectator camera into the
    /// headset. Stopping + deinitializing the loader frees the session for a player process on the
    /// same PC.
    ///
    /// <para><b>Callers:</b> <c>AppBoot.Start</c> and <c>DevSession.ApplySelection</c>, right after
    /// the role resolves. A no-op without a loader, so a double call is harmless.</para>
    /// </summary>
    public static class AdminXrRelease
    {
        public static void Apply()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                return; // Android is always a player — XR is left alone.
            }

            if (!AppSession.RoleResolved || AppSession.Role != AppSession.RoleAdmin)
            {
                return;
            }

            XRManagerSettings manager = XRGeneralSettings.Instance != null
                ? XRGeneralSettings.Instance.Manager
                : null;

            if (manager == null || manager.activeLoader == null)
            {
                return;
            }

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            Debug.Log("[AdminXrRelease] Rol admin — XR bırakıldı, HMD player sürecine kaldı.");
        }
    }
}
