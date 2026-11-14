using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Skills;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Descent.Server.Horde;

/// <summary>The level's horde controller, for the services that act on its robots.</summary>
public interface IHordeCurrent
{
    /// <summary>This level's controller once it has begun; null when the level has no horde.</summary>
    HordeController? Current { get; }
}

/// <summary>The horde service's controller.</summary>
[SideService(Side.Server, As = [typeof(IHordeCurrent)])]
public sealed class HordeCurrent(HordeService horde) : IHordeCurrent
{
    public HordeController? Current => horde.Current is { Begun: true } c ? c : null;
}

/// <summary>
/// THE SEAM THE ROBOT ATTACKS READ (HO7): what a robot deals, as a multiplier, under the debuffs
/// on it (Maggots!' <c>damage_all_pct</c>, <see cref="RobotConditionRules.Weaken"/>). Nothing reads
/// it yet: a robot hurts no hero until HO7.
/// </summary>
public interface IRobotOutgoing
{
    /// <summary>The multiplier on what <paramref name="robot"/> deals now; 1 for a robot with no debuff or no robot.</summary>
    double DamageMultiplier(RobotId robot);
}

/// <summary>
/// A status's damage tick dealt to a body as a hit (<see cref="RobotStatusEffects"/>): the
/// engine's damage path in the game, so every hook a hit runs (the hurt and death hooks, the
/// kill seam with its killer) runs for it; a fact's own in the tests.
/// </summary>
public interface IRobotStatusDamage
{
    /// <summary>
    /// <paramref name="amount"/> of <paramref name="type"/> (before resistance) on
    /// <paramref name="body"/> from status <paramref name="status"/>, as a hit by player
    /// <paramref name="applier"/> (0: by no player).
    /// </summary>
    void Deal(RobotBodies bodies, RobotBody body, int applier, double amount, string type, string status);
}

/// <summary>
/// A status tick through the engine: <see cref="RobotBodies.BeginStatusTick"/> names the tick's
/// own amount and type, then the hit goes through <see cref="IEngineDamage.ApplyAt"/> as the
/// applying hero's, under a weapon name of the status's own (<c>descent_status_&lt;id&gt;</c>: no
/// item carries it, so TF reads no weapon's attributes for it, and no skill is named, so a
/// skill's type does not override it) and TF's burning or bleeding custom. A hit the engine
/// never hands to the body (it refused it) is dealt to the bodies directly, so the roster and
/// the hit chain still take it.
/// </summary>
[SideService(Side.Server, As = [typeof(IRobotStatusDamage)])]
public sealed class EngineRobotStatusDamage(IEngineDamage damage, IEnginePlayers players, IEngineEntities entities, TFDamageScopes scopes)
    : IRobotStatusDamage
{
    /// <summary>The prefix of a status tick's weapon name.</summary>
    public const string Prefix = "descent_status_";

    private readonly Dictionary<string, string> _weaponNames = new(StringComparer.Ordinal);

    public void Deal(RobotBodies bodies, RobotBody body, int applier, double amount, string type, string status)
    {
        EntityHandle attacker = applier > 0 ? players.GetHandle(applier) : EntityHandle.None;
        if (attacker != EntityHandle.None && !entities.IsValid(attacker))
            attacker = EntityHandle.None;

        if (!_weaponNames.TryGetValue(status, out var weapon))
            _weaponNames[status] = weapon = Prefix + status;

        DamageType bits = RobotDamageKinds.BitsOf(type);
        TFDamageCustom custom = status switch { "burn" => TFDamageCustom.Burning, "bleed" => TFDamageCustom.Bleeding, _ => TFDamageCustom.None };
        bodies.BeginStatusTick(body.Index, amount, type);
        try
        {
            using (scopes.Push(custom, TFWeaponId.None, weapon))
                damage.ApplyAt(body.Handle, attacker, (float)amount, bits, default, HordeVectors.ToEngine(body.Position));
        }
        finally
        {
            if (bodies.EndStatusTick())
            {
                bodies.BeginStatusTick(body.Index, amount, type);
                bodies.Hit(body, attacker, (float)amount, bits, default, default, HitGroup.Generic);
            }
        }
    }
}

/// <summary>
/// Status effects on robots (plan_descent_logic HO8, E9, 3.8): the horde's
/// <see cref="IRobotStatusSink"/> over the roster.
///
/// <list type="bullet">
/// <item><b>Apply</b>: the body's entity names its roster row; the effect's target word must be
/// for it (<see cref="RobotConditionRules.Admits"/>: EMP Charge stuns minions and slows the
/// boss); the word's row (robot_conditions.json) puts its status on the row through
/// <see cref="StatusBook"/> for the skill's seconds, by the hero, against the robot's immunity
/// (a fire-immune robot does not burn), or throws the body up (<c>knock_up</c>, through
/// <see cref="IRobotImpulse"/>). The body's networked status bits are written at once.</item>
/// <item><b>What a status does</b>: the motion reads the roster's stun and slow each tick (a
/// stunned body wishes nothing and goes nowhere; a slowed one goes slower); a mark raises what
/// every hit takes (<see cref="RobotBodies.Hit"/>); a damage-over-time status ticks, each
/// server tick for a body (<see cref="OnThink"/>): its damage is a hit by the hero who applied
/// it (<see cref="IRobotStatusDamage"/>), so resistance, immunity, the brain's Damaged, the
/// kill seam and its killer all hold, and it stops at its end tick.</item>
/// <item><b>No body</b> (plan 3.5 is silent): a robot whose body went back keeps its statuses
/// on its row, ending at their end ticks. A far robot's damage ticks run in the far simulation
/// (<see cref="RobotRoster.TickStatuses"/>); an asleep robot's here at the far rate. Either can
/// kill, and the kill is told with no body and no player, as the kill seam's sweep tells it.</item>
/// <item><b>Threaten</b>: the robot targets the hero until the taunt ends
/// (<see cref="RobotRoster.Taunt"/>, which the far simulation and a later decider can read) and
/// its brain is told the hero attacked it (a Damaged of nothing with the hero as its source,
/// <see cref="Session.IRobotTaunts"/>).</item>
/// <item><b>Weaken</b>: a debuff's <c>damage_all_pct</c> on the row, read through
/// <see cref="IRobotOutgoing"/>.</item>
/// </list>
/// A new level's roster starts with none of it; a dead robot's statuses are dropped
/// (<see cref="RobotRoster.Hurt"/>).
/// </summary>
[SideService(Side.Server, As = [typeof(IRobotStatusSink), typeof(IRobotOutgoing)])]
public sealed class RobotStatusEffects(IHordeCurrent horde, IEntityDirectory directory, IEngineEntities entities, IHordeClock clock,
                                       IHordeRules rules, IRobotImpulse impulse, IRobotStatusDamage damage,
                                       Session.IRobotTaunts taunts)
    : IRobotStatusSink, IRobotOutgoing, IFrameLifecycle
{
    /// <summary>After the horde's tick (<see cref="HordeService.Order"/>), before the kill seam's sweep (<c>RobotKills</c>, First + 540).</summary>
    public const int Order = ModStages.First + 505;

    private StatusDue[] _due = [];
    private long _nextAsleep;

    public int FrameOrder => Order;

    /// <summary>Status damage ticks dealt to bodies, and asleep rows' status ticks run.</summary>
    public long TicksDealt { get; private set; }

    public long AsleepTicks { get; private set; }

    public RobotStatusAnswer Apply(EntityHandle robot, string condition, double seconds, EntityHandle hero, string skillId, string target)
    {
        if (Resolve(robot) is not (HordeController c, RobotBody body, int i))
            return horde.Current is null ? RobotStatusAnswer.NotAvailable : RobotStatusAnswer.NotARobot;

        var roster = c.Roster!;
        if (!RobotConditionRules.Admits(target, roster.IsBoss(i)))
            return RobotStatusAnswer.NotARobot;

        var r = rules.Rules;
        if (r.Conditions.Of(condition) is not { } word)
            return RobotStatusAnswer.NotAvailable;

        if (word.KnockUp > 0)
        {
            var up = RobotConditionRules.KnockUp(word, roster.Stats(i).KnockbackResistPct);
            return impulse.Push(body.Index, new AiVector((float)up.X, (float)up.Y, (float)up.Z))
                ? RobotStatusAnswer.Applied
                : RobotStatusAnswer.NotAvailable;
        }

        int row = r.StatusBook.RowOf(word.Status!);
        var what = roster.ApplyStatus(i, row, clock.Tick, seconds, SeatOf(hero));
        WriteBits(body, roster, i);
        return what == StatusApplied.Immune ? RobotStatusAnswer.Immune : RobotStatusAnswer.Applied;
    }

    public RobotStatusAnswer Threaten(EntityHandle robot, EntityHandle hero, double seconds, string skillId)
    {
        if (Resolve(robot) is not (HordeController c, RobotBody body, int i))
            return horde.Current is null ? RobotStatusAnswer.NotAvailable : RobotStatusAnswer.NotARobot;

        int seat = SeatOf(hero);
        if (seat <= 0)
            return RobotStatusAnswer.NotARobot;

        c.Roster!.Taunt(i, seat, clock.Tick + StatusRules.Ticks(seconds, clock.TicksPerSecond));
        taunts.Taunted(body.Robot, body.Index, seat);
        return RobotStatusAnswer.Applied;
    }

    public RobotStatusAnswer Weaken(EntityHandle robot, IReadOnlyList<StatAmount> stats, double seconds, EntityHandle hero, string skillId,
                                    string target)
    {
        if (Resolve(robot) is not (HordeController c, RobotBody _, int i))
            return horde.Current is null ? RobotStatusAnswer.NotAvailable : RobotStatusAnswer.NotARobot;

        var roster = c.Roster!;
        if (!RobotConditionRules.Admits(target, roster.IsBoss(i)))
            return RobotStatusAnswer.NotARobot;

        double pct = RobotConditionRules.OutgoingPct(stats);
        if (pct == 0)
            return RobotStatusAnswer.NotAvailable;

        long now = clock.Tick;
        roster.Weaken(i, pct, now + StatusRules.Ticks(seconds, clock.TicksPerSecond), now);
        return RobotStatusAnswer.Applied;
    }

    public double DamageMultiplier(RobotId robot)
    {
        if (horde.Current?.Roster is not { } roster || !roster.IsLive(robot))
            return 1;
        long now = clock.Tick;
        return RobotConditionRules.OutgoingMultiplier(roster.WeakenPct(robot.Index, now), roster.WeakenUntil(robot.Index), now);
    }

    /// <summary>
    /// Each server tick: every lent body's statuses that are due run (<see cref="RobotRoster.RunStatuses"/>),
    /// their damage dealt as the applier's hits and the body's bits rewritten; at the far rate,
    /// every asleep row's statuses tick on the row.
    /// </summary>
    public void OnThink(float frameTime)
    {
        if (horde.Current is not { Roster: { } roster, Bodies: { } bodies } c)
            return;

        long now = clock.Tick;
        if (_due.Length != rules.Rules.StatusBook.Count)
            _due = new StatusDue[rules.Rules.StatusBook.Count];

        for (int b = 1; b <= bodies.LastBody; b++)
        {
            var body = bodies.Body(b);
            if (body.Parked || !roster.IsLive(body.Robot))
                continue;
            int i = body.Robot.Index;
            if (roster.State(i) != RobotState.Embodied || roster.StatusDue(i) > now)
                continue;

            int n = roster.RunStatuses(i, now, _due);
            WriteBits(body, roster, i);
            var book = rules.Rules.StatusBook;
            for (int k = 0; k < n && roster.State(i) == RobotState.Embodied; k++)
            {
                var row = book.Row(_due[k].Row);
                damage.Deal(bodies, body, _due[k].Applier, _due[k].Amount, row.DamageType!, row.Id);
                TicksDealt++;
            }
        }

        if (now < _nextAsleep && _nextAsleep - now <= c.FarInterval)
            return;
        _nextAsleep = now + c.FarInterval;
        for (int i = 0; i < roster.Count; i++)
            if (roster.State(i) == RobotState.Asleep && roster.StatusDue(i) <= now)
            {
                roster.TickStatuses(i, now);
                AsleepTicks++;
            }
    }

    /// <summary>One robot's statuses, end ticks, appliers, immunity and resistances, for the dev command.</summary>
    public string Line(int i)
    {
        if (horde.Current?.Roster is not { } roster || (uint)i >= (uint)roster.Count)
            return $"no robot {i}";

        var c = CultureInfo.InvariantCulture;
        long now = clock.Tick;
        var book = rules.Rules.StatusBook;
        var stats = roster.Stats(i);
        var parts = new List<string>();
        for (int row = 0; row < book.Count; row++)
        {
            var s = roster.StatusSlot(i, row);
            if (s.Carried)
                parts.Add(string.Create(c,
                    $"{book.Row(row).Id}x{s.Stacks} until tick {s.EndTick} ({Math.Max(0, s.EndTick - now)} left){(s.NextDamageTick != long.MaxValue ? $" next damage {s.NextDamageTick}" : "")} by {(s.Applier > 0 ? "player " + s.Applier : "no player")}"));
        }

        string taunt = roster.TauntSeat(i, now) is var seat and > 0 ? $"; taunted by player {seat} until {roster.TauntUntil(i)}" : "";
        string weak = roster.WeakenPct(i, now) is var pct and not 0 ? string.Create(c, $"; deals {pct:+0.#;-0.#}% until {roster.WeakenUntil(i)}") : "";
        string resist = string.Join(" ", stats.Resist.Select(kv => string.Create(c, $"{kv.Key} {kv.Value:0.#}%")));
        return string.Create(c,
            $"robot {i} {roster.Id(i)} {roster.Template(i)} {roster.State(i)} tick {now}: {(parts.Count == 0 ? "no statuses" : string.Join(", ", parts))}; "
            + $"speed x{roster.SpeedFactor(i):0.##} stunned {roster.Stunned(i)} takes x{roster.DamageTakenFactor(i):0.##}{taunt}{weak}; "
            + $"immune {stats.Immunity ?? "none"}; resist {resist}; affixes {(roster.Affixes(i).IsEmpty ? "none" : string.Join(",", roster.Affixes(i)))}");
    }

    /// <summary>The living, lent robot a body entity is, with its controller and row; null for anything else.</summary>
    private (HordeController Controller, RobotBody Body, int Row)? Resolve(EntityHandle entity)
    {
        if (horde.Current is not { Roster: { } roster } c || directory.Get(entity) is not RobotBody { Parked: false } body
            || !roster.IsLive(body.Robot) || roster.State(body.Robot.Index) != RobotState.Embodied)
            return null;
        return (c, body, body.Robot.Index);
    }

    private int SeatOf(EntityHandle hero) =>
        hero != EntityHandle.None && entities.IsValid(hero) && entities.IsPlayer(hero) ? entities.GetIndex(hero) : 0;

    private static void WriteBits(RobotBody body, RobotRoster roster, int i)
    {
        byte bits = (byte)roster.StatusBits(i);
        if (body.State.Statuses != bits)
            body.SetStatuses(bits);
    }
}
