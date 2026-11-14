using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;
using Xunit;

namespace TF2.Descent.Tests.Sheets;

/// <summary>
/// ValidateCheckpoint (plan_descent_logic RU13, plan_tf2_rpg 3c and C.3): a sheet play
/// cannot reach is refused naming its rule, and what grew must be what the evidence says
/// was earned.
/// </summary>
public sealed class CheckpointTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;
    private static SheetCodec Codec => Rules.SheetCodec;
    private static EvidenceCodec Evidence => Rules.Evidence;

    private static readonly CheckpointEvidence NoEvidence = new([], 0);

    private static ImmutableSortedDictionary<string, int> Map(params (string Key, int Value)[] entries) =>
        entries.ToImmutableSortedDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    private static CheckpointEvidence Earned(long xp, params (string Template, int Kills)[] kills) =>
        Evidence.Write(new CheckpointProof(xp, Map(kills)));

    private static ValidationResult Check(HeroSheet before, HeroSheet after, CheckpointEvidence evidence) =>
        Rules.ValidateCheckpoint(Codec.Write(before), Codec.Write(after), evidence);

    private static HeroSheet Level(int level, bool hardcore = false) =>
        Heroes.GainXp(Heroes.New("soldier", "s") with { Hardcore = hardcore }, Heroes.XpAtLevel(level)).Sheet;

    [Fact]
    public void An_unchanged_sheet_passes_with_no_evidence()
    {
        var hero = Level(6);

        Assert.True(Check(hero, hero, NoEvidence).Ok);
    }

    [Fact]
    public void A_forged_sheet_with_one_extra_attribute_point_is_refused_naming_the_rule()
    {
        var hero = Level(6);
        var forged = hero with { AttributePoints = hero.AttributePoints + 1 };

        var verdict = Check(hero, forged, NoEvidence);

        Assert.False(verdict.Ok);
        Assert.Equal("attribute_points", verdict.FailedRule);
    }

    [Fact]
    public void A_forged_point_spent_into_an_attribute_is_refused_naming_the_rule()
    {
        var hero = Level(6);
        var forged = hero with { Attributes = hero.Attributes.SetItem("aim", hero.Base("aim") + 1) };

        Assert.Equal("attribute_points", Check(hero, forged, NoEvidence).FailedRule);
    }

    [Fact]
    public void A_base_over_its_cap_is_refused()
    {
        var hero = Level(30);
        var forged = hero with { Attributes = hero.Attributes.SetItem("smarts", 51), AttributePoints = hero.AttributePoints - 46 };

        Assert.Equal("attribute_over_cap", Check(hero, forged, NoEvidence).FailedRule);
    }

    [Fact]
    public void Experience_beyond_what_the_evidence_allows_is_refused()
    {
        var hero = Level(3);
        var after = Heroes.GainXp(hero, 300).Sheet;

        Assert.Equal("xp_unexplained", Check(hero, after, Earned(299, ("soldier", 10))).FailedRule);
        Assert.True(Check(hero, after, Earned(300, ("soldier", 10))).Ok);   // the kills pay for it (RU4's bound)
    }

    [Fact]
    public void A_level_up_the_evidence_explains_passes_with_its_points_spent()
    {
        var hero = Level(3);
        var gain = Heroes.GainXp(hero, Heroes.XpToNext(3));
        var after = Heroes.Spend(gain.Sheet, "brawn", 5).Sheet with { SkillRanks = Map(("marksmanship", 1)), SkillPoints = gain.Sheet.SkillPoints - 1 };

        Assert.Equal(4, after.Level);
        Assert.True(Check(hero, after, Earned(Heroes.XpToNext(3), ("soldier", 10))).Ok);
    }

    [Fact]
    public void A_level_its_experience_does_not_reach_is_refused()
    {
        var hero = Level(3);
        var forged = hero with { Level = 4, AttributePoints = hero.AttributePoints + 5, SkillPoints = hero.SkillPoints + 1 };

        Assert.Equal("xp_level_mismatch", Check(hero, forged, Earned(1_000_000)).FailedRule);
    }

    [Fact]
    public void Experience_cannot_fall()
    {
        var hero = Level(3);

        Assert.Equal("xp_decreased", Check(Heroes.GainXp(hero, 10).Sheet, hero, NoEvidence).FailedRule);
    }

    [Fact]
    public void A_fallen_hardcore_hero_does_not_rise()
    {
        var fallen = Level(5, hardcore: true) with { Fallen = true };

        Assert.True(Check(Level(5, hardcore: true), fallen, NoEvidence).Ok);
        Assert.Equal("unfallen", Check(fallen, fallen with { Fallen = false }, NoEvidence).FailedRule);
    }

    [Fact]
    public void Only_a_hardcore_hero_falls()
    {
        var hero = Level(5);

        Assert.Equal("fallen_softcore", Check(hero, hero with { Fallen = true }, NoEvidence).FailedRule);
    }

    [Fact]
    public void Hardcore_cannot_be_switched()
    {
        var hero = Level(5, hardcore: true);

        Assert.Equal("hardcore_changed", Check(hero, hero with { Hardcore = false }, NoEvidence).FailedRule);
    }

    [Fact]
    public void A_point_comes_out_of_an_attribute_only_through_a_respec()
    {
        var hero = Heroes.Spend(Level(5), "aim", 5).Sheet;
        var moved = hero with { Attributes = hero.Attributes.SetItem("aim", hero.Base("aim") - 5).SetItem("grit", hero.Base("grit") + 5) };
        var respecced = Heroes.Respec(hero).Sheet;

        Assert.Equal("attribute_decreased", Check(hero, moved, NoEvidence).FailedRule);
        Assert.True(Check(hero, respecced, NoEvidence).Ok);
    }

    [Fact]
    public void A_bestiary_count_beyond_the_kills_in_the_evidence_is_refused()
    {
        var hero = Level(5) with { Bestiary = Map(("soldier_bot", 4)) };
        var after = hero with { Bestiary = Map(("soldier_bot", 7), ("scout_bot", 1)) };

        Assert.Equal("bestiary_unexplained", Check(hero, after, Earned(0, ("soldier_bot", 2), ("scout_bot", 1))).FailedRule);
        Assert.True(Check(hero, after, Earned(0, ("soldier_bot", 3), ("scout_bot", 1))).Ok);
    }

    [Fact]
    public void A_bestiary_count_cannot_fall()
    {
        var hero = Level(5) with { Bestiary = Map(("soldier_bot", 4)) };

        Assert.Equal("bestiary_decreased", Check(hero, hero with { Bestiary = Map(("soldier_bot", 3)) }, NoEvidence).FailedRule);
        Assert.Equal("bestiary_decreased", Check(hero, hero with { Bestiary = Map() }, NoEvidence).FailedRule);
    }

    [Fact]
    public void A_boss_kill_is_never_lost()
    {
        var hero = Level(10) with { BossKills = ["sir_nukesalot"] };

        Assert.Equal("boss_kill_lost", Check(hero, hero with { BossKills = [] }, NoEvidence).FailedRule);
    }

    [Fact]
    public void A_class_change_is_refused()
    {
        var hero = Level(5);

        Assert.Equal("class_changed", Check(hero, Heroes.New("medic", "s"), NoEvidence).FailedRule);
    }

    //-------------------------------------------------------------------------
    // The evidence
    //-------------------------------------------------------------------------

    [Fact]
    public void Evidence_round_trips_and_an_empty_payload_is_no_evidence()
    {
        var proof = new CheckpointProof(1234, Map(("soldier_bot", 3), ("medic_bot", 1)));

        var payload = Evidence.Write(proof);
        var back = Evidence.Read(payload);

        Assert.Equal(1, payload.SchemaVersion);
        Assert.Equal((1234L, 3, 1), (back.XpEarned, back.KillsOf("soldier_bot"), back.KillsOf("medic_bot")));
        Assert.Equal(0, Evidence.Read(NoEvidence).XpEarned);
        Assert.Equal(0, Evidence.Read(new CheckpointEvidence([], 1)).XpEarned);
    }

    [Fact]
    public void Evidence_that_does_not_decode_is_refused()
    {
        var hero = Level(3);

        Assert.Equal("evidence_malformed", Check(hero, hero, new CheckpointEvidence("{\"xp\":-5,\"kills\":{}}"u8.ToArray(), 1)).FailedRule);
        Assert.Equal("evidence_malformed", Check(hero, hero, new CheckpointEvidence("not json"u8.ToArray(), 1)).FailedRule);
        Assert.Equal("evidence_malformed", Check(hero, hero, new CheckpointEvidence("{}"u8.ToArray(), 7)).FailedRule);
    }

    [Fact]
    public void Evidence_past_its_size_bound_is_refused()
    {
        // Valid JSON padded with whitespace: only the size bound can refuse it.
        var hero = Level(3);
        CheckpointEvidence Padded(int size)
        {
            var json = "{\"xp\":0,\"kills\":{}}";
            return new CheckpointEvidence(System.Text.Encoding.UTF8.GetBytes(json + new string(' ', size - json.Length)), 1);
        }

        Assert.True(Check(hero, hero, Padded(EvidenceCodec.MaxBytes)).Ok);
        Assert.Equal("evidence_malformed", Check(hero, hero, Padded(EvidenceCodec.MaxBytes + 1)).FailedRule);
    }

    [Fact]
    public void A_sheet_that_does_not_decode_is_refused()
    {
        var hero = Codec.Write(Level(3));

        Assert.Equal("sheet_malformed", Rules.ValidateCheckpoint(hero, new CharacterSheet("{}"u8.ToArray(), 2), NoEvidence).FailedRule);
    }
}
