using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The centre print as stock's CCenterStringLabel keeps it
/// (vgui_centerstringpanel.cpp): shown while m_flCentertimeOff (now +
/// scr_centertime, "2") is ahead of curtime, white unless coloured, Clear takes it down.
/// </summary>
public class CentrePrintTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    [Fact]
    public void A_print_shows_for_two_seconds()
    {
        var clock = new Clock();
        var c = new CentrePrint(clock);
        c.Print("Round over");
        clock.Now = 11.9f;
        Assert.Equal(("Round over", 1f), (c.CenterPrint, c.CenterPrintAlpha));
        clock.Now = 12.0f;
        Assert.Equal(("", 0f), (c.CenterPrint, c.CenterPrintAlpha));
    }

    [Fact]
    public void Clear_takes_it_down()
    {
        var c = new CentrePrint(new Clock());
        c.Print("x");
        c.Clear();
        Assert.Equal("", c.CenterPrint);
    }

    [Fact]
    public void A_colour_print_keeps_its_colour_and_a_plain_one_is_white()
    {
        var c = new CentrePrint(new Clock());
        c.Print("red", 255, 0, 0, 255);
        Assert.Equal(0xFF0000FFu, c.Colour);
        c.Print("plain");
        Assert.Equal(0xFFFFFFFFu, c.Colour);
    }

    [Fact]
    public void Nothing_printed_shows_nothing()
        => Assert.Equal("", new CentrePrint(new Clock()).CenterPrint);

    [Fact]
    public void The_declared_time_is_stocks_two_seconds()
        => Assert.Equal("2", typeof(ScrCenterTimeConVar).GetCustomAttributes(typeof(ConVarAttribute), false)
                                 .Cast<ConVarAttribute>().Single().DefaultValue);
}
