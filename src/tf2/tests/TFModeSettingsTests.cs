using SourceSharp.Abi;

using TF2.Server.Modes;

using Xunit;

namespace TF2.Tests;

/// <summary>What TF2's modes publish to the catalog (plan_bots_teams_tf2 D.2).</summary>
public sealed class TFModeSettingsTests
{
    [Fact]
    public void Rounds_and_wins_rather_than_frags()
        => Assert.Equal(["mp_friendlyfire", "mp_timelimit", "mp_maxrounds", "mp_winlimit"], TFModeSettings.Rows().Select(s => s.Key));

    [Theory]
    [InlineData("ctf", "item_teamflag")]
    [InlineData("pl", "team_train_watcher")]
    [InlineData("koth", "tf_logic_koth")]
    [InlineData("cp", "team_control_point_master")]
    public void Each_mode_needs_the_entity_its_selector_picks_it_by(string id, string entity)
        => Assert.Equal([entity], TFModeSettings.For(id).RequiresAnyOf);
}
