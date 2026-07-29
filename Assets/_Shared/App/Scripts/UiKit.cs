using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Fabrika metotları ögelerin adını taşıyor (UiKit.Image / UiKit.Button). Aynı dosyada tip olarak
// da geçtikleri için takma ad şart: `Image x = Image(...)` C#'ta CS0119 verir (basit ad araması
// üye grubunu tipten ÖNCE bulur). Takma adlar yalnız derleme zamanı — dışa açık imza değişmez.
using UiImage = UnityEngine.UI.Image;
using UiButton = UnityEngine.UI.Button;

namespace VortexArena.App
{
    /// <summary>
    /// Prosedürel arayüz kiti: palet + yuvarlatılmış sprite önbelleği + öge fabrikaları.
    /// <para>
    /// <b>Neden prosedürel:</b> bu projedeki masaüstü/VR overlay'leri (bağlantı hata ekranı,
    /// admin gözlemci HUD'ı) HER sahnede kendini önyüklüyor — prefab'a bağlanırlarsa her yeni
    /// arena sahnesine elle bileşen/prefab eklemek gerekir ve bir gün unutulur. Kod tek kaynak
    /// olduğu için yeni arena eklerken hiçbir ek adım doğmaz.
    /// </para>
    /// <para>
    /// <b>Yerleşim kuralı:</b> Layout Group + ContentSizeFitter KULLANILMAZ. Her öge
    /// <see cref="Block"/>/<see cref="Corner"/>/<see cref="Stretch"/> ile sabit anchor'a oturur:
    /// öngörülebilir, yeniden hesap yok, farklı çözünürlükte kayma yok (ölçek CanvasScaler'da).
    /// </para>
    /// <para>
    /// <b>Font ATANMAZ:</b> TMP Settings'teki varsayılan font kullanılır (Türkçe glifler orada).
    /// TMP fontunda garantisi olmayan sembol (⚠, →, •) yazılmaz — eksik glif □ çizilir.
    /// </para>
    /// </summary>
    public static class UiKit
    {
        // ------------------------------------------------------------------ palet

        public static readonly Color Scrim = Hex(0x12151C, 0xDD);
        public static readonly Color Card = Hex(0x1B2029, 0xFF);

        /// <summary>Sahne üstü panel/kart: arkadaki canlı görüntü GÖRÜNÜR kalsın (alfa ≈ 0.88).</summary>
        public static readonly Color CardTranslucent = Hex(0x1B2029, 0xE0);

        public static readonly Color Border = Hex(0x2E3542, 0xFF);
        public static readonly Color Accent = Hex(0xF2A33C, 0xFF);
        public static readonly Color Title = Hex(0xF5F7FA, 0xFF);
        public static readonly Color Muted = Hex(0x9AA4B2, 0xFF);
        public static readonly Color Faint = Hex(0x6E7A8A, 0xFF);
        public static readonly Color OnAccent = Hex(0x12151C, 0xFF);
        public static readonly Color Good = Hex(0x39D98A, 0xFF);
        public static readonly Color Bad = Hex(0xE5484D, 0xFF);

        /// <summary>Takım renkleri <see cref="Core.Player.RemoteAvatar"/> ile BİREBİR aynı olmalı —
        /// aynı oyuncu HUD'da ve sahnede farklı renkte görünürse operatör yanılır.</summary>
        public static readonly Color TeamRed = new Color(0.85f, 0.20f, 0.20f);
        public static readonly Color TeamBlue = new Color(0.20f, 0.40f, 0.90f);
        public static readonly Color TeamNeutral = new Color(0.6f, 0.6f, 0.6f);

        /// <summary>Panel/kart varsayılan köşe yarıçapı (px).</summary>
        public const float PanelRadius = 12f;

        /// <summary>Kart kenar kalınlığı (px) — kenar rengi zemin, içine dolgu kaçar.</summary>
        public const float BorderWidth = 2f;

        public static Color Hex(int rgb, int alpha)
        {
            return new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF), (byte)alpha);
        }

        /// <summary>Takım anahtarını ("red"/"blue"/diğer) renge çevirir.</summary>
        public static Color TeamColor(string team)
        {
            return team == "red" ? TeamRed : team == "blue" ? TeamBlue : TeamNeutral;
        }

        /// <summary>Rengi karartır (ölü oyuncu görünümü; alfa korunur).</summary>
        public static Color Dim(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ------------------------------------------------------------- canvas/kök

        /// <summary>
        /// Ekran-uzayı overlay canvas'ı kurar (1920x1080 referans, yarı yarıya eşleme).
        /// <paramref name="sortingOrder"/> ekranlar arası öncelik: admin HUD 4000,
        /// bağlantı hata ekranı 5000 (hata her zaman üstte kalmalı).
        /// </summary>
        public static Canvas ScreenCanvas(GameObject root, int sortingOrder)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>RectTransform'lu boş düğüm (gruplama/konumlandırma için).</summary>
        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        // ---------------------------------------------------------------- ögeler

        public static UiImage Image(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<UiImage>();
            image.color = color;
            image.raycastTarget = false; // tıklanabilir ögeler bunu kendisi açar

            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = UiImage.Type.Sliced; // 9-slice: köşeler ölçeklenmez
            }

            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, float fontSize,
            Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.richText = false; // dışarıdan gelen ad "<b>" içerirse biçim bozulmasın
            tmp.text = "";

            return tmp;
        }

        /// <summary>
        /// Kenarlı yuvarlatılmış kart: kenar rengindeki zemin + içine kaçmış dolgu.
        /// Döndürdüğü <see cref="Image"/> DOLGUdur — çocuklar ona eklenir.
        /// </summary>
        public static UiImage Panel(Transform parent, string name, Color fill, Color border,
            float radius = PanelRadius)
        {
            UiImage outer = Image(parent, name, RoundedSprite(radius), border);
            UiImage inner = Image(outer.rectTransform, "Fill", RoundedSprite(radius), fill);
            Stretch(inner.rectTransform, BorderWidth);
            return inner;
        }

        /// <summary>Kenarsız düz zemin (ayırıcı, şerit, bar zemini).</summary>
        public static UiImage Solid(Transform parent, string name, Color color, bool rounded = false)
        {
            return Image(parent, name, rounded ? RoundedSprite(4f) : null, color);
        }

        /// <summary>
        /// Düğme: yuvarlatılmış zemin + ortalanmış etiket. `background.color` taban renktir,
        /// Button.colors yalnız tint uygular (normal = beyaz).
        /// </summary>
        public static UiButton Button(Transform parent, string name, string label, float fontSize,
            Color background, Color foreground, UnityAction onClick, out TextMeshProUGUI labelText)
        {
            UiImage image = Image(parent, name, RoundedSprite(8f), background);
            image.raycastTarget = true;

            labelText = Text(image.rectTransform, "Label", fontSize, foreground, FontStyles.Bold,
                TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform, 2f);
            labelText.text = label;
            // NoWrap + Ellipsis: uzun etiket düğmeyi bozmaz. (`enableWordWrapping` obsolete.)
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            // Emniyet ağı: sığmayan etiket kırpılmak (KIRMIZ…) yerine ÖNCE küçülür. Yalnız aşağı
            // ölçekler (max = istenen punto), yani sığan etiket hiç etkilenmez ve tasarım bozulmaz.
            // Neden var: düğme genişlikleri sabit anchor'la veriliyor (Layout Group yok) ve etiket
            // uzunluğu dile/duruma göre değişiyor — "kaç px sığar" hesabını her çağrı yerinde elle
            // yapmak hataya açık. Sınır %70: altına inince okunaksızlaşır, orada ellipsis yeğdir.
            labelText.enableAutoSizing = true;
            labelText.fontSizeMax = fontSize;
            labelText.fontSizeMin = Mathf.Max(8f, fontSize * 0.7f);

            var button = image.gameObject.AddComponent<UiButton>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.42f, 0.45f, 0.52f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        /// <summary>
        /// Tek satırlık metin girişi: koyu yuvarlatılmış zemin + <see cref="TMP_InputField"/>.
        /// <para>
        /// ⚠️ <b>Kitin TEK klavye alanıdır ve yalnız MASAÜSTÜ (admin) arayüzünde kullanılır.</b>
        /// VR tarafında sistem klavyesi gerekir; oradaki metin girişi ayrı bir çözümdür.
        /// </para>
        /// <para>
        /// Bileşen <b>obje pasifken</b> eklenir: <c>TMP_InputField.OnEnable</c> etkinleşme anında
        /// <c>textComponent</c>/<c>textViewport</c> arar, aktif bir objeye eklenirse yarım kurulmuş
        /// hâlde uyanır ve caret'i bozuk kalır. Sıra: pasifleştir → ekle → kur → etkinleştir.
        /// </para>
        /// </summary>
        public static TMP_InputField Input(Transform parent, string name, string placeholder,
            float fontSize, int characterLimit, UnityAction<string> onEndEdit)
        {
            UiImage background = Image(parent, name, RoundedSprite(8f), Hex(0x12151C, 0xFF));
            background.raycastTarget = true;

            RectTransform viewport = Node(background.rectTransform, "TextArea");
            Stretch(viewport, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = Text(viewport, "Text", fontSize, Title, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            TextMeshProUGUI hint = Text(viewport, "Placeholder", fontSize, Faint, FontStyles.Italic,
                TextAlignmentOptions.MidlineLeft);
            Stretch(hint.rectTransform);
            hint.text = placeholder;

            GameObject host = background.gameObject;
            host.SetActive(false);

            var field = host.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = characterLimit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.restoreOriginalTextOnEscape = true;
            field.text = "";

            if (onEndEdit != null)
            {
                field.onEndEdit.AddListener(onEndEdit);
            }

            host.SetActive(true);
            return field;
        }

        /// <summary>
        /// Yatay dolum barı (HP/ilerleme): zemin + soldan büyüyen dolgu.
        /// Dolgunun genişliği <c>fill.rectTransform.anchorMax.x</c> ile 0..1 arası ayarlanır.
        /// </summary>
        public static UiImage Bar(Transform parent, string name, Color background, Color fill)
        {
            UiImage back = Image(parent, name, RoundedSprite(4f), background);

            UiImage front = Image(back.rectTransform, "Fill", RoundedSprite(4f), fill);
            RectTransform rect = front.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return front;
        }

        /// <summary>Bar dolgusunu 0..1 aralığında ayarlar.</summary>
        public static void SetBarFill(UiImage fill, float normalized)
        {
            if (fill == null)
            {
                return;
            }

            RectTransform rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        }

        // -------------------------------------------------------------- yerleşim

        /// <summary>Ebeveyni tamamen kaplar (isteğe bağlı iç boşlukla).</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Üstten ölçülen, yatayda gerilmiş blok (satır yerleşiminin temeli).</summary>
        public static void Block(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(top + height));
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Bir köşeye/kenara sabitlenmiş kutu. <paramref name="anchor"/> aynı zamanda pivot olur,
        /// <paramref name="offset"/> o köşeden içeri doğru (işaret ekseni anchor'a göre) kaydırır.
        /// Ör. sağ-alt için anchor (1,0), offset (-24, 24).
        /// </summary>
        public static void Corner(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>Ebeveyn içinde ortalanmış sabit boyutlu kutu.</summary>
        public static void Center(RectTransform rect, Vector2 size, Vector2 offset = default)
        {
            Corner(rect, new Vector2(0.5f, 0.5f), offset, size);
        }

        // --------------------------------------------------------- EventSystem

        private static EventSystem _ownEventSystem;

        /// <summary>
        /// Tıklanabilir masaüstü arayüzü için EventSystem garantisi.
        /// <para>
        /// ⚠ <b>Tuzak:</b> arena sahnelerinde EventSystem HİÇ YOK (yalnız Lobby'de var) — admin
        /// gözlemci arena sahnesine girdiğinde HUD düğmeleri sessizce ölürdü. Bu yüzden kalıcı
        /// (DontDestroyOnLoad) bir tane kurulur.
        /// </para>
        /// <para>
        /// ⚠ İkinci tuzak: iki EventSystem aynı anda etkinse Unity "There are 2 event systems"
        /// uyarısı basar ve girdi ikisi arasında bölünür. Bu yüzden <see cref="TakeOverEventSystem"/>
        /// sahnedekileri kapatır.
        /// </para>
        /// Proje "Input System Package (New)" ile derlendiği için modül
        /// <see cref="InputSystemUIInputModule"/>'dür; eski <c>StandaloneInputModule</c> runtime'da
        /// <c>UnityEngine.Input</c>'a dokunup patlar.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            if (_ownEventSystem != null)
            {
                return _ownEventSystem;
            }

            // EventSystem.current sahne objesi henüz OnEnable görmediyse null olabilir — aramayla teyit.
            EventSystem existing = EventSystem.current != null
                ? EventSystem.current
                : Object.FindFirstObjectByType<EventSystem>();
            if (existing != null && existing.isActiveAndEnabled)
            {
                return existing; // sahnenin kendi EventSystem'i iş görüyor
            }

            return CreateOwnEventSystem();
        }

        /// <summary>
        /// Kalıcı EventSystem'i tek yetkili yapar: sahneye ait olanları kapatır.
        /// Yalnız admin gözlemci çağırır — Lobby'deki EventSystem VR işaretçisi için oradadır ve
        /// masaüstü admin'de gereksizdir.
        /// </summary>
        public static void TakeOverEventSystem()
        {
            if (_ownEventSystem == null)
            {
                CreateOwnEventSystem();
            }

            EventSystem[] all = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != _ownEventSystem)
                {
                    all[i].gameObject.SetActive(false);
                }
            }

            EventSystem.current = _ownEventSystem;
        }

        private static EventSystem CreateOwnEventSystem()
        {
            var go = new GameObject("[VortexEventSystem]");
            Object.DontDestroyOnLoad(go);
            _ownEventSystem = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            return _ownEventSystem;
        }

        // ------------------------------------------- prosedürel yuvarlak köşe

        // Yarıçap başına tek sprite (px cinsinden anahtar); doku 64x64 sabit.
        private static readonly Dictionary<int, Sprite> RoundedCache = new Dictionary<int, Sprite>();

        /// <summary>
        /// 64x64 beyaz, yuvarlatılmış köşeli, kenarları anti-aliased alfa maskesi.
        /// 9-slice `border` ile üretilir → <c>Image.Type.Sliced</c> her boyutta köşeyi bozmadan
        /// çizer. Renk <c>Image.color</c> ile verilir; sprite yarıçap başına önbelleklenir.
        /// </summary>
        public static Sprite RoundedSprite(float radius = PanelRadius)
        {
            const int size = 64;
            const float half = size * 0.5f;

            int key = Mathf.Clamp(Mathf.RoundToInt(radius), 1, (int)half - 1);
            if (RoundedCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            float r = key;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"VortexRoundedRect{key}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Yuvarlatılmış dikdörtgen işaretli mesafe alanı (içi negatif).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float distance = Mathf.Min(Mathf.Max(qx, qy), 0f) + outside - r;

                    // 1 px'lik yumuşak kenar (anti-aliasing).
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            texture.hideFlags = HideFlags.DontSave; // runtime üretimi — asset'e yazılmaz

            // 9-slice kenarı yarıçaptan 2 px büyük: köşe eğrisi dilime tam sığsın.
            float b = Mathf.Min(half - 1f, r + 2f);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(b, b, b, b));
            sprite.name = $"VortexRoundedRectSprite{key}";
            sprite.hideFlags = HideFlags.DontSave;

            RoundedCache[key] = sprite;
            return sprite;
        }

        // Kalınlık başına tek halka sprite'ı (yüzde cinsinden anahtar).
        private static readonly Dictionary<int, Sprite> RingCache = new Dictionary<int, Sprite>();

        /// <summary>
        /// İçi boş daire (annulus) alfa maskesi — admin gözlemcinin oyuncu halkaları.
        /// <paramref name="thickness"/> yarıçapa oranla çizgi kalınlığıdır (0.05..0.5).
        /// <para>
        /// Mesh + <c>Shader.Find</c> yerine UI sprite kullanılır: build'de kullanılmayan shader
        /// STRIP edilebilir ve <c>Shader.Find("Universal Render Pipeline/Unlit")</c> null dönerdi.
        /// UI/TMP shader'ları her zaman build'de olduğu için world-space canvas güvenli yoldur.
        /// </para>
        /// </summary>
        public static Sprite RingSprite(float thickness = 0.16f)
        {
            const int size = 128;
            const float half = size * 0.5f;

            int key = Mathf.Clamp(Mathf.RoundToInt(thickness * 100f), 5, 50);
            if (RingCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            float outer = half - 1f;
            float inner = outer * (1f - key / 100f);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"VortexRing{key}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // İki kenarda da 1 px yumuşak geçiş (anti-aliasing).
                    float a = Mathf.Clamp01(outer + 0.5f - distance) *
                              Mathf.Clamp01(distance - inner + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            texture.hideFlags = HideFlags.DontSave;

            // Halka ÖLÇEKLENİR (9-slice değil): border 0 → Image.Type.Simple ile kullanılır.
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = $"VortexRingSprite{key}";
            sprite.hideFlags = HideFlags.DontSave;

            RingCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Dünya-uzayı canvas kökü (admin halkaları/ad etiketleri). Rendering için kamera
        /// atamaya gerek yoktur; raycast yapılmadığı için <c>GraphicRaycaster</c> de eklenmez.
        /// </summary>
        public static Canvas WorldCanvas(Transform parent, string name, Vector2 size, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // RectTransform ÖNCE (Transform'u yükseltir): Canvas'ın ona ihtiyacı var.
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * scale;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            return canvas;
        }
    }
}
