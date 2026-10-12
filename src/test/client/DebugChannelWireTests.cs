using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The debug channel's frames on the wire (plan_devapi_interfaces P4): golden
/// bytes for each frame kind, written while the codec still spoke the Abi's
/// prediction records, so moving it into Debug.Api.Shared cannot change a byte.
/// </summary>
public class DebugChannelWireTests
{
    internal static PredictionErrorRecord Record() => new(
        sequence: 41, command: 7, typeId: 0xCAFE_F00Du, className: "ss_wire",
        blobBytes: 64, standingBytes: 4, firstDiffOffset: 12, toleratedFields: 1,
        timeUtc: new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
        fields:
        [
            new PredictionFieldError("Origin", 7, 12, 12, 0.125f, false, 4,
                [new PredictionComponent(1.5, 1.25, 0.25), new PredictionComponent(-2, -2, 0), new PredictionComponent(3, 3.5, -0.5)]),
            new PredictionFieldError("Health", 1, 24, 4, 0f, true, 1, []),
        ]);

    internal static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    internal static readonly IReadOnlyDictionary<string, string> Golden = new Dictionary<string, string>
    {
        ["hello"] = "01010000001100000031323a30303a3030206162633132333435",
        ["query"] = "050900000001",
        ["reply"] = "0609000000c80000007b2273696465223a22636c69656e74227d",
        ["command"] = "0303",
        ["pong"] = "04",
        ["batch"] = "02010000002900000000000000070000000df0feca0700000073735f7769726540000000040000000c0000000100000000605caf7c20df0802000000060000004f726967696e070000000c0000000c0000000000003e000400000003000000000000000000f83f000000000000f43f000000000000d03f00000000000000c000000000000000c0000000000000000000000000000008400000000000000c40000000000000e0bf060000004865616c746801000000180000000400000000000000010100000000000000",
    };

    [Fact]
    public void Each_frame_kind_writes_its_golden_bytes()
    {
        Assert.Equal(Golden["hello"], Hex(DebugChannelCodec.Hello("12:00:00 abc12345")));
        Assert.Equal(Golden["query"], Hex(DebugChannelCodec.Query(9, DebugChannelQuery.Status)));
        Assert.Equal(Golden["reply"], Hex(DebugChannelCodec.Reply(9, 200, "{\"side\":\"client\"}")));
        Assert.Equal(Golden["command"], Hex(DebugChannelCodec.Command(DebugChannelCommand.SendPredictionNow)));
        Assert.Equal(Golden["pong"], Hex(DebugChannelCodec.Pong()));
        Assert.Equal(Golden["batch"], Hex(DebugChannelCodec.PredictionBatch([Record().ToWire()])));
    }

    [Fact]
    public void The_golden_batch_reads_back_field_for_field()
    {
        Assert.True(DebugChannelCodec.TryReadPredictionBatch(Convert.FromHexString(Golden["batch"]), out var records));
        var r = Assert.Single(records);
        Assert.Equal((41L, 7, 0xCAFE_F00Du, "ss_wire", 64, 4, 12, 1), (r.Sequence, r.Command, r.TypeId, r.ClassName,
                     r.BlobBytes, r.StandingBytes, r.FirstDiffOffset, r.ToleratedFields));
        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), r.TimeUtc);
        Assert.Equal(["Origin", "Health"], r.Fields.Select(f => f.Name));
        Assert.Equal([0.25, 0, -0.5], r.Fields[0].Components.Select(c => c.Delta));
        Assert.Equal((7, 12, 12, 0.125f, false, 4), (r.Fields[0].Kind, r.Fields[0].Offset, r.Fields[0].Bytes,
                     r.Fields[0].Tolerance, r.Fields[0].Tolerated, r.Fields[0].DifferingBytes));
        Assert.True(r.Fields[1].Tolerated);
    }
}
