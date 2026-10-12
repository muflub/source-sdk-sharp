using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The one implementation of the string-buffer convention: managed code writes
/// into a native <c>char *pOut, int nOutSize</c>, and the caller returns its own
/// buffer to the engine.
///
/// <para>
/// <b>Why there is a convention at all.</b> All three of the shell's string
/// answers — the game description, the browser map override, the browser game
/// data — hand the engine a <c>const char *</c> that it reads <i>after</i> the
/// call has returned. A managed string's bytes are not that: they are on the GC
/// heap, they move, and marshalling one produces storage nobody frees. So the
/// direction is inverted — the native side owns a member buffer, passes it down,
/// and the managed side fills it.
/// </para>
///
/// <para>
/// <b>Truncation stops on a rune boundary, which stock's does not.</b>
/// <c>ServerGetGameDescription</c> copies <c>min(len, size-1)</c> bytes, so a
/// description whose 128th byte lands mid-sequence hands the engine an invalid
/// UTF-8 tail. Truncating here backs up to the last complete rune instead: a
/// short name is a cosmetic loss, a malformed one is a rendering bug in the
/// server browser that nothing in this process can see.
/// </para>
///
/// <para>
/// <b>Null and empty are different answers and stay different.</b> A null map
/// override means "the engine should use the real map name" and must reach
/// native as a null pointer; an empty string means "show nothing". Collapsing
/// them would make an override of "" silently show the map name. So
/// <see cref="Write(string?, System.Span{byte})"/> returns
/// <see cref="NoAnswer"/> for null and <c>0</c> for empty.
/// </para>
/// </summary>
public static class ListingBuffer
{
    /// <summary>
    /// What <see cref="Write(string?, System.Span{byte})"/> returns when there is
    /// no answer at all, as distinct from an empty one. The caller returns a null
    /// pointer to the engine.
    /// </summary>
    public const int NoAnswer = -1;

    /// <summary>
    /// Writes <paramref name="value"/> into <paramref name="into"/> as
    /// NUL-terminated UTF-8, truncating on a rune boundary if it does not fit.
    /// </summary>
    /// <returns>
    /// The number of bytes written, NOT counting the terminator; zero for an
    /// empty answer; <see cref="NoAnswer"/> when there is no answer or the buffer
    /// has no room even for the terminator.
    /// </returns>
    public static int Write(string? value, Span<byte> into)
    {
        if (value is null)
            return NoAnswer;

        // A zero-length buffer cannot even hold the terminator, so there is no
        // way to say "empty" in it. Refusing is the only honest answer: writing
        // nothing and reporting 0 would leave the caller returning whatever the
        // buffer held before.
        if (into.Length < 1)
            return NoAnswer;

        into[0] = 0;

        if (value.Length == 0)
            return 0;

        int room = into.Length - 1;
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int n = bytes.Length;

        if (n > room)
        {
            n = room;

            // Back up while the FIRST EXCLUDED byte is a continuation byte
            // (10xxxxxx), which is what "the cut landed inside a rune" looks
            // like. The loop stops with bytes[n] either ASCII or a lead byte —
            // that is, with a rune starting exactly at n — so bytes[0..n) is a
            // run of complete runes.
            //
            // It does NOT then step back one more. After the loop the boundary
            // is already correct, and a second decrement would drop a whole
            // rune that fitted — which is the shape this had when first
            // written, and it truncated every ASCII string one byte short of
            // the buffer.
            while (n > 0 && (bytes[n] & 0xC0) == 0x80)
                n--;
        }

        bytes.AsSpan(0, n).CopyTo(into);
        into[n] = 0;

        return n;
    }
}
