using System.Runtime.InteropServices;
using SourceSharp.Ai;
using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>What the heroes see, set by a fact: every point of a room, single points, or into a room.</summary>
internal sealed class FakeSight(HordeLevel level) : IHeroSight
{
    public bool[] SeeInto { get; } = new bool[level.RoomCount];
    public bool[] SeePointsIn { get; } = new bool[level.RoomCount];
    public HashSet<AiVector> Points { get; } = [];
    public int Asked { get; private set; }

    public bool CanSee(AiVector point)
    {
        Asked++;
        if (Points.Contains(point)) return true;
        int r = level.RoomAt(point);
        return r >= 0 && SeePointsIn[r];
    }

    public bool CanSeeInto(int room) => SeeInto[room];

    public void Clear()
    {
        Array.Clear(SeeInto);
        Array.Clear(SeePointsIn);
        Points.Clear();
    }
}

/// <summary>Bodies as a dictionary: a body out holds its state; it is never put out twice.</summary>
internal sealed class FakeBodies : IRobotBodies
{
    public Dictionary<int, (RobotId Robot, RobotBodyState State)> Out { get; } = [];

    public void Begin(BodyLease lease, RobotId robot, in RobotBodyState start)
    {
        if (!Out.TryAdd(lease.Body, (robot, start)))
            throw new InvalidOperationException($"body {lease.Body} is out already, for {Out[lease.Body].Robot}");
    }

    public RobotBodyState End(BodyLease lease, RobotId robot)
    {
        if (!Out.Remove(lease.Body, out var held) || held.Robot != robot)
            throw new InvalidOperationException($"body {lease.Body} is not out for {robot}");
        return held.State;
    }
}

/// <summary>A level's horde with fake sight and bodies: the decision loop the server will run, tick by tick.</summary>
internal sealed class HordeRig
{
    public HordeRig(HordeLevel level, int party = 1, BodyPool? pool = null)
    {
        Level = level;
        Roster = RosterTests.Rolled(level, party);
        Pool = pool ?? BodyPool.For(TheRules.Content.Horde);
        Rules = EmbodimentRules.For(TheRules.Content.Horde, level, RosterTests.TicksPerSecond);
        Orders = HordeOrders.For(TheRules.Content.Horde, level);
        Sight = new FakeSight(level);
        Bodies = new FakeBodies();
        Applier = new HordeOrderApplier(Roster, Pool, Bodies);
    }

    public HordeLevel Level { get; }
    public RobotRoster Roster { get; }
    public BodyPool Pool { get; }
    public EmbodimentRules Rules { get; }
    public HordeOrders Orders { get; }
    public FakeSight Sight { get; }
    public FakeBodies Bodies { get; }
    public HordeOrderApplier Applier { get; }
    public List<HeroPlace> Heroes { get; } = [];
    public long Now { get; set; } = 1000;

    public ReadOnlySpan<HeroPlace> HeroSpan => CollectionsMarshal.AsSpan(Heroes);

    public void HeroIn(int room) => Heroes.Add(new HeroPlace(Heroes.Count, room, Centre(room)));

    /// <summary>Observe and decide at the current tick; nothing applied.</summary>
    public HordeOrders Decide()
    {
        Rules.Observe(Roster, HeroSpan, Sight, Now);
        Rules.Decide(Roster, Pool, HeroSpan, Sight, Now, Orders);
        return Orders;
    }

    /// <summary>Decide without observing: what the rules order from the bookkeeping as it stands.</summary>
    public HordeOrders DecideOnly()
    {
        Rules.Decide(Roster, Pool, HeroSpan, Sight, Now, Orders);
        return Orders;
    }

    /// <summary>Moves the clock on, decides and applies.</summary>
    public HordeOrders Step(long ticks = 1)
    {
        Now += ticks;
        Decide();
        Applier.Apply(Orders, Now);
        return Orders;
    }

    public IEnumerable<HordeOrder> Of(HordeOrderKind kind) => Orders.AsSpan().ToArray().Where(o => o.Kind == kind);

    public int[] Promoted() => [.. Of(HordeOrderKind.Promote).Select(o => o.Robot.Index)];

    public int[] Demoted() => [.. Of(HordeOrderKind.Demote).Select(o => o.Robot.Index)];

    /// <summary>Rows standing in <paramref name="room"/>.</summary>
    public int[] In(int room) => [.. Enumerable.Range(0, Roster.Count).Where(i => Roster.Room(i) == room)];

    /// <summary>Kills every row not in <paramref name="keep"/> (a dead row without a body is out of every decision).</summary>
    public void KillAllBut(params int[] keep)
    {
        for (int i = 0; i < Roster.Count; i++)
            if (!keep.Contains(i)) Roster.Hurt(i, 1e12);
    }

    /// <summary>Gives row <paramref name="i"/> a body outside the rules (a fact's setup).</summary>
    public void Embody(int i, int body)
    {
        if (Roster.State(i) == RobotState.Asleep) Roster.WakeRoom(Roster.Room(i));
        var lease = Pool.Lend(body, Roster.Id(i));
        Bodies.Begin(lease, Roster.Id(i), Roster.Promote(i, lease, Now));
    }

    /// <summary>Stands row <paramref name="i"/> in <paramref name="room"/>, a little off its centre so no two rows share a point.</summary>
    public void Place(int i, int room) => Roster.SetPose(i, room, Centre(room) + At(i % 64, 3 * (i % 64)), 0);
}
