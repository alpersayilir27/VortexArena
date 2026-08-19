using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Shot tracer: a fading line from muzzle to impact plus smoke along it. Draw-only,
    /// never placed in a scene; <see cref="Weapon"/> drives the local shooter,
    /// <see cref="RemoteShotFx"/> remote players (§6.4/6.5) — two callers are mandatory, the server
    /// never echoes a shot back.
    /// <para>⚠️ One trigger pull is NOT one line: a whole pellet spread goes through a SINGLE
    /// <see cref="Play"/> call, since smoke budget and width are scaled per volley.</para>
    /// <para>⚠️ Smoke has NO separate entry point (emitted inside <see cref="Play"/>): a second
    /// gate would let one caller forget it and smoke would differ between screens.</para>
    /// <para>⚠️ The pool is SHARED (<see cref="Shared"/>), never per caller: guns are spawned
    /// and destroyed constantly in <c>weaponSource:"random"</c> modes.</para>
    /// <para>⚠️ Pooled and round-robin: Instantiate/Destroy at ~53 events/s is a GC spike on
    /// Quest. When the pool is full the oldest tracer is cut.</para>
    /// <para>Smoke has no pool: all shots <c>Emit</c> into ONE <see cref="ParticleSystem"/> (one
    /// draw call). Look comes from <see cref="ItemDefinition"/> and smoke is DERIVED from its three
    /// tracer fields, so <c>tracerEveryNthRound</c> disables both at once.</para></summary>
    public class ShotTracer : MonoBehaviour
    {
        // ⚠️ Sized by PER-FRAME burst, not average rate: an average-sized pool lets two
        // overlapping volleys cut each other's lines ("some pellets leave no trail").
        private const int PoolSize = 48;

        private const float MinTracerMeters = 0.5f;

        /// <summary>Max lines (pellets) per volley; BOTH callers clamp to this. Lives here because
        /// it shares the <see cref="PoolSize"/> budget — caller-owned caps would outgrow the
        /// pool.</summary>
        public const int MaxScatterLines = 12;

        /// <summary>Line thinning for spread shots: single-round width turns 6-9 lines into one
        /// opaque funnel and hides the spread angle. Derived, not a separate item field (two would
        /// drift).</summary>
        private const float ScatterWidthScale = 0.6f;

        /// <summary>Shader lookup chain for the line material (first hit wins).</summary>
        // ⚠️ "Sprites/Default" first: it is in Always Included Shaders, so it survives the build.
        // A Shader.Find-only shader no material references is STRIPPED and tracers silently
        // disappear on device. It also multiplies vertex color, so LineRenderer.startColor works.
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>Shader lookup chain for smoke. "Sprites/Default" first for the same reason as
        /// the line, and it multiplies particle vertex color (how per-puff alpha works). The URP
        /// particle shader stays a fallback: it is not in Always Included Shaders.</summary>
        private static readonly string[] SmokeShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
        };

        // ---------------------------------------------------------- smoke (derived from tracer)

        private const float SmokePuffSpacingMeters = 0.75f;

        private const int SmokePuffsMin = 2;
        private const int SmokePuffsMax = 14;

        /// <summary>Particle cap. ⚠️ At the cap the system drops NEW emission (opposite of the line
        /// pool's "cut oldest") — accepted because smoke is cosmetic.</summary>
        private const int SmokeMaxParticles = 384;

        /// <summary>Smoke lifetime = tracer lifetime × this, clamped to the band below. ⚠️ Keep it
        /// SMALL: smoke is residue BEHIND the fading line; much longer leaves unexplained grey
        /// blobs, exactly equal kills the puff before it can be seen dispersing.</summary>
        private const float SmokeLifetimeScale = 2.5f;
        private const float SmokeLifetimeMinSeconds = 0.15f;
        private const float SmokeLifetimeMaxSeconds = 0.5f;

        /// <summary>Puff size = tracer width × (Near at muzzle, Far at impact); grows along path.</summary>
        private const float SmokeSizeNearScale = 3f;
        private const float SmokeSizeFarScale = 8f;

        private const float SmokeAlphaNear = 0.4f;
        private const float SmokeAlphaFar = 0.04f;

        /// <summary>Far-end lifetime vs muzzle end: the far end dies first, so the trail is eaten
        /// back from impact to muzzle. Alpha alone does not carry that direction.</summary>
        private const float SmokeFarLifetimeScale = 0.55f;

        /// <summary>Slight upward drift (m/s) — smoke rises slowly, never falls.</summary>
        private const float SmokeDriftMetersPerSecond = 0.12f;

        private const float SmokeJitterOfSize = 0.35f;

        /// <summary>Tracer color bleed into the smoke tint; full tint would give a coloured cloud.</summary>
        private const float SmokeTintFromTracer = 0.25f;

        /// <summary>Smoke texture edge length (px) — enough for a soft radial falloff.</summary>
        private const int SmokeTextureSize = 32;

        // ------------------------------------------------------------------ line fade

        /// <summary>Fraction of the lifetime held at FULL brightness before the fade starts.
        /// ⚠️ Never zero: at 0.1 s (≈7 frames) a tracer fading from frame one reads as a pale
        /// ghost, not a line.</summary>
        private const float FadeHoldFraction = 0.25f;

        /// <summary>Width left at the end of the fade (1f disables thinning). Alpha alone leaves
        /// the trail equally thick; thinning reads as dispersing.</summary>
        private const float FadeEndWidthScale = 0.55f;

        private sealed class TracerNode
        {
            public LineRenderer Line;
            public bool Active;

            /// <summary>Fade axis: birth time + lifetime (<c>Time.unscaledTime</c>). ⚠️ No ExpireAt
            /// field — a second copy of the same info would drift.</summary>
            public float StartAt;
            public float Duration;

            /// <summary>Fade base values, recomputed from every frame. ⚠️ NEVER read back from the
            /// line: that value is already faded and the trail would fade compounding.</summary>
            public Color BaseColor;
            public float BaseWidth;
        }

        private readonly TracerNode[] _pool = new TracerNode[PoolSize];
        private int _nextNode;

        private Material _material;
        private bool _warnedNoShader;

        private ParticleSystem _smoke;
        private Material _smokeMaterial;
        private Texture2D _smokeTexture;
        private bool _smokeSetupFailed;
        private bool _warnedNoSmokeShader;

        private static ShotTracer _shared;

        /// <summary>The ONE pool every tracer uses; self-bootstraps and goes
        /// <c>DontDestroyOnLoad</c> so a map change does not rebuild pool + material. Never placed
        /// in a scene, never referenced.</summary>
        public static ShotTracer Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ShotTracer]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ShotTracer>();
                }

                return _shared;
            }
        }

        /// <summary>Draws ALL trails of one trigger pull: <paramref name="count"/> lines plus ONE
        /// smoke budget spread over the volley. Returns the lines actually drawn. Invalid (≤0)
        /// lifetime or width falls back to a safe default; degenerately short lines are skipped.
        /// <para>⚠️ NO single-round overload: normal guns pass a one-element array. Two signatures
        /// would let fade/smoke/pool behaviour drift.</para>
        /// <para>⚠️ Smoke is NOT multiplied per pellet: a per-pellet budget fills
        /// <see cref="SmokeMaxParticles"/> on one shot (later shots silently lose smoke) and paints
        /// an opaque fill-rate wall at the muzzle. Puffs are dealt round-robin across
        /// pellets.</para>
        /// <para>⚠️ All lines share ONE origin (the muzzle); endpoints come from the caller, the
        /// only side that knows pellet direction/range. This class only draws.</para></summary>
        /// <param name="toPoints">Pellet endpoints (world space); first <paramref name="count"/> read.</param>
        /// <param name="count">Pellets to draw; clamped to <see cref="MaxScatterLines"/>.</param>
        public int Play(Vector3 from, Vector3[] toPoints, int count, Color color, float width, float lifetime)
        {
            if (toPoints == null)
            {
                return 0;
            }

            count = Mathf.Min(count, Mathf.Min(toPoints.Length, MaxScatterLines));
            if (count < 1)
            {
                return 0;
            }

            Material material = EnsureMaterial();
            if (material == null)
            {
                return 0;
            }

            float w = width > 0f ? width : 0.02f;
            if (count > 1)
            {
                w *= ScatterWidthScale;
            }

            float life = lifetime > 0f ? lifetime : 0.06f;
            float now = Time.unscaledTime;

            int drawn = 0;
            float longestSqr = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 to = toPoints[i];
                float sqrDistance = (to - from).sqrMagnitude;
                if (sqrDistance < MinTracerMeters * MinTracerMeters)
                {
                    // A degenerate pellet does not affect the rest of the volley.
                    continue;
                }

                TracerNode node = TakeNode(material);
                if (node == null || node.Line == null)
                {
                    continue;
                }

                node.Line.startWidth = w;
                node.Line.endWidth = w;
                node.Line.startColor = color;
                node.Line.endColor = color;
                node.Line.SetPosition(0, from);
                node.Line.SetPosition(1, to);
                node.Line.enabled = true;

                // Time.unscaledTime so a paused match (timeScale 0) does not freeze tracers.
                // ⚠️ The whole volley shares ONE stamp (not re-read inside the loop): trails of a
                // single trigger pull must be born and fade together.
                node.StartAt = now;
                node.Duration = life;
                node.BaseColor = color;
                node.BaseWidth = w;
                node.Active = true;

                drawn++;
                longestSqr = Mathf.Max(longestSqr, sqrDistance);
            }

            if (drawn > 0)
            {
                EmitSmoke(from, toPoints, count, Mathf.Sqrt(longestSqr), color, w, life);
            }

            return drawn;
        }

        /// <summary>Fades live lines (alpha down, width thinner) and hides expired ones; pool nodes
        /// are disabled, never destroyed. The fade spans the lifetime ITSELF — no separate
        /// fade-duration field, or <c>tracerLifetime</c> would stop meaning "how long the trail
        /// lasts". Only live nodes are touched, and only vertex color/width (no material instance,
        /// no SRP break).</summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                TracerNode node = _pool[i];
                if (node == null || !node.Active)
                {
                    continue;
                }

                if (node.Line == null)
                {
                    node.Active = false;
                    continue;
                }

                float t = (now - node.StartAt) / node.Duration;
                if (t >= 1f)
                {
                    node.Active = false;
                    node.Line.enabled = false;
                    continue;
                }

                Color color = node.BaseColor;
                color.a = node.BaseColor.a * FadeAlphaAt(t);
                node.Line.startColor = color;
                node.Line.endColor = color;

                float width = node.BaseWidth * Mathf.Lerp(1f, FadeEndWidthScale, t);
                node.Line.startWidth = width;
                node.Line.endWidth = width;
            }
        }

        /// <summary>Fade curve: 1 → 0 for <paramref name="t"/> 0 (birth) → 1 (end of life). Not
        /// linear — after a short hold it accelerates with <c>1 − u²</c>, which reads as a tracer
        /// burning out rather than a dimming lamp.</summary>
        private static float FadeAlphaAt(float t)
        {
            if (t <= FadeHoldFraction)
            {
                return 1f;
            }

            float u = (t - FadeHoldFraction) / (1f - FadeHoldFraction);
            return 1f - u * u;
        }

        // ---------------------------------------------------------------------- smoke

        /// <summary>Emits smoke along the bullet path: dense/small/long-lived at the muzzle,
        /// faint/wide/short-lived toward the impact, so the trail dissipates in that direction.
        /// <para>⚠️ Puff count DERIVES from distance: a fixed count stacks into an opaque blob on
        /// short shots and leaves disconnected dots on long ones.</para>
        /// <para>⚠️ The budget belongs to one trigger pull, not one pellet (see
        /// <see cref="Play"/>); puffs are dealt round-robin across pellets so one budget sweeps the
        /// whole fan.</para>
        /// <para>No timing work here: particles die by <c>startLifetime</c> on
        /// <c>useUnscaledTime</c>, the same clock the line fade uses.</para></summary>
        /// <param name="distance">LONGEST path in the volley (drives the puff count).</param>
        private void EmitSmoke(
            Vector3 from, Vector3[] toPoints, int count, float distance, Color color, float width, float lifetime)
        {
            ParticleSystem smoke = EnsureSmoke();
            if (smoke == null)
            {
                return;
            }

            int puffs = Mathf.Clamp(
                Mathf.RoundToInt(distance / SmokePuffSpacingMeters), SmokePuffsMin, SmokePuffsMax);

            float baseLife = Mathf.Clamp(
                lifetime * SmokeLifetimeScale, SmokeLifetimeMinSeconds, SmokeLifetimeMaxSeconds);

            // Mostly neutral grey, just enough tracer tint to be recognisable.
            Color tint = Color.Lerp(new Color(0.62f, 0.62f, 0.64f), color, SmokeTintFromTracer);

            // Struct reused across the loop: zero allocations.
            var emit = new ParticleSystem.EmitParams();

            for (int i = 0; i < puffs; i++)
            {
                // t=0 muzzle, t=1 impact. Guard stays: lowering SmokePuffsMin would give NaN.
                float t = puffs > 1 ? i / (float)(puffs - 1) : 0f;

                float size = width * Mathf.Lerp(SmokeSizeNearScale, SmokeSizeFarScale, t);

                tint.a = Mathf.Lerp(SmokeAlphaNear, SmokeAlphaFar, t);

                // Round-robin across pellets: each puff moves to the next pellet's path, so one
                // budget sweeps the whole cone.
                Vector3 path = Vector3.Lerp(from, toPoints[i % count], t);

                emit.position = path + Random.insideUnitSphere * (size * SmokeJitterOfSize);
                emit.startSize = size;
                emit.startColor = tint;
                emit.startLifetime = baseLife * Mathf.Lerp(1f, SmokeFarLifetimeScale, t);
                emit.velocity = Vector3.up * SmokeDriftMetersPerSecond;
                emit.rotation = Random.Range(0f, 360f);

                smoke.Emit(emit, 1);
            }
        }

        /// <summary>The smoke system SHARED by all shots (one system = one draw call). The emission
        /// module is DISABLED: particles are born only from <see cref="EmitSmoke"/>'s manual
        /// <c>Emit</c>. Set up once; if it fails (no shader) it is never retried.</summary>
        private ParticleSystem EnsureSmoke()
        {
            if (_smoke != null)
            {
                return _smoke;
            }

            if (_smokeSetupFailed)
            {
                return null;
            }

            Material material = EnsureSmokeMaterial();
            if (material == null)
            {
                _smokeSetupFailed = true;
                return null;
            }

            var go = new GameObject("[TracerSmoke]");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = SmokeMaxParticles;
            // World is explicit: Local would drag every trail along if the root ever moved.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Same clock as the line fade (Time.unscaledTime), so a paused match keeps both fading.
            main.useUnscaledTime = true;
            main.gravityModifier = 0f;
            // Puff size is computed in metres, so no scale is inherited from the root.
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            // ⚠️ AlwaysSimulate: the root sits at the world origin while puffs are all over the
            // arena. Automatic culling would call the system off-screen and freeze smoke mid-air.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            // Dissipation ramp. MULTIPLIES startColor, so the per-puff alpha survives.
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.75f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 1.9f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            // Smoke is a trail, not a light source: shadows/probes off (Quest budget).
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // playOnAwake off: start by hand, then idle empty waiting for Emit.
            ps.Play();

            _smoke = ps;
            return _smoke;
        }

        /// <summary>Smoke material: must be a SEPARATE instance from the line's — it carries a
        /// texture, and writing that into the shared material would texture the
        /// <see cref="LineRenderer"/> too.</summary>
        private Material EnsureSmokeMaterial()
        {
            if (_smokeMaterial != null)
            {
                return _smokeMaterial;
            }

            for (int i = 0; i < SmokeShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(SmokeShaderCandidates[i]);
                if (shader == null)
                {
                    continue;
                }

                _smokeMaterial = new Material(shader) { name = "M_TracerSmoke(runtime)" };
                _smokeMaterial.mainTexture = EnsureSmokeTexture();
                return _smokeMaterial;
            }

            if (!_warnedNoSmokeShader)
            {
                _warnedNoSmokeShader = true;
                Debug.LogWarning(
                    "[ShotTracer] Duman izi için shader bulunamadı (Sprites/Default dahil) — mermi " +
                    "izi dumansız çizilecek. Graphics Settings > Always Included Shaders listesini kontrol et.");
            }

            return null;
        }

        /// <summary>Soft radial-falloff smoke texture, generated at runtime (32×32 RGBA ≈ 4 KB,
        /// built once). ⚠️ The project's smoke texture is not under <c>Resources/</c>, and a
        /// serialized field would turn <see cref="ShotTracer"/> into a component every arena must
        /// place.</summary>
        private Texture2D EnsureSmokeTexture()
        {
            if (_smokeTexture != null)
            {
                return _smokeTexture;
            }

            var texture = new Texture2D(SmokeTextureSize, SmokeTextureSize, TextureFormat.RGBA32, false)
            {
                name = "T_TracerSmoke(runtime)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[SmokeTextureSize * SmokeTextureSize];
            float half = SmokeTextureSize * 0.5f;

            for (int y = 0; y < SmokeTextureSize; y++)
            {
                for (int x = 0; x < SmokeTextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;

                    // Squared falloff: a soft puff, not a hard-edged disc.
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    byte alpha = (byte)(falloff * falloff * 255f);

                    pixels[y * SmokeTextureSize + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            // makeNoLongerReadable: drop the CPU copy (never read again).
            texture.Apply(false, true);

            _smokeTexture = texture;
            return _smokeTexture;
        }

        // ---------------------------------------------------------------------- pool

        private TracerNode TakeNode(Material material)
        {
            TracerNode node = _pool[_nextNode];
            if (node == null || node.Line == null)
            {
                node = CreateNode(material);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private TracerNode CreateNode(Material material)
        {
            var go = new GameObject("[Tracer]");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            // A tracer is a trail, not a light source: shadows/probes off (Quest budget).
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return new TracerNode { Line = line };
        }

        /// <summary>The material SHARED by all tracers; color comes from LineRenderer vertex color,
        /// since a per-item material instance would break the SRP batch.</summary>
        private Material EnsureMaterial()
        {
            if (_material != null)
            {
                return _material;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _material = new Material(shader) { name = "M_ShotTracer(runtime)" };
                    return _material;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[ShotTracer] Tracer için shader bulunamadı (Sprites/Default dahil) — mermi izi " +
                    "çizilmeyecek. Graphics Settings > Always Included Shaders listesini kontrol et.");
            }

            return null;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static field on a destroyed component: the next Play rebuilds
                // the pool (required when domain reload is disabled).
                _shared = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }

            // Smoke material and texture are runtime-generated too: destroy both by hand or they
            // leak on every Play exit when domain reload is disabled.
            if (_smokeMaterial != null)
            {
                Destroy(_smokeMaterial);
                _smokeMaterial = null;
            }

            if (_smokeTexture != null)
            {
                Destroy(_smokeTexture);
                _smokeTexture = null;
            }
        }
    }
}
