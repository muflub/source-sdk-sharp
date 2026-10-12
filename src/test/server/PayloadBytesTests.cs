using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Server.Interface;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dev API's copies of the payload byte utilities (G5) answer exactly as
/// the game's NetSchema does, so the payload view did not change by moving.
/// </summary>
public class PayloadBytesTests
{
    public static TheoryData<byte[], byte[]> Pairs => new()
    {
        { [], [] },
        { [1, 2, 3], [1, 2, 3] },
        { [1, 2, 3, 4], [1, 9, 9, 4] },
        { [1, 2, 3, 4], [9, 2, 3, 9] },
        { [1, 2], [1, 2, 3, 4] },
        { [0xFF, 0x00, 0x7F], [0x00, 0xFF, 0x7F] },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Hex_is_the_games(byte[] previous, byte[] current)
    {
        Assert.Equal(NetSchema.Hex(previous), PayloadBytes.Hex(previous));
        Assert.Equal(NetSchema.Hex(current), PayloadBytes.Hex(current));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Changed_runs_are_the_games(byte[] previous, byte[] current)
        => Assert.Equal(NetSchema.ChangedRuns(previous, current), PayloadBytes.ChangedRuns(previous, current));

    [Theory]
    [InlineData(0, 4)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(8, 4)]
    public void Touches_is_the_games(int offset, int bytes)
    {
        IReadOnlyList<(int, int)> runs = [(1, 2), (6, 2)];
        var field = new NetSchemaField("F", "int", NetFieldKind.Int, offset, bytes, bytes * 8);
        var copy = new DebugNetField("F", "int", "int", offset, bytes, bytes * 8, 0, false, null);
        Assert.Equal(NetSchema.Touches(field, runs), PayloadBytes.Touches(copy, runs));
    }

    [Fact]
    public void The_cases_reach_both_answers()
    {
        Assert.Equal([(1, 2)], PayloadBytes.ChangedRuns(new byte[] { 1, 2, 3, 4 }, new byte[] { 1, 9, 9, 4 }));
        Assert.True(PayloadBytes.Touches(new DebugNetField("F", "int", "int", 2, 1, 8, 0, false, null), [(1, 2)]));
        Assert.False(PayloadBytes.Touches(new DebugNetField("F", "int", "int", 8, 4, 32, 0, false, null), [(1, 2)]));
        Assert.Equal("ff007f", PayloadBytes.Hex(new byte[] { 0xFF, 0x00, 0x7F }));
    }
}
