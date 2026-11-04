using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>Where a server command's parameters say more than their types: a range, a default.</summary>
public class ServerCommandLineTests
{
    private sealed class Sender : IUserMessageSender
    {
        public int Sent;

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true) => Sent++;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    public void Textmsg_refuses_a_destination_outside_one_to_four(string dest)
    {
        var sender = new Sender();
        var said = new List<string>();

        new TextMsgCommand(new RecordingLog(), sender).RunLine($"1 {dest} hello", said.Add);

        Assert.Equal(0, sender.Sent);
        Assert.Contains("'dest' is from 1 to 4", Assert.Single(said));
    }

    [Fact]
    public void Textmsg_sends_within_it()
    {
        var sender = new Sender();

        new TextMsgCommand(new RecordingLog(), sender).RunLine("1 4 #token one two");

        Assert.Equal(1, sender.Sent);
    }

    [Theory]
    [InlineData("1 team", "shown")]
    [InlineData("1 info 1", "shown")]
    [InlineData("1 info 0", "hidden")]
    public void Showpanel_shows_unless_told_not_to(string line, string word)
    {
        var log = new RecordingLog();

        new ShowPanelCommand(log, new Sender()).RunLine(line);

        Assert.Contains(word, log.Text);
    }

    [Fact]
    public void Showpanel_refuses_player_zero()
    {
        var sender = new Sender();
        var said = new List<string>();

        new ShowPanelCommand(new RecordingLog(), sender).RunLine("0 team", said.Add);

        Assert.Equal(0, sender.Sent);
        Assert.Contains("'player' is from 1", Assert.Single(said));
    }
}
