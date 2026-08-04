#nullable enable
namespace VortexArena.Server.Core;

/// <summary>Bir kaydın bağlantı durumu (§2). Telde <c>PlayerInfo.connection</c> stringi olarak
/// taşınır; "çevrimdışı" diye bir değer YOKTUR — kopan cihaz ya geri beklenir ya çıkarılır.
/// <para>⚠️ Unity tarafında serialize EDİLMEZ (tel formatı string), ama yeni değer yine de
/// <b>SONA</b> eklenir: sayısal indeksle karşılaştırma/loglama yapan her yer sessizce kayar.</para></summary>
public enum PlayerConnection
{
    /// <summary>Soket canlı; tüm maç kapıları açık.</summary>
    Connected,

    /// <summary>Soket düştü (kopma ya da HEARTBEAT_TIMEOUT); cihaz RECONNECT_GRACE boyunca geri
    /// bekleniyor. Kayıt durur ama maç kapılarına girmez.</summary>
    Reconnecting,

    /// <summary>Süre doldu, oyuncu oyundan çıkarıldı. Bu duruma yalnız <b>maç katılımcısı</b>
    /// kayıtlar girer (§10.2) — katılımcı olmayan kayıt bunun yerine tümden silinir.</summary>
    Left
}
