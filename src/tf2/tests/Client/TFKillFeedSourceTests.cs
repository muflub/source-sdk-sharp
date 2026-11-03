extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using Feed = client::TF2.Client.Ui.TFKillFeedSource;
using Time = client::SourceSharp.Client.Vgui.HudDeathNoticeTimeConVar;
using Custom = client::TF2.Shared.Combat.TFDamageCustom;
using Crit = client::TF2.Shared.Combat.TFDamageBits;
using Dam = client::SourceSharp.Abi.DamageType;

// The contract assembly is unaliased: the interface and line record are
// global, as the team menu's choices are. PlayerDeathArgs names the client's
// copy — the server's would be a different struct.
using Death = client::SourceSharp.Shared.PlayerDeathArgs;

namespace TF2.Tests.Client;

/// <summary>
/// The death-notice queue against stock's <c>CTFHudDeathNotice</c>: the
/// decode of <c>player_death</c> (hud_basedeathnotice.cpp FireGameEvent
/// :473-556, tf_hud_deathnotice.cpp OnGameEvent's custom-kill switch
/// :975-1039), the icon names of stock's d_images atlas, the 4-line cap
/// (hudlayout.res:510) culled non-local first (RetireExpiredDeathNotices
/// :352-382), the doubled display for a line the local player is in
/// (GetExpiryTime :923-931), hud_deathnotice_time 0 showing nothing
/// (:406-409) and the new-level purge (VidInit :77-80). <see cref="Death"/>
/// names the client's copy of the shared event args (the server's would be a
/// different struct).
/// </summary>
public sealed class TFKillFeedSourceTests
{
    private sealed class Clock : client::SourceSharp.Abi.IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    // User ids 2 (Gordon, team 3, local), 3 (Alyx, team 2), 4 (Barney, team 3).
    private sealed class Players : client::SourceSharp.Client.Vgui.IKillFeedPlayers
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

    private static (Feed Feed, Clock Clock) Rig(Time? time = null)
    {
        var clock = new Clock();
        return (new Feed(clock, new Players(), time), clock);
    }

    [Fact]
    public void A_death_becomes_one_line_killer_weapon_victim_in_their_teams()
    {
        var (f, _) = Rig();
        f.Add(new Death(3, 2, "scattergun", false));
        var l = f.Line(0);
        Assert.Equal(("Gordon", 3, "Alyx", 2, "d_scattergun", false, true),
                     (l.Killer, l.KillerTeam, l.Victim, l.VictimTeam, l.Icon, l.Suicide, l.LocalInvolved));
    }

    [Fact]
    public void Killing_yourself_and_the_world_are_suicides_with_no_killer_and_the_weapon_icon_kept()
    {
        // Stock sets szIcon from killedwith unconditionally (:513-516) and
        // clears only the killer's name (:517-520): the skull answers only
        // when nothing names an icon.
        var (f, _) = Rig();
        f.Add(new Death(3, 3, "smg1", false));        // killer == victim
        f.Add(new Death(4, 0, "worldspawn", false));  // !killer
        Assert.Equal((true, "", "d_smg1"), (f.Line(0).Suicide, f.Line(0).Killer, f.Line(0).Icon));
        Assert.Equal((true, "", "d_worldspawn"), (f.Line(1).Suicide, f.Line(1).Killer, f.Line(1).Icon));
    }

    [Fact]
    public void A_weapon_becomes_d_plus_the_weapon_name()
        => Assert.Equal("d_", Feed.Icon("rocketlauncher_directhit", 0, false).Substring(0, 2));

    [Fact]
    public void A_backstab_draws_the_backstab_art_whatever_the_weapon()
    {
        var (f, _) = Rig();
        f.Add(new Death(3, 4, "knife", false, Custom: (int)Custom.Backstab));
        Assert.Equal("d_backstab", f.Line(0).Icon);
    }

    [Fact]
    public void A_headshot_names_the_weapon_art_the_event_carries()
    {
        var (f, _) = Rig();
        f.Add(new Death(3, 2, "ambassador", false, Custom: (int)Custom.Headshot));
        f.Add(new Death(4, 2, "huntsman", false, Custom: (int)Custom.Headshot));
        f.Add(new Death(3, 2, "sniperrifle", false, Custom: (int)Custom.Headshot));
        Assert.Equal(["d_ambassador_headshot", "d_huntsman_headshot", "d_headshot"],
                     new[] { f.Line(0), f.Line(1), f.Line(2) }.Select(l => l.Icon));
    }

    [Fact]
    public void Burning_is_the_fire_death_only_when_it_killed_its_own()
    {
        // tf_hud_deathnotice.cpp:1014-1020 — the arm reads iSuicide.
        var (f, _) = Rig();
        f.Add(new Death(3, 2, "flamethrower", false, Custom: (int)Custom.Burning));
        f.Add(new Death(4, 4, "flamethrower", false, Custom: (int)Custom.Burning));
        Assert.Equal(["d_flamethrower", "d_firedeath"], new[] { f.Line(0), f.Line(1) }.Select(l => l.Icon));
    }

    [Fact]
    public void The_burning_arrow_flying_burn_and_pumpkin_override_the_weapon()
    {
        var (f, _) = Rig();
        f.Add(new Death(3, 2, "huntsman", false, Custom: (int)Custom.BurningArrow));
        f.Add(new Death(4, 2, "huntsman", false, Custom: (int)Custom.Flyingburn));
        f.Add(new Death(3, 2, "tf_projectile_rocket", false, Custom: (int)Custom.PumpkinBomb));
        Assert.Equal(["d_huntsman_burning", "d_huntsman_flyingburn", "d_pumpkindeath"],
                     new[] { f.Line(0), f.Line(1), f.Line(2) }.Select(l => l.Icon));
    }

    [Fact]
    public void A_line_the_local_player_is_in_names_the_inverted_art_once()
    {
        // GetIcon's kDeathNoticeIcon_Inverted arm (:894-913): built at add
        // time, never per frame — the line carries the name already.
        var (f, _) = Rig();
        f.Add(new Death(2, 3, "scattergun", false));   // local killer
        f.Add(new Death(3, 4, "scattergun", false));   // nobody local
        Assert.Equal("dneg_scattergun", f.Line(0).LocalIcon);
        Assert.Equal("", f.Line(1).LocalIcon);
    }

    [Fact]
    public void The_crit_bit_marks_the_line()
    {
        var (f, _) = Rig();
        f.Add(new Death(3, 2, "sniperrifle", false, Bits: (int)Crit.Critical));
        Assert.True(f.Line(0).Crit);
    }

    [Fact]
    public void A_suicide_carries_the_info_phrase_and_a_kill_none()
    {
        // The info text hud_basedeathnotice.cpp:303-314 draws after the
        // victim: the suicide custom kind's phrase (:1041-1051); the fall's
        // (hud_basedeathnotice.cpp:527-531) wins on the bits.
        var (fall, _) = Rig();
        fall.Add(new Death(3, 3, "", false, Bits: (int)Dam.Fall));
        var (suicide, _) = Rig();
        suicide.Add(new Death(3, 3, "", false, Custom: (int)Custom.Suicide));
        var (kill, _) = Rig();
        kill.Add(new Death(3, 2, "scattergun", false));
        Assert.Equal("#DeathMsg_Fall", fall.Line(0).Info);
        Assert.Equal("#DeathMsg_Suicide", suicide.Line(0).Info);
        Assert.Equal("", kill.Line(0).Info);
    }

    [Fact]
    public void Past_four_lines_a_non_local_one_goes_first()
    {
        var (f, _) = Rig();
        f.Add(new Death(3, 4, "scattergun", false));   // 0 local
        f.Add(new Death(2, 3, "scattergun", false));   // 1 local
        f.Add(new Death(3, 4, "scattergun", false));   // 2 local
        f.Add(new Death(2, 3, "scattergun", false));   // 3 local
        f.Add(new Death(3, 4, "scattergun", false));   // pushes 0 out
        Assert.Equal(Feed.MaxLines, f.LineCount);
        Assert.Equal(4, Feed.MaxLines);
        Assert.True(f.Line(0).LocalInvolved);   // the second kill survived
        Assert.False(f.Line(3).LocalInvolved);  // the newest kill arrived
    }

    [Fact]
    public void All_local_the_oldest_goes()
    {
        var (f, _) = Rig();
        for (int i = 0; i < 5; i++)
            f.Add(new Death(2, 2, "smg1", false));
        Assert.Equal(4, f.LineCount);
        Assert.True(f.Line(0).LocalInvolved);   // the oldest surviving kill
        Assert.True(f.Line(3).LocalInvolved);   // the newest kill is the newest line
    }

    [Fact]
    public void A_line_stays_six_seconds_and_the_locals_stay_twelve()
    {
        var (f, clock) = Rig();
        f.Add(new Death(3, 4, "scattergun", false));   // nobody local
        f.Add(new Death(2, 3, "scattergun", false));   // local killer
        clock.Now = 15.9f;
        Assert.Equal(2, f.LineCount);
        clock.Now = 16.1f;
        Assert.Equal(1, f.LineCount);
        Assert.True(f.Line(0).LocalInvolved);
        clock.Now = 22.1f;
        Assert.Equal(0, f.LineCount);
    }

    [Fact]
    public void Hud_deathnotice_time_0_shows_nothing()
    {
        var time = new Time();
        time.NotifyChanged("0", "6");
        var (f, _) = Rig(time);
        f.Add(new Death(3, 2, "scattergun", false));
        Assert.Equal(0, f.LineCount);
    }

    [Fact]
    public void A_new_level_clears_the_feed()
    {
        var (f, clock) = Rig();
        f.Add(new Death(3, 2, "scattergun", false));
        clock.Now = 0.5f;    // the clock went back: another level
        Assert.Equal(0, f.LineCount);
    }

    [Fact]
    public void The_declared_default_is_stocks_six_seconds()
        => Assert.Equal("6", typeof(Time).GetCustomAttributes(typeof(client::SourceSharp.Abi.ConVarAttribute), false)
                             .Cast<client::SourceSharp.Abi.ConVarAttribute>().Single().DefaultValue);
}
