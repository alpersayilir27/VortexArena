using UnityEngine;

namespace VortexArena.Modes.Burger
{
    /// <summary>Ketchup on your OWN visor: a stain overlay at the EDGES of the view, the middle left
    /// clear, fading in a few seconds. Lives under <c>CenterEyeAnchor</c> (the Burger HUD prefab is
    /// instantiated under the camera), in front of the fade quad.
    /// <para>⚠️ <b>Must NOT be registered as a <c>ScreenFade</c> source</b> and draws in the
    /// <c>Overlay</c> queue with <c>ZTest Always</c> — the <see cref="VortexArena.Core.Player.DamageVignette"/>
    /// rationale: the fade arbiter takes the highest alpha, so a 0.5 stain would be swallowed by a 1.0
    /// blackout.</para>
    /// <para>⚠️ <b>The centre stays clear</b> (the texture's own hole): the player is physically walking
    /// in a room and blinding them is a safety problem, not a stronger effect.</para>
    /// <para>Only ONE player is judged here — the local one. Every headset runs its own droplet
    /// simulation and decides for itself (the flashbang precedent, <c>Yemek-Kitabi.md</c>).</para></summary>
    [DefaultExecutionOrder(30200)]
    public sealed class BurgerKetchupVision : MonoBehaviour
    {
        [Tooltip("Leke quad'ının renderer'ı. Boşsa bu objenin kendi renderer'ı kullanılır.")]
        [SerializeField] private Renderer splatRenderer;

        [Tooltip("Rastgele seçilen kenar leke dokuları. Boşsa materyalin kendi dokusu kalır.")]
        [SerializeField] private Texture[] variants;

        [Tooltip("Lekenin rengi ve tepe opaklığı (alfa).")]
        [SerializeField] private Color tint = new Color(0.62f, 0.07f, 0.06f, 0.85f);

        [Tooltip("Lekenin tamamen solma süresi (sn).")]
        [Range(0.5f, 10f)]
        [SerializeField] private float fadeSeconds = 3f;

        /// <summary>Within this radius of the head a droplet counts as a face hit (m).</summary>
        public const float HeadRadius = 0.2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>The local player's visor overlay, or null in a mode/scene that has none — the
        /// stream asks it both for the head position and for the hit.</summary>
        public static BurgerKetchupVision Active { get; private set; }

        /// <summary>Where the local head is; the stream measures droplet distance against this.</summary>
        public Vector3 HeadPosition => transform.position;

        private MaterialPropertyBlock _propertyBlock;
        private float _hitTime = float.NegativeInfinity;
        private Texture _variant;

        private void Awake()
        {
            if (splatRenderer == null)
            {
                splatRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            Draw(0f);
        }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }

            _hitTime = float.NegativeInfinity;
            Draw(0f);
        }

        /// <summary>A droplet reached the face: restarts the fade with a fresh random stain.</summary>
        /// <remarks>⚠️ The envelope is REFRESHED, never stacked: a stream aimed at the face would
        /// otherwise pile up alpha and paint the view shut.</remarks>
        public void Splash()
        {
            _hitTime = Time.unscaledTime;

            if (variants != null && variants.Length > 0)
            {
                _variant = variants[Random.Range(0, variants.Length)];
            }
        }

        private void LateUpdate()
        {
            if (fadeSeconds <= 0f)
            {
                Draw(0f);
                return;
            }

            // unscaledTime: presentation keeps its timing even if timeScale is played with.
            float elapsed = Time.unscaledTime - _hitTime;
            Draw(elapsed >= fadeSeconds ? 0f : tint.a * (1f - elapsed / fadeSeconds));
        }

        private void Draw(float alpha)
        {
            if (splatRenderer == null)
            {
                return;
            }

            bool visible = alpha > 0.001f;
            splatRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            splatRenderer.GetPropertyBlock(_propertyBlock);

            // ⚠️ .linear is REQUIRED: the project renders in Linear space and SetColor does no
            // conversion (the DamageVignette rationale).
            Color color = tint.linear;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);

            if (_variant != null)
            {
                _propertyBlock.SetTexture(BaseMapId, _variant);
            }

            splatRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
