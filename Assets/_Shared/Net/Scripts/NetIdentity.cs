using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// The baked network id of a scene object (<c>sceneId</c>).
    /// <para>
    /// ⚠️ In v1 PLAYER sync rides <c>playerId</c> (Snapshot/PoseData), so this component is NOT ATTACHED
    /// to a player/avatar. SCENE OBJECTS ONLY: groundwork for future dynamic object sync (door, pickup,
    /// breakable cover).
    /// </para>
    /// <see cref="SceneId"/> is never written by hand: assigned through
    /// <c>GameObject &gt; VortexArena &gt; Network Parent</c> and repaired on save by
    /// <c>SceneIdGuard</c> when left at 0 or colliding. Registration via
    /// <see cref="OnEnable"/>/<see cref="OnDisable"/>; the static list empties itself on a scene change
    /// (no leak).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetIdentity : MonoBehaviour
    {
        [Tooltip("Sahne kaydında bake'lenen benzersiz kimlik — ELLE DÜZENLEMEYİN (0 = atanmamış).")]
        [SerializeField] private uint sceneId;

        private static readonly List<NetIdentity> Registry = new List<NetIdentity>();

        /// <summary>Network id, unique within the scene and baked on save (0 = unassigned).</summary>
        public uint SceneId => sceneId;

        /// <summary>All network identities active in the scene (read-only).</summary>
        public static IReadOnlyList<NetIdentity> All => Registry;

        private void OnEnable()
        {
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>
        /// Finds an identity by sceneId; null when there is no match. A query for 0 ALWAYS returns null
        /// — an object that has not been baked cannot be addressed over the network.
        /// </summary>
        public static NetIdentity Find(uint sceneId)
        {
            if (sceneId == 0u)
            {
                return null;
            }

            for (int i = 0; i < Registry.Count; i++)
            {
                NetIdentity identity = Registry[i];
                if (identity != null && identity.sceneId == sceneId)
                {
                    return identity;
                }
            }

            return null;
        }
    }
}
