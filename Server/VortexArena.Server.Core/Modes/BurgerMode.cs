#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Hamburgerci — the Kids family's co-op shift: no weapons, no teams, no revive; the match
/// ends on TIME alone and the scoreboard waits for the operator.</summary>
/// <remarks>Customers arrive at the counter with an order, the players build the burger and serve it;
/// every point is scored through <see cref="MatchDirector.AddSharedScore"/> (§10.5 shared scoring).
/// <para>Differs from the TDM default (§10.5) in: <see cref="TeamMode.None"/>,
/// <see cref="ScoreKind.PlayerAndShared"/>, <see cref="ReviveAnchor.None"/>,
/// <see cref="WeaponSource.None"/>.</para>
/// <para>⚠️ The winner fields stay EMPTY on every ending (§10.5): with a shared score there is nobody
/// to declare — the result is the common total plus the individual ranking.</para></remarks>
public sealed class BurgerMode : IGameMode
{
    // ---- Tuning (mode-internal; nothing outside reads these) ----
    // The balance numbers (arrival, patience, grill, points) live in _settings — server.json → burger.

    /// <summary>Spawn → arrival at the counter; the same budget as the client's deterministic walk, so
    /// the customer reaches Waiting exactly where the client draws them standing.</summary>
    private const float CustomerWalkSeconds = 6f;

    /// <summary>Happy/unhappy → despawn.</summary>
    private const float CustomerLeaveSeconds = 4f;

    private const int CounterSlotCount = 3;

    /// <summary>Retry interval while every counter slot is taken — a whole arrival interval would be
    /// burnt on a busy shift.</summary>
    private const float CounterFullRetrySeconds = 1f;

    /// <summary>Vertical gap between the halves of a cut bun (m). Born at the SAME point they overlap and
    /// physics flings one of them off the board.</summary>
    private const float BunHalfGap = 0.06f;

    /// <summary>Live ingredients before the oldest FREE one is cleaned away. Every <c>take</c> spawns and
    /// only a serve removes, so a ten minute shift would otherwise carpet the floor.</summary>
    private const int MaxLiveIngredients = 48;

    // Customer stages on the wire.
    private const int CustomerWalking = 0;
    private const int CustomerWaiting = 1;
    private const int CustomerHappy = 2;
    private const int CustomerUnhappy = 3;

    // Patty stages on the wire.
    private const int PattyRaw = 0;
    private const int PattyCooked = 1;
    private const int PattyBurnt = 2;

    private const string BunBottom = "bun_bottom";
    private const string BunTop = "bun_top";
    private const string Patty = "patty";
    private const string DispenserPrefix = "dispenser_";

    /// <summary>What a dispenser may hand out; anything else is a content/export drift, not an
    /// ingredient.</summary>
    private static readonly HashSet<string> IngredientWhitelist = new()
    {
        "bun_whole", "patty", "cheese", "bacon", "lettuce", "onion", "pickle", "tomato", "sauce"
    };

    /// <summary>Fillings a generated order may ask for, patties aside.</summary>
    private static readonly string[] MiddlePool =
    {
        "cheese", "bacon", "lettuce", "onion", "pickle", "tomato", "sauce"
    };

    // ---- Shift state ----
    // ⚠️ One IGameMode instance is created once at RegisterModes() and reused by every match, so all of
    // this is reset in OnMatchStart — never in a field initializer.

    private readonly BurgerSettings _settings;

    private int _happy;
    private int _unhappy;
    private float _spawnTimer;
    private readonly bool[] _slotTaken = new bool[CounterSlotCount];

    /// <summary>Customer netId → its slot, order and countdown.</summary>
    private readonly Dictionary<int, Customer> _customers = new();

    /// <summary>Patty netId → seconds spent on the grill.</summary>
    private readonly Dictionary<int, float> _cooking = new();

    /// <summary>Spawned ingredient netIds, OLDEST FIRST — the cleanup order.</summary>
    private readonly List<int> _ingredients = new();

    public BurgerMode() : this(new BurgerSettings())
    {
    }

    /// <summary>Balance from <c>server.json → burger</c>; expected ALREADY sanitized.</summary>
    public BurgerMode(BurgerSettings settings) => _settings = settings;

    public string ModeId => "burger";

    /// <summary>Kids family (§11): the <c>start_match</c> gate rejects a map from any other family.</summary>
    public string GameType => MapTable.KidsGameType;

    /// <summary>The result screen waits for the operator (§10.1): the shift's table is what the group
    /// reads together at the end, and the core's <c>MATCH_END_SECONDS</c> valve would wipe it mid-read.</summary>
    public bool HoldsResultForOperator => true;

    /// <summary>The Hamburgerci rule shape.</summary>
    /// <remarks><c>Weapons = None</c> is what turns damage off — there is no separate switch (§10.5),
    /// no <c>hit_report</c> is ever sent.
    /// <para><c>Revive = None</c> + <c>RespawnDelay = 0</c>: nobody dies here, so a client countdown to
    /// a revive that never happens would be a lie.</para></remarks>
    public ModeRules Rules => new()
    {
        Teams = TeamMode.None,
        Scoring = ScoreKind.PlayerAndShared,
        Revive = ReviveAnchor.None,
        Weapons = WeaponSource.None,
        RespawnDelay = 0f
    };

    /// <summary>Length of a shift; the operator can override it per match (§5.2).</summary>
    public int DefaultRoundSeconds => 600;

    /// <summary>No score limit: a co-op shift is bounded by the clock, and a target score would end it
    /// early with no winner to declare.</summary>
    public int DefaultScoreLimit => ArenaProtocol.SCORE_LIMIT_UNLIMITED;

    public void OnMatchStart(MatchDirector director)
    {
        _happy = 0;
        _unhappy = 0;
        _customers.Clear();
        _cooking.Clear();
        _ingredients.Clear();
        Array.Clear(_slotTaken, 0, _slotTaken.Length);
        _spawnTimer = _settings.customerIntervalStart;
        PushModeState(director);

        Console.WriteLine($"[burger] vardiya başladı — {director.RoundSeconds} sn; skor limiti yok, " +
                          "kazanan yok (ortak toplam + bireysel sıralama).");
    }

    /// <summary>Time is the ONLY end condition; the winner fields stay empty (§10.5 shared scoring).</summary>
    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        outcome = MatchOutcome.Draw;
        return director.TimeRemaining <= 0f;
    }

    public void OnTick(MatchDirector director, float deltaSeconds)
    {
        TickSpawn(director, deltaSeconds);
        TickCustomers(director, deltaSeconds);
        TickGrill(director, deltaSeconds);
    }

    /// <summary>Shift progress 0→1; the source of both ramps.</summary>
    /// <remarks>A shift with no length (<c>roundSeconds &lt;= 0</c>) stays at 0 — the ramp then plays the
    /// start values instead of dividing by zero.</remarks>
    private static float Progress(MatchDirector director)
    {
        var round = director.RoundSeconds;
        if (round <= 0) return 0f;
        var elapsed = round - director.TimeRemaining;
        return Math.Clamp(elapsed / round, 0f, 1f);
    }

    private static float Lerp(float from, float to, float t) => from + (to - from) * t;

    private void TickSpawn(MatchDirector director, float deltaSeconds)
    {
        _spawnTimer -= deltaSeconds;
        if (_spawnTimer > 0f) return;

        var slot = FreeSlot();
        if (slot < 0)
        {
            _spawnTimer = CounterFullRetrySeconds;
            return;
        }

        var progress = Progress(director);
        _slotTaken[slot] = true;
        var recipe = BuildRecipe();
        var netId = director.SpawnObject("customer", default, payload: $"slot:{slot};r:{recipe}");
        if (netId == 0) _slotTaken[slot] = false;
        else _customers[netId] = new Customer
        {
            Slot = slot,
            Recipe = recipe,
            Stage = CustomerWalking,
            Timer = CustomerWalkSeconds,
            // Frozen at BIRTH: a patience read later would keep shrinking under a customer who is
            // already waiting.
            Patience = Lerp(_settings.patienceStart, _settings.patienceEnd, progress)
        };

        _spawnTimer = Lerp(_settings.customerIntervalStart, _settings.customerIntervalEnd, progress);
    }

    private void TickCustomers(MatchDirector director, float deltaSeconds)
    {
        if (_customers.Count == 0) return;

        // Copied: leaving customers are removed inside the loop.
        foreach (var netId in new List<int>(_customers.Keys))
        {
            if (!_customers.TryGetValue(netId, out var customer)) continue;
            customer.Timer -= deltaSeconds;
            if (customer.Timer > 0f) continue;

            switch (customer.Stage)
            {
                case CustomerWalking:
                    customer.Stage = CustomerWaiting;
                    customer.Timer = customer.Patience;
                    director.SetObjectStage(netId, CustomerWaiting);
                    break;

                case CustomerWaiting:
                    customer.Stage = CustomerUnhappy;
                    customer.Timer = CustomerLeaveSeconds;
                    _unhappy++;
                    director.SetObjectStage(netId, CustomerUnhappy);
                    PushModeState(director);
                    break;

                default:
                    director.DespawnObject(netId);
                    ReleaseCustomer(netId, customer);
                    break;
            }
        }
    }

    private void TickGrill(MatchDirector director, float deltaSeconds)
    {
        if (_cooking.Count == 0) return;

        foreach (var netId in new List<int>(_cooking.Keys))
        {
            if (!director.TryReadObject(netId, out _, out _, out _, out _, out _, out _))
            {
                // Eaten, served or reset away: nothing left to cook.
                _cooking.Remove(netId);
                continue;
            }

            var cooked = _cooking[netId] + deltaSeconds;
            _cooking[netId] = cooked;
            if (cooked >= _settings.burnSeconds) director.SetObjectStage(netId, PattyBurnt);
            else if (cooked >= _settings.cookSeconds) director.SetObjectStage(netId, PattyCooked);
        }
    }

    public bool OnObjectEvent(MatchDirector director, int playerId, int netId, string kind, ObjectEventMsg msg)
    {
        switch (msg.name)
        {
            case "take" when kind.StartsWith(DispenserPrefix, StringComparison.Ordinal):
                return HandleTake(director, playerId, kind, msg);
            case "cut" when kind == "bun_whole":
                return HandleCut(director, netId);
            case "grill" when kind == Patty:
                return HandleGrill(director, netId, msg);
            case "serve" when kind == "board":
                return HandleServe(director, playerId, msg);
            default:
                return false;
        }
    }

    /// <summary>Dispenser → a fresh ingredient straight into the requesting hand.</summary>
    private bool HandleTake(MatchDirector director, int playerId, string kind, ObjectEventMsg msg)
    {
        var ingredient = kind.Substring(DispenserPrefix.Length);
        if (!IngredientWhitelist.Contains(ingredient))
        {
            Console.WriteLine($"[burger] bilinmeyen malzeme dolabı '{kind}' — atlandı.");
            return false;
        }

        var rightHand = msg.i != null && msg.i.Length > 0 && msg.i[0] == 1;
        // Pose default: the object is born in a hand, so a resting pose would be meaningless.
        Track(director, director.SpawnObject(ingredient, default, playerId, rightHand));
        return true;
    }

    /// <summary>Whole bun → bottom + top, the top raised by <see cref="BunHalfGap"/>.</summary>
    private bool HandleCut(MatchDirector director, int netId)
    {
        if (!director.TryReadObject(netId, out _, out _, out var owner, out _, out var pose, out _)) return false;
        if (owner != 0) return false; // A bun in someone's hand is not on the cutting board.

        director.DespawnObject(netId);
        _ingredients.Remove(netId);

        var top = pose;
        top.py += BunHalfGap;
        Track(director, director.SpawnObject(BunBottom, pose));
        Track(director, director.SpawnObject(BunTop, top));
        return true;
    }

    /// <summary>Books a freshly spawned ingredient and keeps the world from silting up.</summary>
    private void Track(MatchDirector director, int netId)
    {
        if (netId == 0) return;
        _ingredients.Add(netId);
        TrimIngredients(director);
    }

    /// <summary>Drops ids the world no longer has, then despawns the OLDEST FREE ingredient while the
    /// shift is over its cap.</summary>
    /// <remarks>⚠️ Only a free one may go: something in a hand is being used right now, and cleaning it
    /// away would take an ingredient out of a player's grip. If everything live is held, nothing is
    /// cleaned — the cap is a tidy-up, not a promise.</remarks>
    private void TrimIngredients(MatchDirector director)
    {
        for (var index = _ingredients.Count - 1; index >= 0; index--)
        {
            if (!director.TryReadObject(_ingredients[index], out _, out _, out _, out _, out _, out _))
                _ingredients.RemoveAt(index);
        }

        while (_ingredients.Count > MaxLiveIngredients)
        {
            var victim = -1;
            for (var index = 0; index < _ingredients.Count; index++)
            {
                if (!director.TryReadObject(_ingredients[index], out _, out _, out var owner, out _, out _, out _))
                    continue;
                if (owner != 0) continue;
                victim = index;
                break;
            }

            if (victim < 0) break;

            var netId = _ingredients[victim];
            director.DespawnObject(netId);
            _cooking.Remove(netId);
            _ingredients.RemoveAt(victim);
        }
    }

    /// <summary>Patty on/off the grill; <c>i[0]</c> 1 = on, 0 = off.</summary>
    /// <remarks>Returns false ON PURPOSE: the sizzle is cosmetic and belongs to everyone, while the
    /// doneness change already publishes its own <c>object_state</c>.</remarks>
    private bool HandleGrill(MatchDirector director, int netId, ObjectEventMsg msg)
    {
        if (!director.TryReadObject(netId, out _, out _, out var owner, out _, out _, out _)) return false;
        if (owner != 0) return false;

        if (msg.i != null && msg.i.Length > 0 && msg.i[0] == 1) _cooking.TryAdd(netId, 0f);
        else _cooking.Remove(netId);
        return false;
    }

    /// <summary>Board handed to a customer: <c>i[0]</c> = customer netId, <c>i[1..]</c> = the stack
    /// bottom-to-top.</summary>
    private bool HandleServe(MatchDirector director, int playerId, ObjectEventMsg msg)
    {
        if (msg.i == null || msg.i.Length < 2) return false;

        var customerNetId = msg.i[0];
        if (!_customers.TryGetValue(customerNetId, out var customer)) return false;
        if (customer.Stage != CustomerWaiting) return false;

        var served = new List<string>(msg.i.Length - 1);
        for (var index = 1; index < msg.i.Length; index++)
        {
            if (!director.TryReadObject(msg.i[index], out var kind, out var stage, out _, out _, out _, out _))
                return false;
            // A raw or burnt patty is a rejected order, not a scoring one.
            if (kind == Patty && stage != PattyCooked) return false;
            served.Add(kind);
        }

        if (served.Count < 2) return false;
        if (served[0] != BunBottom || served[served.Count - 1] != BunTop) return false;
        if (!MiddleMatches(customer.Recipe, served)) return false;

        director.AddSharedScore(playerId, _settings.servePoints);
        customer.Stage = CustomerHappy;
        customer.Timer = CustomerLeaveSeconds;
        _happy++;
        director.SetObjectStage(customerNetId, CustomerHappy);
        PushModeState(director);

        // The board itself survives — it is the workstation, not part of the burger.
        for (var index = 1; index < msg.i.Length; index++)
        {
            director.DespawnObject(msg.i[index]);
            _cooking.Remove(msg.i[index]);
            _ingredients.Remove(msg.i[index]);
        }
        return true;
    }

    /// <summary>Order vs stack by CONTENT: the counts of the filling kinds must match exactly (missing or
    /// extra rejects), the stacking order is free.</summary>
    private static bool MiddleMatches(string recipe, List<string> served)
    {
        var wanted = new Dictionary<string, int>();
        foreach (var item in MiddleOf(recipe))
        {
            wanted.TryGetValue(item, out var count);
            wanted[item] = count + 1;
        }

        for (var index = 1; index < served.Count - 1; index++)
        {
            var item = served[index];
            if (!wanted.TryGetValue(item, out var count) || count == 0) return false;
            wanted[item] = count - 1;
        }

        foreach (var count in wanted.Values) if (count != 0) return false;
        return true;
    }

    /// <summary>Builds an order: bottom bun, 1-2 patties, 0-3 DISTINCT fillings, top bun; the middle is
    /// shuffled so the stack order carries no hint.</summary>
    private static string BuildRecipe()
    {
        var random = Random.Shared;
        var middle = new List<string>();
        var pattyCount = random.Next(1, 3);
        for (var index = 0; index < pattyCount; index++) middle.Add(Patty);

        var pool = new List<string>(MiddlePool);
        var extras = random.Next(0, 4);
        for (var index = 0; index < extras && pool.Count > 0; index++)
        {
            var pick = random.Next(pool.Count);
            middle.Add(pool[pick]);
            pool.RemoveAt(pick);
        }

        for (var index = middle.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (middle[index], middle[swap]) = (middle[swap], middle[index]);
        }

        return $"{BunBottom},{string.Join(",", middle)},{BunTop}";
    }

    /// <summary>The order without its two buns — what the served stack's filling is compared against.</summary>
    private static List<string> MiddleOf(string recipe)
    {
        var parts = recipe.Split(',');
        var middle = new List<string>();
        for (var index = 1; index < parts.Length - 1; index++) middle.Add(parts[index]);
        return middle;
    }

    private int FreeSlot()
    {
        for (var slot = 0; slot < _slotTaken.Length; slot++) if (!_slotTaken[slot]) return slot;
        return -1;
    }

    private void ReleaseCustomer(int netId, Customer customer)
    {
        _slotTaken[customer.Slot] = false;
        _customers.Remove(netId);
    }

    private void PushModeState(MatchDirector director) => director.SetModeState($"h:{_happy};u:{_unhappy}");

    /// <summary>One customer at the counter.</summary>
    private sealed class Customer
    {
        public int Slot;

        /// <summary>Comma separated order, buns included.</summary>
        public string Recipe = "";

        public int Stage;

        /// <summary>Patience granted at birth (seconds), from the ramp.</summary>
        public float Patience;

        /// <summary>Counts the walk, then the patience, then the leaving — one field, because a customer
        /// is only ever in one of the three.</summary>
        public float Timer;
    }
}
