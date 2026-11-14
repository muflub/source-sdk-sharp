using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A refusal told to the one player who asked: their console and their centre print, nobody else's.</summary>
public class PlayerNoticesTests
{
    private sealed class Sender : IUserMessageSender
    {
        public readonly List<(string Key, byte[] Bytes, int Player)> Sent = [];

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true) => Sent.Add((key, payload.ToArray(), player));
    }

    /// <summary>IEnginePlayers answering only GetCommandClient; anything else is a failure.</summary>
    public class Issuer : DispatchProxy
    {
        public EntityHandle Client = EntityHandle.None;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IEnginePlayers.GetCommandClient)
                ? Client
                : throw new InvalidOperationException($"PlayerNotices asked IEnginePlayers.{method?.Name}");
    }

    private readonly Sender _sender = new();
    private readonly IEnginePlayers _players = DispatchProxy.Create<IEnginePlayers, Issuer>();

    private PlayerNotices Notices() => new(_sender, _players);

    private static TextMsg Read(byte[] bytes)
    {
        var reader = new UserMessageReader(bytes);
        return TextMsg.Read(ref reader);
    }

    [Fact]
    public void A_refusal_goes_to_that_player_s_console_and_centre_print_only()
    {
        Notices().Refused(3, "ai_bot_add refused: no navigation for dm_x\n");

        Assert.Equal(2, _sender.Sent.Count);
        Assert.All(_sender.Sent, s => Assert.Equal(3, s.Player));
        Assert.All(_sender.Sent, s => Assert.Equal(UserMessageKey.Of(typeof(TextMsgMessage)), s.Key));
        var printed = _sender.Sent.Select(s => Read(s.Bytes)).ToList();
        Assert.Equal([PlayerNotices.PrintConsole, PlayerNotices.PrintCenter], printed.Select(m => m.Destination));
        Assert.All(printed, m => Assert.Equal("ai_bot_add refused: no navigation for dm_x", m.Text));
    }

    [Fact]
    public void The_server_console_s_refusal_reaches_no_player()
    {
        Notices().Refused(0, "refused");

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public void The_issuer_is_the_slot_of_the_player_running_the_command()
    {
        ((Issuer)(object)_players).Client = new EntityHandle(0x0003_0004u);

        Assert.Equal(4, Notices().CommandIssuer());
    }

    [Fact]
    public void A_command_from_the_server_console_has_no_issuer()
        => Assert.Equal(0, Notices().CommandIssuer());
}
