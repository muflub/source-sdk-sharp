using System;
using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// Armour.Apply against CBasePlayer::OnTakeDamage's armour block in the
/// pristine upstream: the constants, the multiplayer blast doubling, and the
/// float subtraction into an int.
/// </summary>
public class ArmourOracleTests
{
    private static string Player => Regex.Replace(ValveSdk.Read("src/game/server/player.cpp"), @"//[^\n]*", "");

    private static float Define(string name)
    {
        var m = Regex.Match(Player, @"#define\s+" + name + @"\s+(?<v>[\d.]+)");
        Assert.True(m.Success, name + " is gone from player.cpp");
        return float.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void The_ratio_and_bonus_are_stocks()
        => Assert.Equal((Define("ARMOR_RATIO"), Define("ARMOR_BONUS")), (Armour.DefaultRatio, Armour.DefaultBonus));

    [Fact]
    public void A_blast_doubles_the_bonus_in_multiplayer_only()
        => Assert.Matches(@"if\s*\(\s*\(\s*info\.GetDamageType\(\)\s*&\s*DMG_BLAST\s*\)\s*&&\s*g_pGameRules->IsMultiplayer\(\)\s*\)\s*\{\s*flBonus\s*\*=\s*2;", Player);

    [Fact]
    public void The_armour_is_an_int_less_a_float()
    {
        Assert.Matches(@"m_ArmorValue\s*-=\s*flArmor;", Player);
        Assert.Matches(@"\bint\s+m_ArmorValue\b|CNetworkVar\w*\(\s*int\s*,\s*m_ArmorValue\s*\)",
                       ValveSdk.Read("src/game/server/player.h"));
    }
}
