using System;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The shooter's confirmation: hit tick, headshot crunch, kill confirm, hit marker variants
    /// and the matching haptic cues.</summary>
    /// <remarks>⚠️ Aggregated PER FRAME PER ZOMBIE: one shotgun trigger pull reports up to nine pellets
    /// on the same body, and one tick per pellet is a burst of noise plus nine stacked markers.
    /// <para>⚠️ The marker override only CLAIMS the default X (returns true); the mark itself is drawn
    /// in the flush, so the aggregation decides body vs head vs kill once.</para></remarks>
    internal sealed class ZombieHitFeedback : MonoBehaviour
    {
        // Distinct zombies one frame can confirm; a shotgun fan rarely spans more.
        private const int Capacity = 8;

        private static ZombieHitFeedback instance;

        private readonly PendingHit[] pending = new PendingHit[Capacity];
        private int pendingCount;

        private struct PendingHit
        {
            public int NetId;
            public bool Head;
            public Vector3 Point;

            /// <summary>A kill presentation took this zombie over: its own marker and sound win.</summary>
            public bool Killed;
        }

        /// <summary>Marker hook body (<see cref="ArenaCombat.ObjectHitMarkerOverride"/>): <c>true</c> =
        /// a zombie, the default X is skipped and the flush draws our variant.</summary>
        public static bool HandleMarker(ArenaCombat.ObjectHitInfo info)
        {
            return instance != null && info.Local && ZombieFeelEvents.TryGet(info.NetId, out _);
        }

        /// <summary>The delegate the installer compares against before clearing the hook.</summary>
        public static Func<ArenaCombat.ObjectHitInfo, bool> MarkerHook => HandleMarker;

        private void OnEnable()
        {
            instance = this;
            ArenaCombat.ObjectHit += HandleObjectHit;
            ZombieFeelEvents.KillPresented += HandleKill;
            ZombieFeelEvents.Dismembered += HandleDismember;
        }

        private void OnDisable()
        {
            ArenaCombat.ObjectHit -= HandleObjectHit;
            ZombieFeelEvents.KillPresented -= HandleKill;
            ZombieFeelEvents.Dismembered -= HandleDismember;

            pendingCount = 0;

            if (instance == this)
            {
                instance = null;
            }
        }

        private void HandleObjectHit(ArenaCombat.ObjectHitInfo info)
        {
            // Relayed hits belong to someone else's screen: Local is the only "I hit it" signal.
            if (!info.Local || !ZombieFeelEvents.TryGet(info.NetId, out _))
            {
                return;
            }

            bool head = info.Zone == (int)HitZone.Head;

            for (int i = 0; i < pendingCount; i++)
            {
                if (pending[i].NetId != info.NetId)
                {
                    continue;
                }

                // Any pellet in the head makes the whole trigger pull read as a head hit.
                pending[i].Head |= head;
                return;
            }

            if (pendingCount >= Capacity)
            {
                return;
            }

            pending[pendingCount].NetId = info.NetId;
            pending[pendingCount].Head = head;
            pending[pendingCount].Point = info.Point;
            pending[pendingCount].Killed = false;
            pendingCount++;
        }

        // Flushed in LateUpdate: the hits are reported from Weapon.Update, so one frame holds the
        // whole trigger pull.
        private void LateUpdate()
        {
            if (pendingCount == 0)
            {
                return;
            }

            ZombieFeel feel = ZombieFeel.Live;
            ZombieFeelAudio pool = ZombieFeelRunner.Audio;
            bool hasHand = TryGetFiringHand(out Oculus.Haptics.Controller hand);

            for (int i = 0; i < pendingCount; i++)
            {
                if (pending[i].Killed)
                {
                    continue;
                }

                bool head = pending[i].Head;

                HitMarker.Shared.PlayVariant(pending[i].Point,
                    head ? feel.HeadMarkerColor : feel.BodyMarkerColor,
                    head ? feel.HeadMarkerSize : feel.BodyMarkerSize);

                pool?.Play(head ? feel.HeadshotCrunch : feel.HitTick,
                    head ? feel.HeadshotCrunchVolume : feel.HitTickVolume, feel.HitPitchJitter);

                if (hasHand)
                {
                    // Delayed so the fire clip (higher priority on the same hand) has ended.
                    ZombieHaptics.Schedule(head ? ZombieHapticEvent.HitHead : ZombieHapticEvent.HitConfirm,
                        hand, feel.HitConfirmDelaySeconds);
                }
            }

            pendingCount = 0;
        }

        private void HandleKill(ZombieKillPresentation info)
        {
            if (!info.ByLocalPlayer)
            {
                return;
            }

            ZombieFeel feel = ZombieFeel.Live;

            HitMarker.Shared.PlayVariant(info.Point, feel.KillMarkerColor, feel.KillMarkerSize,
                feel.KillMarkerLifetime);
            ZombieFeelRunner.Audio?.Play(feel.KillConfirm, feel.KillConfirmVolume, feel.HitPitchJitter);

            if (TryGetFiringHand(out Oculus.Haptics.Controller hand))
            {
                ZombieHaptics.Play(ZombieHapticEvent.Kill, hand);
            }

            // The killing pellet's own tick would land on top of the kill confirm in the same frame.
            for (int i = 0; i < pendingCount; i++)
            {
                if (pending[i].NetId == info.NetId)
                {
                    pending[i].Killed = true;
                    return;
                }
            }
        }

        private void HandleDismember(ZombieDismemberPresentation info)
        {
            if (!info.ByLocalPlayer || !TryGetFiringHand(out Oculus.Haptics.Controller hand))
            {
                return;
            }

            ZombieHaptics.Play(
                info.Limb == ZombieLimb.Head ? ZombieHapticEvent.HeadPop : ZombieHapticEvent.Dismember,
                hand);
        }

        /// <summary>Hand that fired the shot being confirmed. ⚠️ Read from the recoil hook, not from the
        /// weapon: a kill or a severed limb is presented after the shot, when the grip may be gone.</summary>
        private static bool TryGetFiringHand(out Oculus.Haptics.Controller hand)
        {
            ZombieWeaponFeel feel = ZombieWeaponFeel.Installed;
            if (feel == null)
            {
                hand = Oculus.Haptics.Controller.Both;
                return false;
            }

            return ZombieHaptics.TryMapHand(feel.LastFiringHand, out hand);
        }
    }
}
