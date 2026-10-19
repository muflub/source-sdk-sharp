using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real one, over <c>ClientRecvProp</c> and <c>ClientSchemaRefusal</c>.</summary>
internal sealed unsafe class ClientRecvTables(StandaloneClientArea standaloneClient) : IClientRecvTables
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

    public (int RefusedUpdates, IReadOnlyList<ClientSchemaRefusalRow> Types) Refusals()
    {
        int updates = 0;
        int types = standaloneClient.ClientSchemaRefusal(-1, null, null, null, null, null, 0, &updates);
        var rows = new List<ClientSchemaRefusalRow>(Math.Max(types, 0));
        byte* cls = stackalloc byte[NameBytes];
        for (int i = 0; i < types; i++)
        {
            uint id = 0, expected = 0, seen = 0;
            int count = 0;
            cls[0] = 0;
            standaloneClient.ClientSchemaRefusal(i, &id, &expected, &seen, &count, cls, NameBytes, &updates);
            rows.Add(new ClientSchemaRefusalRow(id, expected, seen, count, Text(cls)));
        }

        return (updates, rows);
    }

    private static string Text(byte* bytes)
    {
        int n = 0;
        while (n < NameBytes && bytes[n] != 0)
            n++;
        return Encoding.UTF8.GetString(bytes, n);
    }
}
