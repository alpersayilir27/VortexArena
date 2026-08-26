using UnityEngine;

namespace VortexArena.Modes.Burger
{
    /// <summary>The customer walk path baked into the scene (door → counter). Plain scene component: the
    /// walk is NOT on the wire (§10.5), every client derives it from this same polyline.</summary>
    [DisallowMultipleComponent]
    public sealed class BurgerCustomerPath : MonoBehaviour
    {
        [Tooltip("Kapıdan bankoya doğru sıralı yol noktaları.")]
        [SerializeField] private Transform[] waypoints;

        /// <summary>The scene holds exactly one path; null before it is enabled.</summary>
        public static BurgerCustomerPath Instance { get; private set; }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[BurgerCustomerPath] Sahnede birden fazla müşteri yolu var — " +
                                 $"'{name}' etkin yol olarak alındı.", this);
            }

            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Point at normalized distance <paramref name="t"/> (0 = door, 1 = counter).
        /// <para>⚠️ Sampled by LENGTH, not by waypoint index: with index-based sampling a long segment
        /// and a short one would take the same time and the customer would sprint through the long
        /// one.</para></summary>
        public Vector3 Sample(float t)
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                return transform.position;
            }

            if (waypoints.Length == 1)
            {
                return waypoints[0] != null ? waypoints[0].position : transform.position;
            }

            t = Mathf.Clamp01(t);

            float total = TotalLength();
            if (total <= 0f)
            {
                return FirstValid();
            }

            float target = total * t;
            float travelled = 0f;

            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                Transform from = waypoints[i];
                Transform to = waypoints[i + 1];
                if (from == null || to == null)
                {
                    continue;
                }

                float length = Vector3.Distance(from.position, to.position);
                if (length <= 0f)
                {
                    continue;
                }

                if (travelled + length >= target)
                {
                    return Vector3.Lerp(from.position, to.position, (target - travelled) / length);
                }

                travelled += length;
            }

            return LastValid();
        }

        private float TotalLength()
        {
            float total = 0f;

            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                if (waypoints[i] != null && waypoints[i + 1] != null)
                {
                    total += Vector3.Distance(waypoints[i].position, waypoints[i + 1].position);
                }
            }

            return total;
        }

        private Vector3 FirstValid()
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] != null)
                {
                    return waypoints[i].position;
                }
            }

            return transform.position;
        }

        private Vector3 LastValid()
        {
            for (int i = waypoints.Length - 1; i >= 0; i--)
            {
                if (waypoints[i] != null)
                {
                    return waypoints[i].position;
                }
            }

            return transform.position;
        }
    }
}
