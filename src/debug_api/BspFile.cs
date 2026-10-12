using System.Text;

namespace Debug.Api;

/// <summary>One key/value pair as the map file spells it. Order is the map's own.</summary>
public readonly record struct MapKeyValue(string Key, string Value);

/// <summary>
/// One connection from an output to an input on another entity.
///
/// This is what the map's I/O graph is made of, and it is stored as a single
/// comma-joined string in the file -- so parsing it is the difference between a
/// list of entities and a graph of them.
/// </summary>
public readonly record struct MapConnection(
    string Output,
    string Target,
    string Input,
    string Parameter,
    float Delay,
    int TimesToFire);

/// <summary>An entity as the map file declares it, before the game ever runs.</summary>
public sealed record MapEntity(
    int Id,
    string ClassName,
    string TargetName,
    float[] Origin,
    float[] Angles,
    string Model,
    IReadOnlyList<MapKeyValue> KeyValues,
    IReadOnlyList<MapConnection> Connections)
{
    /// <summary>
    /// A brush entity's geometry lives in the BSP as model "*N", not in a file.
    /// The distinction matters for drawing: a point entity is a marker, a brush
    /// entity is a volume, and showing a trigger as a dot loses the only thing
    /// worth knowing about it.
    /// </summary>
    public int BrushModel => Model.StartsWith('*') && int.TryParse(Model[1..], out int n) ? n : -1;
}

/// <summary>A brush model's bounding box, which is what a brush entity is drawn as.</summary>
public readonly record struct BspModel(float[] Mins, float[] Maxs, float[] Origin);

/// <summary>
/// The world's surfaces, flattened for drawing.
///
/// Faces arrive as indices into edges into vertices, which is compact on disk
/// and useless to a renderer. This resolves that chain once, on the server,
/// because doing it in the browser would mean shipping four arrays to rebuild
/// what one would have said.
/// </summary>
public sealed record BspGeometry(
    float[] Vertices,
    int[] FaceStarts,
    int[] FaceLengths,
    int[] FaceIndices,
    float[] Mins,
    float[] Maxs);

/// <summary>
/// Reads what a compiled map declares.
///
/// The entity lump is plain text and the geometry lumps are flat arrays, so
/// this needs nothing from the engine -- which is the point. Reading the file
/// rather than asking the game means the static view works before a map loads,
/// after it unloads, and while the game is busy, and it never costs a frame.
///
/// Only the lumps that answer a question the debugger asks are read. A BSP is
/// mostly lightmaps and visibility data, and none of it is worth the memory
/// here.
/// </summary>
public static class BspFile
{
    private const int LumpEntities = 0;
    private const int LumpVertexes = 3;
    private const int LumpFaces = 7;
    private const int LumpEdges = 12;
    private const int LumpSurfEdges = 13;
    private const int LumpModels = 14;

    private const int HeaderLumps = 64;
    private const int FaceSize = 56;
    private const int ModelSize = 48;

    /// <summary>The escape character older maps join output fields with, instead of a comma.</summary>
    private const char OutputSeparator = '\x1b';

    /// <summary>Everything read out of one map file.</summary>
    public sealed record Contents(
        string Path,
        int Version,
        IReadOnlyList<MapEntity> Entities,
        IReadOnlyList<BspModel> Models,
        BspGeometry? Geometry);

    private readonly record struct Lump(int Offset, int Length);

    /// <summary>
    /// Reads a map.
    ///
    /// Geometry is optional because it is by far the largest part and the graph
    /// and inspector views never look at it -- a caller that only wants entities
    /// should not pay for six thousand faces.
    /// </summary>
    public static Contents Read(string path, bool withGeometry)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        string ident = new(reader.ReadChars(4));

        // BSP is the only thing this understands, and a file that is not one
        // would otherwise be read as offsets into nonsense.
        if (ident != "VBSP")
            throw new InvalidDataException($"not a BSP file (identifier '{ident}')");

        int version = reader.ReadInt32();
        var lumps = new Lump[HeaderLumps];

        for (int i = 0; i < HeaderLumps; i++)
        {
            int offset = reader.ReadInt32();
            int length = reader.ReadInt32();
            reader.ReadInt32();                       // lump version
            reader.ReadInt32();                       // fourCC, only set when compressed
            lumps[i] = new Lump(offset, length);
        }

        var entities = ParseEntities(ReadLumpText(stream, lumps[LumpEntities]));
        var models = ReadModels(stream, lumps[LumpModels]);

        var geometry = withGeometry
            ? ReadGeometry(stream, lumps[LumpVertexes], lumps[LumpEdges], lumps[LumpSurfEdges], lumps[LumpFaces])
            : null;

        return new Contents(path, version, entities, models, geometry);
    }

    //-------------------------------------------------------------------------
    // The entity lump: plain text, one brace-delimited block per entity.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Splits the entity lump into entities and their keys.
    ///
    /// Hand-rolled rather than regex because a value can contain almost
    /// anything -- braces and commas included -- and the only reliable structure
    /// is that keys and values are both quoted, one pair per line.
    /// </summary>
    internal static IReadOnlyList<MapEntity> ParseEntities(string text)
    {
        var entities = new List<MapEntity>();
        var keyValues = new List<MapKeyValue>();
        int id = 0;
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] != '{')
            {
                i++;
                continue;
            }

            i++;
            keyValues.Clear();

            while (i < text.Length && text[i] != '}')
            {
                if (!TryReadQuoted(text, ref i, out string key))
                    break;

                if (!TryReadQuoted(text, ref i, out string value))
                    break;

                keyValues.Add(new MapKeyValue(key, value));
            }

            i++;                                      // past the closing brace

            if (keyValues.Count > 0)
                entities.Add(Build(id++, keyValues));
        }

        return entities;
    }

    /// <summary>
    /// The next quoted string, or false at the end of the block. A closing brace
    /// ends the entity rather than letting the reader run on into the next one.
    /// </summary>
    private static bool TryReadQuoted(string text, ref int i, out string value)
    {
        value = string.Empty;

        while (i < text.Length && text[i] != '"')
        {
            if (text[i] == '}')
                return false;

            i++;
        }

        if (i >= text.Length)
            return false;

        int start = ++i;

        while (i < text.Length && text[i] != '"')
            i++;

        value = text[start..Math.Min(i, text.Length)];
        i++;
        return true;
    }

    private static MapEntity Build(int id, List<MapKeyValue> keyValues)
    {
        string className = "";
        string targetName = "";
        string model = "";
        float[] origin = [0f, 0f, 0f];
        float[] angles = [0f, 0f, 0f];

        var connections = new List<MapConnection>();

        foreach (var kv in keyValues)
        {
            switch (kv.Key.ToLowerInvariant())
            {
                case "classname":  className = kv.Value; break;
                case "targetname": targetName = kv.Value; break;
                case "model":      model = kv.Value; break;
                case "origin":     origin = ParseVector(kv.Value); break;
                case "angles":     angles = ParseVector(kv.Value); break;
            }

            if (TryParseConnection(kv, out var connection))
                connections.Add(connection);
        }

        return new MapEntity(id, className, targetName, origin, angles, model,
            keyValues.ToArray(), connections);
    }

    /// <summary>
    /// Whether a key is an output connection, and what it connects.
    ///
    /// An output is told from an ordinary key by its shape rather than its name:
    /// the value is a five-field tuple of target, input, parameter, delay and a
    /// repeat count. Matching on a leading "On" would miss the outputs that do
    /// not start with it and would claim ordinary keys that do.
    /// </summary>
    internal static bool TryParseConnection(MapKeyValue kv, out MapConnection connection)
    {
        connection = default;

        // Both separators appear in the wild, and a map using the other one
        // would silently show no connections at all.
        char separator = kv.Value.Contains(OutputSeparator) ? OutputSeparator : ',';
        string[] parts = kv.Value.Split(separator);

        if (parts.Length < 5)
            return false;

        // A comma in an ordinary value is common -- a colour, a set of bounds --
        // so the field count alone is not enough. What settles it is the second
        // field: on a connection it is an input name, which is an identifier.
        // "255,255,255,0,1" has five fields too, and drawing an edge for it
        // would put a connection in the graph that does not exist.
        if (!IsIdentifier(parts[1]))
            return false;

        connection = new MapConnection(
            kv.Key,
            parts[0],
            parts[1],
            parts[2],
            float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float delay) ? delay : 0f,
            int.TryParse(parts[4], out int times) ? times : -1);

        return true;
    }

    /// <summary>An input or output name: what the engine will accept as one.</summary>
    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_'))
            return false;

        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    internal static float[] ParseVector(string text)
    {
        var result = new float[3];
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < 3 && i < parts.Length; i++)
        {
            float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result[i]);
        }

        return result;
    }

    //-------------------------------------------------------------------------
    // The binary lumps.
    //-------------------------------------------------------------------------

    private static string ReadLumpText(Stream stream, Lump lump)
    {
        var bytes = ReadLump(stream, lump);

        // The lump is null-terminated, and a trailing null would be read as a
        // stray character by the parser.
        int length = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    private static byte[] ReadLump(Stream stream, Lump lump)
    {
        if (lump.Length <= 0)
            return [];

        stream.Seek(lump.Offset, SeekOrigin.Begin);

        var bytes = new byte[lump.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static IReadOnlyList<BspModel> ReadModels(Stream stream, Lump lump)
    {
        var bytes = ReadLump(stream, lump);
        int count = bytes.Length / ModelSize;
        var models = new BspModel[count];

        for (int i = 0; i < count; i++)
        {
            int at = i * ModelSize;

            models[i] = new BspModel(
                [Float(bytes, at), Float(bytes, at + 4), Float(bytes, at + 8)],
                [Float(bytes, at + 12), Float(bytes, at + 16), Float(bytes, at + 20)],
                [Float(bytes, at + 24), Float(bytes, at + 28), Float(bytes, at + 32)]);
        }

        return models;
    }

    /// <summary>
    /// Resolves faces down to vertex positions.
    ///
    /// A face indexes surfedges, which index edges, which index vertices, and a
    /// negative surfedge means the edge is walked backwards. Getting that
    /// winding wrong is the classic BSP mistake: the geometry still draws, but
    /// every other polygon comes out inside for out.
    /// </summary>
    private static BspGeometry ReadGeometry(Stream stream, Lump vertexLump, Lump edgeLump,
                                            Lump surfEdgeLump, Lump faceLump)
    {
        var vertexBytes = ReadLump(stream, vertexLump);
        var edgeBytes = ReadLump(stream, edgeLump);
        var surfEdgeBytes = ReadLump(stream, surfEdgeLump);
        var faceBytes = ReadLump(stream, faceLump);

        int vertexCount = vertexBytes.Length / 12;
        var vertices = new float[vertexCount * 3];
        var mins = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        var maxs = new[] { float.MinValue, float.MinValue, float.MinValue };

        for (int i = 0; i < vertexCount; i++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                float value = Float(vertexBytes, i * 12 + axis * 4);
                vertices[i * 3 + axis] = value;

                if (value < mins[axis]) mins[axis] = value;
                if (value > maxs[axis]) maxs[axis] = value;
            }
        }

        int faceCount = faceBytes.Length / FaceSize;
        var starts = new int[faceCount];
        var lengths = new int[faceCount];
        var indices = new List<int>(faceCount * 6);

        for (int i = 0; i < faceCount; i++)
        {
            int at = i * FaceSize;
            int firstEdge = BitConverter.ToInt32(faceBytes, at + 4);
            int edgeCount = BitConverter.ToInt16(faceBytes, at + 8);

            starts[i] = indices.Count;
            lengths[i] = edgeCount;

            for (int e = 0; e < edgeCount; e++)
            {
                int surfEdge = BitConverter.ToInt32(surfEdgeBytes, (firstEdge + e) * 4);

                // A negative surfedge is the same edge walked the other way, so
                // its second vertex is the one that comes first.
                int edge = Math.Abs(surfEdge);
                int offset = surfEdge >= 0 ? 0 : 2;

                indices.Add(BitConverter.ToUInt16(edgeBytes, edge * 4 + offset));
            }
        }

        if (vertexCount == 0)
        {
            mins = [0f, 0f, 0f];
            maxs = [0f, 0f, 0f];
        }

        return new BspGeometry(vertices, starts, lengths, indices.ToArray(), mins, maxs);
    }

    private static float Float(byte[] bytes, int at) => BitConverter.ToSingle(bytes, at);
}
