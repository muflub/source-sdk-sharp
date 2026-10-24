using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client mode's key decision as stock's ClientModeShared::KeyInput and
/// HandleSpectatorKeyInput made it (clientmode_shared.cpp): the console first,
/// the chat keys, an observer's keys, and no decision for the rest.
/// </summary>
public class ClientKeyInputTests
{
    private sealed class Chat : IChatOpener
    {
        public readonly List<bool> Opened = [];

        public void Open(bool teamOnly) => Opened.Add(teamOnly);
    }

    private sealed class Observer : IObserverSource
    {
        public SpectatorMode Mode { get; set; }
        public string MapName => "";
    }

    private static (ClientKeyInput Keys, Chat Chat, Observer Observer, List<string> Sent) Make()
    {
        var chat = new Chat();
        var observer = new Observer();
        var sent = new List<string>();
        return (new ClientKeyInput(chat, observer, new ChatFeedTests.RecordingEngineClient(sent)), chat, observer, sent);
    }

    [Fact]
    public void With_the_console_up_the_engine_runs_every_binding()
    {
        var (k, chat, o, sent) = Make();
        o.Mode = SpectatorMode.Chase;
        Assert.Equal(1, k.KeyInput(true, ClientKeyBinding.MessageMode, consoleVisible: true, maxClients: 8));
        Assert.Equal(1, k.KeyInput(true, ClientKeyBinding.Attack, consoleVisible: true, maxClients: 8));
        Assert.Empty(chat.Opened);
        Assert.Empty(sent);
    }

    [Fact]
    public void The_chat_keys_open_the_chat_on_the_press_and_are_used_both_ways()
    {
        var (k, chat, _, _) = Make();
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.MessageMode, false, 8));
        Assert.Equal(0, k.KeyInput(false, ClientKeyBinding.MessageMode, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.SayTeam, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Say, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.MessageMode2, false, 8));
        Assert.Equal([false, true, false, true], chat.Opened);
    }

    [Fact]
    public void With_one_player_the_chat_keys_are_used_and_open_nothing()
    {
        var (k, chat, _, _) = Make();
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.MessageMode, false, 1));
        Assert.Empty(chat.Opened);
    }

    [Fact]
    public void Party_chat_is_used_and_does_nothing()
    {
        var (k, chat, _, _) = Make();
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.MessageMode3, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.SayParty, false, 8));
        Assert.Empty(chat.Opened);
    }

    [Fact]
    public void An_observers_keys_run_the_spectator_commands_on_the_press()
    {
        var (k, _, o, sent) = Make();
        o.Mode = SpectatorMode.Chase;
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Attack, false, 8));
        Assert.Equal(-1, k.KeyInput(false, ClientKeyBinding.Attack, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Attack2, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Jump, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Duck, false, 8));
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Strafe, false, 8));
        Assert.Equal(-1, k.KeyInput(true, ClientKeyBinding.Other, false, 8));
        Assert.Equal(["Next", "Prev", "Mode"], sent);
    }

    [Theory]
    [InlineData(SpectatorMode.None)]
    [InlineData(SpectatorMode.DeathCam)]
    public void A_player_or_the_death_cam_gets_no_spectator_keys(SpectatorMode mode)
    {
        var (k, _, o, sent) = Make();
        o.Mode = mode;
        Assert.Equal(-1, k.KeyInput(true, ClientKeyBinding.Attack, false, 8));
        Assert.Equal(-1, k.KeyInput(true, ClientKeyBinding.Duck, false, 8));
        Assert.Empty(sent);
    }

    [Fact]
    public void The_freeze_cam_is_past_the_death_cam()
    {
        var (k, _, o, sent) = Make();
        o.Mode = SpectatorMode.FreezeCam;
        Assert.Equal(0, k.KeyInput(true, ClientKeyBinding.Jump, false, 8));
        Assert.Equal(["Mode"], sent);
    }

    [Theory]
    [InlineData("messagemode", ClientKeyBinding.MessageMode)]
    [InlineData("say", ClientKeyBinding.Say)]
    [InlineData("messagemode2", ClientKeyBinding.MessageMode2)]
    [InlineData("say_team", ClientKeyBinding.SayTeam)]
    [InlineData("messagemode3", ClientKeyBinding.MessageMode3)]
    [InlineData("say_party", ClientKeyBinding.SayParty)]
    [InlineData("+duck", ClientKeyBinding.Duck)]
    [InlineData("+attack", ClientKeyBinding.Attack)]
    [InlineData("+attack2", ClientKeyBinding.Attack2)]
    [InlineData("+jump", ClientKeyBinding.Jump)]
    [InlineData("+strafe", ClientKeyBinding.Strafe)]
    [InlineData("", ClientKeyBinding.None)]
    [InlineData("+attack3", ClientKeyBinding.Other)]
    [InlineData("say_team2", ClientKeyBinding.Other)]
    [InlineData("+forward", ClientKeyBinding.Other)]
    public void A_binding_is_read_by_its_whole_name(string name, ClientKeyBinding expected)
        => Assert.Equal(expected, UiClientBinding.BindingOf(System.Text.Encoding.UTF8.GetBytes(name)));
}
