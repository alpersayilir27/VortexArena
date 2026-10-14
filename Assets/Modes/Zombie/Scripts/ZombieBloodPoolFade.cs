using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Blood pool decal look: spreads in after spawning, fades out before it is recycled.</summary>
    /// <remarks>Pooled by <see cref="ZombieBloodPool"/>, so every enable restarts the cycle; a property
    /// block keeps the shared material untouched.</remarks>
    public sealed class ZombieBloodPoolFade : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer target;
        [SerializeField] private float spreadSeconds = 1.6f;
        [SerializeField] private float startScale = 0.25f;
        [SerializeField] private float fadeStart = 9f;
        [SerializeField] private float fadeSeconds = 2.5f;

        private MaterialPropertyBlock _block;
        private Vector3 _fullScale;
        private Color _baseColor = Color.white;
        private float _age;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (target == null)
            {
                target = GetComponentInChildren<Renderer>();
            }

            _fullScale = target != null ? target.transform.localScale : Vector3.one;
            if (target != null && target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId))
            {
                _baseColor = target.sharedMaterial.GetColor(BaseColorId);
            }
        }

        private void OnEnable()
        {
            _age = 0f;
            Apply();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            if (target == null)
            {
                return;
            }

            // Ease-out spread: fast at first, like liquid running out.
            float s = Mathf.Clamp01(_age / spreadSeconds);
            s = 1f - (1f - s) * (1f - s);
            target.transform.localScale = _fullScale * Mathf.Lerp(startScale, 1f, s);

            float alpha = 1f - Mathf.Clamp01((_age - fadeStart) / fadeSeconds);
            Color c = _baseColor;
            c.a *= alpha;
            target.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            target.SetPropertyBlock(_block);
        }
    }
}
