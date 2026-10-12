using System.Text;

namespace Debug.Api;

/// <summary>
/// The chain from a surface to the material on it.
///
/// Nothing in a BSP names a material directly. A face names a texinfo, a texinfo
/// names a texdata, a texdata holds an offset into one long run of text, and that
/// is where the name is. Four lumps and three indirections, walked by faces,
/// brush sides, overlays and displacements alike.
///
/// So it is walked once, here. Every caller that had its own copy of the chain
/// was a copy that could be wrong on its own, and the failure -- a name resolving
/// to the wrong material -- looks exactly like correct output.
/// </summary>
internal sealed class BspMaterials
{
    private const int TexInfoSize = 72;
    private const int TexDataSize = 32;

    /// <summary>Where texinfo keeps the index of its texdata, and its surface flags.</summary>
    private const int TexInfoTexData = 68;
    private const int TexInfoFlags = 64;

    /// <summary>Where texdata keeps the index of its name.</summary>
    private const int TexDataNameId = 12;

    private readonly byte[] _texInfo;
    private readonly byte[] _texData;

    /// <summary>Every material name in the map, in the order the name table gives them.</summary>
    public IReadOnlyList<string> Names { get; }

    public int TexInfoCount => _texInfo.Length / TexInfoSize;
    public int TexDataCount => _texData.Length / TexDataSize;

    public BspMaterials(byte[] names, byte[] nameTable, byte[] texInfo, byte[] texData,
                        List<string>? warnings = null)
    {
        _texInfo = texInfo;
        _texData = texData;
        Names = ReadNames(names, nameTable, warnings);
    }

    /// <summary>
    /// The names, split by the offset table rather than by the nulls between them.
    ///
    /// Splitting on nulls very nearly works, and is wrong for any map with an
    /// empty name in it: every name after it shifts up by one, and every lookup
    /// through the table lands on its neighbour.
    /// </summary>
    private static IReadOnlyList<string> ReadNames(byte[] text, byte[] table, List<string>? warnings)
    {
        var names = new List<string>(table.Length / 4);

        for (int i = 0; i + 4 <= table.Length; i += 4)
        {
            int at = BitConverter.ToInt32(table, i);

            if (at < 0 || at >= text.Length)
            {
                warnings?.Add($"material {i / 4} points at offset {at}, outside a {text.Length} byte name table");
                names.Add("");
                continue;
            }

            int end = Array.IndexOf(text, (byte)0, at);
            names.Add(Encoding.UTF8.GetString(text, at, (end < 0 ? text.Length : end) - at));
        }

        return names;
    }

    /// <summary>The texdata a texinfo points at, or -1 when it points nowhere valid.</summary>
    public int TexDataOf(int texInfo)
        => texInfo < 0 || (texInfo + 1) * TexInfoSize > _texInfo.Length
            ? -1
            : BitConverter.ToInt32(_texInfo, texInfo * TexInfoSize + TexInfoTexData);

    /// <summary>What a surface does: the SURF_ flags on its texinfo.</summary>
    public int FlagsOf(int texInfo)
        => texInfo < 0 || (texInfo + 1) * TexInfoSize > _texInfo.Length
            ? 0
            : BitConverter.ToInt32(_texInfo, texInfo * TexInfoSize + TexInfoFlags);

    /// <summary>The material a texdata names.</summary>
    public string NameOfData(int texData)
    {
        if (texData < 0 || (texData + 1) * TexDataSize > _texData.Length)
            return "";

        int nameId = BitConverter.ToInt32(_texData, texData * TexDataSize + TexDataNameId);

        return nameId >= 0 && nameId < Names.Count ? Names[nameId] : "";
    }

    /// <summary>The material on a surface, all three hops at once.</summary>
    public string NameOf(int texInfo) => NameOfData(TexDataOf(texInfo));

    /// <summary>
    /// Whether a surface is lit for bump mapping.
    ///
    /// It costs four lightmaps rather than one -- a flat one and three for the
    /// bump basis -- so this is the difference between a lightmap estimate that
    /// matches the lump and one that is a quarter of it. A map of stock content
    /// is mostly bumped surfaces, and one of dev textures is none of them, which
    /// is exactly how the mistake survives being spot-checked.
    /// </summary>
    public bool IsBumped(int texInfo) => (FlagsOf(texInfo) & SurfBumpLight) != 0;

    /// <summary>SURF_BUMPLIGHT.</summary>
    private const int SurfBumpLight = 0x0800;

    /// <summary>How many lightmaps one style takes on a surface.</summary>
    public const int BumpedLightmaps = 4;

    /// <summary>
    /// How many world units one luxel of this surface covers.
    ///
    /// Stored as its reciprocal, spread across a vector, so it is the length of
    /// the lightmap's S axis that holds it. Sixteen is the default and what most
    /// of a map is; anything much smaller is a surface somebody asked for detailed
    /// lighting on, and those are what fill a lightmap.
    /// </summary>
    public float LightmapScaleOf(int texInfo)
    {
        if (texInfo < 0 || (texInfo + 1) * TexInfoSize > _texInfo.Length)
            return 0f;

        int at = texInfo * TexInfoSize + 32;

        double x = BitConverter.ToSingle(_texInfo, at);
        double y = BitConverter.ToSingle(_texInfo, at + 4);
        double z = BitConverter.ToSingle(_texInfo, at + 8);

        double length = Math.Sqrt(x * x + y * y + z * z);

        return length <= 0 ? 0f : (float)(1.0 / length);
    }

    /// <summary>The reflectivity and image size vbsp recorded for a texdata.</summary>
    public (float[] Reflectivity, int Width, int Height, int ViewWidth, int ViewHeight) SizeOf(int texData)
    {
        int at = texData * TexDataSize;

        return (
            [
                BitConverter.ToSingle(_texData, at),
                BitConverter.ToSingle(_texData, at + 4),
                BitConverter.ToSingle(_texData, at + 8),
            ],
            BitConverter.ToInt32(_texData, at + 16),
            BitConverter.ToInt32(_texData, at + 20),
            BitConverter.ToInt32(_texData, at + 24),
            BitConverter.ToInt32(_texData, at + 28));
    }
}
