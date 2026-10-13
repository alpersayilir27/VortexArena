using System.Collections;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Drives the zombie body's dissolve: <c>_Dissolve</c> 1→0 when it materialises, 0→1 when
    /// it leaves. The model is temporarily switched to the shared dissolve material.</summary>
    /// <remarks>Same shape as <c>SimpleWeaponDissolve</c> but without the <c>Weapon</c> gate: here the
    /// trigger is the net state (first pose · relocation · broken), so <see cref="ZombieView"/> calls
    /// it directly.
    /// <para>⚠️ The body's look is carried into the effect through a
    /// <see cref="MaterialPropertyBlock"/> (albedo + colour): the dissolve material is a SINGLE shared
    /// asset and cannot know which of the five looks it is on — without this every zombie dissolves as
    /// the same flat silhouette.</para>
    /// <para>⚠️ After APPEARING the original material is restored. A renderer left with a property
    /// block never enters the SRP Batcher, and that cost would ride every zombie for its whole
    /// life.</para></remarks>
    [DisallowMultipleComponent]
    public class ZombieDissolve : MonoBehaviour
    {
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Warned once per session: the material is missing on every look at once (one builder run), so
        // per-instance logging would just repeat the same line thirty times a wave.
        private static bool _warnedNoMaterial;

        [Header("Materyal")]
        [Tooltip("Geçiş boyunca modele takılan çözülme materyali — " +
                 "Assets/_Shared/Materials/DissolveEffect.mat. Efektin GÖRÜNÜMÜ bu materyalde ayarlanır.")]
        [SerializeField] private Material dissolveMaterial;

        [Header("Hedef")]
        [Tooltip("Boş bırakılırsa alt hiyerarşideki SkinnedMeshRenderer'lar kullanılır.")]
        [SerializeField] private Renderer[] targets;

        private Material[][] _originalMaterials;
        private Material[][] _dissolveMaterials;
        private MaterialPropertyBlock[] _blocks;
        private bool _swapped;
        private Coroutine _routine;

        private void Awake()
        {
            if (targets == null || targets.Length == 0)
            {
                targets = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            _originalMaterials = new Material[targets.Length][];
            _dissolveMaterials = new Material[targets.Length][];
            _blocks = new MaterialPropertyBlock[targets.Length];

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                {
                    continue;
                }

                // sharedMaterials returns a FRESH array per call — the copy we keep is ours.
                _originalMaterials[i] = targets[i].sharedMaterials;
                _dissolveMaterials[i] = new Material[_originalMaterials[i].Length];
                _blocks[i] = new MaterialPropertyBlock();
            }
        }

        private void OnDisable()
        {
            // ⚠️ The coroutine dies with the object; without restoring, the body would come back
            // half dissolved the next time this look is used.
            _routine = null;
            Restore();
        }

        /// <summary>Materialises the body (<c>_Dissolve</c> 1 → 0) and restores the original material at
        /// the end.</summary>
        public void Appear(float seconds)
        {
            Run(Drive(1f, 0f, seconds, true));
        }

        /// <summary>Dissolves the body away (<c>_Dissolve</c> 0 → 1) and LEAVES it gone — the server
        /// despawns the object itself.</summary>
        public void Vanish(float seconds)
        {
            Run(Drive(0f, 1f, seconds, false));
        }

        private void Run(IEnumerator routine)
        {
            if (dissolveMaterial == null)
            {
                WarnNoMaterial();
                return;
            }

            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
            }

            _routine = StartCoroutine(routine);
        }

        private IEnumerator Drive(float from, float to, float seconds, bool restoreAtEnd)
        {
            Swap(from);

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;

                // SmoothStep, not linear: linear reads machine-like in VR.
                SetDissolve(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds)));
                yield return null;
            }

            SetDissolve(to);

            if (restoreAtEnd)
            {
                Restore();
            }

            _routine = null;
        }

        private void Swap(float initial)
        {
            if (_swapped || targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Renderer target = targets[i];
                if (target == null || _blocks[i] == null)
                {
                    continue;
                }

                Material source = _originalMaterials[i].Length > 0 ? _originalMaterials[i][0] : null;

                _blocks[i].Clear();
                WriteAppearance(_blocks[i], source);
                _blocks[i].SetFloat(DissolveId, initial);
                target.SetPropertyBlock(_blocks[i]);

                for (int slot = 0; slot < _dissolveMaterials[i].Length; slot++)
                {
                    _dissolveMaterials[i][slot] = dissolveMaterial;
                }

                // ⚠️ sharedMaterials, not materials: the latter leaks a material COPY per renderer, and
                // a wave spawns thirty of them.
                target.sharedMaterials = _dissolveMaterials[i];
            }

            _swapped = true;
        }

        private void Restore()
        {
            if (!_swapped || targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                {
                    continue;
                }

                targets[i].SetPropertyBlock(null);
                targets[i].sharedMaterials = _originalMaterials[i];
            }

            _swapped = false;
        }

        private void SetDissolve(float value)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null || _blocks[i] == null)
                {
                    continue;
                }

                _blocks[i].SetFloat(DissolveId, value);
                targets[i].SetPropertyBlock(_blocks[i]);
            }
        }

        /// <summary>Carries the original material's look into the shared dissolve material. A missing
        /// texture is not written at all — a <c>null</c> texture in the block throws.</summary>
        private static void WriteAppearance(MaterialPropertyBlock block, Material source)
        {
            if (source == null)
            {
                return;
            }

            Texture albedo = ReadTexture(source, BaseMapId) ?? ReadTexture(source, MainTexId);
            if (albedo != null)
            {
                block.SetTexture(BaseMapId, albedo);
            }

            block.SetColor(BaseColorId, ReadColor(source, BaseColorId, ReadColor(source, ColorId, Color.white)));
        }

        private static Texture ReadTexture(Material source, int propertyId) =>
            source.HasProperty(propertyId) ? source.GetTexture(propertyId) : null;

        private static Color ReadColor(Material source, int propertyId, Color fallback) =>
            source.HasProperty(propertyId) ? source.GetColor(propertyId) : fallback;

        /// <summary>Logged because the failure is silent: the zombie just pops in without an effect, so
        /// the component looks attached but does nothing.</summary>
        private static void WarnNoMaterial()
        {
            if (_warnedNoMaterial)
            {
                return;
            }

            _warnedNoMaterial = true;
            Debug.LogWarning("[ZombieDissolve] Çözülme materyali atanmamış — zombi efektsiz belirir. " +
                             "Tools > VortexArena > Zombie > Zombi görünüşlerini üret menüsünü koştur.");
        }
    }
}
