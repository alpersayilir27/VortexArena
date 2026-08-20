using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Hit marker: an X that flashes briefly at the point of impact. The player's "I hit" feedback —
    /// nothing else changes on screen until health arrives from the server (§10.3).
    /// <para>ONLY the shooter sees it, structurally rather than by filtering: its single caller is
    /// <see cref="ArenaCombat.ReportHit"/>, which runs only on the client DEALING the damage. No
    /// protocol counterpart and none is added — a "hit marker" wire message would draw an X on the
    /// victim's own body too.</para>
    /// <para>The look is tuned in the editor, not in code: size, transparency, lifetime, curves,
    /// outline, material and optionally a prefab replacing the whole look live in the
    /// <see cref="HitMarkerStyle"/> asset
    /// (<c>Assets/_Shared/Data/Resources/HitMarkerStyle.asset</c>); without it the code defaults
    /// apply. ⚠️ The constants here are NOT settings — pool size and shader search chain are
    /// structural values with no meaning in the Inspector.</para>
    /// <para>No scene setup step (<see cref="ShotTracer"/> / <see cref="WeaponGranter"/> pattern):
    /// self-bootstraps on the first hit, goes <c>DontDestroyOnLoad</c> and keeps its pool across map
    /// changes. A scene component would add a manual step to every new arena — which is also why the
    /// settings live in <c>Resources</c> (there is no reference field to bind).</para>
    /// <para>Why the procedural X uses two <see cref="LineRenderer"/>s (not texture + quad): line
    /// geometry is crisp at every distance, needs no texture authoring/loading, and reuses
    /// <see cref="ShotTracer"/>'s proven path — shared material + vertex color, so no material
    /// instance splits the SRP batch. Two lines are required: an X cannot be a single polyline
    /// without drawing a connecting edge. Custom visuals use the prefab path.</para>
    /// <para>⚠️ The marker stays at a FIXED world point, it does not stick to the target: it shows
    /// where the hit landed, and parenting it would drag it along with an avatar that dies or
    /// teleports. Hence the short lifetime — the target cannot move meaningfully within it.</para>
    /// <para>⚠️ Not visible through walls (depth test on). It always sits where the ray REALLY
    /// landed, so a hitscan hit shows it by definition; an area-effect hit behind cover correctly
    /// stays hidden.</para>
    /// </summary>
    public class HitMarker : MonoBehaviour
    {
        /// <summary>Markers that can be alive at once.</summary>
        // At 600 RPM: 10 hits/s × 0.3 s lifetime ≈ 3 concurrent; headroom left for area effects
        // (n targets per blast). When full the oldest is cut — hiding a new hit is worse than an X
        // lingering one frame.
        private const int PoolSize = 12;

        /// <summary>Retry interval when <c>Camera.main</c> is missing (not searched every frame).</summary>
        private const float CameraRetrySeconds = 1f;

        /// <summary>
        /// Shader search chain for the material (first hit wins) — the SAME as
        /// <see cref="ShotTracer"/>'s, for the same reason: "Sprites/Default" is in Graphics
        /// Settings' <i>Always Included Shaders</i>, so it survives the build (a shader found only
        /// via <c>Shader.Find</c> and referenced by no material is STRIPPED, and the marker silently
        /// never draws on device). It also multiplies vertex color, so
        /// <c>LineRenderer.startColor</c> fading works.
        /// <para>⚠️ Only a FALLBACK: a material bound to
        /// <see cref="HitMarkerStyle.LineMaterial"/> wins.</para>
        /// </summary>
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>Sorting order of the outline lines — they must stay BEHIND the main line.</summary>
        // Same material + same queue: only sortingOrder decides draw order. Solving it with depth
        // (pushing the outline back 1 mm) invites z-fighting and, since the marker sits close to the
        // surface, would bury the outline inside it.
        private const int OutlineSortingOrder = -1;

        /// <summary>Pool node: all scene parts of one marker + where it currently sits.</summary>
        // ⚠️ A node carries ONE of two looks (procedural X or prefab instance) and learns which at
        // CONSTRUCTION: if the prefab setting changes the node is rebuilt (TakeNode).
        private sealed class MarkerNode
        {
            /// <summary>Node root — on the prefab path this is the prefab instance itself.</summary>
            public Transform Root;

            /// <summary>Procedural path: the X's two arms and (if any) the outline's two arms.</summary>
            public LineRenderer ArmA;
            public LineRenderer ArmB;
            public LineRenderer OutlineA;
            public LineRenderer OutlineB;

            /// <summary>Prefab path: the instance's particle systems (restarted on every hit).</summary>
            public ParticleSystem[] Particles;

            /// <summary>The prefab's own scale — the size multiplier scales it, never overwrites it.</summary>
            public Vector3 PrefabScale;

            /// <summary>Was this node built on the prefab path (to catch a settings change).</summary>
            public bool UsesPrefab;

            public bool Active;

            /// <summary>World point of the hit — the marker is rebuilt from it every frame.</summary>
            public Vector3 Anchor;

            /// <summary>Birth time (<c>Time.unscaledTime</c>) so a paused match does not freeze the
            /// marker on screen.</summary>
            public float StartAt;
        }

        private readonly MarkerNode[] _pool = new MarkerNode[PoolSize];
        private int _nextNode;

        private HitMarkerStyle _style;
        private bool _styleIsFallback;

        private Material _runtimeMaterial;
        private bool _warnedNoShader;

        private Camera _eye;
        private float _cameraRetryTimer;

        private static HitMarker _shared;

        /// <summary>
        /// The ONE pool every hit marker uses; self-bootstraps on first use and goes
        /// <c>DontDestroyOnLoad</c>. Never placed in a scene, never referenced — callers only say
        /// <c>HitMarker.Shared.Play(point)</c>.
        /// </summary>
        public static HitMarker Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[HitMarker]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<HitMarker>();
                }

                return _shared;
            }
        }

        /// <summary>
        /// Look settings: the asset in <c>Resources</c>, else an in-memory instance holding the code
        /// defaults. ⚠️ Created ONCE and destroyed in <see cref="OnDestroy"/> — calling
        /// <c>CreateInstance</c> per hit would leak in a project without the asset.
        /// </summary>
        private HitMarkerStyle Style
        {
            get
            {
                if (_style != null)
                {
                    return _style;
                }

                _style = HitMarkerStyle.Load();
                if (_style == null)
                {
                    _style = ScriptableObject.CreateInstance<HitMarkerStyle>();
                    _styleIsFallback = true;
                }

                return _style;
            }
        }

        /// <summary>
        /// Starts a hit marker at the given world point.
        /// <para>⚠️ Do NOT call directly: the single caller is <see cref="ArenaCombat.ReportHit"/>
        /// and must stay so. A second caller could show a marker without reporting the hit (lying to
        /// the player) or report it and forget to show one — the answer to "did I hit" would vary by
        /// damage source.</para>
        /// <para>Drawing happens in the first <c>LateUpdate</c> (same frame); here only a pool node
        /// is reserved, since the camera is not yet at its final pose for this frame.</para>
        /// </summary>
        /// <returns>Whether the marker was actually queued (<c>false</c> if no node could be built).</returns>
        public bool Play(Vector3 worldPoint)
        {
            MarkerNode node = TakeNode();
            if (node == null)
            {
                return false;
            }

            node.Anchor = worldPoint;
            node.StartAt = Time.unscaledTime;
            node.Active = true;

            // On the prefab path particles are restarted: a pooled instance may still carry the
            // previous hit's particles.
            if (node.UsesPrefab)
            {
                node.Root.gameObject.SetActive(true);
                RestartParticles(node);
            }

            return true;
        }

        /// <summary>
        /// Rebuilds live markers each frame: turns them to the eye, scales by distance, fades them,
        /// and hides expired ones (pool nodes are never destroyed).
        /// <para><c>LateUpdate</c> because the rig and head move in <c>Update</c>: an earlier pass
        /// would orient to last frame's viewpoint and the X would look skewed on fast head
        /// movement.</para>
        /// </summary>
        private void LateUpdate()
        {
            Camera eye = ResolveEye();
            HitMarkerStyle style = Style;
            float now = Time.unscaledTime;
            float lifetime = style.LifetimeSeconds;

            for (int i = 0; i < _pool.Length; i++)
            {
                MarkerNode node = _pool[i];
                if (node == null || !node.Active)
                {
                    continue;
                }

                if (node.Root == null)
                {
                    node.Active = false;
                    continue;
                }

                float t = (now - node.StartAt) / lifetime;

                // With no camera (scene change, admin rig disabled) the marker is hidden: leaving an
                // X frozen at its last orientation is worse than showing nothing.
                if (t >= 1f || eye == null)
                {
                    Hide(node);
                    continue;
                }

                Draw(node, style, eye, t);
            }
        }

        /// <summary>
        /// One frame of a marker: a fading X on a plane facing the eye, scaled by distance.
        /// <para>Arm directions come from the CAMERA's own right/up axes (not a look-at rotation
        /// from the point): a screen-parallel plane keeps the X readable even at the edge of the
        /// field of view and is consistent across both stereo eyes. The LIFT, however, is toward the
        /// eye — the correct direction to escape a surface is the vector from the point to the eye,
        /// not the camera's view axis.</para>
        /// </summary>
        private void Draw(MarkerNode node, HitMarkerStyle style, Camera eye, float t)
        {
            Transform eyeTransform = eye.transform;
            Vector3 toEye = eyeTransform.position - node.Anchor;
            float distance = toEye.magnitude;

            // Eye exactly at the hit point → direction undefined (muzzle pressed against oneself).
            if (distance < 1e-3f)
            {
                Hide(node);
                return;
            }

            float size = style.SizeAt(distance) * Mathf.Max(0f, style.SizeScaleAt(t));
            Vector3 center = node.Anchor + (toEye / distance) * style.SurfaceLiftMeters;

            if (node.UsesPrefab)
            {
                node.Root.position = center;
                node.Root.localScale = node.PrefabScale * size;
                if (style.FaceCamera)
                {
                    // Same rotation as the camera = screen-parallel. Unity's default Quad faces the
                    // camera in this state (its visible face is -Z).
                    node.Root.rotation = eyeTransform.rotation;
                }

                return;
            }

            // Half size: arms reach ±(right ± up) × half from the centre, so the X's bounding square
            // has edge exactly `size`.
            float half = size * 0.5f;
            Vector3 diagonalA = (eyeTransform.right + eyeTransform.up) * half;
            Vector3 diagonalB = (eyeTransform.right - eyeTransform.up) * half;

            float alpha = Mathf.Clamp01(style.AlphaAt(t));
            float thickness = style.ThicknessFor(size);
            Material material = EnsureMaterial(style);

            Color color = style.Color;
            color.a *= alpha;
            Apply(node.ArmA, material, center - diagonalA, center + diagonalA, color, thickness);
            Apply(node.ArmB, material, center - diagonalB, center + diagonalB, color, thickness);

            if (node.OutlineA == null || node.OutlineB == null)
            {
                return;
            }

            // The outline may be off in the settings: the node is already built, so it is only
            // hidden — no reason to rebuild it, the setting can be toggled in Play mode.
            if (!style.HasOutline)
            {
                node.OutlineA.enabled = false;
                node.OutlineB.enabled = false;
                return;
            }

            Color outline = style.OutlineColor;
            outline.a *= alpha;
            float outlineThickness = thickness * style.OutlineThicknessScale;
            Apply(node.OutlineA, material, center - diagonalA, center + diagonalA, outline, outlineThickness);
            Apply(node.OutlineB, material, center - diagonalB, center + diagonalB, outline, outlineThickness);
        }

        /// <summary>
        /// Writes one arm. The material is assigned only WHEN IT CHANGED: swapping it in Play mode
        /// takes effect immediately without a pointless assignment every frame.
        /// </summary>
        private static void Apply(LineRenderer line, Material material, in Vector3 from, in Vector3 to,
            in Color color, float width)
        {
            if (material != null && line.sharedMaterial != material)
            {
                line.sharedMaterial = material;
            }

            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.enabled = true;
        }

        private static void Hide(MarkerNode node)
        {
            node.Active = false;

            if (node.UsesPrefab)
            {
                if (node.Root != null)
                {
                    node.Root.gameObject.SetActive(false);
                }

                return;
            }

            SetEnabled(node.ArmA, false);
            SetEnabled(node.ArmB, false);
            SetEnabled(node.OutlineA, false);
            SetEnabled(node.OutlineB, false);
        }

        private static void SetEnabled(LineRenderer line, bool value)
        {
            if (line != null)
            {
                line.enabled = value;
            }
        }

        private static void RestartParticles(MarkerNode node)
        {
            if (node.Particles == null)
            {
                return;
            }

            for (int i = 0; i < node.Particles.Length; i++)
            {
                ParticleSystem ps = node.Particles[i];
                if (ps == null)
                {
                    continue;
                }

                ps.Clear(true);
                ps.Play(true);
            }
        }

        // ---------------------------------------------------------------------- pool

        /// <summary>
        /// Round-robin: returns the next (oldest) node, creating it lazily.
        /// <para>A node built with the WRONG look (the settings prefab was bound or removed later)
        /// is destroyed and rebuilt — otherwise binding a prefab would require restarting Play.</para>
        /// </summary>
        private MarkerNode TakeNode()
        {
            HitMarkerStyle style = Style;
            bool wantsPrefab = style.MarkerPrefab != null;

            MarkerNode node = _pool[_nextNode];
            if (node != null && (node.Root == null || node.UsesPrefab != wantsPrefab))
            {
                if (node.Root != null)
                {
                    Destroy(node.Root.gameObject);
                }

                node = null;
            }

            if (node == null)
            {
                node = wantsPrefab ? CreatePrefabNode(style) : CreateLineNode(style);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private MarkerNode CreatePrefabNode(HitMarkerStyle style)
        {
            GameObject instance = Instantiate(style.MarkerPrefab, transform);
            instance.name = "[HitMark]";
            instance.SetActive(false);

            return new MarkerNode
            {
                Root = instance.transform,
                UsesPrefab = true,
                PrefabScale = style.MarkerPrefab.transform.localScale,
                Particles = instance.GetComponentsInChildren<ParticleSystem>(true),
            };
        }

        private MarkerNode CreateLineNode(HitMarkerStyle style)
        {
            var root = new GameObject("[HitMark]");
            root.transform.SetParent(transform, false);

            Material material = EnsureMaterial(style);

            // ⚠️ Outline nodes are created even when the setting is off, so enabling it in Play mode
            // does not leave half the pool with outlines and half without (creation is one-off and
            // costs only two disabled components while off).
            return new MarkerNode
            {
                Root = root.transform,
                UsesPrefab = false,
                OutlineA = CreateArm(root.transform, material, "OutlineA", OutlineSortingOrder),
                OutlineB = CreateArm(root.transform, material, "OutlineB", OutlineSortingOrder),
                ArmA = CreateArm(root.transform, material, "ArmA", 0),
                ArmB = CreateArm(root.transform, material, "ArmB", 0),
            };
        }

        private static LineRenderer CreateArm(Transform parent, Material material, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.sortingOrder = sortingOrder;
            // View: the strip turns to each camera in its own plane, so arms do not thin out and
            // vanish when seen edge-on (positions are already built facing the eye; this is the
            // second safety net).
            line.alignment = LineAlignment.View;
            // The marker is an indicator, not a light source: shadows/probes off (Quest budget).
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return line;
        }

        /// <summary>
        /// Line material: the one from the settings (your own glow material) first, else the runtime
        /// material SHARED by all markers. Color comes from vertex color, so no per-marker material
        /// instance is created (which would split the SRP batch).
        /// </summary>
        private Material EnsureMaterial(HitMarkerStyle style)
        {
            if (style.LineMaterial != null)
            {
                return style.LineMaterial;
            }

            if (_runtimeMaterial != null)
            {
                return _runtimeMaterial;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _runtimeMaterial = new Material(shader) { name = "M_HitMarker(runtime)" };
                    return _runtimeMaterial;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[HitMarker] İsabet göstergesi için shader bulunamadı (Sprites/Default dahil) — " +
                    "işaret çizilmeyecek. Graphics Settings > Always Included Shaders listesini kontrol " +
                    "et ya da HitMarkerStyle.asset'e kendi materyalini bağla.");
            }

            return null;
        }

        /// <summary>
        /// The viewing camera (<c>Camera.main</c>); retried once a second when missing — searching
        /// <c>Camera.main</c> every frame means a tag scan.
        /// </summary>
        private Camera ResolveEye()
        {
            if (_eye != null)
            {
                return _eye;
            }

            _cameraRetryTimer -= Time.unscaledDeltaTime;
            if (_cameraRetryTimer > 0f)
            {
                return null;
            }

            _cameraRetryTimer = CameraRetrySeconds;
            _eye = Camera.main;
            return _eye;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static field on a destroyed component: the next Play request
                // rebuilds the pool (required when domain reload is disabled).
                _shared = null;
            }

            if (_runtimeMaterial != null)
            {
                // Runtime-generated: destroyed by hand, else it leaks on every Play exit when domain
                // reload is disabled. (The settings material is an ASSET and is left alone.)
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }

            if (_styleIsFallback && _style != null)
            {
                // In-memory instance created because the asset was missing — also a Unity object.
                Destroy(_style);
            }

            _style = null;
        }
    }
}
