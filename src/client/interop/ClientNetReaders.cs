using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real ones, over the api.</summary>
internal sealed unsafe class ClientNetReaders(DiagnosticsClientArea diagnosticsClient, NetClientArea netClient) : IClientNetChannel, IClientDiagnosticsLedgers
{
    private const int Groups = IClientNetChannel.Groups;

    private const int NameBytes = 256;

    public ClientNetChannelRow? Channel()
    {
        float* v = stackalloc float[11];
        if (netClient.ClientNetChannel(v, 11) < 11)
            return null;
        return new ClientNetChannelRow(v[0], v[1], (int)v[2], (int)v[3], v[4] != 0, v[5] != 0, v[6], v[7], v[8], v[9], v[10]);
    }

    public ClientNetFlowRow? Flow(int flow)
    {
        float* v = stackalloc float[8];
        if (netClient.ClientNetFlow(flow, v, 8) < 8)
            return null;
        return new ClientNetFlowRow(v[0], v[1], v[2], v[3], v[4], v[5], (int)v[6], (int)v[7]);
    }

    public ClientNetPacketRow? Packet(int flow, int sequence)
    {
        const int count = 5 + Groups;
        float* v = stackalloc float[count];
        if (netClient.ClientNetPacket(flow, sequence, v, count) < count)
            return null;
        var groups = new int[Groups];
        for (int g = 0; g < Groups; g++)
            groups[g] = (int)v[5 + g];
        return new ClientNetPacketRow(v[0] != 0, v[1], v[2], (int)v[3], (int)v[4], groups);
    }

    public (int Total, IReadOnlyList<ClientAbsenceRow> Rows) NotShipped()
    {
        int total = 0, count = 0;
        int names = diagnosticsClient.ClientNotShipped(-1, null, 0, null, 0, null, &total);
        byte* name = stackalloc byte[NameBytes];
        byte* reason = stackalloc byte[NameBytes];
        var rows = new List<ClientAbsenceRow>(Math.Max(names, 0));
        for (int i = 0; i < names; i++)
        {
            name[0] = reason[0] = 0;
            count = 0;
            diagnosticsClient.ClientNotShipped(i, name, NameBytes, reason, NameBytes, &count, &total);
            rows.Add(new ClientAbsenceRow(Text(name), Text(reason), count));
        }
        return (total, rows);
    }

    public (int Total, IReadOnlyList<ClientAbsenceRow> Rows) Refusals()
    {
        int total = 0, count = 0;
        int methods = diagnosticsClient.ClientRefusal(-1, null, 0, null, &total);
        byte* name = stackalloc byte[NameBytes];
        var rows = new List<ClientAbsenceRow>(Math.Max(methods, 0));
        for (int i = 0; i < methods; i++)
        {
            name[0] = 0;
            count = 0;
            diagnosticsClient.ClientRefusal(i, name, NameBytes, &count, &total);
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
