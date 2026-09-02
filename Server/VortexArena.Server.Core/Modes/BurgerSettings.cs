#nullable enable

namespace VortexArena.Server.Core.Modes;

/// <summary>Hamburgerci balance numbers (<c>server.json → burger</c>).</summary>
/// <remarks>Public FIELDS — read via JsonUtil (IncludeFields); names match server.json exactly.
/// <para>NOT on the wire: these are mode-internal rules, so changing one bumps no protocol version.</para>
/// <para>The <c>Start</c>/<c>End</c> pairs ramp linearly over the shift — the shift gets busier as it
/// goes.</para></remarks>
public sealed class BurgerSettings
{
    /// <summary>Seconds between customers at the START of the shift.</summary>
    public float customerIntervalStart = 25f;

    /// <summary>Seconds between customers at the END of the shift.</summary>
    public float customerIntervalEnd = 12f;

    /// <summary>Patience of a customer born at the START of the shift (seconds).</summary>
    public float patienceStart = 150f;

    /// <summary>Patience of a customer born at the END of the shift (seconds).</summary>
    public float patienceEnd = 90f;

    /// <summary>Seconds on the grill until the patty is cooked.</summary>
    public float cookSeconds = 20f;

    /// <summary>Seconds on the grill until the patty burns; must be above <see cref="cookSeconds"/>,
    /// otherwise nothing would ever be servable.</summary>
    public float burnSeconds = 40f;

    /// <summary>Points for one correct serve.</summary>
    public int servePoints = 10;

    /// <summary>Returns a copy with invalid values pulled back to the defaults, each with one console
    /// line — a silent fix would leave the operator with a shift that plays nothing like the file.</summary>
    public BurgerSettings Sanitized()
    {
        var defaults = new BurgerSettings();
        var result = new BurgerSettings
        {
            customerIntervalStart = Positive(customerIntervalStart, defaults.customerIntervalStart, "customerIntervalStart"),
            customerIntervalEnd = Positive(customerIntervalEnd, defaults.customerIntervalEnd, "customerIntervalEnd"),
            patienceStart = Positive(patienceStart, defaults.patienceStart, "patienceStart"),
            patienceEnd = Positive(patienceEnd, defaults.patienceEnd, "patienceEnd"),
            cookSeconds = Positive(cookSeconds, defaults.cookSeconds, "cookSeconds"),
            burnSeconds = Positive(burnSeconds, defaults.burnSeconds, "burnSeconds"),
            servePoints = servePoints
        };

        if (result.servePoints < 0)
        {
            Console.WriteLine($"[burger] server.json → burger.servePoints geçersiz ({servePoints}) — " +
                              $"varsayılana çekildi ({defaults.servePoints}).");
            result.servePoints = defaults.servePoints;
        }

        // Both go back to the defaults together: keeping one edited value next to a default would
        // produce a pair the operator never wrote.
        if (result.burnSeconds <= result.cookSeconds)
        {
            Console.WriteLine($"[burger] server.json → burger.burnSeconds ({result.burnSeconds}) " +
                              $"cookSeconds'ten ({result.cookSeconds}) büyük olmalı — ikisi de " +
                              $"varsayılana çekildi ({defaults.cookSeconds}/{defaults.burnSeconds}).");
            result.cookSeconds = defaults.cookSeconds;
            result.burnSeconds = defaults.burnSeconds;
        }

        return result;
    }

    private static float Positive(float value, float fallback, string field)
    {
        if (value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)) return value;
        Console.WriteLine($"[burger] server.json → burger.{field} geçersiz ({value}) — " +
                          $"varsayılana çekildi ({fallback}).");
        return fallback;
    }
}
