using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// Not shooting a teammate (plan_bots_teams_tf2 B.5, plan_bots_teams_tf2_part_g G.2.1): a sighted
/// teammate nearer than the aim point on the line to it, or on the view's own line, blocks the
/// shot; one aside, beyond the aim point or under the line does not, nor does an enemy; and a
/// teammate inside the shot's blast blocks it wherever the line runs.
/// </summary>
public sealed class FriendLineTests
{
    private const int Mine = 2, Theirs = 3;
    private const float Eye = 64f;
    private static readonly ActorId Bot = new(1, 1), Mate = new(2, 1);
    private static readonly AiVector AimEast = new(600f, 0f, 48f);

    /// <summary>The bot at the origin looking along <paramref name="yaw"/>, with one actor of <paramref name="team"/> in sight at <paramref name="at"/>.</summary>
    private static PerceivedView View(AiVector at, int team = Mine, float yaw = 0f, float selfZ = 0f, float pitch = 0f)
    {
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(1), new SelfState(new AiVector(0f, 0f, selfZ), default, new AiAngles(pitch, yaw, 0f), 100, 0, -1, 0, 0, SelfFlags.Alive, Team: Mine));
        v.AddSighted(new ActorFacts(Mate, at, default, -1, ActorActivity.None, 1f, team));
        return v;
    }

    private static bool Blocked(PerceivedView v, AiVector? aimAt, float blast = 0f, float viewReach = 2048f)
        => FriendLine.Blocked(v, Eye, aimAt, viewReach, blast, Eye);

    [Fact]
    public void ATeammateOnTheLineToTheAimPointBlocksTheShot()
        => Assert.True(Blocked(View(new AiVector(300f, 0f, 0f)), AimEast));

    [Fact]
    public void ATeammateAsideDoesNot()
        => Assert.False(Blocked(View(new AiVector(300f, 150f, 0f)), AimEast));

    [Fact]
    public void ATeammateBeyondTheAimPointDoesNot()
        => Assert.False(Blocked(View(new AiVector(900f, 0f, 0f)), AimEast));

    [Fact]
    public void AnEnemyOnTheLineDoesNot()
        => Assert.False(Blocked(View(new AiVector(300f, 0f, 0f), Theirs), AimEast));

    /// <summary>The view has not turned yet: the shot would go where it points, so a teammate there blocks an aim elsewhere.</summary>
    [Fact]
    public void ATeammateOnTheViewsOwnLineBlocksAnAimElsewhere()
    {
        var north = new AiVector(0f, 600f, 48f);
        Assert.True(Blocked(View(new AiVector(300f, 0f, 0f), yaw: 0f), north));
        // The control: with the view turned to the aim, the same teammate is aside.
        Assert.False(Blocked(View(new AiVector(300f, 0f, 0f), yaw: 90f), north));
    }

    [Fact]
    public void AShotNotAimedAtAPointGoesWhereTheViewPointsAsFarAsItsReach()
    {
        Assert.True(Blocked(View(new AiVector(300f, 0f, 0f)), null));
        Assert.False(Blocked(View(new AiVector(300f, 0f, 0f), yaw: 90f), null));
        Assert.False(Blocked(View(new AiVector(300f, 0f, 0f)), null, viewReach: 200f));
    }

    /// <summary>From a ledge the line passes over a teammate's head: a body is taken as high as its top, and no higher.</summary>
    [Fact]
    public void ALinePassingOverATeammatesHeadIsClear()
    {
        var down = new AiVector(600f, 0f, 400f + Eye);
        Assert.False(Blocked(View(new AiVector(300f, 0f, 0f), selfZ: 400f), down));
        Assert.True(FriendLine.Blocked(View(new AiVector(300f, 0f, 0f), selfZ: 400f), Eye, down, 2048f, 0f, bodyTop: 400f + Eye), "the control: a body that tall is on the line");
    }

    /// <summary>The pitch of a view turned down at the floor 400 u out from an eye 64 u up.</summary>
    private static readonly float DownAtTheFloor = MathF.Atan2(Eye, 400f) * 180f / MathF.PI;

    /// <summary>
    /// Lane Ge: a rocket at the floor 400 u out, the view turned down at it, leaves the eye 64 u up and is at 32 u halfway,
    /// through a teammate standing there; judged at the eye's height the line was 9 degrees over the body's point and clear.
    /// </summary>
    [Fact]
    public void ARocketAtTheFloorPastATeammateStandingBetweenIsHeld()
        => Assert.True(Blocked(View(new AiVector(200f, 0f, 0f), pitch: DownAtTheFloor), new AiVector(400f, 0f, 0f)));

    /// <summary>Beyond the aim point the line does not reach a teammate; the blast does, as before.</summary>
    [Fact]
    public void ARocketAtTheFloorBeforeATeammateBeyondItIsHeldByTheBlastAlone()
    {
        PerceivedView beyond = View(new AiVector(480f, 0f, 0f), pitch: DownAtTheFloor);
        Assert.False(Blocked(beyond, new AiVector(400f, 0f, 0f)), "the control: with no blast the line alone is clear");
        Assert.True(Blocked(beyond, new AiVector(400f, 0f, 0f), blast: 150f));
    }

    /// <summary>A level line over a teammate standing 128 u lower, halfway: its top is 128 u under the line.</summary>
    [Fact]
    public void ALevelLineOverALowerTeammatesHeadIsNotHeld()
        => Assert.False(Blocked(View(new AiVector(300f, 0f, -128f)), new AiVector(600f, 0f, Eye)));

    /// <summary>A level line at the eye over a teammate crouched 100 u out, its top 24 u under the line; standing there it is on the line.</summary>
    [Fact]
    public void ALevelLineOverACrouchedTeammatesHeadIsNotHeldAndOverAStandingOneItIs()
    {
        var aim = new AiVector(600f, 0f, Eye);
        PerceivedView crouched = View(new AiVector(100f, 0f, 0f));
        crouched.Reset(Bot, new Tick(1), crouched.Self);
        crouched.AddSighted(new ActorFacts(Mate, new AiVector(100f, 0f, 0f), default, -1, ActorActivity.Crouched, 1f, Mine));
        Assert.False(FriendLine.Blocked(crouched, Eye, aim, 2048f, 0f, Eye, crouchTop: 40f));
        Assert.True(FriendLine.Blocked(View(new AiVector(100f, 0f, 0f)), Eye, aim, 2048f, 0f, Eye, crouchTop: 40f));
    }

    [Fact]
    public void ATeammateInsideTheShotsBlastBlocksItWhereverTheLineRuns()
    {
        PerceivedView besideTheTarget = View(new AiVector(600f, 120f, 0f));
        Assert.False(Blocked(besideTheTarget, AimEast), "the control: off the line, a shot with no blast is clear");
        Assert.True(Blocked(besideTheTarget, AimEast, blast: 150f));
        Assert.False(Blocked(besideTheTarget, AimEast, blast: 60f));
    }

    [Fact]
    public void AnEnemyInsideTheBlastDoesNot()
        => Assert.False(Blocked(View(new AiVector(600f, 120f, 0f), Theirs), AimEast, blast: 150f));

    /// <summary>
    /// The target itself never holds the fire at it (an unmasked Spy is drawn as a teammate: the suspicion hook), and another
    /// teammate on the line still does (review 2, G4).
    /// </summary>
    [Fact]
    public void TheTargetsOwnBodyDoesNotHoldTheFireAnotherTeammateStillDoes()
    {
        PerceivedView v = View(new AiVector(300f, 0f, 0f));
        Assert.True(Blocked(v, AimEast));
        Assert.False(FriendLine.Blocked(v, Eye, AimEast, 2048f, 0f, Eye, target: Mate));
        v.AddSighted(new ActorFacts(new ActorId(3, 1), new AiVector(150f, 0f, 0f), default, -1, ActorActivity.None, 1f, Mine));
        Assert.True(FriendLine.Blocked(v, Eye, AimEast, 2048f, 0f, Eye, target: Mate));
    }
}
