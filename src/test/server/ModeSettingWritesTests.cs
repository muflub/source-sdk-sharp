using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The playing mode's settings as votes and the admin menu offer them, and the
/// one path that writes one (plan_bots_teams_tf2 D.2): only the active mode's
/// rows, only a listed value, and a <see cref="SettingApplies.NextRound"/> row
/// waits for the next round's setup.
/// </summary>
public class ModeSettingWritesTests
{
    private sealed class Level : ILevelInfo
    {
        public string? Map => "dm_lockdown";
        public bool IsReady => true;
    }

    private sealed class Rig
    {
        public readonly TeamDeathmatchTests.Rig Game = new();
        public readonly GameModeCatalog Catalog;
        public readonly ModeSettingWrites Writes;

        public Rig()
        {
            Catalog = new GameModeCatalog(Game.Modes, new BspEntityClasses(new GameModeCatalogTests.Bsps()), new Level());
            Writes = new ModeSettingWrites(Catalog, Game.Host, Game.Log);
            Game.Services.AddInstance(Writes);
            Game.Host.Returns("Console.FindConVarValue", "0");
        }

        public List<string> Ran() => [.. Game.Host.AllOf("Console.ExecuteServerCommand").Select(c => (string)c.Args[0]!)];
    }

    private static List<string> SettingIds(ModeSpec? mode)
        => [.. GameVoteOptions.Of([], [], null, false, false, false, GameVoteOptions.Votable(mode), _ => "0")
                              .Select(o => o.Id[..o.Id.IndexOf('=')]).Distinct()];

    [Fact]
    public void DeathmatchsVoteOffersNoTeamRows()
    {
        var rig = new Rig();

        Assert.Equal(["mp_timelimit", "mp_fraglimit"], SettingIds(rig.Catalog.Active));
    }

    [Fact]
    public void TeamDeathmatchsVoteOffersItsTeamRows()
    {
        var rig = new Rig();
        rig.Game.Tdm();

        Assert.Contains("mp_tdm_teams", SettingIds(rig.Catalog.Active));
        Assert.Contains("mp_friendlyfire", SettingIds(rig.Catalog.Active));
    }

    [Fact]
    public void ARowNotVotableOrTheAdminsAloneIsNoVote()
    {
        var mode = new ModeSpec("m", "M", false,
        [
            CoreModeSpecs.TimeLimit() with { Votable = false },
            CoreModeSpecs.FragLimit() with { AdminOnly = true },
            CoreModeSpecs.FriendlyFire(),
        ], []);

        Assert.Equal(["mp_friendlyfire"], SettingIds(mode));
    }

    [Fact]
    public void ANowSettingIsWrittenAtOnce()
    {
        var rig = new Rig();

        Assert.Null(rig.Writes.Write("mp_fraglimit", "20"));

        Assert.Equal(["mp_fraglimit 20\n"], rig.Ran());
    }

    [Fact]
    public void ANextRoundSettingWaitsForTheRoundsSetup()
    {
        var rig = new Rig();
        rig.Game.Tdm();
        int before = rig.Ran().Count;

        Assert.Null(rig.Writes.Write("cs_sv_teams_unbalance_limit", "2"));
        Assert.Equal(before, rig.Ran().Count);
        Assert.Equal((SettingApplies.NextRound, "2"), rig.Writes.Pending["cs_sv_teams_unbalance_limit"]);

        rig.Game.Tdm();   // the round's setup, as a switch runs it

        Assert.Contains("cs_sv_teams_unbalance_limit 2\n", rig.Ran());
        Assert.Empty(rig.Writes.Pending);
    }

    [Fact]
    public void AnotherModesSettingIsRefused()
    {
        var rig = new Rig();

        Assert.Equal("'null' has no setting mp_tdm_teams", rig.Writes.Write("mp_tdm_teams", "3"));
        Assert.Empty(rig.Ran());
    }

    [Theory]
    [InlineData("7")]
    [InlineData("20;quit")]
    [InlineData("")]
    public void AValueTheRowDoesNotListIsRefused(string value)
    {
        var rig = new Rig();

        Assert.NotNull(rig.Writes.Write("mp_fraglimit", value));
        Assert.Empty(rig.Ran());
    }

    [Fact]
    public void ARowWithNoChoicesTakesANumberInItsRange()
    {
        var bots = new SettingSpec("ai_bot_count", SettingKind.Int, [], "0", Min: 0f, Max: 16f);

        Assert.True(ModeSettingWrites.Allows(bots, "16"));
        Assert.False(ModeSettingWrites.Allows(bots, "17"));
        Assert.False(ModeSettingWrites.Allows(bots, "2.5"));
        Assert.False(ModeSettingWrites.Allows(bots, "two"));
    }
}
