using UnityEngine;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// The visual-only clone of the target item: renderers + collision surface, nothing else.
    /// <para>⚠️ Instantiated under an INACTIVE holder and stripped there: the item prefab's own
    /// scripts (weapon, network, grab) must never run in the tool — one Awake is enough to register
    /// the clone with a game singleton.</para>
    /// </summary>
    public sealed class GripCaptureItem
    {
        /// <summary>Component removal passes — RequireComponent chains refuse removal until the
        /// dependent component is gone.</summary>
        private const int StripPasses = 4;

        private Vector3 _pivotLocal;
        private Vector3 _placedPivot;
        private Quaternion _placedRotation = Quaternion.identity;

        public Transform Root { get; private set; }

        /// <summary>Has the item been placed in front of the head yet.</summary>
        public bool Placed { get; private set; }

        /// <summary>The grip pivot (renderer bounds centre at spawn) in world space.</summary>
        public Vector3 PivotWorld => Root != null ? Root.TransformPoint(_pivotLocal) : Vector3.zero;

        public Quaternion Rotation => Root != null ? Root.rotation : Quaternion.identity;

        public Vector3 Position => Root != null ? Root.position : Vector3.zero;

        /// <summary>Clones and strips the prefab; <c>false</c> when it carries no renderer.</summary>
        public bool Build(GameObject prefab, Transform parent)
        {
            if (prefab == null)
            {
                return false;
            }

            var holder = new GameObject("ItemHolder");
            holder.transform.SetParent(parent, false);
            holder.SetActive(false);

            GameObject clone = Object.Instantiate(prefab, holder.transform);
            clone.name = prefab.name;

            // The prefab root's own scale is kept: weapon roots are 0.8 and the grip record is
            // unscaled metres, so changing it here would move every captured hand.
            Vector3 scale = prefab.transform.localScale;

            StripToRenderers(clone);
            FlattenLayers(clone);

            clone.transform.SetParent(null, false);
            clone.transform.localScale = scale;
            Object.Destroy(holder);
            clone.SetActive(true);

            Root = clone.transform;

            if (!TryMeasurePivot(clone, out Vector3 centre))
            {
                Debug.LogError($"[GripCapture] Eşya prefab'ında görünür bir mesh yok: {prefab.name}");
                return false;
            }

            _pivotLocal = Root.InverseTransformPoint(centre);
            AddColliders(clone);
            return true;
        }

        /// <summary>Puts the pivot at <paramref name="pivotTarget"/> with this rotation.</summary>
        public void Place(Vector3 pivotTarget, Quaternion rotation)
        {
            if (Root == null)
            {
                return;
            }

            Root.rotation = rotation;
            Root.position += pivotTarget - PivotWorld;

            _placedPivot = pivotTarget;
            _placedRotation = rotation;
            Placed = true;
            Physics.SyncTransforms();
        }

        /// <summary>Back to the last placement (undoes the stick rotation).</summary>
        public void ResetPose()
        {
            if (Placed)
            {
                Place(_placedPivot, _placedRotation);
            }
        }

        /// <summary>Turns the item about an axis through its pivot.</summary>
        public void Rotate(float degrees, Vector3 axis)
        {
            if (Root == null || Mathf.Approximately(degrees, 0f) || axis.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Root.RotateAround(PivotWorld, axis, degrees);
            Physics.SyncTransforms();
        }

        private static bool TryMeasurePivot(GameObject clone, out Vector3 centre)
        {
            centre = Vector3.zero;
            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds bounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer))
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderers[i].bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(renderers[i].bounds);
            }

            centre = bounds.center;
            return any;
        }

        /// <summary>Removes everything but Transform and the mesh renderers from a subtree.</summary>
        public static void StripToRenderers(GameObject clone)
        {
            DisableNonLodRenderers(clone);

            for (int pass = 0; pass < StripPasses; pass++)
            {
                var components = clone.GetComponentsInChildren<Component>(true);

                // Order matters: scripts first (they hold the RequireComponent references), then
                // joints, then bodies, then the rest.
                Remove<MonoBehaviour>(components);
                Remove<Joint>(components);
                Remove<Rigidbody>(components);
                Remove<Collider>(components);
                RemoveRest(components);
            }
        }

        private static void Remove<T>(Component[] components) where T : Component
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i] is T && !(components[i] is Transform))
                {
                    Object.DestroyImmediate(components[i]);
                }
            }
        }

        private static void RemoveRest(Component[] components)
        {
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is MeshFilter ||
                    component is MeshRenderer || component is SkinnedMeshRenderer)
                {
                    continue;
                }

                Object.DestroyImmediate(component);
            }
        }

        /// <summary>
        /// Keeps only LOD0's renderers visible before the LODGroup goes: without the group every LOD
        /// level draws at once and the collision surface would be built from overlapping meshes.
        /// </summary>
        private static void DisableNonLodRenderers(GameObject clone)
        {
            var groups = clone.GetComponentsInChildren<LODGroup>(true);
            for (int g = 0; g < groups.Length; g++)
            {
                LOD[] lods = groups[g].GetLODs();
                for (int level = 1; level < lods.Length; level++)
                {
                    Renderer[] renderers = lods[level].renderers;
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r] != null)
                        {
                            renderers[r].gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        private static void FlattenLayers(GameObject clone)
        {
            var transforms = clone.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.layer = 0;
            }
        }

        /// <summary>
        /// A non-convex MeshCollider per mesh: the finger clamp marches capsules against the real
        /// surface, and a convex hull would fill every grip hollow.
        /// <para>⚠️ Non-readable meshes are NOT skipped: most item meshes are imported without
        /// Read/Write, and the editor still cooks them for MeshCollider.</para>
        /// </summary>
        private static void AddColliders(GameObject clone)
        {
            var filters = clone.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var collider = filters[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
            }

            var skinned = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                if (skinned[i].sharedMesh == null)
                {
                    continue;
                }

                // Baked once: the item never animates in the tool, so a static snapshot is the
                // collision surface.
                var mesh = new Mesh { name = $"{skinned[i].name}_Baked" };
                skinned[i].BakeMesh(mesh, true);

                var collider = skinned[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
            }
        }
    }
}
