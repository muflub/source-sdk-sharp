using System;
using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The buffer convention for an answer that is an IDENTIFIER —
/// plan_gamedll_after.md Phase A3.
///
/// <para>
/// It exists beside <see cref="ListingBuffer"/> for one difference, and every
/// fact here is about that difference: a listing string that does not fit is
/// truncated on a rune boundary, and a name that does not fit is REFUSED,
/// because half a model path names a different file and half a decal name names
/// none.
/// </para>
/// </summary>
public class NameBufferTests
{
    private static string Read(byte[] buf, int written)
        => Encoding.UTF8.GetString(buf, 0, written);

    [Fact]
    public void ANameThatFitsIsWrittenAndTerminated()
    {
        var buf = new byte[16];
        int n = NameBuffer.Write("decals/a", buf);

        Assert.True(n == 8, "the byte count does not include the terminator");
        Assert.True(Read(buf, n) == "decals/a", "and the bytes are the name");
        Assert.True(buf[n] == 0, "which is NUL-terminated for the native caller");
    }

    [Fact]
    public void ANameThatDoesNotFitIsRefusedRatherThanTruncated()
    {
        var buf = new byte[8];
        int n = NameBuffer.Write("models/gibs/wood_gib01e.mdl", buf);

        Assert.True(n == NameBuffer.NoAnswer,
            "half a model path is a path to a different file, so it is not an answer");
    }

    [Fact]
    public void ARefusedWriteLeavesAnEmptyStringAndNotAPrefix()
    {
        var buf = new byte[8];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (byte)'x';

        NameBuffer.Write("models/gibs/wood_gib01e.mdl", buf);

        // A caller that ignores the return value reads an empty string rather
        // than eight characters that look like a real name.
        Assert.True(buf[0] == 0, "the terminator is written even on a refusal");
    }

    [Fact]
    public void AnExactFitIsAcceptedRatherThanBeingOffByOne()
    {
        // The room is length-1 because of the terminator, and this is the case
        // an off-by-one in either direction gets wrong -- one way it refuses a
        // name that fits, the other it writes a terminator past the end.
        var buf = new byte[9];
        int n = NameBuffer.Write("decals/a", buf);

        Assert.True(n == 8, "eight bytes and a terminator fill a nine-byte buffer exactly");
        Assert.True(buf[8] == 0, "and the terminator is the last byte");
    }

    [Fact]
    public void NoAnswerAndAnEmptyAnswerAreDifferent()
    {
        var buf = new byte[8];

        // Both are real answers on this ABI: null is "managed has nothing to
        // say, use what you had", and "" is decals_subrect.txt's own "do not
        // decal this surface". Collapsed, the second becomes the first and a
        // dash-material surface starts taking concrete decals.
        Assert.True(NameBuffer.Write(null, buf) == NameBuffer.NoAnswer, "null is no answer");
        Assert.True(NameBuffer.Write("", buf) == 0, "empty is an answer of no bytes");
    }

    [Fact]
    public void ABufferWithNoRoomEvenForATerminatorIsRefused()
    {
        Assert.True(NameBuffer.Write("a", Array.Empty<byte>()) == NameBuffer.NoAnswer,
            "there is no way to say anything in a zero-length buffer, including 'empty'");
    }

    [Fact]
    public void ItRefusesWhereListingBufferTruncates()
    {
        // The two conventions differ in exactly one place, and this is it. If
        // this fact ever passes for both, one of them has been changed into the
        // other and the reason for having two has gone.
        var name = new byte[8];
        var listing = new byte[8];

        Assert.True(NameBuffer.Write("aaaaaaaaaaaa", name) == NameBuffer.NoAnswer,
            "a name is refused");
        Assert.True(ListingBuffer.Write("aaaaaaaaaaaa", listing) == 7,
            "a listing string is truncated to what fits");
    }
}
