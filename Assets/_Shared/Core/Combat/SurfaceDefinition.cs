using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>What a round leaves behind on ONE kind of surface: the particle prefab, the sound,
    /// and the materials that resolve to it.
    /// <para>The material list is the MAIN binding path — <see cref="SurfaceLibrary"/> builds its
    /// lookup from it, so binding a material here covers every renderer using it in every arena at
    /// once. That is the point: a new environment pack costs list entries, not scene work.
    /// <see cref="SurfaceTag"/> is the per-object exception.</para>
    /// <para>⚠️ Bind the material ASSET. A material read at runtime through <c>renderer.material</c>
    /// is a COPY and never matches — which is why the library only ever reads
    /// <c>sharedMaterial</c>.</para>
    /// <para>⚠️ Multi-material meshes are NOT supported (only the first material resolves): telling
    /// submeshes apart needs <c>hit.triangleIndex</c>, which forces Read/Write on the mesh and a
    /// <c>MeshCollider</c>. Split the object or give it a <see cref="SurfaceTag"/>.</para></summary>
    [CreateAssetMenu(fileName = "SUR_Surface", menuName = "VortexArena/Surface Definition")]
    public class SurfaceDefinition : ScriptableObject
    {
        [Tooltip("İnsan okusun diye — log ve Inspector'da yüzeyi adlandırır (kar, tahta, metal…). " +
                 "Boşsa asset adı kullanılır.")]
        [SerializeField] private string surfaceId = "";

        [Tooltip("Çarpma anında oynatılan parçacık prefabı. HAVUZLANIR — atış başına Instantiate yok. " +
                 "Boşsa yalnız ses çalar.")]
        [SerializeField] private GameObject impactPrefab;

        [Tooltip("Çarpma sesleri — her vuruşta biri rastgele seçilir. Boşsa ses çıkmaz, " +
                 "parçacık yine oynar.")]
        [SerializeField] private AudioClip[] impactClips = Array.Empty<AudioClip>();

        [Range(0f, 1f)]
        [Tooltip("Çarpma sesinin seviyesi.")]
        [SerializeField] private float volume = 0.8f;

        [Tooltip("Perde bandı (min–max): aynı klip peş peşe çalınca tek tip duyulmasın.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(0.94f, 1.06f);

        [Tooltip("Havuz düğümünün gizlenme süresi (sn). ⚠️ Parçacığın kendi ömründen KISA olursa " +
                 "efekt yarıda kesilir.")]
        [SerializeField] private float lifetimeSeconds = 2f;

        [Tooltip("Bu yüzeye çözülen materyaller. Aynı materyal yalnız BİR tanıma bağlanır.")]
        [SerializeField] private Material[] materials = Array.Empty<Material>();

        public string SurfaceId => string.IsNullOrEmpty(surfaceId) ? name : surfaceId;

        public GameObject ImpactPrefab => impactPrefab;

        public bool HasSound => impactClips != null && impactClips.Length > 0;

        public float Volume => volume;

        public float LifetimeSeconds => Mathf.Max(0.05f, lifetimeSeconds);

        public Material[] Materials => materials;

        /// <summary>One of the clips at random; null when the list is empty.</summary>
        public AudioClip PickClip()
        {
            if (impactClips == null || impactClips.Length == 0)
            {
                return null;
            }

            return impactClips[UnityEngine.Random.Range(0, impactClips.Length)];
        }

        /// <summary>A pitch inside the band; tolerates a reversed min/max.</summary>
        public float PickPitch()
        {
            float low = Mathf.Min(pitchRange.x, pitchRange.y);
            float high = Mathf.Max(pitchRange.x, pitchRange.y);
            return high <= 0f ? 1f : UnityEngine.Random.Range(low, high);
        }
    }
}
