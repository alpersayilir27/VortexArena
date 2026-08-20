using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Marker for the arena roof/ceiling: when the admin spectator switches to the <b>top-down</b>
    /// view the geometry under it is hidden, and it comes back in POV/free mode. It has NO effect on
    /// the player side — the component does nothing by itself, it only reacts when
    /// <see cref="ApplyAll"/> is called and only the admin spectator calls that.
    ///
    /// <para><b>Setup (a single step for the developer):</b> add this component to the ROOT of the
    /// roof hierarchy (<c>GameObject &gt; VortexArena &gt; Arena Roof</c>). <b>All</b> Renderers
    /// under it count as roof — there is no need to maintain a separate list. When the component is
    /// added (or via right click → <i>Çatı katmanını uygula</i>) the <see cref="LayerName"/> layer
    /// is stamped on all of them: with the Layers filter in the scene view "which one is roof" can
    /// be seen at a glance. The layer is only for VISIBILITY/FILTERING — the behaviour comes from
    /// the Renderer list, so hiding works even if the stamp is forgotten.</para>
    ///
    /// <para><b>Hiding method:</b> the <c>_BaseColor</c> alpha via a
    /// <c>MaterialPropertyBlock</c> — the shared material is not modified, a block is written per
    /// instance. On a full hide the Renderer is NOT DISABLED, it is switched to
    /// <see cref="ShadowCastingMode.ShadowsOnly"/>: the roof is not drawn but keeps casting its
    /// shadow. If it were disabled the interior would light up and the top-down view would turn into
    /// a flat, unreadable bright slab.</para>
    ///
    /// <para><b>It survives scene changes:</b> the last applied alpha is kept statically and the
    /// roof in the newly loaded scene applies it to itself in <c>OnEnable</c> — when a new arena
    /// opens while the admin is in top-down view, the roof is not visible for even a single
    /// frame.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VortexArena/Arena Roof")]
    public class ArenaRoof : MonoBehaviour
    {
        /// <summary>Name of the layer stamped on the roof geometry (ProjectSettings/TagManager.asset).</summary>
        public const string LayerName = "ArenaRoof";

        /// <summary>Below this alpha it counts as "fully hidden" (shadow kept, drawing stops).</summary>
        private const float HiddenEpsilon = 0.004f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly List<ArenaRoof> ActiveRoofs = new List<ArenaRoof>();

        /// <summary>The last applied alpha; the roof in a new scene inherits it in OnEnable.</summary>
        private static float lastAlpha = 1f;

        [Tooltip("Boş bırakılırsa altındaki TÜM Renderer'lar (kapalı olanlar dâhil) çatı sayılır. " +
                 "Yalnız bir alt kümeyi gizlemek istiyorsan burayı doldur.")]
        [SerializeField] private Renderer[] roofRenderers;

        private Renderer[] resolved;
        private ShadowCastingMode[] originalShadows;
        private MaterialPropertyBlock propertyBlock;

        /// <summary>Active roofs in the scene (the admin UI can check this for "is there a roof").</summary>
        public static IReadOnlyList<ArenaRoof> Active => ActiveRoofs;

        /// <summary>
        /// Applies an alpha to all roofs in the scene and remembers the value for later scenes.
        /// 1 = normal, 0 = not drawn (shadow stays). Values in between give a semi-transparent roof
        /// — ⚠️ but only if the material is of <b>Transparent</b> surface type: writing alpha has no
        /// visual counterpart on an Opaque material, an intermediate value behaves like 1 (0 still
        /// hides fully, because that path goes through
        /// <see cref="ShadowCastingMode.ShadowsOnly"/>, not through alpha).
        /// </summary>
        public static void ApplyAll(float alpha)
        {
            lastAlpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < ActiveRoofs.Count; i++)
            {
                ActiveRoofs[i].SetAlpha(lastAlpha);
            }
        }

        private void Awake()
        {
            Resolve();
        }

        private void OnEnable()
        {
            if (!ActiveRoofs.Contains(this))
            {
                ActiveRoofs.Add(this);
            }

            // If the scene was loaded while in top-down view, the roof starts hidden without ever
            // being visible.
            SetAlpha(lastAlpha);
        }

        private void OnDisable()
        {
            ActiveRoofs.Remove(this);
        }

        /// <summary>1 = normal, 0 = not drawn (shadow is kept).</summary>
        public void SetAlpha(float alpha)
        {
            if (resolved == null)
            {
                Resolve();
            }

            alpha = Mathf.Clamp01(alpha);
            bool hidden = alpha < HiddenEpsilon;
            propertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < resolved.Length; i++)
            {
                Renderer target = resolved[i];
                if (target == null)
                {
                    continue;
                }

                // Hidden: do not draw but keep the shadow. Visible: return to the original shadow mode.
                target.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : originalShadows[i];

                if (hidden)
                {
                    continue; // writing color is meaningless in ShadowsOnly
                }

                target.GetPropertyBlock(propertyBlock);
                Color color = target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId)
                    ? target.sharedMaterial.GetColor(BaseColorId)
                    : Color.white;
                color.a = alpha;
                propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(propertyBlock);
            }
        }

        /// <summary>Resolves the Renderer list and the original shadow modes (manual list > children).</summary>
        private void Resolve()
        {
            resolved = roofRenderers != null && roofRenderers.Length > 0
                ? roofRenderers
                : GetComponentsInChildren<Renderer>(true);

            originalShadows = new ShadowCastingMode[resolved.Length];
            for (int i = 0; i < resolved.Length; i++)
            {
                originalShadows[i] = resolved[i] != null ? resolved[i].shadowCastingMode : ShadowCastingMode.On;
            }
        }

#if UNITY_EDITOR
        /// <summary>Stamp the layer when the component is first added — no extra step for the developer.</summary>
        private void Reset()
        {
            StampLayer();
        }

        /// <summary>
        /// Stamps the <see cref="LayerName"/> layer on the roof geometry. When a new mesh is added
        /// later, right click the component and run it again. The layer is only for
        /// debugging/filtering; the hiding behaviour does NOT depend on it.
        /// </summary>
        [ContextMenu("Çatı katmanını uygula")]
        public void StampLayer()
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0)
            {
                Debug.LogWarning($"[ArenaRoof] '{LayerName}' katmanı yok — " +
                                 "ProjectSettings > Tags and Layers'a ekleyin. Gizleme yine çalışır.", this);
                return;
            }

            Resolve();
            UnityEditor.Undo.RecordObject(gameObject, "Çatı katmanı");
            gameObject.layer = layer;
            for (int i = 0; i < resolved.Length; i++)
            {
                if (resolved[i] == null || resolved[i].gameObject.layer == layer)
                {
                    continue;
                }

                UnityEditor.Undo.RecordObject(resolved[i].gameObject, "Çatı katmanı");
                resolved[i].gameObject.layer = layer;
            }
        }
#endif
    }
}
