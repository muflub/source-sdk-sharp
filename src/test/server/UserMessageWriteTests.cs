using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The writer against the reader, which is the only check that matters.
///
/// A user message's layout is written down in exactly one place — the
/// OnReceived that reads it — and a sender mirrors that call for call. Nothing
/// verifies the mirroring except a round trip, and getting one field out of
/// place does not fail: every field after it is garbage, arrives as garbage, and
/// looks like a client bug.
/// </summary>
public class UserMessageWriteTests
{

    [Fact]
    public void RoundTrip()
    {
        Span<byte> buffer = stackalloc byte[128];
        var w = new UserMessageWriter(buffer);

        w.WriteByte(200);
        w.WriteShort(-1234);
        w.WriteInt(70000);
        w.WriteFloat(3.5f);
        w.WriteString("hello");

        Assert.True(!w.Overran, "the writer did not overrun");

        var r = new UserMessageReader(buffer[..w.Length]);

        Assert.True(r.ReadByte() == 200, "a byte reads back");
        Assert.True(r.ReadShort() == -1234, "a short reads back, sign and all");
        Assert.True(r.ReadInt() == 70000, "an int reads back past the short range");
        Assert.True(r.ReadFloat() == 3.5f, "a float reads back");
        Assert.True(r.ReadString() == "hello", "a string reads back");
        Assert.True(r.Remaining == 0 && !r.Overran, "and nothing is left over");
    }

    [Fact]
    public void Overrun()
    {
        Span<byte> tiny = stackalloc byte[4];
        var w = new UserMessageWriter(tiny);

        w.WriteFloat(1f);
        Assert.True(!w.Overran && w.Length == 4, "a write that exactly fills is not an overrun");

        w.WriteByte(1);
        Assert.True(w.Overran, "the write past the end is flagged");

        // The buffer must not be partly overwritten: a caller that ignored the
        // flag and sent it would send a valid-looking short message rather than
        // a corrupt one, which is the failure that reaches a screen.
        Assert.True(w.Length == 4, "and nothing was written past the end");

        // A string that does not fit is dropped whole rather than truncated —
        // a message cut mid-field has the reader consuming what follows as text.
        Span<byte> small = stackalloc byte[4];
        var s = new UserMessageWriter(small);
        s.WriteString("a string much longer than four bytes");

        Assert.True(s.Overran && s.Length == 0, "a string that does not fit writes nothing");
    }

    [Fact]
    public void HudMessageLayout()
    {
        // The exact field order UTIL_HudMessage writes, read back by the exact
        // code the client runs. This is the pairing game_text depends on.
        Span<byte> buffer = stackalloc byte[256];
        var w = new UserMessageWriter(buffer);

        w.WriteByte(3);
        w.WriteFloat(-1f);
        w.WriteFloat(0.75f);
        w.WriteColor(100, 150, 200, 255);
        w.WriteColor(240, 110, 0, 255);
        w.WriteByte(0);
        w.WriteFloat(1.5f);
        w.WriteFloat(0.5f);
        w.WriteFloat(1.2f);
        w.WriteFloat(0.25f);
        w.WriteString("STATION ONE");

        var r = new UserMessageReader(buffer[..w.Length]);

        Assert.True(r.ReadByte() == 3, "channel arrives first");
        Assert.True(r.ReadFloat() == -1f, "x is next and keeps its negative");
        Assert.True(r.ReadFloat() == 0.75f, "then y");

        bool color1 = r.ReadByte() == 100 && r.ReadByte() == 150
                   && r.ReadByte() == 200 && r.ReadByte() == 255;
        Assert.True(color1, "then four bytes of colour");

        bool color2 = r.ReadByte() == 240 && r.ReadByte() == 110
                   && r.ReadByte() == 0 && r.ReadByte() == 255;
        Assert.True(color2, "then four more");

        Assert.True(r.ReadByte() == 0, "then the effect");
        Assert.True(r.ReadFloat() == 1.5f, "then fade in");
        Assert.True(r.ReadFloat() == 0.5f, "then fade out");
        Assert.True(r.ReadFloat() == 1.2f, "then hold");
        Assert.True(r.ReadFloat() == 0.25f, "then the effect time");
        Assert.True(r.ReadString() == "STATION ONE", "and the text last");
        Assert.True(r.Remaining == 0 && !r.Overran, "with nothing left over");
    }

    [Fact]
    public void Colors()
    {
        var full = GameText.ParseColor("10 20 30 40", 1, 2, 3, 4);
        Assert.True(full == ((byte)10, (byte)20, (byte)30, (byte)40), "four numbers are read as four");

        // Per-component fallback: a map that wrote three numbers meant three,
        // and the alpha is the one most often left off. Taking the whole key as
        // malformed would throw away the colour it did specify.
        var noAlpha = GameText.ParseColor("10 20 30", 1, 2, 3, 255);
        Assert.True(noAlpha == ((byte)10, (byte)20, (byte)30, (byte)255), "a missing alpha falls back on its own");

        var nonsense = GameText.ParseColor("", 7, 8, 9, 10);
        Assert.True(nonsense == ((byte)7, (byte)8, (byte)9, (byte)10), "an empty key keeps every default");

        // Clamped, not wrapped: a map writing 300 means "as bright as it goes",
        // and a cast alone would make it 44.
        var over = GameText.ParseColor("300 -5 0 255", 0, 0, 0, 0);
        Assert.True(over == ((byte)255, (byte)0, (byte)0, (byte)255), "out of range clamps rather than wrapping");
    }
}
