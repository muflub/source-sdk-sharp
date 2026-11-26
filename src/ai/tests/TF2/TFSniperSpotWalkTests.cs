using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

using static SourceSharp.Ai.Tests.TF2.TFSniperFixture;

/// <summary>
/// The Sniper's spot, held only where the bot stands, given up when the walk to it gets no nearer, and only where a player walks (the code review's C3, C4).
/// </summary>
public sealed class TFSniperSpotWalkTests
{
    /// <summary>The field with a ledge (y 576-704, z 128) a player drops off onto the floor and no edge leads up to.</summary>
    private static NavGraph FloorWithDropLedge()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                ids[x, y] = b.AddCell(new NavCell(x * TFField.CellSize, y * TFField.CellSize, (x + 1) * TFField.CellSize, (y + 1) * TFField.CellSize, 0f,
                                                  ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
                if (x > 0)
                    b.AddBoth(ids[x - 1, y], ids[x, y], NavEdgeType.Walk);
                if (y > 0)
                    b.AddBoth(ids[x, y - 1], ids[x, y], NavEdgeType.Walk);
            }
        }

        CellId? last = null;
        for (int x = 10; x < 30; x++)
        {
            CellId c = b.AddCell(new NavCell(x * TFField.CellSize, 576f, (x + 1) * TFField.CellSize, 704f, 128f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            if (last is { } l)
                b.AddBoth(l, c, NavEdgeType.Walk);
            b.AddEdge(c, ids[x, TFField.Width - 1], NavEdgeType.Drop);
            last = c;
        }

        return b.Build();
    }

    /// <summary>At its spot, then moved 600 u off it (a heal, a knock-back), the bot does not hold there: it walks back.</summary>
    [Fact]
    public void MovedOffItsSpotItWalksBack()
    {
        TFField f = Field();
        AiVector spot = Walk(f, 4, _ => TFField.Ctf(), Start);
        Assert.Equal("holding a sight line", f.Decider.Doing.Verdict);
        f.Hud = TFSniperPlayTests.Kit();
        f.StepDrawn(TFSniperPlayTests.Me(spot + new AiVector(0f, 0f, 0f) - new AiVector(600f, 0f, 0f)), TFField.Ctf());
        Assert.Equal(("to a sniper spot", spot), (f.Decider.Doing.Verdict, f.Decider.Doing.Goal));
    }

    /// <summary>A walk to a spot that gets no nearer (the bot stands where it is) gives the spot up after 8 s for another, and not before.</summary>
    [Fact]
    public void AWalkThatGetsNoNearerGivesTheSpotUp()
    {
        TFField f = Field();
        f.Hud = TFSniperPlayTests.Kit();
        f.StepDrawn(TFSniperPlayTests.Me(Start), TFField.Ctf());
        AiVector first = f.Decider.Doing.Goal;
        int changed = -1;
        for (int n = 1; n < 160 && changed < 0; n++)
        {
            f.Hud = TFSniperPlayTests.Kit();
            f.StepDrawn(TFSniperPlayTests.Me(Start), TFField.Ctf());
            if (f.Decider.Doing.Goal != first)
                changed = n;
        }

        float seconds = changed * DecideSchedule.Period * 0.015f;
        Assert.InRange(seconds, 7.9f, 9f);
        Assert.Equal(1, Counter(f, "sniper.spots_given_up"));
    }

    /// <summary>
    /// A ledge only a drop leaves (one joined-up part with the floor, no way up) is no spot from the floor; the control: from the
    /// ledge itself, its cells are spots.
    /// </summary>
    [Fact]
    public void ALedgeOnlyADropLeavesIsNoSpotFromTheFloor()
    {
        NavGraph g = FloorWithDropLedge();
        var fromFloor = TFSniperSpots.Find(g, null, null, Mid, TFField.RedSpawn, attack: true, from: Start);
        var fromLedge = TFSniperSpots.Find(g, null, null, Mid, TFField.RedSpawn, attack: true, from: new AiVector(1000f, 640f, 128f));
        Assert.NotEmpty(fromFloor);
        Assert.All(fromFloor, s => Assert.True(s.At.Y < 512f, $"spot {s.At}"));
        Assert.Contains(fromLedge, s => s.At.Y > 512f);
    }
    /// <summary>
    /// A walk on which the bot moves (back and forth, 100 u a decide: a route round first leads away) but never comes nearer is
    /// not given up at 8 s, as a stall is; it is at 30 s.
    /// </summary>
    [Fact]
    public void AWalkThatMovesButComesNoNearerIsGivenUpAtThirtySeconds()
    {
        TFField f = Field();
        f.Hud = TFSniperPlayTests.Kit();
        f.StepDrawn(TFSniperPlayTests.Me(Start), TFField.Ctf());
        AiVector first = f.Decider.Doing.Goal;
        int changed = -1;
        for (int n = 1; n < 400 && changed < 0; n++)
        {
            f.Hud = TFSniperPlayTests.Kit();
            f.StepDrawn(TFSniperPlayTests.Me(Start + new AiVector(0f, n % 2 == 0 ? 0f : 100f, 0f)), TFField.Ctf());
            if (f.Decider.Doing.Goal != first)
                changed = n;
        }

        Assert.InRange(changed * DecideSchedule.Period * 0.015f, 29.9f, 31f);
    }

    /// <summary>
    /// Three seconds of a stalled walk, ten of a fight (another branch has the bot), then the walk again: the fight's time is no
    /// stall, so the spot is not given up on the walk's return (its clocks start again).
    /// </summary>
    [Fact]
    public void AFightBetweenIsNoPartOfTheWalksStall()
    {
        TFField f = Field();
        int Decides(float seconds) => (int)(seconds / (DecideSchedule.Period * 0.015f));
        for (int n = 0; n < Decides(3f); n++)
        {
            f.Hud = TFSniperPlayTests.Kit();
            f.StepDrawn(TFSniperPlayTests.Me(Start), TFField.Ctf());
        }

        var soldier = TFSniperPlayTests.Drawn(A, Start + new AiVector(1600f, 0f, 0f), TFClass.Soldier);
        int walkedInFight = 0;
        for (int n = 0; n < Decides(10f); n++)
        {
            f.Hud = TFSniperPlayTests.Kit();
            f.StepDrawn(TFSniperPlayTests.Me(Start), TFField.Ctf(), soldier);
            walkedInFight += f.Decider.Doing.Verdict == "to a sniper spot" ? 1 : 0;
        }

        for (int n = 0; n < Decides(2f); n++)
        {
            f.Hud = TFSniperPlayTests.Kit();
            f.StepDrawn(TFSniperPlayTests.Me(Start), TFField.Ctf());
        }

        Assert.Equal((0, 0L), (walkedInFight, Counter(f, "sniper.spots_given_up")));
    }
}
