#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.World;

/// <summary>One network object's server-authoritative state (§10.10).</summary>
public sealed class NetObjectEntry
{
    /// <summary>Baked scene id (<see cref="ArenaProtocol.NET_ID_SCENE_MIN"/>..
    /// <see cref="ArenaProtocol.NET_ID_SCENE_MAX"/>) or a server-allocated dynamic id
    /// (<see cref="ArenaProtocol.NET_ID_DYNAMIC_MIN"/>..<see cref="ArenaProtocol.NET_ID_DYNAMIC_MAX"/>);
    /// unique PER SCENE, not server-wide.</summary>
    public int NetId;

    public string Kind = "";

    /// <summary>0 = takes no damage (§10.10).</summary>
    public float MaxHp;

    public float Hp;

    public int Flags;

    /// <summary>playerId holding the object; 0 = nobody (§10.10).</summary>
    public int Owner;

    /// <summary>Per-kind stage (doneness, fill level); the protocol does not interpret it.</summary>
    public int Stage;

    /// <summary>Spawned at runtime by the server, not baked into the scene: a round reset DELETES it and
    /// its id never comes back before then.</summary>
    public bool Dynamic;

    /// <summary>false = the object never moved, so it has no resting pose here — the scene already holds
    /// it (§10.10). Then <c>pos</c>/<c>rot</c> travel EMPTY, which is what the client expects.</summary>
    public bool HasPose;

    /// <summary>Resting pose in arena space (§10.10); only meaningful with <see cref="HasPose"/>.</summary>
    public PoseData Pose;

    /// <summary>Mode-defined per-instance text (§10.10); the core never interprets it.</summary>
    public string Payload = "";
}

/// <summary>Server-authoritative state of the loaded scene's network objects (§10.10): the only writer
/// of <c>hp</c>/<c>flags</c>/<c>owner</c>/<c>stage</c>, the source of
/// <c>object_state</c>/<c>world_state</c>/<c>object_spawn</c>.</summary>
/// <remarks>⚠️ Lives UNDER <see cref="MatchDirector"/>'s <c>_gate</c> and has NO LOCK OF ITS OWN — a
/// second lock here would be a deadlock candidate (the director already calls in while holding its
/// gate). Hence every method is named <c>…Locked</c>: holding the director's lock is the CALLER's part
/// of the contract.</remarks>
public sealed class WorldObjectTable
{
    private readonly Dictionary<int, NetObjectEntry> _byNetId = new();

    /// <summary>Kind rules of the staged scene, captured at rebuild so the grab/event/spawn paths need
    /// no extra parameter.</summary>
    private KindTable _kinds = KindTable.Empty;

    /// <summary>Next dynamic id to hand out (§10.10). ⚠️ Allocation is MONOTONIC inside a round: a
    /// despawned id is never re-issued before the round/scene reset, because a pose packet or an
    /// <c>object_event</c> still in flight would land on whatever object inherited the id — and nothing
    /// anywhere would report an error.</summary>
    private int _nextDynamicId = ArenaProtocol.NET_ID_DYNAMIC_MIN;

    public int Count => _byNetId.Count;

    /// <summary>Builds the table FROM SCRATCH for the staged scene: every object at full health, no
    /// flags, no owner. A null map (unknown scene / no objects) empties the table.</summary>
    /// <remarks>Dynamic objects are gone with the rest and the id pool restarts — the second match on a
    /// map does not begin with the previous one's props.</remarks>
    public void RebuildLocked(MapEntry? map, KindTable kinds)
    {
        _byNetId.Clear();
        _kinds = kinds;
        _nextDynamicId = ArenaProtocol.NET_ID_DYNAMIC_MIN;
        var objects = map?.objects;
        if (objects == null) return;
        var sceneName = map!.sceneName;

        foreach (var item in objects)
        {
            if (item == null) continue;
            if (item.sceneId < ArenaProtocol.NET_ID_SCENE_MIN || item.sceneId > ArenaProtocol.NET_ID_SCENE_MAX)
            {
                Console.WriteLine($"[world] '{sceneName}' netId {item.sceneId} aralık dışı " +
                                  $"({ArenaProtocol.NET_ID_SCENE_MIN}..{ArenaProtocol.NET_ID_SCENE_MAX}) — atlandı.");
                continue;
            }
            if (!kinds.TryGet(item.kind, out var kind))
            {
                Console.WriteLine($"[world] '{sceneName}' netId {item.sceneId}: bilinmeyen tür '{item.kind}' — atlandı.");
                continue;
            }
            if (_byNetId.ContainsKey(item.sceneId))
            {
                Console.WriteLine($"[world] '{sceneName}' netId {item.sceneId} iki kez geçiyor — ikincisi atlandı.");
                continue;
            }

            _byNetId[item.sceneId] = new NetObjectEntry
            {
                NetId = item.sceneId,
                Kind = kind.kind,
                MaxHp = kind.maxHp,
                Hp = kind.maxHp,
                Flags = 0
            };
        }
    }

    /// <summary>Everything back to full health, no flags, no owner, stage 0, and every DYNAMIC object
    /// deleted; false = nothing to change (so no <c>world_state</c> is produced for a table already
    /// intact).</summary>
    /// <remarks>The dynamic id pool restarts here too: the round boundary is exactly the point where
    /// re-issuing an id becomes safe again (nothing from the previous round is still in flight by the
    /// time the next one starts).</remarks>
    public bool ResetLocked()
    {
        var changed = false;
        var dynamicIds = new List<int>();

        foreach (var entry in _byNetId.Values)
        {
            if (entry.Dynamic)
            {
                dynamicIds.Add(entry.NetId);
                continue;
            }
            if (entry.Hp != entry.MaxHp)
            {
                entry.Hp = entry.MaxHp;
                changed = true;
            }
            if (entry.Flags != 0)
            {
                entry.Flags = 0;
                changed = true;
            }
            if (entry.Owner != 0)
            {
                entry.Owner = 0;
                changed = true;
            }
            if (entry.Stage != 0)
            {
                entry.Stage = 0;
                changed = true;
            }
            if (entry.HasPose)
            {
                // The client reloads/resets the scene, so the baked pose is the truth again.
                entry.HasPose = false;
                entry.Pose = default;
                changed = true;
            }
            // Instance text belongs to the round that wrote it; kept, it would describe an object the
            // next round never touched.
            if (entry.Payload.Length != 0)
            {
                entry.Payload = "";
                changed = true;
            }
        }

        foreach (var id in dynamicIds) _byNetId.Remove(id);
        if (dynamicIds.Count > 0) changed = true;
        _nextDynamicId = ArenaProtocol.NET_ID_DYNAMIC_MIN;
        return changed;
    }

    public bool TryGetLocked(int netId, out NetObjectEntry entry)
    {
        if (_byNetId.TryGetValue(netId, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>netIds of one kind, ASCENDING — the list a mode drives (holes, spawn points).</summary>
    /// <remarks>Sorted on purpose: dictionary order varies between runs, and a mode picking "the first
    /// one" would then behave differently on two servers loading the same scene.</remarks>
    public List<int> ListByKindLocked(string kind)
    {
        var ids = new List<int>();
        if (string.IsNullOrEmpty(kind)) return ids;

        foreach (var entry in _byNetId.Values)
        {
            if (string.Equals(entry.Kind, kind, StringComparison.Ordinal)) ids.Add(entry.NetId);
        }

        ids.Sort();
        return ids;
    }

    /// <summary>Gates 3-4 of the object hit path (§10.10) plus the damage itself; false = rejected, with
    /// the reason for the console line.</summary>
    public bool ApplyDamageLocked(int netId, float damage, out NetObjectEntry entry, out string rejectReason)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            rejectReason = $"netId {netId} bu sahnede yok";
            return false;
        }

        entry = found;
        if (found.MaxHp <= 0f)
        {
            rejectReason = $"'{found.Kind}' hasar almaz (maxHp 0)";
            return false;
        }
        // A second bullet in the same frame must not publish a second break.
        if ((found.Flags & ArenaProtocol.OBJECT_FLAG_BROKEN) != 0)
        {
            rejectReason = $"netId {netId} zaten kırık";
            return false;
        }
        if (!float.IsFinite(damage) || damage <= 0f)
        {
            rejectReason = $"geçersiz hasar {damage}";
            return false;
        }

        found.Hp = MathF.Max(0f, found.Hp - damage);
        if (found.Hp == 0f) found.Flags |= ArenaProtocol.OBJECT_FLAG_BROKEN;
        rejectReason = "";
        return true;
    }

    // ---- Ownership (§10.10) ----

    /// <summary>object_grab gates: the object exists → its kind is grabbable → nobody holds it. On
    /// success the requester becomes the owner and the object hangs off their hand.</summary>
    /// <remarks>⚠️ A grab on a HELD object is a silent rejection by design: there is no stealing and no
    /// denial message — the requester sees someone else in the broadcast <c>object_state</c> and undoes
    /// its optimistic local grab.
    /// <para><c>Awake</c> is cleared: a held object streams no pose (§6.12), its position comes from the
    /// owner's hand.</para></remarks>
    public bool TryGrabLocked(int netId, int playerId, bool rightHand, out NetObjectEntry entry,
        out string rejectReason)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            rejectReason = $"netId {netId} bu sahnede yok";
            return false;
        }

        entry = found;
        if (!_kinds.CanGrab(found.Kind))
        {
            rejectReason = $"'{found.Kind}' tutulamaz (grab none)";
            return false;
        }
        if (found.Owner != 0)
        {
            rejectReason = found.Owner == playerId
                ? $"netId {netId} zaten bu oyuncuda"
                : $"netId {netId} başkasında (owner {found.Owner})";
            return false;
        }

        found.Owner = playerId;
        found.Flags |= ArenaProtocol.OBJECT_FLAG_HELD;
        if (rightHand) found.Flags |= ArenaProtocol.OBJECT_FLAG_HELD_RIGHT;
        else found.Flags &= ~ArenaProtocol.OBJECT_FLAG_HELD_RIGHT;
        found.Flags &= ~ArenaProtocol.OBJECT_FLAG_AWAKE;
        rejectReason = "";
        return true;
    }

    /// <summary>object_release: the object LEAVES THE HAND (§10.10). <c>Held</c>/<c>HeldRight</c> drop,
    /// <c>Awake</c> rises and the OWNER IS KEPT — the flight belongs to the thrower, who streams it over
    /// <c>0x09</c> until it stops (<see cref="TryRestLocked"/>).</summary>
    /// <remarks>⚠️ Leaving the hand and coming to rest are two DIFFERENT moments: with one message the
    /// wire would keep saying "held" through the whole flight, and a player joining in that window would
    /// draw the object stuck to a hand. The reported pose is written anyway — if the stream never
    /// arrives, that is what the table is left holding.</remarks>
    public bool TryReleaseLocked(int netId, int playerId, float[]? pos, float[]? rot,
        out NetObjectEntry entry, out string rejectReason)
    {
        if (!TryGetOwnedLocked(netId, playerId, out entry, out rejectReason)) return false;

        if (TryReadPose(pos, rot, out var pose)) WriteRestPoseLocked(entry, pose);
        entry.Flags &= ~(ArenaProtocol.OBJECT_FLAG_HELD | ArenaProtocol.OBJECT_FLAG_HELD_RIGHT);
        entry.Flags |= ArenaProtocol.OBJECT_FLAG_AWAKE;
        return true;
    }

    /// <summary>object_rest: the object STOPPED (§10.10). <c>Awake</c> drops, ownership ends and the
    /// reported pose becomes the resting pose — that is what a late joiner's <c>world_state</c> must
    /// carry, not a frame from mid-flight.</summary>
    public bool TryRestLocked(int netId, int playerId, float[]? pos, float[]? rot,
        out NetObjectEntry entry, out string rejectReason)
    {
        if (!TryGetOwnedLocked(netId, playerId, out entry, out rejectReason)) return false;

        if (TryReadPose(pos, rot, out var pose)) WriteRestPoseLocked(entry, pose);
        ClearOwnershipLocked(entry);
        return true;
    }

    /// <summary>Shared gate of the two owner-only messages: the object exists and the sender owns it.
    /// Anyone else is rejected (silently, at the caller).</summary>
    private bool TryGetOwnedLocked(int netId, int playerId, out NetObjectEntry entry, out string rejectReason)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            rejectReason = $"netId {netId} bu sahnede yok";
            return false;
        }

        entry = found;
        if (found.Owner != playerId)
        {
            rejectReason = found.Owner == 0
                ? $"netId {netId} zaten sahipsiz"
                : $"netId {netId} sahibi değil (owner {found.Owner})";
            return false;
        }
        rejectReason = "";
        return true;
    }

    /// <summary>Frees EVERY object held by this player, each at the pose the caller last saw (§6.12's
    /// lock-free slot), and returns the affected entries so the caller can broadcast one
    /// <c>object_state</c> per object.</summary>
    /// <remarks>⚠️ Without this gate a dropped or dead owner locks the object PERMANENTLY: nobody can
    /// take it and nobody can see it move until the round resets. <paramref name="lastKnownPose"/>
    /// returning null leaves the stored pose alone — the object stays where the table last knew it,
    /// rather than teleporting back to where it was picked up.</remarks>
    public List<NetObjectEntry> ReleaseOwnedByLocked(int playerId, Func<int, PoseData?> lastKnownPose)
    {
        var released = new List<NetObjectEntry>();
        if (playerId == 0) return released;

        foreach (var entry in _byNetId.Values)
        {
            if (entry.Owner != playerId) continue;

            var pose = lastKnownPose(entry.NetId);
            if (pose.HasValue) WriteRestPoseLocked(entry, pose.Value);
            ClearOwnershipLocked(entry);
            released.Add(entry);
        }
        return released;
    }

    /// <summary>Objects that currently have an owner — the source the director mirrors into its
    /// lock-free ownership map after a rebuild/reset.</summary>
    public IEnumerable<NetObjectEntry> OwnedLocked()
    {
        foreach (var entry in _byNetId.Values)
        {
            if (entry.Owner != 0) yield return entry;
        }
    }

    // ---- Event outcomes (§10.10): the thin writers a mode's OnObjectEvent uses ----

    /// <summary>Writes the per-kind stage; false = no such object or the value is unchanged (an
    /// unchanged write must not produce an <c>object_state</c>).</summary>
    public bool SetStageLocked(int netId, int stage, out NetObjectEntry entry)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            return false;
        }

        entry = found;
        if (found.Stage == stage) return false;
        found.Stage = stage;
        return true;
    }

    /// <summary>Writes the mode-defined per-instance text; false = no such object or the value is
    /// unchanged (an unchanged write must not produce an <c>object_state</c>).</summary>
    public bool SetPayloadLocked(int netId, string payload, out NetObjectEntry entry)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            return false;
        }

        entry = found;
        if (found.Payload == payload) return false;
        found.Payload = payload;
        return true;
    }

    /// <summary>Sets and clears flag bits in one write; false = no such object or nothing changed.</summary>
    /// <remarks>⚠️ Ownership bits are NOT written through here (<c>Held</c>/<c>HeldRight</c> belong to the
    /// grab/release path) — mixing the two would give ownership a second writer.</remarks>
    public bool SetFlagsLocked(int netId, int setMask, int clearMask, out NetObjectEntry entry)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            return false;
        }

        entry = found;
        var next = (found.Flags | setMask) & ~clearMask;
        if (next == found.Flags) return false;
        found.Flags = next;
        return true;
    }

    // ---- Dynamic spawn / despawn (§10.10) ----

    /// <summary>Repairs an all-zero quaternion into the identity rotation.</summary>
    /// <remarks>⚠️ <c>default(PoseData)</c> is the natural thing to write when a spawn's pose is
    /// MEANINGLESS (an object born in a hand), and its quaternion is <c>(0,0,0,0)</c> — not a rotation.
    /// The client still applies it when instantiating, and a degenerate quaternion there is not an
    /// exception, it is a broken transform plus console spam. Guarded once at the source rather than at
    /// every call site.</remarks>
    private static PoseData WithValidRotation(PoseData pose)
    {
        if (pose.qx != 0f || pose.qy != 0f || pose.qz != 0f || pose.qw != 0f) return pose;
        pose.qw = 1f;
        return pose;
    }

    /// <summary>Creates a runtime object with a server-allocated id; false = unknown kind or the dynamic
    /// range is exhausted (reason for the console line).</summary>
    /// <remarks>A non-zero <paramref name="owner"/> makes the object BORN IN A HAND: the held bits are
    /// written here, so no second "give it to them" step can leave it on the floor for a frame.</remarks>
    public bool TrySpawnLocked(string? kind, PoseData pose, int owner, bool rightHand, string? payload,
        out NetObjectEntry entry, out string rejectReason)
    {
        entry = null!;
        if (!_kinds.TryGet(kind, out var kindEntry))
        {
            rejectReason = $"bilinmeyen tür '{kind}'";
            return false;
        }
        if (!TryAllocateDynamicIdLocked(out var netId))
        {
            rejectReason = $"dinamik kimlik aralığı doldu " +
                           $"({ArenaProtocol.NET_ID_DYNAMIC_MIN}..{ArenaProtocol.NET_ID_DYNAMIC_MAX})";
            return false;
        }

        entry = new NetObjectEntry
        {
            NetId = netId,
            Kind = kindEntry.kind,
            MaxHp = kindEntry.maxHp,
            Hp = kindEntry.maxHp,
            Owner = owner,
            Flags = owner != 0
                ? ArenaProtocol.OBJECT_FLAG_HELD | (rightHand ? ArenaProtocol.OBJECT_FLAG_HELD_RIGHT : 0)
                : 0,
            Dynamic = true,
            HasPose = true,
            Pose = WithValidRotation(pose),
            Payload = payload ?? ""
        };
        _byNetId[netId] = entry;
        rejectReason = "";
        return true;
    }

    /// <summary>Removes a dynamic object; false = no such object or it is a SCENE object (its id is baked
    /// into the scene, so there is nothing on the client to destroy).</summary>
    public bool TryDespawnLocked(int netId, out NetObjectEntry entry, out string rejectReason)
    {
        if (!_byNetId.TryGetValue(netId, out var found))
        {
            entry = null!;
            rejectReason = $"netId {netId} bu sahnede yok";
            return false;
        }

        entry = found;
        if (!found.Dynamic)
        {
            rejectReason = $"netId {netId} sahne objesi — despawn edilemez";
            return false;
        }

        _byNetId.Remove(netId);
        rejectReason = "";
        return true;
    }

    /// <summary>Whole-table snapshot for <c>world_state</c>, ordered by netId — a deterministic order
    /// keeps two consecutive snapshots comparable.</summary>
    public ObjectStateMsg[] BuildStatesLocked()
    {
        var ids = new List<int>(_byNetId.Keys);
        ids.Sort();
        var states = new ObjectStateMsg[ids.Count];
        for (var i = 0; i < ids.Count; i++) states[i] = ToStateMsg(_byNetId[ids[i]]);
        return states;
    }

    /// <summary>⚠️ <c>pos</c>/<c>rot</c> are written from the LAST KNOWN pose even while the object is
    /// held, where they are meaningless: the reader skips them whenever the <c>Held</c> bit is set
    /// (§5.3), so there is no second rule to keep in sync.</summary>
    internal static ObjectStateMsg ToStateMsg(NetObjectEntry entry) => new()
    {
        netId = entry.NetId,
        kind = entry.Kind,
        hp = entry.Hp,
        flags = entry.Flags,
        owner = entry.Owner,
        stage = entry.Stage,
        // Never moved → the fields stay EMPTY: the object's pose is the one baked into the scene, and
        // inventing a zero here would let a client teleport an untouched cover to the origin.
        pos = entry.HasPose ? new[] { entry.Pose.px, entry.Pose.py, entry.Pose.pz } : null,
        rot = entry.HasPose ? new[] { entry.Pose.qx, entry.Pose.qy, entry.Pose.qz, entry.Pose.qw } : null,
        s = entry.Payload
    };

    private static void ClearOwnershipLocked(NetObjectEntry entry)
    {
        entry.Owner = 0;
        entry.Flags &= ~(ArenaProtocol.OBJECT_FLAG_HELD
                         | ArenaProtocol.OBJECT_FLAG_HELD_RIGHT
                         | ArenaProtocol.OBJECT_FLAG_AWAKE);
    }

    private static void WriteRestPoseLocked(NetObjectEntry entry, PoseData pose)
    {
        entry.Pose = pose;
        entry.HasPose = true;
    }

    /// <summary>Reads the wire pose; false = missing/short/non-finite, and then the stored pose is kept
    /// (the server has no metres to correct it with, but it will not store garbage either).</summary>
    private static bool TryReadPose(float[]? pos, float[]? rot, out PoseData pose)
    {
        pose = default;
        if (pos is not { Length: >= 3 } || rot is not { Length: >= 4 }) return false;
        for (var i = 0; i < 3; i++) if (!float.IsFinite(pos[i])) return false;
        for (var i = 0; i < 4; i++) if (!float.IsFinite(rot[i])) return false;

        pose.px = pos[0]; pose.py = pos[1]; pose.pz = pos[2];
        pose.qx = rot[0]; pose.qy = rot[1]; pose.qz = rot[2]; pose.qw = rot[3];
        return true;
    }

    private bool TryAllocateDynamicIdLocked(out int netId)
    {
        while (_nextDynamicId <= ArenaProtocol.NET_ID_DYNAMIC_MAX)
        {
            var candidate = _nextDynamicId++;
            if (_byNetId.ContainsKey(candidate)) continue;
            netId = candidate;
            return true;
        }
        netId = 0;
        return false;
    }
}
