using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Presentation of a breakable network object (§10.10): damage visual, collider/mesh swap, break
    /// FX. Breaking is the SERVER's decision — this component only draws what <see cref="NetObject"/>
    /// caches and never writes state.
    /// <para>A snapshot (<see cref="NetStateOrigin.Snapshot"/>) plays no effect: a late joiner must not
    /// see an explosion that happened before they joined.</para>
    /// </summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BreakableObject : MonoBehaviour
    {
        [Tooltip("Hasar oranının yazılacağı görseller. Boş bırakılırsa çocuklardaki tüm Renderer'lar kullanılır.")]
        [SerializeField] private Renderer[] damageRenderers;

        [Tooltip("Kırılınca kapatılacak collider'lar. Boş bırakılırsa çocuklardaki tüm Collider'lar kullanılır.")]
        [SerializeField] private Collider[] hitColliders;

        [Tooltip("Kırılınca gizlenecek sağlam görünüm kökü (isteğe bağlı).")]
        [SerializeField] private GameObject intactRoot;

        [Tooltip("Kırılınca açılacak enkaz/parça kökü (isteğe bağlı).")]
        [SerializeField] private GameObject brokenRoot;

        [Tooltip("Kırılma anında oynatılacak efekt prefabı (isteğe bağlı).")]
        [SerializeField] private GameObject breakFxPrefab;

        [Tooltip("Kırılma efektinin kaç saniye sonra silineceği.")]
        [SerializeField] private float breakFxLifetime = 3f;

        [Tooltip("Kırılma sesi (isteğe bağlı).")]
        [SerializeField] private AudioClip breakClip;

        [Range(0f, 1f)]
        [Tooltip("Kırılma sesinin şiddeti.")]
        [SerializeField] private float breakVolume = 1f;

        /// <summary>⚠️ Silently ignored when the shader has no such property — not an error.</summary>
        private static readonly int DamageAmountId = Shader.PropertyToID("_DamageAmount");

        private NetObject _netObject;
        private MaterialPropertyBlock _block;
        private bool _broken;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
            _block = new MaterialPropertyBlock();

            if (damageRenderers == null || damageRenderers.Length == 0)
            {
                damageRenderers = CollectOutsideBrokenRoot<Renderer>();
            }

            if (hitColliders == null || hitColliders.Length == 0)
            {
                hitColliders = CollectOutsideBrokenRoot<Collider>();
            }

            if (brokenRoot != null)
            {
                brokenRoot.SetActive(false);
            }
        }

        /// <summary>Subtree components EXCEPT the debris root's own: auto-collection must not disable
        /// the very colliders the break just revealed, nor write the damage value onto the pieces.</summary>
        private T[] CollectOutsideBrokenRoot<T>() where T : Component
        {
            T[] all = GetComponentsInChildren<T>(true);
            if (brokenRoot == null)
            {
                return all;
            }

            Transform brokenTransform = brokenRoot.transform;
            var kept = new List<T>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].transform.IsChildOf(brokenTransform))
                {
                    kept.Add(all[i]);
                }
            }

            return kept.ToArray();
        }

        private void OnEnable()
        {
            if (_netObject == null)
            {
                return;
            }

            _netObject.StateChanged += Apply;

            // Catch up silently: a re-enabled object must not replay the break effect.
            Apply(_netObject, NetStateOrigin.Snapshot);
        }

        private void OnDisable()
        {
            if (_netObject != null)
            {
                _netObject.StateChanged -= Apply;
            }
        }

        private void Apply(NetObject o, NetStateOrigin origin)
        {
            // 0 = intact, 1 = destroyed.
            float damageAmount = 1f - o.HealthRatio;
            for (int i = 0; i < damageRenderers.Length; i++)
            {
                Renderer renderer = damageRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                _block.SetFloat(DamageAmountId, damageAmount);
                renderer.SetPropertyBlock(_block);
            }

            bool broken = o.IsBroken;
            if (broken != _broken)
            {
                for (int i = 0; i < hitColliders.Length; i++)
                {
                    if (hitColliders[i] != null)
                    {
                        hitColliders[i].enabled = !broken;
                    }
                }

                if (intactRoot != null)
                {
                    intactRoot.SetActive(!broken);
                }

                if (brokenRoot != null)
                {
                    brokenRoot.SetActive(broken);
                }

                // Effects only on a live break; the repair edge (round reset) stays silent.
                if (origin == NetStateOrigin.Live && broken)
                {
                    PlayBreakPresentation();
                }
            }

            _broken = broken;
        }

        private void PlayBreakPresentation()
        {
            Vector3 position = transform.position;

            if (breakFxPrefab != null)
            {
                GameObject fx = Instantiate(breakFxPrefab, position, Quaternion.identity);
                Destroy(fx, Mathf.Max(0.1f, breakFxLifetime));
            }

            if (breakClip != null)
            {
                AudioSource.PlayClipAtPoint(breakClip, position, breakVolume);
            }
        }
    }
}
