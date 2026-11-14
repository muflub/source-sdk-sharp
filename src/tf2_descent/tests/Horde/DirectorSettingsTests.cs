using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Tests.Robots;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// horde.json's spawn director and door shove blocks (plan_descent_logic HO9, HO11, L9): the
/// shipped numbers, and the loader refusing a floor-bound size that is no size class, a
/// discipline off the skill scale and a shove with no cooldown.
/// </summary>
public sealed class DirectorSettingsTests
{
    private static readonly TF2DescentRules Rules = new();

    [Fact]
    public void The_shipped_director_numbers()
    {
        var d = Rules.Content.Horde.Director;

        Assert.Equal((8.0, 3, 4.0, 18.0, 8), (d.PostGap, d.PostRings, d.DoorMargin, d.StepHeight, d.RefitAttempts));
        Assert.Equal(["giant", "boss"], d.FloorBoundSizes);
    }

    [Fact]
    public void The_shipped_shove_numbers()
    {
        var s = Rules.Content.Horde.Shove;

        Assert.Equal((40.0, 160.0, 0.5, 3, 1.0, 450.0, 160.0), (s.HalfDepth, s.Reach, s.DwellSeconds, s.LowDiscipline, s.CooldownSeconds, s.Speed, s.Lift));
    }

    [Fact]
    public void A_floor_bound_size_that_is_no_size_class_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["director"]!["floorBoundSizes"]!.AsArray().Add("titan"));

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.DanglingReference && e.Message.Contains("titan"));
    }

    [Fact]
    public void A_shove_discipline_off_the_skill_scale_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["shove"]!["lowDiscipline"] = 11);

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.OutOfRange && e.Message.Contains("shove.lowDiscipline"));
    }

    [Fact]
    public void A_shove_with_no_cooldown_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["shove"]!["cooldownSeconds"] = 0);

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.OutOfRange && e.Message.Contains("shove.cooldownSeconds"));
    }

    [Fact]
    public void A_missing_director_field_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["director"]!.AsObject().Remove("postGap"));

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.Malformed);
    }
}
