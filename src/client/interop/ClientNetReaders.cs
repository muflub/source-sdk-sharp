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

/// <summary>The real ones, over the api.</summary>
internal sealed unsafe class ClientNetReaders(EngineSide api) : IClientNetChannel, IClientDiagnosticsLedgers
{
    private const int Groups = IClientNetChannel.Groups;

    private const int NameBytes = 256;

    public ClientNetChannelRow? Channel()
    {
        if (!api.ClientAttached)
            return null;
        float* v = stackalloc float[11];
        if (api.ClientShims.ClientNetChannel(v, 11) < 11)
            return null;
        return new ClientNetChannelRow(v[0], v[1], (int)v[2], (int)v[3], v[4] != 0, v[5] != 0, v[6], v[7], v[8], v[9], v[10]);
    }

    public ClientNetFlowRow? Flow(int flow)
    {
        if (!api.ClientAttached)
            return null;
        float* v = stackalloc float[8];
        if (api.ClientShims.ClientNetFlow(flow, v, 8) < 8)
            return null;
        return new ClientNetFlowRow(v[0], v[1], v[2], v[3], v[4], v[5], (int)v[6], (int)v[7]);
    }

    public ClientNetPacketRow? Packet(int flow, int sequence)
    {
        if (!api.ClientAttached)
            return null;
        const int count = 5 + Groups;
        float* v = stackalloc float[count];
        if (api.ClientShims.ClientNetPacket(flow, sequence, v, count) < count)
            return null;
        var groups = new int[Groups];
        for (int g = 0; g < Groups; g++)
            groups[g] = (int)v[5 + g];
        return new ClientNetPacketRow(v[0] != 0, v[1], v[2], (int)v[3], (int)v[4], groups);
    }

    public (int Total, IReadOnlyList<ClientAbsenceRow> Rows) NotShipped()
    {
        if (!api.ClientAttached)
            return (0, []);
        int total = 0, count = 0;
        int names = api.ClientShims.ClientNotShipped(-1, null, 0, null, 0, null, &total);
        byte* name = stackalloc byte[NameBytes];
        byte* reason = stackalloc byte[NameBytes];
        var rows = new List<ClientAbsenceRow>(Math.Max(names, 0));
        for (int i = 0; i < names; i++)
        {
            name[0] = reason[0] = 0;
            count = 0;
            api.ClientShims.ClientNotShipped(i, name, NameBytes, reason, NameBytes, &count, &total);
            rows.Add(new ClientAbsenceRow(Text(name), Text(reason), count));
        }
        return (total, rows);
    }

    public (int Total, IReadOnlyList<ClientAbsenceRow> Rows) Refusals()
    {
        if (!api.ClientAttached)
            return (0, []);
        int total = 0, count = 0;
        int methods = api.ClientShims.ClientRefusal(-1, null, 0, null, &total);
        byte* name = stackalloc byte[NameBytes];
        var rows = new List<ClientAbsenceRow>(Math.Max(methods, 0));
        for (int i = 0; i < methods; i++)
        {
            name[0] = 0;
            count = 0;
            api.ClientShims.ClientRefusal(i, name, NameBytes, &count, &total);
            rows.Add(new ClientAbsenceRow(Text(name), "refused: unreachable in this build", count));
        }
        return (total, rows);
    }

    private static string Text(byte* bytes)
    {
        int n = 0;
        while (n < NameBytes && bytes[n] != 0)
            n++;
        return Encoding.UTF8.GetString(bytes, n);
    }
}
