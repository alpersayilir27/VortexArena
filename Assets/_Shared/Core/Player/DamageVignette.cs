using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Can kaybının görseli: HMD'ye asılı kırmızı bir vinyet. Can düştükçe nabız atar, can azken
    /// sabit bir çerçeve olarak kalır.
    ///
    /// <para>⚠️ <b>Bu katman <see cref="ScreenFade"/>'e kaynak olarak EKLENEMEZ ve eklenmemelidir.</b>
    /// Hakem "en yüksek alfa kazanır" diyor: engel karartması 1.0'dayken 0.5'lik bir kırmızı her
    /// zaman kaybeder ve oyuncu <b>kapkaranlık bir ekranda canının gittiğini hiç göremez</b>. Bu
    /// yüzden vinyetin kendi renderer'ı vardır ve karartma quad'ının <b>ÜSTÜNE</b> çizilir
    /// (materyali <c>Overlay</c> kuyruğunda ve <c>ZTest Always</c>).</para>
    ///
    /// <para><b>Engele özel değildir:</b> mermiyle gelen hasar da aynı efekti verir. Engel canını
    /// sunucu ~4 Hz bildirdiği için orada ritmik bir nabız görünür — "duruyorum ve eriyorum"un
    /// görsel karşılığı budur.</para>
    ///
    /// <para><b>Neden abone değil örnekleme:</b> can bir <b>durum</b>dur ve bu bileşen bir
    /// <b>sunum</b> katmanıdır; kare başına farkı okumak, kalıcı tekilin doğuş sırasına bağlı bir
    /// abonelik ömrü yönetmekten hem kısa hem de kaçırma riski olmayan yoldur (can yalnız ağ
    /// mesajıyla değişir, kare başına en fazla bir kez).</para>
    ///
    /// <para>Sahibi <c>VA_CameraRig</c> prefabıdır (CenterEyeAnchor altında, karartma quad'ının
    /// önünde). Admin gözlemcide rig kökü kapatıldığı için hiç çalışmaz.</para>
    /// </summary>
    [DefaultExecutionOrder(30200)]
    public class DamageVignette : MonoBehaviour
    {
        [Tooltip("Vinyet quad'ının renderer'ı (bu objenin kendi MeshRenderer'ı).")]
        [SerializeField] private Renderer vignetteRenderer;

        /// <summary>Tek pakette bu kadar hasar tam nabız demektir (HP).</summary>
        private const float PulseFullDamage = 25f;

        /// <summary>Nabzın sönüm hızı (birim/sn).</summary>
        private const float PulseDecayPerSecond = 2f;

        /// <summary>Nabzın tavanı — ekranı tümden kırmızıya boyamaz, oyuncu görmeye devam etmeli.</summary>
        private const float MaxPulseAlpha = 0.55f;

        /// <summary>Bu canın altında sabit vinyet belirir (HP).</summary>
        private const float LowHpThreshold = 40f;

        /// <summary>Sabit (can azlığı) vinyetinin tavanı.</summary>
        private const float MaxLowHpAlpha = 0.35f;

        private static readonly Color VignetteColor = new Color(0.75f, 0.03f, 0.03f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _propertyBlock;
        private float _pulse;
        private float _lastHp = ArenaProtocol.PLAYER_MAX_HP;

        private void Awake()
        {
            if (vignetteRenderer == null)
            {
                vignetteRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            _lastHp = ArenaCombat.LocalHp;
            Draw(0f);
        }

        private void OnDisable()
        {
            _pulse = 0f;
            Draw(0f);
        }

        private void LateUpdate()
        {
            float hp = ArenaCombat.LocalHp;

            // ⚠️ Yalnız DÜŞÜŞ nabız üretir: canlanma (0 → 100) ve iyileşme sessizdir.
            float drop = _lastHp - hp;
            _lastHp = hp;
            if (drop > 0f)
            {
                _pulse = Mathf.Clamp01(_pulse + drop / PulseFullDamage);
            }

            _pulse = Mathf.Max(0f, _pulse - PulseDecayPerSecond * Time.unscaledDeltaTime);

            if (!ArenaCombat.IsAlive)
            {
                // Ölüm ekranı kendi sunumunu yapıyor; üstüne kırmızı bir çerçeve koymak bilgi vermez.
                _pulse = 0f;
                Draw(0f);
                return;
            }

            float lowHp = hp < LowHpThreshold
                ? Mathf.InverseLerp(LowHpThreshold, 0f, hp) * MaxLowHpAlpha
                : 0f;

            Draw(Mathf.Max(_pulse * MaxPulseAlpha, lowHp));
        }

        private void Draw(float alpha)
        {
            if (vignetteRenderer == null)
            {
                return;
            }

            bool visible = alpha > 0.001f;
            vignetteRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            vignetteRenderer.GetPropertyBlock(_propertyBlock);
            Color color = VignetteColor;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);
            vignetteRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
