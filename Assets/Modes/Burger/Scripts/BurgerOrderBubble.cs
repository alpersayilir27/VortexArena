using System.Text;
using TMPro;
using UnityEngine;

namespace VortexArena.Modes.Burger
{
    /// <summary>The order bubble above a customer: turns the wire recipe
    /// (<c>bun_bottom,patty,cheese,bun_top</c>) into readable lines and faces the player.</summary>
    [DisallowMultipleComponent]
    public sealed class BurgerOrderBubble : MonoBehaviour
    {
        [Tooltip("Sipariş satırlarının yazıldığı metin.")]
        [SerializeField] private TMP_Text text;

        [Tooltip("Balonun döneceği baş. Boşsa ana kamera kullanılır.")]
        [SerializeField] private Transform head;

        [Tooltip("Balonun kökü (gösterilip gizlenen obje). Boşsa bu obje kullanılır.")]
        [SerializeField] private GameObject root;

        private readonly StringBuilder _builder = new StringBuilder(64);

        private void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }
        }

        public void Show(string recipe)
        {
            if (text != null)
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
            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private void LateUpdate()
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

        /// <summary>⚠️ An unknown key is written as-is rather than dropped: the recipe vocabulary can gain
        /// ingredients server-side, and a silently missing line would read as a wrong order.</summary>
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

                _builder.Append(DisplayName(part));
            }

            return _builder.ToString();
        }

        private static string DisplayName(string kind)
        {
            switch (kind)
            {
                case BurgerKinds.BunBottom: return "Alt ekmek";
                case BurgerKinds.Patty: return "Köfte";
                case BurgerKinds.Cheese: return "Peynir";
                case BurgerKinds.Bacon: return "Pastırma";
                case BurgerKinds.Lettuce: return "Marul";
                case BurgerKinds.Onion: return "Soğan";
                case BurgerKinds.Pickle: return "Turşu";
                case BurgerKinds.Tomato: return "Domates";
                case BurgerKinds.Sauce: return "Sos";
                case BurgerKinds.BunTop: return "Üst ekmek";
                default: return kind;
            }
        }
    }
}
