using System.Text;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;
using TF2.Descent.Tests.Skills;
using Xunit;

namespace TF2.Descent.Tests.Sheets;

/// <summary>
/// The skill bar on the sheet (sheet schema 3): which skill sits in each of the bar's slots,
/// so a hosted hero's bar is kept by the host and an unhosted one's by the sheet. Schema 1
/// and 2 sheets still decode, with every slot empty; the audit holds a slotted skill to a
/// learned cast skill; a respec empties the bar; a checkpoint may change the bar with no
/// evidence.
/// </summary>
public sealed class SkillSlotSheetTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;
    private static SheetCodec Codec => Rules.SheetCodec;

    private static readonly CheckpointEvidence NoEvidence = new([], 0);

    /// <summary>A level 3 Soldier with Patriot Missile and Marksmanship learned and nothing slotted.</summary>
    private static HeroSheet Learned() =>
        SkillRulesTests.Learn(SkillRulesTests.AtLevel("soldier", 3), "patriot_missile", "marksmanship");

    /// <summary>The same Soldier with Patriot Missile in slot 2.</summary>
    private static HeroSheet Slotted() => Heroes.WithSlot(Learned(), 2, "patriot_missile");

    /// <summary>Schema 2 bytes of <paramref name="sheet"/>: schema 3's JSON without its "slots".</summary>
    private static CharacterSheet AsSchema2(HeroSheet sheet)
    {
        string json = Encoding.UTF8.GetString(SheetCodec.Encode(sheet));
        int at = json.IndexOf(",\"slots\":", StringComparison.Ordinal);
        Assert.True(at > 0, json);
        return new CharacterSheet(Encoding.UTF8.GetBytes(json[..at] + "}"), SheetCodec.Schema2);
    }

    [Fact]
    public void The_sheet_is_schema_3()
    {
        Assert.Equal(3, Rules.Sheets.SchemaVersion);
        Assert.Equal(3, Rules.NewCharacter("soldier", "Jane", hardcore: false).SchemaVersion);
    }

    [Fact]
    public void A_new_hero_has_six_empty_slots()
    {
        Assert.Equal(["", "", "", "", "", ""], Heroes.New("soldier", "Jane").SkillSlots.ToArray());
    }

    [Fact]
    public void Slotting_a_learned_action_puts_it_in_that_slot_and_the_sheet_audits()
    {
        var sheet = Slotted();

        Assert.Equal(["", "patriot_missile", "", "", "", ""], sheet.SkillSlots.ToArray());
        Assert.True(Heroes.Audit(sheet).Ok, Heroes.Audit(sheet).Detail);
    }

    [Fact]
    public void Slotting_a_skill_already_slotted_moves_it()
    {
        var sheet = Heroes.WithSlot(Slotted(), 5, "patriot_missile");

        Assert.Equal(["", "", "", "", "patriot_missile", ""], sheet.SkillSlots.ToArray());
    }

    [Fact]
    public void The_slots_survive_the_codec_byte_for_byte()
    {
        var sheet = Codec.Write(Slotted());

        Assert.Equal(3, sheet.SchemaVersion);
        Assert.Equal(Slotted().SkillSlots.ToArray(), Codec.Read(sheet).SkillSlots.ToArray());
        Assert.Equal(sheet.Data, Codec.Write(Codec.Read(sheet)).Data);
        Assert.Equal(sheet.Data, Rules.Sheets.FromJson(Rules.Sheets.ToJson(sheet)).Data);
    }

    [Fact]
    public void A_schema_2_sheet_decodes_with_every_slot_empty_and_its_other_fields_kept()
    {
        var learned = Learned();

        var hero = Codec.Read(AsSchema2(learned));

        Assert.Equal(["", "", "", "", "", ""], hero.SkillSlots.ToArray());
        Assert.Equal(learned.SkillRanks, hero.SkillRanks);
        Assert.Equal((learned.Level, learned.Xp, learned.SkillPoints), (hero.Level, hero.Xp, hero.SkillPoints));
    }

    [Fact]
    public void A_schema_2_sheet_round_trips_into_schema_3_and_the_checkpoint_validates()
    {
        var v2 = AsSchema2(Learned());

        var v3 = Codec.Write(Codec.Read(v2));

        Assert.Equal(3, v3.SchemaVersion);
        Assert.Equal(v3.Data, Codec.Write(Codec.Read(v3)).Data);
        Assert.True(Rules.ValidateCheckpoint(v2, v3, NoEvidence).Ok);
    }

    [Fact]
    public void Schema_2_json_is_taken_by_the_admin_path_and_comes_back_as_schema_3()
    {
        string json = Encoding.UTF8.GetString(AsSchema2(Learned()).Data);

        var sheet = Rules.Sheets.FromJson(json);

        Assert.Equal(3, sheet.SchemaVersion);
        Assert.Equal(["", "", "", "", "", ""], Codec.Read(sheet).SkillSlots.ToArray());
    }

    [Fact]
    public void Summarize_is_the_same_for_schema_2_and_3()
    {
        var learned = Learned();

        Assert.Equal(Rules.Sheets.Summarize(AsSchema2(learned)), Rules.Sheets.Summarize(Codec.Write(learned)));
    }

    [Fact]
    public void A_slotted_skill_with_no_rank_is_refused()
    {
        var sheet = Learned() with { SkillSlots = ["salvo", "", "", "", "", ""] };

        Assert.Equal("slot_unlearned", Heroes.Audit(sheet).Rule);
    }

    [Fact]
    public void A_slotted_skill_held_at_rank_zero_is_refused()
    {
        var learned = Learned();
        var sheet = learned with { SkillRanks = learned.SkillRanks.Add("salvo", 0), SkillSlots = ["salvo", "", "", "", "", ""] };

        Assert.Equal("slot_unlearned", Heroes.Audit(sheet).Rule);
    }

    [Fact]
    public void A_slotted_upgrade_is_refused()
    {
        var sheet = Learned() with { SkillSlots = ["marksmanship", "", "", "", "", ""] };

        Assert.Equal("slot_not_castable", Heroes.Audit(sheet).Rule);
    }

    [Fact]
    public void A_skill_in_two_slots_is_refused()
    {
        var sheet = Learned() with { SkillSlots = ["patriot_missile", "patriot_missile", "", "", "", ""] };

        Assert.Equal("slot_duplicate", Heroes.Audit(sheet).Rule);
    }

    [Fact]
    public void A_bar_of_the_wrong_length_is_refused()
    {
        var sheet = Learned() with { SkillSlots = ["", "", ""] };

        Assert.Equal("skill_slots", Heroes.Audit(sheet).Rule);
    }

    [Fact]
    public void A_respec_empties_the_bar()
    {
        var after = Heroes.Respec(Slotted()).Sheet;

        Assert.Equal(["", "", "", "", "", ""], after.SkillSlots.ToArray());
        Assert.True(Heroes.Audit(after).Ok, Heroes.Audit(after).Detail);
    }

    [Fact]
    public void A_checkpoint_may_change_the_bar_with_no_evidence()
    {
        var before = Codec.Write(Learned());
        var after = Codec.Write(Slotted());

        Assert.NotEqual(before.Data, after.Data);   // the bar is in the bytes
        Assert.True(Rules.ValidateCheckpoint(before, after, NoEvidence).Ok);
    }

    [Fact]
    public void A_checkpoint_refuses_a_bar_holding_an_unlearned_skill()
    {
        var before = Codec.Write(Learned());
        var after = Codec.Write(Learned() with { SkillSlots = ["salvo", "", "", "", "", ""] });

        Assert.Equal("slot_unlearned", Rules.ValidateCheckpoint(before, after, NoEvidence).FailedRule);
    }
}
