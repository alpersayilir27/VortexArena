using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Operatörün ekranında bir ihlalin görünen türü (§10.9).
    /// <para>
    /// ⚠️ Bu enum <b>serialize EDİLMEZ</b> — hiçbir sahnede/asset'te saklanmıyor, yalnız çalışma
    /// anında halka ve satır rengine çevriliyor. Bu yüzden "yeni değer sona eklenir" kuralı
    /// buraya işlemez; <c>None = 0</c> ise okunurluk içindir (varsayılan = ihlal yok).
    /// </para>
    /// </summary>
    public enum AdminViolationKind
    {
        None = 0,

        /// <summary>Kafa muhafazanın güvenli alanının dışında — <b>ceza üretmez</b>.</summary>
        OutOfBounds,

        /// <summary>Kafa bir iç engelin içinde — can eriten tek ihlal türü.</summary>
        Obstacle
    }

    /// <summary>
    /// İhlalin <b>görünümünün tek doğruluk kaynağı</b>: hangi durumun hangi renkte, hangi ritimde
    /// ve hangi yazıyla anlatıldığı yalnız burada tanımlıdır.
    /// <para>
    /// ⚠️ Aynı kuralı iki yerde yazmamak içindir: kuş bakışı halkası
    /// (<see cref="AdminPlayerMarkers"/>) ve yan paneldeki oyuncu satırı
    /// (<see cref="AdminPlayerRow"/>) aynı ihlali gösteriyor — renk/frekans ikisinde ayrı
    /// yazılsaydı biri değiştiğinde aynı oyuncu iki yerde farklı ciddiyette görünürdü.
    /// </para>
    /// </summary>
    public static class AdminViolations
    {
        /// <summary>Engel ihlalinde yanıp sönme frekansı (Hz) — can eriyor, ritim hızlı.</summary>
        private const float ObstacleBlinkHz = 3f;

        /// <summary>Alan dışında yanıp sönme frekansı (Hz). Bilinçli olarak daha yavaş: ciddiyet
        /// farkını renk tek başına taşımaz, ritim de taşır.</summary>
        private const float OutOfBoundsBlinkHz = 1.5f;

        /// <summary>Yanıp sönmenin "kısık" yarısındaki renk çarpanı — halka kaybolmasın, kararsın.
        /// Tamamen söndürmek operatörün "kaç kişi ihlalde" sorusunu iki kareden birinde
        /// cevapsız bırakırdı.</summary>
        private const float BlinkDim = 0.3f;

        private const string LabelObstacle = "DUVAR";
        private const string LabelOutOfBounds = "ALAN DIŞI";

        /// <summary>
        /// Oyuncunun ŞU ANKİ ihlal durumu — kaynak son snapshot'ın bayraklarıdır
        /// (<see cref="RemotePlayerRegistry"/>), yani durum bayatlayınca kendiliğinden söner.
        /// <para>
        /// ⚠️ <b>Öncelik engeldedir:</b> ikisi birden mümkündür (alanın dışındaki bir kolonun içi)
        /// ve gösterilecek olan can eritenidir — operatörün müdahale sırası ciddiyete göredir.
        /// </para>
        /// <para>Kayıt defteri (registry) yoksa <see cref="AdminViolationKind.None"/>: bilinmeyen
        /// bir durumu ihlal saymak operatöre var olmayan bir olay gösterirdi.</para>
        /// </summary>
        public static AdminViolationKind Of(int playerId)
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                return AdminViolationKind.None;
            }

            if (registry.IsInObstacle(playerId))
            {
                return AdminViolationKind.Obstacle;
            }

            return registry.IsOutOfBounds(playerId)
                ? AdminViolationKind.OutOfBounds
                : AdminViolationKind.None;
        }

        /// <summary>
        /// İhlalin o andaki yanıp sönen rengi. <see cref="AdminViolationKind.None"/> için
        /// <see cref="UiKit.Border"/> döner — çağıran zaten kendi normal rengini kullanmalıdır,
        /// bu yalnız yanlışlıkla çağrının görünür bir hata üretmemesi içindir.
        /// <para>
        /// ⚠️ <b>Faz oyuncu başına KAYDIRILMAZ</b> (<c>Time.unscaledTime</c> tek girdidir): tüm
        /// işaretçiler senkron yanıp söner, böylece operatör "kaç kişi ihlalde" sorusunu tek
        /// bakışta sayabilir. Faz kaydırılsaydı ekranda sürekli bir kısmı yanan bir halka
        /// kalabalığı olur ve sayım imkânsızlaşırdı.
        /// </para>
        /// </summary>
        public static Color Blink(AdminViolationKind kind)
        {
            switch (kind)
            {
                case AdminViolationKind.Obstacle:
                    return Pulse(UiKit.Bad, ObstacleBlinkHz);
                case AdminViolationKind.OutOfBounds:
                    // Repo tonunda "uyarı ama hata değil" Accent'tir (düşük pil, zemin sapması).
                    return Pulse(UiKit.Accent, OutOfBoundsBlinkHz);
                default:
                    return UiKit.Border;
            }
        }

        /// <summary>
        /// İhlalin kısa etiketi; ihlal yoksa boş dize.
        /// <para>⚠️ <b>Düz metin</b> — TMP varsayılan fontunda garantisi olmayan sembol
        /// kullanılmaz (eksik glif □ çizilir); kill feed'in "-&gt;" kararıyla aynı kural.</para>
        /// </summary>
        public static string Label(AdminViolationKind kind)
        {
            switch (kind)
            {
                case AdminViolationKind.Obstacle: return LabelObstacle;
                case AdminViolationKind.OutOfBounds: return LabelOutOfBounds;
                default: return "";
            }
        }

        /// <summary>
        /// Protokolden gelen <c>ArenaProtocol.VIOLATION_KIND_*</c> dizesinin etiketi (ihlal akışı
        /// satırları bunu kullanır).
        /// <para>⚠️ <b>Tanınmayan tür HAM gösterilir</b>, düşürülmez: telde string taşınıyor ve
        /// sunucu bir gün yeni bir tür ekleyebilir — satırı yutmak operatörden gerçekten olmuş bir
        /// olayı gizlerdi.</para>
        /// </summary>
        public static string Label(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return "";
            }

            if (kind == ArenaProtocol.VIOLATION_KIND_OBSTACLE)
            {
                return LabelObstacle;
            }

            return kind == ArenaProtocol.VIOLATION_KIND_OUT_OF_BOUNDS
                ? LabelOutOfBounds
                : kind;
        }

        /// <summary>Bir periyodun yarısı açık, yarısı kısık — <c>hz</c> için saniyede iki katı
        /// yarı periyot.</summary>
        private static Color Pulse(Color color, float hz)
        {
            bool on = Mathf.Repeat(Time.unscaledTime * hz, 1f) < 0.5f;
            return on ? color : UiKit.Dim(color, BlinkDim);
        }
    }
}
