using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>One prop of one recv table, as the engine holds it.</summary>
/// <param name="ClassName">The ClientClass whose root table this is; empty for a nested table.</param>
/// <param name="Type">The engine's SendPropType number.</param>
public readonly record struct ClientRecvPropRow(
    string ClassName, string Table, string Prop, int Type, int Flags, int Offset, int Elements);

/// <summary>One type whose schema hash this client refused (the payload decoder's send/recv agreement).</summary>
public readonly record struct ClientSchemaRefusalRow(uint TypeId, uint Expected, uint Seen, int Count, string ClassName);

/// <summary>
/// The client's recv tables as the engine walks them, and the schema refusals
/// the payload decoder made (plan_client_novalve §4.4, the nettables route).
/// Game thread.
/// </summary>
public interface IClientRecvTables
{
    /// <summary>Every prop of every reachable recv table, each table once.</summary>
    IReadOnlyList<ClientRecvPropRow> Props();

    /// <summary>How many updates were refused, and the types they were spread over.</summary>
    (int RefusedUpdates, IReadOnlyList<ClientSchemaRefusalRow> Types) Refusals();
}

/// <summary>The real one, over <c>ClientRecvProp</c> and <c>ClientSchemaRefusal</c>.</summary>
internal sealed unsafe class ClientRecvTables(EngineSide api) : IClientRecvTables
{
    private const int NameBytes = 128;

    public IReadOnlyList<ClientRecvPropRow> Props()
    {
        if (!api.ClientAttached)
            return [];

        byte* cls = stackalloc byte[NameBytes];
        byte* table = stackalloc byte[NameBytes];
        byte* prop = stackalloc byte[NameBytes];
        int type, flags, offset, elements;

        int total = api.ClientShims.ClientRecvProp(-1, null, 0, null, 0, null, 0, null, null, null, null);
        var rows = new List<ClientRecvPropRow>(Math.Max(total, 0));
        for (int i = 0; i < total; i++)
        {
            cls[0] = table[0] = prop[0] = 0;
            type = flags = offset = elements = 0;
            api.ClientShims.ClientRecvProp(i, cls, NameBytes, table, NameBytes, prop, NameBytes,
                                      &type, &flags, &offset, &elements);
            rows.Add(new ClientRecvPropRow(Text(cls), Text(table), Text(prop), type, flags, offset, elements));
        }

        return rows;
    }

    public (int RefusedUpdates, IReadOnlyList<ClientSchemaRefusalRow> Types) Refusals()
    {
        if (!api.ClientAttached)
            return (0, []);

        int updates = 0;
        int types = api.ClientShims.ClientSchemaRefusal(-1, null, null, null, null, null, 0, &updates);
        var rows = new List<ClientSchemaRefusalRow>(Math.Max(types, 0));
        byte* cls = stackalloc byte[NameBytes];
        for (int i = 0; i < types; i++)
        {
            uint id = 0, expected = 0, seen = 0;
            int count = 0;
            cls[0] = 0;
            api.ClientShims.ClientSchemaRefusal(i, &id, &expected, &seen, &count, cls, NameBytes, &updates);
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
