using System.Collections.Immutable;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Furniture;

//-----------------------------------------------------------------------------
// Containers
//-----------------------------------------------------------------------------

/// <summary>A container on the level: placed at load, opened once.</summary>
public sealed record ContainerState(string Marker, string Kind, string? KeyId, string? Trap, bool Opened = false)
{
    public static ContainerState From(Placed p) =>
        p.Kind == FurnitureKind.Container
            ? new ContainerState(p.Marker, p.Variant, p.KeyId, p.Trap)
            : throw new ArgumentException($"marker '{p.Marker}' holds a {p.Kind}, not a container", nameof(p));
}

/// <summary>What the loot hook is asked for: one roll of <paramref name="LootTable"/> for <paramref name="Player"/>, from <paramref name="Seed"/>.</summary>
public sealed record LootRequest(string LootTable, string Container, string Player, ulong Seed);

public enum ContainerRefusal
{
    AlreadyOpen,
    Locked,
}

/// <summary>
/// A container opened: the container after it, one loot roll per eligible player, the trap
/// it set off (if any), the key it took (if any), or why it stayed shut.
/// </summary>
public sealed record ContainerOpen<TLoot>(
    ContainerState Container,
    ImmutableSortedDictionary<string, TLoot> Loot,
    string? TrapFired,
    string? KeyUsed,
    ContainerRefusal? Refused = null)
{
    public bool Ok => Refused is null;
}

/// <summary>
/// Containers (plan_tf2_rpg 2l, R4): opened once, and every player in the room when it opens
/// gets a roll of their own through the loot hook (loot is another package's), once each,
/// never twice. A locked one needs its key; a trapped one sets its trap off.
/// </summary>
public sealed class ContainerRules
{
    private readonly DescentContent _content;

    public ContainerRules(DescentContent content) => _content = content;

    /// <summary>
    /// Opens <paramref name="container"/> for the players in its room
    /// (<paramref name="eligible"/>; a name listed twice still rolls once), with the keys the
    /// opener holds. Each roll's seed is the level seed, the container and the player.
    /// </summary>
    public ContainerOpen<TLoot> Open<TLoot>(ContainerState container, ulong levelSeed, IEnumerable<string> eligible,
        IReadOnlySet<string> keysHeld, Func<LootRequest, TLoot> roll)
    {
        var none = ImmutableSortedDictionary.Create<string, TLoot>(StringComparer.Ordinal);
        if (container.Opened) return new(container, none, null, null, ContainerRefusal.AlreadyOpen);
        if (container.KeyId is { } key && !keysHeld.Contains(key)) return new(container, none, null, null, ContainerRefusal.Locked);
        var loot = ImmutableSortedDictionary.CreateBuilder<string, TLoot>(StringComparer.Ordinal);
        string table = _content.Furniture.Containers.LootTable;
        foreach (var player in eligible.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            loot[player] = roll(new LootRequest(table, container.Marker, player, Seeded.From(levelSeed, "container", container.Marker, player).NextULong()));
        return new(container with { Opened = true }, loot.ToImmutable(), container.Trap, container.KeyId);
    }
}

//-----------------------------------------------------------------------------
// The Dispenser
//-----------------------------------------------------------------------------

/// <summary>A Dispenser's charge left: it only falls.</summary>
public sealed record DispenserState(double Charge);

/// <summary>What a Dispenser gave in one draw, and its state after.</summary>
public sealed record Dispensed(double Health, double Ammo, DispenserState After);

/// <summary>
/// The fountain (plan_tf2_rpg 2l): a working Dispenser that heals and gives ammo at the
/// table's rates from a finite charge that never refills. Health is paid for first.
/// </summary>
public sealed class DispenserRules
{
    private readonly DescentContent _content;

    public DispenserRules(DescentContent content) => _content = content;

    private DispenserSettings S => _content.Furniture.Dispenser;

    public DispenserState New() => new(S.Charge);

    /// <summary>A hero missing <paramref name="missingHealth"/> and <paramref name="missingAmmo"/> stands at it for <paramref name="seconds"/>.</summary>
    public Dispensed Draw(DispenserState state, double seconds, double missingHealth, double missingAmmo)
    {
        if (seconds < 0 || missingHealth < 0 || missingAmmo < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds), "a draw takes nothing negative");
        double charge = state.Charge;
        double health = Math.Min(Math.Min(missingHealth, S.HealPerSecond * seconds), charge / S.ChargePerHealth);
        charge -= health * S.ChargePerHealth;
        double ammo = Math.Min(Math.Min(missingAmmo, S.AmmoPerSecond * seconds), charge / S.ChargePerAmmo);
        charge -= ammo * S.ChargePerAmmo;
        return new Dispensed(health, ammo, new DispenserState(Math.Max(0, Math.Min(state.Charge, charge))));
    }
}

//-----------------------------------------------------------------------------
// Explosives
//-----------------------------------------------------------------------------

/// <summary>A point in the world, in game units.</summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    public double DistanceTo(Point3 o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y) + (Z - o.Z) * (Z - o.Z));
}

/// <summary>An explosive standing on the level: its marker, its row and where it is.</summary>
public sealed record ExplosiveAt(string Marker, string Kind, Point3 At);

/// <summary>Something a blast can hurt: a hero or a robot (both take it alike).</summary>
public sealed record BlastTarget(string Id, bool Hero, Point3 At);

/// <summary>One blast's damage to one target.</summary>
public sealed record BlastHit(string Explosive, string Target, string DamageType, double Damage);

/// <summary>A chain: the explosives that went off, in order, and every hit.</summary>
public sealed record Detonation(ImmutableArray<string> Exploded, ImmutableArray<BlastHit> Hits);

/// <summary>An explosive standing on the level: the damage it has left to take before it goes off, and whether it has.</summary>
public sealed record ExplosiveState(string Marker, string Kind, double Health, bool Exploded = false);

/// <summary>A hit on an explosive: the explosive after it, and whether this hit set it off.</summary>
public sealed record ExplosiveHit(ExplosiveState After, bool SetOff);

/// <summary>One explosive of a chain and when it goes off, in seconds after the first.</summary>
public sealed record ChainStep(string Marker, double After);

/// <summary>
/// Pumpkin bombs and barrels (plan_tf2_rpg 2l): an area burst of the row's damage type to
/// heroes and robots alike, full at the centre and falling linearly to the row's edge share
/// at its radius (stock TF's radius damage halves at the edge: edgePct 50), and nothing
/// beyond. Any explosive within the chain radius of one that goes off goes off next.
/// </summary>
public sealed class ExplosiveRules
{
    private readonly DescentContent _content;

    public ExplosiveRules(DescentContent content) => _content = content;

    /// <summary>An explosive of <paramref name="kind"/> as it stands at load: its row's health, not gone off.</summary>
    public ExplosiveState New(string marker, string kind) => new(marker, kind, _content.Furniture.Explosive(kind).Health);

    /// <summary>
    /// <paramref name="damage"/> on an explosive: its health falls by it, and at 0 or below it goes off.
    /// One that has gone off takes nothing more and is not set off again.
    /// </summary>
    public ExplosiveHit Hit(ExplosiveState state, double damage)
    {
        if (state.Exploded || !(damage > 0))
            return new ExplosiveHit(state, false);
        double health = state.Health - damage;
        bool off = health <= 0;
        return new ExplosiveHit(state with { Health = Math.Max(0, health), Exploded = off }, off);
    }

    /// <summary>
    /// When each explosive of <paramref name="first"/>'s chain goes off: the order
    /// <see cref="Detonate"/> gives, each one its setter-off's row's <see cref="ExplosiveRow.ChainDelay"/>
    /// after the one that set it off (the first at 0).
    /// </summary>
    public ImmutableArray<ChainStep> Schedule(string first, IReadOnlyList<ExplosiveAt> explosives)
    {
        var byId = explosives.ToDictionary(e => e.Marker, StringComparer.Ordinal);
        if (!byId.ContainsKey(first)) throw new ArgumentException($"'{first}' is not a standing explosive", nameof(first));
        var at = new Dictionary<string, double>(StringComparer.Ordinal) { [first] = 0 };
        var queue = new Queue<string>([first]);
        var steps = ImmutableArray.CreateBuilder<ChainStep>();
        while (queue.Count > 0)
        {
            var e = byId[queue.Dequeue()];
            var row = _content.Furniture.Explosive(e.Kind);
            steps.Add(new ChainStep(e.Marker, at[e.Marker]));
            foreach (var next in explosives
                         .Where(o => !at.ContainsKey(o.Marker) && e.At.DistanceTo(o.At) <= row.ChainRadius)
                         .OrderBy(o => e.At.DistanceTo(o.At)).ThenBy(o => o.Marker, StringComparer.Ordinal))
            {
                at[next.Marker] = at[e.Marker] + row.ChainDelay;
                queue.Enqueue(next.Marker);
            }
        }
        return steps.ToImmutable();
    }

    /// <summary>The damage <paramref name="kind"/> does at <paramref name="distance"/> from its centre.</summary>
    public double DamageAt(string kind, double distance)
    {
        var x = _content.Furniture.Explosive(kind);
        if (distance < 0 || distance > x.Radius) return 0;
        return x.Damage * (1 - distance / x.Radius * (1 - x.EdgePct / 100));
    }

    /// <summary>
    /// <paramref name="first"/> goes off among <paramref name="explosives"/> (the ones still
    /// standing), with <paramref name="targets"/> around: the chain in the order it goes off
    /// (breadth first, nearest first), and each blast's hit on every target in its radius.
    /// </summary>
    public Detonation Detonate(string first, IReadOnlyList<ExplosiveAt> explosives, IReadOnlyList<BlastTarget> targets)
    {
        var byId = explosives.ToDictionary(e => e.Marker, StringComparer.Ordinal);
        if (!byId.ContainsKey(first)) throw new ArgumentException($"'{first}' is not a standing explosive", nameof(first));
        var gone = new HashSet<string>(StringComparer.Ordinal) { first };
        var queue = new Queue<string>([first]);
        var order = ImmutableArray.CreateBuilder<string>();
        var hits = ImmutableArray.CreateBuilder<BlastHit>();
        while (queue.Count > 0)
        {
            var e = byId[queue.Dequeue()];
            var row = _content.Furniture.Explosive(e.Kind);
            order.Add(e.Marker);
            foreach (var t in targets)
            {
                double d = DamageAt(e.Kind, e.At.DistanceTo(t.At));
                if (d > 0) hits.Add(new BlastHit(e.Marker, t.Id, row.DamageType, d));
            }
            foreach (var next in explosives
                         .Where(o => !gone.Contains(o.Marker) && e.At.DistanceTo(o.At) <= row.ChainRadius)
                         .OrderBy(o => e.At.DistanceTo(o.At)).ThenBy(o => o.Marker, StringComparer.Ordinal))
            {
                gone.Add(next.Marker);
                queue.Enqueue(next.Marker);
            }
        }
        return new Detonation(order.ToImmutable(), hits.ToImmutable());
    }
}

//-----------------------------------------------------------------------------
// Traps
//-----------------------------------------------------------------------------

/// <summary>Where a trap is in its cycle.</summary>
public enum TrapPhase
{
    /// <summary>Waiting for a body to step in.</summary>
    Armed,

    /// <summary>Stepped on: its tell given, it fires when its fuse runs (<see cref="TrapState.Until"/>).</summary>
    Tripped,

    /// <summary>Fired: armed again at <see cref="TrapState.Until"/>.</summary>
    Rearming,

    /// <summary>Fired and never re-arms.</summary>
    Spent,
}

/// <summary>A trap on the level: its marker, its row, its phase and the time (seconds) its phase ends.</summary>
public sealed record TrapState(string Marker, string Trap, TrapPhase Phase, double Until);

/// <summary>A trap's clock: the trap after it, and what fired (null when nothing did).</summary>
public sealed record TrapTick(TrapState After, TrapFire? Fired);

/// <summary>A trap going off: its damage type and damage, its radius, and the tell that warned of it.</summary>
public sealed record TrapFire(string Trap, string DamageType, double Damage, double Radius, TrapTell Tell);

/// <summary>
/// Traps (plan_tf2_rpg 2l): on containers and doorways, dart (bullet), flame jet (fire),
/// charge (blast) and tesla coil (shock), each with a tell a careful player can learn.
/// They hurt robots too: a fire says what it does, not to whom.
/// </summary>
public sealed class TrapRules
{
    private readonly DescentContent _content;

    public TrapRules(DescentContent content) => _content = content;

    /// <summary>A trap as it stands at load: armed.</summary>
    public TrapState New(string marker, string trap)
    {
        _ = _content.Furniture.Trap(trap);
        return new TrapState(marker, trap, TrapPhase.Armed, 0);
    }

    /// <summary>
    /// A body steps into the trap's trigger area at <paramref name="now"/> (seconds). An armed trap
    /// trips: its tell is given and it fires <see cref="TrapRow.Fuse"/> later (<see cref="TrapState.Until"/>).
    /// A robot trips only a trap whose row says so. A tripped, spent or re-arming trap does nothing.
    /// </summary>
    public TrapState Trip(TrapState state, double now, bool robot)
    {
        var row = _content.Furniture.Trap(state.Trap);
        if (state.Phase != TrapPhase.Armed || (robot && !row.Robots))
            return state;
        return state with { Phase = TrapPhase.Tripped, Until = now + row.Fuse };
    }

    /// <summary>
    /// The clock at <paramref name="now"/>: a tripped trap whose fuse has run fires
    /// (<see cref="TrapTick.Fired"/>) and re-arms <see cref="TrapRow.Rearm"/> later, or is spent for
    /// good with no re-arm; a re-arming one whose time has come is armed again.
    /// </summary>
    public TrapTick Tick(TrapState state, double now)
    {
        var row = _content.Furniture.Trap(state.Trap);
        switch (state.Phase)
        {
            case TrapPhase.Tripped when now >= state.Until:
                var after = row.Rearm > 0
                    ? state with { Phase = TrapPhase.Rearming, Until = now + row.Rearm }
                    : state with { Phase = TrapPhase.Spent, Until = 0 };
                return new TrapTick(after, Fire(state.Trap));
            case TrapPhase.Rearming when now >= state.Until:
                return new TrapTick(state with { Phase = TrapPhase.Armed, Until = 0 }, null);
            default:
                return new TrapTick(state, null);
        }
    }

    public TrapFire Fire(string trap)
    {
        var t = _content.Furniture.Trap(trap);
        return new TrapFire(t.Id, t.DamageType, t.Damage, t.Radius, t.Tell);
    }

    public TrapTell Tell(string trap) => _content.Furniture.Trap(trap).Tell;
}
