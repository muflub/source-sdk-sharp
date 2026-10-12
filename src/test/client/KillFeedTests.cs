using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The kill feed's queue against stock CHudDeathNotice (hl2mp/hud_deathnotice.cpp):
/// MaxDeathNotices "4", hud_deathnotice_time "6" (0 = none), a suicide is
/// <c>!killer || killer == victim</c> with no killer drawn and the skull, the
/// icon is the UI icon set's art for the weapon (KillIcons), and
/// each death is written to the console as stock writes it.
/// </summary>
public class KillFeedTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    // User ids 2 (Gordon, team 3, local), 3 (Alyx, team 2), 4 (Barney, team 3).
    private sealed class Players : IKillFeedPlayers
    {
        public bool TryFind(int userId, out string name, out int team, out bool local)
        {
            (name, team, local) = userId switch
            {
                2 => ("Gordon", 3, true),
                3 => ("Alyx", 2, false),
                4 => ("Barney", 3, false),
                _ => ("", 0, false),
            };
            return name.Length > 0;
        }
    }

    private static (KillFeed Feed, Clock Clock, RecordingLog Log) Feed()
    {
        var clock = new Clock();
        var log = new RecordingLog();
        return (new KillFeed(clock, log, new Players()), clock, log);
    }

    [Fact]
    public void A_kill_is_one_line_killer_weapon_victim_in_their_teams()
    {
        var (f, _, _) = Feed();
        f.Add(new PlayerDeathArgs(3, 2, "smg1", false));
        Assert.Equal(1, f.LineCount);
        var l = f.Line(0);
        Assert.Equal(("Gordon", 3, "Alyx", 2, "smg1", "SMG1", false, true),
                     (l.Killer, l.KillerTeam, l.Victim, l.VictimTeam, l.Icon, l.Weapon, l.Suicide, l.Local));
    }

    [Fact]
    public void Killing_yourself_and_the_world_are_suicides_with_no_killer_and_the_skull()
    {
        var (f, _, _) = Feed();
        f.Add(new PlayerDeathArgs(3, 3, "smg1", false));       // killer == victim
        f.Add(new PlayerDeathArgs(4, 0, "worldspawn", false));  // !killer
        Assert.All(new[] { f.Line(0), f.Line(1) }, l => Assert.Equal((true, "", "skull"), (l.Suicide, l.Killer, l.Icon)));
    }

    [Fact]
    public void A_weapon_the_icon_set_has_no_art_for_gets_the_generic_icon()
    {
        var (f, _, _) = Feed();
        f.Add(new PlayerDeathArgs(3, 4, "trigger_hurt", false));
        Assert.Equal(("generic", "TRIGGER HURT", false), (f.Line(0).Icon, f.Line(0).Weapon, f.Line(0).Suicide));
    }

    [Fact]
    public void A_kill_with_no_weapon_held_is_world_and_draws_the_killer_and_the_skull()
    {
        // The server writes "world" for a kill with nothing held; stock finds no
        // death_world icon and falls back to the skull, the killer still drawn.
        var (f, _, _) = Feed();
        f.Add(new PlayerDeathArgs(3, 2, "world", false));
        Assert.Equal(("Gordon", "skull", false), (f.Line(0).Killer, f.Line(0).Icon, f.Line(0).Suicide));
    }

    [Fact]
    public void Past_four_lines_the_oldest_goes()
    {
        var (f, _, _) = Feed();
        for (int i = 0; i < 5; i++)
            f.Add(new PlayerDeathArgs(3, i % 2 == 0 ? 2 : 4, "smg1", false));
        Assert.Equal(KillFeed.MaxLines, f.LineCount);
        Assert.Equal(4, KillFeed.MaxLines);
        Assert.Equal("Barney", f.Line(0).Killer);    // the second kill is now the oldest
    }

    [Fact]
    public void A_line_stays_six_seconds()
    {
        var (f, clock, _) = Feed();
        f.Add(new PlayerDeathArgs(3, 2, "smg1", false));
        clock.Now = 15.9f;
        f.Refresh();
        Assert.Equal(1, f.LineCount);
        clock.Now = 16.1f;
        f.Refresh();
        Assert.Equal(0, f.LineCount);
    }

    [Fact]
    public void The_declared_default_is_stocks_six_seconds()
        => Assert.Equal("6", typeof(HudDeathNoticeTimeConVar).GetCustomAttributes(typeof(ConVarAttribute), false)
                                 .Cast<ConVarAttribute>().Single().DefaultValue);

    [Fact]
    public void Hud_deathnotice_time_0_shows_nothing()
    {
        var clock = new Clock();
        var time = new HudDeathNoticeTimeConVar();
        time.NotifyChanged("0", "6");
        var f = new KillFeed(clock, new RecordingLog(), new Players(), time);
        f.Add(new PlayerDeathArgs(3, 2, "smg1", false));
        Assert.Equal(0, f.LineCount);
    }

    [Fact]
    public void Turning_the_time_down_keeps_the_lines_already_shown()
    {
        var clock = new Clock();
        var time = new HudDeathNoticeTimeConVar();
        time.NotifyChanged("6", "0");
        var f = new KillFeed(clock, new RecordingLog(), new Players(), time);
        f.Add(new PlayerDeathArgs(3, 2, "smg1", false));
        time.NotifyChanged("0", "6");          // stock: each line keeps the display time it was given
        clock.Now = 11f;
        Assert.Equal(1, f.LineCount);
    }

    [Fact]
    public void A_new_level_clears_the_feed()
    {
        var (f, clock, _) = Feed();
        f.Add(new PlayerDeathArgs(3, 2, "smg1", false));
        clock.Now = 0.5f;           // the clock went back: another level
        f.Refresh();
        Assert.Equal(0, f.LineCount);
    }

    [Fact]
    public void The_console_says_what_stock_says()
    {
        var (f, _, log) = Feed();
        f.Add(new PlayerDeathArgs(3, 2, "smg1", false));
        f.Add(new PlayerDeathArgs(3, 3, "smg1", false));
        f.Add(new PlayerDeathArgs(4, 0, "worldspawn", false));
        Assert.Equal(["Gordon killed Alyx with smg1.\n", "Alyx suicided.\n", "Barney suicided.\n"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Nobody_dead_shows_nothing()
    {
        var (f, _, _) = Feed();
        f.Refresh();
        Assert.Equal(0, f.LineCount);
    }
}
