using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// One temp entity table native built on the client (or had built, before a
/// managed reload): its handle, which is also the handle an arriving event of
/// the class is played under, and the shape it was built with.
/// </summary>
public readonly record struct TempEntRecvTable(int Handle, bool Existed, uint ShapeHash);

/// <summary>
/// The native half of the client's temp entity tables: a planned table in, a
/// built RecvTable (and, if asked, its ClientClass and receiving shell) out.
/// One crossing per table, through IShimTempEntBackendsClient::TempEntBuildClient.
/// </summary>
public interface ITempEntRecvTables
{
    /// <summary>
    /// Builds the table one block describes (see <see cref="TempEntRecvBlock"/>),
    /// with a receiving shell over a blob of <c>blobBytes</c> when it registers a class,
    /// or answers the one an earlier load built under the same class name; null
    /// when native refuses (the window has shut, the table limit is reached, or
    /// the block is malformed).
    /// </summary>
    TempEntRecvTable? Build(ReadOnlySpan<byte> block, int blobBytes);
}

/// <summary><see cref="ITempEntRecvTables"/> over the client's shim slot.</summary>
public sealed unsafe class ClientTempEntTables : ITempEntRecvTables
{
    private readonly EngineSide _side;

    public ClientTempEntTables(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _side = engine.Api;
    }

    /// <inheritdoc/>
    public TempEntRecvTable? Build(ReadOnlySpan<byte> block, int blobBytes)
    {
        if (!_side.IsInitialized || !_side.ClientAttached || block.IsEmpty)
            return null;

        TeRecvBuilt built = default;
        int handle;

        fixed (byte* p = block)
            handle = _side.ClientShims.TempEntBackendsClient.TempEntBuildClient(p, block.Length, blobBytes, &built);

        return handle < 0 ? null : new TempEntRecvTable(handle, built.Existed != 0, built.ShapeHash);
    }
}

/// <summary>
/// One client table's length-prefixed block: <see cref="TeTableBlock"/> with
/// <see cref="RecvPropRowDesc"/> rows. Laid out for SourceSharp_TempEntBuildClient.
/// </summary>
public static class TempEntRecvBlock
{
    /// <summary>The block for one table.</summary>
    public static byte[] Write(string className, string tableName, bool registerClass, uint shapeHash,
                               IReadOnlyList<RecvPropRow> rows)
        => TeTableBlock.Write(className, tableName, registerClass, shapeHash, rows, row => row.Name,
            (row, name) => new RecvPropRowDesc
            {
                Type = (int)row.Type,
                Offset = row.Offset,
                Flags = row.Flags,
                Elements = row.Elements,
                ElementStride = row.ElementStride,
                Proxy = (int)row.Proxy,
                DataTable = row.DataTable,
                Name = name,
            });
}
