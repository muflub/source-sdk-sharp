using System.Collections.Immutable;
using System.Text;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;
using Xunit;

namespace TF2.Descent.Tests.Sheets;

/// <summary>
/// Sheet codec 2 (plan_descent_logic RU13): the whole hero in the sheet's bytes, a byte
/// for byte round trip (the host's admin edit requires one), and schema 1 sheets still
/// decoding into a consistent schema 2 hero.
/// </summary>
public sealed class SheetCodecTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;
    private static SheetCodec Codec => Rules.SheetCodec;

    private static ImmutableSortedDictionary<string, int> Map(params (string Key, int Value)[] entries) =>
        entries.ToImmutableSortedDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    /// <summary>A level 8 Demoman with every field of the sheet holding something.</summary>
    private static HeroSheet Full()
    {
        var hero = Heroes.GainXp(Heroes.New("demoman", "Tavish"), Heroes.XpAtLevel(8)).Sheet;
        hero = Heroes.Spend(Heroes.Spend(hero, "brawn", 5).Sheet, "grit", 5).Sheet;
        return hero with
        {
            ReachedDepth = 4,
            Hardcore = true,
            SkillRanks = Map(("leech", 2), ("deep_pockets", 1)),
            SkillPoints = hero.SkillPoints - 3,
            SpellRanks = Map(("fireball", 3)),
            Bestiary = Map(("soldier_bot", 17), ("scout_bot", 3)),
            Belt = ["sandvich", "bonk", "sandvich"],
            RespecCount = 1,
            BossKills = ["sir_nukesalot"],
        };
    }

    private const string V1 =
        """{"ClassName":"soldier","Name":"Old","Level":4,"Xp":0,"ReachedDepth":3,"Hardcore":false,"Fallen":false}""";

    [Fact]
    public void A_new_character_is_a_schema_2_sheet_of_a_level_1_hero()
    {
        var sheet = Rules.NewCharacter("Medic", "Ludwig", hardcore: true);
        var hero = Codec.Read(sheet);

        Assert.Equal(2, sheet.SchemaVersion);
        Assert.Equal(2, Rules.Sheets.SchemaVersion);
        Assert.Equal(("medic", "Ludwig", 1, true), (hero.ClassId, hero.Name, hero.Level, hero.Hardcore));
        Assert.True(Heroes.Audit(hero).Ok);
    }

    [Fact]
    public void Every_field_of_the_hero_survives_the_codec()
    {
        var hero = Full();
        Assert.True(Heroes.Audit(hero).Ok);

        var back = Codec.Read(Codec.Write(hero));

        Assert.Equal((hero.ClassId, hero.Name, hero.Level, hero.Xp, hero.ReachedDepth, hero.Hardcore, hero.Fallen),
            (back.ClassId, back.Name, back.Level, back.Xp, back.ReachedDepth, back.Hardcore, back.Fallen));
        Assert.Equal(hero.Attributes, back.Attributes);
        Assert.Equal((hero.AttributePoints, hero.SkillPoints, hero.RespecCount), (back.AttributePoints, back.SkillPoints, back.RespecCount));
        Assert.Equal(hero.SkillRanks, back.SkillRanks);
        Assert.Equal(hero.SpellRanks, back.SpellRanks);
        Assert.Equal(hero.Bestiary, back.Bestiary);
        Assert.Equal(hero.Belt.ToArray(), back.Belt.ToArray());   // ImmutableArray's own Equals is by reference
        Assert.Equal(hero.BossKills, back.BossKills);
    }

    [Fact]
    public void A_schema_2_sheet_round_trips_byte_for_byte_through_bytes_and_json()
    {
        var sheet = Codec.Write(Full());

        Assert.Equal(sheet.Data, Codec.Write(Codec.Read(sheet)).Data);
        var again = Rules.Sheets.FromJson(Rules.Sheets.ToJson(sheet));   // the host's admin-edit check
        Assert.Equal(sheet.Data, again.Data);
        Assert.Equal(sheet.SchemaVersion, again.SchemaVersion);
    }

    [Fact]
    public void A_schema_1_sheet_decodes_at_its_class_starts_with_every_point_unspent()
    {
        var hero = Codec.Read(new CharacterSheet(Encoding.UTF8.GetBytes(V1), 1));

        Assert.Equal(("soldier", "Old", 4, 3), (hero.ClassId, hero.Name, hero.Level, hero.ReachedDepth));
        Assert.Equal(Heroes.New("soldier", "x").Attributes, hero.Attributes);
        Assert.Equal((15, 3), (hero.AttributePoints, hero.SkillPoints));
        Assert.Equal(Heroes.XpAtLevel(4), hero.Xp);   // raised to what level 4 takes
        Assert.True(Heroes.Audit(hero).Ok);
    }

    [Fact]
    public void A_schema_1_sheet_round_trips_into_schema_2_and_validates()
    {
        var v1 = new CharacterSheet(Encoding.UTF8.GetBytes(V1), 1);

        var v2 = Codec.Write(Codec.Read(v1));

        Assert.Equal(2, v2.SchemaVersion);
        Assert.Equal(v2.Data, Codec.Write(Codec.Read(v2)).Data);
        Assert.True(Rules.ValidateCheckpoint(v1, v2, new CheckpointEvidence([], 0)).Ok);
        Assert.True(Rules.ValidateCheckpoint(v1, v1, new CheckpointEvidence([], 0)).Ok);
    }

    [Fact]
    public void Schema_1_json_is_taken_by_the_admin_path_and_comes_back_as_schema_2()
    {
        var sheet = Rules.Sheets.FromJson(V1);

        Assert.Equal(2, sheet.SchemaVersion);
        Assert.Equal("soldier", Codec.Read(sheet).ClassId);
    }

    [Fact]
    public void Summarize_answers_the_hosts_columns_for_both_schemas()
    {
        var v1 = Rules.Sheets.Summarize(new CharacterSheet(Encoding.UTF8.GetBytes(V1), 1));
        var v2 = Rules.Sheets.Summarize(Codec.Write(Full()));

        Assert.Equal(new SheetSummary("soldier", "Old", 4, Heroes.XpAtLevel(4), 3, false, false), v1);
        Assert.Equal(new SheetSummary("demoman", "Tavish", 8, Heroes.XpAtLevel(8), 4, true, false), v2);
    }

    [Fact]
    public void A_field_the_codec_does_not_know_is_refused()
    {
        var json = Rules.Sheets.ToJson(Codec.Write(Full())).Replace("\"respecs\":", "\"gold\":1,\"respecs\":");

        Assert.Throws<InvalidDataException>(() => Rules.Sheets.FromJson(json));
    }

    [Fact]
    public void A_sheet_of_an_unknown_schema_is_refused()
    {
        var sheet = Codec.Write(Full());

        Assert.Throws<InvalidDataException>(() => Codec.Read(sheet with { SchemaVersion = 3 }));
    }

    [Fact]
    public void A_schema_1_sheet_of_a_class_that_does_not_exist_is_refused()
    {
        var v1 = Encoding.UTF8.GetBytes(V1.Replace("soldier", "bard"));

        Assert.Throws<InvalidDataException>(() => Codec.Read(new CharacterSheet(v1, 1)));
    }
}
