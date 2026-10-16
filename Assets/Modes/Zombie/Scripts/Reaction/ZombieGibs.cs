using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The flesh, bone and skull pieces thrown by a head pop or a severed limb.</summary>
    /// <remarks>Self-bootstrapping pool: the nodes are built ONCE on the first burst and reused for the
    /// rest of the session — a wave severs dozens of limbs and an <c>Instantiate</c> per piece is a GC
    /// spike on Quest.
    /// <para>⚠️ The pieces live on the <c>ZombieRagdoll</c> layer, which the shot raycast excludes
    /// (<see cref="ArenaLayers.ShotMask"/>): a gib in the air must never swallow a round. Without that
    /// layer they fly with collisions OFF instead — a piece that falls through the floor is a look
    /// problem, one that eats bullets is a gameplay bug.</para>
    /// <para>⚠️ Meshes and materials are read from the tuning asset's gore prefabs WITHOUT instantiating
    /// them: the material slots are remapped by name ("Bone" → bone material, else flesh).</para></remarks>
    internal class ZombieGibs : MonoBehaviour
    {
        /// <summary>One drawable piece: the mesh and the materials it is drawn with.</summary>
        private readonly struct Piece
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;

            public Piece(Mesh mesh, Material[] materials)
            {
                Mesh = mesh;
                Materials = materials;
            }
        }

        private static ZombieGibs _shared;

        public static ZombieGibs Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ZombieGibs]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ZombieGibs>();
                }

                return _shared;
            }
        }

        private ZombieReactionTuning _tuning;
        private bool _ready;
        private bool _failed;

        private readonly List<Piece> _flesh = new List<Piece>(4);
        private readonly List<Piece> _bone = new List<Piece>(2);
        private readonly List<Piece> _skull = new List<Piece>(2);

        private Transform[] _nodes;
        private Rigidbody[] _bodies;
        private MeshFilter[] _filters;
        private MeshRenderer[] _renderers;
        private float[] _born;
        private int _next;

        /// <summary>Throws <paramref name="count"/> pieces from <paramref name="point"/> along
        /// <paramref name="direction"/>. <paramref name="skull"/> = a head pop: the first piece is a
        /// skull fragment.</summary>
        public void Burst(Vector3 point, Vector3 direction, int count, bool skull)
        {
            if (!Ensure() || count <= 0)
            {
                return;
            }

            Vector3 along = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
            Vector3 bias = (along + Vector3.up * _tuning.GibUpBias).normalized;

            for (int i = 0; i < count; i++)
            {
                List<Piece> set = skull && i == 0 ? _skull : Draw();
                if (set.Count == 0)
                {
                    set = _flesh.Count > 0 ? _flesh : _bone;
                }

                if (set.Count == 0)
                {
                    return;
                }

                Throw(point, bias, set[Random.Range(0, set.Count)]);
            }
        }

        /// <summary>Mostly flesh, some bone — a pile of bone chips reads as gravel.</summary>
        private List<Piece> Draw() => Random.value < 0.75f || _bone.Count == 0 ? _flesh : _bone;

        private void Throw(Vector3 point, Vector3 bias, in Piece piece)
        {
            int slot = _next;
            _next = (_next + 1) % _nodes.Length;

            Transform node = _nodes[slot];
            Rigidbody body = _bodies[slot];

            _filters[slot].sharedMesh = piece.Mesh;
            _renderers[slot].sharedMaterials = piece.Materials;

            node.SetPositionAndRotation(point, Random.rotationUniform);
            node.localScale = Vector3.one;
            node.gameObject.SetActive(true);

            // Cone around the biased direction: a perfectly collinear spray reads as one object.
            Vector3 scatter = (bias + Random.insideUnitSphere * 0.45f).normalized;

            body.isKinematic = false;
            body.linearVelocity = scatter * Random.Range(_tuning.GibEjectSpeedMin, _tuning.GibEjectSpeedMax);
            body.angularVelocity = Random.insideUnitSphere * (_tuning.GibSpinDegrees * Mathf.Deg2Rad);

            _born[slot] = Time.time;
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            float now = Time.time;
            float life = _tuning.GibLifetimeSeconds;
            float shrink = _tuning.GibShrinkSeconds;

            for (int i = 0; i < _nodes.Length; i++)
            {
                Transform node = _nodes[i];
                if (node == null || !node.gameObject.activeSelf)
                {
                    continue;
                }

                float age = now - _born[i];
                if (age < life)
                {
                    continue;
                }

                if (age >= life + shrink)
                {
                    node.gameObject.SetActive(false);
                    continue;
                }

                node.localScale = Vector3.one * (1f - (age - life) / shrink);
            }
        }

        private bool Ensure()
        {
            if (_ready)
            {
                return true;
            }

            if (_failed)
            {
                return false;
            }

            _tuning = ZombieReactionTuning.Live;

            Collect(_tuning.FleshGibPrefabs, _flesh);
            Collect(_tuning.BoneGibPrefabs, _bone);
            Collect(_tuning.SkullGibPrefabs, _skull);

            if ((_flesh.Count == 0 && _bone.Count == 0 && _skull.Count == 0) ||
                (_tuning.FleshMaterial == null && _tuning.BoneMaterial == null))
            {
                // No gore meshes or no material assigned: the sever still happens, it just throws
                // nothing (a missing material would draw Unity's magenta error shader).
                _failed = true;
                return false;
            }

            int size = _tuning.GibPoolSize;
            _nodes = new Transform[size];
            _bodies = new Rigidbody[size];
            _filters = new MeshFilter[size];
            _renderers = new MeshRenderer[size];
            _born = new float[size];

            int layer = LayerMask.NameToLayer(ArenaLayers.ZombieRagdollName);

            for (int i = 0; i < size; i++)
            {
                Build(i, layer);
            }

            _ready = true;
            return true;
        }

        private void Build(int slot, int layer)
        {
            var go = new GameObject("[Gib]");
            go.transform.SetParent(transform, false);

            if (layer >= 0)
            {
                go.layer = layer;
            }

            _filters[slot] = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderers[slot] = renderer;

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = _tuning.GibRadius;

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            // Without the corpse layer a gib would stop rounds: it flies through the world instead.
            body.detectCollisions = layer >= 0;
            _bodies[slot] = body;

            go.SetActive(false);
            _nodes[slot] = go.transform;
        }

        /// <summary>Reads the drawable pieces out of the gore prefabs (asset access only — nothing is
        /// instantiated) and remaps their material slots onto the tuning asset's two materials.</summary>
        private void Collect(GameObject[] prefabs, List<Piece> target)
        {
            target.Clear();

            if (prefabs == null)
            {
                return;
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null)
                {
                    continue;
                }

                MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                for (int f = 0; f < filters.Length; f++)
                {
                    Mesh mesh = filters[f].sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    target.Add(new Piece(mesh, RemapMaterials(filters[f])));
                }
            }
        }

        private Material[] RemapMaterials(MeshFilter filter)
        {
            int slots = Mathf.Max(1, filter.sharedMesh.subMeshCount);
            var materials = new Material[slots];

            Material[] authored = filter.TryGetComponent(out MeshRenderer renderer)
                ? renderer.sharedMaterials
                : null;

            for (int i = 0; i < slots; i++)
            {
                string name = authored != null && i < authored.Length && authored[i] != null
                    ? authored[i].name
                    : null;

                bool bone = name != null && name.IndexOf("Bone", System.StringComparison.OrdinalIgnoreCase) >= 0;
                materials[i] = bone && _tuning.BoneMaterial != null
                    ? _tuning.BoneMaterial
                    : _tuning.FleshMaterial;
            }

            return materials;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static on a destroyed component (required with domain reload off).
                _shared = null;
            }
        }
    }
}
