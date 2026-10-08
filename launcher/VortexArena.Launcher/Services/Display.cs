using System.Globalization;

namespace VortexArena.Launcher.Services;

/// <summary>Protocol values and byte counts turned into the operator's language.</summary>
public static class Display
{
    /// <summary>Match phase (§5.3 values). An unknown phase is shown verbatim, never swallowed.</summary>
    public static string Phase(string? phase) => phase switch
    {
        null or "" => "—",
        "lobby" => "Lobi",
        "loading" => "Yükleniyor",
        "countdown" => "Geri sayım",
        "playing" => "Oyunda",
        "paused" => "Duraklatıldı",
        "finished" => "Bitti",
        _ => phase,
    };

    public static string Megabytes(long bytes)
        => bytes <= 0 ? "—" : (bytes / 1048576.0).ToString("N1", CultureInfo.CurrentCulture) + " MB";

    /// <summary>mm:ss, or h:mm:ss past an hour.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
