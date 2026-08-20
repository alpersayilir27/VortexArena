using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Plays a dissolve-in transition when the weapon arrives in the hand: the model is temporarily
    /// switched to the dissolve material, <c>_Dissolve</c> is driven 1→0, then the originals are
    /// restored. No effect on release — the weapon disappears instantly.
    /// <para>The gate is <see cref="Weapon.HeldChanged"/>, not the call sites: three paths put a
    /// weapon in the hand (random grant, frame clone, direct ISDK grab) and hooking each separately
    /// is a step silently forgotten when a fourth appears. <see cref="WeaponFrame"/> listens to the
    /// same event for the same reason.</para>
    /// <para>⚠️ The weapon's look is preserved: the original albedo and color are carried into the
    /// dissolve material via <see cref="MaterialPropertyBlock"/>. Without that the weapon dissolves
    /// as a flat-colored silhouette — the dissolve material is a SINGLE shared asset.</para>
    /// <para>⚠️ The effect's look is tuned in the material, not here (edge color/thickness, pattern
    /// frequency, axis…). This component only drives <c>_Dissolve</c> and carries the albedo; the
    /// same setting in two places would drift.</para>
    /// </summary>
    [RequireComponent(typeof(Weapon))]
    public class SimpleWeaponDissolve : MonoBehaviour
    {
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        // Albedo read from two names: URP/Lit writes `_BaseMap`, older Standard/mobile shaders
        // `_MainTex`. The pack's material carries both; whichever is filled wins.
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Warned once PER SESSION: if the material is missing it is missing on every weapon (same
        // prefab kit), so per-instance logging would just multiply the same line.
        private static bool _warnedNoMaterial;

        [Header("Materyal")]
        [Tooltip("Geçiş boyunca modele takılan çözülme materyali — Assets/_Shared/Materials/ " +
                 "altında DissolveEffect.mat (yumuşak lekeler) ya da VoronoiDissolveEffect.mat " +
                 "(hücresel). Efektin GÖRÜNÜMÜ bu materyalde ayarlanır.")]
        [SerializeField] private Material dissolveMaterial;

        [Header("Zamanlama")]
        [Tooltip("Silahın tamamen belirme süresi (sn). ⚠️ Kalıcı değer WeaponKitBuilder'dadır — " +
                 "araç her koşuda buraya geri yazar; burada yapılan değişiklik yalnız denemeliktir.")]
        [SerializeField] private float appearSeconds = 1.2f;

        private Weapon _weapon;
        private Coroutine _routine;
        private bool _swapped;

        private readonly List<Target> _targets = new List<Target>();

        /// <summary>One Renderer the effect touches plus what is needed to restore it. The property
        /// block is PER Renderer: albedo varies even between parts of one weapon (body vs scope
        /// glass are separate materials).</summary>
        private sealed class Target
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] DissolveMaterials;
            public MaterialPropertyBlock Block;
        }

        private void Awake()
        {
            _weapon = GetComponent<Weapon>();
            CollectTargets();
        }

        private void OnEnable()
        {
            _weapon.HeldChanged += HandleHeldChanged;
        }

        private void OnDisable()
        {
            _weapon.HeldChanged -= HandleHeldChanged;
            _routine = null;

            // ⚠️ The coroutine DIES when the object is disabled (frame clone hidden on release).
            // Without restoring, the weapon returns HALF DISSOLVED next summon; and a renderer with
            // a property block cannot enter the SRP Batcher, so the cost would persist too.
            Restore();
        }

        /// <summary>
        /// Collects the Renderers the effect applies to, once.
        /// <para>Only the solid body: muzzle flash/smoke (<see cref="ParticleSystemRenderer"/>) and
        /// the aim ray (<see cref="LineRenderer"/>) use their own materials and would disappear or
        /// render broken under the dissolve material.</para>
        /// <para>The <see cref="WeaponFrame"/> subtree is skipped too — it belongs to the SOURCE
        /// weapon in the scene and is already disabled while held (absent on the clone).</para>
        /// </summary>
        private void CollectTargets()
        {
            var frame = GetComponentInChildren<WeaponFrame>(true);
            Renderer[] all = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                if (frame != null && renderer.transform.IsChildOf(frame.transform))
                {
                    continue;
                }

                _targets.Add(new Target
                {
                    Renderer = renderer,
                    // sharedMaterials returns a NEW array on every call — the copy we keep is safe.
                    OriginalMaterials = renderer.sharedMaterials,
                    Block = new MaterialPropertyBlock(),
                });
            }
        }

        private void HandleHeldChanged(bool held)
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (!held)
            {
                // Left the hand: instant, no transition. Still restore so the next summon is clean.
                Restore();
                return;
            }

            if (dissolveMaterial == null)
            {
                WarnNoMaterial();
                return;
            }

            _routine = StartCoroutine(Appear());
        }

        /// <summary>Materializes the weapon: <c>_Dissolve</c> 1 → 0.</summary>
        private IEnumerator Appear()
        {
            Swap();

            float elapsed = 0f;
            while (elapsed < appearSeconds)
            {
                elapsed += Time.deltaTime;

                // SmoothStep, not linear: linear reads "machine-like" in VR.
                float k = Mathf.SmoothStep(0f, 1f, elapsed / appearSeconds);
                SetDissolve(1f - k);
                yield return null;
            }

            SetDissolve(0f);
            Restore();
            _routine = null;
        }

        /// <summary>Switches the model to the dissolve material, filling each Renderer's property
        /// block with its OWN original look (albedo + color).</summary>
        private void Swap()
        {
            if (_swapped)
            {
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                Material source = target.OriginalMaterials.Length > 0 ? target.OriginalMaterials[0] : null;

                target.Block.Clear();
                WriteAppearance(target.Block, source);
                target.Block.SetFloat(DissolveId, 1f); // so the first frame is not fully visible
                target.Renderer.SetPropertyBlock(target.Block);

                target.Renderer.sharedMaterials = GetDissolveMaterials(target);
            }

            _swapped = true;
        }

        /// <summary>Restores the original materials and clears the property block.</summary>
        private void Restore()
        {
            if (!_swapped)
            {
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                target.Renderer.SetPropertyBlock(null);
                target.Renderer.sharedMaterials = target.OriginalMaterials;
            }

            _swapped = false;
        }

        private void SetDissolve(float value)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                target.Block.SetFloat(DissolveId, value);
                target.Renderer.SetPropertyBlock(target.Block);
            }
        }

        /// <summary>
        /// Dissolve material array sized to the Renderer's slot count; built once, reused (no
        /// garbage per grab).
        /// <para>⚠️ Written to <c>.sharedMaterials</c>, NOT <c>.materials</c>: the latter creates a
        /// material COPY per Renderer that is never collected (leak). The dissolve material is
        /// shared as a single asset; everything weapon-specific lives in the property block.</para>
        /// </summary>
        private Material[] GetDissolveMaterials(Target target)
        {
            if (target.DissolveMaterials == null)
            {
                target.DissolveMaterials = new Material[target.OriginalMaterials.Length];
            }

            for (int i = 0; i < target.DissolveMaterials.Length; i++)
            {
                target.DissolveMaterials[i] = dissolveMaterial;
            }

            return target.DissolveMaterials;
        }

        /// <summary>
        /// Carries the original material's look into the dissolve material.
        /// <para>A missing texture is not written at all (a <c>null</c> texture in the block throws)
        /// — that part dissolves flat-colored, the effect still works.</para>
        /// </summary>
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

        private static Texture ReadTexture(Material source, int propertyId)
        {
            return source.HasProperty(propertyId) ? source.GetTexture(propertyId) : null;
        }

        private static Color ReadColor(Material source, int propertyId, Color fallback)
        {
            return source.HasProperty(propertyId) ? source.GetColor(propertyId) : fallback;
        }

        /// <summary>
        /// Warns once when no material is assigned. Logged because the failure is silent: the
        /// weapon just appears instantly, so the component looks attached but does nothing.
        /// </summary>
        private static void WarnNoMaterial()
        {
            if (_warnedNoMaterial)
            {
                return;
            }

            _warnedNoMaterial = true;
            Debug.LogWarning("[SimpleWeaponDissolve] Çözülme materyali atanmamış — silah efektsiz " +
                             "belirir. WPN_* prefabının kökündeki SimpleWeaponDissolve'a " +
                             "Assets/_Shared/Materials/DissolveEffect.mat bağla.");
        }
    }
}
