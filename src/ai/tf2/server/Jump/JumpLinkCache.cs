using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What a pass found and what it took, as its cache file holds it: the links, the candidates searched, the flights and its thread CPU.</summary>
public sealed record JumpLinkFile(IReadOnlyList<JumpLink> Links, int Candidates, int Flights, double PassMs);

/// <summary>
/// A level's blast links cached beside its graph (lane G5c-2a, as the choke points are): the file
/// is named after the graph's (<see cref="NavGraphCache.SidePathFor"/>, suffix
/// <c>blast&lt;revision&gt;</c>), and its header carries the revision, the graph's hash and cell
/// count, and a key of everything else the links come from (the launcher, the body, the tick, the
/// search's and the pass's settings, the spawns: <see cref="InputsKey"/>), so a file of another
/// graph, another revision or other inputs is refused and the pass runs again.
/// </summary>
/// <remarks>
/// Format (little-endian): magic <c>SSJLNK01</c>; revision; graph hash; cell count; the inputs key
/// (length-prefixed UTF-8); candidates, flights, the pass's milliseconds; the link count, and each
/// link: from, to, the recipe (takeoff, landing, run-up, pitch, yaw offset, shot delay, ducked), the
/// family, robustness, seconds, air seconds, health, walk seconds (NaN for no walk); then whether the
/// recipe has a second rocket (revision 2, lane G7b: 1; revision 3, lane G7c: 2 for one fired at a wall),
/// and when it has, its place, earliest and latest commands, pitch and yaw offset. A one-rocket link's
/// numbers are the bits they were at revision 1.
/// </remarks>
/// <param name="revision">The format's and the pass's revision: <see cref="Revision"/>, another only in a fact.</param>
public sealed class JumpLinkCache(int revision = JumpLinkCache.Revision)
{
    /// <summary>
    /// Raised whenever the pass or the file changes what a link is: 2, a recipe's second rocket (lane G7b); 3, a second rocket
    /// at a wall (lane G7c); 4, the simulated rocket converges on the crosshair as the game's does (round 2's r2g and r2j), so
    /// every level's links are found again under the new name (blast4).
    /// </summary>
    public const int Revision = 4;

    private static ReadOnlySpan<byte> Magic => "SSJLNK01"u8;

    /// <summary>The suffix of the file beside the graph's.</summary>
    public string Suffix => $"blast{revision}";

    /// <summary>
    /// The key of the inputs the links come from besides the graph: the first 16 hex digits of the
    /// SHA-256 of the records' own text (every member, printed in the invariant culture), so any
    /// change of a number refuses an older file. A place the pass was told to skip (lane G6b) is part of it only when
    /// there is one, so a pass with none keeps the key it had.
    /// </summary>
    public static string InputsKey(JumpLauncher launcher, in JumpPilotLimits limits, JumpSearchSettings search, JumpPlacesSettings places, IReadOnlyList<int> seeds,
                                   JumpSkip? skip = null)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        CultureInfo was = CultureInfo.CurrentCulture;
        string text;
        try
        {
            // A record prints its members in the thread's culture: the key must not change with the server's locale.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            text = $"{launcher}|{limits}|{search}|{places}|{string.Join(",", seeds)}" + (skip is { } s ? $"|skip {s}" : "");
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    public byte[] Encode(NavGraph graph, string inputsKey, JumpLinkFile file)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(inputsKey);
        ArgumentNullException.ThrowIfNull(file);
        var w = new List<byte>(64 + (file.Links.Count * 72));
        var b = new byte[8];
        void I32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(b, v);
            w.AddRange(b.AsSpan(0, 4));
        }

        void F32(float v) => I32(BitConverter.SingleToInt32Bits(v));
        void Vec(Vector v)
        {
            F32(v.X);
            F32(v.Y);
            F32(v.Z);
        }

        w.AddRange(Magic);
        I32(revision);
        BinaryPrimitives.WriteUInt64LittleEndian(b, graph.Hash);
        w.AddRange(b);
        I32(graph.CellCount);
        byte[] key = Encoding.UTF8.GetBytes(inputsKey);
        I32(key.Length);
        w.AddRange(key);
        I32(file.Candidates);
        I32(file.Flights);
        BinaryPrimitives.WriteDoubleLittleEndian(b, file.PassMs);
        w.AddRange(b);
        I32(file.Links.Count);
        foreach (JumpLink l in file.Links)
        {
            I32(l.From);
            I32(l.To);
            Vec(l.Recipe.Takeoff);
            Vec(l.Recipe.Landing);
            F32(l.Recipe.RunUp);
            F32(l.Recipe.Pitch);
            F32(l.Recipe.YawOffset);
            I32(l.Recipe.ShotDelay);
            I32(l.Recipe.Ducked ? 1 : 0);
            I32((int)l.Family);
            F32(l.Robustness);
            F32(l.Seconds);
            F32(l.AirSeconds);
            F32(l.Health);
            F32(l.WalkSeconds ?? float.NaN);
            I32(l.Recipe.Second is not { } shot ? 0 : shot.Wall ? 2 : 1);
            if (l.Recipe.Second is { } second)
            {
                Vec(second.Over);
                I32(second.Earliest);
                I32(second.Latest);
                F32(second.Pitch);
                F32(second.YawOffset);
            }
        }

        return [.. w];
    }

    /// <summary>The links a file holds, or null when it is not one of this revision, of <paramref name="graph"/> and of <paramref name="inputsKey"/>'s inputs, or is cut short.</summary>
    public JumpLinkFile? Decode(ReadOnlySpan<byte> bytes, NavGraph graph, string inputsKey)
    {
        ArgumentNullException.ThrowIfNull(graph);
        int at = 0;
        bool Take(int n, out ReadOnlySpan<byte> s, ReadOnlySpan<byte> all)
        {
            s = n >= 0 && at + n <= all.Length ? all.Slice(at, n) : default;
            at += n;
            return n >= 0 && at <= all.Length;
        }

        if (!Take(Magic.Length, out ReadOnlySpan<byte> magic, bytes) || !magic.SequenceEqual(Magic))
            return null;
        if (!Take(20, out ReadOnlySpan<byte> head, bytes)
            || BinaryPrimitives.ReadInt32LittleEndian(head) != revision
            || BinaryPrimitives.ReadUInt64LittleEndian(head[4..]) != graph.Hash
            || BinaryPrimitives.ReadInt32LittleEndian(head[12..]) != graph.CellCount)
            return null;
        int keyLength = BinaryPrimitives.ReadInt32LittleEndian(head[16..]);
        if (keyLength != Encoding.UTF8.GetByteCount(inputsKey) || !Take(keyLength, out ReadOnlySpan<byte> key, bytes) || Encoding.UTF8.GetString(key) != inputsKey)
            return null;
        if (!Take(20, out ReadOnlySpan<byte> stats, bytes))
            return null;
        int candidates = BinaryPrimitives.ReadInt32LittleEndian(stats), flights = BinaryPrimitives.ReadInt32LittleEndian(stats[4..]);
        double passMs = BinaryPrimitives.ReadDoubleLittleEndian(stats[8..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(stats[16..]);
        const int LinkBytes = (2 * 4) + (6 * 4) + (3 * 4) + (3 * 4) + (5 * 4) + 4, SecondBytes = (3 * 4) + (2 * 4) + (2 * 4);
        if (count < 0 || count > (bytes.Length - at) / LinkBytes)
            return null;
        var links = new JumpLink[count];
        for (int i = 0; i < count; i++)
        {
            if (!Take(LinkBytes, out ReadOnlySpan<byte> r, bytes))
                return null;
            int from = I(r, 0), to = I(r, 4), hasSecond = I(r, 76);
            if ((uint)from >= (uint)graph.CellCount || (uint)to >= (uint)graph.CellCount || (uint)hasSecond > 2u)
                return null;
            JumpSecondShot? second = null;
            if (hasSecond > 0)
            {
                if (!Take(SecondBytes, out ReadOnlySpan<byte> t, bytes))
                    return null;
                second = new JumpSecondShot(new Vector(F(t, 0), F(t, 4), F(t, 8)), I(t, 12), I(t, 16), F(t, 20), F(t, 24)) { Wall = hasSecond == 2 };
            }

            var recipe = new JumpRecipe(new Vector(F(r, 8), F(r, 12), F(r, 16)), new Vector(F(r, 20), F(r, 24), F(r, 28)), F(r, 32), F(r, 36), F(r, 40), I(r, 44), I(r, 48) != 0, second);
            float walk = F(r, 72);
            links[i] = new JumpLink(from, to, recipe, (JumpFamily)I(r, 52), F(r, 56), F(r, 60), F(r, 64), F(r, 68), float.IsNaN(walk) ? null : walk);
        }

        if (at != bytes.Length)
            return null;
        return new JumpLinkFile(links, candidates, flights, passMs);
    }

    private static int I(ReadOnlySpan<byte> r, int at) => BinaryPrimitives.ReadInt32LittleEndian(r[at..]);

    private static float F(ReadOnlySpan<byte> r, int at) => BitConverter.Int32BitsToSingle(I(r, at));

    /// <summary>The file at <paramref name="path"/>, or null when there is none or it is refused.</summary>
    public JumpLinkFile? TryRead(string path, NavGraph graph, string inputsKey)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return Decode(File.ReadAllBytes(path), graph, inputsKey);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Writes the file through a name of its own and a rename, so a reader never sees half of it.</summary>
    public void Write(string path, NavGraph graph, string inputsKey, JumpLinkFile file)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BuiltNavStore.Replace(path, Encode(graph, inputsKey, file));
    }
}
