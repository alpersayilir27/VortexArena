using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>Lookup of the live network objects by <c>netId</c> (§10.10) —
    /// <see cref="NetObjectSync"/> resolves incoming <c>object_state</c>/<c>world_state</c> through it.
    /// <para>⚠️ Baked SCENE objects (registered from <c>OnEnable</c>) and DYNAMIC ones (registered by
    /// the spawner through <c>NetObject.BindDynamicId</c>) share ONE table: the id ranges never overlap
    /// (§1) and a second table would make every lookup ask "which kind of object is this" first.</para>
    /// <para>No cleanup call is needed on a scene change: the objects die with the scene and
    /// <c>OnDisable</c> unregisters them.</para></summary>
    public static class NetObjectRegistry
    {
        private static readonly Dictionary<int, NetObject> Objects = new Dictionary<int, NetObject>();

        public static int Count => Objects.Count;

        /// <summary>All registered network objects (read-only).</summary>
        public static IReadOnlyCollection<NetObject> All => Objects.Values;

        public static void Register(int netId, NetObject netObject)
        {
            if (netObject == null || netId <= 0)
            {
                return;
            }

            if (Objects.TryGetValue(netId, out NetObject existing) && existing != null && existing != netObject)
            {
                Debug.LogError($"[NetObjectRegistry] netId {netId} iki objede birden: " +
                               $"'{existing.name}' ve '{netObject.name}'. Sahne bake'i bozuk — " +
                               "ikincisi ağa kaydedilmedi (sunucudan gelen durumu almayacak).", netObject);
                return;
            }

            Objects[netId] = netObject;
        }

        public static void Unregister(int netId, NetObject netObject)
        {
            if (Objects.TryGetValue(netId, out NetObject existing) && existing == netObject)
            {
                Objects.Remove(netId);
            }
        }

        public static bool TryGet(int netId, out NetObject netObject)
        {
            return Objects.TryGetValue(netId, out netObject) && netObject != null;
        }
    }
}
