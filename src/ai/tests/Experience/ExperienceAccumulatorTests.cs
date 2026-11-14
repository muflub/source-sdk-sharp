using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The accumulator (plan_bots_teams_tf2 Part G, G.3's feed): sampled frames,
/// hits and kills become a delta's rows; a human weighs 1 and a bot a quarter
/// in <c>cell_stat</c> and <c>look</c>; nothing of who a player is gets in.
/// </summary>
public sealed class ExperienceAccumulatorTests
{
    private static readonly ExperienceContext Red = new(0, 1, 3), Blue = new(0, 2, 7);
    private static readonly AiVector HereAt = new(100f, 150f, 10f), ThereAt = new(600f, 150f, 10f);
    private static readonly WorldBin Here = new(1, 2, 0), There = new(9, 2, 0);

    private static ActorSample Human(AiVector at, ExperienceContext context, float yaw = 0f, float pitch = 0f) => new(at, yaw, pitch, context, Alive: true, Bot: false);

    private static ActorSample Bot(AiVector at, ExperienceContext context, float yaw = 0f, float pitch = 0f) => new(at, yaw, pitch, context, Alive: true, Bot: true);

    [Fact]
    public void AFramePutsALivingHumansSecondsInItsBin()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red)], 0.1d);
        acc.Frame([Human(HereAt, Red)], 0.1d);
        ExperienceDelta delta = acc.Take();
        Assert.Equal(new CellStat(0.2d, 0d, 0d, 0d, 0d), Assert.Single(delta.Cells).Value);
        Assert.Equal(new CellKey(Red, Here, ExperienceSource.Human), delta.Cells.Keys.Single());
    }

    [Fact]
    public void ABotsSecondsWeighAQuarterAndAreKeptApart()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red), Bot(HereAt, Red)], 1d);
        ExperienceDelta delta = acc.Take();
        Assert.Equal(1d, delta.Cells[new CellKey(Red, Here, ExperienceSource.Human)].Seconds);
        Assert.Equal(0.25d, delta.Cells[new CellKey(Red, Here, ExperienceSource.Bot)].Seconds);
    }

    [Fact]
    public void ADeadPlayerIsNotPresent()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red) with { Alive = false }], 1d);
        Assert.True(acc.Take().IsEmpty);
    }

    [Fact]
    public void AFrameOfNoTimeAddsNothing()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red)], 0d);
        Assert.True(acc.Take().IsEmpty);
        Assert.Equal(1, acc.Frames);
    }

    [Fact]
    public void AFrameCountsTheLookInItsYawAndPitchBucket()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red, yaw: 92f, pitch: -30f)], 0.5d);
        Assert.Equal(new KeyValuePair<LookKey, Tally>(new LookKey(Red, Here, 4, 0), new Tally(0.5d)), Assert.Single(acc.Take().Looks));
    }

    [Fact]
    public void ABotsLookWeighsAQuarter()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Bot(HereAt, Red, yaw: 180f)], 2d);
        Assert.Equal(0.5d, acc.Take().Looks[new LookKey(Red, Here, 8, 1)].Count);
    }

    [Fact]
    public void AHitIsDamageTakenWhereTheVictimStoodAndDealtWhereTheAttackerStood()
    {
        var acc = new ExperienceAccumulator();
        acc.Damage(new CombatSample(HereAt, Red, Bot: false), new CombatSample(ThereAt, Blue, Bot: false), 40d);
        ExperienceDelta delta = acc.Take();
        Assert.Equal(new CellStat(0d, 0d, 0d, 40d, 0d), delta.Cells[new CellKey(Red, Here, ExperienceSource.Human)]);
        Assert.Equal(new CellStat(0d, 0d, 0d, 0d, 40d), delta.Cells[new CellKey(Blue, There, ExperienceSource.Human)]);
    }

    [Fact]
    public void ABotsDamageWeighsAQuarterOnItsOwnSide()
    {
        var acc = new ExperienceAccumulator();
        acc.Damage(new CombatSample(HereAt, Red, Bot: true), new CombatSample(ThereAt, Blue, Bot: false), 40d);
        ExperienceDelta delta = acc.Take();
        Assert.Equal(10d, delta.Cells[new CellKey(Red, Here, ExperienceSource.Bot)].DamageTaken);
        Assert.Equal(40d, delta.Cells[new CellKey(Blue, There, ExperienceSource.Human)].DamageDealt);
    }

    [Fact]
    public void DamageFromTheWorldIsOnlyTaken()
    {
        var acc = new ExperienceAccumulator();
        acc.Damage(new CombatSample(HereAt, Red, Bot: false), null, 15d);
        Assert.Equal(new CellStat(0d, 0d, 0d, 15d, 0d), Assert.Single(acc.Take().Cells).Value);
    }

    [Fact]
    public void AKillIsADeathWhereTheVictimStoodAndAKillWhereTheKillerStood()
    {
        var acc = new ExperienceAccumulator();
        acc.Kill(new CombatSample(HereAt, Red, Bot: false), new CombatSample(ThereAt, Blue, Bot: true), weaponClass: 6);
        ExperienceDelta delta = acc.Take();
        Assert.Equal(new CellStat(0d, 1d, 0d, 0d, 0d), delta.Cells[new CellKey(Red, Here, ExperienceSource.Human)]);
        Assert.Equal(new CellStat(0d, 0d, 0.25d, 0d, 0d), delta.Cells[new CellKey(Blue, There, ExperienceSource.Bot)]);
    }

    [Fact]
    public void AKillIsOneOnTheSightLineUnderTheVictimsContextWhoeverKilled()
    {
        var acc = new ExperienceAccumulator();
        acc.Kill(new CombatSample(HereAt, Red, Bot: true), new CombatSample(ThereAt, Blue, Bot: true), weaponClass: 6);
        Assert.Equal(new KeyValuePair<SightKey, Tally>(new SightKey(Red, Here, There, 6), new Tally(1d)), Assert.Single(acc.Take().Sights));
    }

    [Fact]
    public void ADeathToTheWorldHasNoSightLineAndNoKill()
    {
        var acc = new ExperienceAccumulator();
        acc.Kill(new CombatSample(HereAt, Red, Bot: false), null, weaponClass: 0);
        ExperienceDelta delta = acc.Take();
        Assert.Empty(delta.Sights);
        Assert.Equal(new CellStat(0d, 1d, 0d, 0d, 0d), Assert.Single(delta.Cells).Value);
    }

    [Fact]
    public void ARoundsEndAddsItsSecondsItsWinnerAndItsMeanHeadCounts()
    {
        var acc = new ExperienceAccumulator();
        for (int i = 0; i < 100; i++)
            acc.Frame(i < 50 ? [Human(HereAt, Red), Bot(ThereAt, Blue), Bot(ThereAt, Blue)] : [Human(HereAt, Red), Bot(ThereAt, Blue)], 1d);
        acc.RoundEnd(winningRole: 2);
        RoundRow round = Assert.Single(acc.Take().Rounds);
        Assert.Equal((100d, 2, 1d, 1.5d, 1d), (round.Seconds, round.WinningRole, round.Humans, round.Bots, round.Weight));
    }

    [Fact]
    public void ARoundsCompositionIsEachRoleAndClasssMeanHeadCount()
    {
        var acc = new ExperienceAccumulator();
        var blueMedic = new ExperienceContext(0, 2, 5);
        var late = new ExperienceContext(0, 1, 9);
        for (int i = 0; i < 10; i++)
        {
            acc.Frame(i < 4 ? [Human(HereAt, Red), Bot(ThereAt, Blue), Bot(ThereAt, Blue), Bot(HereAt, blueMedic), Human(HereAt, late)]
                            : [Human(HereAt, Red), Bot(ThereAt, Blue), Bot(ThereAt, Blue), Bot(HereAt, blueMedic)], 1d);
        }

        acc.RoundEnd(winningRole: 1);
        // 1.9 was played for four tenths of the round: its mean rounds to none.
        Assert.Equal("1.3*1 2.5*1 2.7*2", Assert.Single(acc.Take().Rounds).Composition);
    }

    [Fact]
    public void TheNextRoundStartsFromNothing()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red)], 30d);
        acc.RoundEnd(winningRole: 1);
        acc.Frame([Bot(HereAt, Blue)], 10d);
        acc.RoundEnd(winningRole: 0);
        IReadOnlyList<RoundRow> rounds = acc.Take().Rounds;
        Assert.Equal(new RoundRow(30d, 1, 1d, 0d, "1.3*1"), rounds[0]);
        Assert.Equal(new RoundRow(10d, 0, 0d, 1d, "2.7*1"), rounds[1]);
    }

    [Fact]
    public void ARoundWithNoFramesAddsNoRow()
    {
        var acc = new ExperienceAccumulator();
        acc.RoundEnd(winningRole: 1);
        Assert.Empty(acc.Take().Rounds);
    }

    [Fact]
    public void ARoundStartDropsTheTalliesOfTheRoundCutShort()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red)], 30d);
        acc.RoundStart();
        acc.Frame([Human(HereAt, Red)], 5d);
        acc.RoundEnd(winningRole: 1);
        ExperienceDelta delta = acc.Take();
        Assert.Equal(5d, Assert.Single(delta.Rounds).Seconds);
        // What was seen in the round cut short is still experience.
        Assert.Equal(35d, Assert.Single(delta.Cells).Value.Seconds);
    }

    [Fact]
    public void TakeHandsOverTheDeltaAndTheAccumulatorGoesOnIntoANewOne()
    {
        var acc = new ExperienceAccumulator();
        acc.Frame([Human(HereAt, Red)], 1d);
        ExperienceDelta first = acc.Take();
        acc.Frame([Human(HereAt, Red)], 2d);
        acc.RoundEnd(winningRole: 1);
        ExperienceDelta second = acc.Take();
        Assert.Equal(1d, Assert.Single(first.Cells).Value.Seconds);
        Assert.Empty(first.Rounds);
        Assert.Equal(2d, Assert.Single(second.Cells).Value.Seconds);
        // The round's tallies are not the delta's: the round spans both.
        Assert.Equal(3d, Assert.Single(second.Rounds).Seconds);
    }

    [Fact]
    public void ASpotALinkAndAnOutcomeCountInFullWhoeverDidThem()
    {
        var acc = new ExperienceAccumulator();
        acc.Spot(Red, kind: 1, HereAt, yaw: 90f, new SpotStat(1d, 30d, 2d, 0d));
        acc.Spot(Red, kind: 1, HereAt, yaw: 95f, new SpotStat(1d, 10d, 0d, 1d));
        var link = new LinkKey(3, Here, There, 1, LinkSource.Practice, new LinkRecipe([9]));
        acc.Link(link, new LinkStat(1d, 1d, 2.5d, 45d));
        acc.Outcome(new OutcomeKey(Red, 2, 11L), new OutcomeStat(1d, 0.5d));
        ExperienceDelta delta = acc.Take();
        Assert.Equal(new KeyValuePair<SpotKey, SpotStat>(new SpotKey(Red, 1, Here, 4), new SpotStat(2d, 40d, 2d, 1d)), Assert.Single(delta.Spots));
        Assert.Equal(new LinkStat(1d, 1d, 2.5d, 45d), delta.Links[link]);
        Assert.Equal(new OutcomeStat(1d, 0.5d), delta.Outcomes[new OutcomeKey(Red, 2, 11L)]);
    }

    private static ulong Played()
    {
        var acc = new ExperienceAccumulator();
        var random = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            var at = new AiVector(random.Next(-2000, 2000), random.Next(-2000, 2000), random.Next(0, 300));
            acc.Frame([Human(at, Red, random.Next(360), random.Next(-80, 80)), Bot(ThereAt, Blue, random.Next(360))], 0.105d);
            if (i % 37 == 0)
                acc.Damage(new CombatSample(at, Red, false), new CombatSample(ThereAt, Blue, true), random.Next(1, 90));
            if (i % 131 == 0)
                acc.Kill(new CombatSample(at, Red, false), new CombatSample(ThereAt, Blue, true), i % 5);
        }

        acc.RoundEnd(winningRole: 2);
        return new MapExperience(new ExperienceKey("m", "c", "ctf"), 1, acc.Take()).ContentHash;
    }

    [Fact]
    public void TheSameCallsInTheSameOrderGiveTheSameDelta()
    {
        Assert.Equal(Played(), Played());
    }

    /// <summary>
    /// No name, SteamID or slot reaches a delta: every field of a sample and of
    /// a delta's rows, followed down to its leaves, is a number, a flag or a
    /// place, and none is named for who a player is. The one text is a round's
    /// composition, which <see cref="ARoundsCompositionIsEachRoleAndClasssMeanHeadCount"/> pins.
    /// </summary>
    [Fact]
    public void NoSampleAndNoRowCanHoldWhoAPlayerIs()
    {
        var leaves = new List<(Type Owner, FieldInfo Field)>();
        var seen = new HashSet<Type>();
        foreach (Type start in new[] { typeof(ExperienceDelta), typeof(ActorSample), typeof(CombatSample) })
            Leaves(start, seen, leaves);

        Assert.Contains(leaves, l => l.Owner == typeof(CellKey));
        Assert.Contains(leaves, l => l.Owner == typeof(RoundRow));
        Assert.Contains(leaves, l => l.Owner == typeof(ActorSample));
        var who = new Regex("name|steam|slot|user|actor|player|identity|serial|handle|account|address", RegexOptions.IgnoreCase);
        Type[] numbers = [typeof(int), typeof(long), typeof(float), typeof(double), typeof(bool), typeof(byte), typeof(ExperienceSource), typeof(LinkSource)];
        foreach ((Type owner, FieldInfo field) in leaves)
        {
            Assert.False(who.IsMatch(field.Name), $"{owner.Name}.{field.Name} is named for who a player is");
            Assert.NotEqual(typeof(ActorId), field.FieldType);
            bool text = field.FieldType == typeof(string);
            Assert.True(numbers.Contains(field.FieldType) || (text && owner == typeof(RoundRow)),
                        $"{owner.Name}.{field.Name} is a {field.FieldType.Name}: a row holds numbers, flags and places only");
        }
    }

    /// <summary>The fields of <paramref name="type"/> that are not themselves of this assembly's types or collections of them.</summary>
    private static void Leaves(Type type, HashSet<Type> seen, List<(Type Owner, FieldInfo Field)> into)
    {
        if (!seen.Add(type))
            return;
        if (type.IsGenericType)
        {
            foreach (Type arg in type.GetGenericArguments())
                Leaves(arg, seen, into);
        }

        if (type.Assembly != typeof(ExperienceDelta).Assembly || type.IsEnum)
            return;
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Type of = field.FieldType;
            if (of.IsGenericType || (of.Assembly == typeof(ExperienceDelta).Assembly && !of.IsEnum))
                Leaves(of, seen, into);
            else
                into.Add((type, field));
        }
    }
}
