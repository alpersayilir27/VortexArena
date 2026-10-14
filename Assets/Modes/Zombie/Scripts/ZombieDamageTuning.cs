using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Installs the zombie mode's OWN weapon damage (<see cref="ZombieArsenal"/> rows) over
    /// <see cref="Weapon.DamageOverride"/> while zombie rules are running, and drops it otherwise.</summary>
    /// <remarks>⚠️ Separate from the PvP table on purpose: zombies are 100 HP sacks shot for a whole
    /// match, PvP damage is tuned against players — one shared number cannot serve both, and editing
    /// <c>WD_*.asset</c> for the mode would retune PvP silently.
    /// <para>⚠️ Client-side only (§10.5): damage already travels in <c>hit_report.damage</c> and the
    /// server applies it verbatim, so no message and no server deploy change.</para>
    /// <para>The lifecycle mirrors <see cref="ZombieWeaponUnlocks"/>: one static installer, the hook
    /// cleared only while it is still ours.</para></remarks>
    internal static class ZombieDamageTuning
    {
        private static bool _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            ModeRuntime.Changed += Refresh;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            NetEvents.OnReturnToLobby += _ => Stop();
            NetEvents.OnDisconnected += Stop;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        /// <summary>Zombie rules on a headset: the admin fires nothing, so it needs no hook.</summary>
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

            _active = true;
            Weapon.DamageOverride = Resolve;
        }

        private static void Stop()
        {
            if (!_active)
            {
                return;
            }

            _active = false;

            // ⚠️ Only OUR hook is dropped: clearing it blindly would cancel another mode's.
            if (Weapon.DamageOverride == (Func<WeaponDefinition, HitZone, float, float>)Resolve)
            {
                Weapon.DamageOverride = null;
            }
        }

        /// <summary>Damage of one hit (one PELLET on a shotgun). A row that overrides only the body
        /// number keeps the weapon's own stomach/leg ratios, applied to the new body damage.</summary>
        private static float Resolve(WeaponDefinition definition, HitZone zone, float defaultDamage)
        {
            if (definition == null ||
                !ZombieArsenal.Live.TryGetDamage(definition.WeaponId, out float body, out float head))
            {
                return defaultDamage;
            }

            if (body <= 0f)
            {
                body = definition.Damage;
            }

            if (zone == HitZone.Head)
            {
                return body * (head > 0f ? head : definition.HeadshotMultiplier);
            }

            return body * definition.GetZoneMultiplier(zone);
        }
    }
}
