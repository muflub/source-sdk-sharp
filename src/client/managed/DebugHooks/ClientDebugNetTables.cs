using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientNetTables"/> over the engine's recv tables, this
/// side's declared types and tiers, and the schema gate's refusals
/// (plan_client_novalve §4.4). The agreement is decided here: a tier agrees
/// when the engine's table holds the props the tier says it carries.
/// </summary>
public sealed class ClientDebugNetTables(IClientRecvTables recv, INetTiers tiers, ClientNetSchemaTypes types,
                                         IClientSchemaGate schemaGate)
    : IDebugClientNetTables
{
    /// <summary>The engine's SendPropType names, by number (dt_common.h).</summary>
    private static string TypeName(int type) => type switch
    {
        0 => "int", 1 => "float", 2 => "vector", 3 => "vectorxy", 4 => "string", 5 => "array",
        6 => "datatable", 7 => "int64", _ => $"type{type}",
    };

    public DebugClientNetTables Read()
    {
        var props = recv.Props();
        var tables = props
            .GroupBy(p => p.Table, StringComparer.Ordinal)
            .Select(g => new DebugClientRecvTable(g.Key, g.First().ClassName,
                g.Select(p => new DebugClientRecvProp(p.Prop, TypeName(p.Type), p.Flags, p.Offset, p.Elements)).ToArray()))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var engineProps = tables.ToDictionary(t => t.Name, t => t.Props.Count, StringComparer.Ordinal);
        var tierRows = tiers.For(NetShellKind.Standalone)
            .Select(t => new DebugClientTierAgreement(t.Table, t.Props, engineProps.GetValueOrDefault(t.Table, -1),
                                                      engineProps.GetValueOrDefault(t.Table, -1) == t.Props))
            .ToArray();

        var declared = types.All
            .Select(t => new DebugClientNetType(t.ClassName, t.TypeId, t.SchemaHash, t.TableName, t.LayoutBytes,
                t.PayloadWords,
                t.Fields.Select(f => new DebugClientNetField(f.Name, f.Kind.ToString().ToLowerInvariant(), f.Offset,
                                                            f.Bytes, f.Bits, f.BitShift)).ToArray()))
            .ToArray();

        var (updates, refused) = schemaGate.Refusals();
        var refusals = refused.Select(r => new DebugClientSchemaRefusal(r.ClassName, r.TypeId, r.Expected, r.Seen, r.Count))
                              .ToArray();

        bool agrees = tierRows.Length > 0 && tierRows.All(t => t.Agrees) && refusals.Length == 0;
        return new DebugClientNetTables(tables, declared, tierRows, updates, refusals, agrees);
    }
}
