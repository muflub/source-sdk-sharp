using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// One temp entity table native built (or had built, before a managed reload):
/// its handle, the SendTable to play it through, and where the engine keeps its
/// class id. <see cref="ClassIdAddress"/> is 0 for a table with no ServerClass.
/// </summary>
public readonly record struct TempEntTable(int Handle, bool Existed, uint ShapeHash, nint SendTable, nint ClassIdAddress);

/// <summary>
/// The native half of the server's temp entity tables (plan_server_native_v2
/// T4b): a planned table in, a built one out. One crossing per table, through
/// IShimEffectsServer::TempEntBuild.
/// </summary>
public interface ITempEntTables
{
    /// <summary>
    /// Builds the table one block describes (see <see cref="TempEntBlock"/>), or
    /// answers the one an earlier load built under the same class name; null
    /// when native refuses (the window has shut, or the block is malformed).
    /// </summary>
    TempEntTable? Build(ReadOnlySpan<byte> block);
}

/// <summary><see cref="ITempEntTables"/> over the server's shim slot.</summary>
public sealed unsafe class TempEntTables : ITempEntTables
{
    private readonly IEffectsServerArea _effects;

    internal TempEntTables(IEffectsServerArea effects)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    /// <summary>Over <paramref name="engine"/>'s effects area, taken here once.</summary>
    public static TempEntTables For(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new TempEntTables(engine.Attachment.Areas.Require<IEffectsServerArea>());
    }

    /// <inheritdoc/>
    public TempEntTable? Build(ReadOnlySpan<byte> block)
    {
        if (block.IsEmpty)
            return null;

        TeBuiltTable built = default;
        int handle;

        fixed (byte* p = block)
            handle = _effects.TempEntBuild(p, block.Length, &built);

        if (handle < 0)
            return null;

        return new TempEntTable(handle, built.Existed != 0, built.ShapeHash,
                                (nint)built.SendTable, (nint)built.ClassId);
    }
}

/// <summary>
/// One table's length-prefixed block (BitBlock: an int32 bit count, then the
/// payload): the header, the rows, then the names, NUL-terminated, at the payload
/// offsets the header and the rows carry. Laid out for SourceSharp_TempEntBuild.
/// </summary>
public static class TempEntBlock
{
    /// <summary>The block for one table (<see cref="TeTableBlock"/>, with SendProp rows).</summary>
    public static byte[] Write(string className, string tableName, bool registerClass, uint shapeHash,
                               IReadOnlyList<SendPropRow> rows)
        => TeTableBlock.Write(className, tableName, registerClass, shapeHash, rows, row => row.Name,
            (row, name) => new SendPropRowDesc
            {
                Type = (int)row.Type,
                Offset = row.Offset,
                Flags = row.Flags,
                Bits = row.Bits,
                LowValue = row.Low,
                HighValue = row.High,
                HighLowMul = row.HighLowMul,
                Elements = row.Elements,
                ElementStride = row.ElementStride,
                Proxy = (int)row.Proxy,
                DataTable = row.DataTable,
                Name = name,
            });
}

/// <summary>
/// The send (plan_server_native_v2 T4b): <c>PlaybackTempEntity</c> through the
/// engine forwarder, with the IRecipientFilter libss_fwd_server built over the
/// managed sink pointed at this send's recipients for the length of the call.
/// </summary>
public sealed unsafe class TempEntSendEngine(IFwdEngineServer server, RecipientFilterSink sink, nint nativeFilter)
{
    /// <summary>
    /// The class id the engine gave a table's ServerClass, read where native
    /// pointed (ServerClass::m_ClassID, written by the engine after DLL init);
    /// -1 for a table with no class.
    /// </summary>
    public static int ClassId(in TempEntTable table)
        => table.ClassIdAddress == 0 ? -1 : *(int*)table.ClassIdAddress;

    /// <summary>One temp entity, now.</summary>
    public void Playback(IRecipientList recipients, float delay, ReadOnlySpan<byte> blob, in TempEntTable table)
    {
        int classId = ClassId(table);

        if (classId < 0)
            return;

        using (sink.Use(recipients))
            server.PlaybackTempEntity(nativeFilter, delay, blob, table.SendTable, classId);
    }
}
