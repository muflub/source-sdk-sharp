using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That the test fixture's tier table is the one native registers.
///
/// <para>
/// WHY THIS EXISTS. <see cref="TestServices.SupplyTiers"/> is a hand-written
/// stand-in for what native reports, and it spent several steps describing
/// <c>{32, 256, 2048}</c> after native had grown to seven rows. Nothing failed:
/// the only suite using it supplies the table and reads the same table back,
/// which is true whatever is in it. A test asking which tier a 100-byte type
/// lands in would have been told 256, where the game says 128, and would have
/// passed while being wrong about the thing it was checking.
/// </para>
///
/// <para>
/// A fixture that is allowed to disagree with the thing it stands in for is a
/// check that cannot fail, so the agreement is asserted rather than maintained.
/// This reads the tier list out of generic_nettable.h for the same reason
/// <see cref="AbiParityTests"/> reads the ABI header: the C side is the
/// authority, and a second copy of a boundary rule drifts.
/// </para>
/// </summary>
public class TierParityTests
{
    [Fact]
    public void TheFixtureIsTheTierListNativeRegisters()
    {
        string? header = FindHeader();

        if (header is null)
        {
            // Skipped rather than failed, matching AbiParityTests: the tests
            // must still run from a directory with no source tree next to it.
            Assert.True(true, "the tier header was found (skipped, not found)");
            return;
        }

        List<int> native = TierSizesIn(File.ReadAllText(header));

        Assert.True(native.Count > 0, "the tier list in generic_nettable.h parsed into at least one row");

        int[] fixtureSizes = TestServices.SupplyTiers()
            .For(NetShellKind.Standalone)
            .Select(t => t.PayloadBytes)
            .ToArray();

        Assert.True(fixtureSizes.SequenceEqual(native),
            $"the fixture's tiers are native's, in order (fixture {Show(fixtureSizes)}, native {Show(native)})");
    }

    /// <summary>
    /// That a payload lands in the smallest tier carrying it, over the sizes
    /// where the answer changed when 64, 128, 512 and 1024 arrived.
    ///
    /// <para>
    /// Each of these resolved to a bigger tier under the old three-row table, so
    /// this fails against the stale fixture rather than merely differing from it.
    /// </para>
    /// </summary>
    [Fact]
    public void APayloadLandsInTheSmallestTierThatCarriesIt()
    {
        var tiers = TestServices.SupplyTiers().For(NetShellKind.Standalone);

        (int Payload, int Expected)[] cases =
        {
            (1, 32), (32, 32),
            (33, 64), (64, 64),
            (65, 128), (100, 128), (128, 128),
            (129, 256), (256, 256),
            (257, 512), (512, 512),
            (513, 1024), (1024, 1024),
            (1025, 2048), (2048, 2048),
        };

        foreach (var (payload, expected) in cases)
        {
            NetTierInfo tier = tiers.First(t => t.PayloadBytes >= payload);
            Assert.True(tier.PayloadBytes == expected,
                $"{payload} bytes lands on the {expected} tier, not {tier.PayloadBytes}");
        }

        // Never a clamp. A payload no tier carries has no answer, and rounding
        // it down to the largest would put it on a table too small to hold it.
        Assert.True(!tiers.Any(t => t.PayloadBytes >= 2049), "nothing carries a payload past the largest tier");
    }

    /// <summary>
    /// That each row's word and prop counts follow from its size, so a row added
    /// with numbers that disagree with its own byte count is caught here.
    /// </summary>
    [Fact]
    public void EachRowsCountsFollowFromItsSize()
    {
        foreach (NetTierInfo tier in TestServices.SupplyTiers().For(NetShellKind.Standalone))
        {
            Assert.True(tier.Words == tier.PayloadBytes / 4,
                $"the {tier.PayloadBytes} tier's words are its bytes over four");

            // SOURCESHARP_TIER_PROPS_STANDALONE, the rows of StandaloneNetFields.
            Assert.True(tier.Props == tier.Words + 10,
                $"the {tier.PayloadBytes} tier carries ten real props in front of its payload");

            Assert.True(tier.Table == $"DT_SourceSharpStdEnt{tier.PayloadBytes}",
                $"the {tier.PayloadBytes} tier's table is named for its size");
        }
    }

    /// <summary>
    /// That the managed prop count is the number of shared fields native puts
    /// in front of every payload.
    ///
    /// <para>
    /// It said six for two rows after <c>m_fEffects</c> and
    /// <c>m_ubInterpolationFrame</c> joined <c>StandaloneNetFields</c>, and so
    /// did the native constant; /client/{slot}/nettables then reported every
    /// tier disagreeing with the engine's recv tables. Native holds its own
    /// constant to the list with a static_assert; this holds the managed one.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSharedPropCountIsTheNativeFieldList()
    {
        string? header = RepoLayout.FindSdkFile(
            Path.Combine("src", "sourcesharp", "game", "shared", "standalone_nettable.h"));

        Assert.True(header is not null, "standalone_nettable.h is in the compat farm");

        int rows = SharedFieldRowsIn(File.ReadAllText(header!));

        Assert.True(rows > 0, "StandaloneNetFields parsed into at least one row");
        Assert.True(NetSchema.StandaloneSharedProps == rows,
            $"NetSchema.StandaloneSharedProps is {NetSchema.StandaloneSharedProps}; StandaloneNetFields has {rows} row(s)");
    }

    /// <summary>
    /// The rows of the <c>s_Fields</c> initializer in <c>StandaloneNetFields</c>:
    /// each one opens with a quoted m_ name and a STANDALONE_NET_ kind.
    /// </summary>
    private static int SharedFieldRowsIn(string text)
    {
        int start = text.IndexOf("StandaloneNetFields(", StringComparison.Ordinal);
        int end   = start < 0 ? -1 : text.IndexOf("};", start, StringComparison.Ordinal);

        if (start < 0 || end < 0)
            return 0;

        return Regex.Matches(text[start..end], @"\{\s*""m_\w+"",\s*STANDALONE_NET_\w+").Count;
    }

    private static string Show(IEnumerable<int> sizes) => "{" + string.Join(", ", sizes) + "}";

    /// <summary>
    /// The payload sizes of <c>SourceSharp_Tiers</c>, in declaration order. Each
    /// row ends with its byte count, which is the last number on the line
    /// carrying the standalone table name.
    /// </summary>
    private static List<int> TierSizesIn(string text)
    {
        var sizes = new List<int>();

        foreach (Match m in Regex.Matches(text, @"""CSourceSharpStdEnt(\d+)"",\s*""DT_SourceSharpStdEnt\d+"",\s*(\d+)"))
            sizes.Add(int.Parse(m.Groups[2].Value));

        return sizes;
    }

    /// <summary>The tier header in the compat farm.</summary>
    private static string? FindHeader()
    {
        string relative = Path.Combine("src", "sourcesharp", "game", "shared", "generic_nettable.h");

        return RepoLayout.FindSdkFile(relative);
    }
}
