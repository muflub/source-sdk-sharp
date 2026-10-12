using System;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ScriptedVarParse.Int"/> AGAINST THE PRECISION IT HAS TO HAVE, and
/// against the stock behaviour it has to keep.
///
/// <para>
/// THE BUG THESE PIN. The parse read every integer through a <c>float</c>, whose
/// mantissa is 24 bits, so every value above 2^24 came back rounded and no
/// caller could tell: <c>16777217</c> read as <c>16777216</c>, <c>117506305</c>
/// as <c>117506304</c>. That is not a map-only concern. <c>SavedEntity.Set(string,
/// int)</c> writes the exact digits and <c>GetInt</c> reads them back through
/// this, so a save could store a value perfectly and restore it WRONG -- silently,
/// with no parse failure to notice.
/// </para>
///
/// <para>
/// EVERY PRECISION FACT HERE USES A VALUE THE OLD CODE ACTUALLY GOT WRONG, and
/// that is the point of the file rather than an incidental choice: a fact whose
/// input is below 2^24 passes with the bug present and proves nothing. The two
/// facts that DO use small or non-integer input are labelled controls, and they
/// are here to pin the behaviour the fix must not break rather than the bug.
/// </para>
///
/// <para>
/// WHAT STOCK DOES, because the fix had to preserve it. A FIELD_INTEGER keyvalue
/// is parsed by <c>atoi( szValue )</c> in <c>ParseKeyvalue</c>
/// (src/game/server/saverestore_gamedll.cpp): integer-exact, and it truncates
/// trailing junk rather than failing, so <c>atoi("5.7")</c> is 5. Reading an
/// exact integer exactly and still truncating "5.7" are therefore BOTH stock,
/// and the fix reads the integer first and keeps the float reading as the
/// fallback that handles the rest.
/// </para>
/// </summary>
public class ScriptedVarParseIntTests
{
    /// <summary>
    /// 2^24 + 1: the first integer a float cannot hold. Read through a float it
    /// comes back as 16777216.
    /// </summary>
    [Fact]
    public void FirstIntegerAboveTheFloatMantissaIsExact()
    {
        Assert.True(ScriptedVarParse.Int("16777217", out int result));
        Assert.Equal(16777217, result);
    }

    /// <summary>
    /// CONTROL, and it passed with the bug present: 2^24 itself is exactly
    /// representable. It is here to place the boundary -- the fact above is one
    /// higher and was red.
    /// </summary>
    [Fact]
    public void TheMantissaBoundaryItselfWasAlwaysExact()
    {
        Assert.True(ScriptedVarParse.Int("16777216", out int result));
        Assert.Equal(16777216, result);
    }

    /// <summary>
    /// 0x0701_0101 == 117506305, the handle shape this suite uses as a live
    /// fixture. Through a float it is 117506304.
    /// </summary>
    [Fact]
    public void AHandleShapedIntegerIsExact()
    {
        Assert.True(ScriptedVarParse.Int("117506305", out int result));
        Assert.Equal(117506305, result);
    }

    /// <summary>Near the top of the range, where a float is 128 apart.</summary>
    [Fact]
    public void ALargeIntegerIsExact()
    {
        Assert.True(ScriptedVarParse.Int("2000000001", out int result));
        Assert.Equal(2000000001, result);
    }

    /// <summary>
    /// CONTROL, AND AN HONEST ONE: int.MaxValue passed with the bug present, by
    /// accident. A float rounds 2147483647 UP to 2147483648, which is out of
    /// range, and .NET's float-to-int conversion then SATURATES back to
    /// int.MaxValue -- two errors cancelling. It is kept because the top of the
    /// range is worth pinning, and labelled because a fact that was never seen
    /// red is not evidence about the bug it sits next to.
    /// </summary>
    [Fact]
    public void IntMaxValueIsExact()
    {
        Assert.True(ScriptedVarParse.Int("2147483647", out int result));
        Assert.Equal(int.MaxValue, result);
    }

    /// <summary>Negative values lose the same bits, so they are pinned too.</summary>
    [Fact]
    public void ALargeNegativeIntegerIsExact()
    {
        Assert.True(ScriptedVarParse.Int("-2000000001", out int result));
        Assert.Equal(-2000000001, result);
    }

    /// <summary>
    /// THE SAVE ROUND TRIP, which is the reason this is a bug and not a
    /// curiosity: the value is written by <c>Set</c> as exact digits and read
    /// back by <c>GetInt</c> through the parse under test.
    /// </summary>
    [Fact]
    public void ASavedIntegerAboveTheMantissaRestoresExactly()
    {
        var saved = new SavedEntity(0x0701_0101u, "ss_fixture", "big_int");

        saved.Set("count", 117506305);

        Assert.Equal(117506305, saved.GetInt("count"));
    }

    /// <summary>
    /// CONTROL: "5.7" still reads as 5. Stock's <c>atoi</c> truncates trailing
    /// junk rather than failing, and a map writing "5.0" where an integer is
    /// expected is ordinary -- <c>int.TryParse</c> alone would have REFUSED
    /// both, which is why the float reading stayed as the fallback.
    /// </summary>
    [Fact]
    public void ANonIntegerStillTruncatesRatherThanFailing()
    {
        Assert.True(ScriptedVarParse.Int("5.7", out int result));
        Assert.Equal(5, result);
    }

    /// <summary>
    /// A value past int.MaxValue saturates, and this fact PINS that rather than
    /// endorsing it. Stock's <c>atoi</c> wraps here (-2147483648); .NET's
    /// float-to-int conversion saturates. The fix did not change it -- an
    /// overflowing value fails <c>int.TryParse</c> and lands in the same float
    /// fallback it always did -- so the divergence is older than this file and
    /// is recorded here so a later change to it is visible rather than silent.
    /// </summary>
    [Fact]
    public void AnOverflowingIntegerSaturatesAsItDidBefore()
    {
        Assert.True(ScriptedVarParse.Int("2147483648", out int result));
        Assert.Equal(int.MaxValue, result);
    }

    /// <summary>
    /// THE ENUM PATH, which routes through <see cref="ScriptedVarParse.Int"/>
    /// and inherited the same loss. A member above 2^24 is unlikely; a FLAGS
    /// enum whose high bit is set alongside a low one is not, and it is exactly
    /// the shape that loses the low bit -- 2^26 | 1 rounds to 2^26 in a float,
    /// dropping <see cref="HighFlags.Low"/> with no failure reported.
    /// </summary>
    [Fact]
    public void AFlagsEnumKeepsItsLowBitBesideAHighOne()
    {
        Assert.True(ScriptedVarParse.Enum<HighFlags>("67108865", out HighFlags result));
        Assert.Equal(HighFlags.High | HighFlags.Low, result);
    }

    /// <summary>
    /// CONTROL for the fact above: the enum's own low bits were never affected,
    /// so a green there has to come from the high bit and not from the enum path
    /// working at all.
    /// </summary>
    [Fact]
    public void AFlagsEnumStillReadsALowCombination()
    {
        Assert.True(ScriptedVarParse.Enum<HighFlags>("3", out HighFlags result));
        Assert.Equal(HighFlags.Low | HighFlags.Mid, result);
    }

    /// <summary>
    /// THE SAME LOSS ON A REAL MAP KEY, and this is not a hypothetical shape:
    /// <c>filter_damage_type</c> declares a <see cref="DamageType"/> key and a
    /// map writes the raw DMG_ number into it. <c>DMG_PLASMA</c> is 0x01000000,
    /// which is 2^24 exactly, and stock's flags run up to <c>DMG_BUCKSHOT</c>
    /// (1&lt;&lt;29) -- so every combination of a high DMG_ bit with a low one
    /// was losing the low one, and the filter then matched the wrong damage
    /// with nothing logged.
    /// </summary>
    [Fact]
    public void AFilterDamageTypeKeyKeepsBothOfItsBits()
    {
        Assert.True(ScriptedVarParse.Enum<DamageType>("16777217", out DamageType result));
        Assert.Equal(DamageType.Plasma | DamageType.Crush, result);
    }

    [Flags]
    private enum HighFlags
    {
        None = 0,
        Low = 1,
        Mid = 2,
        High = 1 << 26,
    }
}
