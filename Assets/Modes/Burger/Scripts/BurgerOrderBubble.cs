using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;

namespace VortexArena.Modes.Burger
{
    /// <summary>The order bubble above a customer: turns the wire recipe
    /// (<c>bun_bottom,patty,cheese,bun_top</c>) into a stacked burger picture plus a readable list,
    /// and faces the player.
    /// <para>⚠️ The stack is not decoration: the audience may not read yet, so the picture is the
    /// order and the list is the caption.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerOrderBubble : MonoBehaviour
    {
        /// <summary>How many ingredients the generated bubble can draw. A longer recipe is clipped —
        /// the server's recipes are shorter than this by design (§10.5).</summary>
        public const int Capacity = 7;

        [Tooltip("Sipariş satırlarının yazıldığı metin (yalnız satır yuvaları bağlı değilse).")]
        [SerializeField] private TMP_Text text;

        [Tooltip("Geçici bildirim metni (red sebebi).")]
        [SerializeField] private TMP_Text noticeText;

        [Tooltip("Bildirim etiketinin kökü. Boşsa yalnız metin gösterilir.")]
        [SerializeField] private GameObject noticeRoot;

        [Tooltip("Balonun döneceği baş. Boşsa ana kamera kullanılır.")]
        [SerializeField] private Transform head;

        [Tooltip("Balonun kökü (gösterilip gizlenen obje). Boşsa bu obje kullanılır.")]
        [SerializeField] private GameObject root;

        [Tooltip("Soldaki burger yığınının dilimleri (alttan üste).")]
        [SerializeField] private UiShape[] slabs;

        [Tooltip("Dilimlerin çizgi katmanı (yalnız pastırma).")]
        [SerializeField] private UiStripes[] slabStripes;

        [Tooltip("Dilimlerin susam katmanı (yalnız üst ekmek).")]
        [SerializeField] private GameObject[] slabSesame;

        [Tooltip("Dilimler arası boşluk.")]
        [SerializeField] private float slabGap = 2f;

        [Tooltip("Sağdaki liste satırları (alttan üste).")]
        [SerializeField] private RectTransform[] listRows;

        [Tooltip("Satır başındaki renk noktaları.")]
        [SerializeField] private UiShape[] listDots;

        [Tooltip("Satır metinleri.")]
        [SerializeField] private TMP_Text[] listLabels;

        [Tooltip("Satır yüksekliği + boşluk.")]
        [SerializeField] private float rowPitch = 28f;

        [Tooltip("Sabır çubuğunun dolan parçası.")]
        [SerializeField] private RectTransform patienceFill;

        [Tooltip("Sabır çubuğunun rengini taşıyan şekil.")]
        [SerializeField] private UiShape patienceShape;

        [Tooltip("Sabır çubuğu tam dolu genişliği.")]
        [SerializeField] private float patienceWidth = 104f;

        [Tooltip("Müşterinin yüzü.")]
        [SerializeField] private Image moodFace;

        [SerializeField] private Sprite moodHappy;
        [SerializeField] private Sprite moodMeh;
        [SerializeField] private Sprite moodSad;

        private readonly StringBuilder _builder = new StringBuilder(64);

        /// <summary>Last recipe shown — the legacy single-text path borrows the label and this is what
        /// comes back when the notice expires.</summary>
        private string _recipe = "";

        /// <summary>Unscaled deadline of the running notice; <c>0</c> = no notice.</summary>
        private float _noticeUntil;

        private void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }

            // The customer stands behind the order window: depth-tested, the frame and the pass wall
            // would hide the order. Offset 0 keeps it under the obstacle blackout (distance-sorted).
            UiDrawOnTop.Apply(gameObject, 0);
            HideNotice();
        }

        public void Show(string recipe)
        {
            _recipe = recipe;
            _noticeUntil = 0f;
            HideNotice();

            int used = BuildRows(recipe);
            BuildStack(recipe);

            // Legacy fallback: an un-regenerated prefab has no row slots, only the one text block.
            if (used < 0 && text != null)
            {
                text.text = Format(recipe);
            }

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

        /// <summary>Temporary sticker on this customer's bubble (why a serve was refused). Ignored while
        /// the bubble is hidden — a customer who is not waiting has nothing to say.</summary>
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
            }

            if (noticeRoot != null)
            {
                noticeRoot.SetActive(true);
                return;
            }

            if (noticeText != null)
            {
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
            float p = Mathf.Clamp01(remaining01);

            if (patienceFill != null)
            {
                Vector2 size = patienceFill.sizeDelta;
                size.x = patienceWidth * p;
                patienceFill.sizeDelta = size;
            }

            if (patienceShape != null)
            {
                patienceShape.Fill(p >= Lokanta.PatienceWarn
                    ? Lokanta.Green
                    : p >= Lokanta.PatienceBad ? Lokanta.Must : Lokanta.Red);
            }

            if (moodFace != null)
            {
                Sprite face = p >= Lokanta.PatienceWarn
                    ? moodHappy
                    : p >= Lokanta.PatienceBad ? moodMeh : moodSad;
                if (face != null)
                {
                    moodFace.sprite = face;
                }
            }

            // Legacy fallback: no bar and no face, so the order text itself carries the warning.
            if (patienceFill == null && patienceShape == null && moodFace == null && text != null)
            {
                text.color = p >= Lokanta.PatienceWarn
                    ? Lokanta.Ink
                    : p >= Lokanta.PatienceBad ? Lokanta.Must2 : Lokanta.Red2;
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
            HideNotice();

            if (noticeRoot == null && noticeText == null && text != null)
            {
                text.text = Format(_recipe);
            }
        }

        private void HideNotice()
        {
            if (noticeRoot != null)
            {
                noticeRoot.SetActive(false);
            }

            if (noticeText != null && noticeRoot == null)
            {
                noticeText.gameObject.SetActive(false);
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

        // -------------------------------------------------------------------- rows

        /// <summary>Fills the list bottom→top, the same way the recipe reads and the burger is stacked.
        /// Returns the used row count, or <c>-1</c> when no row slots are bound.</summary>
        private int BuildRows(string recipe)
        {
            if (listRows == null || listRows.Length == 0)
            {
                return -1;
            }

            int used = 0;
            if (!string.IsNullOrEmpty(recipe))
            {
                string[] parts = recipe.Split(',');
                for (int i = 0; i < parts.Length && used < listRows.Length; i++)
                {
                    string kind = parts[i].Trim();
                    if (kind.Length == 0)
                    {
                        continue;
                    }

                    SlabStyle style = Style(kind);
                    if (listDots != null && used < listDots.Length && listDots[used] != null)
                    {
                        listDots[used].Fill(style.A, style.B, UiGradientMode.Vertical);
                    }

                    if (listLabels != null && used < listLabels.Length && listLabels[used] != null)
                    {
                        listLabels[used].text = BurgerKinds.DisplayName(kind);
                    }

                    used++;
                }
            }

            // Centred block: the mockup's list sits in the middle of the body column.
            float total = used > 0 ? used * rowPitch - (rowPitch - RowHeight) : 0f;
            for (int i = 0; i < listRows.Length; i++)
            {
                RectTransform row = listRows[i];
                if (row == null)
                {
                    continue;
                }

                bool on = i < used;
                if (row.gameObject.activeSelf != on)
                {
                    row.gameObject.SetActive(on);
                }

                if (on)
                {
                    Vector2 pos = row.anchoredPosition;
                    pos.y = -total * 0.5f + i * rowPitch + RowHeight * 0.5f;
                    row.anchoredPosition = pos;
                }
            }

            return used;
        }

        // ------------------------------------------------------------------- stack

        private void BuildStack(string recipe)
        {
            if (slabs == null || slabs.Length == 0)
            {
                return;
            }

            // Two passes: the stack is centred vertically, so the total height has to be known first.
            int used = 0;
            float total = 0f;
            if (!string.IsNullOrEmpty(recipe))
            {
                string[] parts = recipe.Split(',');
                for (int i = 0; i < parts.Length && used < slabs.Length; i++)
                {
                    string kind = parts[i].Trim();
                    if (kind.Length == 0)
                    {
                        continue;
                    }

                    SlabStyle style = Style(kind);
                    Apply(used, style);
                    total += style.Height;
                    used++;
                }
            }

            if (used > 1)
            {
                total += slabGap * (used - 1);
            }

            float y = -total * 0.5f;
            for (int i = 0; i < slabs.Length; i++)
            {
                UiShape slab = slabs[i];
                if (slab == null)
                {
                    continue;
                }

                bool on = i < used;
                if (slab.gameObject.activeSelf != on)
                {
                    slab.gameObject.SetActive(on);
                }

                if (!on)
                {
                    continue;
                }

                float h = slab.rectTransform.sizeDelta.y;
                slab.rectTransform.anchoredPosition = new Vector2(0f, y + h * 0.5f);
                y += h + slabGap;
            }
        }

        private void Apply(int index, SlabStyle style)
        {
            UiShape slab = slabs[index];
            if (slab == null)
            {
                return;
            }

            slab.rectTransform.sizeDelta = new Vector2(style.Width, style.Height);
            slab.Radius(style.RadiusTop, style.RadiusTop, style.RadiusBottom, style.RadiusBottom);
            slab.Fill(style.A, style.B, UiGradientMode.Vertical);

            if (slabStripes != null && index < slabStripes.Length && slabStripes[index] != null)
            {
                UiStripes stripes = slabStripes[index];
                bool on = style.Extra == SlabExtra.Stripes;
                if (stripes.gameObject.activeSelf != on)
                {
                    stripes.gameObject.SetActive(on);
                }
            }

            if (slabSesame != null && index < slabSesame.Length && slabSesame[index] != null)
            {
                bool on = style.Extra == SlabExtra.Sesame;
                if (slabSesame[index].activeSelf != on)
                {
                    slabSesame[index].SetActive(on);
                }
            }
        }

        // ------------------------------------------------------------- slab styles

        private const float RowHeight = 22f;

        private enum SlabExtra
        {
            None,
            Sesame,
            Stripes
        }

        /// <summary>One ingredient's slab geometry + gradient.
        /// <para>⚠️ Sizes are the mockup's (<c>asci.css</c> <c>.bing.*</c>) and the colours come from
        /// <see cref="Lokanta"/>: a slab measured by eye at a call site drifts from the picture the
        /// child learns the burger from.</para></summary>
        private readonly struct SlabStyle
        {
            public readonly float Width;
            public readonly float Height;
            public readonly float RadiusTop;
            public readonly float RadiusBottom;
            public readonly Color A;
            public readonly Color B;
            public readonly SlabExtra Extra;

            public SlabStyle(float width, float height, float radiusTop, float radiusBottom,
                Color a, Color b, SlabExtra extra = SlabExtra.None)
            {
                Width = width;
                Height = height;
                RadiusTop = radiusTop;
                RadiusBottom = radiusBottom;
                A = a;
                B = b;
                Extra = extra;
            }
        }

        private static SlabStyle Style(string kind)
        {
            switch (kind)
            {
                case BurgerKinds.BunBottom:
                    return new SlabStyle(140f, 26f, 5f, 15f, Lokanta.BunBottomA, Lokanta.BunBottomB);
                case BurgerKinds.BunTop:
                    return new SlabStyle(140f, 36f, 44f, 7f, Lokanta.BunTopA, Lokanta.BunTopB,
                        SlabExtra.Sesame);
                case BurgerKinds.Patty:
                    return new SlabStyle(150f, 20f, 5f, 5f, Lokanta.PattyA, Lokanta.PattyB);
                case BurgerKinds.Cheese:
                    return new SlabStyle(156f, 11f, 5f, 5f, Lokanta.CheeseA, Lokanta.CheeseB);
                case BurgerKinds.Lettuce:
                    return new SlabStyle(158f, 14f, 9f, 9f, Lokanta.LettuceA, Lokanta.LettuceB);
                case BurgerKinds.Tomato:
                    return new SlabStyle(140f, 12f, 5f, 5f, Lokanta.TomatoA, Lokanta.TomatoB);
                case BurgerKinds.Onion:
                    return new SlabStyle(140f, 10f, 5f, 5f, Lokanta.OnionA, Lokanta.OnionB);
                case BurgerKinds.Pickle:
                    return new SlabStyle(118f, 10f, 5f, 5f, Lokanta.PickleA, Lokanta.PickleB);
                case BurgerKinds.Bacon:
                    return new SlabStyle(152f, 12f, 5f, 5f, Lokanta.BaconStripe, Lokanta.BaconStripe,
                        SlabExtra.Stripes);
                case BurgerKinds.Sauce:
                    return new SlabStyle(130f, 8f, 5f, 5f, Lokanta.SauceA, Lokanta.SauceB);
                default:
                    // Unknown kind still gets a slab: the vocabulary can grow server-side and a missing
                    // row would read as a shorter order.
                    return new SlabStyle(140f, 14f, 5f, 5f, Lokanta.Cream2, Lokanta.Must2);
            }
        }

        // -------------------------------------------------------------------- text

        /// <summary>Legacy single-block caption, used only when no row slots are bound.</summary>
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
