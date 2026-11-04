using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The round machine's adapters over the engine: the announcements are the
/// stock game events with the stock field names, and the prints ride the
/// stock TextMsg to everyone; the settings read the registered convars.
/// </summary>
public sealed class TeamRoundAdapterTests
{
    private static readonly IntPtr Event = new(0x1234);

    private static (GameEventRoundAnnouncer Announcer, RecordingEngine Engine) Announcer()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("GameEvents.EventCreate", Event);
        engine.Returns("GameEvents.EventFire", true);
        return (new GameEventRoundAnnouncer(engine, engine), engine);
    }

    private static List<(string Key, object? Value)> Fields(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member.StartsWith("GameEvents.EventSet", StringComparison.Ordinal))
                       .Select(c => ((string)c.Args[1]!, c.Args[2]))
                       .ToList();

    [Fact]
    public void A_round_win_is_teamplay_round_win_with_stock_s_five_fields()
    {
        var (announcer, engine) = Announcer();

        announcer.RoundWin(TFTeam.Blue, WinReason.FlagCaptureLimit, fullRound: true, roundTime: 12.5f, wasSuddenDeath: false);

        var created = Assert.Single(engine.Calls, c => c.Member == "GameEvents.EventCreate");
        Assert.Equal(TFMessages.EventRoundWin, created.Args[0]);
        Assert.Equal(new (string, object?)[]
        {
            ("team", (int)TFTeam.Blue), ("winreason", (int)WinReason.FlagCaptureLimit), ("full_round", true),
            ("round_time", "12.5"), ("was_sudden_death", false),
        }, Fields(engine));
        Assert.Single(engine.Calls, c => c.Member == "GameEvents.EventFire");
    }

    [Fact]
    public void An_event_the_engine_does_not_know_is_neither_filled_nor_fired()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("GameEvents.EventCreate", IntPtr.Zero);

        new GameEventRoundAnnouncer(engine, engine).RoundStart(fullReset: true);

        Assert.Empty(Fields(engine));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "GameEvents.EventFire");
    }

    [Fact]
    public void A_balanced_player_is_the_stock_event_and_the_stock_chat_line_to_everyone()
    {
        var (announcer, engine) = Announcer();

        announcer.TeamBalanced(entIndex: 4, TFTeam.Red, "Scout Guy");

        Assert.Equal(TFMessages.EventTeamBalancedPlayer, Assert.Single(engine.Calls, c => c.Member == "GameEvents.EventCreate").Args[0]);
        Assert.Equal(new (string, object?)[] { ("player", 4), ("team", (int)TFTeam.Red) }, Fields(engine));

        var sent = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal("TextMsg", sent.Args[0]);
        Assert.Equal(0, sent.Args[2]);   // everyone
        byte[] wire = (byte[])sent.Args[4]!;
        Assert.Equal(TFMessages.HudPrintTalk, wire[0]);   // TextMsg's first field: the destination
        string payload = Encoding.UTF8.GetString(wire);
        Assert.Contains(TFMessages.PlayerWasTeamBalanced, payload);
        Assert.Contains("Scout Guy", payload);
    }

    [Fact]
    public void The_switch_line_is_stock_s_chat_print_to_everyone()
    {
        var (announcer, engine) = Announcer();

        announcer.TeamsSwitched();

        var sent = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal("TextMsg", sent.Args[0]);
        Assert.Equal(0, sent.Args[2]);   // everyone
        byte[] wire = (byte[])sent.Args[4]!;
        Assert.Equal(TFMessages.HudPrintTalk, wire[0]);   // UTIL_ClientPrintAll's HUD_PRINTTALK, not a centre print
        string payload = Encoding.UTF8.GetString(wire);
        Assert.Contains(TFMessages.TeamsSwitched, payload);
    }

    [Fact]
    public void A_restart_countdown_prints_only_when_stock_has_a_format()
    {
        var (announcer, engine) = Announcer();

        announcer.RestartSeconds(5, format: null);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "UserMessages.Send");

        announcer.RestartSeconds(5, TFMessages.RoundRestartInSecs);
        Assert.Equal(2, engine.Calls.Count(c => c.Member == "UserMessages.Send"));   // centre and console
        Assert.Equal(2, engine.Calls.Count(c => c.Member == "GameEvents.EventFire"));
    }

    [Fact]
    public void A_win_song_is_the_team_win_jingle_then_the_per_team_songs()
    {
        var (announcer, engine) = Announcer();

        announcer.WinSong(TFTeam.Red);

        Assert.Equal(3, engine.Calls.Count(c => c.Member == "GameEvents.EventFire"));
        Assert.Equal(new (string, object?)[]
        {
            ("team", (int)TFTeam.Unassigned), ("sound", "Game.TeamWin2"),
            ("team", (int)TFTeam.Red), ("sound", "Game.YourTeamWon"),
            ("team", (int)TFTeam.Blue), ("sound", "Game.YourTeamLost"),
        }, Fields(engine));
    }

    [Fact]
    public void A_winless_song_is_the_stalemate_s()
    {
        var (announcer, engine) = Announcer();

        announcer.WinSong(TFTeam.Unassigned);          // PlayWinSong's stalemate arm

        Assert.Equal(3, engine.Calls.Count(c => c.Member == "GameEvents.EventFire"));
        Assert.All(Fields(engine).Where(f => f.Key == "sound"), f => Assert.Equal("Game.Stalemate", f.Value));
    }

    [Fact]
    public void Sudden_death_and_the_round_start_voice_go_to_every_team()
    {
        var (announcer, engine) = Announcer();

        announcer.SuddenDeathSong();

        Assert.Equal(3, engine.Calls.Count(c => c.Member == "GameEvents.EventFire"));
        Assert.All(Fields(engine).Where(f => f.Key == "sound"), f => Assert.Equal("Game.SuddenDeath", f.Value));
    }

    [Fact]
    public void The_round_start_voice_is_each_game_team_s_own()
    {
        var (announcer, engine) = Announcer();

        announcer.StartRoundVoice();

        Assert.Equal(2, engine.Calls.Count(c => c.Member == "GameEvents.EventFire"));
        Assert.Equal(new (string, object?)[]
        {
            ("team", (int)TFTeam.Red), ("sound", "Game.TeamRoundStart2"),
            ("team", (int)TFTeam.Blue), ("sound", "Game.TeamRoundStart3"),
        }, Fields(engine));
    }

    [Fact]
    public void The_settings_read_the_registered_round_convars_at_their_stock_defaults()
    {
        // An unattached registry: the values are the declarations' defaults
        // (a write goes to the engine and comes back by its change callback,
        // which a registry with no engine never hears).
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(GameRules).Assembly);
        convars.RegisterAll(typeof(TeamRoundFlow).Assembly);
        var settings = convars.Holder<ConVarTeamRoundSettings>();

        Assert.Equal(10f, settings.RespawnWaveTime);         // mp_respawnwavetime
        Assert.Equal(15, settings.BonusRoundTime);           // mp_bonusroundtime
        Assert.Equal(1f, settings.EnableRoundWaitTime);      // mp_enableroundwaittime
        Assert.Equal(240, settings.StalemateTimeLimit);      // mp_stalemate_timelimit
        Assert.Equal(1, settings.TeamsUnbalanceLimit);       // mp_teams_unbalance_limit
        Assert.Equal(1, settings.AutoTeamBalance);           // mp_autoteambalance
        Assert.False(settings.StalemateEnable);              // mp_stalemate_enable
        Assert.Equal(string.Empty, settings.NextLevel);      // nextlevel
    }
}
