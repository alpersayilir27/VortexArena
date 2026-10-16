using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Contract between the zombie actor side (raises) and the player-side hit feel (listens).</summary>
    /// <remarks>Actor side = <see cref="ZombieView"/> and its gore/reaction parts; player side = the
    /// <c>Feel/</c> feedback (haptics, 2D audio, hit markers). Neither side references the other's types.</remarks>
    internal static class ZombieFeelEvents
    {
        private static readonly Dictionary<int, IZombieFeelTarget> Targets = new Dictionary<int, IZombieFeelTarget>();

        /// <summary>A zombie's death starts on this headset; raised once per zombie life.</summary>
        public static event Action<ZombieKillPresentation> KillPresented;

        /// <summary>A head or limb came off (death shot or live leg loss).</summary>
        public static event Action<ZombieDismemberPresentation> Dismembered;

        public static void Register(IZombieFeelTarget target)
        {
            if (target != null)
            {
                Targets[target.NetId] = target;
            }
        }

        public static void Unregister(IZombieFeelTarget target)
        {
            if (target != null && Targets.TryGetValue(target.NetId, out IZombieFeelTarget current) && current == target)
            {
                Targets.Remove(target.NetId);
            }
        }

        public static bool TryGet(int netId, out IZombieFeelTarget target) => Targets.TryGetValue(netId, out target);

        public static void RaiseKill(in ZombieKillPresentation info) => KillPresented?.Invoke(info);

        public static void RaiseDismember(in ZombieDismemberPresentation info) => Dismembered?.Invoke(info);
    }

    /// <summary>What the player side may read from a live zombie.</summary>
    internal interface IZombieFeelTarget
    {
        int NetId { get; }

        bool IsAlive { get; }

        /// <summary>Chest height world point; source of the damage direction arc.</summary>
        Vector3 ChestPosition { get; }

        /// <summary>Boss-class (Brute, Dev): lighter reactions, no dismemberment for Dev.</summary>
        bool IsHeavy { get; }
    }

    internal enum ZombieLimb
    {
        None = 0,
        Head = 1,
        LeftArm = 2,
        RightArm = 3,
        LeftLeg = 4,
        RightLeg = 5,
    }

    internal readonly struct ZombieKillPresentation
    {
        public readonly int NetId;
        public readonly Vector3 Point;
        public readonly HitZone Zone;

        /// <summary>The killing hit was this headset's own shot.</summary>
        public readonly bool ByLocalPlayer;

        /// <summary>Started from local prediction, before the server's <c>broken</c>.</summary>
        public readonly bool Predicted;

        public readonly ZombieLimb Severed;

        public ZombieKillPresentation(int netId, Vector3 point, HitZone zone, bool byLocalPlayer, bool predicted,
            ZombieLimb severed)
        {
            NetId = netId;
            Point = point;
            Zone = zone;
            ByLocalPlayer = byLocalPlayer;
            Predicted = predicted;
            Severed = severed;
        }
    }

    internal readonly struct ZombieDismemberPresentation
    {
        public readonly int NetId;
        public readonly ZombieLimb Limb;
        public readonly Vector3 Point;
        public readonly bool ByLocalPlayer;

        public ZombieDismemberPresentation(int netId, ZombieLimb limb, Vector3 point, bool byLocalPlayer)
        {
            NetId = netId;
            Limb = limb;
            Point = point;
            ByLocalPlayer = byLocalPlayer;
        }
    }
}
