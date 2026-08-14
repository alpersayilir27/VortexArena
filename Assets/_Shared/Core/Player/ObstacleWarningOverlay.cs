using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Engelin içindeyken karartmanın üstünde beliren, nabız atan uyarı yazısı: oyuncu ekranın
    /// neden karardığını ve ne yapması gerektiğini bilmeli.
    ///
    /// <para><b>Ölçüm yapmaz</b> — <see cref="ObstacleViolationProbe"/>'un statik durumunu okur.
    /// Alfası probe'un karartmasına (<see cref="ObstacleViolationProbe.FadeAlpha"/>) bağlıdır:
    /// yazı ekran kararırken gelir, kararmadan önce belirmez.</para>
    ///
    /// <para>Karartma quad'ından <b>daha yakında</b> durur (prefabda z ≈ 0.42 · quad 0.5), bu yüzden
    /// saydam sıralamada onun üstüne çizilir — ayrı bir kuyruk/sorting ayarı gerekmez.
    /// (<see cref="DamageVignette"/> aynı sorunu farklı çözer: o mesafeye değil <c>Overlay</c>
    /// kuyruğuna güvenir, çünkü onun her şeyin üstünde olması şart.)</para>
    ///
    /// <para>Sahibi <c>VA_CameraRig</c> prefabıdır. Admin gözlemcide rig kökü kapatıldığı için hiç
    /// çalışmaz.</para>
    /// </summary>
    [DefaultExecutionOrder(30200)]
    public class ObstacleWarningOverlay : MonoBehaviour
    {
        [Tooltip("Uyarı yazısı (bu objenin kendi TextMesh'i).")]
        [SerializeField] private TextMesh warningText;

        /// <summary>Nabız frekansı (Hz) — okunabilir kalsın diye yavaş.</summary>
        private const float PulseHz = 1.6f;

        /// <summary>Nabzın alt ucu: yazı hiç kaybolmaz, yalnız soluklaşır.</summary>
        private const float MinPulseAlpha = 0.55f;

        /// <summary>Ölçek nefesinin genliği (oran).</summary>
        private const float ScaleBreathe = 0.04f;

        private Renderer _renderer;
        private Vector3 _baseScale;

        private void Awake()
        {
            if (warningText == null)
            {
                warningText = GetComponent<TextMesh>();
            }

            _renderer = warningText != null ? warningText.GetComponent<Renderer>() : null;
            _baseScale = transform.localScale;
            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (warningText == null)
            {
                return;
            }

            // ⚠️ Kapıda faz da canlılık da YOKTUR: yazı karartmanın AÇIKLAMASIDIR, karartma ise
            // her durumda çalışıyor (gerekçe ObstacleViolationProbe.ReportPresentation). Ölüyken
            // susturmak, kapkaranlık bir ekranı sebepsiz bırakırdı — üstelik engelin içinde
            // canlanma yok (§10.9), yani oyuncunun okuması gereken tek yönerge tam da budur.
            bool show = ObstacleViolationProbe.IsViolating;
            if (!show)
            {
                SetVisible(false);
                return;
            }

            // Karartmanın kendisi 0.2 sn'de geliyor; yazı onunla birlikte belirsin.
            float fade = Mathf.Clamp01(ObstacleViolationProbe.FadeAlpha);
            float pulse = Mathf.Lerp(MinPulseAlpha, 1f,
                0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * 2f * Mathf.PI));

            Color color = warningText.color;
            color.a = fade * pulse;
            warningText.color = color;

            transform.localScale = _baseScale * (1f + ScaleBreathe * (pulse - MinPulseAlpha));

            SetVisible(color.a > 0.01f);
        }

        private void SetVisible(bool on)
        {
            if (_renderer != null)
            {
                _renderer.enabled = on;
            }
        }
    }
}
