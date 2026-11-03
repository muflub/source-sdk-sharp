using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The official map lists (<see cref="TFValveMaps"/>): what stock's free
/// <c>IsValveMap</c>/<c>IsCommunityMap</c> (<c>tf_gamerules.cpp:638-661</c>)
/// answer, and the <c>CTFGameRules::IsOfficialMap</c> composition
/// (<c>:16654-16665</c>).
/// </summary>
public sealed class TFValveMapTests
{
    [Fact]
    public void ctf_2fort_is_a_Valve_map()
    {
        Assert.True(TFValveMaps.IsValveMap("ctf_2fort"));
        Assert.True(TFValveMaps.IsOfficialMap("ctf_2fort"));
    }

    [Fact]
    public void A_made_up_map_name_is_on_neither_list()
    {
        Assert.False(TFValveMaps.IsValveMap("ss_barrel"));
        Assert.False(TFValveMaps.IsCommunityMap("ss_barrel"));
        Assert.False(TFValveMaps.IsOfficialMap("ss_barrel"));
    }

    [Fact]
    public void A_featured_community_map_is_official_but_not_a_Valve_map()
    {
        Assert.False(TFValveMaps.IsValveMap("koth_lazarus"));
        Assert.True(TFValveMaps.IsCommunityMap("koth_lazarus"));
        Assert.True(TFValveMaps.IsOfficialMap("koth_lazarus"));
    }

    [Fact]
    public void The_lookup_ignores_case_as_Q_stricmp_does()
    {
        Assert.True(TFValveMaps.IsValveMap("CTF_2FORT"));
        Assert.True(TFValveMaps.IsCommunityMap("KOTH_LAZARUS"));
    }

    [Fact]
    public void A_row_carries_its_display_name_and_game_type_token()
    {
        var twoFort = TFValveMaps.Row("ctf_2fort");
        Assert.NotNull(twoFort);
        Assert.Equal("2Fort", twoFort.DisplayName);
        Assert.Equal("#Gametype_CTF", twoFort.GameType);

        var marshlands = TFValveMaps.Row("sd_marshlands");
        Assert.NotNull(marshlands);
        Assert.Equal("Marshlands", marshlands.DisplayName);
        Assert.Equal("#GameType_HTF", marshlands.GameType);
    }
}
