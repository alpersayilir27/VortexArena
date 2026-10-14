using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>One zombie BODY: the handles <see cref="ZombieView"/> needs on a look prefab
    /// (<c>ZB_*</c>) so the view never searches the hierarchy at runtime.</summary>
    /// <remarks>⚠️ Everything here is written by <c>ZombieLookBuilder</c>. Hand edits survive only
    /// until the next run of the tool — except the eye offsets, which are meant to be tuned here
    /// (the builder only ESTIMATES them from the head bone).
    /// <para>⚠️ The hitboxes hang on BONES, not on the root: a box on the root would stay standing
    /// while the body crawls or falls, and the squad would be shooting at air.</para></remarks>
    [DisallowMultipleComponent]
    public class ZombieLook : MonoBehaviour
    {
        [Header("Bileşenler")]
        [SerializeField] private Animator animator;
        [SerializeField] private SkinnedMeshRenderer bodyRenderer;
        [SerializeField] private ZombieDissolve dissolve;

        [Header("Kemikler")]
        [SerializeField] private Transform headBone;

        [Header("Gözler")]
        [Tooltip("Sol gözün KAFA KEMİĞİNE göre yerel konumu — araç tahmin eder, buradan ince ayar yapılır.")]
        [SerializeField] private Vector3 leftEyeOffset;
        [Tooltip("Sağ gözün kafa kemiğine göre yerel konumu.")]
        [SerializeField] private Vector3 rightEyeOffset;
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;

        [Header("Vuruş kutuları")]
        [Tooltip("Zombi kırılınca kapatılacak kutular — kemiklerde durdukları için tek kökleri yok.")]
        [SerializeField] private GameObject[] hitboxes;

        [Header("Yürüyüş")]
        [Tooltip("LocoSet başına yürüme klibinin KENDİ zemin hızı (m/s) — animatör hızının " +
                 "(LocoRate) böleni. Yanlışsa ayaklar kayar.")]
        [SerializeField] private float[] walkSpeeds = { 0.29f, 0.22f, 0.20f };

        // Mirrors M_ZB_EyeGlow (URP/Unlit) — the eye colour lives in _BaseColor there.
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _eyeBlock;

        public Animator Animator => animator;

        public SkinnedMeshRenderer BodyRenderer => bodyRenderer;

        public ZombieDissolve Dissolve => dissolve;

        public Transform HeadBone => headBone;

        /// <summary>Nominal ground speed of the walk clip in <paramref name="locoSet"/>; never zero, so
        /// the caller can divide by it.</summary>
        public float WalkSpeed(int locoSet)
        {
            if (walkSpeeds == null || walkSpeeds.Length == 0)
            {
                return 0.25f;
            }

            float speed = walkSpeeds[Mathf.Clamp(locoSet, 0, walkSpeeds.Length - 1)];
            return speed > 0.01f ? speed : 0.25f;
        }

        /// <summary>Tints both eyes — the zombie TYPE reads from the eye colour before the silhouette
        /// does.</summary>
        /// <remarks>⚠️ Through a property block, NOT the material: the eye material is SHARED by every
        /// body, so writing it would recolour the whole wave, and a per-zombie instance would break the
        /// batching the eyes are cheap because of.</remarks>
        public void SetEyeColor(Color color)
        {
            _eyeBlock ??= new MaterialPropertyBlock();
            _eyeBlock.SetColor(BaseColorProperty, color);

            ApplyEyeBlock(leftEye);
            ApplyEyeBlock(rightEye);
        }

        private void ApplyEyeBlock(Transform eye)
        {
            if (eye != null && eye.TryGetComponent(out Renderer renderer))
            {
                renderer.SetPropertyBlock(_eyeBlock);
            }
        }

        public void SetHitboxesActive(bool active)
        {
            if (hitboxes == null)
            {
                return;
            }

            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] != null && hitboxes[i].activeSelf != active)
                {
                    hitboxes[i].SetActive(active);
                }
            }
        }

        /// <summary>Puts the eyes on their authored offsets. Called from the editor so a tuned offset is
        /// visible without entering play mode.</summary>
        public void ApplyEyeOffsets()
        {
            if (leftEye != null)
            {
                leftEye.localPosition = leftEyeOffset;
            }

            if (rightEye != null)
            {
                rightEye.localPosition = rightEyeOffset;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyEyeOffsets();
        }
#endif
    }
}
