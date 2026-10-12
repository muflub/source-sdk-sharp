using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;

namespace Debug.Api;

/// <summary>One lump as the header describes it, and what is known about its shape.</summary>
/// <param name="Stride">Bytes per record, or 0 for a lump that is not a array of records.</param>
/// <param name="Records">How many records that makes, or -1 when the lump has no fixed shape.</param>
public readonly record struct BspLump(
    int Index,
    string Name,
    int Offset,
    int Length,
    int Version,
    int UncompressedSize,
    int Stride,
    int Records,
    string Note);

/// <summary>A sub-lump of the game lump: content a game puts in a map that the engine does not define.</summary>
public readonly record struct BspGameLump(string Id, int Flags, int Version, int Offset, int Length);

/// <summary>
/// A material the map uses, with what the map is built out of it.
///
/// The last three are counted by walking the faces rather than read: they are
/// what turns a list of names into an answer to "what is this map made of, and
/// what is it costing" -- surface area is how much of the world you are looking
/// at, and luxels are how much of the lightmap it took to light it.
/// </summary>
public readonly record struct BspTexData(
    int Index, string Name, float[] Reflectivity,
    int Width, int Height, int ViewWidth, int ViewHeight,
    int Faces, float Area, long Luxels);

/// <summary>A visibility area, and the portals that join it to the others.</summary>
public readonly record struct BspArea(int Index, int FirstPortal, int Portals);

/// <summary>One areaportal, which is the thing that shuts an area off from view.</summary>
public readonly record struct BspAreaPortal(
    int Index, int Key, int OtherArea, int Plane, int FirstVertex, int Vertices);

/// <summary>A body of water, as the leaves under it refer to it.</summary>
public readonly record struct BspWaterData(
    int Index, float SurfaceZ, float MinZ, int TexInfo, string Material);

/// <summary>A detail prop model: grass, rubble, the things scattered by a material.</summary>
public readonly record struct BspDetailModel(int Index, string Name);

/// <summary>
/// One static prop.
///
/// Props are not entities and do not appear in the entity lump at all, which is
/// why a map full of them reads as empty to everything else in this tool.
/// </summary>
public readonly record struct BspStaticProp(
    int Index, string Model, float[] Origin, float[] Angles,
    int Skin, float FadeMin, float FadeMax, int Solid, int Flags,
    int FirstLeaf, int LeafCount);

/// <summary>A cubemap sample. The position is what names the .vtf that vrad bakes for it.</summary>
public readonly record struct BspCubemap(int[] Origin, int Size);

/// <summary>
/// A light as vrad recorded it.
///
/// Note this is not what lights the world: surface lighting is baked into the
/// lighting lump. This is what the engine reads at runtime, for model and prop
/// lighting.
/// </summary>
public readonly record struct BspWorldLight(
    int Index, float[] Origin, float[] Intensity, float[] Normal,
    string Type, int Style, float Radius, int Owner, bool Hdr);

/// <summary>A decal projected onto world surfaces -- a sign, a stain, a scorch.</summary>
public readonly record struct BspOverlay(
    int Id, int TexInfo, string Material, float[] Origin, int FaceCount);

/// <summary>A displacement surface: the sculpted terrain a face was turned into.</summary>
public readonly record struct BspDisplacement(
    int Index, float[] StartPosition, int Power, int MapFace, int Contents, int Vertices);

/// <summary>One file inside the map's embedded zip.</summary>
public readonly record struct BspPakEntry(string Name, long Bytes, long Compressed);

/// <summary>How many entities of one classname the map declares.</summary>
public readonly record struct BspClassCount(string ClassName, int Count);

/// <summary>
/// Everything a compiled map holds, read out for looking at.
///
/// This is the whole file rather than the part the entity debugger needs. Where
/// <see cref="BspFile"/> reads the six lumps that answer "what does this map
/// declare and what shape is it", this reads the header, every lump's place in
/// the file, and the contents of each lump whose format is known -- so the
/// question "what is actually in this map" has an answer that does not involve
/// a hex editor.
///
/// Nothing here decodes the lumps that exist only to be fed back to the engine:
/// visibility, lightmaps, physics collision and the BSP tree itself are megabytes
/// of data with nothing a person would read in them. Those are reported as a size
/// and a record count, which is the useful half -- "the lightmaps are eight
/// megabytes of this eleven megabyte file" is a thing worth knowing, and the
/// luxels themselves are not.
///
/// Every reader here is bounds-checked against the lump it was given rather than
/// trusting the header. A map is a build output and a truncated one is a normal
/// thing to find on disk; a debugger that throws while being asked what is wrong
/// with a file is no use at the moment it is most needed.
/// </summary>
public sealed class BspReport
{
    public required string Path { get; init; }
    public required string Map { get; init; }
    public required long Bytes { get; init; }

    /// <summary>"VBSP" for anything this can read. Reported because a file that is not one says so here.</summary>
    public required string Ident { get; init; }

    public required int Version { get; init; }

    /// <summary>Hammer's iteration count for the map. Zero on anything compiled before BSPVERSION 6.</summary>
    public required int Revision { get; init; }

    /// <summary>How much of the file no lump claims: header padding, alignment, and appended lumps' slack.</summary>
    public required long Unaccounted { get; init; }

    public required IReadOnlyList<BspLump> Lumps { get; init; }
    public required IReadOnlyList<BspGameLump> GameLumps { get; init; }
    public required IReadOnlyList<BspClassCount> Classes { get; init; }
    public required IReadOnlyList<BspTexData> TexDatas { get; init; }
    public required IReadOnlyList<string> Materials { get; init; }
    public required IReadOnlyList<BspStaticProp> Props { get; init; }
    public required IReadOnlyList<string> PropModels { get; init; }
    public required IReadOnlyList<BspCubemap> Cubemaps { get; init; }
    public required IReadOnlyList<BspWorldLight> WorldLights { get; init; }
    public required IReadOnlyList<BspOverlay> Overlays { get; init; }
    public required IReadOnlyList<BspDisplacement> Displacements { get; init; }
    public required IReadOnlyList<BspPakEntry> Pak { get; init; }
    public required IReadOnlyList<BspModel> Models { get; init; }
    public required IReadOnlyList<BspArea> Areas { get; init; }
    public required IReadOnlyList<BspAreaPortal> AreaPortals { get; init; }
    public required IReadOnlyList<BspWaterData> Water { get; init; }
    public required IReadOnlyList<BspDetailModel> DetailModels { get; init; }

    /// <summary>How many occluders vrad wrote, which is nearly always none.</summary>
    public required int Occluders { get; init; }

    /// <summary>What the level was compiled with, already spelled out.</summary>
    public required string LevelFlags { get; init; }

    /// <summary>
    /// How many clusters an average cluster can see, and the worst one.
    ///
    /// The number that decides whether a map runs: everything visible from where
    /// you stand is drawn, so this is the size of a frame's work in the one unit
    /// the compiler thinks in. -1 when the map has no visibility to measure.
    /// </summary>
    public required int VisAverage { get; init; }
    public required int VisMax { get; init; }

    /// <summary>Entities, as the entity lump declares them. The same list the debugger reads.</summary>
    public required IReadOnlyList<MapEntity> Entities { get; init; }

    /// <summary>Visibility clusters, or -1 when the map was compiled without vis.</summary>
    public required int VisClusters { get; init; }

    /// <summary>Anything that could not be decoded, said out loud rather than dropped.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    //-------------------------------------------------------------------------
    // The header.
    //-------------------------------------------------------------------------

    private const int HeaderLumps = 64;
    private const int HeaderSize = 8 + HeaderLumps * 16 + 4;

    /// <summary>One dface_t. Named because three lumps are arrays of it.</summary>
    internal const int FaceSize = 56;

    /// <summary>
    /// What each lump is called, by index.
    ///
    /// Straight from the engine's own enum. The numbers are the format and the
    /// names are how anybody talks about it, so showing "lump 43" without
    /// "TEXDATA_STRING_DATA" beside it would make the table something you have to
    /// look things up against rather than read.
    /// </summary>
    internal static readonly ImmutableArray<string> LumpNames =
    [
        "ENTITIES", "PLANES", "TEXDATA", "VERTEXES", "VISIBILITY", "NODES",
        "TEXINFO", "FACES", "LIGHTING", "OCCLUSION", "LEAFS", "FACEIDS",
        "EDGES", "SURFEDGES", "MODELS", "WORLDLIGHTS", "LEAFFACES",
        "LEAFBRUSHES", "BRUSHES", "BRUSHSIDES", "AREAS", "AREAPORTALS",
        "UNUSED0", "UNUSED1", "UNUSED2", "UNUSED3", "DISPINFO",
        "ORIGINALFACES", "PHYSDISP", "PHYSCOLLIDE", "VERTNORMALS",
        "VERTNORMALINDICES", "DISP_LIGHTMAP_ALPHAS", "DISP_VERTS",
        "DISP_LIGHTMAP_SAMPLE_POSITIONS", "GAME_LUMP", "LEAFWATERDATA",
        "PRIMITIVES", "PRIMVERTS", "PRIMINDICES", "PAKFILE", "CLIPPORTALVERTS",
        "CUBEMAPS", "TEXDATA_STRING_DATA", "TEXDATA_STRING_TABLE", "OVERLAYS",
        "LEAFMINDISTTOWATER", "FACE_MACRO_TEXTURE_INFO", "DISP_TRIS",
        "PHYSCOLLIDESURFACE", "WATEROVERLAYS", "LEAF_AMBIENT_INDEX_HDR",
        "LEAF_AMBIENT_INDEX", "LIGHTING_HDR", "WORLDLIGHTS_HDR",
        "LEAF_AMBIENT_LIGHTING_HDR", "LEAF_AMBIENT_LIGHTING", "XZIPPAKFILE",
        "FACES_HDR", "MAP_FLAGS", "OVERLAY_FADES", "UNUSED4", "UNUSED5",
        "UNUSED6",
    ];

    /// <summary>
    /// Bytes per record, for the lumps that are arrays of one.
    ///
    /// This is what turns a size into a count, and a count is what a person
    /// actually wants: "6,142 faces" says something about a map, "343,952 bytes"
    /// says nothing. Zero means the lump has no fixed record -- either it is text,
    /// a stream with its own header, or opaque data the engine hands to something
    /// else -- and those report a size only.
    ///
    /// The sizes come from the structures in public/bspfile.h, laid out as the
    /// compiler packs them. They are checked against the lumps of a real map by
    /// <c>BspReportTests</c>, because a stride that is wrong by four bytes
    /// produces a plausible count rather than an obvious failure.
    /// </summary>
    internal static int StrideOf(int lump, int version) => lump switch
    {
        1 => 20,                                       // dplane_t
        2 => 32,                                       // dtexdata_t
        3 => 12,                                       // Vector
        5 => 32,                                       // dnode_t
        6 => 72,                                       // texinfo_t
        7 => 56,                                       // dface_t
        8 => 4,                                        // ColorRGBExp32, one per luxel

        // The ambient light cube left dleaf_t in version 1 and moved into a lump
        // of its own, which took 24 bytes with it.
        10 => version >= 1 ? 32 : 56,                  // dleaf_t

        11 => 2,                                       // dfaceid_t
        12 => 4,                                       // dedge_t
        13 => 4,                                       // surfedge
        14 => 48,                                      // dmodel_t
        15 => 88,                                      // dworldlight_t
        16 => 2,                                       // leafface
        17 => 2,                                       // leafbrush
        18 => 12,                                      // dbrush_t
        19 => 8,                                       // dbrushside_t
        20 => 8,                                       // darea_t
        21 => 12,                                      // dareaportal_t
        26 => 176,                                     // ddispinfo_t
        27 => 56,                                      // dface_t, pre-splitting
        30 => 12,                                      // Vector
        31 => 2,                                       // vertnormalindex
        32 => 1,                                       // displacement lightmap alpha
        33 => 20,                                      // CDispVert
        34 => 1,                                       // lightmap sample position
        36 => 10,                                      // dleafwaterdata_t
        37 => 10,                                      // dprimitive_t
        38 => 12,                                      // Vector
        39 => 2,                                       // primindex
        41 => 12,                                      // Vector
        42 => 16,                                      // dcubemapsample_t
        44 => 4,                                       // offset into the string data
        45 => 352,                                     // doverlay_t
        46 => 2,                                       // leafmindisttowater
        47 => 2,                                       // macro texture info
        48 => 2,                                       // CDispTri
        51 or 52 => 4,                                 // dleafambientindex_t
        53 => 4,                                       // ColorRGBExp32, one per luxel
        54 => 88,                                      // dworldlight_t
        55 or 56 => 28,                                // dleafambientlighting_t
        58 => 56,                                      // dface_t
        59 => 4,                                       // dflagslump_t
        60 => 8,                                       // doverlayfade_t
        _ => 0,
    };

    /// <summary>
    /// What a lump is for, where knowing changes what you do with it.
    ///
    /// Only written for the lumps that surprise people: the ones that are most of
    /// the file, the ones that are safe to edit, and the ones whose emptiness
    /// means the map was compiled wrong.
    /// </summary>
    internal static string NoteFor(int lump, int length) => lump switch
    {
        0 => "text; every entity the map declares. The one lump this tool writes.",
        4 when length == 0 => "empty: this map was compiled without vis, so it draws everything, everywhere.",
        4 => "compressed visibility. Opaque, and rebuilt only by vvis.",
        8 or 53 => "baked lightmaps. Usually most of the file, and only vrad makes them.",
        9 => "occluders.",
        28 or 29 => "physics collision, built by vphysics. Stale the moment geometry moves.",
        35 => "game lump: static props and detail props live in here, not in the entity lump.",
        40 => "an embedded zip. Materials, soundscapes and scripts a map carries with it.",
        42 => "cubemap sample positions. The baked .vtf files themselves are in the pakfile.",
        43 => "every material name the map uses, as one run of text.",
        _ => "",
    };

    //-------------------------------------------------------------------------

    /// <summary>
    /// Reads a map.
    ///
    /// One pass, seeking to each lump in turn. The alternative -- reading the
    /// whole file in -- would be eleven megabytes held for the sake of the two
    /// hundred kilobytes anybody looks at.
    /// </summary>
    public static BspReport Read(string path, string map)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        long bytes = stream.Length;
        var warnings = new List<string>();

        if (bytes < HeaderSize)
            throw new InvalidDataException($"too short to be a BSP file ({bytes} bytes)");

        string ident = new(reader.ReadChars(4));

        if (ident != "VBSP")
            throw new InvalidDataException($"not a BSP file (identifier '{ident}')");

        int version = reader.ReadInt32();
        var lumps = new BspLump[HeaderLumps];
        var raw = new (int Offset, int Length)[HeaderLumps];

        for (int i = 0; i < HeaderLumps; i++)
        {
            int offset = reader.ReadInt32();
            int length = reader.ReadInt32();
            int lumpVersion = reader.ReadInt32();
            int uncompressed = reader.ReadInt32();

            // A lump whose offset or length runs off the end is the shape a
            // truncated file takes. It is reported rather than read: the header
            // is still worth showing, and every other lump may be intact.
            if (offset < 0 || length < 0 || offset + (long)length > bytes)
            {
                warnings.Add($"lump {i} ({LumpNames[i]}) claims {length} bytes at {offset}, "
                           + $"which is past the end of a {bytes} byte file");
                length = 0;
            }

            raw[i] = (offset, length);

            int stride = StrideOf(i, lumpVersion);

            lumps[i] = new BspLump(i, LumpNames[i], offset, length, lumpVersion, uncompressed,
                stride, stride > 0 ? length / stride : -1, NoteFor(i, length));
        }

        int revision = reader.ReadInt32();

        // Materials first: texdata, overlays, faces and brush sides all name
        // their material by an index into this, and an index is not an answer.
        var materials = new BspMaterials(
            ReadBytes(stream, raw[43]), ReadBytes(stream, raw[44]),
            ReadBytes(stream, raw[6]), ReadBytes(stream, raw[2]), warnings);

        var entities = BspFile.ParseEntities(ReadText(stream, raw[0]));

        // Read once and passed on: the props live inside one of these, and
        // parsing the directory twice is two chances to disagree with itself.
        var gameLumps = ReadGameLumps(stream, raw[35], warnings);

        // Measured before the report is built because it is the one thing here
        // that decompresses rather than reads, and it is worth being able to see
        // it costing something in a profile.
        var visibility = MeasureVisibility(stream, raw[4]);

        var report = new BspReport
        {
            Path = path,
            Map = map,
            Bytes = bytes,
            Ident = ident,
            Version = version,
            Revision = revision,
            Unaccounted = Unclaimed(bytes, raw),
            Lumps = lumps,
            Entities = entities,
            Classes = Histogram(entities),
            Materials = materials.Names,
            TexDatas = ReadTexDatas(stream, raw[7], materials),
            GameLumps = gameLumps,
            Props = ReadProps(stream, gameLumps, out var propModels, warnings),
            PropModels = propModels,
            Cubemaps = ReadCubemaps(stream, raw[42]),
            WorldLights = ReadWorldLights(stream, raw[15], raw[54]),
            Overlays = ReadOverlays(stream, raw[45], materials),
            Displacements = ReadDisplacements(stream, raw[26]),
            Pak = ReadPak(stream, raw[40], warnings),
            Models = ReadModels(stream, raw[14]),
            VisClusters = ReadVisClusters(stream, raw[4]),
            Areas = ReadAreas(stream, raw[20]),
            AreaPortals = ReadAreaPortals(stream, raw[21]),
            Water = ReadWater(stream, raw[36], materials),
            DetailModels = ReadDetailModels(stream, gameLumps),
            Occluders = ReadOccluders(stream, raw[9]),
            LevelFlags = ReadLevelFlags(stream, raw[59]),
            VisAverage = visibility.Average,
            VisMax = visibility.Most,
            Warnings = warnings,
        };

        return report;
    }

    /// <summary>
    /// How much of the file no lump claims.
    ///
    /// Slack is normal -- lumps are four-byte aligned and the header itself
    /// belongs to nobody -- but a large number here means something appended a
    /// lump and left the old copy behind, which is exactly what this tool's own
    /// map writer does every time it saves.
    /// </summary>
    private static long Unclaimed(long bytes, (int Offset, int Length)[] raw)
    {
        long claimed = HeaderSize;

        foreach (var (_, length) in raw)
            claimed += length;

        return Math.Max(0, bytes - claimed);
    }

    private static IReadOnlyList<BspClassCount> Histogram(IReadOnlyList<MapEntity> entities)
        => entities
            .GroupBy(e => e.ClassName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BspClassCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    //-------------------------------------------------------------------------
    // Materials.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The materials, with what the map is built out of each one.
    ///
    /// The last three columns are not in the lump: they come from walking every
    /// face, through its texinfo, to the texdata it lands on, and adding up what
    /// is there. That walk is the only way to answer the two questions worth
    /// asking about a map's materials -- which ones it is actually made of, and
    /// which ones are eating the lightmap -- and it costs one pass over a lump
    /// that has already been read.
    ///
    /// A material with no faces is not an error. It is a material somebody
    /// deleted the last of, still named in the file, and seeing that is the
    /// point.
    /// </summary>
    private static IReadOnlyList<BspTexData> ReadTexDatas(
        Stream stream, (int Offset, int Length) faces, BspMaterials materials)
    {
        int count = materials.TexDataCount;

        var used = new int[count];
        var area = new float[count];
        var luxels = new long[count];

        var faceBytes = ReadBytes(stream, faces);

        for (int i = 0; i + FaceSize <= faceBytes.Length; i += FaceSize)
        {
            int data = materials.TexDataOf(BitConverter.ToInt16(faceBytes, i + 10));

            if (data < 0 || data >= count)
                continue;

            used[data]++;
            area[data] += BitConverter.ToSingle(faceBytes, i + 24);
            luxels[data] += LuxelsOf(faceBytes, i, materials.IsBumped(BitConverter.ToInt16(faceBytes, i + 10)));
        }

        var result = new BspTexData[count];

        for (int i = 0; i < count; i++)
        {
            var (reflectivity, width, height, viewWidth, viewHeight) = materials.SizeOf(i);

            result[i] = new BspTexData(i, materials.NameOfData(i), reflectivity,
                width, height, viewWidth, viewHeight, used[i], area[i], luxels[i]);
        }

        return result;
    }

    /// <summary>
    /// How many lightmap samples one face takes.
    ///
    /// Three multipliers, and leaving out any of them under-reports the lightmap
    /// by a factor that looks perfectly plausible on its own. The stored size is
    /// the number of luxels less one on each axis, because a flat face still has
    /// a sample at both ends. A face is lit once per light style that touches it.
    /// And a bumped surface is lit four times over, once flat and once per vector
    /// of the bump basis.
    ///
    /// The check that these are right is that the total across a map comes to
    /// what is actually in the lighting lump -- which it does, to within the
    /// alignment slack, on both a map of dev textures and a map of stock content.
    /// </summary>
    internal static long LuxelsOf(byte[] faces, int at, bool bumped)
    {
        // -1 means the face has no lightmap at all: sky, nodraw, and anything
        // else vrad did not light.
        if (BitConverter.ToInt32(faces, at + 20) < 0)
            return 0;

        long width = BitConverter.ToInt32(faces, at + 36) + 1L;
        long height = BitConverter.ToInt32(faces, at + 40) + 1L;

        if (width <= 0 || height <= 0)
            return 0;

        int styles = 0;

        for (int i = 0; i < 4; i++)
        {
            if (faces[at + 16 + i] != 255)
                styles++;
        }

        return width * height * Math.Max(styles, 1)
             * (bumped ? BspMaterials.BumpedLightmaps : 1);
    }

    //-------------------------------------------------------------------------
    // The game lump, and the static props inside it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The game lump's directory.
    ///
    /// Worth showing on its own: a map with detail props but no static props,
    /// or with a static prop version this build cannot read, is legible here
    /// even when the props list below comes back empty.
    ///
    /// The offsets in this directory are offsets into the <em>file</em>, not into
    /// the game lump. That is the trap in this format, and the reason a tool that
    /// relocates the game lump has to rewrite every one of them.
    /// </summary>
    private static IReadOnlyList<BspGameLump> ReadGameLumps(
        Stream stream, (int Offset, int Length) lump, List<string> warnings)
    {
        var bytes = ReadBytes(stream, lump);

        if (bytes.Length < 4)
            return [];

        int count = BitConverter.ToInt32(bytes, 0);

        if (count < 0 || 4 + count * 16 > bytes.Length)
        {
            warnings.Add($"the game lump claims {count} sub-lumps, which do not fit in {bytes.Length} bytes");
            return [];
        }

        var result = new BspGameLump[count];

        for (int i = 0; i < count; i++)
        {
            int at = 4 + i * 16;
            int id = BitConverter.ToInt32(bytes, at);

            result[i] = new BspGameLump(
                FourCC(id),
                BitConverter.ToUInt16(bytes, at + 4),
                BitConverter.ToUInt16(bytes, at + 6),
                BitConverter.ToInt32(bytes, at + 8),
                BitConverter.ToInt32(bytes, at + 12));
        }

        return result;
    }

    /// <summary>An id stored as a packed four-character code, big end first.</summary>
    private static string FourCC(int id) => new(
    [
        (char)((id >> 24) & 0xff), (char)((id >> 16) & 0xff),
        (char)((id >> 8) & 0xff), (char)(id & 0xff),
    ]);

    /// <summary>
    /// The static props.
    ///
    /// The record grew across six versions of the format and there is no field
    /// saying which one is on disk beyond the sub-lump's version number -- and
    /// maps exist whose version number does not match their record size, because
    /// the tools that made them predate the field. So the size is measured
    /// instead: the count is known and the bytes are known, and the quotient is
    /// the stride whatever the header claims.
    ///
    /// Only the fields common to every version are read from a stride this does
    /// not recognise. A prop drawn in the wrong place is worse than one whose
    /// fade distance is missing.
    /// </summary>
    private static IReadOnlyList<BspStaticProp> ReadProps(
        Stream stream, IReadOnlyList<BspGameLump> directory,
        out IReadOnlyList<string> models, List<string> warnings)
    {
        models = [];

        var props = directory.FirstOrDefault(g => g.Id == "sprp");

        if (props.Id != "sprp" || props.Length <= 0)
            return [];

        var bytes = ReadAt(stream, props.Offset, props.Length);

        if (bytes.Length < 4)
            return [];

        int at = 0;
        int dictCount = BitConverter.ToInt32(bytes, at);
        at += 4;

        if (dictCount < 0 || at + dictCount * 128 > bytes.Length)
        {
            warnings.Add($"the static prop dictionary claims {dictCount} models, which do not fit");
            return [];
        }

        var names = new string[dictCount];

        for (int i = 0; i < dictCount; i++)
        {
            names[i] = Encoding.UTF8.GetString(bytes, at, 128).TrimEnd('\0');
            at += 128;
        }

        models = names;

        if (at + 4 > bytes.Length)
            return [];

        // The leaf array. Skipped rather than read -- it is what tells the engine
        // which visibility leaves each prop can be seen from, and it is a fact
        // about the tree rather than about the prop.
        int leafCount = BitConverter.ToInt32(bytes, at);
        at += 4;

        if (leafCount < 0 || at + leafCount * 2 > bytes.Length)
        {
            warnings.Add($"the static prop leaf array claims {leafCount} entries, which do not fit");
            return [];
        }

        at += leafCount * 2;

        if (at + 4 > bytes.Length)
            return [];

        int count = BitConverter.ToInt32(bytes, at);
        at += 4;

        if (count <= 0)
            return [];

        int remaining = bytes.Length - at;
        int stride = remaining / count;

        if (stride < 56)
        {
            warnings.Add($"static prop records are {stride} bytes, which is shorter than any known version");
            return [];
        }

        // The skin, the fade distances and the lighting origin sit at the same
        // offsets in every version of the record: the int after the solidity is
        // four-byte aligned whether the byte before it holds the flags or is
        // padding. Only the flags themselves moved -- version 10 widened them and
        // put them at the end -- and the stride is what tells the two apart.
        bool v10 = stride >= 72;

        var result = new BspStaticProp[count];

        for (int i = 0; i < count; i++)
        {
            int p = at + i * stride;
            int type = BitConverter.ToUInt16(bytes, p + 24);

            result[i] = new BspStaticProp(
                i,
                type >= 0 && type < names.Length ? names[type] : $"<model {type}>",
                [Float(bytes, p), Float(bytes, p + 4), Float(bytes, p + 8)],
                [Float(bytes, p + 12), Float(bytes, p + 16), Float(bytes, p + 20)],
                BitConverter.ToInt32(bytes, p + 32),
                Float(bytes, p + 36),
                Float(bytes, p + 40),
                bytes[p + 30],
                v10 ? BitConverter.ToInt32(bytes, p + 64) : bytes[p + 31],
                BitConverter.ToUInt16(bytes, p + 26),
                BitConverter.ToUInt16(bytes, p + 28));
        }

        return result;
    }

    //-------------------------------------------------------------------------
    // The small, flat lumps.
    //-------------------------------------------------------------------------

    private static IReadOnlyList<BspCubemap> ReadCubemaps(Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);
        var result = new BspCubemap[bytes.Length / 16];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * 16;

            result[i] = new BspCubemap(
                [BitConverter.ToInt32(bytes, at), BitConverter.ToInt32(bytes, at + 4),
                 BitConverter.ToInt32(bytes, at + 8)],
                bytes[at + 12]);
        }

        return result;
    }

    /// <summary>What kind of light vrad recorded. The values are emittype_t.</summary>
    private static string LightType(int type) => type switch
    {
        0 => "surface",
        1 => "point",
        2 => "spotlight",
        3 => "skylight",
        4 => "quakelight",
        5 => "skyambient",
        _ => $"type {type}",
    };

    /// <summary>
    /// The lights, from both the LDR and the HDR lump.
    ///
    /// Both, because they are separately compiled and a map lit for one and not
    /// the other is a real and confusing state: the map looks right until
    /// somebody turns HDR on.
    /// </summary>
    private static IReadOnlyList<BspWorldLight> ReadWorldLights(
        Stream stream, (int Offset, int Length) ldr, (int Offset, int Length) hdr)
    {
        var result = new List<BspWorldLight>();

        Add(ldr, false);
        Add(hdr, true);

        return result;

        void Add((int Offset, int Length) lump, bool isHdr)
        {
            var bytes = ReadBytes(stream, lump);

            for (int i = 0; i + 88 <= bytes.Length; i += 88)
            {
                result.Add(new BspWorldLight(
                    result.Count,
                    [Float(bytes, i), Float(bytes, i + 4), Float(bytes, i + 8)],
                    [Float(bytes, i + 12), Float(bytes, i + 16), Float(bytes, i + 20)],
                    [Float(bytes, i + 24), Float(bytes, i + 28), Float(bytes, i + 32)],
                    LightType(BitConverter.ToInt32(bytes, i + 40)),
                    BitConverter.ToInt32(bytes, i + 44),
                    Float(bytes, i + 60),
                    BitConverter.ToInt32(bytes, i + 84),
                    isHdr));
            }
        }
    }

    private static IReadOnlyList<BspOverlay> ReadOverlays(
        Stream stream, (int Offset, int Length) lump, BspMaterials materials)
    {
        var bytes = ReadBytes(stream, lump);

        if (bytes.Length < 352)
            return [];

        var result = new List<BspOverlay>(bytes.Length / 352);

        for (int i = 0; i + 352 <= bytes.Length; i += 352)
        {
            short info = BitConverter.ToInt16(bytes, i + 4);
            ushort packed = BitConverter.ToUInt16(bytes, i + 6);

            result.Add(new BspOverlay(
                BitConverter.ToInt32(bytes, i),
                info,
                materials.NameOf(info),

                // Past the 64 face slots, the two UV ranges and the four UV
                // points: the origin is the last vector but one.
                [Float(bytes, i + 328), Float(bytes, i + 332), Float(bytes, i + 336)],

                // The low nine bits are the face count; the rest is render order.
                packed & 0x1ff));
        }

        return result;
    }

    private static IReadOnlyList<BspDisplacement> ReadDisplacements(
        Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);
        var result = new List<BspDisplacement>(bytes.Length / 176);

        for (int i = 0; i + 176 <= bytes.Length; i += 176)
        {
            int power = BitConverter.ToInt32(bytes, i + 20);

            // A displacement is a (2^power + 1) square grid of vertices. Showing
            // the power alone would mean everybody looking at this doing the same
            // arithmetic in their head.
            int side = power is >= 1 and <= 4 ? (1 << power) + 1 : 0;

            result.Add(new BspDisplacement(
                result.Count,
                [Float(bytes, i), Float(bytes, i + 4), Float(bytes, i + 8)],
                power,
                BitConverter.ToUInt16(bytes, i + 36),
                BitConverter.ToInt32(bytes, i + 32),
                side * side));
        }

        return result;
    }

    private static IReadOnlyList<BspModel> ReadModels(Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);
        var result = new BspModel[bytes.Length / 48];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * 48;

            result[i] = new BspModel(
                [Float(bytes, at), Float(bytes, at + 4), Float(bytes, at + 8)],
                [Float(bytes, at + 12), Float(bytes, at + 16), Float(bytes, at + 20)],
                [Float(bytes, at + 24), Float(bytes, at + 28), Float(bytes, at + 32)]);
        }

        return result;
    }

    /// <summary>
    /// How many visibility clusters the map has.
    ///
    /// The rest of the lump is a compressed bit vector per cluster, which is
    /// nothing to look at. The count is worth having on its own: zero means the
    /// map was compiled without vis and is drawing every surface in it at once,
    /// which is the single most common reason a small map runs badly.
    /// </summary>
    private static int ReadVisClusters(Stream stream, (int Offset, int Length) lump)
    {
        if (lump.Length < 4)
            return 0;

        var bytes = ReadAt(stream, lump.Offset, 4);
        return BitConverter.ToInt32(bytes, 0);
    }

    //-------------------------------------------------------------------------
    // Areas, water, occluders and the level's own flags. All small, all read
    // for the same reason: each is one number that changes what you go and look
    // at next, and none of them is visible anywhere else.
    //-------------------------------------------------------------------------

    private static IReadOnlyList<BspArea> ReadAreas(Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);
        var result = new BspArea[bytes.Length / 8];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new BspArea(i,
                BitConverter.ToInt32(bytes, i * 8 + 4),
                BitConverter.ToInt32(bytes, i * 8));
        }

        return result;
    }

    private static IReadOnlyList<BspAreaPortal> ReadAreaPortals(
        Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);
        var result = new BspAreaPortal[bytes.Length / 12];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * 12;

            result[i] = new BspAreaPortal(i,
                BitConverter.ToUInt16(bytes, at),
                BitConverter.ToUInt16(bytes, at + 2),
                BitConverter.ToInt32(bytes, at + 8),
                BitConverter.ToUInt16(bytes, at + 4),
                BitConverter.ToUInt16(bytes, at + 6));
        }

        return result;
    }

    private static IReadOnlyList<BspWaterData> ReadWater(
        Stream stream, (int Offset, int Length) lump, BspMaterials materials)
    {
        var bytes = ReadBytes(stream, lump);
        var result = new BspWaterData[bytes.Length / 10];

        for (int i = 0; i < result.Length; i++)
        {
            int at = i * 10;
            short info = BitConverter.ToInt16(bytes, at + 8);

            result[i] = new BspWaterData(i,
                Float(bytes, at), Float(bytes, at + 4), info, materials.NameOf(info));
        }

        return result;
    }

    /// <summary>
    /// The detail prop models: grass and rubble, scattered by a material rather
    /// than placed. Only the dictionary is read -- the objects themselves are
    /// thousands of two-triangle sprites, and none of them is a thing anybody
    /// looks up one at a time.
    /// </summary>
    private static IReadOnlyList<BspDetailModel> ReadDetailModels(
        Stream stream, IReadOnlyList<BspGameLump> directory)
    {
        var detail = directory.FirstOrDefault(g => g.Id == "dprp");

        if (detail.Id != "dprp" || detail.Length < 4)
            return [];

        var bytes = ReadAt(stream, detail.Offset, detail.Length);

        if (bytes.Length < 4)
            return [];

        int count = BitConverter.ToInt32(bytes, 0);

        if (count <= 0 || 4 + count * 128 > bytes.Length)
            return [];

        var result = new BspDetailModel[count];

        for (int i = 0; i < count; i++)
            result[i] = new BspDetailModel(i, Encoding.UTF8.GetString(bytes, 4 + i * 128, 128).TrimEnd('\0'));

        return result;
    }

    /// <summary>How many occluders the map has. Nearly always none, which is itself worth seeing.</summary>
    private static int ReadOccluders(Stream stream, (int Offset, int Length) lump)
    {
        if (lump.Length < 4)
            return 0;

        var bytes = ReadAt(stream, lump.Offset, 4);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static string ReadLevelFlags(Stream stream, (int Offset, int Length) lump)
    {
        if (lump.Length < 4)
            return "";

        var bytes = ReadAt(stream, lump.Offset, 4);
        return BspFlags.DescribeLevel(BitConverter.ToInt32(bytes, 0));
    }

    //-------------------------------------------------------------------------
    // Visibility.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How much of the map an average place in it can see.
    ///
    /// This is the number that decides whether a map runs. Everything in the
    /// visible set is drawn, so the size of that set is the size of a frame's
    /// work in the unit the compiler thinks in -- and it is the one important
    /// fact about a compiled map that is nowhere in it, because the lump holds
    /// the sets rather than their sizes.
    ///
    /// The sets are run-length encoded, zero bytes only: a zero is followed by a
    /// count of how many zero bytes it stands for. Anything else is eight
    /// clusters' worth of bits. Decompressing every one of them is a few
    /// hundred kilobytes of work on the largest map, done once per read.
    /// </summary>
    private static (int Average, int Most) MeasureVisibility(
        Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);

        if (bytes.Length < 4)
            return (-1, -1);

        int clusters = BitConverter.ToInt32(bytes, 0);

        // The offset table follows the count, two per cluster: one for what can
        // be seen and one for what can be heard. Only the first is wanted.
        if (clusters <= 0 || 4 + clusters * 8 > bytes.Length)
            return (-1, -1);

        long total = 0;
        int most = 0;

        // A set has one bit per cluster, so this is how far to read before the
        // rest of the bytes belong to somebody else's set.
        int setBytes = (clusters + 7) / 8;

        for (int cluster = 0; cluster < clusters; cluster++)
        {
            int at = BitConverter.ToInt32(bytes, 4 + cluster * 8);

            if (at <= 0 || at >= bytes.Length)
                continue;

            int visible = 0;
            int written = 0;

            while (written < setBytes && at < bytes.Length)
            {
                byte value = bytes[at++];

                if (value != 0)
                {
                    visible += System.Numerics.BitOperations.PopCount(value);
                    written++;
                    continue;
                }

                // A zero run. The count that follows is bytes, not clusters, and
                // reading it as clusters would under-count by eight.
                if (at >= bytes.Length)
                    break;

                written += bytes[at++];
            }

            total += visible;

            if (visible > most)
                most = visible;
        }

        return ((int)(total / clusters), most);
    }

    //-------------------------------------------------------------------------
    // The pakfile.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What the map carries inside it.
    ///
    /// A plain zip, read with the framework's reader rather than by hand: it is a
    /// real zip written by a real zip writer, and hand-walking a central
    /// directory to save a MemoryStream would be inventing a way to be wrong.
    ///
    /// Listed rather than extracted. The names and sizes answer the question this
    /// is asked -- "is the missing material in the map or not" -- and the contents
    /// are somebody else's format.
    /// </summary>
    private static IReadOnlyList<BspPakEntry> ReadPak(
        Stream stream, (int Offset, int Length) lump, List<string> warnings)
    {
        if (lump.Length <= 0)
            return [];

        try
        {
            using var memory = new MemoryStream(ReadBytes(stream, lump), writable: false);
            using var zip = new ZipArchive(memory, ZipArchiveMode.Read);

            return zip.Entries
                .Select(e => new BspPakEntry(e.FullName, e.Length, e.CompressedLength))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception e)
        {
            // A pakfile that will not open is a fact about the map worth
            // reporting. Everything else in the report is still good.
            warnings.Add($"the pakfile could not be read: {e.Message}");
            return [];
        }
    }

    //-------------------------------------------------------------------------

    private static byte[] ReadBytes(Stream stream, (int Offset, int Length) lump)
        => lump.Length <= 0 ? [] : ReadAt(stream, lump.Offset, lump.Length);

    private static byte[] ReadAt(Stream stream, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + (long)length > stream.Length)
            return [];

        stream.Seek(offset, SeekOrigin.Begin);

        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string ReadText(Stream stream, (int Offset, int Length) lump)
    {
        var bytes = ReadBytes(stream, lump);
        int end = Array.IndexOf(bytes, (byte)0);

        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    private static float Float(byte[] bytes, int at) => BitConverter.ToSingle(bytes, at);
}
