namespace SourceSharp.Client.Interface;

/// <summary>One prop of a recv table, as the engine holds it.</summary>
/// <param name="Type">The engine's prop type (int, float, vector, string, array, datatable, int64).</param>
public sealed record DebugClientRecvProp(string Name, string Type, int Flags, int Offset, int Elements);

/// <summary>One recv table, as the engine holds it.</summary>
/// <param name="ClassName">The ClientClass whose root table this is; empty for a nested table.</param>
public sealed record DebugClientRecvTable(string Name, string ClassName, IReadOnlyList<DebugClientRecvProp> Props);

/// <summary>One declared field of a managed networked type: its place in the blob and on the wire.</summary>
public sealed record DebugClientNetField(string Name, string Kind, int Offset, int Bytes, int Bits, int Shift);

/// <summary>One managed networked type this side declares, and the tier table it travels on.</summary>
public sealed record DebugClientNetType(
    string ClassName, uint TypeId, uint SchemaHash, string Table, int LayoutBytes, int Words,
    IReadOnlyList<DebugClientNetField> Fields);

/// <summary>One tier: the props the tier says it carries against the props the engine's table holds.</summary>
public sealed record DebugClientTierAgreement(string Table, int TierProps, int EngineProps, bool Agrees);

/// <summary>One type whose schema hash the payload decoder refused: the server's and this side's disagree.</summary>
public sealed record DebugClientSchemaRefusal(string ClassName, uint TypeId, uint Expected, uint Seen, int Count);

/// <summary>
/// The client's recv tables and its send/recv agreement, as
/// <c>/client/{slot}/nettables</c> reports them (plan_client_novalve §4.4).
/// </summary>
/// <param name="Agrees">Every tier agrees and no schema was refused.</param>
public sealed record DebugClientNetTables(
    IReadOnlyList<DebugClientRecvTable> Tables, IReadOnlyList<DebugClientNetType> Types,
    IReadOnlyList<DebugClientTierAgreement> Tiers, int RefusedUpdates,
    IReadOnlyList<DebugClientSchemaRefusal> Refusals, bool Agrees);

/// <summary>The client's recv tables and send/recv agreement (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientNetTables
{
    DebugClientNetTables Read();
}
