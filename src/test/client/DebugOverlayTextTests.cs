using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The debug overlay's text as stock's CDebugOverlay::Paint walked it
/// (vgui_debugoverlaypanel.cpp): every entry to the list's end, an entry with
/// no text or off the screen skipped, 13 pixels a line offset, the colour, and
/// the dead ones cleared after the walk.
/// </summary>
public class DebugOverlayTextTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 1f;
    }

    private sealed class Overlay : IClientDebugOverlay
    {
        public (float X, float Y, int Line, uint Rgba, string Text, int Result)[] Entries = [];
        public int Cleared, Reads;

        public int Text(int index, out float x, out float y, out int line, out uint rgba, Span<byte> text)
        {
            Reads++;
            (x, y, line, rgba) = (0, 0, 0, 0);
            if (index >= Entries.Length)
                return -1;
            var e = Entries[index];
            (x, y, line, rgba) = (e.X, e.Y, e.Line, e.Rgba);
            int n = System.Text.Encoding.UTF8.GetBytes(e.Text, text);
            text[n] = 0;
            return e.Result;
        }

        public void ClearDead() => Cleared++;
    }

    [Fact]
    public void Every_drawn_entry_is_a_line_with_its_offset_and_colour()
    {
        var o = new Overlay
        {
            Entries =
            [
                (100, 200, 0, 0xFF0000FF, "first", 1),
                (0, 0, 0, 0, "", 0),                   // off the screen: skipped
                (100, 200, 2, 0x00FF00FF, "third", 1),
            ],
        };
        var t = new DebugOverlayText(o, new Clock());
        Assert.Equal(2, t.Count);
        Assert.Equal((100f, 200f, "first", (byte)255, (byte)0), (t.Line(0).X, t.Line(0).Y, t.Line(0).Text, t.Line(0).Colour.R, t.Line(0).Colour.G));
        Assert.Equal((226f, "third"), (t.Line(1).Y, t.Line(1).Text));      // 200 + 2 lines of 13
        Assert.Equal(1, o.Cleared);
    }

    [Fact]
    public void The_list_is_read_once_a_tick_and_a_steady_line_keeps_its_string()
    {
        var o = new Overlay { Entries = [(1, 1, 0, 0xFFFFFFFF, "steady", 1)] };
        var clock = new Clock();
        var t = new DebugOverlayText(o, clock);
        _ = t.Count;
        string first = t.Line(0).Text;
        int reads = o.Reads;
        _ = t.Count;
        Assert.Equal(reads, o.Reads);              // same tick: not read again
        clock.Now = 2f;
        _ = t.Count;
        Assert.Same(first, t.Line(0).Text);        // same bytes: the same string
    }

    [Fact]
    public void An_empty_list_is_no_lines_and_still_clears()
    {
        var o = new Overlay();
        var t = new DebugOverlayText(o, new Clock());
        Assert.Equal(0, t.Count);
        Assert.Equal(1, o.Cleared);
    }
}
