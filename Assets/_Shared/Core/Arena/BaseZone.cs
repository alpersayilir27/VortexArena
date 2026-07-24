using UnityEngine;
using UnityEngine.Events;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Team base strip along one edge of the arena where the team's weapons rest.
    /// Watches the HMD position in the zone's local space (no physics, mirroring
    /// ArenaBoundary) and raises events when the player walks in or out. Hook
    /// rearm/heal/respawn behaviour onto the events as the game grows.
    /// </summary>
    public class BaseZone : MonoBehaviour
    {
        [SerializeField] private Team team = Team.Red;

        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;

        [Header("Zone size (meters)")]
        [Tooltip("Half depth of the strip, along local X.")]
        [SerializeField] private float halfExtentX = 0.5f;
        [Tooltip("Half width of the strip, along local Z.")]
        [SerializeField] private float halfExtentZ = 5f;

        public UnityEvent onPlayerEntered;
        public UnityEvent onPlayerExited;

        public Team Team => team;
        public bool IsPlayerInside { get; private set; }

        private void Awake()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        private void Update()
        {
            if (head == null)
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            bool inside = Mathf.Abs(local.x) <= halfExtentX && Mathf.Abs(local.z) <= halfExtentZ;
            if (inside == IsPlayerInside)
                return;

            IsPlayerInside = inside;
            Debug.Log($"BaseZone: player {(inside ? "entered" : "left")} {team} base.");
            if (inside)
                onPlayerEntered?.Invoke();
            else
                onPlayerExited?.Invoke();
        }
    }
}
