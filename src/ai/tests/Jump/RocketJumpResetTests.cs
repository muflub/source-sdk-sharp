using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.TF2;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A jump map's reset (lane G7a): a body that fell under where its jump started and is put back at the start by the game
/// (a trigger_teleport under the jump) ends the jump as a reset, a failed try of its link; a body moved anywhere else is
/// moved, as before (a round's restart), and says nothing.
/// </summary>
public sealed class RocketJumpResetTests
{
    private static readonly MotionParams Normal = new(250f, 600f, 10f, 150f);

    private static (IAbilityRun Run, List<JumpFlown> Flown, Vector Start, AbilityBody Body) Begun()
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        JumpPlan plan = TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true };
        var book = new JumpBook(new JumpBookSettings());
        book.SetLinks(links, [plan]);
        var flown = new List<JumpFlown>();
        var program = new RocketJumpProgram(book, JumpRig.Classes(), flown.Add);
        Vector start = plan.Recipe.Start().Origin;
        var body = new AbilityBody(new AiVector(start.X - 30f, start.Y, start.Z), AiVector.Zero, new AiAngles(0f, 0f, 0f), true);
        IAbilityRun run = program.Begin(NavLinks.Argument(0), body, Normal, 0.015f)!;
        Assert.False(run.Step(body).Over);
        return (run, flown, start, body);
    }

    /// <summary>
    /// The body is under the start (steps it can take; on a floor there, so the approach goes on: a fall's frames are a
    /// flight's, which a fact cannot launch), then the game puts it back at the start.
    /// </summary>
    [Fact]
    public void AFallenBodyPutBackAtTheStartEndsTheJumpAsAReset()
    {
        var (run, flown, start, body) = Begun();
        Assert.False(run.Step(body with { Origin = new AiVector(start.X - 20f, start.Y, start.Z - 100f) }).Over);
        Assert.False(run.Step(body with { Origin = new AiVector(start.X - 20f, start.Y, start.Z - 200f) }).Over);
        Assert.Empty(flown);
        Assert.True(run.Step(body with { Origin = new AiVector(start.X + 200f, start.Y, start.Z + 1f) }).Over);
        Assert.Equal(JumpEnd.Reset, Assert.Single(flown).End);
    }

    [Fact]
    public void AFallenBodyPutSomewhereElseIsMoved()
    {
        var (run, flown, start, body) = Begun();
        Assert.False(run.Step(body with { Origin = new AiVector(start.X - 20f, start.Y, start.Z - 100f) }).Over);
        Assert.False(run.Step(body with { Origin = new AiVector(start.X - 20f, start.Y, start.Z - 200f) }).Over);
        Assert.True(run.Step(body with { Origin = new AiVector(start.X + RocketJumpProgram.ResetReach + 100f, start.Y, start.Z) }).Over);
        Assert.Equal(JumpEnd.Moved, Assert.Single(flown).End);
    }

    [Fact]
    public void AResetIsAFailedTryOfItsLinkWhereAMoveIsNone()
    {
        JumpPlan plan = TFJumpAdvice.Plan(id: NavLinks.Argument(0));
        JumpFlown Ended(JumpEnd end) => new(plan, plan.Recipe, plan.Recipe.Start(), true, 0f, 40, 120, false, new Vector(0f, 0f, 0f), end, 0.015f);
        Assert.Equal(new LinkStat(1d, 0d, 0d, 0d), TFLinkExperience.StatOf(Ended(JumpEnd.Reset)));
        Assert.Null(TFLinkExperience.StatOf(Ended(JumpEnd.Moved)));
    }
}
