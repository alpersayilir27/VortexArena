using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Draws the patty's doneness: <c>stage</c> is the SERVER's counter (§10.5), this only
    /// colours it and marks the moment it turns cooked (ding + puff) or burnt (own sound).
    /// <para>⚠️ Without this the only sign of a raw or burnt patty is a silent rejection at the counter —
    /// the server refuses anything that is not <see cref="BurgerKinds.PattyCooked"/> and says nothing
    /// about why.</para>
    /// <para>⚠️ The sizzle is NOT here: it belongs to <see cref="BurgerGrill"/>. On the patty it kept
    /// sizzling wherever the patty went once an "off" report was lost.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerPatty : MonoBehaviour
    {
        [Tooltip("Pişme rengi yazılacak görseller. Boşsa çocuklardaki tüm renderer'lar kullanılır.")]
        [SerializeField] private Renderer[] renderers;

        [Tooltip("Çiğ köfte rengi.")]
        [SerializeField] private Color rawColor = new Color(0.78f, 0.32f, 0.33f);

        [Tooltip("Pişmiş köfte rengi — servise giren tek renk budur.")]
        [SerializeField] private Color cookedColor = new Color(0.42f, 0.24f, 0.12f);

        [Tooltip("Yanmış köfte rengi.")]
        [SerializeField] private Color burntColor = new Color(0.10f, 0.08f, 0.07f);

        [Tooltip("Pişti sesinin çaldığı kaynak (köftenin üstünde, 3D). Boşsa sesi noktada çalar.")]
        [FormerlySerializedAs("sizzleSource")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Köfte piştiğinde çalan tek vuruş. Atanmazsa sessizdir.")]
        [SerializeField] private AudioClip cookedClip;

        [Tooltip("Köfte yandığında çalan tek vuruş — pişti sesinden ayırt edilebilir olmalı. Atanmazsa sessizdir.")]
        [SerializeField] private AudioClip burntClip;

        [Tooltip("Köfte piştiği an oynayan küçük efekt. Boşsa çocuklardaki ParticleSystem aranır.")]
        [SerializeField] private ParticleSystem cookedFx;

        // No child fallback: a second ParticleSystem would make cookedFx's lookup ambiguous.
        [Tooltip("Köfte yandığı an oynayan kara duman. Atanmazsa efektsizdir (çocuklarda aranmaz).")]
        [SerializeField] private ParticleSystem burntFx;

        // URP Lit uses _BaseColor; _Color is written too so an unlit/legacy material still reacts.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private NetObject _net;
        private MaterialPropertyBlock _block;

        private int _lastStage = -1;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _block = new MaterialPropertyBlock();

            if (renderers == null || renderers.Length == 0)
            {
                renderers = CollectPaintableRenderers();
            }

            if (cookedFx == null)
            {
                cookedFx = GetComponentInChildren<ParticleSystem>(true);
            }
        }

        /// <summary>⚠️ Skips the thermometer's gauge: its renderer is a child too, and the doneness colour
        /// written into _BaseColor would paint the whole dial meat-coloured.</summary>
        private Renderer[] CollectPaintableRenderers()
        {
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            var kept = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer target = all[i];
                if (target != null && target.GetComponentInParent<BurgerPattyThermometer>(true) == null)
                {
                    kept.Add(target);
                }
            }

            return kept.ToArray();
        }

        private void OnEnable()
        {
            _net.StateChanged += HandleStateChanged;

            // A late joiner gets the doneness with the spawn state, not with an event.
            ApplyStage(_net.Stage);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(NetObject net, NetStateOrigin origin) => ApplyStage(net.Stage);

        private void ApplyStage(int stage)
        {
            bool wasKnown = _lastStage >= 0;
            bool changed = stage != _lastStage;
            _lastStage = stage;

            Paint(stage);

            // Only a real transition rings: the first apply is a late joiner's snapshot, not a bake.
            if (!changed || !wasKnown)
            {
                return;
            }

            if (stage == BurgerKinds.PattyCooked)
            {
                PlayCooked();
            }
            else if (stage == BurgerKinds.PattyBurnt)
            {
                if (burntFx != null)
                {
                    burntFx.Play(true);
                }

                PlayClip(burntClip);
            }
        }

        private void Paint(int stage)
        {
            if (renderers == null)
            {
                return;
            }

            Color color = stage == BurgerKinds.PattyBurnt
                ? burntColor
                : stage == BurgerKinds.PattyCooked
                    ? cookedColor
                    : rawColor;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer target = renderers[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                target.SetPropertyBlock(_block);
            }
        }

        private void PlayCooked()
        {
            if (cookedFx != null)
            {
                cookedFx.Play(true);
            }

            PlayClip(cookedClip);
        }

        private void PlayClip(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (audioSource != null)
            {
                audioSource.PlayOneShot(clip);
                return;
            }

            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
