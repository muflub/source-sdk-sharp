using SourceSharp.Ai.Server.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The entity lump's team (plan_bots_teams_tf2 P6 step 5): every Source entity may be placed on a
/// team (<c>TeamNum</c>), which a game's binding reads its own entities' teams by; one without
/// reads 0. The reader names no game's entities.
/// </summary>
public sealed class MapEntityTeamTests
{
    private const string Lump = "{\n\"classname\" \"worldspawn\"\n}\n"
                                + "{\n\"classname\" \"info_player_teamspawn\"\n\"origin\" \"10 20 30\"\n\"TeamNum\" \"3\"\n}\n"
                                + "{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"40 40 17\"\n}\n";

    [Fact]
    public void AnEntitysTeamIsItsTeamNum()
    {
        MapEntities lump = MapEntities.Read(TinyBsp.Make(Lump));
        Assert.Equal(3, lump.All.Single(e => e.ClassName == "info_player_teamspawn").Team);
    }

    [Fact]
    public void AnEntityWithoutATeamIsOnNone()
        => Assert.Equal(0, MapEntities.Read(TinyBsp.Make(Lump)).All.Single(e => e.ClassName == "info_player_deathmatch").Team);
}
