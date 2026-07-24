using System;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// Spawn kataloğunun tek satırı: ağda taşınan <c>id</c> string'i ile yerelde üretilecek
    /// prefab eşlemesi. Serialize edilen ikincil tip olduğu için kendi dosyasındadır.
    /// </summary>
    [Serializable]
    public sealed class NetSpawnEntry
    {
        [Tooltip("Ağda taşınan spawn kimliği — sunucu ve istemcide BİREBİR aynı yazılır.")]
        [SerializeField] private string id = "";
        [SerializeField] private GameObject prefab;

        /// <summary>Ağda taşınan spawn kimliği.</summary>
        public string Id => id;

        /// <summary>Bu kimlikle üretilecek prefab.</summary>
        public GameObject Prefab => prefab;
    }
}
