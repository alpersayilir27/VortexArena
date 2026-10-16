using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Draws the patty's doneness: <c>stage</c> is the SERVER's counter (§10.5), this only
    /// colours it and marks the moment it turns cooked (ding + puff) or burnt (own sound).
    /// <para>⚠️ Without this the only sign of a raw or burnt patty is a silent rejection at the counter —
    /// the server refuses anything that is not <see cref="BurgerKinds.PattyCooked"/> and says nothing
    /// about why.</para>
    /// <para>⚠️ The sizzle is NOT here: it belongs to <see cref="BurgerGrill"/>. On the patty it kept
    /// sizzling wherever the patty went once an "off" report was lost.</para>
    /// <para>Hot-grill rule: a patty lying on a grill refuses the BARE hand (<see cref="IHandGrabGate"/>)
    /// and answers with a burn buzz plus the thermometer's "SPATULA" card; the spatula's own claim never
    /// asks the gate.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerPatty : MonoBehaviour, IHandGrabGate
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

        [Tooltip("Izgaradaki köfte çıplak elle alınmaya çalışılınca çalan 'cız' (yalnız deneyen oyuncu " +
                 "duyar). Atanmazsa sessizdir; titreşim ve termometre uyarısı yine verilir.")]
        [SerializeField] private AudioClip handBurnClip;

        // URP Lit uses _BaseColor; _Color is written too so an unlit/legacy material still reacts.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private const string BurnHapticSource = "burn";
        private const float BurnHapticSeconds = 0.3f;
        private const float BurnHapticAmplitude = 1f;

        /// <summary>A closed hand in a flight catch asks every frame; feedback restarts at most this often.</summary>
        private const float BurnFeedbackCooldown = 0.5f;

        private NetObject _net;
        private NetObjectGrabBridge _bridge;
        private BurgerPattyThermometer _thermometer;
        private MaterialPropertyBlock _block;

        private int _lastStage = -1;

        private float _burnFeedbackAt = float.NegativeInfinity;

        /// <summary>End of the burn buzz; the arbiter wants a report every frame (heartbeat).</summary>
        private float _hapticUntil;

        private bool _hapticRight;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _bridge = GetComponent<NetObjectGrabBridge>();
            _thermometer = GetComponentInChildren<BurgerPattyThermometer>(true);
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

            if (_bridge != null)
            {
                _bridge.HandGate = this;
            }

            // A late joiner gets the doneness with the spawn state, not with an event.
            ApplyStage(_net.Stage);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;

            if (_bridge != null && ReferenceEquals(_bridge.HandGate, this))
            {
                _bridge.HandGate = null;
            }

            // A pooled patty must not leave a hand buzzing until the arbiter's timeout.
            if (_hapticUntil > 0f)
            {
                _hapticUntil = 0f;
                ControllerHaptics.ReportHand(BurnHapticSource, _hapticRight, 0f);
            }
        }

        // ------------------------------------------------------------------- hot grill

        /// <summary>Same "on the grill" the thermometer and the sizzle use (<see cref="BurgerGrill.IsCooking"/>):
        /// a patty riding a spatula over the grill is held, so the free hand may still take it off.</summary>
        public bool AllowHandGrab(bool rightHand)
        {
            if (!BurgerGrill.IsCooking(_net))
            {
                return true;
            }

            if (Time.unscaledTime >= _burnFeedbackAt + BurnFeedbackCooldown)
            {
                _burnFeedbackAt = Time.unscaledTime;
                GiveBurnFeedback(rightHand);
            }

            return false;
        }

        private void GiveBurnFeedback(bool rightHand)
        {
            if (_hapticUntil > 0f && rightHand != _hapticRight)
            {
                ControllerHaptics.ReportHand(BurnHapticSource, _hapticRight, 0f);
            }

            _hapticRight = rightHand;
            _hapticUntil = Time.unscaledTime + BurnHapticSeconds;

            if (_thermometer != null)
            {
                _thermometer.ShowSpatulaHint();
            }

            PlayClip(handBurnClip);
        }

        private void LateUpdate()
        {
            if (_hapticUntil <= 0f)
            {
                return;
            }

            if (Time.unscaledTime >= _hapticUntil)
            {
                _hapticUntil = 0f;
                ControllerHaptics.ReportHand(BurnHapticSource, _hapticRight, 0f);
                return;
            }

            ControllerHaptics.ReportHand(BurnHapticSource, _hapticRight, BurnHapticAmplitude);
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
