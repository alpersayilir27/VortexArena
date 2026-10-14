using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Who fired, when and from where — the brain's only "that one is loud" signal
    /// (<see cref="ZombieBrain"/> targeting, §10.5).</summary>
    /// <remarks>⚠️ The fire event carries NO position (§6.5), so the origin is resolved from the
    /// shooter's interpolated head at the moment the event arrives; a player with no pose yet is
    /// skipped instead of remembered at the arena origin.
    /// <para>Lives on the brain machine like everything else here: the hook is harmless elsewhere, but
    /// nothing reads it there.</para></remarks>
    internal static class ZombieShotMemory
    {
        private struct Shot
        {
            public float Time;
            public Vector3 Origin;
        }

        private static readonly Dictionary<int, Shot> Shots = new Dictionary<int, Shot>();

        private static bool _hooked;

        /// <summary>⚠️ Runs before the first scene even with domain reload OFF: without the clear, a
        /// previous run's shots would make the first wave chase a player who has not fired yet.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Shots.Clear();

            if (_hooked)
            {
                return;
            }

            _hooked = true;
            NetEvents.OnRemoteFireEvent += Remember;
        }

        /// <summary>Shots and throws both count: either one gives the zombies a direction.</summary>
        private static void Remember(RemoteFireEvent fire)
        {
            ArenaClient client = ArenaClient.Instance;
            RemotePlayerRegistry remotes = client != null ? client.Remotes : null;
            if (remotes == null || !remotes.GetInterpolatedPose(fire.playerId, out Pose head, out _, out _))
            {
                return;
            }

            Shots[fire.playerId] = new Shot
            {
                Time = Time.time,
                Origin = ArenaSpace.ArenaToWorld(head.position),
            };
        }

        /// <summary>Did <paramref name="playerId"/> fire in the last <paramref name="seconds"/> from a
        /// point within <paramref name="radius"/> (flat) of <paramref name="position"/>?</summary>
        public static bool FiredNear(int playerId, in Vector3 position, float radius, float seconds)
        {
            if (!Shots.TryGetValue(playerId, out Shot shot) || Time.time - shot.Time > seconds)
            {
                return false;
            }

            float dx = shot.Origin.x - position.x;
            float dz = shot.Origin.z - position.z;
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
