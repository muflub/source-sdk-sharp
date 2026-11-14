using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Tests.Robots;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// horde.json (plan_descent_logic section 3, E1, E2): the shipped numbers are the plan's, and
/// the loader refuses a pool that would leave the edict budget, a second boss body, and a
/// tick rate of zero.
/// </summary>
public sealed class HordeSettingsTests
{
    private static readonly TF2DescentRules Rules = new();

    [Fact]
    public void The_shipped_horde_numbers_are_the_plans_defaults()
    {
        var h = Rules.Content.Horde;

        Assert.Equal((256, 48, 1, 49, 2), (h.RosterCapacity, h.PoolBodies, h.BossBodies, h.Bodies, h.WakeRooms));
        Assert.Equal((10.0, 10.0, 2.0), (h.DemoteDelaySeconds, h.SleepDelaySeconds, h.FarTickHz));
    }

    [Fact]
    public void The_horde_table_is_one_of_the_content_tables()
    {
        Assert.Contains(ContentTables.Horde, ContentTables.Names());
    }

    [Fact]
    public void A_pool_past_the_edict_budget_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["poolBodies"] = 64);

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.OutOfRange && e.Message.Contains("poolBodies+bossBodies"));
    }

    [Fact]
    public void A_second_boss_body_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["bossBodies"] = 2);

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.OutOfRange && e.Message.Contains("bossBodies"));
    }

    [Fact]
    public void A_far_tick_rate_of_zero_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["farTickHz"] = 0);

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.OutOfRange && e.Message.Contains("farTickHz"));
    }

    [Fact]
    public void A_missing_horde_field_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n.AsObject().Remove("wakeRooms"));

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.Malformed);
    }

    [Fact]
    public void The_shipped_horde_table_loads_clean()
    {
        Assert.Empty(RobotContentTests.Errors(ContentTables.Horde, _ => { }));
    }
}
