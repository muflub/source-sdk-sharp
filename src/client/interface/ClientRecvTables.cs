using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>One prop of one recv table, as the engine holds it.</summary>
/// <param name="ClassName">The ClientClass whose root table this is; empty for a nested table.</param>
/// <param name="Type">The engine's SendPropType number.</param>
public readonly record struct ClientRecvPropRow(
    string ClassName, string Table, string Prop, int Type, int Flags, int Offset, int Elements);

/// <summary>One type whose schema hash this client refused (the payload decoder's send/recv agreement, the client's schema gate).</summary>
public readonly record struct ClientSchemaRefusalRow(uint TypeId, uint Expected, uint Seen, int Count, string ClassName);

/// <summary>
/// The client's recv tables as the engine walks them (plan_client_novalve
/// §4.4, the nettables route). Game thread.
/// </summary>
public interface IClientRecvTables
{
    /// <summary>Every prop of every reachable recv table, each table once.</summary>
    IReadOnlyList<ClientRecvPropRow> Props();
}
