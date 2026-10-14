using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>Proximity measurement for <see cref="ModeReviveAnchor.Teammate"/>: how long a living
    /// teammate has stood next to a downed player (§10.4).</summary>
    /// <remarks>
    /// LOCAL ONLY — a presentation/condition measurement, no wire of its own: the downed player's own
    /// client still sends the single <c>revive_request</c> (there is no second server revive path), and
    /// the reviver's id only rides along for scoring.
    /// <para>⚠️ Measured on BOTH sides of the same radius and the two answers are NOT the same thing:
    /// <see cref="LocalProgress"/> is what the downed player's own client will act on,
    /// <see cref="AllyProgress"/> is the reviver's ESTIMATE for its HUD (it measures from interpolated
    /// remote poses, which lag). Never gate a request on the ally estimate.</para>
    /// <para>Ticked by <see cref="PlayerCombatState"/> — not a MonoBehaviour, so there is no second
    /// object to place in a scene and no second update order to reason about.</para>
    /// </remarks>
    public static class TeammateRevive
    {
        /// <summary>0..1 hold progress on the LOCAL (downed) player; 0 when no teammate is in reach.</summary>
        public static float LocalProgress { get; private set; }

        /// <summary>Nearest teammate holding the local player up; 0 = none. Goes on
        /// <c>revive_request.by</c>.</summary>
        public static int LocalReviverId { get; private set; }

        /// <summary>Is there any OTHER connected, calibrated, in-match player.</summary>
        /// <remarks>Drives the solo fallback: alone in the arena there is nobody to lift you, so the
        /// mode must not leave the player dead for the rest of the match.</remarks>
        public static bool HasOtherCalibratedPlayer { get; private set; }

        // Roster mirror (ZombieRoster pattern): connected + calibrated + inMatch. An id missing here
        // is never counted — RemotePlayerRegistry.IsAlive answers "true" for ids it has never seen.
        private static readonly HashSet<int> Eligible = new HashSet<int>();

        // Reviver-side hold start per downed ally (Time.time); an ally out of reach is removed.
        private static readonly Dictionary<int, float> AllyHoldSince = new Dictionary<int, float>();

        private static readonly List<int> PlayerIds = new List<int>();
        private static readonly List<int> StaleAllies = new List<int>();

        private static float _localHoldSince;
        private static bool _localHolding;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Static handler on a static event: nothing to unsubscribe, and the mirror survives scene
            // loads exactly like the roster it mirrors.
            NetEvents.OnLobbyState += ApplyRoster;
            NetEvents.OnDisconnected += Reset;
        }

        /// <summary>0..1 estimate for a downed ally the LOCAL player is standing next to; 0 when it is
        /// not being held. HUD only (see the class remarks).</summary>
        public static float AllyProgress(int playerId)
        {
            return AllyHoldSince.TryGetValue(playerId, out float since)
                ? Progress(since)
                : 0f;
        }

        /// <summary>⚠️ Called ONLY by <see cref="PlayerCombatState.Update"/> — a second caller would
        /// measure the same hold twice per frame and halve the time it takes.</summary>
        /// <param name="localHead">Local HMD transform; <c>null</c> = untrackable, everything resets.</param>
        public static void Tick(Transform localHead, bool localAlive)
        {
            if (ModeRuntime.Revive != ModeReviveAnchor.Teammate || localHead == null)
            {
                ClearProgress();
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            RemotePlayerRegistry remotes = client != null ? client.Remotes : null;
            if (remotes == null)
            {
                ClearProgress();
                return;
            }

            Vector3 head = localHead.position;
            int localFloor = FloorState.Local;

            remotes.GetActivePlayerIds(PlayerIds);

            if (localAlive)
            {
                TickAllies(remotes, head, localFloor);
                LocalProgress = 0f;
                LocalReviverId = 0;
                _localHolding = false;
                return;
            }

            AllyHoldSince.Clear(); // dead: the reviver-side estimate belongs to a living player
            TickLocal(remotes, head, localFloor);
        }

        /// <summary>Nearest living teammate on our floor inside the radius keeps our hold running.</summary>
        private static void TickLocal(RemotePlayerRegistry remotes, in Vector3 head, int localFloor)
        {
            int nearest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < PlayerIds.Count; i++)
            {
                int id = PlayerIds[i];
                if (!Eligible.Contains(id) || !remotes.IsAlive(id))
                {
                    continue;
                }

                if (!TryReach(remotes, id, head, localFloor, out float distance) || distance >= best)
                {
                    continue;
                }

                best = distance;
                nearest = id;
            }

            if (nearest == 0)
            {
                // Out of reach resets to zero: the hold is CONTINUOUS, otherwise a reviver could
                // collect three seconds in three safe dashes.
                LocalProgress = 0f;
                LocalReviverId = 0;
                _localHolding = false;
                return;
            }

            // The timer tracks PRESENCE, not identity: a second teammate taking over mid-hold does not
            // punish the downed player, who cannot see who is kneeling anyway.
            if (!_localHolding)
            {
                _localHoldSince = Time.time;
                _localHolding = true;
            }

            LocalReviverId = nearest;
            LocalProgress = Progress(_localHoldSince);
        }

        /// <summary>Reviver side: per downed ally on our floor inside the radius.</summary>
        private static void TickAllies(RemotePlayerRegistry remotes, in Vector3 head, int localFloor)
        {
            StaleAllies.Clear();
            foreach (KeyValuePair<int, float> pair in AllyHoldSince)
            {
                StaleAllies.Add(pair.Key);
            }

            for (int i = 0; i < PlayerIds.Count; i++)
            {
                int id = PlayerIds[i];
                if (!Eligible.Contains(id) || remotes.IsAlive(id))
                {
                    continue;
                }

                if (!TryReach(remotes, id, head, localFloor, out float _))
                {
                    continue;
                }

                StaleAllies.Remove(id);
                if (!AllyHoldSince.ContainsKey(id))
                {
                    AllyHoldSince[id] = Time.time;
                }
            }

            for (int i = 0; i < StaleAllies.Count; i++)
            {
                AllyHoldSince.Remove(StaleAllies[i]);
            }
        }

        /// <summary>Is this player on <paramref name="localFloor"/> and within the revive radius of
        /// <paramref name="head"/>; <paramref name="distance"/> is HORIZONTAL.</summary>
        /// <remarks>Height is dropped on purpose: both players stand on the same floor plate, and a
        /// vertical term would let a tall player and a crouching one fail a radius they are standing
        /// inside of.</remarks>
        private static bool TryReach(
            RemotePlayerRegistry remotes, int playerId, in Vector3 head, int localFloor, out float distance)
        {
            distance = 0f;

            if (!remotes.GetInterpolatedPose(playerId, out Pose pose, out _, out _))
            {
                return false;
            }

            Vector3 world = ArenaSpace.ArenaToWorld(pose.position);
            if (FloorOf(playerId, world) != localFloor)
            {
                return false;
            }

            float dx = world.x - head.x;
            float dz = world.z - head.z;
            distance = Mathf.Sqrt(dx * dx + dz * dz);
            return distance <= ArenaProtocol.REVIVE_TEAMMATE_RADIUS;
        }

        /// <summary>Storey a player stands on (<c>ZombieBrain.FloorOf</c> rule): the roster lags a hop
        /// by a few frames, so a head already below its level takes the storey it is really on.</summary>
        private static int FloorOf(int playerId, in Vector3 headWorld)
        {
            int floor = FloorState.PlayerFloor(playerId);
            if (headWorld.y < ArenaFloors.HeightOf(floor))
            {
                floor = ArenaFloors.FloorAt(headWorld.y);
            }

            return floor;
        }

        private static float Progress(float since)
        {
            float seconds = ArenaProtocol.REVIVE_TEAMMATE_SECONDS;
            return seconds <= 0f ? 1f : Mathf.Clamp01((Time.time - since) / seconds);
        }

        private static void ApplyRoster(LobbyStateMsg msg)
        {
            Eligible.Clear();
            HasOtherCalibratedPlayer = false;

            if (msg?.players == null)
            {
                return;
            }

            int selfId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];

                // Admin rows carry calibrated:false, which keeps the operator out of the set for free.
                if (info == null || !info.calibrated || !info.inMatch ||
                    info.connection != ArenaProtocol.CONNECTION_CONNECTED)
                {
                    continue;
                }

                Eligible.Add(info.playerId);
                if (info.playerId != selfId)
                {
                    HasOtherCalibratedPlayer = true;
                }
            }
        }

        private static void Reset()
        {
            Eligible.Clear();
            HasOtherCalibratedPlayer = false;
            ClearProgress();
        }

        private static void ClearProgress()
        {
            LocalProgress = 0f;
            LocalReviverId = 0;
            _localHolding = false;
            AllyHoldSince.Clear();
        }
    }
}
