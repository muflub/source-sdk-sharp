using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>One flow of the engine's net channel (0 outgoing, 1 incoming), as CNetGraphPanel read it.</summary>
public readonly record struct ClientNetFlowRow(
    float Latency, float AvgLatency, float AvgLoss, float AvgChoke, float AvgData, float AvgPackets,
    int TotalData, int Sequence);

/// <summary>The channel as a whole, and this client's interpolation amount.</summary>
public readonly record struct ClientNetChannelRow(
    float Time, float TimeConnected, int BufferSize, int DataRate, bool Loopback, bool TimingOut,
    float SinceLastReceived, float RemoteFrameTime, float RemoteFrameTimeDeviation, float TimeoutSeconds,
    float InterpAmount);

/// <summary>One packet of one flow: whether it arrived, when, and its bytes per message group (the last is the total).</summary>
public readonly record struct ClientNetPacketRow(
    bool Valid, float Time, float CommandInterpolation, int ResponseLatencyMs, int Choke, int[] GroupBytes);

/// <summary>
/// The engine's net channel, read through IShimNetClient's three forwards
/// (plan_client_novalve §4.4, the netgraph route). Null with no channel.
/// Game thread.
/// </summary>
public interface IClientNetChannel
{
    /// <summary>INetChannelInfo's message groups, GENERIC through SIGNON, then TOTAL.</summary>
    const int Groups = 14;

    ClientNetChannelRow? Channel();

    ClientNetFlowRow? Flow(int flow);

    ClientNetPacketRow? Packet(int flow, int sequence);
}

/// <summary>One row of a diagnostics ledger: what was reached, why it is absent, and how often.</summary>
public readonly record struct ClientAbsenceRow(string Name, string Reason, int Count);

/// <summary>
/// The client's not-shipped and refused-method ledgers, read through
/// IShimDiagnosticsClient (the absences route). Game thread.
/// </summary>
public interface IClientDiagnosticsLedgers
{
    (int Total, IReadOnlyList<ClientAbsenceRow> Rows) NotShipped();

    (int Total, IReadOnlyList<ClientAbsenceRow> Rows) Refusals();
}
