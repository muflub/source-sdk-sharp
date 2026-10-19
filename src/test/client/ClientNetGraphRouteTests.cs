using System.Text.Json;

using SourceSharp.Client;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>netgraph</c> and <c>absences</c> routes
/// (plan_client_novalve §4.4): what CNetGraphPanel drew, and the not-shipped
/// and refused rows raised this run.
/// </summary>
public class ClientNetGraphRouteTests
{
    private sealed class FakeChannel(bool connected) : IClientNetChannel
    {
        public readonly List<int> Asked = [];

        public ClientNetChannelRow? Channel() => connected
            ? new ClientNetChannelRow(100f, 42f, 64, 80000, true, false, 0.015f, 0.016f, 0.002f, 30f, 0.1f)
            : null;

        public ClientNetFlowRow? Flow(int flow) => connected
            ? new ClientNetFlowRow(0.02f, 0.025f, 0.01f, 0f, 5000f + flow, 66f, 123456, flow == 1 ? 900 : 450)
            : null;

        public ClientNetPacketRow? Packet(int flow, int sequence)
        {
            Asked.Add(sequence);
            var groups = new int[IClientNetChannel.Groups];
            groups[3] = 120;               // entities
            groups[6] = 8;                 // user messages
            groups[^1] = 140;              // total
            return new ClientNetPacketRow(sequence % 10 != 0, sequence * 0.015f, 0.1f, 20, 0, groups);
        }
    }

    private static JsonElement Read(IClientNetChannel channel, string frames, out JsonDocument doc)
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(netGraph: new ClientDebugNetGraph(channel)));
        doc = JsonDocument.Parse(client.Answer(DebugChannelQuery.NetGraph, frames));
        return doc.RootElement;
    }

    [Fact]
    public void The_route_reads_both_flows_and_the_newest_incoming_packets()
    {
        var channel = new FakeChannel(connected: true);
        var root = Read(channel, "5", out var doc);
        using (doc)
        {
            Assert.True(root.GetProperty("connected").GetBoolean());
            Assert.Equal(["outgoing", "incoming"], root.GetProperty("flows").EnumerateArray().Select(f => f.GetProperty("flow").GetString()));
            Assert.Equal(20f, root.GetProperty("flows")[0].GetProperty("latencyMs").GetSingle(), 3);
            Assert.Equal(100f, root.GetProperty("interpMs").GetSingle(), 3);

            // The positive arm: five packets, newest first from the incoming
            // sequence, each with its bytes by group and the total.
            Assert.Equal([900, 899, 898, 897, 896], channel.Asked);
            var first = root.GetProperty("packets")[0];
            Assert.Equal(140, first.GetProperty("bytes").GetInt32());
            Assert.Equal(120, first.GetProperty("groups").GetProperty("entities").GetInt32());
            Assert.False(first.GetProperty("valid").GetBoolean());
        }
    }

    [Fact]
    public void The_frames_asked_for_are_capped_at_the_panels_history()
    {
        var channel = new FakeChannel(connected: true);
        Read(channel, "1000", out var doc).GetProperty("packets");
        doc.Dispose();
        Assert.Equal(ClientDebugNetGraph.MaxFrames, channel.Asked.Count);
    }

    [Fact]
    public void Without_a_channel_it_says_not_connected()
    {
        var root = Read(new FakeChannel(connected: false), "", out var doc);
        using (doc)
        {
            Assert.False(root.GetProperty("connected").GetBoolean());
            Assert.Equal(0, root.GetProperty("packets").GetArrayLength());
        }
    }

    private sealed class Ledgers : IClientDiagnosticsLedgers
    {
        public (int Total, IReadOnlyList<ClientAbsenceRow> Rows) NotShipped()
            => (7, [new ClientAbsenceRow("C_HLTVCamera", "SourceTV camera not shipped", 7)]);

        public (int Total, IReadOnlyList<ClientAbsenceRow> Rows) Refusals()
            => (1, [new ClientAbsenceRow("IBaseClientDLL::GetPresenceID", "refused: unreachable in this build", 1)]);
    }

    [Fact]
    public void The_absences_answer_renders_exactly()
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(absences: new ClientDebugAbsences(new Ledgers())));
        Assert.Equal(DebugApiClientGoldenTests.Golden("absences"), client.Answer(DebugChannelQuery.Absences));
    }

    private sealed class FixedNetGraph(DebugClientNetGraph graph) : IDebugClientNetGraph
    {
        public DebugClientNetGraph Read(int frames) => graph;
    }

    [Fact]
    public void The_netgraph_answer_renders_exactly()
    {
        var graph = new DebugClientNetGraph(true, 42f, 80000, true, false, 15f, 16f, 2f, 100f,
            [new DebugClientNetFlow("incoming", 20f, 25f, 0.01f, 0f, 5001f, 66f, 123456, 900)],
            [new DebugClientNetPacket(900, true, 13.5f, 0.1f, 20, 0, 140, [new DebugCount("entities", 120)])]);

        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(netGraph: new FixedNetGraph(graph)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("netgraph"), client.Answer(DebugChannelQuery.NetGraph));
    }
}
