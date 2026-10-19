//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// Which standard recv proxy a prop carries. Native turns the id into the
/// function (the engine's local backdoor compares a prop's proxy with
/// g_StandardRecvProxies, so it must be the real one). The values are
/// SOURCESHARP_RECVPROXY_* in sourcesharp.h.
/// </summary>
public enum RecvProxyId
{
    /// <summary>A DPT_Array: InternalRecvPropArray's, with no length proxy.</summary>
    None            = 0,
    Int32ToInt8     = 1,
    Int32ToInt16    = 2,
    Int32ToInt32    = 3,
    FloatToFloat    = 4,
    VectorToVector  = 5,

    /// <summary>A DPT_DataTable's table proxy, DataTableRecvProxy_StaticDataTable.</summary>
    StaticDataTable = 6,
}

/// <summary>
/// One RecvProp, decided: every field the engine reads from it, as dt_recv.h's
/// RecvPropInt / RecvPropFloat / RecvPropVector / RecvPropDataTable /
/// InternalRecvPropArray would have left it. Native stores these and decides
/// nothing.
/// </summary>
/// <param name="Name">The RECV name, which the client pairs with the server's prop by.</param>
/// <param name="Type">DPT_* (m_RecvType).</param>
/// <param name="Offset">Into the blob, always positive: the recv side never negates.</param>
/// <param name="Flags">SPROP_*.</param>
/// <param name="Elements">m_nElements (1 unless an array).</param>
/// <param name="ElementStride">m_ElementStride (-1 unless an array).</param>
/// <param name="Proxy">The var proxy, or the datatable proxy for a DPT_DataTable.</param>
/// <param name="DataTable">The nested table's build handle, or -1.</param>
public readonly record struct RecvPropRow(
    string       Name,
    SendPropType Type,
    int          Offset,
    int          Flags,
    int          Elements,
    int          ElementStride,
    RecvProxyId  Proxy,
    int          DataTable);

/// <summary>
/// The client's half of a temp entity table's props: one planned prop
/// (<see cref="TePropPlanner"/>, the plan both sides build from) as the RecvProp
/// shell_tempents_client.cpp's BuildOneProp constructed, in C#.
///
/// <para>
/// THE RECV NAME, NOT THE PLAN'S: an array's element template is
/// <c>m_vecPoints[0]</c> here where the server says <c>m_vecPoints</c>; two recv
/// props of one name make CreateDecoders resolve the DPT_Array to the template
/// and refuse the join. THE OFFSET, NOT THE DECLARE OFFSET: RECVINFO is a plain
/// offsetof, so a client declaring with a vector elem's negated offset would
/// decode in front of the blob. The angle kinds collapse into float and vector,
/// because SendProxy_AngleToFloat runs on the way out and RecvPropQAngles is
/// RecvPropVector (dt_recv.h).
/// </para>
/// </summary>
public static class RecvPropPlan
{
    /// <summary>
    /// The row one planned prop becomes; null for a prop that cannot be one: an
    /// int whose storage no write proxy narrows to, a nested table with no
    /// built table under it, or a kind this does not know.
    /// </summary>
    /// <param name="plan">The planned prop.</param>
    /// <param name="nested">For a DataTable prop, the build handle of the table it nests.</param>
    public static RecvPropRow? FromPlan(in TePropPlan plan, int nested = -1)
    {
        string name = TePropPlanner.RecvPropName(plan);
        int flags = (int)plan.Flags;

        switch (plan.Kind)
        {
            case TePropKind.Int:
                // The storage width picks the narrowing write (RecvPropInt):
                // a byte field described as four writes three bytes of the next.
                RecvProxyId? proxy = plan.SizeBytes switch
                {
                    1 => RecvProxyId.Int32ToInt8,
                    2 => RecvProxyId.Int32ToInt16,
                    4 => RecvProxyId.Int32ToInt32,
                    _ => null,
                };

                return proxy is { } id ? Scalar(name, SendPropType.Int, plan.Offset, flags, id) : null;

            case TePropKind.Float:
            case TePropKind.Angle:
                return Scalar(name, SendPropType.Float, plan.Offset, flags, RecvProxyId.FloatToFloat);

            case TePropKind.Vector:
            case TePropKind.QAngles:
                return Scalar(name, SendPropType.Vector, plan.Offset, flags, RecvProxyId.VectorToVector);

            case TePropKind.DataTable:
                return nested < 0
                    ? null
                    : new RecvPropRow(name, SendPropType.DataTable, plan.Offset, flags, 1, -1,
                                      RecvProxyId.StaticDataTable, nested);

            // The DPT_Array half of the pair. InternalRecvPropArray takes no
            // offset and no flags, and the engine links it to the preceding prop.
            case TePropKind.Array:
                return plan.Elems > 0 && plan.ElemStride > 0
                    ? new RecvPropRow(name, SendPropType.Array, 0, 0, plan.Elems, plan.ElemStride,
                                      RecvProxyId.None, -1)
                    : null;
        }

        return null;
    }

    private static RecvPropRow Scalar(string name, SendPropType type, int offset, int flags, RecvProxyId proxy)
        => new(name, type, offset, flags, 1, -1, proxy, -1);
}
