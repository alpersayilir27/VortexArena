using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Installs the zombie mode's hit feel over the shared hooks while zombie rules run on a
    /// headset, and takes it all back down otherwise.</summary>
    /// <remarks>⚠️ Lifecycle mirrors <see cref="ZombieDamageTuning"/>: one static installer, each hook
    /// cleared ONLY while it is still ours — clearing blindly would cancel another mode's.
    /// <para>⚠️ Admin excluded: it fires nothing, holds no controller and has no rig to buzz.</para>
    /// <para>While nothing is installed the shared path is byte-for-byte the PvP one: the weapon's own
    /// kick and pulse, the default hit marker X.</para></remarks>
    internal static class ZombieFeelInstaller
    {
        private static bool active;
        private static ZombieWeaponFeel feel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            ModeRuntime.Changed += Refresh;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            NetEvents.OnReturnToLobby += _ => Stop();
            NetEvents.OnDisconnected += Stop;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        /// <summary>Zombie rules on a headset; the admin needs none of this.</summary>
        private static bool ShouldRun
        {
            get
            {
                if (!string.Equals(ModeRuntime.ModeId, ZombieKinds.Zombie, StringComparison.Ordinal))
                {
                    return false;
                }

                ArenaClient client = ArenaClient.Instance;
                return client == null || !client.IsAdminRole;
            }
        }

        private static void Refresh()
        {
            if (!ShouldRun)
            {
                Stop();
                return;
            }

            active = true;

            // The hook object outlives scene loads: a new one would drop every weapon's half-recovered
            // spring state. Refresh only re-asserts the hooks (idempotent).
            if (feel == null)
            {
                feel = new ZombieWeaponFeel();
            }

            ZombieWeaponFeel.Installed = feel;
            Weapon.FeelOverride = feel;

            // Runner FIRST: the marker hook answers "ours" only while the feedback component is live,
            // so installing the hook before it would hand a few frames back to the default X.
            ZombieFeelRunner.Ensure();
            ArenaCombat.ObjectHitMarkerOverride = ZombieHitFeedback.MarkerHook;
        }

        private static void Stop()
        {
            if (!active)
            {
                return;
            }

            active = false;

            if (ReferenceEquals(Weapon.FeelOverride, feel))
            {
                Weapon.FeelOverride = null;
            }

            if (ArenaCombat.ObjectHitMarkerOverride == (Func<ArenaCombat.ObjectHitInfo, bool>)
                ZombieHitFeedback.HandleMarker)
            {
                ArenaCombat.ObjectHitMarkerOverride = null;
            }

            if (ZombieWeaponFeel.Installed == feel)
            {
                ZombieWeaponFeel.Installed = null;
            }

            feel = null;

            // Stops any clip still running on a controller: without this a cue cut by the mode ending
            // keeps the controller buzzing until it finishes on its own.
            ZombieFeelRunner.Release();
        }
    }
}
