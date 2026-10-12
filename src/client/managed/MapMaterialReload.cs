using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SourceSharp.Client;

/// <summary>
/// What a listen server's spawn reloads from the client's material system (plan_map_load P1).
///
/// <para>
/// The shipped engine's <c>SV_SpawnServer</c> mounts the new map's pak and then
/// calls <c>materials-&gt;ReloadMaterials(NULL)</c>: every resident material is
/// re-parsed and every shader object restored, which rebuilds every loaded
/// model's meshes. It exists so a map pak's override of an already-loaded
/// material takes effect. Measured at 96d57d1 on a listen server: 0.56–0.94 s a
/// load, growing with what is resident, and the same on a same-map reload, where
/// nothing can have changed. A remote client never runs it.
/// </para>
///
/// <para>
/// What can change is bounded by two paks: the new map's (its materials may
/// override loaded ones) and the previous map's (its overrides must give way to
/// the stock files). So only the materials either pak names are reloaded, and
/// none when the same pak is mounted again. A map's own
/// <c>materials/maps/&lt;map&gt;/</c> directory (vbsp's cubemap patches) cannot
/// already be loaded under another map, so it is left out. A pak that ships a
/// texture outside that directory may change a material it does not name, and
/// only the full reload covers that: the answer is then "everything".
/// </para>
/// </summary>
public sealed class MapMaterialReload
{
    /// <summary>The reload a spawn needs.</summary>
    /// <param name="Full">True: the stock full reload.</param>
    /// <param name="Materials">Otherwise, the material names to reload (possibly none).</param>
    public sealed record Plan(bool Full, IReadOnlyList<string> Materials);

    /// <summary>The paks' material names and their texture overrides, or null when the file is not a readable BSP.</summary>
    public sealed record PakIndex(string Map, string ContentHash, IReadOnlyList<string> Materials, bool OverridesTextures);

    private const int PakFileLump = 40;
    private const int LumpCount = 64;
    private const uint EndOfCentralDirectory = 0x06054b50;
    private const uint CentralDirectoryEntry = 0x02014b50;

    /// <summary>
    /// The plan for a spawn mounting <paramref name="next"/> after
    /// <paramref name="previous"/> (null: no previous map). An unreadable pak on
    /// either side is the full reload: guessing would be how an override is lost.
    /// </summary>
    public Plan Decide(PakIndex? next, PakIndex? previous, bool previousExpected)
    {
        if (next is null || (previousExpected && previous is null))
            return new Plan(true, []);
        if (previous is not null && previous.ContentHash == next.ContentHash && previous.Map == next.Map)
            return new Plan(false, []);
        if (next.OverridesTextures || (previous?.OverridesTextures ?? false))
            return new Plan(true, []);

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        names.UnionWith(next.Materials);
        if (previous is not null)
            names.UnionWith(previous.Materials);
        return new Plan(false, [.. names]);
    }

    /// <summary>The material names (no <c>materials/</c>, no <c>.vmt</c>) a BSP's pak ships, outside its own map directory.</summary>
    public PakIndex? Index(string map, ReadOnlySpan<byte> bsp)
    {
        if (bsp.Length < 8 + LumpCount * 16 || BinaryPrimitives.ReadUInt32LittleEndian(bsp) != 0x50534256) // "VBSP"
            return null;
        int at = 8 + PakFileLump * 16;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bsp[at..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bsp[(at + 4)..]);
        if (offset < 0 || length < 0 || (long)offset + length > bsp.Length)
            return null;
        var pak = bsp.Slice(offset, length);
        string hash = Convert.ToHexString(SHA256.HashData(pak));
        if (length == 0)
            return new PakIndex(map, hash, [], false);

        // The end-of-central-directory record: 22 bytes plus up to 64 KiB of comment, from the end.
        int eocd = -1;
        for (int i = pak.Length - 22; i >= Math.Max(0, pak.Length - 22 - 65535); i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(pak[i..]) == EndOfCentralDirectory)
            {
                eocd = i;
                break;
            }
        }
        if (eocd < 0)
            return null;
        int entries = BinaryPrimitives.ReadUInt16LittleEndian(pak[(eocd + 10)..]);
        int cd = BinaryPrimitives.ReadInt32LittleEndian(pak[(eocd + 16)..]);

        string own = $"materials/maps/{map}/";
        var materials = new List<string>();
        bool textures = false;
        int p = cd;
        for (int e = 0; e < entries; e++)
        {
            if (p < 0 || p + 46 > pak.Length || BinaryPrimitives.ReadUInt32LittleEndian(pak[p..]) != CentralDirectoryEntry)
                return null;
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(pak[(p + 28)..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(pak[(p + 30)..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(pak[(p + 32)..]);
            if (p + 46 + nameLength > pak.Length)
                return null;
            string name = Encoding.ASCII.GetString(pak.Slice(p + 46, nameLength)).Replace('\\', '/').ToLowerInvariant();
            p += 46 + nameLength + extraLength + commentLength;

            if (!name.StartsWith("materials/", StringComparison.Ordinal) || name.StartsWith(own, StringComparison.Ordinal))
                continue;
            if (name.EndsWith(".vmt", StringComparison.Ordinal))
                materials.Add(name["materials/".Length..^".vmt".Length]);
            else if (name.EndsWith(".vtf", StringComparison.Ordinal))
                textures = true;
        }
        return new PakIndex(map, hash, materials, textures);
    }
}
