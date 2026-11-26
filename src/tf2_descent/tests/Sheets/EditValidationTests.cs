using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;
using Xunit;

namespace TF2.Descent.Tests.Sheets;

/// <summary>
/// An operator's edit of a sheet (contract 1.3's <c>IGameRules.ValidateEdit</c>, plan X4): the
/// admin UI's character edit asks it, never <c>ValidateCheckpoint</c>, so an edit with no play
/// evidence can raise XP and level. Everything else a checkpoint holds still holds: the sheet
/// after is one play could reach, and nothing that only grows went back.
/// </summary>
public sealed class EditValidationTests
{
    private static readonly TF2DescentRules Rules = new();
    private static IGameRules Host => Rules;
    private static HeroRules Heroes => Rules.Heroes;
    private static SheetCodec Codec => Rules.SheetCodec;

    private static HeroSheet Level(int level) =>
        Heroes.GainXp(Heroes.New("soldier", "s"), Heroes.XpAtLevel(level)).Sheet;

    private static ValidationResult Edit(HeroSheet before, HeroSheet after) =>
        Host.ValidateEdit(Codec.Write(before), Codec.Write(after));

    [Fact]
    public void An_edit_may_raise_xp_and_level_with_no_evidence()
    {
        var before = Level(3);
        var after = Heroes.GainXp(before, Heroes.XpAtLevel(10) - before.Xp).Sheet;
        Assert.Equal(10, after.Level);   // the stimulus: the edit raises the level
        Assert.Equal("xp_unexplained",
            Host.ValidateCheckpoint(Codec.Write(before), Codec.Write(after), new CheckpointEvidence([], 0)).FailedRule);   // the control: a checkpoint refuses it

        Assert.True(Edit(before, after).Ok, Edit(before, after).FailedRule);
    }

    [Fact]
    public void An_edit_whose_level_does_not_match_its_xp_is_refused()
    {
        var before = Level(3);
        var after = before with { Level = 10 };

        Assert.Equal("xp_level_mismatch", Edit(before, after).FailedRule);
    }

    [Fact]
    public void An_edit_may_not_change_the_class()
    {
        var before = Level(3);
        var after = before with { ClassId = "heavy" };

        Assert.Equal("class_changed", Edit(before, after).FailedRule);
    }

    [Fact]
    public void An_edit_may_not_lower_the_level()
    {
        var before = Level(10);
        var after = Level(3);

        Assert.Equal("level_decreased", Edit(before, after).FailedRule);
    }

    [Fact]
    public void An_edit_of_an_undecodable_sheet_is_refused()
    {
        var good = Codec.Write(Level(3));
        var bad = new CharacterSheet(good.Data[..1], good.SchemaVersion);

        Assert.Equal("sheet_malformed", Host.ValidateEdit(good, bad).FailedRule);
    }
}
