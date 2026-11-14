using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A defender holds where its side's players held by its flag (plan_bots_teams_tf2_part_g G.4's "spots", lane G4b), on the
/// synthetic field (<see cref="TFField"/>): red's flag is at (256, 256); the store says red players held the bin
/// (10, 2) (the place (672, 160), 427 u from the flag) facing north (90 degrees).
/// </summary>
public sealed class TFLearnedHoldTests
{
    private static readonly ExperienceKey Key = new("tf_field", "0123456789abcdef", "ctf");
    private static readonly WorldBin Corner = new(10, 2, 0);
    private static readonly AiVector Away = new(1200f, 256f, 0f);

    private static MapExperience Holds(int role = TFField.Red, WorldBin? bin = null, int kind = (int)TFSpotKind.Hold)
    {
        var rows = new ExperienceDelta();
        rows.Add(new SpotKey(new ExperienceContext(0, role, 3), kind, bin ?? Corner, 4), new SpotStat(2d, 40d, 1d, 0d));
        return new MapExperience(Key, 1, rows);
    }

    private static TFField Defender(MapExperience experience, float trust = 1f, ulong seed = 7)
    {
        var f = new TFField(slot: 3, seed: seed, mates: [1, 2], tune: p => p with { SpotTrust = trust });
        f.Memory.World.ExperienceSlot.Current = experience;
        return f;
    }

    /// <summary>Decides of the defender walking from <see cref="Away"/> to wherever it goes, then standing there, 400 u a decide at most.</summary>
    private static List<IntentCommand> Defend(TFField f, int decides = 40)
    {
        var intents = new List<IntentCommand>();
        AiVector at = Away;
        for (int d = 0; d < decides; d++)
        {
            IntentCommand i = f.Step(TFField.Self(at), TFField.Ctf());
            intents.Add(i);
            if (i.Move.Kind == MoveKind.Follow)
            {
                AiVector to = i.Move.Corner0 - at;
                at += to.Length <= 40f ? to : to * (40f / to.Length);
            }
        }

        return intents;
    }

    [Fact]
    public void ADefenderGoesToWhereItsSideHeldAndFacesTheWayTheyFaced()
    {
        TFField f = Defender(Holds());
        List<IntentCommand> intents = Defend(f);
        Assert.Equal(TFDecider.Defend, f.Branch);
        Assert.Equal(Corner.Centre, intents[0].Move.Corner0);
        IntentCommand holding = intents.Last();
        Assert.Equal(LookKind.Point, holding.Look.Kind);
        AiVector look = holding.Look.Point - Corner.Centre;
        Assert.InRange(MathF.Atan2(look.Y, look.X) * (180f / MathF.PI), 85f, 95f);
        Assert.True(f.Decider.LearnedHoldPicks >= 1);
        Assert.True(f.Decider.LearnedHoldDecides > 0);
    }

    [Fact]
    public void WithAnEmptyStoreEveryIntentIsAsBefore()
    {
        List<IntentCommand> before = Defend(Defender(MapExperience.Empty));
        Assert.DoesNotContain(before, i => i.Move.Corner0 == Corner.Centre);
        // The other side's holds, another kind's, and a hold far from the flag change nothing.
        TFField blue = Defender(Holds(role: TFField.Blue));
        Assert.Equal(before, Defend(blue));
        Assert.Equal(0, blue.Decider.LearnedHoldPicks);
        Assert.Equal(before, Defend(Defender(Holds(kind: (int)TFSpotKind.Sentry))));
        Assert.Equal(before, Defend(Defender(Holds(bin: new WorldBin(40, 2, 0)))));
        // The control: its own side's do.
        Assert.NotEqual(before, Defend(Defender(Holds())));
    }

    /// <summary>
    /// The defender holds the learned spot through its picks: each pick every 6 s takes it again at a trust of 1, and the
    /// re-pick is not forced by the spot lying past 1.5 x the random radius from the flag.
    /// </summary>
    [Fact]
    public void ALearnedSpotIsHeldNotDroppedForBeingPastTheOldRadius()
    {
        TFField far = Defender(Holds(bin: new WorldBin(18, 2, 0)));
        List<IntentCommand> intents = Defend(far, decides: 80);
        Assert.True(new WorldBin(18, 2, 0).Centre.DistanceTo(TFField.RedFlag) > TFDecider.DefendRadius * 1.5f);
        Assert.All(intents.Where(i => i.Move.Kind == MoveKind.Follow), i => Assert.Equal(new WorldBin(18, 2, 0).Centre, i.Move.Corner0));
        Assert.InRange(far.Decider.LearnedHoldPicks, 1, 3);
    }

    /// <summary>How readily is its Discipline's: at a trust of 0.2 most picks are the old random point.</summary>
    [Fact]
    public void AnUndisciplinedDefenderTakesALearnedSpotLessOften()
    {
        int Picks(float trust) => Enumerable.Range(1, 30).Sum(s =>
        {
            TFField f = Defender(Holds(), trust, (ulong)s);
            Defend(f, decides: 80);
            return f.Decider.LearnedHoldPicks;
        });
        Assert.True(Picks(0.2f) * 2 < Picks(1f), $"{Picks(0.2f)} against {Picks(1f)}");
        SkillCurves curves = Skills.Curves.Game();
        Assert.True(curves.For(Profile("discipline", 10)).SpotTrust > curves.For(Profile("discipline", 0)).SpotTrust);
    }

    private static SkillProfile Profile(string skill, int level)
    {
        SkillProfile hard = SkillPresets.TryGet("hard", out SkillProfile? p) ? p! : throw new InvalidOperationException();
        int[] levels = [.. hard.Levels];
        levels[hard.IndexOf(skill)] = level;
        return new SkillProfile(hard.Name, hard.Skills, [.. levels]);
    }
}
