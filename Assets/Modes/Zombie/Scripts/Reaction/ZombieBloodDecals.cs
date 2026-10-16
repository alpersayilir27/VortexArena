using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The blood a round leaves on the WORLD (wall/floor splats) and on the zombie's own body
    /// (bone-parented wounds).</summary>
    /// <remarks>Self-bootstrapping pool, same shape as <see cref="ZombieBloodPool"/>: no scene step and
    /// no reference to bind.
    /// <para>⚠️ Everything is POOLED and the oldest entry is recycled — a late wave lands hundreds of
    /// rounds, and an <c>Instantiate</c> per hit is a GC spike on Quest. The caps also bound how much
    /// overdraw the arena can collect.</para>
    /// <para>⚠️ Silent no-op without <see cref="ZombieReactionTuning.BloodDecalMaterial"/>: a missing
    /// decoration must never break a hit.</para></remarks>
    internal class ZombieBloodDecals : MonoBehaviour
    {
        /// <summary>Lift off the hit surface (m) so the decal does not z-fight with it.</summary>
        private const float SurfaceOffset = 0.001f;

        /// <summary>Wounds sit further out: the skinned body moves THROUGH its own hit boxes and a
        /// 1 mm offset disappears into the mesh on the next step.</summary>
        private const float WoundOffset = 0.004f;

        /// <summary>Floor probe for a stump's splat (m).</summary>
        private const float FloorProbeUp = 0.3f;

        private const float FloorProbeDown = 3f;

        private static readonly RaycastHit[] Hits = new RaycastHit[6];

        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        private static ZombieBloodDecals _shared;

        public static ZombieBloodDecals Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ZombieBloodDecals]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ZombieBloodDecals>();
                }

                return _shared;
            }
        }

        private ZombieReactionTuning _tuning;
        private Material _material;
        private Mesh[] _cells;
        private MaterialPropertyBlock _block;
        private bool _ready;
        private bool _failed;

        private Transform[] _splats;
        private MeshRenderer[] _splatRenderers;
        private float[] _splatBorn;
        private int _nextSplat;

        private Transform[] _wounds;
        private object[] _woundOwners;
        private int _nextWound;

        /// <summary>Marks a surface where a round came out of a body — wall or floor, whichever the
        /// shot direction finds first. <c>false</c> = nothing was placed (no material, no surface, or
        /// the chance roll lost).</summary>
        public bool TrySplat(Vector3 point, Vector3 direction, bool head)
        {
            if (!Ensure())
            {
                return false;
            }

            float chance = head ? _tuning.SplatChanceHead : _tuning.SplatChanceOther;
            if (Random.value > chance || direction.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            direction.Normalize();

            if (!TryFindSurface(new Ray(point, direction), _tuning.SplatRayMeters,
                    out Vector3 surface, out Vector3 normal))
            {
                return false;
            }

            Place(surface, normal);
            return true;
        }

        /// <summary>Splat on the floor under a severed limb's stump.</summary>
        public void SplatOnFloor(Vector3 world)
        {
            if (!Ensure())
            {
                return;
            }

            if (TryFindSurface(new Ray(world + Vector3.up * FloorProbeUp, Vector3.down),
                    FloorProbeUp + FloorProbeDown, out Vector3 surface, out Vector3 normal))
            {
                Place(surface, normal);
            }
        }

        /// <summary>A wound on the body itself: a small quad parented to the bone that was hit, so it
        /// rides the animation and the ragdoll.</summary>
        /// <param name="owner">Whoever claims it — <see cref="ReleaseWounds"/> drops that one's quads
        /// when the body despawns.</param>
        public void Wound(object owner, Transform bone, Vector3 point, Vector3 direction)
        {
            if (!Ensure() || bone == null)
            {
                return;
            }

            Transform node = _wounds[_nextWound];
            if (node == null)
            {
                node = NewNode("[ZombieWound]", out MeshRenderer _);
                _wounds[_nextWound] = node;
            }

            _woundOwners[_nextWound] = owner;
            _nextWound = (_nextWound + 1) % _wounds.Length;

            // Facing the shooter: the entry side is the one that bleeds.
            Vector3 outward = direction.sqrMagnitude > 1e-6f ? -direction.normalized : Vector3.up;

            node.SetParent(bone, false);
            node.SetPositionAndRotation(point + outward * WoundOffset,
                Quaternion.AngleAxis(Random.Range(0f, 360f), outward) * Quaternion.LookRotation(outward));

            // The bone carries the rig's own scale (Mixamo centimetres): the wound's size is in metres,
            // so it has to be divided back out.
            float size = Random.Range(_tuning.WoundSizeMin, _tuning.WoundSizeMax);
            float boneScale = Mathf.Abs(bone.lossyScale.x) > 0.0001f ? bone.lossyScale.x : 1f;
            node.localScale = Vector3.one * (size / boneScale);

            ApplyCell(node, 1f);
            node.gameObject.SetActive(true);
        }

        /// <summary>Drops every wound that belongs to <paramref name="owner"/> — the body is gone and a
        /// quad left parented to a destroyed bone would be destroyed with it, leaking the pool slot.</summary>
        public void ReleaseWounds(object owner)
        {
            if (_wounds == null || owner == null)
            {
                return;
            }

            for (int i = 0; i < _wounds.Length; i++)
            {
                if (_woundOwners[i] != owner)
                {
                    continue;
                }

                _woundOwners[i] = null;

                Transform node = _wounds[i];
                if (node == null)
                {
                    continue;
                }

                node.gameObject.SetActive(false);
                node.SetParent(transform, false);
            }
        }

        private void Place(Vector3 surface, Vector3 normal)
        {
            Transform node = _splats[_nextSplat];
            if (node == null)
            {
                node = NewNode("[ZombieSplat]", out MeshRenderer renderer);
                _splats[_nextSplat] = node;
                _splatRenderers[_nextSplat] = renderer;
            }

            _splatBorn[_nextSplat] = Time.unscaledTime;
            _nextSplat = (_nextSplat + 1) % _splats.Length;

            node.SetParent(transform, false);
            node.SetPositionAndRotation(surface + normal * SurfaceOffset,
                Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Quaternion.LookRotation(normal));
            node.localScale = Vector3.one * Random.Range(_tuning.SplatSizeMin, _tuning.SplatSizeMax);

            ApplyCell(node, 1f);
            node.gameObject.SetActive(true);
        }

        /// <summary>Nearest WORLD surface along the ray. ⚠️ Hit boxes are skipped by component, not by
        /// layer: zombie and player boxes sit on <c>Default</c> next to the walls, and a splat on a body
        /// would ride away with it.</summary>
        private static bool TryFindSurface(in Ray ray, float distance, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;

            int mask = ArenaLayers.AreaTargetMask & ~ArenaLayers.ZombieRagdollMask;
            int count = Physics.RaycastNonAlloc(ray, Hits, distance, mask, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider collider = Hits[i].collider;
                if (collider == null || collider.GetComponent<RemoteHitBox>() != null)
                {
                    continue;
                }

                if (Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    point = Hits[i].point;
                    normal = Hits[i].normal;
                    found = true;
                }
            }

            return found;
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            float now = Time.unscaledTime;
            float life = _tuning.SplatLifetimeSeconds;
            float fade = _tuning.SplatFadeSeconds;

            for (int i = 0; i < _splats.Length; i++)
            {
                Transform node = _splats[i];
                if (node == null || !node.gameObject.activeSelf)
                {
                    continue;
                }

                float age = now - _splatBorn[i];
                if (age < life)
                {
                    continue;
                }

                if (age >= life + fade)
                {
                    node.gameObject.SetActive(false);
                    continue;
                }

                ApplyAlpha(_splatRenderers[i], 1f - (age - life) / fade);
            }
        }

        private Transform NewNode(string label, out MeshRenderer renderer)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            return go.transform;
        }

        /// <summary>Picks an atlas cell (a different quad MESH per cell — a per-renderer UV offset would
        /// need a property block and cost the SRP batcher) and resets the fade.</summary>
        private void ApplyCell(Transform node, float alpha)
        {
            if (node.TryGetComponent(out MeshFilter filter))
            {
                filter.sharedMesh = _cells[Random.Range(0, _cells.Length)];
            }

            if (node.TryGetComponent(out MeshRenderer renderer))
            {
                ApplyAlpha(renderer, alpha);
            }
        }

        private void ApplyAlpha(MeshRenderer renderer, float alpha)
        {
            if (renderer == null)
            {
                return;
            }

            _block.SetColor(BaseColorProperty, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            renderer.SetPropertyBlock(_block);
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
            Material source = _tuning.BloodDecalMaterial;
            if (source == null)
            {
                // Not a warning: the decal material is optional data, and the mode is playable without it.
                _failed = true;
                return false;
            }

            // A runtime COPY: the generated atlas may not be written into the shared asset material,
            // which would dirty it in the editor and persist into the project.
            _material = new Material(source) { name = "M_ZombieBlood (runtime)" };

            // Both names: URP tags _BaseMap as the main texture, a Built-in/Unlit fallback uses _MainTex.
            Texture2D atlas = ZombieBloodTexture.Atlas;
            if (_material.HasProperty("_BaseMap"))
            {
                _material.SetTexture("_BaseMap", atlas);
            }

            _material.mainTexture = atlas;

            _block = new MaterialPropertyBlock();
            _cells = BuildCellMeshes();

            _splats = new Transform[_tuning.SplatPoolSize];
            _splatRenderers = new MeshRenderer[_splats.Length];
            _splatBorn = new float[_splats.Length];

            _wounds = new Transform[_tuning.WoundPoolSize];
            _woundOwners = new object[_wounds.Length];

            _ready = true;
            return true;
        }

        /// <summary>One unit quad per atlas cell, centred on its own pivot and facing +Z.</summary>
        private static Mesh[] BuildCellMeshes()
        {
            int cells = ZombieBloodTexture.Cells;
            var meshes = new Mesh[cells * cells];
            float step = 1f / cells;

            var vertices = new Vector3[4];
            var uv = new Vector2[4];
            var triangles = new[] { 0, 2, 1, 2, 3, 1 };
            var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };

            vertices[0] = new Vector3(-0.5f, -0.5f, 0f);
            vertices[1] = new Vector3(0.5f, -0.5f, 0f);
            vertices[2] = new Vector3(-0.5f, 0.5f, 0f);
            vertices[3] = new Vector3(0.5f, 0.5f, 0f);

            for (int y = 0; y < cells; y++)
            {
                for (int x = 0; x < cells; x++)
                {
                    uv[0] = new Vector2(x * step, y * step);
                    uv[1] = new Vector2((x + 1) * step, y * step);
                    uv[2] = new Vector2(x * step, (y + 1) * step);
                    uv[3] = new Vector2((x + 1) * step, (y + 1) * step);

                    var mesh = new Mesh { name = $"ZombieSplatQuad_{x}{y}" };
                    mesh.SetVertices(vertices);
                    mesh.SetUVs(0, uv);
                    mesh.SetNormals(normals);
                    mesh.SetTriangles(triangles, 0);
                    mesh.UploadMeshData(true);

                    meshes[y * cells + x] = mesh;
                }
            }

            return meshes;
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
