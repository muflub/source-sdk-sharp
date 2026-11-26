using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

using static SourceSharp.Ai.Tests.TF2.TFSniperFixture;

/// <summary>The Sniper review facts' field: a seed per archetype, a walk that stands the bot on its goal, two red/blue points.</summary>
internal static class TFSniperFixture
{
    public static readonly ActorId A = new(5, 0x4000_0001), B = new(6, 0x4000_0001), Medic = new(7, 0x4000_0001), Pyro = new(8, 0x4000_0001);
    public static readonly AiVector Mid = new(2048f, 256f, 0f), Start = new(1500f, 256f, 0f);
    public static readonly AiVector PointA = new(1600f, 256f, 0f), PointB = new(2400f, 256f, 0f);
    public const int Red = TFField.Red, Blue = TFField.Blue;

    public static TFField Field(string archetype = TFSniperClass.Marksman, int[]? mates = null, Func<DecisionParams, DecisionParams>? tune = null)
        => new(seed: TFSniperPlayTests.SeedOf(archetype), weapons: TFSniperPlayTests.Weapons, mates: mates, tune: tune);

    public static long Counter(TFField f, string name) => f.Decider.ClassCounters().Single(kv => kv.Key == name).Value;

    /// <summary>Steps with the objectives as <paramref name="objectives"/> says per decide, standing the bot on its goal when it walks.</summary>
    public static AiVector Walk(TFField f, int decides, Func<int, ObjectiveKnowledge> objectives, AiVector from, int weapon = TFSniperPlayTests.Rifle,
                                Func<int, ActorFacts[]>? seen = null, TFHud? hud = null)
    {
        AiVector at = from;
        for (int n = 0; n < decides; n++)
        {
            f.Hud = hud ?? TFSniperPlayTests.Kit();
            f.StepDrawn(TFSniperPlayTests.Me(at, weapon), objectives(n), seen?.Invoke(n) ?? []);
            if (f.Decider.Doing.HasGoal && f.Decider.Doing.Verdict is "to a sniper spot" or "leaving the spot")
                at = f.Decider.Doing.Goal;
        }

        return at;
    }

    /// <summary>Red's points: A at 1,600 and B at 2,400, as asked.</summary>
    public static ObjectiveKnowledge Points(int aOwner, int aMay, ObjectiveState aState, int bOwner, int bMay, ObjectiveState bState)
        => new([
            new Objective(20, ObjectiveKind.Capture, aOwner, aMay, aState, PointA),
            new Objective(21, ObjectiveKind.Capture, bOwner, bMay, bState, PointB),
        ]);

    public static IntentCommand[] Scoped(TFField f, float charge, int decides, params ActorFacts[] seen)
    {
        f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: charge);
        return [.. Enumerable.Range(0, decides).Select(_ => f.StepDrawn(TFSniperPlayTests.Me(Mid), TFField.Ctf(), seen))];
    }

    public static AiAngles Facing(AiVector from, AiVector to)
    {
        AiVector d = to - from;
        return new AiAngles(0f, MathF.Atan2(d.Y, d.X) * 180f / MathF.PI, 0f);
    }
}

/// <summary>
/// The Sniper's CTF line (the code review's C2): the middle of the flags' homes, which a carried flag does not move.
/// </summary>
public sealed class TFSniperCtfLineTests
{
    /// <summary>In CTF the watched line is the middle of the flags' homes: the spot is 500-1,100 u from the field's middle, on red's side.</summary>
    [Fact]
    public void InCtfTheLineIsTheMiddleOfTheFlagsHomes()
    {
        TFField f = Field();
        Walk(f, 2, _ => TFField.Ctf(), Start);
        AiVector goal = f.Decider.Doing.Goal;
        Assert.InRange(goal.DistanceTo(Mid), TFSniperSpots.NearestUnits, TFSniperSpots.FarthestUnits);
        Assert.True(goal.X < Mid.X, $"spot {goal}");
    }

    /// <summary>
    /// The enemy's flag lifted and carried 2,800 u toward red's base for 20 s (past the 15 s a watch is held) does not move the
    /// line: no new spot search. The control: red's own spots were searched once at the start.
    /// </summary>
    [Fact]
    public void ACarriedFlagDoesNotMoveTheLine()
    {
        TFField f = Field();
        Walk(f, 2, _ => TFField.Ctf(), Start);
        long searched = Counter(f, "sniper.spot_searches");
        Walk(f, (int)(20f / (DecideSchedule.Period * 0.015f)), _ => TFField.Ctf(blue: ObjectiveState.Carried, blueAt: new AiVector(1000f, 256f, 0f), blueCarrier: 5), Start);
        Assert.Equal((1L, 1L), (searched, Counter(f, "sniper.spot_searches")));
    }
}
