extern alias client;

using TF2.Server.Rules;
using Xunit;

using ClientRule = client::TF2.Client.TFClientTeamNameRule;
using Engine = client::SourceSharp.Abi.RecordingEngine;

namespace TF2.Tests.Rules;

/// <summary>
/// The TF teams' names: g_aTeamNames on the server (tf_shareddefs.cpp), and
/// C_TFTeam::UpdateTeamName's localised names on the client (c_tf_team.cpp),
/// where the core would say Combine and Rebels.
/// </summary>
public sealed class TFTeamNameRuleTests
{
    [Fact]
    public void The_server_names_the_teams_as_g_aTeamNames_does()
    {
        var rule = new TFTeamNameRule();

        Assert.Equal(["Unassigned", "Spectator", "Red", "Blue"],
                     new[] { 0, 1, 2, 3 }.Select(t => rule.Name(t, "core")));
        Assert.Equal("core", rule.Name(4, "core"));
    }

    [Fact]
    public void The_client_reads_the_localised_names()
    {
        var engine = Engine.Default();
        var tables = new Dictionary<string, string>
        {
            ["#TF_RedTeam_Name"] = "RED team", ["#TF_BlueTeam_Name"] = "BLU team", ["#TF_Spectators"] = "Watchers",
        };
        engine.ReturnsEachCall("Hud.Localise", () =>
        {
            string token = (string)engine.Calls[^1].Arg(0)!;
            return tables.TryGetValue(token, out var v) ? v : token;
        });
        var rule = new ClientRule(engine);

        Assert.Equal(["", "Watchers", "RED team", "BLU team"], new[] { 0, 1, 2, 3 }.Select(t => rule.Name(t, "core")));
    }

    [Fact]
    public void The_client_falls_back_to_stocks_names_when_the_tables_lack_them()
    {
        var engine = Engine.Default();
        engine.ReturnsEachCall("Hud.Localise", () => (string)engine.Calls[^1].Arg(0)!);
        var rule = new ClientRule(engine);

        Assert.Equal(["SPECTATORS", "RED", "BLU"], new[] { 1, 2, 3 }.Select(t => rule.Name(t, "core")));
        Assert.Equal("core", rule.Name(4, "core"));
    }
}
