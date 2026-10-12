using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The MOTD's content as stock's CTextWindow takes it from VGUIMenu "info"
/// (game_controls/vguitextwindow.cpp): type 0 the text, type 1 the InfoPanel
/// string table's user data for the entry msg names (ShowIndex), nothing
/// shown for an entry with no data.
/// </summary>
public class MotdSourceTests
{
    private static KeyValuePair<string, string>[] Info(string type, string msg)
        => [new("title", "Lockdown"), new("type", type), new("msg", msg)];

    [Fact]
    public void Type_1_reads_the_infopanel_entry_msg_names()
    {
        string? asked = null;
        var motd = new MotdUi(entry => { asked = entry; return "Welcome to the lockdown.\nNo camping."; });
        motd.SetData(Info("1", "motd"));
        Assert.Equal(("motd", "Lockdown", "Welcome to the lockdown.\nNo camping."), (asked, motd.Title, motd.Text));
    }

    [Fact]
    public void An_html_entry_shows_motd_text_as_stock_does_with_html_disallowed()
    {
        var table = new Dictionary<string, string>
        {
            ["motd"] = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\"><html>...</html>",
            ["motd_text"] = "HL2MP MOTD\n\nYou are playing Half-Life 2 Deathmatch",
        };
        var motd = new MotdUi(e => table.GetValueOrDefault(e));
        motd.SetData(Info("1", "motd"));
        Assert.Equal(table["motd_text"], motd.Text);
    }

    [Fact]
    public void A_url_entry_shows_motd_text_too()
    {
        var motd = new MotdUi(e => e == "motd" ? "https://example.org/motd" : "plain");
        motd.SetData(Info("1", "motd"));
        Assert.Equal("plain", motd.Text);
    }

    [Fact]
    public void Crlf_line_ends_are_line_ends()
    {
        var motd = new MotdUi(_ => "HL2MP MOTD\r\n\r\nYou are playing");
        motd.SetData(Info("1", "motd"));
        Assert.Equal("HL2MP MOTD\n\nYou are playing", motd.Text);
    }

    [Fact]
    public void Type_1_with_no_entry_shows_nothing()
    {
        var motd = new MotdUi(_ => null);
        motd.SetData(Info("1", "motd"));
        Assert.Equal("", motd.Text);
    }

    [Fact]
    public void Type_0_is_the_text_itself_and_reads_no_table()
    {
        bool asked = false;
        var motd = new MotdUi(_ => { asked = true; return "table"; });
        motd.SetData(Info("0", "inline text"));
        Assert.Equal(("inline text", false), (motd.Text, asked));
    }
}
