extern alias client;

using Xunit;

using ClientShared = client::SourceSharp.Shared;
using ClientSide = client::SourceSharp.Client;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using Countdown = client::TF2.Client.Ui.TFRespawnCountdown;
using EventLink = client::TF2.Client.EventLink;
using DeathArgs = client::SourceSharp.Shared.PlayerDeathArgs;
using DeathEvent = client::SourceSharp.Shared.PlayerDeathEvent;
using HurtEvent = client::SourceSharp.Shared.PlayerHurtEvent;
using DeathScreen = client::TF2.Client.Ui.TFDeathScreen;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Names = client::SourceSharp.Client.Vgui.IPlayerNames;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using RoundStateId = client::TF2.Shared.Rules.RoundState;
using Standalone = client::SourceSharp.Client.Standalone;
using TFClassId = client::TF2.Shared.TFClass;

namespace TF2.Tests.Client;

/// <summary>
/// The freeze panel's and the respawn countdown's data sources
/// (src/tf2/client/Ui): each maps the networked rows into what the screens
/// draw, before and after the data arrives. The deaths are raised the way
/// the engine dispatches them, through the event the service subscribes to.
/// </summary>
public sealed class TFDeathScreenTests
{
    /// <summary>The killer's row as stock's panel reads it (tf_hud_freezepanel.cpp:355-404):
    /// name, team, class, health, and the flags that arm the nemesis panel.</summary>
    [Fact]
    public void A_local_death_snaps_the_killer_row()
    {
        var (source, deaths, engine) = Built();
        ArriveKiller(engine.St, engine.Resources);

        // Nothing is up before a death, health or none.
        Assert.False(source.Showing);

        deaths.Raise(new DeathArgs(UserId: 7, AttackerUserId: 9, "rocketlauncher", Headshot: false,
                                   (int)TF2.Server.Combat.TFDominations.Domination));

        Assert.True(source.Showing);
        Assert.Equal((int)TF2.Server.Combat.TFDominations.Domination, source.DeathFlags);
        Assert.True(source.HasKiller);
        Assert.Equal("Bob", source.KillerName);
        Assert.Equal(2, source.KillerTeam);
        Assert.Equal("Soldier", source.KillerClass);
        Assert.Equal(40, source.KillerHealth);
        Assert.Equal(125, source.KillerMaxHealth);
        Assert.Equal(187, source.KillerMaxBuffedHealth);
        Assert.True(source.KillerAlive);
    }

    /// <summary>A suicide and a world death have no killer row: the panel
    /// draws its label alone (stock's pKiller-null arm, :568-599).</summary>
    [Fact]
    public void A_suicide_and_a_world_death_have_no_killer_row()
    {
        var (source, deaths, engine) = Built();
        ArriveKiller(engine.St, engine.Resources);       // the stimulus: a killer exists, and is ignored

        deaths.Raise(new DeathArgs(7, 7, "rocketlauncher", false, 0));

        Assert.True(source.Showing);
        Assert.False(source.HasKiller);
        Assert.Equal("", source.KillerName);
        Assert.Equal(0, source.KillerTeam);
        Assert.Equal("", source.KillerClass);
        Assert.Equal(0, source.KillerHealth);
        Assert.Equal(0, source.KillerMaxHealth);
        Assert.False(source.KillerAlive);

        deaths.Raise(new DeathArgs(7, 0, "", false, 0));
        Assert.False(source.HasKiller);
    }

    /// <summary>Another player's death is not ours, and a respawn lowers
    /// the panel (stock's hide_freezepanel — here, the health returning).</summary>
    [Fact]
    public void Another_players_death_shows_nothing_and_a_respawn_hides_the_panel()
    {
        var (source, deaths, engine) = Built();
        ArriveKiller(engine.St, engine.Resources);

        deaths.Raise(new DeathArgs(9, 7, "rocketlauncher", false, 0));
        Assert.False(source.Showing);                    // Bob died, not us

        deaths.Raise(new DeathArgs(7, 9, "rocketlauncher", false, 0));
        Assert.True(source.Showing);
        engine.Engine.Returns("Hud.LocalPlayerHealth", 100);
        Assert.False(source.Showing);
    }

    /// <summary>The killer's health is zero once they are dead, stock's
    /// !IsAlive arm (:363-366) — the row keeps its columns.</summary>
    [Fact]
    public void A_dead_killers_health_reads_zero()
    {
        var (source, deaths, engine) = Built();
        ArriveKiller(engine.St, engine.Resources, killerAlive: false);

        deaths.Raise(new DeathArgs(7, 9, "rocketlauncher", false, 0));

        Assert.True(source.HasKiller);
        Assert.Equal(0, source.KillerHealth);
        Assert.False(source.KillerAlive);
        Assert.Equal("Soldier", source.KillerClass);     // the row keeps its columns
        Assert.Equal(125, source.KillerMaxHealth);
    }

    // ---- the respawn countdown ----

    /// <summary>No wave before the data arrives, and the local row's
    /// deadline truncated whole seconds against the clock
    /// (UpdateReinforcements, tf_spectatorgui.cpp:393-401).</summary>
    [Fact]
    public void The_countdown_reads_the_local_rows_next_wave()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 0);
        engine.Returns("Clock.Now", 30f);
        var resources = new PlayerResources();
        var source = new Countdown(engine, resources, engine);

        Assert.False(source.HasWave);
        Assert.Equal(0, source.WaitSeconds);
        Assert.False(source.Stalemate);
        Assert.False(source.TeamWin);

        var tf = new PlayerResource(engine, resources);
        tf.State.BindLocal();
        tf.OnSpawn();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        tf.State.NextRespawnTime[2] = 42.7f;

        Assert.True(source.HasWave);
        Assert.Equal(12, source.WaitSeconds);            // (int)(42.7 - 30), stock's cast truncates
    }

    /// <summary>A wave whose time has passed is not a negative countdown.</summary>
    [Fact]
    public void A_passed_wave_counts_nothing()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Clock.Now", 50f);
        var resources = new PlayerResources();
        var tf = new PlayerResource(engine, resources);
        tf.State.BindLocal();
        tf.OnSpawn();
        tf.State.NextRespawnTime[2] = 42.7f;
        var source = new Countdown(engine, resources, engine);

        Assert.True(source.HasWave);
        Assert.Equal(0, source.WaitSeconds);
    }

    /// <summary>The round's two named arms ride the resource's word (the
    /// m_iRoundState the gamerules proxy would carry).</summary>
    [Fact]
    public void The_rounds_word_names_the_stalemate_and_team_win_arms()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Clock.Now", 30f);
        var resources = new PlayerResources();
        var tf = new PlayerResource(engine, resources);
        tf.State.BindLocal();
        tf.OnSpawn();
        var source = new Countdown(engine, resources, engine);

        tf.State.RoundState = (byte)RoundStateId.Stalemate;
        Assert.True(source.Stalemate);
        Assert.False(source.TeamWin);

        tf.State.RoundState = (byte)RoundStateId.TeamWin;
        Assert.True(source.TeamWin);
        Assert.False(source.Stalemate);
    }

    // ---- the rig ----

    /// <summary>The screen over a strict engine, an empty standalone list
    /// and the event, not yet wired to any row.</summary>
    private static (DeathScreen Source, DeathEvent Deaths, (Engine Engine, Standalones St, PlayerResources Resources)) Built()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 0);
        var st = new Standalones();
        var resources = new PlayerResources();
        var deaths = new DeathEvent();
        var source = new DeathScreen(engine, st, new Roster((3, "Bob")), resources,
            new EventLink(deaths, new HurtEvent(), new ClientShared.ControlPointStartTouchEvent(),
                new ClientShared.ControlPointEndTouchEvent(), new ClientShared.TeamplayWinPanelEvent(),
                new ClientShared.TeamplayRoundStartEvent()));
        return (source, deaths, (engine, st, resources));
    }

    /// <summary>The local player (slot 2, user id 7) and the killer
    /// (slot 3, user id 9, RED) arriving on both resources, as their first
    /// updates deliver them; the killer is a soldier of 125/187 with 40 left.</summary>
    private static PlayerResource ArriveKiller(Standalones st, PlayerResources resources,
        bool killerAlive = true)
    {
        var core = new Standalone.ClientPlayerResource(new Engine());
        core.State.BindLocal();
        core.State.SetRow(2, 0, 0, 60, 7, 2, connected: true, alive: true, valid: true);
        core.State.SetRow(3, 0, 0, killerAlive ? 40 : 0, 9, 2, connected: true, alive: killerAlive, valid: true);
        st.All.Add(core);

        var tf = new PlayerResource(new Engine(), resources);
        tf.State.BindLocal();
        tf.State.PlayerClass[3] = (byte)TFClassId.Soldier;
        tf.State.MaxHealth[3] = 125;
        tf.State.MaxBuffedHealth[3] = 187;
        tf.State.Team[3] = 2;
        tf.OnSpawn();
        return tf;
    }

    private sealed class Standalones : ClientSide.IClientStandalones
    {
        public readonly List<Standalone.ClientStandaloneEntity> All = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => All;

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>The g_PR role: one slot answering its name, the rest nobody.</summary>
    private sealed class Roster(params (int Slot, string Name)[] rows) : Names
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            foreach (var (found, name) in rows)
                if (found == slot)
                {
                    info = new ClientPlayerInfo(name, 0, false);
                    return true;
                }
            info = default;
            return false;
        }
    }
}
