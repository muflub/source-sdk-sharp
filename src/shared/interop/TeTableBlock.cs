using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One planned temp entity table as the length-prefixed block native stores it
/// from (BitBlock: an int32 bit count, then the payload): a
/// <see cref="TeTableHeader"/>, one row per prop, then the names, NUL-terminated,
/// at the payload offsets the header and the rows carry (the class name, the
/// table name, then each row's in row order). Both sides cross in this shape;
/// only the row differs: the server's <see cref="SendPropRowDesc"/>, the
/// client's <see cref="RecvPropRowDesc"/>.
/// </summary>
public static class TeTableBlock
{
    /// <summary>
    /// The block for one table. <paramref name="describe"/> turns a row into its
    /// blittable mirror, given the payload offset its name was written at.
    /// </summary>
    public static byte[] Write<TRow, TDesc>(string className, string tableName, bool registerClass, uint shapeHash,
                                            IReadOnlyList<TRow> rows, Func<TRow, string> nameOf,
                                            Func<TRow, int, TDesc> describe)
        where TDesc : unmanaged
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(describe);

        int headerBytes = Unsafe.SizeOf<TeTableHeader>();
        int rowBytes = Unsafe.SizeOf<TDesc>();
        var names = new List<byte>();

        int Name(string text)
        {
            int at = headerBytes + rows.Count * rowBytes + names.Count;
            names.AddRange(System.Text.Encoding.UTF8.GetBytes(text));
            names.Add(0);
            return at;
        }

        var header = new TeTableHeader
        {
            Props = rows.Count,
            RegisterClass = registerClass ? 1 : 0,
            ClassName = Name(className),
            TableName = Name(tableName),
            ShapeHash = shapeHash,
        };

        var descs = new TDesc[rows.Count];

        for (int i = 0; i < rows.Count; i++)
            descs[i] = describe(rows[i], Name(nameOf(rows[i])));

        int payload = headerBytes + rows.Count * rowBytes + names.Count;
        var block = new byte[BitBlock.HeaderBytes + payload];
        Span<byte> body = BitBlock.Payload(block.AsSpan());

        MemoryMarshal.Write(body, in header);
        MemoryMarshal.AsBytes(descs.AsSpan()).CopyTo(body[headerBytes..]);
        names.ToArray().CopyTo(body[(headerBytes + rows.Count * rowBytes)..]);
        BitBlock.SetBits(block, payload * 8);

        return block;
    }
}
