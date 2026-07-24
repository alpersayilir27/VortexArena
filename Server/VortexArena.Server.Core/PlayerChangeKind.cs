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

    /// <summary>Bağlantı koptu veya 15 sn status gelmedi.</summary>
    Offline
}
