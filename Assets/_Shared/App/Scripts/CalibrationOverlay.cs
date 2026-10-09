using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;

namespace VortexArena.App
{
    /// <summary>
    /// Player's always-on-top calibration card: prompt while uncalibrated, progress after point A,
    /// short success notice.
    /// <para>
    /// Self-bootstrapping persistent singleton — visuals live entirely in the prefab
    /// (<c>Resources/UI/CalibrationOverlay</c>); this class only picks a state and writes texts,
    /// colours and the hand-centred title row.
    /// </para>
    /// <para>
    /// ⚠️ No role gate here — <see cref="AppSingletons"/> installs it in the player branch only.
    /// </para>
    /// </summary>
    public class CalibrationOverlay : MonoBehaviour
    {
        /// <summary>Prefab path inside <c>Resources</c> (no extension).</summary>
        public const string ResourcePath = "UI/CalibrationOverlay";

        /// <summary>Sorting offset (<see cref="UiDrawOnTop"/>): above mode HUD (10), match result
        /// (20) and the connection card (30) — the prompt must win over every gameplay layer.</summary>
        private const int SortingOffset = 40;

        /// <summary>How long the success notice stays after calibration lands (s).</summary>
        private const float SuccessSeconds = 2f;

        private const string PromptTitle = "Lütfen kalibre alınız";
        private const string PromptHint = "Sağ kumandada A basılıyken B'ye bas";
        private const string AwaitingTitle = "1. kalibre alındı. 2.si bekleniyor";
        private const string AwaitingHint = "İkinci işarette A basılıyken B'ye bas";
        private const string SuccessTitle = "Kalibre başarıyla alındı";

        private static CalibrationOverlay _instance;

        // ⚠️ Fields are [SerializeField] — visuals come FROM THE PREFAB (wired by GirdapUiBuilder).
        [Header("Kök")]
        [SerializeField] private Canvas canvas;

        [Header("Kart")]
        [SerializeField] private UiShape frame;
        [SerializeField] private Image rule;
        [SerializeField] private Image ruleLeft;
        [SerializeField] private Image ruleRight;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private StatusPlate hint;

        [Header("Ölçüler (CSS .ccard)")]
        [SerializeField] private float iconSize = 34f;
        [SerializeField] private float iconGap = 12f;
        [SerializeField] private float rowMaxWidth = 672f;

        private HudFollow _hudFollow;
        private CardState _state = CardState.Hidden;

        /// <summary>A success notice may only FOLLOW a prompt: a silent anchor restore on map change
        /// raises calibration too and must not flash "başarıyla alındı".</summary>
        private bool _prompted;

        private float _successUntil = -1f;

        private enum CardState
        {
            Hidden,
            Prompt,
            AwaitingB,
            Success
        }

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — "is it needed this session"
        /// is <see cref="AppSingletons"/>'s call (rationale lives there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var prefab = Resources.Load<CalibrationOverlay>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[CalibrationOverlay] '{ResourcePath}' prefabı bulunamadı — kalibre " +
                               "uyarısı çizilemeyecek.");
                return;
            }

            Spawn(prefab);
        }

        private static CalibrationOverlay Spawn(CalibrationOverlay prefab)
        {
            CalibrationOverlay overlay = Instantiate(prefab);
            overlay.name = "[CalibrationOverlay]";
            DontDestroyOnLoad(overlay.gameObject);
            _instance = overlay;
            return overlay;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Everything on this card exists in the prefab, so one call covers it: walls and the
            // blackout quad must not cover the calibration prompt.
            UiDrawOnTop.Apply(gameObject, SortingOffset);

            _hudFollow = GetComponent<HudFollow>();
            if (_hudFollow != null)
            {
                // Drawing on top of a NEARER wall is a stereo depth conflict; pulling the card in
                // front of the wall keeps depth consistent. Safe here — no head-locked child.
                _hudFollow.AvoidWalls = true;
            }

            // Don't carry a stale visible state over from a Play entry without domain reload.
            _state = CardState.Hidden;
            Apply(CardState.Hidden);
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        /// <summary>Restarts <see cref="HudFollow"/> so the card snaps into the new scene's rig.</summary>
        private void HandleActiveSceneChanged(Scene previous, Scene current)
        {
            if (_hudFollow != null)
            {
                _hudFollow.enabled = false;
                _hudFollow.enabled = true; // OnEnable → resets _initialized
            }
        }

        private void Update()
        {
            CardState state = Evaluate();
            if (state == _state)
            {
                return;
            }

            _state = state;
            Apply(state);
        }

        /// <summary>Polling statics is cheaper than keeping four subscriptions in sync with the
        /// success timer.</summary>
        private CardState Evaluate()
        {
            CardState state;
            if (!CalibrationState.AppearsCalibrated)
            {
                _prompted = true;
                _successUntil = -1f;
                state = ArenaCalibrator.AwaitingPointB ? CardState.AwaitingB : CardState.Prompt;
            }
            else if (_prompted)
            {
                _prompted = false;
                _successUntil = Time.unscaledTime + SuccessSeconds;
                state = CardState.Success;
            }
            else
            {
                state = Time.unscaledTime < _successUntil ? CardState.Success : CardState.Hidden;
            }

            // Display-only veto, AFTER the bookkeeping above: the survey scene is its own measurement
            // UI (the card would cover it) and with no camera yet HudFollow parks the card at origin.
            if (SceneManager.GetActiveScene().name == AppSession.SceneVenueSurvey ||
                Camera.main == null)
            {
                return CardState.Hidden;
            }

            return state;
        }

        private void Apply(CardState state)
        {
            if (state == CardState.Hidden)
            {
                if (canvas != null)
                {
                    canvas.enabled = false;
                }

                return;
            }

            bool success = state == CardState.Success;
            Color tone = success ? Girdap.Good : Girdap.Warn;
            string iconName = success ? "Check" : state == CardState.AwaitingB ? "Timer" : "Warn";
            string titleText = success
                ? SuccessTitle
                : state == CardState.AwaitingB ? AwaitingTitle : PromptTitle;
            string hintText = success
                ? ""
                : state == CardState.AwaitingB ? AwaitingHint : PromptHint;

            ApplyTone(tone);

            if (icon != null)
            {
                icon.sprite = Girdap.Icon(iconName);
            }

            if (title != null)
            {
                title.text = titleText;
            }

            if (hint != null)
            {
                hint.Set(hintText, CombatStatusKind.None);
            }

            CentreRow(titleText);

            if (canvas != null)
            {
                canvas.enabled = true;
            }
        }

        /// <summary>⚠️ Colour goes through <see cref="Graphic.color"/> / the UiShape API only —
        /// <c>fontMaterial</c>/<c>material</c> were swapped by <see cref="UiDrawOnTop"/>.</summary>
        private void ApplyTone(Color tone)
        {
            Color border = tone;
            border.a = 0.65f;
            if (frame != null)
            {
                frame.Outline(1f, border, Girdap.Rgba(170, 205, 255, 0.14f));
            }

            Color flank = tone;
            flank.a = 0.8f;

            if (rule != null)
            {
                rule.color = tone;
            }

            if (ruleLeft != null)
            {
                ruleLeft.color = flank;
            }

            if (ruleRight != null)
            {
                ruleRight.color = flank;
            }

            if (icon != null)
            {
                icon.color = tone;
            }

            if (title != null)
            {
                title.color = tone;
            }
        }

        /// <summary>Centres icon + gap + text as one block (no LayoutGroup in this project).</summary>
        private void CentreRow(string titleText)
        {
            if (title == null)
            {
                return;
            }

            RectTransform titleRect = title.rectTransform;
            float textWidth = Mathf.Min(rowMaxWidth - iconSize - iconGap,
                Mathf.Ceil(title.GetPreferredValues(titleText).x));
            float total = iconSize + iconGap + textWidth;
            float left = -total * 0.5f;

            if (icon != null)
            {
                RectTransform iconRect = icon.rectTransform;
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = new Vector2(left + iconSize * 0.5f,
                    iconRect.anchoredPosition.y);
            }

            titleRect.sizeDelta = new Vector2(textWidth, titleRect.sizeDelta.y);
            titleRect.anchoredPosition = new Vector2(left + iconSize + iconGap + textWidth * 0.5f,
                titleRect.anchoredPosition.y);
        }
    }
}
