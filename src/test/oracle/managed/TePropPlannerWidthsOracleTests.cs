using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// <see cref="TePropPlanner"/>'s restated widths and <see cref="TeFlags"/>'
/// bits against the stock headers that own them, read from the pristine
/// upstream clone. The planner deliberately depends on no engine header, so it
/// restates COORD_INTEGER_BITS, MAX_EDICT_BITS and the rest; a restated
/// constant nobody checks is the one that drifts, and one bit of drift is a
/// bitstream that stops making sense with no log line. tempent_schema_test.cpp
/// held tempent_schema.h's copies to the same headers; both sides' tables are
/// planned from this one now, so this is where they are held.
/// </summary>
public sealed class TePropPlannerWidthsOracleTests
{
    private static string Header(string relative)
        => Regex.Replace(ValveSdk.Read(relative), @"//[^\n]*", string.Empty);

    private static int Define(string text, string name)
    {
        Match m = Regex.Match(text, $@"^\s*#define\s+{name}\s+(\d+)\s*$", RegexOptions.Multiline);
        Assert.True(m.Success, $"#define {name} <number> is in the header");
        return int.Parse(m.Groups[1].Value);
    }

    [Theory]
    [InlineData("COORD_INTEGER_BITS", TePropPlanner.CoordIntegerBits)]
    [InlineData("COORD_FRACTIONAL_BITS", TePropPlanner.CoordFractionalBits)]
    [InlineData("COORD_FRACTIONAL_BITS_MP_LOWPRECISION", TePropPlanner.CoordFractionalBitsMpLowPrecision)]
    [InlineData("NORMAL_FRACTIONAL_BITS", TePropPlanner.NormalFractionalBits)]
    public void TheCoordWidthsAreCoordsizes(string name, int value)
        => Assert.Equal(value, Define(Header("src/public/coordsize.h"), name));

    [Theory]
    [InlineData("MAX_EDICT_BITS", TePropPlanner.MaxEdictBits)]
    [InlineData("SP_MODEL_INDEX_BITS", TePropPlanner.ModelIndexBits)]
    public void TheEntityWidthsAreConstHs(string name, int value)
        => Assert.Equal(value, Define(Header("src/public/const.h"), name));

    [Fact]
    public void TheEnumeratedAndDerivedWidthsAreConstHs()
    {
        string text = Header("src/public/const.h");

        // EF_MAX_BITS is an enumerator, and NUM_NETWORKED_EHANDLE_BITS is
        // MAX_EDICT_BITS plus the serial bits: neither is a literal #define.
        Match ef = Regex.Match(text, @"\bEF_MAX_BITS\s*=\s*(\d+)");
        Assert.True(ef.Success, "const.h enumerates EF_MAX_BITS");
        Assert.Equal(TePropPlanner.EfMaxBits, int.Parse(ef.Groups[1].Value));

        Assert.Matches(@"#define\s+NUM_NETWORKED_EHANDLE_BITS\s+\(\s*MAX_EDICT_BITS\s*\+\s*NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS\s*\)",
                       text);
        Assert.Equal(TePropPlanner.EHandleBits,
                     Define(text, "MAX_EDICT_BITS") + Define(text, "NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS"));
    }

    [Theory]
    [InlineData("MAX_EFFECT_FLAG_BITS", TePropPlanner.EffectFlagBits)]
    [InlineData("MAX_EFFECT_DISPATCH_STRING_BITS", TePropPlanner.EffectStringBits)]
    public void TheEffectDispatchWidthsAreItsHeaders(string name, int value)
        => Assert.Equal(value, Define(Header("src/game/shared/effect_dispatch_data.h"), name));

    [Fact]
    public void TheSkinWidthIsBaseAnimatings()
        => Assert.Equal(TePropPlanner.AnimationSkinBits,
                        Define(Header("src/game/server/baseanimating.h"), "ANIMATION_SKIN_BITS"));

    [Fact]
    public void MaxPlayersIsTheBranchThisBuildCompiles()
    {
        // shareddefs.h gives 65 under CSTRIKE_DLL and 101 under TF_DLL/HL2MP;
        // this build defines none of them, so it is the #else arm.
        Match m = Regex.Match(Header("src/game/shared/shareddefs.h"),
            @"#if\s+defined\(\s*CSTRIKE_DLL\s*\)\s*#define\s+MAX_PLAYERS\s+\d+\s*#elif[^\n]*\s*#define\s+MAX_PLAYERS\s+\d+\s*#else\s*#define\s+MAX_PLAYERS\s+(\d+)");
        Assert.True(m.Success, "shareddefs.h keeps its three MAX_PLAYERS arms");
        Assert.Equal(TePropPlanner.MaxPlayers, int.Parse(m.Groups[1].Value));
    }

    [Theory]
    [InlineData(TeFlags.Unsigned, "SPROP_UNSIGNED")]
    [InlineData(TeFlags.Coord, "SPROP_COORD")]
    [InlineData(TeFlags.NoScale, "SPROP_NOSCALE")]
    [InlineData(TeFlags.RoundDown, "SPROP_ROUNDDOWN")]
    [InlineData(TeFlags.RoundUp, "SPROP_ROUNDUP")]
    [InlineData(TeFlags.Normal, "SPROP_NORMAL")]
    [InlineData(TeFlags.VectorElem, "SPROP_IS_A_VECTOR_ELEM")]
    [InlineData(TeFlags.CoordMp, "SPROP_COORD_MP")]
    [InlineData(TeFlags.CoordMpLowPrecision, "SPROP_COORD_MP_LOWPRECISION")]
    [InlineData(TeFlags.CoordMpIntegral, "SPROP_COORD_MP_INTEGRAL")]
    public void EveryFlagIsDtCommonsBit(TeFlags flag, string name)
    {
        // The client hands these to a RecvProp unchanged, so each must be the bit.
        Match m = Regex.Match(Header("src/public/dt_common.h"), $@"#define\s+{name}\s+\(1\s*<<\s*(\d+)\)");
        Assert.True(m.Success, $"dt_common.h defines {name}");
        Assert.Equal((int)flag, 1 << int.Parse(m.Groups[1].Value));
    }

    [Fact]
    public void EveryFlagIsHeldHere()
        => Assert.Equal(11, Enum.GetValues<TeFlags>().Length);   // None and the ten above
}
