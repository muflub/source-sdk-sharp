using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy.Model;
using Xunit;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// RU14's boss gates (plan_descent_logic RU14, HO13; plan_tf2_rpg D13; mine plan M14) over the
/// shipped content: each boss fought where the model's own descent brings a hero or a party,
/// at the level and in the gear the run gave them (seeds 1 and 2), the party whole as it goes
/// in. The bands are economy_model.json's <c>boss</c>: the depth-10 band is M14's 10 to 20
/// minutes; 5's and 15's are the model's own (the design is silent). Each gate has a planted
/// imbalance that turns it red.
/// </summary>
public sealed class EconomyModelBossGateTests
{
    private static readonly EconomyModel Model = new(DescentContent.Shipped());
    private static readonly ModelGates Gates = new(Model);
    private static readonly string[] Builds = ["artillery", "gunman", "trolldier", "sergeant"];
    private static readonly ulong[] Seeds = [1, 2];

    private static readonly Lazy<IReadOnlyList<RunReport>> SoloRuns =
        new(() => [.. Builds.SelectMany(b => Seeds.Select(s => Model.Run([b], s)))]);

    private static readonly Lazy<IReadOnlyList<RunReport>> PartyRuns =
        new(() => [.. Seeds.Select(s => Model.Run(Builds, s))]);

    private static IEnumerable<(RunReport Run, int Depth, IReadOnlyList<int> Levels, FightResult Fight)> Fights(IEnumerable<RunReport> runs) =>
        runs.SelectMany(r => ModelGates.BossFights(r).Select(f => (r, f.Depth, f.Levels, f.Fight)));

    private static string Say(RunReport run, int depth, IReadOnlyList<int> levels, FightResult f) =>
        $"{string.Join("+", run.Builds)} seed {run.Seed} depth {depth} level {string.Join("/", levels)}: {f.Seconds:F1} s cleared {f.Cleared} deaths {f.Deaths}"
        + $" hit {f.LargestHitShare:P0} special {f.LargestSpecialShare:P0} taken {f.TakenShare:P0} roof {f.RoofFell}";

    //-------------------------------------------------------------------------
    // Boss gate 1: no single hit kills a hero at full life
    //-------------------------------------------------------------------------

    /// <summary>No single attack of a boss's own takes more than half the life of the hero it strikes, solo or in the party, at each boss depth.</summary>
    [Fact]
    public void Boss_gate1_no_ordinary_boss_hit_takes_more_than_half_a_heros_life()
    {
        var fights = Fights([.. SoloRuns.Value, .. PartyRuns.Value]).ToList();

        Assert.Equal(Builds.Length * Seeds.Length * 3 + Seeds.Length * 3, fights.Count);
        Assert.All(fights, x => Assert.True(Gates.OrdinaryHitHolds(x.Fight), Say(x.Run, x.Depth, x.Levels, x.Fight)));
        Assert.All(fights, x => Assert.True(x.Fight.LargestHitShare > 0, "the stimulus: " + Say(x.Run, x.Depth, x.Levels, x.Fight)));
    }

    /// <summary>
    /// No single shot or slam of a boss's special takes a hero's whole life: the design gives no
    /// boss a telegraphed blow that kills outright (D13 and HO13 say only "a telegraphed special a phase").
    /// </summary>
    [Fact]
    public void Boss_gate1_no_single_shot_of_a_special_kills_a_hero_at_full_life()
    {
        var fights = Fights([.. SoloRuns.Value, .. PartyRuns.Value]).ToList();

        Assert.All(fights, x => Assert.True(Gates.SpecialHitHolds(x.Fight), Say(x.Run, x.Depth, x.Levels, x.Fight)));
        Assert.All(fights, x => Assert.True(x.Fight.LargestSpecialShare > 0, "the stimulus: " + Say(x.Run, x.Depth, x.Levels, x.Fight)));
    }

    [Fact]
    public void Boss_gate1_fails_when_major_crits_hits_a_hundred_times_as_hard()
    {
        var planted = new EconomyModel(PlantedContent.With(FormattableString.Invariant($"robots:[major_crits].damage={Model.Rules.Content.Robots["major_crits"].Damage * 100}")));
        var fight = ModelGates.BossFights(planted.Run(["gunman"], 1)).Single(f => f.Depth == 10).Fight;

        Assert.False(new ModelGates(planted).OrdinaryHitHolds(fight), $"hit {fight.LargestHitShare:P0}");
    }

    [Fact]
    public void Boss_gate1_fails_when_punchs_slam_is_planted_at_a_hundred_times_his_damage()
    {
        var planted = new EconomyModel(PlantedContent.With("bosses:specials.[ground_slam].damageShare=100"));
        var fight = ModelGates.BossFights(planted.Run(["gunman"], 1)).Single(f => f.Depth == 15).Fight;

        Assert.False(new ModelGates(planted).SpecialHitHolds(fight), $"special {fight.LargestSpecialShare:P0}");
    }

    //-------------------------------------------------------------------------
    // Boss gate 2: a solo fight's length is inside its depth's band
    //-------------------------------------------------------------------------

    /// <summary>Each of economy_model.json's solo builds (A.6's first three) wins the boss of <paramref name="depth"/> alone, inside the depth's band.</summary>
    private static void SoloFightsHoldTheirBand(int depth)
    {
        var solo = SoloRuns.Value.Where(r => Model.Rules.Model.Boss.SoloBuilds.Contains(r.Builds[0])).ToList();
        var fights = Fights(solo).Where(x => x.Depth == depth).ToList();

        Assert.Equal(Model.Rules.Model.Boss.SoloBuilds.Count * Seeds.Length, fights.Count);
        Assert.All(fights, x => Assert.True(Gates.SoloLengthHolds(x.Depth, x.Fight), Say(x.Run, x.Depth, x.Levels, x.Fight)));
    }

    /// <summary>Sir Nukesalot alone: won inside 60 to 240 s (the model's band; the design is silent).</summary>
    [Fact]
    public void Boss_gate2_a_solo_fight_at_depth_5_is_won_inside_its_band() => SoloFightsHoldTheirBand(5);

    /// <summary>
    /// Major Crits and the Tank alone: won inside 90 to 360 s (the user, 2026-11-22, plan L26:
    /// depth 10's fight is shortened, between its neighbours' bands; M14's 10 to 20 minutes was
    /// longer than a solo hero lives through the fight: an earlier lane measured Major Crits at
    /// life 19,200 losing two of six solo fights and the builds 2.5 times apart). Shipped (life
    /// 8,000, damage 12), seeds 1 and 2: won in 214.0 to 354.2 s.
    /// </summary>
    [Fact]
    public void Boss_gate2_a_solo_fight_at_depth_10_is_won_inside_its_band() => SoloFightsHoldTheirBand(10);

    /// <summary>Every A.6 build, the Sergeant too, wins every boss alone where its own descent brings it (the band's other half, which holds at depth 10 as well).</summary>
    [Fact]
    public void Boss_gate2_every_build_wins_each_boss_alone()
    {
        var fights = Fights(SoloRuns.Value).ToList();

        Assert.Equal(Builds.Length * Seeds.Length * 3, fights.Count);
        Assert.All(fights, x => Assert.True(x.Fight.Cleared, Say(x.Run, x.Depth, x.Levels, x.Fight)));
    }

    [Fact]
    public void Boss_gate2_fails_when_sir_nukesalot_hits_fifty_times_as_hard()
    {
        var planted = new EconomyModel(PlantedContent.With(FormattableString.Invariant($"robots:[sir_nukesalot].damage={Model.Rules.Content.Robots["sir_nukesalot"].Damage * 50}")));
        var fight = ModelGates.BossFights(planted.Run(["gunman"], 1)).Single(f => f.Depth == 5).Fight;

        Assert.False(fight.Cleared, $"{fight.Seconds:F1} s, {fight.Deaths} deaths");
    }

    /// <summary>Captain Punch alone: won inside 120 to 480 s (the model's band; the design is silent).</summary>
    [Fact]
    public void Boss_gate2_a_solo_fight_at_depth_15_is_won_inside_its_band() => SoloFightsHoldTheirBand(15);

    [Fact]
    public void Boss_gate2_fails_when_sir_nukesalot_has_a_tenth_of_his_life()
    {
        var planted = new EconomyModel(PlantedContent.With(FormattableString.Invariant($"robots:[sir_nukesalot].life={Model.Rules.Content.Robots["sir_nukesalot"].Life / 10}")));
        var fight = ModelGates.BossFights(planted.Run(["gunman"], 1)).Single(f => f.Depth == 5).Fight;

        Assert.True(fight.Cleared);
        Assert.False(new ModelGates(planted).SoloLengthHolds(5, fight), $"{fight.Seconds:F1} s");
    }

    //-------------------------------------------------------------------------
    // Boss gate 3: a party of four, neither impossible nor trivial
    //-------------------------------------------------------------------------

    /// <summary>A party of the four A.6 builds wins each boss (not impossible).</summary>
    [Fact]
    public void Boss_gate3_a_party_of_four_wins_each_boss()
    {
        var fights = Fights(PartyRuns.Value).ToList();

        Assert.Equal(Seeds.Length * 3, fights.Count);
        Assert.All(fights, x => Assert.True(x.Fight.Cleared, Say(x.Run, x.Depth, x.Levels, x.Fight)));
    }

    /// <summary>A party of four is not trivial against any boss: it takes a quarter of its life in damage and the fight lasts a minute at least.</summary>
    [Fact]
    public void Boss_gate3_a_party_of_four_is_not_trivial()
    {
        var fights = Fights(PartyRuns.Value).ToList();

        Assert.All(fights, x => Assert.True(Gates.PartyNotTrivial(x.Fight), Say(x.Run, x.Depth, x.Levels, x.Fight)));
    }

    [Fact]
    public void Boss_gate3_fails_impossible_when_captain_punch_hits_a_hundred_times_as_hard()
    {
        var planted = new EconomyModel(PlantedContent.With(FormattableString.Invariant($"robots:[captain_punch].damage={Model.Rules.Content.Robots["captain_punch"].Damage * 100}")));
        var fight = ModelGates.BossFights(planted.Run(Builds, 1)).Single(f => f.Depth == 15).Fight;

        Assert.False(fight.Cleared, $"{fight.Seconds:F1} s, {fight.Deaths} deaths");
    }

    [Fact]
    public void Boss_gate3_fails_trivial_when_captain_punch_has_a_twentieth_of_his_life()
    {
        var planted = new EconomyModel(PlantedContent.With(FormattableString.Invariant($"robots:[captain_punch].life={Model.Rules.Content.Robots["captain_punch"].Life / 20}")));
        var fight = ModelGates.BossFights(planted.Run(Builds, 1)).Single(f => f.Depth == 15).Fight;

        Assert.True(fight.Cleared);
        Assert.False(new ModelGates(planted).PartyNotTrivial(fight), $"{fight.Seconds:F1} s, taken {fight.TakenShare:P0}");
    }
}
