using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The string-buffer convention: the managed side writes into a native
/// <c>char *pOut, int nOutSize</c> and the shell returns its own member buffer,
/// because all three of the shell's string answers hand the engine a pointer it
/// reads after the call.
/// </summary>
public class ListingBufferTests
{
    private static string TextIn(Span<byte> buffer)
    {
        int end = buffer.IndexOf((byte)0);
        return Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
    }

    [Fact]
    public void AnAnswerThatFitsIsWrittenWhole()
    {
        Span<byte> buffer = stackalloc byte[128];
        ListingBuffer.Write("Half-Life 2 Deathmatch", buffer);

        Assert.True(TextIn(buffer) == "Half-Life 2 Deathmatch", "the description reaches the buffer");
    }

    [Fact]
    public void AnAnswerThatFitsReportsItsLength()
    {
        Span<byte> buffer = stackalloc byte[128];

        Assert.True(ListingBuffer.Write("abc", buffer) == 3, "and the count excludes the terminator");
    }

    [Fact]
    public void AWrittenAnswerIsNulTerminated()
    {
        Span<byte> buffer = stackalloc byte[8];
        buffer.Fill(0xFF);
        ListingBuffer.Write("abc", buffer);

        Assert.True(buffer[3] == 0, "the engine reads a C string, so there is a terminator after it");
    }

    /// <summary>
    /// Null and empty are different answers and must stay different: a null map
    /// override means "use the real map name" and reaches native as a null
    /// pointer, while an empty one means "show nothing".
    /// </summary>
    [Fact]
    public void NoAnswerIsNotAnEmptyAnswer()
    {
        Span<byte> buffer = stackalloc byte[8];

        Assert.True(ListingBuffer.Write(null, buffer) == ListingBuffer.NoAnswer,
            "a null answer says so, so the shell can return a null pointer");
    }

    [Fact]
    public void AnEmptyAnswerIsAnAnswer()
    {
        Span<byte> buffer = stackalloc byte[8];
        buffer.Fill(0xFF);

        Assert.True(ListingBuffer.Write("", buffer) == 0 && buffer[0] == 0,
            "an empty answer writes an empty string rather than nothing at all");
    }

    [Fact]
    public void NothingIsWrittenForANullAnswer()
    {
        Span<byte> buffer = stackalloc byte[8];
        buffer.Fill(0xFF);
        ListingBuffer.Write(null, buffer);

        Assert.True(buffer[0] == 0xFF, "a null answer leaves the caller's buffer alone");
    }

    [Fact]
    public void ABufferWithNoRoomEvenForATerminatorIsRefused()
    {
        Assert.True(ListingBuffer.Write("abc", Span<byte>.Empty) == ListingBuffer.NoAnswer,
            "a zero-length buffer cannot hold even an empty string, so it is refused rather than half-written");
    }

    [Fact]
    public void AnAnswerTooLongForTheBufferIsTruncated()
    {
        Span<byte> buffer = stackalloc byte[4];

        Assert.True(ListingBuffer.Write("abcdef", buffer) == 3, "three bytes and a terminator is all four hold");
    }

    [Fact]
    public void ATruncatedAnswerIsStillTerminated()
    {
        Span<byte> buffer = stackalloc byte[4];
        ListingBuffer.Write("abcdef", buffer);

        Assert.True(TextIn(buffer) == "abc", "and what is there is a valid C string");
    }

    [Fact]
    public void AnAnswerThatExactlyFillsTheBufferIsNotTrimmedFurther()
    {
        Span<byte> buffer = stackalloc byte[4];

        Assert.True(ListingBuffer.Write("abc", buffer) == 3,
            "three bytes and a terminator fit in four, and nothing is given back");
    }

    /// <summary>
    /// <b>Truncation stops on a rune boundary, which the loop this replaces did
    /// not.</b>
    ///
    /// <para>
    /// <c>ServerGetGameDescription</c> copied <c>min(len, size-1)</c> bytes, so a
    /// description whose last byte landed inside a multi-byte sequence handed the
    /// engine an invalid UTF-8 tail. "aé" is three bytes — <c>61 C3 A9</c> — so a
    /// four-byte buffer has room for three and the naive cut splits the é.
    /// </para>
    /// </summary>
    [Fact]
    public void TruncationDoesNotSplitARune()
    {
        Span<byte> buffer = stackalloc byte[3];

        int n = ListingBuffer.Write("aé", buffer);

        Assert.True(n == 1 && TextIn(buffer) == "a",
            "the cut backs up to the last complete rune rather than leaving half of one");
    }

    [Fact]
    public void ARuneThatFitsExactlyIsKept()
    {
        Span<byte> buffer = stackalloc byte[4];

        Assert.True(ListingBuffer.Write("aé", buffer) == 3 && TextIn(buffer) == "aé",
            "a multi-byte rune with room for all of it is written whole");
    }

    [Fact]
    public void AnAnswerWhoseFirstRuneDoesNotFitWritesNothing()
    {
        Span<byte> buffer = stackalloc byte[2];

        Assert.True(ListingBuffer.Write("é", buffer) == 0 && TextIn(buffer).Length == 0,
            "a buffer with room for one byte cannot hold a two-byte rune, and half of one is not an answer");
    }
}
