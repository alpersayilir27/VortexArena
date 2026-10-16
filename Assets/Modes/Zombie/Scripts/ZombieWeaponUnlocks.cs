using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Wave-by-wave weapon progression on the zombie boards: installs the selection gate while
    /// the running mode is zombie, dresses every board slot with a <see cref="ZombieWeaponLockView"/>
    /// and names each wave's new weapons for the HUD's clear-phase notice.</summary>
    /// <remarks>⚠️ Entirely CLIENT-side: the server validates no weapon (§10.5), so the progression adds
    /// no message and no protocol version.
    /// <para>ONE static installer for the whole mode (the <see cref="ZombieRoster"/> pattern): the gate
    /// is a single static on <see cref="WeaponGranter"/> and the wave is a single static on
    /// <see cref="ZombieWaveState"/> — a per-board listener would be the same answer computed once per
    /// board.</para></remarks>
    internal static class ZombieWeaponUnlocks
    {
        /// <summary>Wave the plates were last opened for; the unlock animation must not repeat on every
        /// <c>match_state</c>.</summary>
        private static int _announcedWave;

        private static bool _active;

        /// <summary>Board weapons outside the zombie roster, hidden by <see cref="BuildViews"/> and shown
        /// again by <see cref="DestroyViews"/>.</summary>
        private static readonly List<GameObject> HiddenOffRoster = new List<GameObject>();

        private static readonly List<string> NewNames = new List<string>();
        private static readonly StringBuilder Joined = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Static handler on static events: nothing to unsubscribe, and the gate outlives scene
            // loads exactly like the match it belongs to.
            ModeRuntime.Changed += Refresh;

            // The staged arena is announced by the SELECTION, not by the rules: without this the
            // boards stay open until start_match.
            ModeSelection.Changed += Refresh;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ZombieWaveState.UnlockWaveChanged += HandleUnlockWaveChanged;
            ZombieWaveState.FinalStandChanged += HandleFinalStandChanged;
            // Re-judged, not stopped: the squad usually returns to the lobby INSIDE the same zombie
            // arena, where the boards must go back to wave 1 instead of opening up.
            NetEvents.OnReturnToLobby += _ => Refresh();
            NetEvents.OnDisconnected += Stop;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        /// <summary>Is the progression this machine's business: a zombie arena underfoot, and a headset
        /// (the admin has no boards to look at and no HUD to toast on).</summary>
        private static bool ShouldRun
        {
            get
            {
                if (!IsZombieHere)
                {
                    return false;
                }

                ArenaClient client = ArenaClient.Instance;
                return client == null || !client.IsAdminRole;
            }
        }

        /// <summary>Zombie rules are running, OR a zombie arena is merely STAGED.</summary>
        /// <remarks>⚠️ The second branch is what locks the boards BEFORE the match: while the arena is
        /// staged (the in-arena wait before start_match) the running rule is still the lobby profile
        /// (§10.7), so judging by the rules alone handed the squad every weapon until the run began.
        /// <para>⚠️ The scene's spawn markers are the "which arena" half: a zombie selection made while
        /// the squad waits in the real lobby scene must lock nothing there.</para></remarks>
        private static bool IsZombieHere
        {
            get
            {
                if (string.Equals(ModeRuntime.ModeId, ZombieKinds.Zombie, StringComparison.Ordinal))
                {
                    return true;
                }

                return string.Equals(ModeSelection.ModeId, ZombieKinds.Zombie, StringComparison.Ordinal) &&
                       UnityEngine.Object.FindFirstObjectByType<ZombieSpawnPoint>(
                           FindObjectsInactive.Include) != null;
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
            WristHolster.SelectionGate = IsThrowableUnlocked;

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

            if (WristHolster.SelectionGate == (Func<ThrowableDefinition, bool>)IsThrowableUnlocked)
            {
                WristHolster.SelectionGate = null;
            }

            DestroyViews();
        }

        /// <summary>Wave the locks are judged by: the wave gap already counts as the NEXT wave, because
        /// the gap is when the squad swaps weapons (<see cref="ZombieWaveState.UnlockWave"/>).</summary>
        private static int CurrentWave => ZombieWaveState.UnlockWave;

        private static bool IsUnlocked(WeaponDefinition definition)
        {
            return definition == null || IsOpen(definition.WeaponId);
        }

        /// <summary>The wrist bomb rides the same table by its <c>throwableId</c>.</summary>
        private static bool IsThrowableUnlocked(ThrowableDefinition definition)
        {
            return definition == null || IsOpen(definition.ThrowableId);
        }

        /// <summary>Listed in the roster AND its wave reached; the final stand (§10.5) opens every
        /// LISTED weapon — an unlisted one never opens.</summary>
        private static bool IsOpen(string id)
        {
            ZombieArsenal arsenal = ZombieArsenal.Live;
            return arsenal.IsListed(id) &&
                   (ZombieWaveState.FinalStand || arsenal.UnlockWave(id) <= CurrentWave);
        }

        /// <summary>Final stand: every locked slot opens at once. No per-weapon toast — the HUD's
        /// "SON DİRENİŞ" notice already says it, and a dozen names would bury it.</summary>
        private static void HandleFinalStandChanged()
        {
            if (!_active)
            {
                return;
            }

            // The final stand ENDS with the run: the covers go straight back on, or the next run
            // starts with the whole arsenal open.
            if (!ZombieWaveState.FinalStand)
            {
                _announcedWave = CurrentWave;
                ApplyAll(_announcedWave);
                return;
            }

            IReadOnlyList<ZombieWeaponLockView> views = ZombieWeaponLockView.All;
            for (int i = 0; i < views.Count; i++)
            {
                ZombieWeaponLockView view = views[i];
                if (view != null && view.UnlockWave > CurrentWave)
                {
                    view.PlayUnlock();
                }
            }
        }

        // -------------------------------------------------------------------- waves

        private static void HandleUnlockWaveChanged(int wave)
        {
            if (!_active)
            {
                return;
            }

            int current = Mathf.Max(1, wave);
            if (current <= _announcedWave)
            {
                // Wave went back (a new run on the same arena): re-lock without animating anything.
                _announcedWave = current;
                ApplyAll(current);
                return;
            }

            // Everything opened between the last announced wave and this one — a dropped match_state
            // must not swallow a plate's unlock.
            IReadOnlyList<ZombieWeaponLockView> views = ZombieWeaponLockView.All;
            for (int i = 0; i < views.Count; i++)
            {
                ZombieWeaponLockView view = views[i];
                if (view != null && view.UnlockWave > _announcedWave && view.UnlockWave <= current)
                {
                    view.PlayUnlock();
                }
            }

            _announcedWave = current;
            ApplyAll(current);
        }

        /// <summary>Display names of everything that opens EXACTLY at <paramref name="wave"/>, joined
        /// with <c>" · "</c>; empty when that wave opens nothing. The HUD's clear-phase notice reads it.</summary>
        /// <remarks>Looked up on demand (once per wave gap), so no cached holster or board reference can
        /// go stale. Two boards can carry the same weapon (one per playable storey) — it is named once.</remarks>
        internal static string UnlockedNamesAt(int wave)
        {
            NewNames.Clear();

            IReadOnlyList<ZombieWeaponLockView> views = ZombieWeaponLockView.All;
            for (int i = 0; i < views.Count; i++)
            {
                ZombieWeaponLockView view = views[i];
                if (view == null || view.UnlockWave != wave)
                {
                    continue;
                }

                string label = view.DisplayName;
                if (!string.IsNullOrEmpty(label) && !NewNames.Contains(label))
                {
                    NewNames.Add(label);
                }
            }

            AddThrowableAt(wave);

            Joined.Clear();
            for (int i = 0; i < NewNames.Count; i++)
            {
                if (i > 0)
                {
                    Joined.Append(" · ");
                }

                Joined.Append(NewNames[i]);
            }

            return Joined.ToString();
        }

        /// <summary>Adds the wrist bomb's name when it opens exactly at <paramref name="wave"/>.</summary>
        private static void AddThrowableAt(int wave)
        {
            WristHolster holster = UnityEngine.Object.FindFirstObjectByType<WristHolster>();
            ThrowableDefinition throwable = holster != null ? holster.Throwable : null;
            if (throwable == null)
            {
                return;
            }

            string label = Girdap.Upper(throwable.DisplayName);
            if (ZombieArsenal.Live.UnlockWave(throwable.ThrowableId) == wave &&
                !string.IsNullOrEmpty(label) && !NewNames.Contains(label))
            {
                NewNames.Add(label);
            }
        }

        /// <summary>Pushes the locked/open state onto every view WITHOUT animation (the views that just
        /// opened are mid-animation and ignore it, because their own wave is already reached).</summary>
        private static void ApplyAll(int wave)
        {
            // ⚠️ Nothing is re-locked during the final stand: a scene load or a late join there would
            // otherwise put the covers back on an arsenal the squad already has.
            if (ZombieWaveState.FinalStand)
            {
                return;
            }

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

                // Off-roster slot: hidden, not locked — a plate would promise a wave that never comes.
                if (weapon.Definition != null && !ZombieArsenal.Live.IsListed(weapon.Definition.WeaponId))
                {
                    if (weapon.gameObject.activeSelf)
                    {
                        weapon.gameObject.SetActive(false);
                        HiddenOffRoster.Add(weapon.gameObject);
                    }

                    continue;
                }

                weapon.gameObject.AddComponent<ZombieWeaponLockView>().Bind(frame);
            }
        }

        private static void DestroyViews()
        {
            for (int i = 0; i < HiddenOffRoster.Count; i++)
            {
                if (HiddenOffRoster[i] != null)
                {
                    HiddenOffRoster[i].SetActive(true);
                }
            }

            HiddenOffRoster.Clear();

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
