using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Wave-by-wave weapon progression on the zombie boards: installs the selection gate while
    /// the running mode is zombie, dresses every board slot with a <see cref="ZombieWeaponLockView"/>
    /// and announces each wave's new weapons on the HUD.</summary>
    /// <remarks>⚠️ Entirely CLIENT-side: the server validates no weapon (§10.5), so the progression adds
    /// no message and no protocol version.
    /// <para>ONE static installer for the whole mode (the <see cref="ZombieRoster"/> pattern): the gate
    /// is a single static on <see cref="WeaponGranter"/> and the wave is a single static on
    /// <see cref="ZombieWaveState"/> — a per-board listener would be the same answer computed once per
    /// board.</para></remarks>
    internal static class ZombieWeaponUnlocks
    {
        /// <summary>Waves already announced; the toast must not repeat on every <c>match_state</c>.</summary>
        private static int _announcedWave;

        private static bool _active;

        private static readonly List<string> NewNames = new List<string>();
        private static readonly StringBuilder Joined = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Static handler on static events: nothing to unsubscribe, and the gate outlives scene
            // loads exactly like the match it belongs to.
            ModeRuntime.Changed += Refresh;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ZombieWaveState.WaveChanged += HandleWaveChanged;
            NetEvents.OnReturnToLobby += _ => Stop();
            NetEvents.OnDisconnected += Stop;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        /// <summary>Is the progression this machine's business: zombie rules, and a headset (the admin
        /// has no boards to look at and no HUD to toast on).</summary>
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
            WeaponGranter.SelectionGate = IsUnlocked;

            BuildViews();

            // The gate and the plates must agree the moment the arena appears; a late join can arrive
            // mid-run, so the CURRENT wave is applied rather than wave 1.
            _announcedWave = CurrentWave;
            ApplyAll(_announcedWave);
        }

        private static void Stop()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _announcedWave = 0;

            // ⚠️ Only OUR gate is dropped: clearing it blindly would silently cancel another mode's.
            if (WeaponGranter.SelectionGate == (Func<WeaponDefinition, bool>)IsUnlocked)
            {
                WeaponGranter.SelectionGate = null;
            }

            DestroyViews();
        }

        /// <summary>Wave the locks are judged by — the pre-start countdown (<c>0</c>) counts as wave 1,
        /// so the starter weapons are takeable while the players are still being placed.</summary>
        private static int CurrentWave => Mathf.Max(1, ZombieWaveState.Wave);

        private static bool IsUnlocked(WeaponDefinition definition)
        {
            return definition == null ||
                   ZombieArsenal.Live.UnlockWave(definition.WeaponId) <= CurrentWave;
        }

        // -------------------------------------------------------------------- waves

        private static void HandleWaveChanged(int wave)
        {
            if (!_active)
            {
                return;
            }

            int current = Mathf.Max(1, wave);
            if (current <= _announcedWave)
            {
                // Wave went back (a new run on the same arena): re-lock without announcing anything.
                _announcedWave = current;
                ApplyAll(current);
                return;
            }

            // Everything opened between the last announced wave and this one — a dropped match_state
            // must not swallow a weapon's announcement.
            NewNames.Clear();
            IReadOnlyList<ZombieWeaponLockView> views = ZombieWeaponLockView.All;
            for (int i = 0; i < views.Count; i++)
            {
                ZombieWeaponLockView view = views[i];
                if (view == null || view.UnlockWave <= _announcedWave || view.UnlockWave > current)
                {
                    continue;
                }

                view.PlayUnlock();

                // Two boards can carry the same weapon (one per playable storey) — the toast says it once.
                string label = view.DisplayName;
                if (!string.IsNullOrEmpty(label) && !NewNames.Contains(label))
                {
                    NewNames.Add(label);
                }
            }

            _announcedWave = current;

            // Wave 1 opens nothing: those weapons were never locked.
            if (current >= 2 && NewNames.Count > 0)
            {
                Announce();
            }

            ApplyAll(current);
        }

        private static void Announce()
        {
            ZombieClientController hud = ZombieClientController.Live;
            if (hud == null)
            {
                return;
            }

            Joined.Clear();
            for (int i = 0; i < NewNames.Count; i++)
            {
                if (i > 0)
                {
                    Joined.Append(" · ");
                }

                Joined.Append(NewNames[i]);
            }

            hud.ShowWeaponUnlockedToast(Joined.ToString());
        }

        /// <summary>Pushes the locked/open state onto every view WITHOUT animation (the views that just
        /// opened are mid-animation and ignore it, because their own wave is already reached).</summary>
        private static void ApplyAll(int wave)
        {
            IReadOnlyList<ZombieWeaponLockView> views = ZombieWeaponLockView.All;
            for (int i = 0; i < views.Count; i++)
            {
                ZombieWeaponLockView view = views[i];
                if (view != null && view.UnlockWave > wave)
                {
                    view.Apply(wave);
                }
            }
        }

        // -------------------------------------------------------------------- views

        /// <summary>Dresses every board slot of the loaded scenes. ⚠️ Scanned ONCE per scene load / rule
        /// change, not per frame and not per board: the scan is the expensive part, the components are
        /// not.</summary>
        private static void BuildViews()
        {
            WeaponFrame[] frames = UnityEngine.Object.FindObjectsByType<WeaponFrame>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < frames.Length; i++)
            {
                WeaponFrame frame = frames[i];
                if (frame == null)
                {
                    continue;
                }

                // ⚠️ The frame is a CHILD of the weapon and hides itself while the weapon is held; the
                // view belongs to the weapon, which stays where the board put it.
                // includeInactive: a frame hides its own GameObject while the weapon is held.
                Weapon weapon = frame.GetComponentInParent<Weapon>(true);
                if (weapon == null || weapon.GetComponent<ZombieWeaponLockView>() != null)
                {
                    continue;
                }

                weapon.gameObject.AddComponent<ZombieWeaponLockView>().Bind(frame);
            }
        }

        private static void DestroyViews()
        {
            IReadOnlyList<ZombieWeaponLockView> views = ZombieWeaponLockView.All;
            for (int i = views.Count - 1; i >= 0; i--)
            {
                ZombieWeaponLockView view = views[i];
                if (view != null)
                {
                    view.Teardown();
                }
            }
        }
    }
}
