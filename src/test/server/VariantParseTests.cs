//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What the C runtime answers about a map's text, pinned.
///
/// <para>
/// EVERY EXPECTATION IN THIS FILE CAME OUT OF A PROGRAM, not out of a reading
/// of the standard. <c>tools/variant-oracle.c</c> holds the same expressions
/// stock's <c>variant_t::Convert</c> and <c>variant_t::ToString</c> use —
/// <c>atoi</c>, <c>atof</c>, the two <c>sscanf</c>s and the <c>snprintf</c>
/// formats — and was run against the glibc this game links against. The numbers
/// below are its output.
/// </para>
///
/// <para>
/// AND THE SAME TABLE IS ASSERTED NATIVELY, in
/// <c>src/sourcesharp/test/variant_convert_test.cpp</c>, against the real libc.
/// <see cref="VariantParse"/> is a stand-in for native code and a stand-in
/// drifts silently; two suites over one table is what makes the drift a
/// failure rather than a surprise.
/// </para>
/// </summary>
public class VariantParseAtofTests
{
    [Theory]
    // Ordinary numbers a map writes.
    [InlineData("0", 0f)]
    [InlineData("1", 1f)]
    [InlineData("-1", -1f)]
    [InlineData("3", 3f)]
    [InlineData("5.7", 5.7f)]
    [InlineData("-5.7", -5.7f)]
    [InlineData(".5", 0.5f)]
    [InlineData("+3", 3f)]
    // Nothing readable is zero, never a refusal.
    [InlineData("", 0f)]
    [InlineData(" ", 0f)]
    [InlineData("abc", 0f)]
    [InlineData("true", 0f)]
    [InlineData("false", 0f)]
    [InlineData("--3", 0f)]
    [InlineData("- 3", 0f)]
    [InlineData("0b101", 0f)]
    // A PREFIX, which is what makes a mapper's stray character harmless.
    [InlineData("  7  ", 7f)]
    [InlineData("7abc", 7f)]
    [InlineData("1,5", 1f)]
    [InlineData("12e2x", 1200f)]
    [InlineData("\t\n 12", 12f)]
    // An exponent marker with no digits is not part of the number.
    [InlineData("1e", 1f)]
    [InlineData("1e+", 1f)]
    [InlineData("1.", 1f)]
    public void AtofReadsTheLeadingNumber(string text, float expected)
        => Assert.Equal(expected, VariantParse.Atof(text));

    /// <summary>
    /// An exponent IS read — the case a hand-rolled digit scanner gets wrong,
    /// and the divergence this file was written to close. <c>EventAction</c>'s
    /// old parser stopped at the <c>e</c> and answered 1.
    /// </summary>
    [Theory]
    [InlineData("1e3", 1000f)]
    [InlineData("1E3", 1000f)]
    [InlineData("1e-3", 0.001f)]
    [InlineData("2.5e2", 250f)]
    public void AtofReadsAnExponent(string text, float expected)
        => Assert.Equal(expected, VariantParse.Atof(text));

    /// <summary>
    /// <c>strtod</c> takes C99 hex floats, so a string that is ZERO as an
    /// integer is sixteen as a float.
    /// </summary>
    [Theory]
    [InlineData("0x10", 16f)]
    [InlineData("0X1p4", 16f)]
    [InlineData("0x1.8p1", 3f)]
    [InlineData("0xff", 255f)]
    public void AtofReadsAHexFloat(string text, float expected)
        => Assert.Equal(expected, VariantParse.Atof(text));

    [Fact]
    public void AtofOfHexWithNoDigitsIsTheLeadingZero()
        => Assert.Equal(0f, VariantParse.Atof("0xzz"));

    [Theory]
    [InlineData("inf")]
    [InlineData("INFINITY")]
    public void AtofReadsInfinity(string text)
        => Assert.Equal(float.PositiveInfinity, VariantParse.Atof(text));

    [Fact]
    public void AtofReadsNegativeInfinity()
        => Assert.Equal(float.NegativeInfinity, VariantParse.Atof("-inf"));

    [Theory]
    [InlineData("nan")]
    [InlineData("NaN(123)")]
    public void AtofReadsNotANumber(string text)
        => Assert.True(float.IsNaN(VariantParse.Atof(text)));

    /// <summary>A value too big for a float becomes infinity, rather than failing.</summary>
    [Fact]
    public void AtofOverflowsToInfinity()
        => Assert.Equal(float.PositiveInfinity, VariantParse.Atof("1e309"));

    [Fact]
    public void AtofUnderflowsThroughDouble()
        => Assert.Equal(1e-30f, VariantParse.Atof("1e-30"));

    /// <summary>
    /// 16777217 is not a float. It is read as one anyway, which is why the
    /// integer conversion cannot go through here.
    /// </summary>
    [Fact]
    public void AtofRoundsPastTwoToTheTwentyFourth()
        => Assert.Equal(16777216f, VariantParse.Atof("16777217"));

    [Fact]
    public void AtofOfNullIsZero()
        => Assert.Equal(0f, VariantParse.Atof(null));
}

public class VariantParseAtoiTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("-1", -1)]
    [InlineData("3", 3)]
    [InlineData("+3", 3)]
    [InlineData("  7  ", 7)]
    [InlineData("7abc", 7)]
    [InlineData("\t\n 12", 12)]
    [InlineData("1,5", 1)]
    [InlineData("", 0)]
    [InlineData(" ", 0)]
    [InlineData("abc", 0)]
    [InlineData("true", 0)]
    [InlineData("--3", 0)]
    public void AtoiReadsTheLeadingInteger(string text, int expected)
        => Assert.Equal(expected, VariantParse.Atoi(text));

    /// <summary>
    /// BASE TEN AND NOTHING ELSE, which is where it parts company with
    /// <see cref="VariantParse.Atof"/> on the same string.
    /// </summary>
    [Theory]
    [InlineData("5.7", 5)]
    [InlineData("-5.7", -5)]
    [InlineData("1e3", 1)]
    [InlineData("0x10", 0)]
    [InlineData("010", 10)]
    [InlineData("12e2x", 12)]
    public void AtoiStopsAtAnythingThatIsNotADigit(string text, int expected)
        => Assert.Equal(expected, VariantParse.Atoi(text));

    /// <summary>
    /// <c>(int)strtol(...)</c>: saturate at 64 bits, then keep the low 32. A map
    /// that writes a number too big for an integer gets a negative one.
    /// </summary>
    [Theory]
    [InlineData("2147483647", 2147483647)]
    [InlineData("2147483648", -2147483648)]
    [InlineData("3000000000", -1294967296)]
    [InlineData("-2147483648", -2147483648)]
    [InlineData("-3000000000", 1294967296)]
    [InlineData("2147483647999", -1)]
    [InlineData("9999999999999999999999", -1)]
    [InlineData("-9999999999999999999999", 0)]
    public void AtoiWrapsThroughLong(string text, int expected)
        => Assert.Equal(expected, VariantParse.Atoi(text));

    [Fact]
    public void AtoiOfNullIsZero()
        => Assert.Equal(0, VariantParse.Atoi(null));
}

public class VariantParseFormatTests
{
    /// <summary>
    /// <c>%g</c>, six significant digits, fixed while the exponent is in
    /// <c>[-4, 6)</c> and scientific outside it, trailing zeros stripped, at
    /// least two exponent digits, lower-case <c>e</c>.
    /// </summary>
    [Theory]
    [InlineData(0f, "0")]
    [InlineData(1f, "1")]
    [InlineData(-1f, "-1")]
    [InlineData(0.5f, "0.5")]
    [InlineData(5.7f, "5.7")]
    [InlineData(0.1f, "0.1")]
    [InlineData(1.5f, "1.5")]
    [InlineData(-2.5f, "-2.5")]
    [InlineData(3.25f, "3.25")]
    [InlineData(100f, "100")]
    [InlineData(123456f, "123456")]
    [InlineData(999999f, "999999")]
    [InlineData(0.0001f, "0.0001")]
    [InlineData(0.000123456f, "0.000123456")]
    public void FormatGWritesFixedInsideTheBracket(float value, string expected)
        => Assert.Equal(expected, VariantParse.FormatG(value));

    [Theory]
    [InlineData(1000000f, "1e+06")]
    [InlineData(1234567f, "1.23457e+06")]
    [InlineData(16777216f, "1.67772e+07")]
    [InlineData(3e9f, "3e+09")]
    [InlineData(2147483648f, "2.14748e+09")]
    [InlineData(1e30f, "1e+30")]
    [InlineData(1e-5f, "1e-05")]
    [InlineData(1e-30f, "1e-30")]
    public void FormatGWritesScientificOutsideIt(float value, string expected)
        => Assert.Equal(expected, VariantParse.FormatG(value));

    /// <summary>Six significant digits is LOSSY, and this is what that looks like.</summary>
    [Fact]
    public void FormatGRoundsToSixSignificantDigits()
        => Assert.Equal("12345.7", VariantParse.FormatG(12345.6789f));

    [Fact]
    public void FormatGKeepsTheSignOfNegativeZero()
        => Assert.Equal("-0", VariantParse.FormatG(-0.0f));

    [Theory]
    [InlineData(float.PositiveInfinity, "inf")]
    [InlineData(float.NegativeInfinity, "-inf")]
    [InlineData(float.NaN, "nan")]
    public void FormatGSpellsTheSpecialValuesAsCDoes(float value, string expected)
        => Assert.Equal(expected, VariantParse.FormatG(value));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-7, "-7")]
    [InlineData(2147483647, "2147483647")]
    [InlineData(-2147483648, "-2147483648")]
    public void FormatIWritesTheDigits(int value, string expected)
        => Assert.Equal(expected, VariantParse.FormatI(value));
}

public class VariantParseScanVectorTests
{
    [Theory]
    [InlineData("[1 2 3]", 1f, 2f, 3f)]
    [InlineData("[1.5 -2.5 3.25]", 1.5f, -2.5f, 3.25f)]
    [InlineData("[  4   5   6  ]", 4f, 5f, 6f)]
    public void ABracketedVectorReadsWhole(string text, float x, float y, float z)
        => Assert.Equal(new Vector(x, y, z), VariantParse.ScanVector(text));

    /// <summary>The retry, which is how the form Hammer writes gets read at all.</summary>
    [Theory]
    [InlineData("1 2 3", 1f, 2f, 3f)]
    [InlineData("255 128 64", 255f, 128f, 64f)]
    public void ABareVectorReadsThroughTheRetry(string text, float x, float y, float z)
        => Assert.Equal(new Vector(x, y, z), VariantParse.ScanVector(text));

    /// <summary>
    /// <b>THE UGLY ONE.</b> Stock retries only when the bracketed form assigned
    /// NOTHING, so a truncated bracketed vector keeps what it managed to read
    /// and zeroes the rest. <c>"[1 2"</c> is a two-component vector delivered as
    /// a three-component one.
    /// </summary>
    [Fact]
    public void ATruncatedBracketedVectorKeepsWhatItRead()
        => Assert.Equal(new Vector(1f, 2f, 0f), VariantParse.ScanVector("[1 2"));

    [Fact]
    public void ATruncatedBareVectorKeepsWhatItRead()
        => Assert.Equal(new Vector(1f, 2f, 0f), VariantParse.ScanVector("1 2"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[")]
    [InlineData("abc")]
    public void AnUnreadableVectorIsTheOrigin(string text)
        => Assert.Equal(new Vector(0f, 0f, 0f), VariantParse.ScanVector(text));

    /// <summary>One number is a vector too, with the other two at zero.</summary>
    [Fact]
    public void OneNumberIsAVectorAlongX()
        => Assert.Equal(new Vector(3f, 0f, 0f), VariantParse.ScanVector("3"));
}

public class VariantParseScanColorTests
{
    [Fact]
    public void FourChannelsRead() => Assert.Equal(new Color32(255, 128, 64, 32),
                                                   VariantParse.ScanColor("255 128 64 32"));

    /// <summary>Alpha keeps its initialiser, so three channels is opaque.</summary>
    [Fact]
    public void ThreeChannelsAreOpaque() => Assert.Equal(new Color32(255, 128, 64, 255),
                                                         VariantParse.ScanColor("255 128 64"));

    /// <summary>
    /// Stock never checks <c>sscanf</c>'s return, so a channel it did not reach
    /// keeps its initialiser and there is no such thing as an unreadable colour.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void AnUnreadableColourIsOpaqueBlack(string text)
        => Assert.Equal(new Color32(0, 0, 0, 255), VariantParse.ScanColor(text));

    [Fact]
    public void TwoChannelsLeaveBlueAtZeroAndAlphaOpaque()
        => Assert.Equal(new Color32(255, 128, 0, 255), VariantParse.ScanColor("255 128"));

    /// <summary>
    /// <b>THE UGLY ONE.</b> <c>sscanf</c> reads into an <c>int</c> and
    /// <c>SetColor32</c> assigns to a byte, so a mapper who writes 300 for
    /// "brighter" gets 44, and -1 for "transparent" gets 255.
    /// </summary>
    [Fact]
    public void ChannelsTruncateToEightBits()
        => Assert.Equal(new Color32(44, 255, 0, 0), VariantParse.ScanColor("300 -1 0 0"));

    /// <summary>A colour is read with <c>%d</c>, so a fractional channel stops at the point.</summary>
    [Fact]
    public void AFractionalChannelStopsAtThePoint()
        => Assert.Equal(new Color32(5, 0, 0, 255), VariantParse.ScanColor("5.7"));
}

/// <summary>
/// That the I/O queue reads a map's numbers the same way the input path does.
///
/// <para>
/// <b>THERE WERE TWO PARSERS AND THEY DISAGREED.</b> An input's value reached
/// managed code two ways: through the native queue, where
/// <c>shell_dispatch.cpp</c> converted it with <c>variant_t::Convert</c> —
/// <c>atof</c> — and through the managed queue, where
/// <c>EntityDirectory.Deliver</c> converted it with <c>EventAction.ParseFloat</c>,
/// a hand-rolled digit scanner. On <c>"1e3"</c> the first said 1000 and the
/// second said 1, for the same map value; on <c>"0x10"</c>, 16 and 0. Which
/// answer an entity got depended on which queue happened to carry the event,
/// and neither said anything.
/// </para>
///
/// <para>
/// The scanner also read the DELAY and the TIMES-TO-FIRE off every map
/// connection, where stock is plainly <c>atof(szToken)</c> and
/// <c>atoi(szToken)</c> (<c>CEventAction::CEventAction</c>,
/// src/game/server/cbase.cpp), so the disagreement was not confined to values.
/// </para>
/// </summary>
public class EventActionParsesAsStockDoesTests
{
    [Theory]
    [InlineData("1e3", 1000f)]
    [InlineData("2.5e2", 250f)]
    [InlineData("1e-3", 0.001f)]
    public void ADelayInExponentNotationIsRead(string text, float expected)
        => Assert.Equal(expected, SourceSharp.Abi.EventAction.ParseFloat(text));

    [Fact]
    public void AHexValueIsReadAsAFloatExactlyAsAtofDoes()
        => Assert.Equal(16f, SourceSharp.Abi.EventAction.ParseFloat("0x10"));

    /// <summary>
    /// A times-to-fire too big for an integer wraps, as <c>atoi</c> wraps, rather
    /// than reading as zero — and zero means "always", so refusing the number
    /// turned a limited connection into an unlimited one.
    /// </summary>
    [Fact]
    public void ATimesToFireTooBigForAnIntegerWrapsRatherThanReadingAsZero()
        => Assert.Equal(-2147483648, SourceSharp.Abi.EventAction.ParseInt("2147483648"));

    /// <summary>The cases the old scanner already got right, kept so the change is a widening.</summary>
    [Theory]
    [InlineData("0.5 ", 0.5f)]
    [InlineData("2x", 2f)]
    [InlineData(".5", 0.5f)]
    [InlineData("+3", 3f)]
    [InlineData("", 0f)]
    [InlineData("abc", 0f)]
    public void TheCasesItAlreadyReadStillRead(string text, float expected)
        => Assert.Equal(expected, SourceSharp.Abi.EventAction.ParseFloat(text));

    /// <summary>
    /// The point of the change, stated as one assertion: the two routes an input
    /// value can take now agree by construction, because there is one parser.
    /// </summary>
    [Theory]
    [InlineData("1e3")]
    [InlineData("0x10")]
    [InlineData("5.7")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  7  ")]
    public void TheQueuesNumberAgreesWithTheInputPathsNumber(string text)
        => Assert.Equal(VariantValue.FromString(text).AsNumber,
                        SourceSharp.Abi.EventAction.ParseFloat(text));
}
