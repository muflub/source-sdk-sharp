namespace Debug.Api;

/// <summary>
/// One world surface, as the compiler cut it.
///
/// The lightmap columns are the reason this is worth reading. A map's lighting
/// is usually most of its file, and it is spent per face at a resolution the
/// mapper chose per surface -- so "which faces are eating the lightmap" is both
/// the commonest question about a large map and one nothing else here can
/// answer.
/// </summary>
public readonly record struct BspFace(
    int Index, int Plane, bool Side, bool OnNode, int FirstEdge, int Edges,
    int TexInfo, string Material, string Surface, int DispInfo, float Area,
    int LightmapWidth, int LightmapHeight, long Luxels, int Styles,
    int OrigFace, int Primitives);

/// <summary>
/// How a material is applied to the surfaces that use it.
///
/// The flags are the useful part: nodraw, hint, skip and trigger are all
/// invisible in the game and all four change what the compiler did with the
/// brush they came from.
/// </summary>
public readonly record struct BspTexInfoRow(
    int Index, int TexData, string Material, string Surface, float LightmapScale);

/// <summary>A leaf of the BSP tree: one convex region of space the player can be in.</summary>
public readonly record struct BspLeaf(
    int Index, int Cluster, int Area, string Contents,
    int FirstFace, int Faces, int FirstBrush, int Brushes, int Water,
    int[] Mins, int[] Maxs);

/// <summary>A brush, as it survived compilation, and what it is made of.</summary>
public readonly record struct BspBrush(int Index, int FirstSide, int Sides, string Contents);

/// <summary>One face of a brush, with the material on it.</summary>
public readonly record struct BspBrushSide(
    int Index, int Plane, int TexInfo, string Material, int DispInfo, bool Bevel);

/// <summary>
/// The lumps that are read one at a time.
///
/// Everything in <see cref="BspReport"/> is read together because it is small
/// enough to be: a map has a few hundred props and a few hundred materials. The
/// lumps here are the other kind. A medium map has six thousand faces, sixteen
/// thousand brush sides and three thousand leaves, and sending all of them with
/// every read would mean paying several megabytes to look at the pakfile.
///
/// So they are read on request, one lump at a time, and only when somebody opens
/// one. The alternative -- capping them -- would make the export lie, and an
/// export that is quietly incomplete is worse than one that takes a moment.
/// </summary>
public sealed record BspLumpDetail
{
    private const int TexInfoSize = 72;
    private const int BrushSize = 12;
    private const int BrushSideSize = 8;

    public required string Map { get; init; }
    public required int Lump { get; init; }
    public required string Name { get; init; }

    /// <summary>Whether this lump is one of the ones read this way at all.</summary>
    public required bool Known { get; init; }

    public IReadOnlyList<BspFace> Faces { get; init; } = [];
    public IReadOnlyList<BspTexInfoRow> TexInfos { get; init; } = [];
    public IReadOnlyList<BspLeaf> Leaves { get; init; } = [];
    public IReadOnlyList<BspBrush> Brushes { get; init; } = [];
    public IReadOnlyList<BspBrushSide> BrushSides { get; init; } = [];

    /// <summary>The lumps this can decode. Anything else is answered as unknown rather than empty.</summary>
    public static bool CanRead(int lump) => lump is 6 or 7 or 10 or 18 or 19 or 27 or 58;

    /// <summary>
    /// Reads one lump.
    ///
    /// The material table is built for every one of them, because four of the
    /// five decoders here resolve a material and building it costs one read of
    /// four small lumps.
    /// </summary>
    public static BspLumpDetail Read(string path, string map, int lump)
    {
        if (lump < 0 || lump >= 64)
            throw new ArgumentOutOfRangeException(nameof(lump), "a lump is numbered 0 to 63");

        using var stream = File.OpenRead(path);
        var header = BspHeader.Read(stream);

        var detail = new BspLumpDetail
        {
            Map = map,
            Lump = lump,
            Name = BspReport.LumpNames[lump],
            Known = CanRead(lump),
        };

        if (!detail.Known)
            return detail;

        var materials = new BspMaterials(
            header.Bytes(stream, 43), header.Bytes(stream, 44),
            header.Bytes(stream, 6), header.Bytes(stream, 2));

        var bytes = header.Bytes(stream, lump);

        return lump switch
        {
            // Three lumps are arrays of the same face record: the faces the
            // engine draws, the ones they were split from, and the separate set
            // an HDR compile can produce.
            7 or 27 or 58 => detail with { Faces = ReadFaces(bytes, materials) },

            6 => detail with { TexInfos = ReadTexInfos(bytes, materials) },
            10 => detail with { Leaves = ReadLeaves(bytes, header.Version(10)) },
            18 => detail with { Brushes = ReadBrushes(bytes) },
            19 => detail with { BrushSides = ReadBrushSides(bytes, materials) },
            _ => detail,
        };
    }

    private static IReadOnlyList<BspFace> ReadFaces(byte[] bytes, BspMaterials materials)
    {
        int size = BspReport.FaceSize;
        var result = new BspFace[bytes.Length / size];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * size;
            short info = BitConverter.ToInt16(bytes, at + 10);

            int styles = 0;

            for (int style = 0; style < 4; style++)
            {
                if (bytes[at + 16 + style] != 255)
                    styles++;
            }

            result[i] = new BspFace(
                i,
                BitConverter.ToUInt16(bytes, at),
                bytes[at + 2] != 0,
                bytes[at + 3] != 0,
                BitConverter.ToInt32(bytes, at + 4),
                BitConverter.ToInt16(bytes, at + 8),
                info,
                materials.NameOf(info),
                BspFlags.DescribeSurface(materials.FlagsOf(info)),
                BitConverter.ToInt16(bytes, at + 12),
                BitConverter.ToSingle(bytes, at + 24),

                // Stored one less than the number of samples on each axis: a flat
                // face still has a luxel at both ends of it.
                BitConverter.ToInt32(bytes, at + 36) + 1,
                BitConverter.ToInt32(bytes, at + 40) + 1,
                BspReport.LuxelsOf(bytes, at, materials.IsBumped(info)),
                styles,
                BitConverter.ToInt32(bytes, at + 44),

                // The top bit of the primitive count is a flag about shadows
                // rather than a count, and leaving it in reports thirty-two
                // thousand primitives on an ordinary face.
                BitConverter.ToUInt16(bytes, at + 48) & 0x7fff);
        }

        return result;
    }

    private static IReadOnlyList<BspTexInfoRow> ReadTexInfos(byte[] bytes, BspMaterials materials)
    {
        var result = new BspTexInfoRow[bytes.Length / TexInfoSize];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new BspTexInfoRow(
                i,
                materials.TexDataOf(i),
                materials.NameOf(i),
                BspFlags.DescribeSurface(materials.FlagsOf(i)),
                materials.LightmapScaleOf(i));
        }

        return result;
    }

    /// <summary>
    /// The leaves.
    ///
    /// Version 1 of the lump dropped the ambient light cube out of the record and
    /// into a lump of its own, taking twenty-four bytes with it. Everything read
    /// here is in the part that did not move, so the two versions differ only in
    /// how far apart the records are.
    /// </summary>
    private static IReadOnlyList<BspLeaf> ReadLeaves(byte[] bytes, int version)
    {
        int size = BspReport.StrideOf(10, version);
        var result = new BspLeaf[bytes.Length / size];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * size;

            // The area and the leaf's own flags share one short: nine bits of
            // area, seven of flags. Reading the whole short as the area gives a
            // number in the thousands for a map with four areas in it.
            int packed = BitConverter.ToUInt16(bytes, at + 6);

            result[i] = new BspLeaf(
                i,
                BitConverter.ToInt16(bytes, at + 4),
                packed & 0x1ff,
                BspFlags.DescribeContents(BitConverter.ToInt32(bytes, at)),
                BitConverter.ToUInt16(bytes, at + 20),
                BitConverter.ToUInt16(bytes, at + 22),
                BitConverter.ToUInt16(bytes, at + 24),
                BitConverter.ToUInt16(bytes, at + 26),
                BitConverter.ToInt16(bytes, at + 28),
                [BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10),
                 BitConverter.ToInt16(bytes, at + 12)],
                [BitConverter.ToInt16(bytes, at + 14), BitConverter.ToInt16(bytes, at + 16),
                 BitConverter.ToInt16(bytes, at + 18)]);
        }

        return result;
    }

    private static IReadOnlyList<BspBrush> ReadBrushes(byte[] bytes)
    {
        var result = new BspBrush[bytes.Length / BrushSize];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * BrushSize;

            result[i] = new BspBrush(
                i,
                BitConverter.ToInt32(bytes, at),
                BitConverter.ToInt32(bytes, at + 4),
                BspFlags.DescribeContents(BitConverter.ToInt32(bytes, at + 8)));
        }

        return result;
    }

    private static IReadOnlyList<BspBrushSide> ReadBrushSides(byte[] bytes, BspMaterials materials)
    {
        var result = new BspBrushSide[bytes.Length / BrushSideSize];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * BrushSideSize;
            short info = BitConverter.ToInt16(bytes, at + 2);

            result[i] = new BspBrushSide(
                i,
                BitConverter.ToUInt16(bytes, at),
                info,
                materials.NameOf(info),
                BitConverter.ToInt16(bytes, at + 4),
                BitConverter.ToInt16(bytes, at + 6) != 0);
        }

        return result;
    }
}

/// <summary>
/// A map's header, kept open so lumps can be pulled out of it one at a time.
///
/// The whole-file reader walks every lump in one pass and has no use for this;
/// what needs it is the reader that is asked for one lump and has to find it.
/// </summary>
internal sealed class BspHeader
{
    private const int HeaderLumps = 64;

    private readonly (int Offset, int Length, int Version)[] _lumps =
        new (int, int, int)[HeaderLumps];

    private readonly long _size;

    private BspHeader(long size) => _size = size;

    public static BspHeader Read(Stream stream)
    {
        var header = new BspHeader(stream.Length);
        var reader = new BinaryReader(stream);

        stream.Seek(0, SeekOrigin.Begin);

        if (new string(reader.ReadChars(4)) != "VBSP")
            throw new InvalidDataException("not a BSP file");

        reader.ReadInt32();

        for (int i = 0; i < HeaderLumps; i++)
        {
            int offset = reader.ReadInt32();
            int length = reader.ReadInt32();
            int version = reader.ReadInt32();
            reader.ReadInt32();

            header._lumps[i] = (offset, length, version);
        }

        return header;
    }

    public int Version(int lump) => _lumps[lump].Version;

    /// <summary>One lump's bytes, or none at all if the header points off the end of the file.</summary>
    public byte[] Bytes(Stream stream, int lump)
    {
        var (offset, length, _) = _lumps[lump];

        if (offset < 0 || length <= 0 || offset + (long)length > _size)
            return [];

        stream.Seek(offset, SeekOrigin.Begin);

        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
