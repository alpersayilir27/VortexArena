#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>One accepted interaction of a kind (<c>kinds[].events[]</c>, §11): the name allowed on the
/// wire plus its gates.</summary>
/// <remarks>Public FIELDS — read via JsonUtil (IncludeFields); names match maps.json exactly.
/// Empty <c>policy</c>/<c>phaseGate</c> = old export → normalized at load (anyone / playing).</remarks>
public sealed class KindEventEntry
{
    public string name = "";

    /// <summary><see cref="ArenaProtocol.OBJECT_EVENT_POLICY_ANYONE"/> |
    /// <see cref="ArenaProtocol.OBJECT_EVENT_POLICY_OWNER"/>.</summary>
    public string policy = "";

    /// <summary><see cref="ArenaProtocol.OBJECT_PHASE_GATE_PLAYING"/> |
    /// <see cref="ArenaProtocol.OBJECT_PHASE_GATE_ANY"/>.</summary>
    public string phaseGate = "";
}

/// <summary>A network object KIND in the root <c>kinds[]</c> of maps.json (§10.10).</summary>
/// <remarks>Public FIELDS — read via JsonUtil (IncludeFields); names match maps.json exactly.
/// <para><c>maxHp == 0</c> = takes no damage: an object with an identity but no health is legitimate
/// (decorative / grabbable objects are typically like this).</para>
/// <para>⚠️ The RULE lives on the kind, not on the object (§10.10): one kind appears in ten arenas and
/// copying "can it be grabbed / which events" onto objects would make ten truth sources.</para></remarks>
public sealed class KindEntry
{
    public string kind = "";
    public float maxHp;

    /// <summary><see cref="ArenaProtocol.OBJECT_GRAB_NONE"/> (default) |
    /// <see cref="ArenaProtocol.OBJECT_GRAB_ANYONE"/> (§10.10).</summary>
    public string grab = "";

    /// <summary>Accepted <c>object_event</c> names; EMPTY = the kind accepts no event at all.</summary>
    public KindEventEntry[] events = Array.Empty<KindEventEntry>();
}

/// <summary>The kind catalog of maps.json (§10.10): the RULES of a kind, keyed by <c>kind</c>.</summary>
/// <remarks>⚠️ Split from the per-map <c>objects[]</c> list on purpose — not to avoid repetition but
/// because ownership differs: the identity list belongs to the SCENE, the kind rules belong to the
/// CONTENT. So one kind appears in ten arenas and its health changes in a single place.
/// <para>Read-only after load → no lock needed.</para></remarks>
public sealed class KindTable
{
    /// <summary>No kinds known → every object is skipped at table build (§10.10).</summary>
    public static readonly KindTable Empty = new(Array.Empty<KindEntry>());

    private readonly Dictionary<string, KindEntry> _byKind = new(StringComparer.Ordinal);

    private KindTable(IEnumerable<KindEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.kind))
            {
                Console.WriteLine("[KindTable] kind'ı boş girdi — atlandı.");
                continue;
            }
            Normalize(entry);
            _byKind[entry.kind] = entry;
        }
    }

    /// <summary>Fills the empty fields of an OLD export so no reader has to test for them (§11):
    /// <c>grab</c> → none, <c>events</c> → empty, per event policy → anyone, phaseGate →
    /// playing.</summary>
    private static void Normalize(KindEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.grab)) entry.grab = ArenaProtocol.OBJECT_GRAB_NONE;
        entry.events ??= Array.Empty<KindEventEntry>();
        foreach (var ev in entry.events)
        {
            if (ev == null) continue;
            if (string.IsNullOrWhiteSpace(ev.policy)) ev.policy = ArenaProtocol.OBJECT_EVENT_POLICY_ANYONE;
            if (string.IsNullOrWhiteSpace(ev.phaseGate)) ev.phaseGate = ArenaProtocol.OBJECT_PHASE_GATE_PLAYING;
        }
    }

    public int Count => _byKind.Count;

    public IReadOnlyCollection<KindEntry> Entries => _byKind.Values;

    public static KindTable From(KindEntry[]? entries) =>
        entries is { Length: > 0 } ? new KindTable(entries) : Empty;

    /// <summary>false = unknown kind → the object is not taken into the world table (§10.10).</summary>
    public bool TryGet(string? kind, out KindEntry entry)
    {
        if (!string.IsNullOrEmpty(kind) && _byKind.TryGetValue(kind, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>Can this kind be picked up (§10.10)? Unknown kind = no.</summary>
    public bool CanGrab(string? kind) =>
        TryGet(kind, out var entry)
        && !string.Equals(entry.grab, ArenaProtocol.OBJECT_GRAB_NONE, StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves <c>(kind, eventName)</c> to its gates (§10.10 gate 2); false = the kind does not
    /// accept that event, so the message is rejected. An unknown kind or an empty <c>events[]</c> accepts
    /// nothing — free text on the wire, none on the server.</summary>
    public bool TryGetEvent(string? kind, string? name, out KindEventEntry entry)
    {
        entry = null!;
        if (string.IsNullOrEmpty(name) || !TryGet(kind, out var found)) return false;

        foreach (var ev in found.events)
        {
            if (ev == null || !string.Equals(ev.name, name, StringComparison.Ordinal)) continue;
            entry = ev;
            return true;
        }
        return false;
    }
}
