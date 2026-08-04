#nullable enable
namespace VortexArena.Server.Core;

/// <summary>PlayerRegistry.Changed olayının nedeni.</summary>
public enum PlayerChangeKind
{
    /// <summary>İlk kez bağlandı (yeni playerId tahsis edildi).</summary>
    Added,

    /// <summary>Bilinen deviceId yeniden bağlandı (eski soket kapatıldı, playerId korunur).</summary>
    Reconnected,

    /// <summary>Roster verisi değişti (status, ad, ready, takım).</summary>
    Updated,

    /// <summary>Bağlantı koptu veya HEARTBEAT_TIMEOUT boyunca status gelmedi: cihaz
    /// RECONNECT_GRACE boyunca geri bekleniyor (§2).</summary>
    Reconnecting,

    /// <summary>Kayıt tümüyle silindi: admin kopunca (oturumluk kimlik, hayalet satır bırakmaz —
    /// Docs/ArenaNet-Protokol.md §2), atılan oyuncu (§5.4) ve RECONNECT_GRACE'i dolan ama maç
    /// katılımcısı OLMAYAN oyuncu.</summary>
    Removed,

    /// <summary>RECONNECT_GRACE doldu ve kayıt maç katılımcısı olduğu için silinmedi: oyuncu
    /// oyundan çıkarıldı, adı ve sayaçları maç bitene kadar tabloda durur (§10.2).
    /// <para>⚠️ Yeni değer enum'un SONUNA eklenir.</para></summary>
    Left
}
