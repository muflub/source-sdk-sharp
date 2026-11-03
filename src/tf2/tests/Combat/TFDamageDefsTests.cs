using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.OracleTests;

using TF2.Server.Combat;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// The damage layer's stock constants (<c>src/tf2/shared/Combat/TFDamageDefs.cs</c>
/// and the rules' own) held against the PRISTINE headers and sources through
/// <see cref="ValveSdk"/>, which fails on a missing clone or file.
/// </summary>
public sealed class TFDamageDefsTests
{
    private const string SharedDefsH = "src/game/shared/tf/tf_shareddefs.h";
    private const string CoreSharedDefsH = "src/game/shared/shareddefs.h";
    private const string TakeDamageInfoH = "src/game/shared/takedamageinfo.h";
    private const string TFRules = "src/game/shared/tf/tf_gamerules.cpp";
    private const string PlayerShared = "src/game/shared/tf/tf_player_shared.cpp";
    private const string AchievementDataH = "src/game/server/tf/tf_achievementdata.h";

    private static string Define(string text, string name)
    {
        var m = Regex.Match(text, $@"^#define\s+{name}\s+([^\r\n]*?)\s*(//[^\n]*)?$", RegexOptions.Multiline);
        Assert.True(m.Success, $"no #define {name}");
        return m.Groups[1].Value.Trim();
    }

    private static float FloatDefine(string text, string name) =>
        float.Parse(Define(text, name).TrimEnd('f'), CultureInfo.InvariantCulture);

    [Fact]
    public void Every_ETFDmgCustom_enumerator_is_a_TFDamageCustom_member_of_the_same_value()
    {
        string text = ValveSdk.Read(SharedDefsH);
        int open = text.IndexOf('{', text.IndexOf("enum ETFDmgCustom", StringComparison.Ordinal));
        string body = Regex.Replace(text[(open + 1)..text.IndexOf('}', open)], @"//[^\n]*", "");
        var names = body.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

        Assert.Equal(88, names.Count);
        Assert.Equal(names.Count, Enum.GetValues<TFDamageCustom>().Length);
        for (int value = 0; value < names.Count; value++)
        {
            string stock = names[value].Split('=')[0].Trim();
            string member = stock == "TF_DMG_WRENCH_FIX" ? "WrenchFix"
                : string.Concat(stock["TF_DMG_CUSTOM_".Length..].Split('_').Select(w => w[..1] + w[1..].ToLowerInvariant()));
            Assert.True(Enum.TryParse<TFDamageCustom>(member, out var parsed) && (int)parsed == value,
                        $"{stock} ({value}) is not TFDamageCustom.{member}");
        }
    }

    [Fact]
    public void The_TF_damage_bit_aliases_are_the_HL2_bits_stock_names()
    {
        string tf = ValveSdk.Read(SharedDefsH);
        string core = ValveSdk.Read(CoreSharedDefsH);
        int Bit(string name)
        {
            string value = Define(core, name);
            var shift = Regex.Match(value, @"\(1\s*<<\s*(\d+)\)");
            return shift.Success ? 1 << int.Parse(shift.Groups[1].Value, CultureInfo.InvariantCulture) : throw new InvalidDataException(name + " = " + value);
        }
        int Alias(string tfName) => Bit(Define(tf, tfName).Trim('(', ')'));

        Assert.Equal((int)TFDamageBits.UseHitLocations, Alias("DMG_USE_HITLOCATIONS"));
        Assert.Equal((int)TFDamageBits.HalfFalloff, Alias("DMG_HALF_FALLOFF"));
        Assert.Equal((int)TFDamageBits.Critical, Alias("DMG_CRITICAL"));
        Assert.Equal((int)TFDamageBits.RadiusMax, Alias("DMG_RADIUS_MAX"));
        Assert.Equal((int)TFDamageBits.Ignite, Alias("DMG_IGNITE"));
        Assert.Equal((int)TFDamageBits.UseDistanceMod, Alias("DMG_USEDISTANCEMOD"));
        Assert.Equal((int)TFDamageBits.NoCloseDistanceMod, Alias("DMG_NOCLOSEDISTANCEMOD"));
        Assert.Equal((int)TFDamageBits.Melee, Alias("DMG_MELEE"));
    }

    [Fact]
    public void ECritType_is_TFCritType()
    {
        string text = ValveSdk.Read(TakeDamageInfoH);
        Assert.Matches(@"enum ECritType\s*\{\s*CRIT_NONE = 0,\s*CRIT_MINI,\s*CRIT_FULL,", text);
        Assert.Equal((0, 1, 2), ((int)TFCritType.None, (int)TFCritType.Mini, (int)TFCritType.Full));
    }

    [Fact]
    public void The_burning_assist_and_fall_defines_are_stocks()
    {
        string tf = ValveSdk.Read(SharedDefsH);
        Assert.Equal(TFCombatDefs.BurningFrequency, FloatDefine(tf, "TF_BURNING_FREQUENCY"));
        Assert.Equal(TFCombatDefs.BurningFlameLife, FloatDefine(tf, "TF_BURNING_FLAME_LIFE"));
        Assert.Equal(TFCombatDefs.BurningFlameLifePyro, FloatDefine(tf, "TF_BURNING_FLAME_LIFE_PYRO"));
        Assert.Equal(TFCombatDefs.BurningDamage, FloatDefine(tf, "TF_BURNING_DMG"));
        Assert.Equal(TFCombatDefs.TimeAssistKill, FloatDefine(tf, "TF_TIME_ASSIST_KILL"));
        Assert.Equal(TFCombatDefs.TimeSuicideKillCredit, FloatDefine(tf, "TF_TIME_SUICIDE_KILL_CREDIT"));
        Assert.Equal(TFCombatDefs.PlayerMaxSafeFallSpeed, FloatDefine(ValveSdk.Read(TFRules), "TF_PLAYER_MAX_SAFE_FALL_SPEED"));
        Assert.Equal(TFDamageRecords.HistorySlots, FloatDefine(ValveSdk.Read(AchievementDataH), "MAX_ACHIEVEMENT_HISTORY_SLOTS"));
    }

    [Fact]
    public void The_afterburn_constants_are_stocks()
    {
        string text = ValveSdk.Read(PlayerShared);
        float Const(string name) => float.Parse(Regex.Match(text, $@"const float {name} = ([0-9.]+)f;").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(TFBurn.MaxDuration, Const("tf_afterburn_max_duration"));
        Assert.Equal(TFBurn.SecondDegreeRatio, Const("tf_afterburn_duration_ratio_second_degree"));
        Assert.Equal(TFBurn.ThirdDegreeRatio, Const("tf_afterburn_duration_ratio_third_degree"));
        Assert.Equal(TFBurn.SecondDegreeMult, Const("tf_afterburn_mult_second_degree"));
        Assert.Equal(TFBurn.ThirdDegreeMult, Const("tf_afterburn_mult_third_degree"));
    }

    [Fact]
    public void The_range_constants_are_the_modify_rules_literals()
    {
        string text = ValveSdk.Read(TFRules);
        Assert.Contains("float flRandomDamageSpread = 0.10f;", text);
        Assert.Contains("float flOptimalDistance = 512.0;", text);
        Assert.Contains("flOptimalDistance *= 2.5f;", text);
        Assert.Contains("float flCenter = RemapValClamped( flDistance / flOptimalDistance, 0.0, 2.0, 1.0, 0.0 );", text);
        Assert.Contains("flCenter = RemapVal( flCenter, 0.5, 1.0, 0.5, 0.65 );", text);
        Assert.Contains("flDmgVariance = SimpleSplineRemapValClamped( flRandomRangeVal, 0, 1, -flRandomDamage, flRandomDamage );", text);
        Assert.Equal((0.10f, 512f, 2.5f), (TFDamageModifyRules.RandomDamageSpread, TFDamageModifyRules.OptimalDistance, TFDamageModifyRules.SniperOptimalScale));
    }
}
