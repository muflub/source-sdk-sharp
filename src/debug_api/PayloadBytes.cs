using System.Globalization;
using System.Text;

using Debug.Api.Server.Interfaces;

namespace Debug.Api;

/// <summary>
/// The byte utilities the payload view needs (group G5): pure functions over
/// bytes, copied rather than reached for so the dev API names no Abi type.
/// PayloadBytesTests holds each equal to the game's own (NetSchema.Hex,
/// ChangedRuns and Touches).
/// </summary>
internal static class PayloadBytes
{
    /// <summary>Lower-case hex, two digits a byte.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);

        foreach (byte b in bytes)
            text.Append(b.ToString("x2", CultureInfo.InvariantCulture));

        return text.ToString();
    }

    /// <summary>The runs of bytes that differ, over the length both share.</summary>
    public static IReadOnlyList<(int Offset, int Length)> ChangedRuns(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current)
    {
        var runs = new List<(int, int)>();
        int shared = Math.Min(previous.Length, current.Length);
        int start = -1;

        for (int i = 0; i < shared; i++)
        {
            bool differs = previous[i] != current[i];

            if (differs && start < 0)
                start = i;
            else if (!differs && start >= 0)
            {
                runs.Add((start, i - start));
                start = -1;
            }
        }

        if (start >= 0)
            runs.Add((start, shared - start));

        return runs;
    }

    /// <summary>Whether any run overlaps the field's bytes.</summary>
    public static bool Touches(DebugNetField field, IReadOnlyList<(int Offset, int Length)> runs)
    {
        foreach (var (offset, length) in runs)
        {
            if (offset < field.Offset + field.Bytes && field.Offset < offset + length)
                return true;
        }

        return false;
    }
}
