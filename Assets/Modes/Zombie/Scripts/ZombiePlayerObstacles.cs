using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>A <see cref="NavMeshObstacle"/> that follows every chaseable player, so the zombies walk
    /// AROUND the squad instead of through it.</summary>
    /// <remarks>⚠️ <b>Carving is OFF</b>: a carving obstacle rebuilds the NavMesh at every step of every
    /// player and the whole wave re-paths each frame. Non-carving means the agents avoid the body with
    /// local avoidance, which is all that is wanted here.
    /// <para>⚠️ The radius (0.3 m) plus the agent's own (0.3 m) stays UNDER the zombie's stop distance
    /// (≥1 m, <see cref="ZombieBrain"/>): the body is pushed aside, never stopped short of its reach.</para>
    /// <para>Brain machine only (the owner admin, §10.10): created when the first owned brain goes live
    /// and destroyed when the last one goes away — on every other headset nothing exists, because
    /// nothing there drives an agent.</para></remarks>
    [DisallowMultipleComponent]
    internal sealed class ZombiePlayerObstacles : MonoBehaviour
    {
        private const float UpdateInterval = 0.1f;

        private const float ObstacleRadius = 0.3f;

        private const float ObstacleHeight = 1.8f;

        /// <summary>Head → floor drop used when the arena has a single storey: there is no measured
        /// level to sit the capsule on.</summary>
        private const float HeadToFloor = 1.6f;

        private static ZombiePlayerObstacles _instance;

        private readonly Dictionary<int, Transform> _bodies = new Dictionary<int, Transform>();
        private readonly List<int> _playerIds = new List<int>();
        private readonly List<int> _stale = new List<int>();

        private float _nextUpdate;

        /// <summary>Called by the first brain that goes live.</summary>
        public static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject host = new GameObject("ZombiePlayerObstacles")
            {
                // Hidden and not saved: a runtime-only helper must not land in a saved scene.
                hideFlags = HideFlags.HideAndDontSave,
            };

            _instance = host.AddComponent<ZombiePlayerObstacles>();
        }

        /// <summary>Called when the last brain goes away: a leftover obstacle would keep blocking the
        /// next mode's agents.</summary>
        public static void Release()
        {
            if (_instance == null)
            {
                return;
            }

            GameObject host = _instance.gameObject;
            _instance = null;
            Destroy(host);
        }

        private void Update()
        {
            if (Time.time < _nextUpdate)
            {
                return;
            }

            _nextUpdate = Time.time + UpdateInterval;

            ArenaClient client = ArenaClient.Instance;
            RemotePlayerRegistry remotes = client != null ? client.Remotes : null;
            if (remotes == null)
            {
                ClearAll();
                return;
            }

            remotes.GetActivePlayerIds(_playerIds);

            _stale.Clear();
            foreach (KeyValuePair<int, Transform> pair in _bodies)
            {
                if (!_playerIds.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < _playerIds.Count; i++)
            {
                int id = _playerIds[i];

                // A downed or protected player is not a wall: the zombies have to be able to stand over
                // the body a reviver is heading for.
                if (!ZombieRoster.CanBeChased(id) || !remotes.IsAlive(id) ||
                    !remotes.GetInterpolatedPose(id, out Pose head, out _, out _))
                {
                    _stale.Add(id);
                    continue;
                }

                Vector3 world = ArenaSpace.ArenaToWorld(head.position);
                Place(id, world);
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                Remove(_stale[i]);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            ClearAll();
        }

        private void Place(int playerId, in Vector3 headWorld)
        {
            if (!_bodies.TryGetValue(playerId, out Transform body) || body == null)
            {
                body = Create(playerId);
                _bodies[playerId] = body;
            }

            float y;
            if (ArenaFloors.Count > 1)
            {
                int floor = FloorState.PlayerFloor(playerId);
                if (headWorld.y < ArenaFloors.HeightOf(floor))
                {
                    floor = ArenaFloors.FloorAt(headWorld.y);
                }

                y = ArenaFloors.HeightOf(floor);
            }
            else
            {
                y = headWorld.y - HeadToFloor;
            }

            body.position = new Vector3(headWorld.x, y, headWorld.z);
        }

        private Transform Create(int playerId)
        {
            GameObject host = new GameObject($"PlayerObstacle_{playerId}");
            host.transform.SetParent(transform, false);

            NavMeshObstacle obstacle = host.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.carving = false;
            obstacle.radius = ObstacleRadius;
            obstacle.height = ObstacleHeight;
            obstacle.center = new Vector3(0f, ObstacleHeight * 0.5f, 0f);

            return host.transform;
        }

        private void Remove(int playerId)
        {
            if (!_bodies.TryGetValue(playerId, out Transform body))
            {
                return;
            }

            _bodies.Remove(playerId);
            if (body != null)
            {
                Destroy(body.gameObject);
            }
        }

        private void ClearAll()
        {
            foreach (KeyValuePair<int, Transform> pair in _bodies)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            _bodies.Clear();
        }
    }
}
