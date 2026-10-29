using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real one, over <c>ClientRecvProp</c>.</summary>
internal sealed unsafe class ClientRecvTables(IStandaloneClientArea standaloneClient) : IClientRecvTables
{
    private const int NameBytes = 128;

    public IReadOnlyList<ClientRecvPropRow> Props()
    {
        byte* cls = stackalloc byte[NameBytes];
        byte* table = stackalloc byte[NameBytes];
        byte* prop = stackalloc byte[NameBytes];
        int type, flags, offset, elements;

        int total = standaloneClient.ClientRecvProp(-1, null, 0, null, 0, null, 0, null, null, null, null);
        var rows = new List<ClientRecvPropRow>(Math.Max(total, 0));
        for (int i = 0; i < total; i++)
        {
            cls[0] = table[0] = prop[0] = 0;
            type = flags = offset = elements = 0;
            standaloneClient.ClientRecvProp(i, cls, NameBytes, table, NameBytes, prop, NameBytes,
                                      &type, &flags, &offset, &elements);
            rows.Add(new ClientRecvPropRow(Text(cls), Text(table), Text(prop), type, flags, offset, elements));
        }

        return rows;
    }

    private static string Text(byte* bytes)
    {
        int n = 0;
        while (n < NameBytes && bytes[n] != 0)
            n++;
        return Encoding.UTF8.GetString(bytes, n);
    }
}
