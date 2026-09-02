using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Burger
{
    /// <summary>Draws the patty's doneness: <c>stage</c> is the SERVER's counter (§10.5), this only
    /// colours it and runs the sizzle.
    /// <para>⚠️ Without this the only sign of a raw or burnt patty is a silent rejection at the counter —
    /// the server refuses anything that is not <see cref="BurgerKinds.PattyCooked"/> and says nothing
    /// about why.</para>
    /// <para>The <c>grill</c> event is relayed to EVERYONE (<c>i:[1]</c> on, <c>i:[0]</c> off), so the
    /// sizzle is heard by every headset, not just the one that put the patty down.</para></summary>
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

        [Tooltip("Izgaradaki cızırtı (loop olmalı). Atanmazsa sessizdir.")]
        [SerializeField] private AudioSource sizzleSource;

        [Tooltip("Köfte piştiğinde çalan tek vuruş. Atanmazsa sessizdir.")]
        [SerializeField] private AudioClip cookedClip;

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
                renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private void OnEnable()
        {
            _net.StateChanged += HandleStateChanged;
            _net.EventReceived += HandleEventReceived;

            // A late joiner gets the doneness with the spawn state, not with an event.
            ApplyStage(_net.Stage);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
            _net.EventReceived -= HandleEventReceived;
        }

        private void HandleStateChanged(NetObject net, NetStateOrigin origin) => ApplyStage(net.Stage);

        private void ApplyStage(int stage)
        {
            bool wasKnown = _lastStage >= 0;
            bool changed = stage != _lastStage;
            _lastStage = stage;

            Paint(stage);

            // Only a real transition rings: the first apply is a late joiner's snapshot, not a bake.
            if (changed && wasKnown && stage == BurgerKinds.PattyCooked)
            {
                PlayCooked();
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
            if (cookedClip == null)
            {
                return;
            }

            if (sizzleSource != null)
            {
                sizzleSource.PlayOneShot(cookedClip);
                return;
            }

            AudioSource.PlayClipAtPoint(cookedClip, transform.position);
        }

        /// <summary>⚠️ Burning does NOT stop the sizzle: a burnt patty can still be sitting on the grill,
        /// and only leaving it (<c>i:[0]</c>) means it came off.</summary>
        private void HandleEventReceived(ObjectEventMsg msg)
        {
            if (msg == null || msg.name != BurgerKinds.EventGrill || sizzleSource == null ||
                msg.i == null || msg.i.Length == 0)
            {
                return;
            }

            if (msg.i[0] == 1)
            {
                if (!sizzleSource.isPlaying)
                {
                    sizzleSource.Play();
                }
            }
            else
            {
                sizzleSource.Stop();
            }
        }
    }
}
