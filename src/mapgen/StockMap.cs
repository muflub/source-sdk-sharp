using System.Text;

namespace SourceSharp.MapGen;

/// <summary>
/// ss_sandbox as stock hl2mp can load it: the same compiled map, with the
/// classnames only SourceSharp registers renamed to what stock has.
///
/// <para>
/// tools/stock-ab runs stock hl2mp and the test mod side by side on this map
/// and compares what they show. Stock refuses an unknown classname ("Attempted
/// to create unknown entity type ss_barrel!"), so the comparison needs a map
/// whose every entity stock can spawn. Only the entity lump changes: the
/// geometry, the lighting and the static props are the same bytes the test mod
/// loads, so a difference in a screenshot is a difference in the game, not in
/// the map.
/// </para>
/// </summary>
public static class StockMap
{
    /// <summary>
    /// SourceSharp-only classname to the stock hl2mp class that stands in for it.
    ///
    /// <para>
    /// ss_barrel is a prop_physics with a client half of its own (TestBarrel),
    /// so prop_physics draws and moves the way it does. ss_te_tester and
    /// game_mode_switch have no stock counterpart and do nothing until an input
    /// reaches them (a reset button, a SwitchTo); info_target keeps their
    /// targetnames, so the inputs aimed at them still find an entity.
    /// func_friction and game_player_hurt are absent from stock hl2mp's
    /// server.so too, but they are stock code (HL1_DLL-only, and commented out
    /// in maprules.cpp) that tools/stock-ab's patch series compiles in, so they
    /// keep their names.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Renames { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ss_barrel"] = "prop_physics",
            ["ss_te_tester"] = "info_target",
            ["game_mode_switch"] = "info_target",
        };

    private const int VbspIdent = 'V' | ('B' << 8) | ('S' << 16) | ('P' << 24);

    private const int HeaderLumps = 64;

    /// <summary>
    /// The lump with each <c>"classname" "X"</c> pair whose X is a key of
    /// <paramref name="renames"/> rewritten. Every other token, the
    /// <c>te</c> keyvalue that names a classname-like string among them, is
    /// left as it was.
    /// </summary>
    public static string Rename(string lump, IReadOnlyDictionary<string, string> renames, out int count)
    {
        var text = new StringBuilder(lump.Length + 256);
        count = 0;
        int i = 0;
        bool afterClassnameKey = false;
        bool isKey = true;

        while (i < lump.Length)
        {
            char c = lump[i];

            if (c == '{' || c == '}')
            {
                isKey = true;
                afterClassnameKey = false;
                text.Append(c);
                i++;
                continue;
            }

            if (c != '"')
            {
                text.Append(c);
                i++;
                continue;
            }

            int end = lump.IndexOf('"', i + 1);
            if (end < 0)
                throw new InvalidDataException($"unterminated token at offset {i} of the entity lump");

            string token = lump.Substring(i + 1, end - i - 1);

            if (!isKey && afterClassnameKey && renames.TryGetValue(token, out string? stock))
            {
                token = stock;
                count++;
            }

            afterClassnameKey = isKey && token.Equals("classname", StringComparison.OrdinalIgnoreCase);
            isKey = !isKey;
            text.Append('"').Append(token).Append('"');
            i = end + 1;
        }

        return text.ToString();
    }

    /// <summary>
    /// <paramref name="bsp"/> with its entity lump replaced by
    /// <paramref name="lump"/>. The new lump goes at the end of the file,
    /// 4-byte aligned, and lump 0's descriptor points at it; every other byte
    /// is the input's, so the world is unchanged.
    /// </summary>
    public static byte[] WithEntityLump(byte[] bsp, string lump)
    {
        if (bsp.Length < 8 + (HeaderLumps * 16) || BitConverter.ToInt32(bsp, 0) != VbspIdent)
            throw new InvalidDataException("not a VBSP file");

        if (BitConverter.ToInt32(bsp, 8 + 12) != 0)
            throw new InvalidDataException("the entity lump is compressed; only plain lumps are rewritten");

        byte[] text = Encoding.Latin1.GetBytes(lump + "\0");
        int offset = (bsp.Length + 3) & ~3;

        var output = new byte[offset + text.Length];
        Buffer.BlockCopy(bsp, 0, output, 0, bsp.Length);
        Buffer.BlockCopy(text, 0, output, offset, text.Length);

        BitConverter.TryWriteBytes(output.AsSpan(8, 4), offset);
        BitConverter.TryWriteBytes(output.AsSpan(12, 4), text.Length);
        return output;
    }

    /// <summary>Write the stock copy of <paramref name="inputBsp"/>; returns how many classnames were renamed.</summary>
    public static int Write(string inputBsp, string outputBsp)
    {
        string lump = BspEntities.ReadLumpText(inputBsp);
        string renamed = Rename(lump, Renames, out int count);
        string temporary = outputBsp + ".tmp";

        File.WriteAllBytes(temporary, WithEntityLump(File.ReadAllBytes(inputBsp), renamed));
        File.Move(temporary, outputBsp, overwrite: true);
        return count;
    }
}
