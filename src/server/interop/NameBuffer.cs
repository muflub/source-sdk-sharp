using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The string-buffer convention for an ANSWER THAT IS AN IDENTIFIER — a decal
/// name, a model path — as opposed to <see cref="ListingBuffer"/>, which is the
/// convention for one a human reads.
///
/// <para>
/// <b>The only difference is what happens when it does not fit, and it is the
/// whole reason there are two.</b> <see cref="ListingBuffer"/> TRUNCATES, on a
/// rune boundary, because a server-browser description that is one word short
/// is a cosmetic loss. This one REFUSES, because half a path names a different
/// file and half a decal name names none — and the caller cannot tell either
/// from the real thing.
/// </para>
///
/// <para>
/// <b>That is not a new rule; it is Phase A8's, applied a second time.</b>
/// <c>CShellStringTables::GetString</c> refuses an entry that does not fit for
/// exactly this reason, and the ledger records it as one of that phase's four
/// design decisions. Written as its own type rather than as a flag on
/// <see cref="ListingBuffer"/> so that a caller picks a CONTRACT rather than
/// remembering to pass a boolean.
/// </para>
/// </summary>
public static class NameBuffer
{
    /// <summary>
    /// What <see cref="Write(string?, System.Span{byte})"/> returns when there
    /// is no answer, or when the answer would not fit. The native shim turns it
    /// back into whatever its own interface calls a miss — <c>NULL</c> for
    /// <c>GetRandomChunkModel</c>, <c>""</c> for <c>GetDecalNameForIndex</c>.
    /// </summary>
    public const int NoAnswer = -1;

    /// <summary>
    /// Writes <paramref name="value"/> into <paramref name="into"/> as
    /// NUL-terminated UTF-8, or refuses.
    /// </summary>
    /// <returns>
    /// The byte count written, NOT counting the terminator; zero for an empty
    /// answer; <see cref="NoAnswer"/> for no answer at all and for one that
    /// does not fit.
    /// </returns>
    public static int Write(string? value, Span<byte> into)
    {
        if (value is null || into.Length < 1)
            return NoAnswer;

        into[0] = 0;

        if (value.Length == 0)
            return 0;

        byte[] bytes = Encoding.UTF8.GetBytes(value);

        // REFUSED, not truncated. The buffer is left holding the terminator
        // written above, so a caller that ignores the return value reads an
        // empty string rather than a prefix that looks like a real name.
        if (bytes.Length > into.Length - 1)
            return NoAnswer;

        bytes.CopyTo(into);
        into[bytes.Length] = 0;

        return bytes.Length;
    }
}
