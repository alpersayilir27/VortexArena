using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>Iris on the HMD for the floor portal: while the player waits on the disc the view
    /// darkens from every edge toward the centre, is fully black at the hop, and opens again on the
    /// new floor.</summary>
    /// <remarks>
    /// <b>Why not a <see cref="ScreenFade"/> source:</b> the arbiter resolves a single flat alpha, it
    /// has no notion of a shrinking hole — and "highest alpha wins" would let a boundary blackout
    /// swallow the dwell feedback that tells the player the hop is coming.
    /// <para><b>Heartbeat contract:</b> the portal reports its dwell progress EVERY frame; a report
    /// older than <see cref="ReportTimeoutSeconds"/> counts as "stepped off" and the iris eases back
    /// open by itself. So a portal that is disabled, destroyed or simply stops caring can never leave
    /// the player in the dark (the <see cref="ScreenFade"/> pattern).</para>
    /// <para>Owned by the <c>VA_CameraRig</c> prefab (under CenterEyeAnchor, behind the damage
    /// vignette). Never runs on an admin observer, whose rig root is disabled — the portal does not
    /// fire there either.</para>
    /// </remarks>
    [DefaultExecutionOrder(30200)]
    public class PortalIris : MonoBehaviour
    {
        /// <summary>A dwell report older than this means the player left the disc (s).</summary>
        private const float ReportTimeoutSeconds = 0.2f;

        private static readonly int ClearRadiusId = Shader.PropertyToID("_ClearRadius");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

        private enum Phase
        {
            /// <summary>Open, or following a live dwell report.</summary>
            Idle,

            /// <summary>Full black while the floor change is applied.</summary>
            Hold,

            /// <summary>Opening back up on the new floor.</summary>
            Opening
        }

        [Tooltip("İris quad'ının renderer'ı (bu objenin kendi MeshRenderer'ı).")]
        [SerializeField] private Renderer irisRenderer;

        [Tooltip("Tam açıkken şeffaf göbeğin yarıçapı. Görüş alanının köşesinden (≈0.7) BÜYÜK olmalı — " +
                 "yoksa hiç kimse diskte değilken de kenarlar hafif kararır.")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float openRadius = 0.78f;

        [Tooltip("Karanlığın kenar yumuşaklığı (tam açıkken). Kapanırken yarıçapla birlikte küçülür, " +
                 "yoksa tam kapanışta göbekte şeffaf bir leke kalırdı.")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float edgeSoftness = 0.2f;

        [Tooltip("Geçiş anında tam karanlıkta beklenen süre (sn) — kat değişimi bu karanlığın arkasında olur.")]
        [Range(0f, 1f)]
        [SerializeField] private float holdSeconds = 0.15f;

        [Tooltip("Yeni katta açılma süresi (sn).")]
        [Range(0.05f, 2f)]
        [SerializeField] private float openSeconds = 0.35f;

        [Tooltip("Oyuncu diskten erken çıkınca açılma yumuşatması (sn).")]
        [Range(0.01f, 1f)]
        [SerializeField] private float releaseSmoothSeconds = 0.12f;

        private static PortalIris _instance;

        private MaterialPropertyBlock _propertyBlock;
        private Phase _phase;
        private float _closure;
        private float _reported;
        private float _reportTime = float.NegativeInfinity;
        private float _phaseTime;
        private float _releaseVelocity;

        /// <summary>Dwell progress of the portal holding the player (0..1). Reported every frame;
        /// silence means the player stepped off.</summary>
        public static void ReportDwell(float progress)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._reported = Mathf.Clamp01(progress);
            _instance._reportTime = Time.unscaledTime;
        }

        /// <summary>The hop was accepted: hold full black, then open on the new floor. Call it in the
        /// same frame the floor is changed — the iris is already black at that point, so the arena
        /// swap is never seen.</summary>
        public static void ReportHop()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._phase = Phase.Hold;
            _instance._closure = 1f;
            _instance._phaseTime = Time.unscaledTime;
        }

        private void Awake()
        {
            if (irisRenderer == null)
            {
                irisRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            _instance = this;
            _phase = Phase.Idle;
            _closure = 0f;
            _releaseVelocity = 0f;
            _reportTime = float.NegativeInfinity;
            Draw(0f);
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            Draw(0f);
        }

        private void LateUpdate()
        {
            // Unscaled throughout: the iris is presentation and must finish even if the match is
            // paused with timeScale.
            float now = Time.unscaledTime;

            switch (_phase)
            {
                case Phase.Hold:
                    _closure = 1f;
                    if (now - _phaseTime >= holdSeconds)
                    {
                        _phase = Phase.Opening;
                        _phaseTime = now;
                    }

                    break;

                case Phase.Opening:
                    float u = openSeconds > 0f ? (now - _phaseTime) / openSeconds : 1f;
                    if (u >= 1f)
                    {
                        _phase = Phase.Idle;
                        _closure = 0f;
                        _releaseVelocity = 0f;
                    }
                    else
                    {
                        _closure = 1f - Ease(u);
                    }

                    break;

                default:
                    if (now - _reportTime <= ReportTimeoutSeconds)
                    {
                        // Tracked directly, not smoothed: the dwell progress is already continuous and
                        // a lag here would leave the screen grey at the hop.
                        _closure = Ease(_reported);
                        _releaseVelocity = 0f;
                    }
                    else if (_closure > 0.001f)
                    {
                        _closure = Mathf.SmoothDamp(_closure, 0f, ref _releaseVelocity,
                            releaseSmoothSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
                    }
                    else
                    {
                        _closure = 0f;
                    }

                    break;
            }

            Draw(_closure);
        }

        /// <summary>Smoothstep: the first second of the dwell barely darkens, the close accelerates as
        /// the hop nears. A linear ramp reads as "the screen is broken" long before anything happens.</summary>
        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void Draw(float closure)
        {
            if (irisRenderer == null)
            {
                return;
            }

            bool visible = closure > 0.001f;
            irisRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            // ⚠️ The soft edge shrinks WITH the hole: a fixed softness would keep the very centre
            // transparent at closure 1 and the hop would happen through a peephole.
            float open = 1f - closure;
            _propertyBlock ??= new MaterialPropertyBlock();
            irisRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(ClearRadiusId, openRadius * open);
            _propertyBlock.SetFloat(EdgeSoftnessId, edgeSoftness * open);
            irisRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
