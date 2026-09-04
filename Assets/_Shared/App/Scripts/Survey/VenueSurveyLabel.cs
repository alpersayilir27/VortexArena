using TMPro;
using UnityEngine;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// The survey's head-locked instruction panel: a world-space <see cref="TextMeshPro"/> parented
    /// to the head anchor, slightly below the line of sight.
    /// <para>⚠️ Head-locked on purpose — the player is walking along walls with the controller up,
    /// so a panel left standing in the room would be behind them exactly when they need it.</para>
    /// <para>The font is the component's default (<c>TMP_Settings.defaultFontAsset</c>): a font
    /// reference of its own would have to be serialized somewhere, and this scene carries nothing.</para>
    /// </summary>
    internal sealed class VenueSurveyLabel
    {
        private readonly TextMeshPro text;

        internal VenueSurveyLabel(Transform headAnchor)
        {
            var go = new GameObject("SurveyLabel");
            go.transform.SetParent(headAnchor, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 1.6f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            text = go.AddComponent<TextMeshPro>();

            // TMP's 3D size is "10 pt ≈ 1 m", so 0.5 lands at ~5 cm caps at 1.6 m.
            text.fontSize = 0.5f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.color = Color.white;
            text.rectTransform.sizeDelta = new Vector2(1.6f, 0.8f);
            text.text = "";
        }

        internal void SetText(string value)
        {
            if (text != null)
            {
                text.text = value ?? "";
            }
        }
    }
}
