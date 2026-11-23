using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// How a fighting Soldier moves (lane Gfight; the user, 2026-11-15: "and not move while shooting") on
/// the synthetic field (<see cref="TFField"/>): a defender holding an enemy in sight at 600 u, inside
/// the launcher's range, over many decides. It sidesteps nearly all the time, holds a side at least a
/// person's least and mostly reverses, hops now and then on the ground only, crouches by Cover's
/// share, and loads a dry launcher in a place the enemy cannot see.
/// </summary>
public sealed class TFFightMotionTests
{
    /// <summary>The least a person's key holds a side: what the fight's sidestep is held to.</summary>
    private const float PersonsLeastSeconds = 0.25f;

    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Here = new(1000f, 256f, 0f), There = new(1600f, 256f, 0f);

    /// <summary>A Medium defender with TF2's own curves, as the tf2 mod deals one.</summary>
    private static TFField Medium(Func<DecisionParams, DecisionParams>? tune = null, ICellSight? sight = null, bool fight = true, NavGraph? floor = null)
        => new(slot: 3, preset: "normal", mates: [1, 2], curves: TFSkillCurvesTests.ShippedCurves(), tune: tune, sight: sight,
               feel: TFFeelSwitches.All with { Fight = fight }, floor: floor);

    /// <summary><paramref name="decides"/> decides holding the enemy in sight at range: each intent and the leaf it ran.</summary>
    private static List<(IntentCommand Intent, string Leaf)> Fight(TFField f, int decides, SelfFlags flags = SelfFlags.Grounded, int clip = 4, AiVector? at = null)
    {
        AiVector here = at ?? Here;
        var all = new List<(IntentCommand, string)>();
        SelfFlags extra = flags & ~SelfFlags.Grounded;
        for (int i = 0; i < decides; i++)
        {
            SelfState self = TFField.Self(here, clip: clip, extra: extra);
            if ((flags & SelfFlags.Grounded) == 0)
                self = self with { Flags = self.Flags & ~SelfFlags.Grounded, Velocity = new AiVector(0f, 0f, 120f) };
            IntentCommand intent = f.Step(self, TFField.Ctf(), (Enemy, There with { Y = here.Y }, AiVector.Zero, TFField.Blue));
            all.Add((intent, f.Decider.Doing.Leaf));
        }

        return all;
    }

    /// <summary>The share of the decides in the fight's holding leaf whose move is not a stand.</summary>
    private static double Moving(List<(IntentCommand Intent, string Leaf)> run)
    {
        var held = run.Where(r => r.Leaf == "fight at range").ToList();
        Assert.True(held.Count > run.Count / 2, $"{held.Count} of {run.Count} decides held range");
        return held.Count(r => r.Intent.Move.Kind != MoveKind.Hold) / (double)held.Count;
    }

    /// <summary>
    /// A Medium Soldier holding range sidesteps over 80 % of its decides (it stood still for about 70 % of them
    /// when a stretch stepped only at Agility's 0.3).
    /// </summary>
    [Fact]
    public void AMediumSoldierHoldingRangeMovesNearlyAllTheTime()
    {
        double moving = Moving(Fight(Medium(), 3000));
        Assert.True(moving > 0.78, $"moving {moving:P1} of the decides holding range");
    }

    /// <summary>
    /// The cause, as the switch keeps it (<c>ai_tf_feel_fight 0</c>): a stretch stepped only at Agility's share, 0.3 at
    /// Medium, and in range a stretch with no step was a stand, so a Medium Soldier stood still for most of a fight.
    /// </summary>
    [Fact]
    public void WithTheFightSwitchOffAMediumSoldierStandsForMostOfAFight()
    {
        double moving = Moving(Fight(Medium(fight: false), 3000));
        Assert.True(moving is > 0.2 and < 0.4, $"moving {moving:P1} of the decides holding range");
    }

    /// <summary>Agility still tells: a Statue (Agility 0) stands for more of a fight than a Dodger (10), who never stands.</summary>
    [Fact]
    public void AgilityStillTellsHowMuchOfAFightStands()
    {
        double statue = Moving(Fight(Medium(p => p with { StrafeWeight = 0f }), 3000));
        double dodger = Moving(Fight(Medium(p => p with { StrafeWeight = 1f }), 3000));
        Assert.True(statue is > 0.65 and < 0.85 && dodger > 0.99, $"Agility 0 moves {statue:P1}, Agility 10 {dodger:P1}");
    }

    /// <summary>
    /// The side changes at a person's intervals: never inside a person's least hold (250 ms),
    /// no fixed rhythm (their spread is wide), and a stretch's end mostly reverses.
    /// </summary>
    [Fact]
    public void ASidestepHoldsItsSideAPersonsLeastAndHasNoRhythm()
    {
        TFField f = Medium(p => p with { StrafeWeight = 1f });
        List<(IntentCommand Intent, string Leaf)> run = Fight(f, 3000);
        var turns = new List<int>();
        int side = 0, since = 0;
        for (int i = 0; i < run.Count; i++)
        {
            // A sidestep's way, or a hop's (aimed along the sidestep it begins).
            MoveIntent m = run[i].Intent.Move;
            float across = m.Kind == MoveKind.Strafe ? m.Direction.Y : m.Kind == MoveKind.Jump ? m.Corner(0).Y - Here.Y : 0f;
            int now = Math.Sign(MathF.Round(across, 3));
            if (now == 0)
                continue;
            if (side != 0 && now != side)
            {
                turns.Add((i - since) * DecideSchedule.Period);
                since = i;
            }
            else if (side == 0)
            {
                since = i;
            }

            side = now;
        }

        // A stretch drawn at a decide ends its least hold later, and the side is seen at the first decide after that.
        // A person's least, 250 ms, written here and not read from the decider, so a decider that drops it is caught.
        int least = (int)Math.Ceiling((int)(PersonsLeastSeconds / 0.015f) / (double)DecideSchedule.Period) * DecideSchedule.Period;
        double mean = turns.Average(), spread = Math.Sqrt(turns.Average(t => (t - mean) * (t - mean)));
        Assert.True(turns.Count > 50 && turns.Min() >= least && spread / mean > 0.3,
                    $"{turns.Count} turns, the shortest {turns.Min() * 15} ms, mean {mean * 15:F0} ms, spread {spread / mean:F2} of it");
        Assert.InRange(f.Decider.FightSteps.Changes / (double)f.Decider.FightSteps.Stretches, 0.7, 0.9);
    }

    /// <summary>A reversal sometimes begins with a hop the way it goes; never in the air.</summary>
    [Fact]
    public void AFightingSoldierHopsNowAndThenAndNeverInTheAir()
    {
        List<(IntentCommand Intent, string Leaf)> ground = Fight(Medium(), 3000);
        List<(IntentCommand Intent, string Leaf)> air = Fight(Medium(), 3000, flags: SelfFlags.None);
        int hops = ground.Count(r => r.Intent.Move.Kind == MoveKind.Jump);
        Assert.True(hops is > 5 and < 300 && air.All(r => r.Intent.Move.Kind != MoveKind.Jump), $"{hops} hops on the ground");
    }

    /// <summary>The way a fight's move takes the bot across the line to the enemy (+y): a sidestep's, or a hop's.</summary>
    private static float Across(MoveIntent m, AiVector at)
        => m.Kind == MoveKind.Strafe ? m.Direction.Y : m.Kind == MoveKind.Jump ? m.Corner(0).Y - at.Y : 0f;

    /// <summary>
    /// Never into a wall (lane Gmove, 2026-11-16: on cp_granary 28 % of the still sidestepping firing ticks had a wall
    /// or a drop a step to that side): 8 u from the floor's south edge a Dodger never steps south and still steps
    /// north on most decides; in the floor's middle it steps both ways.
    /// </summary>
    [Fact]
    public void AFightingSoldierNeverSidestepsIntoAWall()
    {
        var byWall = new AiVector(1000f, 8f, 0f);
        TFField f = Medium(p => p with { StrafeWeight = 1f });
        List<(IntentCommand Intent, string Leaf)> wall = Fight(f, 2000, at: byWall);
        List<(IntentCommand Intent, string Leaf)> open = Fight(Medium(p => p with { StrafeWeight = 1f }), 2000);
        int south = wall.Count(r => Across(r.Intent.Move, byWall) < -0.01f), north = wall.Count(r => Across(r.Intent.Move, byWall) > 0.01f);
        int openSouth = open.Count(r => Across(r.Intent.Move, Here) < -0.01f), openNorth = open.Count(r => Across(r.Intent.Move, Here) > 0.01f);
        Assert.True(south == 0 && north > 1500 && f.Decider.WallTurns > 50, $"by the wall {south} south, {north} north, {f.Decider.WallTurns} turned");
        Assert.True(openSouth > 500 && openNorth > 500, $"in the open {openSouth} south, {openNorth} north");
    }

    /// <summary>
    /// In a corridor 24 u wide along the line to the enemy, a wall less than half a step off either side, the fight
    /// does not sidestep: holding range it steps along the line instead, nearer and further, and never stands.
    /// </summary>
    [Fact]
    public void WithAWallEitherSideTheFightStepsAlongTheLine()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length];
        for (int x = 0; x < ids.Length; x++)
            ids[x] = b.AddCell(new NavCell(x * TFField.CellSize, 0f, (x + 1) * TFField.CellSize, 24f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        for (int x = 0; x + 1 < ids.Length; x++)
            b.AddBoth(ids[x], ids[x + 1], NavEdgeType.Walk);
        var at = new AiVector(1000f, 12f, 0f);
        TFField f = Medium(p => p with { StrafeWeight = 1f, CrouchShare = 0f }, floor: b.Build());
        List<(IntentCommand Intent, string Leaf)> run = Fight(f, 1000, at: at);
        var held = run.Where(r => r.Leaf == "fight at range").ToList();
        int stepped = held.Count(r => MathF.Abs(Across(r.Intent.Move, at)) > 0.01f);
        int nearer = held.Count(r => r.Intent.Move.Kind == MoveKind.Strafe && r.Intent.Move.Direction.X > 0.5f);
        int further = held.Count(r => r.Intent.Move.Kind == MoveKind.Strafe && r.Intent.Move.Direction.X < -0.5f);
        int stood = held.Count(r => r.Intent.Move.Kind == MoveKind.Hold);
        Assert.True(held.Count > 500 && stepped == 0 && nearer > 100 && further > 100 && stood == 0 && f.Decider.WallAlongs > 50,
                    $"{held.Count} held range: {stepped} sidesteps, {nearer} nearer, {further} further, {stood} stood, {f.Decider.WallAlongs} along");
    }

    /// <summary>A turn at a wall is the wall's, not the rhythm's reversal: it never begins a hop (a hop from a stand at a wall is still air).</summary>
    [Fact]
    public void ATurnAtAWallBeginsNoHop()
    {
        var byWall = new AiVector(1000f, 8f, 0f);
        TFField f = Medium(p => p with { StrafeWeight = 1f });
        int turns = 0, hopsAtTurns = 0;
        for (int i = 0; i < 3000; i++)
        {
            int before = f.Decider.WallTurns;
            IntentCommand intent = Fight(f, 1, at: byWall)[0].Intent;
            if (f.Decider.WallTurns == before)
                continue;
            turns++;
            hopsAtTurns += intent.Move.Kind == MoveKind.Jump ? 1 : 0;
        }

        Assert.True(turns > 100 && hopsAtTurns == 0 && f.Decider.FightHops > 0, $"{turns} turns, {hopsAtTurns} began with a hop, {f.Decider.FightHops} hops in all");
    }

    /// <summary>Holding range a bot of Cover crouches by its share; one of no share never does.</summary>
    [Fact]
    public void ABotOfCoverCrouchesWhileHoldingRangeAndOneOfNoneNever()
    {
        int Crouched(float share) => Fight(Medium(p => p with { CrouchShare = share }), 2000).Count(r => r.Intent.Move.CrouchOn);
        int some = Crouched(0.3f), none = Crouched(0f);
        Assert.True(some > 100 && none == 0, $"crouched {some} decides at 0.3, {none} at 0");
    }

    /// <summary>
    /// A dry launcher with the enemy in sight goes to load where the enemy's cell does not see (the sight table's),
    /// within Cover's reach; with no Cover it backs off sidestepping.
    /// </summary>
    [Fact]
    public void ADryLauncherLoadsWhereTheEnemyCannotSee()
    {
        // The enemy's cells see the whole floor but the strip y >= 448 (the floor's top row).
        var sight = new RowSight(TFField.Floor(), row => row < 7);
        IntentCommand covered = Fight(Medium(p => p with { CoverWeight = 1f }, sight), 1, clip: 0)[^1].Intent;
        IntentCommand open = Fight(Medium(p => p with { CoverWeight = 0f }, sight), 1, clip: 0)[^1].Intent;
        Assert.Equal((MoveKind.Follow, true), (covered.Move.Kind, covered.Move.CornerCount > 0 && covered.Move.Corner(covered.Move.CornerCount - 1).Y >= 448f));
        Assert.Equal(MoveKind.Strafe, open.Move.Kind);
    }

    private sealed class RowSight(NavGraph graph, Func<int, bool> rowSeen) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => rowSeen((int)(graph.Cell(b).Centre.Y / TFField.CellSize));

        public float Exposure(int cell) => 0.5f;
    }
}
