using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot coming into the spot where its side keeps dying checks the place death came from (plan_bots_teams_tf2_part_g
/// G.4's "danger places", lane G4b), on the synthetic field (<see cref="TFField"/>): the red Soldier runs east to the blue
/// flag at 300 u/s through bins 32 to 47 of its row (y bin 4), where the store says red players were killed from one place
/// to the north (<see cref="Window"/>, bin 40, 12).
/// </summary>
public sealed class TFDangerLookTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private const float TickSeconds = 0.015f, Speed = 300f;
    private const int FirstBin = 32, LastBin = 47;
    private static readonly WorldBin Window = new(40, 12, 0);
    private static readonly ExperienceKey Key = new("tf_field", "0123456789abcdef", "ctf");

    /// <summary>A snapshot whose sight lines say <paramref name="role"/>'s players died across the stretch, each bin's death from <paramref name="killer"/>'s bin for it (the window by default).</summary>
    private static MapExperience Deaths(int role = TFField.Red, Func<int, WorldBin>? killer = null)
    {
        var rows = new ExperienceDelta();
        for (int x = FirstBin; x <= LastBin; x++)
            rows.Add(new SightKey(new ExperienceContext(0, role, 3), new WorldBin(x, 4, 0), killer?.Invoke(x) ?? Window, 2), new Tally(1d));
        return new MapExperience(Key, 1, rows);
    }

    private static TFField Runner(MapExperience experience, ulong seed = 7, float chance = 1f, float seconds = 0.9f)
    {
        var f = new TFField(seed: seed, graph: false, tune: p => p with { DangerLookChance = chance, DangerLookSeconds = seconds });
        f.Memory.World.ExperienceSlot.Current = experience;
        return f;
    }

    /// <summary>Runs the bot east from x 1,800 to 3,300 at <see cref="Speed"/>, a decide every <see cref="DecideSchedule.Period"/> ticks.</summary>
    private static List<IntentCommand> Cross(TFField f, Func<int, (ActorId, AiVector, AiVector, int)[]>? sighted = null)
    {
        var intents = new List<IntentCommand>();
        float step = Speed * DecideSchedule.Period * TickSeconds;
        int decide = 0;
        for (var at = new AiVector(1800f, 288f, 0f); at.X < 3300f; at += new AiVector(step, 0f, 0f))
            intents.Add(f.Step(TFField.Self(at, velocity: new AiVector(Speed, 0f, 0f)), TFField.Ctf(), sighted?.Invoke(decide++) ?? []));
        return intents;
    }

    private static bool Checks(in IntentCommand i) => i.Look.Kind == LookKind.CornerCheck && i.Look.Point.DistanceTo(Window.Centre) < 1f;

    [Fact]
    public void ABotComingIntoTheSpotChecksThePlaceDeathCameFrom()
    {
        TFField f = Runner(Deaths());
        List<IntentCommand> intents = Cross(f);
        int first = intents.FindIndex(i => Checks(in i));
        Assert.True(first >= 0, "no decide checked the place");
        // The first check is on coming into the stretch's first bin with a place (x 2,048), not before.
        float x = 1800f + first * Speed * DecideSchedule.Period * TickSeconds;
        Assert.InRange(x, FirstBin * WorldBin.Size, (FirstBin + 1) * WorldBin.Size + 64f);
        Assert.Equal(intents.Count(i => Checks(in i)), f.Decider.DangerLookDecides);
        Assert.Equal(1, f.Decider.DangerLooks.Checks);
    }

    [Fact]
    public void ACheckHoldsForItsSecondsAndThePlaceIsNotCheckedAgainAtOnce()
    {
        TFField f = Runner(Deaths(), seconds: 0.9f);
        List<IntentCommand> intents = Cross(f);
        float decideSeconds = DecideSchedule.Period * TickSeconds;
        int checks = intents.Count(i => Checks(in i));
        Assert.InRange(checks * decideSeconds, 0.9f - decideSeconds, 0.9f + decideSeconds);
        // After the check the look is the path's again (or a glance), though every bin of the stretch has the place.
        int last = intents.FindLastIndex(i => Checks(in i));
        Assert.True(last + 1 < intents.Count);
        Assert.All(intents.Skip(last + 1), i => Assert.NotEqual(LookKind.CornerCheck, i.Look.Kind));
        Assert.Equal(1, f.Decider.DangerLooks.Entries);
    }

    [Fact]
    public void WithAnEmptyStoreEveryIntentIsAsBefore()
    {
        TFField empty = Runner(MapExperience.Empty);
        List<IntentCommand> before = Cross(empty);
        Assert.DoesNotContain(before, i => i.Look.Kind == LookKind.CornerCheck);
        Assert.Equal(0, empty.Decider.DangerLooks.Entries);
        // The other team's deaths, and a killer at arm's length, change no intent either.
        TFField blue = Runner(Deaths(role: TFField.Blue));
        Assert.Equal(before, Cross(blue));
        Assert.Equal(0, blue.Decider.DangerLooks.Entries);
        Assert.Equal(before, Cross(Runner(Deaths(killer: x => new WorldBin(x, 5, 0)))));
        // The control: its own team's do.
        Assert.NotEqual(before, Cross(Runner(Deaths())));
    }

    [Fact]
    public void ATargetInSightComesFirst()
    {
        int first = Cross(Runner(Deaths())).FindIndex(i => Checks(in i));
        Assert.True(first > 0, "the control: a check");
        List<IntentCommand> intents = Cross(Runner(Deaths()), d => d >= first - 1 ? [(Enemy, new AiVector(3600f, 288f, 0f), AiVector.Zero, TFField.Blue)] : []);
        Assert.DoesNotContain(intents, i => i.Look.Kind == LookKind.CornerCheck);
    }

    /// <summary>The chance is drawn once a place on coming in: over seeds the share of crossings with a check is about it.</summary>
    [Fact]
    public void HowOftenABotChecksIsItsChance()
    {
        double Share(float chance) => Enumerable.Range(1, 40).Count(s => Cross(Runner(Deaths(), seed: (ulong)s, chance: chance)).Exists(i => Checks(in i))) / 40d;
        double low = Share(0.2f), high = Share(0.95f);
        Assert.InRange(low, 0.05, 0.4);
        Assert.InRange(high, 0.8, 1d);
    }

    /// <summary>The chance and the seconds rise with Alertness (the curves' defaults: no file has them).</summary>
    [Fact]
    public void AnAlertBotChecksMoreOftenAndLonger()
    {
        SkillCurves curves = Skills.Curves.Game();
        DecisionParams dull = curves.For(Profile(alertness: 0)), sharp = curves.For(Profile(alertness: 10));
        Assert.Equal(0.2f, dull.DangerLookChance, 3);
        Assert.Equal(0.95f, sharp.DangerLookChance, 3);
        Assert.True(sharp.DangerLookSeconds > dull.DangerLookSeconds);
    }

    private static SkillProfile Profile(int alertness)
    {
        SkillProfile hard = SkillPresets.TryGet("hard", out SkillProfile? p) ? p! : throw new InvalidOperationException();
        int i = hard.IndexOf("alertness");
        int[] levels = [.. hard.Levels];
        levels[i] = alertness;
        return new SkillProfile(hard.Name, hard.Skills, [.. levels]);
    }

    [Fact]
    public void TheSameSeedChecksAlike()
        => Assert.Equal(Cross(Runner(Deaths(), seed: 3, chance: 0.5f)), Cross(Runner(Deaths(), seed: 3, chance: 0.5f)));
}
