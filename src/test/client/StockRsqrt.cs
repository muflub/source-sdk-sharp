using System.Globalization;

using SourceSharp.Client.Detail;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Whether this CPU's <c>rsqrtss</c> estimate is the one the stock recordings
/// were made on.
///
/// <para>
/// The estimate's last bits differ between CPU makers (an Intel Xeon answers
/// 7 of the 8 probes below differently from the recording machine), and
/// stock's VectorNormalize, which the managed ports reproduce through
/// <c>MoveVec.RSqrt</c>, starts from it. A recording replays bit for bit only
/// on a CPU that gives the recording machine's estimates; on any other, the
/// rows built on a normalised vector land a few ulps away and nothing else
/// changes.
/// </para>
///
/// <para>
/// The probes are the <c>RSQ</c> rows the detail props oracle recorded beside
/// its stock run (<c>Fixtures/detailprops-stock.tsv</c>): an input and the
/// recording machine's estimate for it. The other stock recordings were made
/// on the same machine, so its estimates are theirs too.
/// </para>
/// </summary>
internal static class StockRsqrt
{
    /// <summary>The recorded probes, as (input bits, estimate bits); a missing or probe-less fixture fails.</summary>
    public static IReadOnlyList<(uint Input, uint Estimate)> Probes()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "detailprops-stock.tsv");
        Assert.True(File.Exists(path), "the recorded rsqrtss probes are missing: " + path);

        var probes = File.ReadLines(path)
            .Where(l => l.StartsWith("RSQ\t", StringComparison.Ordinal))
            .Select(l => l.Split('\t'))
            .Select(c => (uint.Parse(c[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                          uint.Parse(c[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.True(probes.Count > 0, "the stock recording carries rsqrtss probes");
        return probes;
    }

    /// <summary>True when this CPU's estimate gives every probe's recorded bits.</summary>
    public static bool MatchesRecording()
        => Probes().All(p => BitConverter.SingleToUInt32Bits(
                                 DetailMath.ReciprocalSqrtEstimate(BitConverter.UInt32BitsToSingle(p.Input))) == p.Estimate);
}
