using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Drives a second model (team body) from the character's live skeleton via a <b>bone mirror</b>:
    /// <c>localRotation</c> is copied between bones whose NAMES match in both trees.
    /// <para>
    /// ⚠️ <b>Why <see cref="HumanPoseHandler"/> is NOT used:</b> <c>GetHumanPose</c> returns
    /// <c>bodyPosition</c> in WORLD space while <c>SetHumanPose</c> applies it RELATIVE to the root
    /// — with two roots stacked, the translation is applied twice and the body slides out of the
    /// arena. For two models sharing the same Mixamo skeleton there is no need to enter muscle space
    /// at all: the bone names are identical.
    /// </para>
    /// <para>
    /// ⚠️ <b>Precondition is matching bone NAMES</b> (same Mixamo rig). No Humanoid Avatar needed;
    /// with no match the component logs an error and disables itself.
    /// </para>
    /// <para>
    /// ⚠️ <b>Execution order is high and must stay that way:</b> what is read is the SDK's
    /// <b>applied</b> pose for the frame. <c>CharacterRetargeter</c> and
    /// <see cref="ArenaNetCharacterBehaviour"/> write in their own <c>LateUpdate</c>; an earlier
    /// driver would draw a one-frame-stale body.
    /// </para>
    /// <para>
    /// Never runs while invisible: <see cref="RemoteAvatar"/> toggles this component's
    /// <c>enabled</c> together with the drawn body.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30100)]
    public class SkeletonPoseMirror : MonoBehaviour
    {
        [Header("Kaynak — ağdan sürülen karakter")]
        [Tooltip("Retargeter'ın yazdığı iskeletin kökü (ArenaNetCharacterBehaviour'un oturttuğu kök).")]
        [SerializeField] private Transform sourceRoot;

        [Header("Hedef — aynalanan model")]
        [Tooltip("Aynalanan modelin kökü. Model değiştirmek bu kökü değiştirmektir.")]
        [SerializeField] private Transform targetRoot;

        [Header("Kalça (iskeletin kökü)")]
        [Tooltip("Kaynak modelin kalça kemiği (mixamorig:Hips).")]
        [SerializeField] private Transform sourceHips;

        [Tooltip("Hedef modelin kalça kemiği (mixamorig:Hips).")]
        [SerializeField] private Transform targetHips;

        [Tooltip("Kaynak kalçanın BIND pozundaki localPosition'ı. ⚠️ Çalışma anında hesaplanamaz: " +
                 "prefabdaki iskelet bind pozunda olmak zorunda değil, bu yüzden editör aracı " +
                 "FBX asset'inden okuyup buraya yazar.")]
        [SerializeField] private Vector3 sourceHipsBind;

        [Tooltip("Hedef kalçanın BIND pozundaki localPosition'ı (gerekçe: sourceHipsBind).")]
        [SerializeField] private Vector3 targetHipsBind;

        [Tooltip("Hedefin iskelet kolonunu kaynağınkine eşitleyen tek tip çarpan. Kemik " +
                 "kopyalamada hedef KENDİ boyunda çizilir; iki modelin kolon uzunluğu farklıysa " +
                 "bu çarpan onu eşitler. Oyuncunun gerçek boyu AYRI bir şeydir (kaynağın " +
                 "localScale'i) ve ayrıca çarpılır.")]
        [SerializeField] private float heightCalibration = 1f;

        // ⚠️ Arrays are built once: a per-frame GetComponentsInChildren allocates garbage
        // (this runs at 72/s for every remote player).
        private Transform[] _source;
        private Transform[] _target;

        private bool _ready;

        private void Awake()
        {
            _ready = TrySetup();
        }

        /// <summary>
        /// Builds the bone mapping. ⚠️ <b>Logs an error and disables itself:</b> failing silently
        /// freezes the body in T-pose, which reads on site as "the network is broken" while the real
        /// cause is a missing prefab link or a mismatched skeleton.
        /// </summary>
        private bool TrySetup()
        {
            if (sourceRoot == null || targetRoot == null)
            {
                Debug.LogError("[SkeletonPoseMirror] sourceRoot/targetRoot boş — gövde " +
                               "sürülemeyecek (T-pozunda donar).", this);
                enabled = false;
                return false;
            }

            // Index target names once so the lookup while walking the source is O(1).
            Transform[] targetBones = targetRoot.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>(targetBones.Length);
            for (int i = 0; i < targetBones.Length; i++)
            {
                byName[targetBones[i].name] = targetBones[i];
            }

            Transform[] sourceBones = sourceRoot.GetComponentsInChildren<Transform>(true);
            var sources = new List<Transform>(sourceBones.Length);
            var targets = new List<Transform>(sourceBones.Length);

            for (int i = 0; i < sourceBones.Length; i++)
            {
                Transform bone = sourceBones[i];

                // ⚠️ The root ITSELF is excluded: LateUpdate writes its placement every frame
                // (source world pose); copying its local rotation would overwrite that.
                if (bone == sourceRoot)
                {
                    continue;
                }

                if (byName.TryGetValue(bone.name, out Transform match) && match != targetRoot)
                {
                    sources.Add(bone);
                    targets.Add(match);
                }
            }

            if (sources.Count == 0)
            {
                Debug.LogError("[SkeletonPoseMirror] İki modelin kemik adları hiç eşleşmiyor — " +
                               "bu bileşenin tek ön koşulu aynı iskeleti (Mixamo) paylaşmalarıdır. " +
                               "Gövde sürülemeyecek (T-pozunda donar).", this);
                enabled = false;
                return false;
            }

            _source = sources.ToArray();
            _target = targets.ToArray();
            return true;
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                return;
            }

            targetRoot.SetPositionAndRotation(sourceRoot.position, sourceRoot.rotation);

            // The two multipliers are different things and must not be conflated: the source's
            // localScale is the player's REAL height (from joint 0 of the blob, written by the SDK),
            // heightCalibration is the fixed difference between the two models' skeleton columns.
            targetRoot.localScale = sourceRoot.localScale * heightCalibration;

            // ⚠️ Only localRotation is copied, NOT localPosition: bone lengths must come from the
            // target's OWN model, else the target is forced into the source's proportions and the
            // mesh deforms.
            for (int i = 0; i < _source.Length; i++)
            {
                _target[i].localRotation = _source[i].localRotation;
            }

            // ⚠️ Hips are the exception: they are the skeleton root and their position belongs to
            // the POSE (crouch, step). Transferred RELATIVE to each bind — copying the raw position
            // would turn the two models' different hip heights into a direct offset.
            if (sourceHips != null && targetHips != null)
            {
                targetHips.localPosition = sourceHips.localPosition - sourceHipsBind + targetHipsBind;
            }
        }
    }
}
