using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// One kill feed plate (CSS <c>.kf</c>): killer on a team-colored plate, weapon in the middle,
    /// victim on a "dead" plate — or victim + a sentence when there is no killer.
    /// <para>
    /// ⚠️ <b>Widths are measured, not laid out:</b> the kit forbids Layout Groups, and a plate that
    /// does not hug its text reads as a rendering bug. The whole row is re-measured on bind, which is
    /// why <see cref="AdminKillFeedView"/> only rebinds when the feed actually changed.
    /// </para>
    /// <para><b>Look comes from the prefab</b> (<c>AdminHud.prefab</c> → the feed's inactive
    /// template); this class only sizes, colors and fills it.</para>
    /// </summary>
    public class AdminKillFeedRow : MonoBehaviour
    {
        /// <summary>CSS <c>.kf</c> height.</summary>
        public const float Height = 36f;

        /// <summary>CSS <c>.kf.new</c> height — the newest line is bigger, not just brighter.</summary>
        public const float NewHeight = 52f;

        /// <summary>Parallelogram slant of the plate (CSS <c>clip-path</c>, px). Fixed at any height.</summary>
        private const float Slant = 12f;

        /// <summary>CSS <c>.kf > span:first-child/:last-child</c> outer padding.</summary>
        private const float EdgePad = 24f;

        /// <summary>CSS <c>.kf > span</c> padding on every inner side.</summary>
        private const float Pad = 14f;

        /// <summary>CSS <c>.kf > span { gap }</c> — icon to text.</summary>
        private const float IconGap = 6f;

        /// <summary>CSS <c>.kf.new::before</c> accent underline height.</summary>
        private const float UnderlineHeight = 3f;

        [SerializeField] private RectTransform root;

        [Tooltip("Satırın yaş solması (CSS .kf:nth-child opaklıkları) buradan uygulanır.")]
        [SerializeField] private CanvasGroup fade;

        [SerializeField] private UiShape killerShape;
        [SerializeField] private TextMeshProUGUI killerLabel;
        [SerializeField] private UiShape weaponShape;
        [SerializeField] private TextMeshProUGUI weaponLabel;
        [SerializeField] private UiShape victimShape;
        [SerializeField] private Image victimIcon;
        [SerializeField] private TextMeshProUGUI victimLabel;
        [SerializeField] private UiShape sentenceShape;
        [SerializeField] private TextMeshProUGUI sentenceLabel;

        [Tooltip("Yalnız en yeni satırda görünen vurgu çizgisi (CSS .kf.new::before).")]
        [SerializeField] private UiShape newUnderline;

        /// <summary>
        /// Fills the plate. <paramref name="weapon"/> is the already resolved weapon display name;
        /// <paramref name="newest"/> draws the <c>.kf.new</c> variant and <paramref name="alpha"/> the
        /// age fade. <paramref name="top"/> is the row's offset from the feed's top edge.
        /// </summary>
        public void Bind(AdminKillFeedEntry entry, string weapon, bool newest, float alpha, float top)
        {
            if (entry == null || root == null)
            {
                return;
            }

            float h = newest ? NewHeight : Height;
            float nameSize = newest ? 28f : 20f;
            float weaponSize = newest ? 16f : 13f;
            float iconSize = newest ? 20f : 16f;

            if (fade != null)
            {
                fade.alpha = alpha;
            }

            bool hasKiller = entry.kind == AdminKillKind.Kill;
            float x = 0f;

            Show(killerShape, hasKiller);
            Show(weaponShape, hasKiller);
            Show(sentenceShape, !hasKiller);

            if (hasKiller)
            {
                killerLabel.fontSize = nameSize;
                killerLabel.text = entry.killerName;
                killerLabel.color = Color.white;
                killerShape.Slant(-Slant, 0f)
                    .Fill(Girdap.TeamHi(entry.killerTeam), Girdap.TeamLo(entry.killerTeam),
                        UiGradientMode.Vertical);
                x += Put(killerShape, killerLabel, null, 0f, x, h, EdgePad, Pad);

                weaponLabel.fontSize = weaponSize;
                weaponLabel.text = weapon ?? "";
                weaponShape.Slant(0f, 0f).Fill(Girdap.KfMidA, Girdap.KfMidB, UiGradientMode.Vertical);
                x += Put(weaponShape, weaponLabel, null, 0f, x, h, Pad, Pad);

                x += Victim(entry, iconSize, nameSize, x, h, Pad, EdgePad, 0f, Slant);
            }
            else
            {
                x += Victim(entry, iconSize, nameSize, x, h, EdgePad, Pad, -Slant, 0f);

                sentenceLabel.fontSize = 18f; // CSS `.kf .t` keeps 18 even on the new line
                sentenceLabel.text = Sentence(entry.kind);
                sentenceLabel.color = Girdap.Text;
                sentenceShape.Slant(0f, Slant).Fill(Girdap.KfMidA, Girdap.KfMidB, UiGradientMode.Vertical);
                x += Put(sentenceShape, sentenceLabel, null, 0f, x, h, Pad, EdgePad);
            }

            root.anchoredPosition = new Vector2(0f, -top);
            root.sizeDelta = new Vector2(x, h);

            if (newUnderline != null)
            {
                newUnderline.gameObject.SetActive(newest);
                // Stops Slant px short: the parallelogram's BOTTOM edge is that much narrower, and a
                // full-width bar would stick out of the plate (there is no clip mask here).
                newUnderline.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(0f, x - Slant), UnderlineHeight);
            }
        }

        private float Victim(AdminKillFeedEntry entry, float iconSize, float nameSize, float x,
            float h, float padLeft, float padRight, float slantLeft, float slantRight)
        {
            VictimColors(entry.victimTeam, out Color a, out Color b, out Color fg);

            victimLabel.fontSize = nameSize;
            victimLabel.text = entry.victimName;
            victimLabel.color = fg;

            if (victimIcon != null)
            {
                victimIcon.color = fg;
            }

            victimShape.Slant(slantLeft, slantRight).Fill(a, b, UiGradientMode.Vertical);
            return Put(victimShape, victimLabel, victimIcon, iconSize, x, h, padLeft, padRight);
        }

        /// <summary>Dead plate colors; a teamless victim (FFA) falls back to the neutral mid plate.</summary>
        private static void VictimColors(string team, out Color a, out Color b, out Color fg)
        {
            if (team == "red")
            {
                a = Girdap.DeadRedA;
                b = Girdap.DeadRedB;
                fg = Girdap.DeadRedFg;
                return;
            }

            if (team == "blue")
            {
                a = Girdap.DeadBlueA;
                b = Girdap.DeadBlueB;
                fg = Girdap.DeadBlueFg;
                return;
            }

            a = Girdap.KfMidA;
            b = Girdap.KfMidB;
            fg = Girdap.Muted;
        }

        /// <summary>
        /// ⚠️ Same wording as <see cref="KillFeedText"/>: a suicide shown as "öldü" is
        /// indistinguishable from a server death, and the operator investigates a bug that never
        /// happened.
        /// </summary>
        private static string Sentence(AdminKillKind kind)
        {
            switch (kind)
            {
                case AdminKillKind.Suicide: return "kendini havaya uçurdu";
                case AdminKillKind.Obstacle: return "engelde kaldı";
                default: return "öldü";
            }
        }

        /// <summary>Lays out one plate segment and returns its width.</summary>
        private static float Put(UiShape shape, TextMeshProUGUI label, Image icon, float iconSize,
            float x, float h, float padLeft, float padRight)
        {
            float textWidth = Mathf.Ceil(label.GetPreferredValues(label.text).x);
            bool hasIcon = icon != null && icon.gameObject.activeSelf;
            float width = padLeft + (hasIcon ? iconSize + IconGap : 0f) + textWidth + padRight;

            RectTransform rect = shape.rectTransform;
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, h);

            float cursor = padLeft;
            if (hasIcon)
            {
                RectTransform iconRect = icon.rectTransform;
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = new Vector2(cursor, 0f);
                cursor += iconSize + IconGap;
            }

            RectTransform labelRect = label.rectTransform;
            labelRect.anchoredPosition = new Vector2(cursor, 0f);
            labelRect.sizeDelta = new Vector2(textWidth, h);
            return width;
        }

        private static void Show(UiShape shape, bool visible)
        {
            if (shape != null && shape.gameObject.activeSelf != visible)
            {
                shape.gameObject.SetActive(visible);
            }
        }
    }
}
