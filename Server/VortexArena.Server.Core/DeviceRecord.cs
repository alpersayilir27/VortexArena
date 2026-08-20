#nullable enable
namespace VortexArena.Server.Core;

/// <summary>A single device row in <c>devices.json</c>: <c>deviceId → { name, number }</c> (§2).</summary>
/// <remarks>Properties, not fields: this is not a protocol DTO but the server's own config, written
/// without <c>IncludeFields</c> (camelCase policy).
/// <para>Number uniqueness lives here: "no two devices share a number" holds across ALL records in
/// this file, not just online players, so a headset keeps its number for years. Ownership is always
/// queried from this map — a device that never connected (no in-memory <c>PlayerState</c>) may still
/// hold a number.</para></remarks>
public sealed class DeviceRecord
{
    /// <summary>Name from the pool or set via <c>set_identity</c>; NOT unique (the pool holds 20
    /// names).</summary>
    public string Name { get; set; } = "";

    /// <summary>Jersey number 1..99; <c>0</c> = unassigned (v1 upgrades, exhausted pool) and the only
    /// non-unique value.</summary>
    public int Number { get; set; }
}
