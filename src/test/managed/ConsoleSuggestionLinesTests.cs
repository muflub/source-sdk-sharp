using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A command's suggestions in the one buffer they cross the boundary in.</summary>
public unsafe class ConsoleSuggestionLinesTests
{
    private static (int Written, IReadOnlyList<string> Read) RoundTrip(IReadOnlyList<string> lines, int size)
    {
        byte* buffer = stackalloc byte[size];
        int written = ConsoleSuggestionLines.Write(lines, buffer, size);
        return (written, ConsoleSuggestionLines.Read(buffer));
    }

    [Fact]
    public void Lines_come_back_as_they_went_in_with_their_count()
    {
        var (written, read) = RoundTrip(["map dm_lockdown", "map dm_ünder"], 64);

        Assert.Equal(2, written);
        Assert.Equal(["map dm_lockdown", "map dm_ünder"], read);
    }

    [Fact]
    public void A_line_that_does_not_fit_is_left_out_whole()
    {
        var (written, read) = RoundTrip(["0123456789", "abcdefghij"], 16);

        Assert.Equal(1, written);
        Assert.Equal(["0123456789"], read);
    }

    [Fact]
    public void A_line_holding_a_newline_or_nothing_is_dropped()
    {
        var (written, read) = RoundTrip(["bad\nline", "", "good"], 64);

        Assert.Equal(1, written);
        Assert.Equal(["good"], read);
    }

    [Fact]
    public void No_lines_read_back_as_none()
    {
        var (written, read) = RoundTrip([], 8);

        Assert.Equal(0, written);
        Assert.Empty(read);
    }

    [Fact]
    public void A_null_buffer_takes_nothing_and_reads_as_none()
    {
        Assert.Equal(0, ConsoleSuggestionLines.Write(["x"], null, 8));
        Assert.Empty(ConsoleSuggestionLines.Read(null));
    }
}
