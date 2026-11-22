using SourceSharp.Ai.Replay;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay fightstill</c> (<see cref="FightStillTool"/>, lane Gmove) on synthetic streams: a bot
/// firing, its view along +x in a corridor of cells along x (y 0 to 64), standing at y 40: its left
/// (+y) is a wall a step off, its right (-y) open floor.
/// </summary>
public sealed class FightStillTests
{
    private static readonly ActorId Bot = new(3, 7), Mate = new(4, 9);

    private const int Attack = 1, Duck = 4;

    private const float Right = 450f, Left = -450f;

    private static NavGraph Corridor()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[8];
        for (int x = 0; x < 8; x++)
            ids[x] = b.AddCell(new NavCell(x * 64f, 0f, (x + 1) * 64f, 64f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        for (int x = 0; x + 1 < 8; x++)
            b.AddBoth(ids[x], ids[x + 1], NavEdgeType.Walk);
        return b.Build();
    }

    private static readonly AiVector At = new(200f, 40f, 0f);

    private static AiUserCmd Cmd(int tick, float side, int buttons = Attack)
        => new(Bot, new Tick(tick), tick, tick, new AiAngles(0f, 0f, 0f), 0f, side, 0f, buttons, 0, 0, 0, 0, 0, 0);

    /// <summary>A body standing at <see cref="At"/> from tick 0 to 60, or moving along x at a run when <paramref name="moving"/>.</summary>
    private static SortedList<int, AiActorFrame> Body(ActorId who, AiVector at, bool moving = false)
    {
        var frames = new SortedList<int, AiActorFrame>();
        for (int t = 0; t < 60; t++)
        {
            AiVector o = moving ? at + new AiVector(240f * AiMetrics.TickSeconds * t, 0f, 0f) : at;
            frames[t] = new AiActorFrame(who, new Tick(t), o, o, default);
        }

        return frames;
    }

    private static FightStillReport Run(IEnumerable<AiUserCmd> cmds, bool moving = false, AiVector? mate = null)
    {
        var frames = new Dictionary<ActorId, SortedList<int, AiActorFrame>> { [Bot] = Body(Bot, At, moving) };
        if (mate is { } m)
            frames[Mate] = Body(Mate, m);
        return FightStillTool.From([Bot], new Dictionary<ActorId, List<AiUserCmd>> { [Bot] = cmds.ToList() }, frames, Corridor());
    }

    [Fact]
    public void AStillSidestepIntoTheWallIsAWallAndOneToTheOpenSideIsNot()
    {
        FightStillReport left = Run(Enumerable.Range(20, 10).Select(t => Cmd(t, Left)));
        FightStillReport right = Run(Enumerable.Range(20, 10).Select(t => Cmd(t, Right)));

        Assert.Equal((10, 10), (left.StillSidestep, left.Count(StillCause.Wall)));
        Assert.Equal((10, 0, 10), (right.StillSidestep, right.Count(StillCause.Wall), right.Count(StillCause.Else)));
    }

    [Fact]
    public void AMovingBodyIsNotStill()
    {
        FightStillReport r = Run(Enumerable.Range(20, 10).Select(t => Cmd(t, Left)), moving: true);

        Assert.Equal((10, 10, 0), (r.FiringTicks, r.CommandedTicks, r.StillCommanded));
    }

    [Fact]
    public void HoldingDuckIsCrouchedBeforeTheWall()
        => Assert.Equal(10, Run(Enumerable.Range(20, 10).Select(t => Cmd(t, Left, Attack | Duck))).Count(StillCause.Crouched));

    /// <summary>The side turned round at tick 20: the open side's still ticks within 16 of it are a reversal, the rest not.</summary>
    [Fact]
    public void AChangeOfSideIsAReversalForSixteenTicks()
    {
        var cmds = Enumerable.Range(10, 10).Select(t => Cmd(t, Left, 0)).Concat(Enumerable.Range(20, 20).Select(t => Cmd(t, Right)));
        FightStillReport r = Run(cmds);

        Assert.Equal((16, 4), (r.Count(StillCause.Reversal), r.Count(StillCause.Else)));
    }

    [Fact]
    public void AnotherBodyInTheStepIsABody()
    {
        FightStillReport r = Run(Enumerable.Range(20, 10).Select(t => Cmd(t, Right)), mate: At + new AiVector(0f, -40f, 0f));

        Assert.Equal(10, r.Count(StillCause.Body));
    }
}
