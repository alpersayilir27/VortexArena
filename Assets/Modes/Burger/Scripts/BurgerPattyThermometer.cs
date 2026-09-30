using System.Globalization;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Cooking gauge above a patty: thresholds and the last measured value come from the patty's
    /// <c>s</c> payload (§10.5), the seconds in between are counted locally while the patty lies on a
    /// grill.
    /// <para>⚠️ The green window is the player's "take it off now" decision, so its edges must come from
    /// the server (<c>burger.cookSeconds/burnSeconds</c>) — hard-coding them on the client turns a tuned
    /// shift into a lie the moment the config changes.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerPattyThermometer : MonoBehaviour
    {
        [Tooltip("Göstergeyi çizen renderer (Quad). Boşsa bu objedeki renderer kullanılır.")]
        [SerializeField] private Renderer gauge;

        [Tooltip("Köfte merkezinin DÜNYA yukarısına göre yüksekliği (m).")]
        [SerializeField] private float heightOffset = 0.10f;

        [Tooltip("Izgaradan kalkınca göstergenin görünür kaldığı süre (sn).")]
        [SerializeField] private float lingerSeconds = 2.5f;

        [Tooltip("Göstergenin açılıp kapanma süresi (sn).")]
        [SerializeField] private float fadeSeconds = 0.2f;

        [Tooltip("Pişmiş penceresinin bu oranı geçilince yanma uyarısı yanıp söner.")]
        [SerializeField] private float warnFraction = 0.7f;

        [Tooltip("Eşikler henüz gelmemişken yeşil çizginin çizildiği yer (0-1).")]
        [SerializeField] private float fallbackCookMark = 0.5f;

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int CookMarkId = Shader.PropertyToID("_CookMark");
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly int WarnId = Shader.PropertyToID("_Warn");
        private static readonly int BurntId = Shader.PropertyToID("_Burnt");
        private static readonly int VisibilityId = Shader.PropertyToID("_Visibility");

        private NetObject _net;
        private MaterialPropertyBlock _block;

        /// <summary>Payload seen last, so the string is parsed only when it actually changes.</summary>
        private string _lastPayload;

        /// <summary>Did a valid <c>c</c>/<c>b</c> pair ever arrive? Without it only the stage is drawn.</summary>
        private bool _hasTiming;

        private float _cook;
        private float _burn;
        private float _progress;

        private float _lastCookingTime = float.NegativeInfinity;
        private float _visibility;
        private float _heat;

        private void Awake()
        {
            _net = GetComponentInParent<NetObject>();
            _block = new MaterialPropertyBlock();

            if (gauge == null)
            {
                gauge = GetComponent<Renderer>();
            }
        }

        private void OnEnable()
        {
            if (_net == null)
            {
                return;
            }

            // ⚠️ Pooled patties come back with the previous one's reading; without this reset raw meat
            // would show the last patty's doneness.
            _lastPayload = null;
            _hasTiming = false;
            _progress = 0f;
            _visibility = 0f;
            _heat = 0f;
            _lastCookingTime = float.NegativeInfinity;

            _net.StateChanged += HandleStateChanged;

            ApplyState();
            Push();
        }

        private void OnDisable()
        {
            if (_net != null)
            {
                _net.StateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(NetObject net, NetStateOrigin origin) => ApplyState();

        /// <summary>Reads the payload when it changed, then clamps the local guess to the stage.</summary>
        private void ApplyState()
        {
            string payload = _net.Payload ?? "";
            if (!string.Equals(payload, _lastPayload))
            {
                _lastPayload = payload;
                ReadTiming(payload);
            }

            ClampToStage();
        }

        private void ReadTiming(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                // Never grilled: forget the thresholds too, nothing is counting.
                _hasTiming = false;
                _progress = 0f;
                return;
            }

            if (!TryReadMs(BurgerKinds.PayloadCookMs, out int cookMs) ||
                !TryReadMs(BurgerKinds.PayloadBurnMs, out int burnMs) ||
                cookMs <= 0 || burnMs <= cookMs)
            {
                // ⚠️ Keep the last good thresholds: a payload carrying only `slot` must not blank the gauge.
                return;
            }

            _hasTiming = true;
            _cook = cookMs / 1000f;
            _burn = burnMs / 1000f;

            if (TryReadMs(BurgerKinds.PayloadCookProgress, out int progressMs) && progressMs >= 0)
            {
                _progress = progressMs / 1000f;
            }
        }

        private bool TryReadMs(string key, out int value)
        {
            value = 0;
            return _net.TryGetPayloadValue(key, out string raw) &&
                   int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>⚠️ <c>stage</c> is the only authority: the drift the local count builds up from latency
        /// is wiped at every stage boundary.</summary>
        private void ClampToStage()
        {
            if (!_hasTiming)
            {
                return;
            }

            switch (_net.Stage)
            {
                case BurgerKinds.PattyCooked:
                    _progress = Mathf.Clamp(_progress, _cook, _burn * 0.995f);
                    break;
                case BurgerKinds.PattyBurnt:
                    _progress = _burn;
                    break;
                default:
                    _progress = Mathf.Min(_progress, _cook * 0.995f);
                    break;
            }
        }

        private void Update()
        {
            if (_net == null)
            {
                return;
            }

            bool cooking = BurgerGrill.IsCooking(_net);
            if (cooking)
            {
                _lastCookingTime = Time.time;

                if (_hasTiming)
                {
                    _progress += Time.deltaTime;
                }
            }

            ClampToStage();

            float visible = cooking || Time.time < _lastCookingTime + lingerSeconds ? 1f : 0f;
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            _visibility = Mathf.MoveTowards(_visibility, visible, step);
            _heat = Mathf.MoveTowards(_heat, cooking ? 1f : 0f, step);

            Push();
        }

        private void Push()
        {
            if (gauge == null)
            {
                return;
            }

            bool on = _visibility > 0f;
            if (gauge.enabled != on)
            {
                // A hidden gauge costs a draw call per patty otherwise, and patties are plentiful.
                gauge.enabled = on;
            }

            if (!on)
            {
                return;
            }

            int stage = _net.Stage;
            float fill;
            float cookMark;

            if (_hasTiming && _burn > 0f)
            {
                fill = Mathf.Clamp01(_progress / _burn);
                cookMark = Mathf.Clamp01(_cook / _burn);
            }
            else
            {
                cookMark = Mathf.Clamp01(fallbackCookMark);
                fill = stage == BurgerKinds.PattyBurnt
                    ? 1f
                    : stage == BurgerKinds.PattyCooked
                        ? cookMark
                        : 0f;
            }

            bool warn = _hasTiming && stage == BurgerKinds.PattyCooked &&
                        _progress >= _cook + (_burn - _cook) * warnFraction;

            gauge.GetPropertyBlock(_block);
            _block.SetFloat(FillId, fill);
            _block.SetFloat(CookMarkId, cookMark);
            _block.SetFloat(HeatId, _heat);
            _block.SetFloat(WarnId, warn ? 1f : 0f);
            _block.SetFloat(BurntId, stage == BurgerKinds.PattyBurnt ? 1f : 0f);
            _block.SetFloat(VisibilityId, Mathf.Clamp01(_visibility));
            gauge.SetPropertyBlock(_block);
        }

        /// <summary>⚠️ Pose is written, not inherited: the patty is flipped with a spatula, and a gauge
        /// riding its rotation ends up upside down under the meat.</summary>
        private void LateUpdate()
        {
            if (_net == null || _visibility <= 0f)
            {
                return;
            }

            transform.position = _net.transform.position + Vector3.up * heightOffset;

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 dir = transform.position - camera.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }
    }
}
