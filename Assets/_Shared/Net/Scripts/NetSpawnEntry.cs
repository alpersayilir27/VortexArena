using System;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// A single row of the spawn catalogue: the mapping between the <c>id</c> string carried on the
    /// wire and the prefab to instantiate locally. It lives in its own file because it is a serialized
    /// secondary type.
    /// </summary>
    [Serializable]
    public sealed class NetSpawnEntry
    {
        [Tooltip("Ağda taşınan spawn kimliği — sunucu ve istemcide BİREBİR aynı yazılır.")]
        [SerializeField] private string id = "";
        [SerializeField] private GameObject prefab;

        /// <summary>The spawn id carried on the wire.</summary>
        public string Id => id;

        /// <summary>The prefab to instantiate for this id.</summary>
        public GameObject Prefab => prefab;
    }
}
