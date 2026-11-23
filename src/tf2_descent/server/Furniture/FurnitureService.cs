using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Loot;
using TF2.Descent.Server.Mode;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// The furniture of a level (plan_descent_logic WO3; plan_tf2_rpg 2l, R24):
/// every shrine, container, Dispenser, explosive and trap the rules place on
/// the level's markers, kept as rows, and the pool of
/// <c>tf2_descent_furniture</c> entities the rows of woken rooms stand on.
///
/// <list type="bullet">
/// <item><b>Placing.</b> At level ready, when the Descent mode plays on a
/// level with a seed: the rules' <see cref="FurnishingRules.Furnish"/> over
/// the level's rooms and furniture markers (<see cref="LevelAdapters.ToFurniture"/>),
/// from the level's seed, the one the horde and the loot roll from. Each placed
/// marker is a row with its rules state.</item>
/// <item><b>The pool.</b> Made once at level ready, as many entities as the
/// level has rows up to <see cref="PoolCeiling"/> (section 2's budget),
/// parked; lent to the rows of awake rooms and parked again when a room
/// sleeps (<see cref="IRoomActivity"/>), never made or removed in play. A row
/// keeps its state while it is data, so it wakes as it slept. An explosive
/// that has gone off parks its entity for good.</item>
/// <item><b>Using.</b> A shrine and a container answer +USE (and
/// <c>tf2_descent_furniture_use</c>, the same path): the rules decide, the
/// service carries out (a shrine's effect on its takers' stats, a container's
/// rolls on the loot floor for each hero in its room). The Dispenser heals and
/// gives ammo to heroes in its reach at the rules' rates; an explosive takes
/// damage and goes off; a trap trips, fires and re-arms on its clock.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureService(IDescentModeState mode, ILevelData level, TF2DescentRules rules, IFurnitureSpawner spawner,
                                     IRoomActivity rooms, IFurnitureWorld world, IFurnitureHeroes heroes, IItemLedger ledger,
                                     LootFloor floor, ILootGround ground, FurnitureBuffs buffs, FurnitureParking parking,
                                     IHooks<IFurnitureHazardHook> hazards, ClientMessages messages, ILog log,
                                     Automap.IAutomapReveal automap, Party.IPartyRoster roster, IFurnitureBreakWorld breaks)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>The most furniture entities a level has (section 2: furniture in woken rooms, 32).</summary>
    public const int PoolCeiling = 32;

    /// <summary>After the horde (First + 500) and the loot floor (+800), in Descent's furniture block.</summary>
    public const int Order = ModStages.First + 850;

    /// <summary>Frames between the checks of the rows' rooms (and of the keys' rooms).</summary>
    public const int RoomCheckEvery = 16;

    /// <summary>Seconds between a Dispenser's draws.</summary>
    public const double DispenseEvery = 0.2;

    /// <summary>How far above or below a trap or a Dispenser a hero's feet may be and still count as at it.</summary>
    public const float Reach = 64f;

    /// <summary>How far in front of a container its drop lies: clear of its box.</summary>
    public const float FootDistance = 48f;

    /// <summary>How high above an explosive's foot its blast starts: its middle, roughly.</summary>
    public const float BlastLift = 32f;

    public const string ShrineSpentToken = "#Descent_Furniture_ShrineSpent";
    public const string LockedToken = "#Descent_Furniture_Locked";
    public const string OpenedToken = "#Descent_Furniture_Opened";
    public const string KeyFoundToken = "#Descent_Furniture_KeyFound";

    private readonly List<FurnitureRow> _rows = [];
    private readonly List<FurnitureEntity> _pool = [];
    private readonly Stack<FurnitureEntity> _free = new();
    private BreakOutcome? _forced;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Row, int Slot), (double Health, double Ammo)> _owed = [];
    private readonly Dictionary<(int Row, int Slot), double> _lastSaid = [];
    private int _frames;
    private double _lastDispense;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    /// <summary>Counts levels: a container's drop that waited on the ledger across a level change does nothing.</summary>
    public int Level { get; private set; }

    /// <summary>Whether this level has its furniture and pool.</summary>
    public bool Ready { get; private set; }

    /// <summary>The seed and depth the level's furniture was rolled from.</summary>
    public ulong Seed { get; private set; }

    public int Depth { get; private set; }

    /// <summary>Every row of the level, in the rules' order (room, then marker kind, then marker).</summary>
    public IReadOnlyList<FurnitureRow> Rows => _rows;

    /// <summary>Entities this level's pool was made with.</summary>
    public int Made => _pool.Count;

    /// <summary>Pool entities made since the server started.</summary>
    public int MadeEver { get; private set; }

    /// <summary>Entities parked now.</summary>
    public int Free => _free.Count;

    /// <summary>Crates and barrels broken this level.</summary>
    public int Broken { get; private set; }

    /// <summary>The outcome the next break rolls instead of its own (the dev command); null for the roll's own.</summary>
    public BreakOutcome? ForcedNext => _forced;

    /// <summary>The engine's edicts before and after this level's pool was made.</summary>
    public (int Before, int After) PoolEdicts { get; private set; }

    /// <summary>The keys the party has found on this level (a locked container's <see cref="Placed.KeyId"/>).</summary>
    public IReadOnlyCollection<string> Keys => _keys;

    /// <summary>Whether a shrine of sight has revealed this level's map (for the automap, UI5).</summary>
    public bool MapRevealed { get; private set; }

    //-------------------------------------------------------------------------
    // The level
    //-------------------------------------------------------------------------

    public void OnLevelReady(string mapName)
    {
        Reset();
        if (!mode.Active)
            return;
        if (level.Current is not { Seed: ulong seed } data)
        {
            log.Msg($"[TF2Descent/Furniture] {mapName}: no level with a seed, no furniture\n");
            return;
        }

        Seed = seed;
        Depth = Math.Max(1, data.Depth);
        var furnishing = rules.Furniture.Placement.Furnish(seed, LevelAdapters.ToFurniture(data));
        var markers = data.Markers.ToDictionary(m => LevelAdapters.MarkerKey(m.Id), StringComparer.Ordinal);
        foreach (var placed in furnishing.Placed)
        {
            if (!markers.TryGetValue(placed.Marker, out var marker))
                continue;
            var row = new FurnitureRow(_rows.Count, placed, marker.Id, marker.Position, marker.Yaw, marker.Room);
            switch (placed.Kind)
            {
                case FurnitureKind.Shrine:
                    row.Shrine = new ShrineState(placed.Marker, placed.Variant);
                    break;
                case FurnitureKind.Dispenser:
                    row.Dispenser = rules.Furniture.Dispensers.New();
                    break;
                case FurnitureKind.Container:
                    row.Container = ContainerState.From(placed);
                    break;
                case FurnitureKind.Explosive:
                    row.Explosive = rules.Furniture.Explosives.New(placed.Marker, placed.Variant);
                    break;
                case FurnitureKind.Trap:
                    row.Trap = rules.Furniture.Traps.New(placed.Marker, placed.Variant);
                    break;
            }

            _rows.Add(row);
        }

        int before = spawner.Edicts();
        // The crates and barrels stand on no pool entity: the breakables' one entity owns them all (FurnitureBreakables).
        int breakables = Count(FurnitureKind.Breakable);
        int size = Math.Min(PoolCeiling, _rows.Count - breakables);
        for (int i = 0; i < size; i++)
        {
            var e = spawner.Make();
            if (e is null)
            {
                log.Warning($"[TF2Descent/Furniture] the engine refused furniture entity {i + 1} of {size}: the pool holds {_pool.Count}\n");
                break;
            }

            e.Join(this);
            e.Park();
            parking.Park(e.Handle);
            _pool.Add(e);
            MadeEver++;
        }

        for (int i = _pool.Count - 1; i >= 0; i--)
            _free.Push(_pool[i]);
        Ready = true;
        PoolEdicts = (before, spawner.Edicts());
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] level ready: {_rows.Count} furniture on {data.Markers.Count(m => LevelAdapters.IsFurniture(m.Kind))} markers from seed {seed} depth {Depth} " +
            $"({Count(FurnitureKind.Shrine)} shrines of {data.OfKind(LevelMarkerKind.Shrine).Count()}, {Count(FurnitureKind.Container)} containers of {data.OfKind(LevelMarkerKind.Container).Count()}, " +
            $"{Count(FurnitureKind.Dispenser)} dispensers of {data.OfKind(LevelMarkerKind.Dispenser).Count()}, {Count(FurnitureKind.Explosive)} explosives of {data.OfKind(LevelMarkerKind.Barrel).Count()}, " +
            $"{Count(FurnitureKind.Trap)} traps of {data.OfKind(LevelMarkerKind.Trap).Count()}, {breakables} crates and barrels), pool of {_pool.Count} (ceiling {PoolCeiling}) made and parked, entities {PoolEdicts.Before} -> {PoolEdicts.After}\n"));
        CheckRooms();
    }

    /// <summary>The crates' and barrels' models and gibs, standing and what each breaks as, precached while the level loads: a break makes them later.</summary>
    public void OnLevelInitPreEntity(string mapName) =>
        breaks.Precache(rules.Content.Furniture.Breakables.Kinds.Select(k => FurnitureCatalog.Of(FurnitureCatalog.Number(FurnitureKindId.Breakable, k)))
            .OfType<FurnitureEntry>().SelectMany(e => new[] { e.Model, e.SpentModel }).Distinct(StringComparer.Ordinal));

    private int Count(FurnitureKind kind) => _rows.Count(r => r.Kind == kind);

    public void OnLevelShutdownPreEntity()
    {
        if (_rows.Count > 0)
            log.Msg($"[TF2Descent/Furniture] level over: {_rows.Count} furniture rows forgotten, shrine effects end\n");
        Reset();
    }

    private void Reset()
    {
        Level++;
        Ready = false;
        _rows.Clear();
        _pool.Clear();
        _free.Clear();
        Broken = 0;
        _forced = null;
        _keys.Clear();
        _owed.Clear();
        _lastSaid.Clear();
        parking.Clear();
        buffs.LeaveLevel();
        MapRevealed = false;
        PoolEdicts = default;
        Seed = 0;
        Depth = 0;
        _frames = 0;
        _lastDispense = 0;
    }

    public void OnThink(float frameTime)
    {
        if (!Ready)
            return;

        double now = world.Now;
        if (++_frames >= RoomCheckEvery)
        {
            _frames = 0;
            CheckRooms();
            FindKeys();
        }

        Explosions(now);
        Breaks(now);
        Traps(now);
        if (now - _lastDispense >= DispenseEvery)
        {
            Dispense(now, Math.Min(0.5, now - _lastDispense));
            _lastDispense = now;
        }
    }

    //-------------------------------------------------------------------------
    // Sleep and wake
    //-------------------------------------------------------------------------

    /// <summary>Folds every row on an entity whose room sleeps (or that went off), then lends free entities to rows in woken rooms; answers how many folded.</summary>
    public int CheckRooms()
    {
        int folded = 0;
        foreach (var row in _rows)
        {
            if (row.Entity is not null && (row.Went || !rooms.Awake(row.Room)))
            {
                Fold(row);
                folded++;
            }
        }

        // A crate or barrel never takes a pool entity: the breakables' one entity stands for them all.
        foreach (var row in _rows)
        {
            if (_free.Count == 0)
                break;
            if (row.Kind != FurnitureKind.Breakable && row.Entity is null && !row.Went && rooms.Awake(row.Room))
                Embody(row, _free.Pop());
        }

        return folded;
    }

    private void Embody(FurnitureRow row, FurnitureEntity e)
    {
        row.Entity = e;
        parking.Unpark(e.Handle);
        e.Lend(row.Index, row.Position, row.Yaw, Look(row));
        if (Hazard(row) is float radius)
            hazards.Each(nameof(IFurnitureHazardHook.HazardArmed), h => h.HazardArmed(row.Index, row.KindId, row.Variant, row.Position, radius));
    }

    /// <summary>The row becomes data; its entity is parked and freed.</summary>
    private void Fold(FurnitureRow row)
    {
        if (row.Entity is not { } e)
            return;
        bool hazard = Hazard(row) is not null || (row.Went && row.Kind == FurnitureKind.Explosive);
        row.Entity = null;
        e.Park();
        parking.Park(e.Handle);
        _free.Push(e);
        if (hazard)
            hazards.Each(nameof(IFurnitureHazardHook.HazardGone), h => h.HazardGone(row.Index));
    }

    /// <summary>The radius a standing hazard threatens: an explosive not gone off, a trap not spent; null for the rest.</summary>
    private float? Hazard(FurnitureRow row) => row.Kind switch
    {
        FurnitureKind.Explosive when !row.Went => (float)rules.Content.Furniture.Explosive(row.Variant).Radius,
        FurnitureKind.Trap when row.Trap!.Phase != TrapPhase.Spent => (float)Math.Max(rules.Content.Furniture.Trap(row.Variant).Radius,
                                                                                       rules.Content.Furniture.Trap(row.Variant).Trigger),
        _ => null,
    };

    /// <summary>How the row looks now: its catalog entry, its standing or spent model, solidity, use, health and flags.</summary>
    public FurnitureLook Look(FurnitureRow row)
    {
        var entry = FurnitureCatalog.Of(FurnitureCatalog.Number(row.KindId, row.Variant));
        byte number = FurnitureCatalog.Number(row.KindId, row.Variant);
        string standing = entry?.Model ?? FurnitureCatalog.CrateModel;
        string spent = entry?.SpentModel ?? standing;
        var f = rules.Content.Furniture;
        switch (row.Kind)
        {
            case FurnitureKind.Shrine:
            {
                string model = row.Shrine!.Used ? spent : standing;
                return Make(model, 0, solid: true, usable: true, 0, row.Shrine.Used ? FurnitureFlags.Spent : FurnitureFlags.None, 255);
            }
            case FurnitureKind.Container:
            {
                var c = row.Container!;
                bool locked = !c.Opened && c.KeyId is { } key && !_keys.Contains(key);
                var flags = c.Opened ? FurnitureFlags.Opened : locked ? FurnitureFlags.Locked : FurnitureFlags.None;
                // A wooden supply crate breaks open to a hit as well as to +USE; a locked one takes none until its key is found.
                int health = !c.Opened && !locked && row.BreakAt is null && f.Breaks(c.Kind) ? 1 : 0;
                return Make(c.Opened ? spent : standing, 0, solid: true, usable: true, health, flags, 255);
            }
            case FurnitureKind.Dispenser:
            {
                double charge = row.Dispenser!.Charge;
                bool empty = charge <= 0;
                return Make(standing, empty ? 1 : 0, solid: true, usable: false, 0, empty ? FurnitureFlags.Spent : FurnitureFlags.None,
                    Fraction(charge, f.Dispenser.Charge));
            }
            case FurnitureKind.Explosive:
            {
                double health = row.Explosive!.Health;
                int engine = row.Went || row.ExplodeAt is not null ? 0 : Math.Max(1, (int)Math.Ceiling(health));
                return Make(standing, 0, solid: true, usable: false, engine, FurnitureFlags.None, Fraction(health, f.Explosive(row.Variant).Health));
            }
            default:
            {
                var flags = row.Trap!.Phase switch
                {
                    TrapPhase.Tripped => FurnitureFlags.Tripped,
                    TrapPhase.Rearming => FurnitureFlags.Rearming,
                    TrapPhase.Spent => FurnitureFlags.Spent,
                    _ => FurnitureFlags.None,
                };
                return Make(standing, 0, solid: false, usable: false, 0, flags, 255);
            }
        }

        FurnitureLook Make(string model, int skin, bool solid, bool usable, int health, FurnitureFlags flags, byte left)
        {
            var (mins, maxs, lift) = FurnitureModels.Of(model);
            return new FurnitureLook(number, model, skin, lift, solid, mins, maxs, usable, health, flags, left);
        }
    }

    private static byte Fraction(double value, double max) => (byte)Math.Clamp((int)Math.Ceiling(255 * (max > 0 ? value / max : 0)), 0, 255);

    /// <summary>The row's entity takes its look again after a change of state.</summary>
    private void Restyle(FurnitureRow row)
    {
        if (row.Entity is { } e)
            e.Show(Look(row));
    }

    //-------------------------------------------------------------------------
    // Use
    //-------------------------------------------------------------------------

    /// <summary>A +USE of <paramref name="e"/> by <paramref name="activator"/>.</summary>
    internal void Use(FurnitureEntity e, EntityHandle activator)
    {
        if (e.Row < 0 || e.Row >= _rows.Count || _rows[e.Row].Entity != e || heroes.Of(activator) is not { } hero)
            return;
        _ = UseAs(_rows[e.Row], hero);
    }

    /// <summary>What a use did.</summary>
    public enum UseOutcome
    {
        /// <summary>A shrine's effect taken, a container opened.</summary>
        Used,

        /// <summary>The rules refused (a spent shrine, an opened or locked container): the hero was told.</summary>
        Refused,

        /// <summary>Nothing answers use there (a Dispenser, an explosive, a trap).</summary>
        NotUsable,

        /// <summary>The row is data (its room sleeps) or gone.</summary>
        NotThere,

        /// <summary>The level ended while the ledger answered.</summary>
        LevelEnded,
    }

    /// <summary>
    /// <paramref name="hero"/> uses <paramref name="row"/>, as +USE does: a shrine or a container on
    /// its entity. Completes when the ledger has answered a container's drops.
    /// </summary>
    public async Task<UseOutcome> UseAs(FurnitureRow row, FurnitureHero hero)
    {
        if (!row.OnEntity)
            return UseOutcome.NotThere;
        switch (row.Kind)
        {
            case FurnitureKind.Shrine:
                return UseShrine(row, hero).Ok ? UseOutcome.Used : UseOutcome.Refused;
            case FurnitureKind.Container:
                return await OpenContainer(row, hero);
            default:
                log.Msg($"[TF2Descent/Furniture] #{row.Index} ({row.Variant}) answers no use\n");
                return UseOutcome.NotUsable;
        }
    }

    //-------------------------------------------------------------------------
    // Shrines
    //-------------------------------------------------------------------------

    /// <summary>
    /// The rules' use (<see cref="ShrineRules.Use"/>): each taker carries a level-long effect on his
    /// stats (<see cref="FurnitureContributions"/>), a refill fills his life and mana, a sight shrine
    /// reveals the map; each taker is told the shrine's message; a used shrine looks spent and says so
    /// when used again.
    /// </summary>
    public ShrineUse UseShrine(FurnitureRow row, FurnitureHero user)
    {
        // The party the shrine's "party" means: the user's (WO9), every member here; a stranger takes nothing of it.
        var living = heroes.Living().Where(h => roster.SameParty(user.Slot, h.Slot)).ToList();
        var slots = living.ToDictionary(h => h.Key, h => h.Slot, StringComparer.Ordinal);
        var use = rules.Furniture.Shrines.Use(row.Shrine!, user.Key, [.. living.Select(h => h.Key)],
            key => slots.TryGetValue(key, out int slot) ? heroes.SpellsKnown(slot) : [], Seed);
        if (!use.Ok)
        {
            messages.Notice(user.Slot, ShrineSpentToken);
            log.Msg($"[TF2Descent/Furniture] shrine #{row.Index} ({row.Placed.Marker}) used by player {user.Slot}: refused, {use.Refused}\n");
            return use;
        }

        row.Shrine = use.Shrine;
        foreach (string taker in use.Takers)
        {
            buffs.Set(taker, ShrineRules.Take(buffs.Of(taker), use, taker));
            if (!slots.TryGetValue(taker, out int slot))
                continue;
            if (use.Effect.Refill)
                heroes.Refill(slot);
            heroes.Recompute(slot);
            messages.Notice(slot, use.MessageToken);
            if (use.Effect.RevealMap)
                automap.RevealAll(slot);   // the sight shrine: every room, on each taker's own map
        }

        automap.ShrineUsed(row.Placed.Marker);
        if (use.Effect.RevealMap)
            MapRevealed = true;
        if (row.Entity is { } e)
            world.Sound(e.Handle, FurnitureSounds.ShrineUse);
        Restyle(row);
        string stats = string.Join(", ", use.Effect.Stats.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Amount:+0.##;-0.##} {s.Stat}")));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] shrine #{row.Index} ({row.Placed.Marker}, {use.Effect.Id}) used by player {user.Slot}: takers [{string.Join(", ", use.Takers)}], " +
            $"{use.Effect.Lasts.ToString().ToLowerInvariant()}{(stats.Length > 0 ? " " + stats : "")}{(use.Effect.Refill ? " refill" : "")}{(use.Effect.RevealMap ? " map revealed" : "")}" +
            $"{(use.Spells.Count > 0 ? " spells [" + string.Join(", ", use.Spells.Select(p => $"{p.Key} +{p.Value.Ranks} {p.Value.Spell}")) + "]" : "")}\n"));
        return use;
    }

    //-------------------------------------------------------------------------
    // Containers
    //-------------------------------------------------------------------------

    /// <summary>
    /// The rules' opening (<see cref="ContainerRules.Open{TLoot}"/>) for the heroes in the container's
    /// room (the opener first; at most the floor's seats), each roll the rules' own
    /// (<c>LootRules.Container</c>), each item or potion asked of the ledger
    /// (<see cref="IItemLedger.ContainerDrop"/>) and the lot put on the loot floor at the container's
    /// foot, a seat a hero. A locked one needs the key the party found; a trapped one fires its trap
    /// at the opener.
    /// </summary>
    public async Task<UseOutcome> OpenContainer(FurnitureRow row, FurnitureHero opener, bool broken = false)
    {
        var members = Members(row, opener.Slot);
        int depth = Depth;
        var open = rules.Furniture.Containers.Open(row.Container!, Seed, members.Select(m => m.Key), _keys,
            request => (Request: request, Loot: rules.Loot.Container(request, depth)));
        if (!open.Ok)
        {
            messages.Notice(opener.Slot, open.Refused == ContainerRefusal.Locked ? LockedToken : OpenedToken);
            log.Msg($"[TF2Descent/Furniture] container #{row.Index} ({row.Placed.Marker}) opened by player {opener.Slot}: refused, {open.Refused}\n");
            return UseOutcome.Refused;
        }

        row.Container = open.Container;
        if (open.KeyUsed is { } key)
            _keys.Remove(key);
        if (row.Entity is { } e)
        {
            world.Sound(e.Handle, FurnitureSounds.ContainerOpen);
            if (broken)
            {
                // TF2's wood break, of the opened body the entity wears from now on (its splinters): the
                // standing crate's own break holds that body as a piece, which would stand a second one.
                breaks.Shatter(Look(row).Model, row.Position, row.Yaw, default);
                world.Sound(e.Handle, FurnitureSounds.CrateBreak);
            }
        }

        Restyle(row);
        if (open.TrapFired is { } trap)
            Fire(row, rules.Furniture.Traps.Fire(trap), row.Position, row.Entity?.Handle ?? EntityHandle.None, opener);

        var place = Foot(row);
        var drop = await DropShares(row, members, open.Loot.ToDictionary(p => p.Key, p => p.Value), depth, place, (uint)row.Index);
        if (drop is not { } placed)
            return UseOutcome.LevelEnded;
        string rolls = string.Join(", ", open.Loot.Select(p => $"{p.Key} {Say(p.Value.Loot)}"));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] container #{row.Index} ({row.Placed.Marker}, {row.Variant}) {(broken ? "broken open" : "opened")} by player {opener.Slot}: rolls [{rolls}]" +
            $"{(open.TrapFired is { } t ? ", trap " + t : "")}{(open.KeyUsed is { } k ? ", key " + k : "")}, " +
            $"{(placed.Drop is { } d ? $"drop {d.Number} at ({place.X:0} {place.Y:0} {place.Z:0})" : "nothing on the floor")}\n"));
        return UseOutcome.Used;
    }

    /// <summary>The living heroes a container or a break of <paramref name="row"/> rolls for: the one who did it first, then those in its room, at most the floor's seats.</summary>
    private List<FurnitureHero> Members(FurnitureRow row, int first) =>
        heroes.Living()
            .Where(h => h.Slot == first || rooms.RoomAt(h.Origin) == row.Room)
            .OrderBy(h => h.Slot != first).ThenBy(h => h.Slot)
            .Take(LootFloor.Seats).ToList();

    /// <summary>
    /// Each member's roll asked of the ledger (<see cref="IItemLedger.ContainerDrop"/>: an item of its
    /// tier, a potion) and the lot put on the loot floor at <paramref name="place"/>, a seat a hero, with
    /// his own Australium (none when hosted: the ledger pays it). Null when the level ended while the
    /// ledger answered; else the floor's drop (null when nothing lay on the floor). A container's
    /// number (<paramref name="seq"/>, its row's index) goes with each ask: hosted, the host rolls each
    /// member's share itself, once per container and member, even when the game's roll gave nothing.
    /// </summary>
    private async Task<Dropped?> DropShares(FurnitureRow row, IReadOnlyList<FurnitureHero> members,
                                            IReadOnlyDictionary<string, (LootRequest Request, KillLoot Loot)> loot, int depth, Vector place,
                                            uint? seq = null)
    {
        int levelAt = Level;
        var asked = new List<(string Hero, Task<DropResult> Task)>();
        foreach (var (hero, (request, roll)) in loot)
            asked.Add((hero, ledger.ContainerDrop(new ContainerDrop(hero, row.Placed.Marker, request.Seed, roll.Item ? roll.Tier : -1,
                roll.Outcome == LootOutcome.Potion ? roll.Potion : null, depth, place) { Seq = seq })));

        var items = new Dictionary<string, FloorItem?>(StringComparer.Ordinal);
        foreach (var (hero, task) in asked)
        {
            var answer = await task;
            if (Level != levelAt)
                return null;
            if (!answer.Ok)
                log.Warning($"[TF2Descent/Furniture] {row.Kind.ToString().ToLowerInvariant()} #{row.Index}: the ledger refused {hero}'s drop: {answer.Refusal}\n");
            items[hero] = answer.Ok && answer.Items.Count > 0 ? answer.Items[0] : null;
        }

        bool hosted = ledger.Kind == LedgerKind.Hosted;
        var shares = members.Select(m => new LootShare(m.Key, m.Slot, items.GetValueOrDefault(m.Key),
            hosted || !loot.TryGetValue(m.Key, out var l) ? 0 : l.Loot.Australium)).ToList();
        return new Dropped(floor.Place(place, shares));
    }

    /// <summary>What a drop put on the floor (null: nothing lay there).</summary>
    private sealed record Dropped(FloorDrop? Drop);

    private static string Say(KillLoot l) => l.Outcome switch
    {
        LootOutcome.Item => string.Create(CultureInfo.InvariantCulture, $"item tier {l.Tier}"),
        LootOutcome.Potion => "potion " + l.Potion,
        _ => "nothing",
    } + (l.Australium > 0 ? string.Create(CultureInfo.InvariantCulture, $" + {l.Australium} Australium") : "");

    /// <summary>Where a container's drop lies: in front of it along its facing, on the floor.</summary>
    public Vector Foot(FurnitureRow row)
    {
        double yaw = row.Yaw * Math.PI / 180;
        var front = row.Position + new Vector((float)(Math.Cos(yaw) * FootDistance), (float)(Math.Sin(yaw) * FootDistance), 0f);
        return ground.TryGround(front, out var g) ? g : front;
    }

    /// <summary>The keys whose rooms a hero stands in are found, for the party (a key lies in its room as data: no entity).</summary>
    public int FindKeys()
    {
        int found = 0;
        foreach (var hero in heroes.Living())
        {
            int room = rooms.RoomAt(hero.Origin);
            if (room < 0)
                continue;
            string here = LevelAdapters.RoomKey(room);
            foreach (var row in _rows)
            {
                if (row.Container is not { KeyId: { } key, Opened: false } || row.Placed.KeyRoom != here || !_keys.Add(key))
                    continue;
                found++;
                Restyle(row);
                log.Msg($"[TF2Descent/Furniture] player {hero.Slot} found {key} in room {room}: container #{row.Index} can be opened\n");
                foreach (var h in heroes.Living())
                    messages.Notice(h.Slot, KeyFoundToken);
            }
        }

        return found;
    }

    //-------------------------------------------------------------------------
    // The Dispenser
    //-------------------------------------------------------------------------

    /// <summary>
    /// Each Dispenser with charge gives each hero in its reach the rules' draw for
    /// <paramref name="seconds"/> (<see cref="DispenserRules.Draw"/>: health first, then ammo), in
    /// whole points, a fraction owed kept until it makes one; its charge only falls.
    /// </summary>
    public void Dispense(double now, double seconds)
    {
        if (!(seconds > 0))
            return;
        var reach = rules.Content.Furniture.Dispenser.Radius;
        foreach (var row in _rows)
        {
            if (row.Kind != FurnitureKind.Dispenser || !row.OnEntity || row.Dispenser!.Charge <= 0)
                continue;
            foreach (var hero in heroes.Living())
            {
                if (!Near(row.Position, hero.Origin, (float)reach))
                    continue;
                var (life, max) = heroes.Life(hero.Player);
                int missingAmmo = heroes.MissingAmmo(hero.Player);
                if (life >= max && missingAmmo <= 0)
                    continue;
                var draw = rules.Furniture.Dispensers.Draw(row.Dispenser, seconds, Math.Max(0, max - life), missingAmmo);
                bool emptied = draw.After.Charge <= 0 && row.Dispenser.Charge > 0;
                row.Dispenser = draw.After;
                var owed = _owed.GetValueOrDefault((row.Index, hero.Slot));
                owed = (owed.Health + draw.Health, owed.Ammo + draw.Ammo);
                int healed = heroes.Heal(hero.Player, (int)Math.Floor(owed.Health));
                int given = heroes.GiveAmmo(hero.Player, (int)Math.Floor(owed.Ammo));
                owed = (owed.Health - Math.Floor(owed.Health), owed.Ammo - Math.Floor(owed.Ammo));
                _owed[(row.Index, hero.Slot)] = owed;
                if (now - _lastSaid.GetValueOrDefault((row.Index, hero.Slot), -10) >= 1 || emptied)
                {
                    _lastSaid[(row.Index, hero.Slot)] = now;
                    log.Msg(string.Create(CultureInfo.InvariantCulture,
                        $"[TF2Descent/Furniture] dispenser #{row.Index}: player {hero.Slot} drew {draw.Health:0.##} life and {draw.Ammo:0.##} ammo over {seconds:0.###} s " +
                        $"(healed {healed}, ammo {given}; life {life + healed}/{max}), charge {row.Dispenser.Charge:0.##}\n"));
                }

                if (emptied)
                {
                    Restyle(row);
                    log.Msg($"[TF2Descent/Furniture] dispenser #{row.Index} is spent\n");
                    break;
                }
            }
        }
    }

    /// <summary>Whether <paramref name="at"/> is within <paramref name="reach"/> of <paramref name="from"/> across the floor and within <see cref="Reach"/> up or down.</summary>
    internal static bool Near(Vector from, Vector at, float reach)
    {
        float dx = at.X - from.X, dy = at.Y - from.Y;
        return dx * dx + dy * dy <= reach * reach && Math.Abs(at.Z - from.Z) <= Reach;
    }

    //-------------------------------------------------------------------------
    // Explosives
    //-------------------------------------------------------------------------

    /// <summary>
    /// A hit on an explosive's entity (its <see cref="FurnitureEntity.OnDamagedAt"/>, after the
    /// damage rules): the rules take its health (<see cref="ExplosiveRules.Hit"/>); the hit that sets
    /// it off has it go off at the next think, set off by the hero who struck (none for a robot or
    /// the world). One already set off takes nothing. Answers the engine health the hit takes away.
    /// </summary>
    internal int Hurt(FurnitureEntity e, EntityHandle attacker, float damage)
    {
        if (e.Row < 0 || e.Row >= _rows.Count || _rows[e.Row] is not { } struck || struck.Entity != e)
            return 0;
        if (struck.Kind == FurnitureKind.Container)
        {
            int taken = Strike(struck, heroes.Of(attacker)?.Player ?? EntityHandle.None, damage) ? e.EngineHealth : 0;
            e.Took(taken);
            return taken;
        }

        if (struck is not { Kind: FurnitureKind.Explosive } row)
            return 0;
        int healthAfter = HurtCore(row, heroes.Of(attacker)?.Player ?? EntityHandle.None, damage);
        int answer = Math.Max(0, e.EngineHealth - healthAfter);
        e.Took(answer);
        return answer;
    }

    /// <summary>
    /// <paramref name="damage"/> on the explosive <paramref name="row"/> by <paramref name="hero"/>
    /// (his player, or none), by the path an entity's hit takes, with no engine hit behind it (the
    /// dev command): its entity's health follows. Answers the engine health it leaves.
    /// </summary>
    public int HurtRow(FurnitureRow row, EntityHandle hero, float damage)
    {
        int left = HurtCore(row, hero, damage);
        Restyle(row);
        return left;
    }

    private int HurtCore(FurnitureRow row, EntityHandle hero, float damage)
    {
        if (row.Kind != FurnitureKind.Explosive || row.Went || row.ExplodeAt is not null)
            return 0;
        var hit = rules.Furniture.Explosives.Hit(row.Explosive!, damage);
        row.Explosive = hit.After;
        if (hit.SetOff)
        {
            row.ExplodeAt = world.Now;
            row.SetOffBy = hero;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Furniture] {row.Variant} #{row.Index} ({row.Placed.Marker}) set off by {(hero != EntityHandle.None ? "player " + hero.EntryIndex : "no hero")} ({damage:0.##} damage)\n"));
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(hit.After.Health));
    }

    /// <summary>Every set-off explosive whose time has come goes off.</summary>
    private void Explosions(double now)
    {
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i] is { ExplodeAt: { } at } row && at <= now)
                Explode(row, now);
    }

    /// <summary>
    /// <paramref name="row"/> goes off: every standing explosive within its chain radius is set off to
    /// go its chain delay later (by the same hero); TF2's blast hurts what is not a player, credited
    /// to the hero who set it off (a robot it kills is his kill), with the explosive as inflictor;
    /// each hero within its radius in sight of it takes the rules' damage at his distance through the
    /// damage rules (his resistances apply), the explosive as attacker. Its entity parks for good.
    /// </summary>
    public void Explode(FurnitureRow row, double now)
    {
        if (row.Kind != FurnitureKind.Explosive || row.Went)
            return;
        var x = rules.Content.Furniture.Explosive(row.Variant);
        row.Went = true;
        row.ExplodeAt = null;
        row.Explosive = row.Explosive! with { Health = 0, Exploded = true };
        EntityHandle self = row.Entity?.Handle ?? EntityHandle.None;
        EntityHandle setter = row.SetOffBy;

        var centre = row.Position + new Vector(0f, 0f, BlastLift);
        if (self != EntityHandle.None)
            world.Sound(self, FurnitureSounds.ExplodeOf(row.Variant));

        // Parked first: TF2's blast traces from its centre to each victim passing only the
        // ignored hero, so a barrel still solid would stop its own blast.
        bool stood = row.Entity is not null;
        if (stood)
            Fold(row);

        var (hit, chain) = GoOff(row, row.Variant, centre, self, setter, now);

        if (!stood)   // the fold told the hazard chain for one that stood
            hazards.Each(nameof(IFurnitureHazardHook.HazardGone), h => h.HazardGone(row.Index));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] {row.Variant} #{row.Index} ({row.Placed.Marker}) went off at ({centre.X:0} {centre.Y:0} {centre.Z:0}), set off by " +
            $"{(setter != EntityHandle.None ? "player " + setter.EntryIndex : "no hero")}: blast {x.Damage:0.##} {x.DamageType} over {x.Radius:0}, " +
            $"heroes [{string.Join(", ", hit)}], chain [{string.Join(", ", chain.Select(c => $"#{c.Index} in {x.ChainDelay:0.##} s"))}]\n"));
    }

    /// <summary>
    /// A blast of the explosive row <paramref name="explosive"/> at <paramref name="centre"/>, from
    /// <paramref name="row"/>: every standing explosive within its chain radius is set off to go its
    /// chain delay later (by the same hero); its look; TF2's blast on what is not a player, credited to
    /// <paramref name="setter"/> (else <paramref name="self"/>); and each hero within its radius in sight
    /// of it takes the rules' damage at his distance, <paramref name="self"/> as attacker. Answers the
    /// heroes hit and the chain.
    /// </summary>
    private (List<string> Heroes, List<FurnitureRow> Chain) GoOff(FurnitureRow row, string explosive, Vector centre, EntityHandle self, EntityHandle setter,
                                                                  double now)
    {
        var x = rules.Content.Furniture.Explosive(explosive);
        var chain = new List<FurnitureRow>();
        foreach (var other in _rows)
        {
            if (other == row || other.Kind != FurnitureKind.Explosive || other.Went || other.ExplodeAt is not null
                || Distance(row.Position, other.Position) > x.ChainRadius)
                continue;
            other.ExplodeAt = now + x.ChainDelay;
            other.SetOffBy = setter;
            chain.Add(other);
        }

        world.Explosion(centre, (float)x.Radius);
        EntityHandle attacker = setter != EntityHandle.None ? setter : self;
        if (attacker != EntityHandle.None)
            world.Blast(centre, (float)x.Damage, (float)x.Radius, attacker, self != EntityHandle.None ? self : attacker, setter);

        var hit = new List<string>();
        foreach (var hero in heroes.Living())
        {
            double d = Distance(centre, hero.Centre);
            double amount = rules.Furniture.Explosives.DamageAt(explosive, d);
            if (amount <= 0 || !world.Clear(centre, hero.Centre))
                continue;
            world.Hurt(hero.Player, self, (float)amount, DamageBits(x.DamageType), centre);
            hit.Add(string.Create(CultureInfo.InvariantCulture, $"player {hero.Slot} {amount:0.##} at {d:0}"));
        }

        return (hit, chain);
    }

    internal static double Distance(Vector a, Vector b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>The engine damage bits of a rules damage type (fire burns, a shock shocks).</summary>
    public static DamageType DamageBits(string type) => type switch
    {
        "fire" => DamageType.Burn,
        "blast" => DamageType.Blast,
        "shock" => DamageType.Shock,
        _ => DamageType.Bullet,
    };

    //-------------------------------------------------------------------------
    // Crates and barrels
    //-------------------------------------------------------------------------

    /// <summary>
    /// A hit of <paramref name="damage"/> on <paramref name="row"/> by <paramref name="hero"/> (his
    /// player; none for a robot or the world), standing for it <paramref name="source"/>: a standing
    /// crate or barrel stops a hit and a step no more at once and breaks at the next think, ONE roll
    /// however often it is struck; a shut supply crate a hero struck (not locked) breaks open at the
    /// next think as +USE opens it. <paramref name="force"/> is the hit's push (a damage force), which
    /// the pieces leave with. Answers whether the hit broke something.
    /// </summary>
    public bool Strike(FurnitureRow row, EntityHandle hero, float damage, EntityHandle source = default, Vector force = default)
    {
        if (!(damage > 0) || row.Gone)
            return false;
        switch (row.Kind)
        {
            case FurnitureKind.Breakable:
                break;
            case FurnitureKind.Container when row is { OnEntity: true, Container: { Opened: false } c } && rules.Content.Furniture.Breaks(c.Kind)
                                               && hero != EntityHandle.None && (c.KeyId is not { } key || _keys.Contains(key)):
                source = row.Entity!.Handle;
                break;
            default:
                return false;
        }

        row.BreakAt = world.Now;
        row.BrokenBy = hero;
        row.BreakSource = source;
        row.BreakForce = force;
        return true;
    }

    /// <summary>The outcome the next break rolls instead of its own (the dev command); null for its own roll.</summary>
    public void ForceNext(BreakOutcome? outcome) => _forced = outcome;

    /// <summary>Every struck crate, barrel and supply crate whose time has come breaks.</summary>
    private void Breaks(double now)
    {
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i] is { BreakAt: { } at } row && at <= now)
                _ = Break(row);
    }

    /// <summary>
    /// <paramref name="row"/>, struck (<see cref="Strike"/>), breaks: a supply crate breaks open for
    /// its striker as +USE opens it; a crate or barrel plays TF2's wood break and rolls once
    /// (<see cref="BreakRules.Roll"/>, from the level seed and its marker; the dev command's outcome
    /// instead when one is set) and carries the outcome out: each player's share on the floor where it
    /// stood (an Australium pile, a potion of the band, an item: R4), the furniture barrel's blast, the
    /// cloud's status on each hero in its radius, or an ambush hunting the hero who broke it. Answers
    /// the outcome (null for a supply crate, or one already broken).
    /// </summary>
    public async Task<BreakOutcome?> Break(FurnitureRow row)
    {
        if (row.BreakAt is null)
            return null;
        row.BreakAt = null;
        if (row.Kind == FurnitureKind.Container)
        {
            if (heroes.Of(row.BrokenBy) is { } opener)
                await OpenContainer(row, opener, broken: true);
            else
                Restyle(row);
            return null;
        }

        if (row.Kind != FurnitureKind.Breakable || row.Went)
            return null;
        row.Went = true;
        Broken++;
        bool forced = _forced is not null;
        // Hosted, a crate or barrel is numbered by its slot (SDK 2.8 Break) and its outcome keyed on the instance,
        // that number and the depth, as the host's BreakableDrops keys it; unhosted on the level seed and its marker.
        uint? hostedSeq = ledger.Kind == LedgerKind.Hosted && row.BreakableSlot is int slot ? (uint)slot : null;
        var outcome = _forced ?? (hostedSeq is { } seq ? rules.Furniture.Breaks.Hosted(Seed, seq, Depth) : rules.Furniture.Breaks.Roll(Seed, row.Placed.Marker).Outcome);
        _forced = null;
        row.Broke = outcome;

        var b = rules.Content.Furniture.Breakables;
        var by = heroes.Of(row.BrokenBy);
        var self = row.BreakSource;
        var centre = row.Position + new Vector(0f, 0f, BlastLift);
        // What it breaks as (its spent model: the crate's broken body, whose pieces are splinters), where
        // it stood, the pieces leaving with the hit's push over its mass as a prop_physics' would.
        var entry = FurnitureCatalog.Of(FurnitureCatalog.Number(FurnitureKindId.Breakable, row.Variant));
        string standing = entry?.Model ?? FurnitureCatalog.CrateModel;
        breaks.Shatter(entry?.SpentModel ?? standing, row.Position + new Vector(0f, 0f, FurnitureModels.Of(standing).Lift), row.Yaw,
                       row.BreakForce * (1f / FurnitureModels.Mass(standing)));
        if (self != EntityHandle.None)
            world.Sound(self, FurnitureSounds.BreakOf(row.Variant));

        string what;
        string? onHost = null;
        if (hostedSeq is { } numbered)
        {
            // R4: every hero in the room breaks it for himself, the breaker first, whatever the outcome (the host's roll).
            onHost = await BreakOnHost(row, by?.Slot ?? -1, numbered);
            if (onHost is null)
                return outcome;
        }

        switch (outcome)
        {
            case BreakOutcome.Australium or BreakOutcome.Potion or BreakOutcome.Item when onHost is not null:
                what = onHost;
                break;
            case BreakOutcome.Australium or BreakOutcome.Potion or BreakOutcome.Item:
            {
                var members = Members(row, by?.Slot ?? -1);
                var loot = new Dictionary<string, (LootRequest Request, KillLoot Loot)>(StringComparer.Ordinal);
                foreach (var m in members)
                {
                    var request = rules.Furniture.Breaks.Request(Seed, row.Placed.Marker, m.Key);
                    loot[m.Key] = (request, rules.Loot.Breakable(request, outcome, Depth));
                }

                var place = ground.TryGround(row.Position + new Vector(0f, 0f, 8f), out var g) ? g : row.Position;
                int levelAt = Level;
                var drop = await DropShares(row, members, loot, Depth, place);
                if (drop is null || Level != levelAt)
                    return outcome;
                what = $"shares [{string.Join(", ", loot.Select(p => $"{p.Key} {Say(p.Value.Loot)}"))}], " +
                       (drop.Drop is { } d ? string.Create(CultureInfo.InvariantCulture, $"drop {d.Number} at ({place.X:0} {place.Y:0} {place.Z:0})") : "nothing on the floor");
                break;
            }
            case BreakOutcome.Blast:
            {
                if (self != EntityHandle.None)
                    world.Sound(self, FurnitureSounds.ExplodeOf(b.Blast));
                var x = rules.Content.Furniture.Explosive(b.Blast);
                var (hit, chain) = GoOff(row, b.Blast, centre, self, row.BrokenBy, world.Now);
                what = string.Create(CultureInfo.InvariantCulture,
                    $"blast {x.Damage:0.##} {x.DamageType} over {x.Radius:0}, heroes [{string.Join(", ", hit)}], chain [{string.Join(", ", chain.Select(c => "#" + c.Index))}]");
                break;
            }
            case BreakOutcome.Cloud:
            {
                if (self != EntityHandle.None)
                    world.Sound(self, EngineFurnitureBreakWorld.CloudSound);
                breaks.Cloud(centre);
                var marked = new List<string>();
                foreach (var hero in heroes.Living())
                {
                    if (Distance(centre, hero.Centre) > b.Cloud.Radius || !world.Clear(centre, hero.Centre))
                        continue;
                    bool took = breaks.Mark(hero.Player, b.Cloud.Condition, b.Cloud.Seconds, self);
                    marked.Add($"player {hero.Slot}{(took ? "" : " refused")}");
                }

                what = string.Create(CultureInfo.InvariantCulture, $"cloud {b.Cloud.Condition} {b.Cloud.Seconds:0.##} s over {b.Cloud.Radius:0}: [{string.Join(", ", marked)}]");
                break;
            }
            case BreakOutcome.Ambush:
            {
                var templates = rules.Furniture.Breaks.Ambush(Seed, row.Placed.Marker, Depth);
                var lines = breaks.Ambush(templates, row.Position, row.Yaw, by?.Player ?? EntityHandle.None,
                    Seeded.From(Seed, "break_ambush_spawn", row.Placed.Marker).NextULong(), b.Ambush.HuntSeconds);
                what = $"ambush of {templates.Length} [{string.Join("; ", lines)}]";
                break;
            }
            default:
                what = "splinters";
                break;
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] {row.Variant} #{row.Index} ({row.Placed.Marker}) broken by {(by is { } h ? "player " + h.Slot : "no hero")} at ({row.Position.X:0} {row.Position.Y:0} {row.Position.Z:0}): " +
            $"roll {outcome.ToString().ToLowerInvariant()}{(forced ? " (forced)" : "")}: {what}" +
            $"{(onHost is not null && !BreakableSettings.PerPlayer(outcome) ? "; " + onHost : "")}\n"));
        return outcome;
    }

    /// <summary>
    /// A numbered crate or barrel broken on the host (SDK 2.8 <c>Break</c>): once for each hero
    /// <see cref="Members"/> names (the breaker first, then the room's), each with his own lease and
    /// roll; each one's Australium is credited to him at once and his items lie on the floor on his own
    /// seat. Answers what each got, for the break's line; null when the level ended meanwhile.
    /// </summary>
    private async Task<string?> BreakOnHost(FurnitureRow row, int first, uint seq)
    {
        int levelAt = Level;
        var place = ground.TryGround(row.Position + new Vector(0f, 0f, 8f), out var g) ? g : row.Position;
        var asked = Members(row, first).Select(m => (Hero: m, Answer: ledger.Break(new BreakDrop(m.Key, seq, Seed, Depth, place)))).ToList();
        var shares = new List<LootShare>();
        var said = new List<string>();
        foreach (var (m, task) in asked)
        {
            var answer = await task;
            if (Level != levelAt)
                return null;
            if (!answer.Ok)
            {
                said.Add($"{m.Key} refused: {answer.Refusal}");
                continue;
            }
            var item = answer.Items.Count > 0 ? answer.Items[0] : null;
            shares.Add(new LootShare(m.Key, m.Slot, item, 0));
            said.Add(string.Create(CultureInfo.InvariantCulture,
                $"{m.Key} {(item is { } i ? i.Item.BaseId : "no item")}{(answer.Australium > 0 ? $", {answer.Australium} Australium credited" : "")}"));
        }

        var drop = floor.Place(place, shares);
        return string.Create(CultureInfo.InvariantCulture,
            $"hosted breakable {seq}, each hero's own [{string.Join("; ", said)}], {(drop is { } d ? $"drop {d.Number} at ({place.X:0} {place.Y:0} {place.Z:0})" : "nothing on the floor")}");
    }

    //-------------------------------------------------------------------------
    // Traps
    //-------------------------------------------------------------------------

    /// <summary>
    /// Each armed trap of an awake room is tripped by a hero whose feet are within its trigger area
    /// (or a robot body, when its row says robots trip it): its tell sounds, and the rules' clock
    /// (<see cref="TrapRules.Tick"/>) fires it when its fuse runs and re-arms it, or leaves it spent.
    /// </summary>
    private void Traps(double now)
    {
        foreach (var row in _rows)
        {
            if (row.Kind != FurnitureKind.Trap)
                continue;
            var trapRow = rules.Content.Furniture.Trap(row.Variant);
            var state = row.Trap!;
            if (state.Phase == TrapPhase.Armed && row.OnEntity)
                state = TripAt(row, state, trapRow, now);

            var tick = rules.Furniture.Traps.Tick(state, now);
            bool changed = !ReferenceEquals(tick.After, row.Trap);
            row.Trap = tick.After;
            if (tick.Fired is { } fire)
            {
                Fire(row, fire, row.Position, row.Entity?.Handle ?? EntityHandle.None);
                if (tick.After.Phase == TrapPhase.Spent)
                    hazards.Each(nameof(IFurnitureHazardHook.HazardGone), h => h.HazardGone(row.Index));
            }
            else if (state.Phase == TrapPhase.Rearming && tick.After.Phase == TrapPhase.Armed)
            {
                log.Msg($"[TF2Descent/Furniture] trap #{row.Index} ({row.Variant}) is armed again\n");
            }

            if (changed)
                Restyle(row);
        }
    }

    private TrapState TripAt(FurnitureRow row, TrapState state, TrapRow trapRow, double now)
    {
        string? by = null;
        foreach (var hero in heroes.Living())
        {
            if (Near(row.Position, hero.Origin, (float)trapRow.Trigger))
            {
                by = "player " + hero.Slot.ToString(CultureInfo.InvariantCulture);
                state = rules.Furniture.Traps.Trip(state, now, robot: false);
                break;
            }
        }

        if (by is null && trapRow.Robots && world.RobotsNear(row.Position, (float)trapRow.Trigger).Count > 0)
        {
            by = "a robot";
            state = rules.Furniture.Traps.Trip(state, now, robot: true);
        }

        if (by is not null && state.Phase == TrapPhase.Tripped)
        {
            if (row.Entity is { } e)
                world.Sound(e.Handle, FurnitureSounds.TellOf(row.Variant));
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Furniture] trap #{row.Index} ({row.Variant}, {trapRow.Tell.Kind} tell {trapRow.Tell.Token}) tripped by {by}: fires in {trapRow.Fuse:0.##} s\n"));
        }

        return state;
    }

    /// <summary>
    /// A trap's fire at <paramref name="at"/>: the rules' damage (<see cref="TrapFire"/>) to every hero
    /// and robot body within its radius, or within its trigger area when its radius is 0, through the
    /// damage rules (a hero's resistances apply), the trap as attacker. A container's trap of radius 0
    /// hits <paramref name="opener"/> alone: he stands at its foot, outside any trigger area.
    /// </summary>
    public void Fire(FurnitureRow row, TrapFire fire, Vector at, EntityHandle attacker, FurnitureHero? opener = null)
    {
        var trapRow = rules.Content.Furniture.Trap(fire.Trap);
        bool openerAlone = opener is not null && fire.Radius <= 0;
        float area = (float)(fire.Radius > 0 ? fire.Radius : trapRow.Trigger);
        var bits = DamageBits(fire.DamageType);
        var hit = new List<string>();
        foreach (var hero in heroes.Living())
        {
            if (openerAlone ? hero.Slot != opener!.Slot : !Near(at, hero.Origin, area))
                continue;
            world.Hurt(hero.Player, attacker, (float)fire.Damage, bits, at);
            hit.Add("player " + hero.Slot.ToString(CultureInfo.InvariantCulture));
        }

        int robots = 0;
        foreach (var robot in openerAlone ? [] : world.RobotsNear(at, area))
        {
            world.Hurt(robot, attacker, (float)fire.Damage, bits, at);
            robots++;
        }

        if (attacker != EntityHandle.None)
            world.Sound(attacker, FurnitureSounds.FireOf(fire.Trap));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] trap {fire.Trap} of #{row.Index} fired: {fire.Damage:0.##} {fire.DamageType} over {area:0} on [{string.Join(", ", hit)}] and {robots} robots\n"));
    }

    //-------------------------------------------------------------------------
    // Reading
    //-------------------------------------------------------------------------

    /// <summary>The furniture as lines: each row (or those of one room), its place, room, awake or asleep, entity or data, state; then the pool.</summary>
    public string Report(int room = -1)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] {_rows.Count} furniture (seed {Seed}, depth {Depth}), {_rows.Count(r => r.OnEntity)} on entities, pool {_pool.Count} (ceiling {PoolCeiling}), free {_free.Count}, keys [{string.Join(", ", _keys)}]{(MapRevealed ? ", map revealed" : "")}\n");
        foreach (var r in _rows)
        {
            if (room >= 0 && r.Room != room)
                continue;
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Furniture]   #{r.Index} {r.Kind.ToString().ToLowerInvariant()} {r.Variant} on {r.Placed.Marker} at ({r.Position.X:0} {r.Position.Y:0} {r.Position.Z:0}) yaw {r.Yaw:0} room {r.Room} " +
                $"{(rooms.Awake(r.Room) ? "awake" : "asleep")} {(r.Entity is { } e ? "entity #" + e.Handle.EntryIndex : "data")}: {r.StateText()}\n");
        }

        return sb.ToString();
    }
}
