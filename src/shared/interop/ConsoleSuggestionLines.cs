using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// A command's suggestions as they cross between managed and native: one buffer of
/// newline-separated UTF-8 lines, NUL at the end (console_suggestions.h is the native half).
/// </summary>
public static unsafe class ConsoleSuggestionLines
{
    /// <summary>Writes whole lines, as many as fit, and answers how many.</summary>
    public static int Write(IReadOnlyList<string> lines, byte* output, int outputSize)
    {
        if (output == null || outputSize <= 0)
            return 0;

        output[0] = 0;
        int used = 0;
        int written = 0;
        foreach (string line in lines)
        {
            if (line.Length == 0 || line.Contains('\n', StringComparison.Ordinal))
                continue;

            byte[] bytes = Encoding.UTF8.GetBytes(line);
            if (used + bytes.Length + 2 > outputSize)
                break;

            bytes.CopyTo(new Span<byte>(output + used, bytes.Length));
            used += bytes.Length;
            output[used++] = (byte)'\n';
            output[used] = 0;
            written++;
        }
        return written;
    }

    /// <summary>The lines of a buffer <see cref="Write"/> or native filled.</summary>
    public static IReadOnlyList<string> Read(byte* buffer)
    {
        string? text = buffer == null ? null : Marshal.PtrToStringUTF8((nint)buffer);
        return string.IsNullOrEmpty(text) ? [] : text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
