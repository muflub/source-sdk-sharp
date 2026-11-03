using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// Two players who want each other's place (user report 2026-11-02: "head on"):
/// the driver's commands over a small body simulation, two 32 u boxes that stop
/// each other and stop at the graph's edge, as the game's would.
/// </summary>
public sealed class GiveWayTests
{
    private const float RunPerTick = 190f * 0.015f, Hull = 32f;

    private static IntentCommand Brain(ActorId bot, AiVector goal, int tick)
        => new(bot, new Tick(tick), tick, new Tick(tick + 16), new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, goal, default, default, default, default, false, 0),
               LookIntent.AlongPath, ActionIntent.None, default);

    /// <summary>The flat way a command walks, as the applier reads it.</summary>
    private static AiVector Wish(in MoveIntent move, AiVector at)
    {
        switch (move.Kind)
        {
            case MoveKind.Strafe:
                return new AiVector(move.Direction.X, move.Direction.Y, 0f).Normalised();
            case MoveKind.Follow:
            case MoveKind.Retreat:
                for (int i = 0; i < move.CornerCount; i++)
                {
                    bool last = i == move.CornerCount - 1;
                    var to = new AiVector(move.Corner(i).X - at.X, move.Corner(i).Y - at.Y, 0f);
                    if (to.Length > (last ? BotCommandApplier.CornerHeld : BotCommandApplier.CornerReached))
                        return Steering.Bend(to.Normalised(), move.Direction);
                }

                return default;
            default:
                return default;
        }
    }

    private static bool Free(NavGraph g, AiVector at, AiVector other)
        => g.FindCell(at + new AiVector(0f, 0f, 1f)).IsValid && !(MathF.Abs(at.X - other.X) < Hull && MathF.Abs(at.Y - other.Y) < Hull);

    /// <summary>Runs two bots to their goals; answers the ticks each took, or -1 for one that never got there.</summary>
    private static (int A, int B, BrainDriver Driver) Run(NavGraph g, AiVector a, AiVector goalA, AiVector b, AiVector goalB, int limit)
    {
        var d = new BrainDriver();
        ActorId botA = new(2, 1), botB = new(3, 2);
        var open = new NavObstacleState(0);
        int doneA = -1, doneB = -1;
        for (int t = 2; t < limit && (doneA < 0 || doneB < 0); t++)
        {
            d.Players = [new NearbyPlayer(botA.Slot, a), new NearbyPlayer(botB.Slot, b)];
            AiVector wishA = Wish(d.Drive(Brain(botA, goalA, t), a, new Tick(t), t, g, open).Move, a);
            AiVector wishB = Wish(d.Drive(Brain(botB, goalB, t), b, new Tick(t), t, g, open).Move, b);
            a = Moved(g, a, wishA, b);
            b = Moved(g, b, wishB, a);
            if (doneA < 0 && a.DistanceTo(goalA) < NavFollowerReach)
                doneA = t;
            if (doneB < 0 && b.DistanceTo(goalB) < NavFollowerReach)
                doneB = t;
        }

        return (doneA, doneB, d);
    }

    private const float NavFollowerReach = 20f;

    private static AiVector Moved(NavGraph g, AiVector at, AiVector wish, AiVector other)
    {
        // Each axis on its own, as a player slides along what stops it.
        var x = new AiVector(at.X + wish.X * RunPerTick, at.Y, at.Z);
        if (Free(g, x, other))
            at = x;
        var y = new AiVector(at.X, at.Y + wish.Y * RunPerTick, at.Z);
        if (Free(g, y, other))
            at = y;
        return at;
    }

    private static NavGraph Corridor(out AiVector west, out AiVector east)
    {
        var (g, ids) = Grids.Parse(
            "...       ...",
            "...       ...",
            ".............",
            "...       ...",
            "...       ...");
        west = g.Cell(Grids.At(ids, 0, 2)).Centre;
        east = g.Cell(Grids.At(ids, 12, 2)).Centre;
        return g;
    }

    /// <summary>
    /// Head on in a corridor one hull wide, each wanting the room behind the
    /// other: the lower one backs out to the room, steps aside and lets the other
    /// by, and both get through. Pressing, neither ever would.
    /// </summary>
    [Fact]
    public void TwoBotsHeadOnInAOneCellCorridorBothGetThrough()
    {
        NavGraph g = Corridor(out AiVector west, out AiVector east);
        (int a, int b, BrainDriver d) = Run(g, west, east, east, west, 2400);
        Assert.True(a > 0 && b > 0, $"west bot {a}, east bot {b}: ticks to get through, -1 for never");
        Assert.True(d.Yields > 0, "nobody gave way: the two never met");
    }

    /// <summary>
    /// A corridor exactly two hulls wide (two cells, 64 u between the walls' reach,
    /// so bodies' centres have just 32 u to play in): the lower one gets clear
    /// across and the other walks by (live, dm_lockdown at (-2728 4430 128)).
    /// </summary>
    [Fact]
    public void AHeadOnInATwoHullCorridorPasses()
    {
        var (g, ids) = Grids.Parse(
            "..............",
            "..............");
        AiVector west = new(16f, 16f, 0f), east = g.Cell(Grids.At(ids, 13, 1)).Centre;
        (int a, int b, BrainDriver d) = Run(g, west, east, east, west, 1200);
        Assert.True(a > 0 && b > 0, $"west bot {a}, east bot {b}");
        Assert.True(a < 400 && b < 400, $"west bot {a} ticks, east bot {b} ticks: no dawdling");
        _ = d;
    }

    /// <summary>One bot behind another going the same way is not held up: it does not back off from someone walking ahead of it.</summary>
    [Fact]
    public void ABotFollowingAnotherDoesNotGiveWay()
    {
        NavGraph g = Corridor(out AiVector west, out AiVector east);
        (int a, int b, BrainDriver d) = Run(g, west, east, west + new AiVector(40f, 0f, 0f), east + new AiVector(0f, 64f, 0f), 2400);
        Assert.True(a > 0 && b > 0, $"{a} {b}");
        Assert.Equal(0, d.Yields);
    }

    /// <summary>A higher bot walking into one who is not moving at all gives way itself once its patience is out, and goes round.</summary>
    [Fact]
    public void ABotBehindSomeoneStandingStillGoesRound()
    {
        var (g, ids) = Grids.Parse(
            ".........",
            ".........",
            ".........",
            ".........",
            ".........");
        AiVector start = g.Cell(Grids.At(ids, 0, 2)).Centre, goal = g.Cell(Grids.At(ids, 8, 2)).Centre, standing = g.Cell(Grids.At(ids, 4, 2)).Centre;
        var d = new BrainDriver();
        var bot = new ActorId(1, 1);
        var open = new NavObstacleState(0);
        AiVector at = start;
        bool through = false;
        for (int t = 2; t < 1200 && !through; t++)
        {
            d.Players = [new NearbyPlayer(bot.Slot, at), new NearbyPlayer(5, standing)];
            at = Moved(g, at, Wish(d.Drive(Brain(bot, goal, t), at, new Tick(t), t, g, open).Move, at), standing);
            through = at.X > standing.X + Hull;
        }

        Assert.True(through, $"stopped at {at}, behind the player at {standing}");
    }

    /// <summary>
    /// Two bots with one goal in the open: both make way toward it, one gets
    /// there, and the other stands off the place rather than push them.
    /// </summary>
    [Fact]
    public void TwoBotsOnOneGoalOneArrivesAndTheOtherStandsOff()
    {
        var (g, ids) = Grids.Parse(
            ".........",
            ".........",
            ".........");
        AiVector goal = g.Cell(Grids.At(ids, 4, 1)).Centre, a = g.Cell(Grids.At(ids, 0, 1)).Centre, b = g.Cell(Grids.At(ids, 8, 1)).Centre + new AiVector(16f, 0f, 0f);
        var d = new BrainDriver();
        ActorId botA = new(2, 1), botB = new(3, 2);
        var open = new NavObstacleState(0);
        IntentCommand lastB = default;
        for (int t = 2; t < 400; t++)
        {
            d.Players = [new NearbyPlayer(botA.Slot, a), new NearbyPlayer(botB.Slot, b)];
            a = Moved(g, a, Wish(d.Drive(Brain(botA, goal, t), a, new Tick(t), t, g, open).Move, a), b);
            lastB = d.Drive(Brain(botB, goal, t), b, new Tick(t), t, g, open);
            b = Moved(g, b, Wish(lastB.Move, b), a);
        }

        Assert.True(a.DistanceTo(goal) < NavFollowerReach, $"the nearer bot got there: {a}");
        Assert.True(b.X < g.Cell(Grids.At(ids, 8, 1)).Centre.X, $"the other made way toward it: {b}");
        Assert.Equal(MoveKind.Hold, lastB.Move.Kind);
        Assert.Equal(0, d.Stucks);
        Assert.Equal(0, d.Yields);   // standing off, not shoving and backing
    }
}
