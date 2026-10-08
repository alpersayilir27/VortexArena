using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Cooking gauge above a patty: thresholds and the last measured value come from the patty's
    /// <c>s</c> payload (§10.5), the seconds in between are counted locally while the patty lies on a
    /// grill.
    /// <para>⚠️ The green window is the player's "take it off now" decision, so its edges must come from
    /// the server (<c>burger.cookSeconds/burnSeconds</c>) — hard-coding them on the client turns a tuned
    /// shift into a lie the moment the config changes.</para>
    /// <para>⚠️ Two drawing paths on purpose: <see cref="uiRoot"/> (the Lokanta canvas) when the prefab
    /// carries one, otherwise the old <see cref="gauge"/> renderer. The stage/progress maths is shared,
    /// so the two can never disagree about doneness.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerPattyThermometer : MonoBehaviour
    {
        [Tooltip("Göstergeyi çizen renderer (Quad). Arayüz kökü bağlıysa kullanılmaz.")]
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

        // ------------------------------------------------------------ lokanta ui

        [Tooltip("Arayüz göstergesinin kökü. Bağlıysa renderer yerine bu sürülür.")]
        [SerializeField] private Transform uiRoot;

        [Tooltip("Arayüzün sönümlemesi.")]
        [SerializeField] private CanvasGroup uiGroup;

        [Tooltip("Cıva sütunu (alta ankrajlı; yüksekliği doluluk oranı).")]
        [SerializeField] private RectTransform mercury;

        [SerializeField] private UiShape mercuryShape;

        [Tooltip("Hazneye dökülen cıva (renk cıvayı izler).")]
        [SerializeField] private UiShape bulbShape;

        [Tooltip("Kartın zemini (duruma göre boyanır).")]
        [SerializeField] private UiShape cardShape;

        [Tooltip("Yeşil pencere bandı (pişti → yanmaya başladı aralığı).")]
        [SerializeField] private RectTransform zone;

        [SerializeField] private TMP_Text label;

        [SerializeField] private Image icon;

        [SerializeField] private Sprite iconFlame;
        [SerializeField] private Sprite iconCheck;
        [SerializeField] private Sprite iconWarn;
        [SerializeField] private Sprite iconX;

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int CookMarkId = Shader.PropertyToID("_CookMark");
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly int WarnId = Shader.PropertyToID("_Warn");
        private static readonly int BurntId = Shader.PropertyToID("_Burnt");
        private static readonly int VisibilityId = Shader.PropertyToID("_Visibility");

        /// <summary>Card blink rate over the warn threshold (Hz).</summary>
        private const float BlinkHz = 2f;

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

        /// <summary>Last zone span written, so the band's rect is touched only when it moves.</summary>
        private Vector2 _zoneSpan = new Vector2(-1f, -1f);

        private void Awake()
        {
            _net = GetComponentInParent<NetObject>();
            _block = new MaterialPropertyBlock();

            // ⚠️ Only the renderer path hunts for a Renderer: with a UI child bound, picking up some
            // unrelated renderer on this object would push shader properties into the patty's own mesh.
            if (gauge == null && uiRoot == null)
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
            _zoneSpan = new Vector2(-1f, -1f);

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

        // ------------------------------------------------------------------ shared

        /// <summary>Doneness in gauge terms, shared by both drawing paths.</summary>
        private void Measure(out float fill, out float cookMark, out bool warn, out bool burnt)
        {
            int stage = _net.Stage;

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

            warn = _hasTiming && stage == BurgerKinds.PattyCooked &&
                   _progress >= _cook + (_burn - _cook) * warnFraction;
            burnt = stage == BurgerKinds.PattyBurnt;
        }

        private void Push()
        {
            if (uiRoot != null)
            {
                PushUi();
                return;
            }

            PushRenderer();
        }

        // ------------------------------------------------------------- lokanta ui

        private void PushUi()
        {
            bool on = _visibility > 0f;
            if (uiRoot.gameObject.activeSelf != on)
            {
                // A faded-out canvas still rebuilds and draws; patties are plentiful.
                uiRoot.gameObject.SetActive(on);
            }

            if (!on)
            {
                return;
            }

            if (uiGroup != null)
            {
                uiGroup.alpha = Mathf.Clamp01(_visibility);
            }

            Measure(out float fill, out float cookMark, out bool warn, out bool burnt);
            bool cooked = _net.Stage == BurgerKinds.PattyCooked;

            Color mercuryA;
            Color mercuryB;
            Color card;
            Color labelColor;
            Sprite iconSprite;
            Color iconColor;
            string word;

            if (burnt)
            {
                mercuryA = Lokanta.MercuryBurntA;
                mercuryB = Lokanta.MercuryBurntB;
                card = Lokanta.BurntCard;
                labelColor = Fade(Lokanta.Ink, 0.7f);
                iconSprite = iconX;
                iconColor = Fade(Lokanta.Ink, 0.7f);
                word = "YANDI";
            }
            else if (cooked && warn)
            {
                mercuryA = Lokanta.MercuryHotA;
                mercuryB = Lokanta.MercuryHotB;
                // 2 Hz blink: the only moving part, so the eye finds it without reading.
                card = Mathf.Repeat(Time.unscaledTime * BlinkHz, 1f) < 0.5f
                    ? Lokanta.HotCard
                    : Lokanta.Cream;
                labelColor = Lokanta.Red2;
                iconSprite = iconWarn;
                iconColor = Lokanta.Red2;
                word = "YANIYOR";
            }
            else if (cooked)
            {
                mercuryA = Lokanta.MercuryCookedA;
                mercuryB = Lokanta.MercuryCookedB;
                card = Lokanta.Cream;
                labelColor = Lokanta.Green2;
                iconSprite = iconCheck;
                iconColor = Lokanta.Green2;
                word = "HAZIR!";
            }
            else
            {
                mercuryA = Lokanta.MercuryRawA;
                mercuryB = Lokanta.MercuryRawB;
                card = Lokanta.Cream;
                labelColor = Lokanta.Ink;
                // Raw shows the flame only while the meat is actually over fire.
                iconSprite = _heat > 0.5f ? iconFlame : null;
                iconColor = Lokanta.Flame;
                word = "ÇİĞ";
            }

            if (mercuryShape != null)
            {
                mercuryShape.Fill(mercuryA, mercuryB, UiGradientMode.Vertical);
            }

            if (bulbShape != null)
            {
                bulbShape.Fill(mercuryB);
            }

            if (cardShape != null)
            {
                cardShape.Fill(card);
            }

            if (mercury != null)
            {
                float h = TrackHeight(mercury);
                Vector2 size = mercury.sizeDelta;
                size.y = h * fill;
                mercury.sizeDelta = size;
            }

            WriteZone(cookMark);

            if (label != null)
            {
                label.text = word;
                label.color = labelColor;
            }

            if (icon != null)
            {
                bool show = iconSprite != null;
                if (icon.enabled != show)
                {
                    icon.enabled = show;
                }

                if (show)
                {
                    icon.sprite = iconSprite;
                    icon.color = iconColor;
                }
            }
        }

        /// <summary>Green window = cooked threshold → the moment the warning starts, both in gauge
        /// units. ⚠️ Comes from the payload, never from a constant (see the class remark).</summary>
        private void WriteZone(float cookMark)
        {
            if (zone == null)
            {
                return;
            }

            float top = _hasTiming && _burn > 0f
                ? Mathf.Clamp01((_cook + (_burn - _cook) * warnFraction) / _burn)
                : Mathf.Clamp01(cookMark + (1f - cookMark) * warnFraction);
            var span = new Vector2(cookMark, Mathf.Max(top, cookMark));
            if (span == _zoneSpan)
            {
                return;
            }

            _zoneSpan = span;

            float h = TrackHeight(zone);
            Vector2 size = zone.sizeDelta;
            size.y = (span.y - span.x) * h;
            zone.sizeDelta = size;
            zone.anchoredPosition = new Vector2(zone.anchoredPosition.x, span.x * h);
        }

        /// <summary>Usable height of a bottom-anchored bar: read off the PARENT, so the one place the
        /// tube's size lives is the generated prefab.</summary>
        private static float TrackHeight(RectTransform bar)
        {
            var parent = bar.parent as RectTransform;
            return parent != null ? parent.rect.height : bar.rect.height;
        }

        private static Color Fade(Color c, float alpha)
        {
            c.a *= alpha;
            return c;
        }

        // ---------------------------------------------------------- renderer path

        private void PushRenderer()
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

            Measure(out float fill, out float cookMark, out bool warn, out bool burnt);

            gauge.GetPropertyBlock(_block);
            _block.SetFloat(FillId, fill);
            _block.SetFloat(CookMarkId, cookMark);
            _block.SetFloat(HeatId, _heat);
            _block.SetFloat(WarnId, warn ? 1f : 0f);
            _block.SetFloat(BurntId, burnt ? 1f : 0f);
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
