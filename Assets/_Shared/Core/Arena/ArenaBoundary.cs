using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Free-roam arena guard for a 10x10 m physical play space. The player moves
    /// 1:1 with their real body; this component watches the HMD position in the
    /// arena's local space and (a) fades in the boundary walls as the player
    /// approaches the edge, (b) fades the screen to black and shows a warning
    /// when they step outside the allowed area.
    /// Attach to an object positioned at the arena center, aligned with the
    /// arena's rotation; walls are expected to sit at +/- halfExtent.
    /// </summary>
    public class ArenaBoundary : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;
        [SerializeField] private Renderer[] wallRenderers;
        [Tooltip("Quad parented to the HMD used for the out-of-bounds blackout.")]
        [SerializeField] private Renderer fadeRenderer;
        [SerializeField] private TextMesh warningText;

        [Header("Arena size (meters)")]
        [SerializeField] private float halfExtentX = 5f;
        [SerializeField] private float halfExtentZ = 5f;

        [Header("Warning behaviour")]
        [Tooltip("Distance from the edge (m) where the walls start brightening.")]
        [SerializeField] private float warnDistance = 1f;
        [SerializeField] private float minWallAlpha = 0.05f;
        [SerializeField] private float maxWallAlpha = 0.85f;
        [Tooltip("Meters past the boundary at which the blackout is fully opaque.")]
        [SerializeField] private float fadeOutsideDistance = 0.3f;
        [SerializeField] private float maxFadeAlpha = 0.96f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock propertyBlock;

        /// <summary>True while the HMD is outside the allowed area.</summary>
        public bool IsOutOfBounds { get; private set; }

        /// <summary>Arena yarı ölçüleri (metre, X/Z) — admin kuş bakışı kadrajı bunu okur.</summary>
        public Vector2 HalfExtents => new Vector2(halfExtentX, halfExtentZ);

        // Gözlemci (admin) kipi: görsel muhafaza susar, ArenaSpace origin kaydı YERİNDE kalır.
        private bool spectatorMode;
        private float spectatorWallAlpha = 0.25f;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (warningText != null)
                warningText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // Arena origin kaydı: ağ pozları bu transforma göre arena uzayına çevrilir.
            ArenaSpace.SetOrigin(transform);
        }

        private void OnDisable()
        {
            ArenaSpace.ClearOrigin(transform);
        }

        /// <summary>
        /// Gözlemci (admin) kipi. Görsel muhafazayı susturur: karartma quad'ı ve alan-dışı
        /// uyarısı kapanır, duvarlar <paramref name="wallAlpha"/>'da sabitlenir,
        /// <see cref="IsOutOfBounds"/> false'a kilitlenir.
        /// <para>
        /// ⚠ <b>Bileşeni DEVRE DIŞI BIRAKMAYIN:</b> <c>OnDisable</c> →
        /// <c>ArenaSpace.ClearOrigin</c> arena uzayı origin'ini siler ve ağdan gelen TÜM uzak
        /// avatarlar dünya origin'ine yığılır. Admin gözlemci bu yüzden bileşeni açık bırakıp
        /// bu anahtarı kullanır (bkz. <c>VortexArena.App.Admin.AdminSpectator</c>).
        /// </para>
        /// Gerekçe: admin masaüstündedir, HMD'si yoktur; kafası (kapatılmış rig'in
        /// CenterEyeAnchor'ı) sabit durduğu için muhafaza mantığı anlamsız veri üretir.
        /// </summary>
        public void SetSpectatorMode(bool on, float wallAlpha = 0.25f)
        {
            spectatorMode = on;
            spectatorWallAlpha = Mathf.Clamp01(wallAlpha);
            if (!on)
                return; // bir sonraki Update gerçek duruma göre yeniden çizer

            propertyBlock ??= new MaterialPropertyBlock();
            IsOutOfBounds = false;
            SetWallsAlpha(spectatorWallAlpha);
            if (fadeRenderer != null)
                SetAlpha(fadeRenderer, 0f);
            if (warningText != null && warningText.gameObject.activeSelf)
                warningText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (spectatorMode)
            {
                // Muhafaza susuyor; duvar alfası tercihten gelip sabit kaldığı için iş yok.
                return;
            }

            if (head == null)
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            float edgeDistance = Mathf.Min(halfExtentX - Mathf.Abs(local.x), halfExtentZ - Mathf.Abs(local.z));
            IsOutOfBounds = edgeDistance < 0f;

            float warn = Mathf.Clamp01(1f - edgeDistance / warnDistance);
            SetWallsAlpha(Mathf.Lerp(minWallAlpha, maxWallAlpha, warn));

            float outside = Mathf.Clamp01(-edgeDistance / fadeOutsideDistance);
            if (fadeRenderer != null)
                SetAlpha(fadeRenderer, outside * maxFadeAlpha);
            if (warningText != null && warningText.gameObject.activeSelf != IsOutOfBounds)
                warningText.gameObject.SetActive(IsOutOfBounds);
        }

        private void SetWallsAlpha(float alpha)
        {
            if (wallRenderers == null)
                return;
            foreach (var wall in wallRenderers)
                if (wall != null)
                    SetAlpha(wall, alpha);
        }

        private void SetAlpha(Renderer target, float alpha)
        {
            target.GetPropertyBlock(propertyBlock);
            Color color = target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId)
                ? target.sharedMaterial.GetColor(BaseColorId)
                : Color.white;
            color.a = alpha;
            propertyBlock.SetColor(BaseColorId, color);
            target.SetPropertyBlock(propertyBlock);
            target.enabled = alpha > 0.001f;
        }
    }
}
