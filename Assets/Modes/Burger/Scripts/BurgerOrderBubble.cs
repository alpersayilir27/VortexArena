using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Modes.Burger
{
    /// <summary>The order bubble above a customer: turns the wire recipe
    /// (<c>bun_bottom,patty,cheese,bun_top</c>) into readable lines, draws the same order as coloured
    /// slices and faces the player.
    /// <para>⚠️ The slices are not decoration: the audience may not read yet, so the picture is the
    /// order and the text is the caption.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerOrderBubble : MonoBehaviour
    {
        /// <summary>Recipe kind → slice colour. ⚠️ Kept in sync with the ingredient prefabs' own
        /// materials by eye; a slice in the wrong colour teaches the child the wrong burger.</summary>
        [Serializable]
        private struct KindColor
        {
            public string kind;
            public Color color;
        }

        [Tooltip("Sipariş satırlarının yazıldığı metin.")]
        [SerializeField] private TMP_Text text;

        [Tooltip("Geçici bildirim metni (red sebebi). Boşsa sipariş metninin yerine yazılır.")]
        [SerializeField] private TMP_Text noticeText;

        [Tooltip("Balonun döneceği baş. Boşsa ana kamera kullanılır.")]
        [SerializeField] private Transform head;

        [Tooltip("Balonun kökü (gösterilip gizlenen obje). Boşsa bu obje kullanılır.")]
        [SerializeField] private GameObject root;

        [Tooltip("Renkli dilimlerin dizildiği kök. Boşsa metnin yanında çalışma anında üretilir.")]
        [SerializeField] private RectTransform sliceRoot;

        [Tooltip("Bir dilimin boyutu (RectTransform birimi).")]
        [SerializeField] private Vector2 sliceSize = new Vector2(0.12f, 0.025f);

        [Tooltip("Malzeme türlerinin dilim renkleri. Tabloda olmayan tür gri çizilir.")]
        [SerializeField] private KindColor[] sliceColors = DefaultSliceColors();

        [Tooltip("Sabır göstergesi (balon arka planı). Boşsa sipariş metninin rengi kullanılır.")]
        [SerializeField] private Graphic patienceGraphic;

        [Tooltip("Sabır doluyken renk.")]
        [SerializeField] private Color patienceFull = new Color(0.30f, 0.75f, 0.30f);

        [Tooltip("Sabır biterken renk.")]
        [SerializeField] private Color patienceEmpty = new Color(0.85f, 0.20f, 0.20f);

        private static readonly Color UnknownSliceColor = new Color(0.6f, 0.6f, 0.6f);

        private readonly StringBuilder _builder = new StringBuilder(64);

        /// <summary>Slice pool: rebuilt on every order, so the images are reused rather than destroyed —
        /// a customer arrives every few seconds and the churn would be per-order garbage.</summary>
        private readonly List<Image> _slices = new List<Image>();

        /// <summary>Last recipe shown — the notice borrows the text and this is what comes back.</summary>
        private string _recipe = "";

        /// <summary>Unscaled deadline of the running notice; <c>0</c> = no notice.</summary>
        private float _noticeUntil;

        private void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }

            if (noticeText != null)
            {
                noticeText.gameObject.SetActive(false);
            }

            if (sliceRoot == null)
            {
                sliceRoot = BuildSliceRoot();
            }
        }

        public void Show(string recipe)
        {
            _recipe = recipe;
            _noticeUntil = 0f;

            if (noticeText != null)
            {
                noticeText.gameObject.SetActive(false);
            }

            if (text != null)
            {
                text.text = Format(recipe);
            }

            BuildSlices(recipe);

            if (root != null)
            {
                root.SetActive(true);
            }
        }

        public void Hide()
        {
            _noticeUntil = 0f;

            if (root != null)
            {
                root.SetActive(false);
            }
        }

        /// <summary>Temporary line in this customer's bubble (why a serve was refused). Ignored while the
        /// bubble is hidden — a customer who is not waiting has nothing to say.</summary>
        public void ShowNotice(string notice, float seconds)
        {
            if (root == null || !root.activeSelf || string.IsNullOrEmpty(notice))
            {
                return;
            }

            _noticeUntil = Time.unscaledTime + Mathf.Max(0.1f, seconds);

            if (noticeText != null)
            {
                noticeText.text = notice;
                noticeText.gameObject.SetActive(true);
                return;
            }

            if (text != null)
            {
                text.text = notice;
            }
        }

        /// <summary>Patience gauge, <c>1</c> = fresh, <c>0</c> = out of patience.
        /// <para>⚠️ Approximate by construction — the remaining time is not on the wire
        /// (<see cref="BurgerKinds.CustomerPatienceSeconds"/>).</para></summary>
        public void SetPatience(float remaining01)
        {
            Color color = Color.Lerp(patienceEmpty, patienceFull, Mathf.Clamp01(remaining01));

            if (patienceGraphic != null)
            {
                patienceGraphic.color = color;
                return;
            }

            if (text != null)
            {
                text.color = color;
            }
        }

        private void LateUpdate()
        {
            TickNotice();
            FaceHead();
        }

        private void TickNotice()
        {
            if (_noticeUntil <= 0f || Time.unscaledTime < _noticeUntil)
            {
                return;
            }

            _noticeUntil = 0f;

            if (noticeText != null)
            {
                noticeText.gameObject.SetActive(false);
                return;
            }

            if (text != null)
            {
                text.text = Format(_recipe);
            }
        }

        private void FaceHead()
        {
            Transform target = head;
            if (target == null)
            {
                Camera camera = Camera.main;
                target = camera != null ? camera.transform : null;
            }

            if (target == null)
            {
                return;
            }

            // Billboard: faces AWAY from the head so the text is not mirrored.
            transform.rotation = Quaternion.LookRotation(transform.position - target.position, Vector3.up);
        }

        // ------------------------------------------------------------------- slices

        /// <summary>Slices are drawn BOTTOM TO TOP, the same way the recipe reads and the same way the
        /// burger is stacked on the board.</summary>
        private void BuildSlices(string recipe)
        {
            if (sliceRoot == null)
            {
                return;
            }

            int used = 0;
            if (!string.IsNullOrEmpty(recipe))
            {
                string[] parts = recipe.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    if (part.Length == 0)
                    {
                        continue;
                    }

                    Image slice = TakeSlice(used);
                    slice.color = ResolveSliceColor(part);
                    used++;
                }
            }

            for (int i = used; i < _slices.Count; i++)
            {
                if (_slices[i] != null)
                {
                    _slices[i].gameObject.SetActive(false);
                }
            }
        }

        private Image TakeSlice(int index)
        {
            while (_slices.Count <= index)
            {
                _slices.Add(CreateSlice());
            }

            Image slice = _slices[index];
            if (slice == null)
            {
                slice = CreateSlice();
                _slices[index] = slice;
            }

            slice.transform.SetSiblingIndex(index);
            slice.gameObject.SetActive(true);
            return slice;
        }

        private Image CreateSlice()
        {
            var go = new GameObject("Dilim", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(sliceRoot, false);

            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = sliceSize.x;
            element.preferredHeight = sliceSize.y;

            return go.GetComponent<Image>();
        }

        /// <summary>Runtime fallback root next to the text, so an unbound bubble still draws slices.</summary>
        private RectTransform BuildSliceRoot()
        {
            Transform parent = text != null ? text.transform.parent : transform;

            var go = new GameObject("Dilimler", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(sliceSize.x, sliceSize.y * 6f);
            rect.anchoredPosition = new Vector2(-sliceSize.x, 0f);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = sliceSize.y * 0.15f;

            // First child at the BOTTOM: the recipe reads bottom to top.
            layout.reverseArrangement = true;

            return rect;
        }

        private Color ResolveSliceColor(string kind)
        {
            if (sliceColors != null)
            {
                for (int i = 0; i < sliceColors.Length; i++)
                {
                    if (string.Equals(sliceColors[i].kind, kind, StringComparison.Ordinal))
                    {
                        return sliceColors[i].color;
                    }
                }
            }

            return UnknownSliceColor;
        }

        private static KindColor[] DefaultSliceColors()
        {
            return new[]
            {
                new KindColor { kind = BurgerKinds.BunBottom, color = new Color(0.80f, 0.60f, 0.32f) },
                new KindColor { kind = BurgerKinds.BunTop, color = new Color(0.86f, 0.66f, 0.36f) },
                new KindColor { kind = BurgerKinds.Patty, color = new Color(0.42f, 0.24f, 0.12f) },
                new KindColor { kind = BurgerKinds.Cheese, color = new Color(0.98f, 0.80f, 0.25f) },
                new KindColor { kind = BurgerKinds.Bacon, color = new Color(0.70f, 0.30f, 0.25f) },
                new KindColor { kind = BurgerKinds.Lettuce, color = new Color(0.42f, 0.72f, 0.32f) },
                new KindColor { kind = BurgerKinds.Onion, color = new Color(0.90f, 0.85f, 0.92f) },
                new KindColor { kind = BurgerKinds.Pickle, color = new Color(0.30f, 0.50f, 0.20f) },
                new KindColor { kind = BurgerKinds.Tomato, color = new Color(0.85f, 0.22f, 0.20f) },
                new KindColor { kind = BurgerKinds.Sauce, color = new Color(0.92f, 0.52f, 0.18f) }
            };
        }

        // ------------------------------------------------------------------- text

        private string Format(string recipe)
        {
            _builder.Clear();

            if (string.IsNullOrEmpty(recipe))
            {
                return "";
            }

            string[] parts = recipe.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (_builder.Length > 0)
                {
                    _builder.Append('\n');
                }

                _builder.Append(BurgerKinds.DisplayName(part));
            }

            return _builder.ToString();
        }
    }
}
