using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// The tool's world-space panel: slot states, tracking, the exit forecast and the help line.
    /// <para>⚠️ A 3D <c>TextMeshPro</c>, not a canvas: there is no camera-space UI in the tool scene
    /// and a world canvas would need its own event/scale setup for no gain.</para>
    /// <para>⚠️ No glyph symbols (✓, ●): the panel font is dynamic and a missing glyph renders as a
    /// box. States are words, emphasis is <c>color</c>.</para>
    /// </summary>
    public sealed class GripCaptureHud
    {
        private const string FontResource = "UI/Fonts/Barlow-Medium SDF";

        private const string Green = "#7CFF8A";
        private const string Red = "#FF6B6B";

        private const float PanelWidth = 0.42f;
        private const float PanelHeight = 0.32f;
        private const float SideOffset = 0.30f;
        private const float UpOffset = 0.08f;

        private static readonly Color BackgroundColour = new Color(0f, 0f, 0f, 0.6f);

        /// <summary>Finger initials: başparmak, işaret, orta, yüzük, serçe.</summary>
        private static readonly string[] FingerLetters = { "B", "İ", "O", "Y", "S" };

        private readonly StringBuilder _builder = new StringBuilder(512);

        // [kind][hand] — 0 = left, 1 = right.
        private readonly string[,] _slots = { { "—", "—" }, { "—", "—" } };
        private readonly bool[] _tracked = new bool[2];
        private readonly bool[] _palmInside = new bool[2];
        private readonly bool[,] _touching = new bool[2, 5];
        private readonly bool[,] _penetrating = new bool[2, 5];

        private Transform _root;
        private TextMeshPro _text;
        private string _itemName = "—";
        private bool _twoHanded;
        private string _forecast = "Çıkışta: değişiklik yok";
        private bool _forecastWarning;
        private string _multimodal = "—";
        private string _lastAction;
        private bool _lastActionOk;
        private string _error;
        private bool _dirty = true;

        /// <summary>Builds the panel; <c>false</c> when the font asset is missing.</summary>
        public bool Build(Transform parent)
        {
            var font = Resources.Load<TMP_FontAsset>(FontResource);
            if (font == null)
            {
                Debug.LogError($"[GripCapture] Panel yazı tipi yüklenemedi: {FontResource}");
                return false;
            }

            var root = new GameObject("Panel");
            root.transform.SetParent(parent, false);
            _root = root.transform;

            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Arkaplan";
            UnityEngine.Object.Destroy(background.GetComponent<Collider>());
            background.transform.SetParent(_root, false);
            background.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            background.transform.localScale = new Vector3(PanelWidth * 1.05f, PanelHeight * 1.05f, 1f);
            GripGhostMaterial.Apply(background, BackgroundColour);

            var textObject = new GameObject("Metin");
            textObject.transform.SetParent(_root, false);
            textObject.AddComponent<RectTransform>();
            _text = textObject.AddComponent<TextMeshPro>();
            _text.font = font;
            _text.richText = true;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.color = Color.white;
            _text.rectTransform.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            // Auto sizing instead of a fixed font size: TMP's size unit depends on the rect and the
            // scale, and a hardcoded value renders either invisible or page-sized here.
            _text.enableAutoSizing = true;
            _text.fontSizeMin = 0.005f;
            _text.fontSizeMax = 0.05f;

            return true;
        }

        /// <summary>Moves the panel beside the item pivot (called on every re-place).</summary>
        public void Place(Vector3 pivotWorld, Transform head)
        {
            if (_root == null || head == null)
            {
                return;
            }

            Vector3 right = Flat(head.right);
            _root.position = pivotWorld - right * SideOffset + Vector3.up * UpOffset;
            Tick(head);
        }

        /// <summary>Yaw-billboards the panel and flushes pending text.</summary>
        public void Tick(Transform head)
        {
            if (_root == null)
            {
                return;
            }

            if (head != null)
            {
                Vector3 toHead = Flat(head.position - _root.position);
                if (toHead.sqrMagnitude > 1e-6f)
                {
                    // Forward points AT the head: TMP text is readable from its +Z side.
                    _root.rotation = Quaternion.LookRotation(toHead, Vector3.up);
                }
            }

            if (_dirty)
            {
                _dirty = false;
                _text.SetText(Compose());
            }
        }

        public void SetItem(string itemName, bool twoHanded)
        {
            _itemName = string.IsNullOrEmpty(itemName) ? "—" : itemName;
            _twoHanded = twoHanded;
            _dirty = true;
        }

        public void SetSlot(GripSocketKind kind, bool rightHand, string state)
        {
            _slots[kind == GripSocketKind.Secondary ? 1 : 0, rightHand ? 1 : 0] = state;
            _dirty = true;
        }

        public void SetForecast(string text, bool warning)
        {
            _forecast = text;
            _forecastWarning = warning;
            _dirty = true;
        }

        public void SetHand(bool rightHand, bool tracked, bool[] touching, bool[] penetrating,
            bool palmInside)
        {
            int hand = rightHand ? 1 : 0;
            _tracked[hand] = tracked;
            _palmInside[hand] = palmInside;

            for (int i = 0; i < 5; i++)
            {
                _touching[hand, i] = touching != null && i < touching.Length && touching[i];
                _penetrating[hand, i] = penetrating != null && i < penetrating.Length && penetrating[i];
            }

            _dirty = true;
        }

        public void SetMultimodal(string status)
        {
            _multimodal = status;
            _dirty = true;
        }

        public void SetLastAction(string message, bool success)
        {
            _lastAction = message;
            _lastActionOk = success;
            _dirty = true;
        }

        public void SetError(string message)
        {
            _error = message;
            _dirty = true;
        }

        private string Compose()
        {
            _builder.Clear();
            _builder.AppendLine($"<b>KAVRAMA STÜDYOSU</b> · {_itemName}");
            _builder.AppendLine($"Şu an: {(_twoHanded ? "Çift kavramalı" : "Tek kavramalı")}");
            _builder.AppendLine(SlotLine("1. kavrama", GripSocketKind.Primary));
            _builder.AppendLine(SlotLine("2. kavrama", GripSocketKind.Secondary));
            _builder.AppendLine(_forecastWarning ? Colour(_forecast, Red) : _forecast);
            _builder.AppendLine(HandLine("Sağ el", true));
            _builder.AppendLine(HandLine("Sol el", false));
            _builder.AppendLine($"Giriş: {_multimodal}");

            if (!string.IsNullOrEmpty(_lastAction))
            {
                _builder.AppendLine(Colour(_lastAction, _lastActionOk ? Green : Red));
            }

            if (!string.IsNullOrEmpty(_error))
            {
                _builder.AppendLine(Colour(_error, Red));
            }

            _builder.Append("Sol kumanda X/Y → SAĞ el · Sağ kumanda A/B → SOL el · X/A = 1. kavrama · " +
                            "Y/B = 2. kavrama · Çubuk: döndür · Çubuğa bas: önüne getir");
            return _builder.ToString();
        }

        private string SlotLine(string label, GripSocketKind kind)
        {
            int row = kind == GripSocketKind.Secondary ? 1 : 0;
            return $"{label} · Sağ: {_slots[row, 1]} · Sol: {_slots[row, 0]}";
        }

        private string HandLine(string label, bool rightHand)
        {
            int hand = rightHand ? 1 : 0;
            if (!_tracked[hand])
            {
                return $"{label}: izlenmiyor";
            }

            var line = new StringBuilder($"{label}: izleniyor · temas:");

            for (int i = 0; i < FingerLetters.Length; i++)
            {
                string letter = FingerLetters[i];
                if (_penetrating[hand, i])
                {
                    letter = Colour(letter, Red);
                }
                else if (_touching[hand, i])
                {
                    letter = Colour(letter, Green);
                }

                line.Append(' ').Append(letter);
            }

            if (_palmInside[hand])
            {
                line.Append(" · ").Append(Colour("AVUÇ İÇERİDE", Red));
            }

            return line.ToString();
        }

        private static string Colour(string text, string hex)
        {
            return $"<color={hex}>{text}</color>";
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }
    }
}
