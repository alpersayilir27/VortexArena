using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Net;
using VortexArena.Protocol;
using Random = UnityEngine.Random;

namespace VortexArena.Modes.Burger
{
    /// <summary>The ONE pool of ketchup stains: a ring of flat quads laid on whatever a droplet hit.
    /// Self-bootstrapping and <c>DontDestroyOnLoad</c> (the <c>CasingPool</c> pattern) — nothing in a
    /// scene references it, callers only say <c>BurgerSplatPool.Shared.Add(...)</c>.
    /// <para>⚠️ The pool must OUTLIVE the bottle: a stain lives in world space while the stream
    /// component that created it can be destroyed with the arena, and a lifetime driven from there
    /// would leave every live stain visible forever.</para>
    /// <para>⚠️ <c>DecalProjector</c> is NOT used (<c>Yapma-Listesi.md</c>): the project has no decal
    /// renderer feature and projectors are expensive on Quest. A stain is a quad.</para></summary>
    internal sealed class BurgerSplatPool : MonoBehaviour
    {
        /// <summary>Simultaneous stains; at the cap the OLDEST slot is reused.</summary>
        /// <remarks>⚠️ The cap is a draw-call budget, not a taste: every stain is one more quad in the
        /// arena for the rest of the shift.</remarks>
        private const int Capacity = 300;

        /// <summary>How long a stain stays at full opacity (s).</summary>
        private const float HoldSeconds = 4f;

        /// <summary>Fade at the end of the hold (s). A hard cut reads as "the stain blinked out".</summary>
        private const float FadeSeconds = 1f;

        /// <summary>Lift off the hit surface (m) — a quad exactly on the surface z-fights it.</summary>
        private const float SurfaceOffset = 0.004f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static BurgerSplatPool _shared;

        public static BurgerSplatPool Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[BurgerSplatPool]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<BurgerSplatPool>();
                }

                return _shared;
            }
        }

        private readonly Transform[] _slots = new Transform[Capacity];
        private readonly Renderer[] _renderers = new Renderer[Capacity];

        /// <summary>Prefab each slot was built from — a slot parented to a dying avatar bone comes back
        /// as <c>null</c> and has to be rebuilt.</summary>
        private readonly GameObject[] _prefabs = new GameObject[Capacity];

        /// <summary>When the slot's fade starts; <c>0</c> = unused.</summary>
        private readonly float[] _fadeAt = new float[Capacity];

        private readonly Color[] _tints = new Color[Capacity];

        private int _next;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();

            // A new match on the same scene does not reload it, so the shift's stains are cleared on
            // the match edges rather than on scene load alone.
            SceneManager.sceneLoaded += HandleSceneLoaded;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => ClearAll();

        private void HandleLoadMatch(LoadMatchMsg msg) => ClearAll();

        private void HandleMatchEnd(MatchEndMsg msg) => ClearAll();

        private void HandleReturnToLobby(ReturnToLobbyMsg msg) => ClearAll();

        /// <summary>Lays a stain on a surface.</summary>
        /// <param name="prefab">Stain quad (Unlit, alpha-clipped); the pool is built from it.</param>
        /// <param name="attachTo">Bone/transform the stain rides along with, or null for world space —
        /// a stain on an avatar must follow the body it landed on.</param>
        /// <param name="tint">Colour written into <c>_BaseColor</c>; its alpha is the stain's peak.</param>
        public void Add(GameObject prefab, Vector3 worldPoint, Vector3 worldNormal, Transform attachTo,
            Color tint, float scale)
        {
            if (prefab == null)
            {
                return;
            }

            int i = _next;
            _next = (_next + 1) % Capacity;

            if (_slots[i] == null || _prefabs[i] != prefab)
            {
                if (_slots[i] != null)
                {
                    Destroy(_slots[i].gameObject);
                }

                GameObject instance = Instantiate(prefab, transform);
                _slots[i] = instance.transform;
                _renderers[i] = instance.GetComponentInChildren<Renderer>(true);
                _prefabs[i] = prefab;
            }

            Transform slot = _slots[i];

            // The quad's own +Z looks along the surface normal; the roll is random so the same texture
            // does not tile visibly over a counter.
            Quaternion rotation = Quaternion.LookRotation(worldNormal.sqrMagnitude > 0.0001f
                ? worldNormal
                : Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            // No bone to ride = the POOL root, not the scene: a parentless stain is destroyed with the
            // arena and its slot could never be used again (the CasingPool rationale).
            slot.SetParent(attachTo != null ? attachTo : transform, true);
            slot.SetPositionAndRotation(worldPoint + worldNormal.normalized * SurfaceOffset, rotation);
            slot.localScale = Vector3.one * scale;
            slot.gameObject.SetActive(true);

            _fadeAt[i] = Time.time + HoldSeconds;
            _tints[i] = tint;
            Draw(i, tint.a);
        }

        /// <summary>Hides every stain — a new shift does not start in the last one's ketchup.</summary>
        public void ClearAll()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _fadeAt[i] = 0f;

                if (_slots[i] == null)
                {
                    continue;
                }

                // Back under the pool: the stain's parent may be an avatar bone that is about to die,
                // and the slot would come back null and have to be rebuilt.
                _slots[i].SetParent(transform, false);
                _slots[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            float now = Time.time;

            for (int i = 0; i < Capacity; i++)
            {
                if (_fadeAt[i] <= 0f || _slots[i] == null)
                {
                    continue;
                }

                float elapsed = now - _fadeAt[i];
                if (elapsed < 0f)
                {
                    continue;
                }

                if (FadeSeconds <= 0f || elapsed >= FadeSeconds)
                {
                    _fadeAt[i] = 0f;
                    _slots[i].SetParent(transform, false);
                    _slots[i].gameObject.SetActive(false);
                    continue;
                }

                Draw(i, _tints[i].a * (1f - elapsed / FadeSeconds));
            }
        }

        /// <summary>Alpha through a property block — the stains share one material so they stay batched
        /// (a per-instance material on Quest is a draw call each).</summary>
        private void Draw(int index, float alpha)
        {
            Renderer renderer = _renderers[index];
            if (renderer == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_propertyBlock);

            // ⚠️ .linear is REQUIRED: the project renders in Linear space and SetColor does no
            // conversion (the DamageVignette rationale).
            Color color = _tints[index].linear;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
