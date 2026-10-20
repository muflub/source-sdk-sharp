using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's observer mode (CBasePlayer: StartObserverMode, SetObserverMode,
/// SetObserverTarget, IsValidObserverTarget, FindNextObserverTarget,
/// CheckObserverSettings, the spec_* commands), on the managed player: the
/// mode and target are networked (PlayerNetState.ObserverMode /
/// ObserverTarget, the invalid handle for nobody) and the native player
/// carries out the movetype.
/// </summary>
public class ObserverTests
{
    private static readonly EntityHandle[] Handles = { new(0x010001), new(0x010002), new(0x010003) };

    internal sealed class Clients : IConnectedClients
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public float? AvgLatency(int entIndex) => null;

        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public string? SpecMode;
        public string? ClientConVar(int entIndex, string name)
            => name == "name" ? "player" + entIndex : name == "cl_spec_mode" ? SpecMode : null;
        public int UserId(int entIndex) => entIndex + 10;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    internal sealed class Rig
    {
        public readonly RecordingEngine Engine;
        public readonly Observers Observers;
        public readonly SsPlayer[] Players;
        public readonly ConVarRegistry ConVars;
        public readonly Clients Clients = new();

        public Rig(int count = 3)
        {
            var registry = new ScriptedEntityRegistry(TestEngines.Server());
            registry.RegisterAll(typeof(SsPlayer).Assembly);
            var services = Services(out Engine);
            Engine.Returns("Entities.IsValid", true)
                  .ReturnsEachCall("Entities.GetIndex", () => Engine.Calls[^1].Args[0] is EntityHandle h ? Array.IndexOf(Handles, h) + 1 : 0)
                  .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
                  .Returns("Players.GetTeam", 0)
                  .Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f))
                  .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(new Vector(0f, -64f, 64f), false, false))
                  .Ignores("Players.SetObserverMode", "PlayerMovement.SetMoveType", "EntityRender.SetVisible",
                           "Players.HolsterActiveWeapon", "Damage.SetTakeDamageMode", "Entities.Teleport",
                           "PlayerMovement.SnapEyeAngles", "EntityPhysics.SetNotSolid", "Players.RemoveAllItems",
                           "Players.Respawn");
            services.AddInstance<IEntityDirectory>(registry);
            registry.Services = services;
            for (int i = 0; i < count; i++)
                registry.KeyValue(Handles[i], EntityNames.SsPlayer, "targetname", "p" + i);
            Players = Handles.Take(count).Select(h => (SsPlayer)registry.Get(h)!).ToArray();
            foreach (var p in Players)
            {
                p.State.BindLocal();
                p.State.Alive = true;
                p.State.ObserverTarget = EntityHandle.None.Value;
            }
            ConVars = ConVarRegistry.For(UnattachedConsole());
            ConVars.RegisterAll(typeof(GameRules).Assembly);
            ConVars.Get<AllowSpectatorsConVar>()!.NotifyChanged("1", "0");   // stock's default
            Observers = new Observers(new RecordingLog(), registry, Engine, Engine, Engine, Engine, Engine, Engine, Engine, ConVars, Clients);
        }

        public void Die(int i, float at)
        {
            Players[i].State.Alive = false;
            Players[i].Observer.DeathTime = at;
        }

        /// <summary>StartObserverMode for a player who asked to watch (m_bEnterObserver).</summary>
        public bool Watch(SsPlayer p, ObserverMode mode, float now)
        {
            p.Observer.EnterObserver = true;
            return Observers.Start(p, mode, now);
        }
    }

    [Fact]
    public void Starting_to_watch_networks_the_mode_and_sets_stocks_movetype()
    {
        var r = new Rig();
        r.Die(0, 1f);

        Assert.True(r.Watch(r.Players[0], ObserverMode.Roaming, 1f));

        Assert.Equal((int)ObserverMode.Roaming, r.Players[0].State.ObserverMode);
        Assert.Equal(MoveType.Observer, (MoveType)r.Engine.AllOf("PlayerMovement.SetMoveType").Last().Args[1]!);
        Assert.Contains(r.Engine.AllOf("EntityRender.SetVisible"), c => (bool)c.Args[1]! == false);
    }

    [Theory]
    [InlineData(ObserverMode.DeathCam, MoveType.None)]
    [InlineData(ObserverMode.Fixed, MoveType.None)]
    [InlineData(ObserverMode.InEye, MoveType.Observer)]
    [InlineData(ObserverMode.Chase, MoveType.Observer)]
    public void Each_mode_has_stocks_movetype(ObserverMode mode, MoveType moveType)
    {
        var r = new Rig();
        r.Die(0, 1f);
        r.Watch(r.Players[0], mode, 1f);
        Assert.Equal(moveType, (MoveType)r.Engine.AllOf("PlayerMovement.SetMoveType").Last().Args[1]!);
    }

    [Fact]
    public void A_target_is_another_drawn_player_or_one_dead_under_three_seconds()
    {
        var r = new Rig();
        var (me, other, third) = (r.Players[0], r.Players[1], r.Players[2]);

        Assert.False(r.Observers.IsValidTarget(me, null, 10f), "nobody");
        Assert.False(r.Observers.IsValidTarget(me, me, 10f), "not yourself");
        Assert.True(r.Observers.IsValidTarget(me, other, 10f), "a living player");

        r.Die(1, 8f);
        Assert.True(r.Observers.IsValidTarget(me, other, 10.9f), "dead under DEATH_ANIMATION_TIME");
        Assert.False(r.Observers.IsValidTarget(me, other, 11.1f), "dead over it");

        r.Die(2, 10f);
        r.Watch(third, ObserverMode.Roaming, 10f);
        Assert.False(r.Observers.IsValidTarget(me, third, 10.1f), "an observer is not drawn");
    }

    [Fact]
    public void The_next_target_walks_the_slots_from_the_current_one_and_wraps()
    {
        var r = new Rig();
        r.Die(0, 1f);
        r.Watch(r.Players[0], ObserverMode.InEye, 1f);
        var me = r.Players[0];

        r.Observers.SetTarget(me, r.Players[1], 1f);
        Assert.Same(r.Players[2], r.Observers.FindNext(me, reverse: false, 1f));
        r.Observers.SetTarget(me, r.Players[2], 1f);
        Assert.Same(r.Players[1], r.Observers.FindNext(me, reverse: false, 1f));   // skips slot 1 (self), wraps
        Assert.Same(r.Players[1], r.Observers.FindNext(me, reverse: true, 1f));
    }

    [Fact]
    public void The_target_is_networked_as_its_handle_and_nobody_as_the_invalid_handle()
    {
        var r = new Rig();
        r.Die(0, 1f);
        r.Watch(r.Players[0], ObserverMode.InEye, 1f);
        Assert.Equal(Handles[1].Value, r.Players[0].State.ObserverTarget);   // CheckObserverSettings found one

        var alone = new Rig(count: 1);
        alone.Die(0, 1f);
        alone.Watch(alone.Players[0], ObserverMode.InEye, 1f);
        Assert.Equal(EntityHandle.None.Value, alone.Players[0].State.ObserverTarget);
    }

    [Fact]
    public void With_nobody_to_watch_in_eye_falls_back_to_fixed_under_the_default_forcecamera()
    {
        var alone = new Rig(count: 1);
        alone.Die(0, 1f);
        alone.Watch(alone.Players[0], ObserverMode.InEye, 1f);
        Assert.Equal((int)ObserverMode.Fixed, alone.Players[0].State.ObserverMode);
    }

    [Fact]
    public void Spec_next_moves_the_target_in_a_chasing_mode_and_not_in_the_death_cam()
    {
        var r = new Rig();
        r.Die(0, 1f);
        var me = r.Players[0];

        r.Watch(me, ObserverMode.DeathCam, 1f);
        r.Observers.SpecNext(me, reverse: false, 1f);
        Assert.Equal(EntityHandle.None.Value, me.State.ObserverTarget);

        r.Observers.SetMode(me, ObserverMode.Chase, 1f);
        uint first = me.State.ObserverTarget;
        r.Observers.SpecNext(me, reverse: false, 1f);
        Assert.NotEqual(first, me.State.ObserverTarget);
        Assert.Equal(Handles[2].Value, me.State.ObserverTarget);
    }

    [Fact]
    public void Spec_mode_cycles_in_eye_to_roaming_and_a_bad_argument_is_in_eye()
    {
        var r = new Rig();
        r.Die(0, 1f);
        var me = r.Players[0];
        r.Watch(me, ObserverMode.InEye, 1f);

        var seen = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            r.Observers.SpecMode(me, null, 1f);
            seen.Add(me.State.ObserverMode);
        }
        Assert.Equal(new[] { 5, 6, 7, 4 }, seen);

        r.Observers.SpecMode(me, "99", 1f);
        Assert.Equal((int)ObserverMode.InEye, me.State.ObserverMode);
    }

    /// <summary>
    /// The movement loop reads the observer mode the native player holds
    /// (MvGetObserverMode), not the networked one: a spec_mode that changed
    /// only the networked mode left an in-eye observer roaming where
    /// SetObserverTarget's jump put it, never moved onto its target
    /// (C7 item 5's spectate check; live, 8 of 8 passes).
    /// </summary>
    [Fact]
    public void Spec_mode_hands_the_new_mode_to_the_player_the_movement_reads()
    {
        var r = new Rig();
        r.Die(0, 1f);
        var me = r.Players[0];
        r.Watch(me, ObserverMode.Roaming, 1f);
        r.Engine.Clear();

        r.Observers.SpecMode(me, "4", 1f);

        Assert.Equal((int)ObserverMode.InEye, me.State.ObserverMode);   // control: the networked mode moved
        Assert.Equal(ObserverMode.InEye, (ObserverMode)r.Engine.Last("Players.SetObserverMode")?.Args[1]!);
    }

    [Fact]
    public void Spec_mode_in_the_death_cam_only_remembers_the_mode()
    {
        var r = new Rig();
        r.Die(0, 1f);
        var me = r.Players[0];
        r.Watch(me, ObserverMode.DeathCam, 1f);
        r.Observers.SpecMode(me, "5", 1f);
        Assert.Equal(((int)ObserverMode.DeathCam, ObserverMode.Chase), (me.State.ObserverMode, me.Observer.LastMode));
    }

    [Fact]
    public void Spec_player_picks_a_valid_target_by_name()
    {
        var r = new Rig();
        r.Die(0, 1f);
        var me = r.Players[0];
        r.Watch(me, ObserverMode.Chase, 1f);
        r.Observers.SpecPlayer(me, "#13", 1f);   // userid 13: slot 3
        Assert.Equal(Handles[2].Value, me.State.ObserverTarget);
        r.Observers.SpecPlayer(me, "player1", 1f);   // yourself, by name: refused
        Assert.Equal(Handles[2].Value, me.State.ObserverTarget);
    }

    [Fact]
    public void Stopping_returns_to_play_and_remembers_a_watching_mode()
    {
        var r = new Rig();
        r.Die(0, 1f);
        var me = r.Players[0];
        r.Watch(me, ObserverMode.Chase, 1f);
        r.Observers.Stop(me);
        Assert.Equal((0, ObserverMode.Chase), (me.State.ObserverMode, me.Observer.LastMode));
    }

    //-- HL2MP: observing is asked for, never a death timeout --------------------

    [Fact]
    public void A_killed_hl2mp_player_stays_mode_none_past_the_death_animation_and_spec_next_does_nothing()
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Die(0, 1f);
        r.Observers.Killed(me, 1f);

        r.Observers.DeathThink(me, 1f + Observers.DeathAnimationTime + 2f);
        r.Observers.SpecNext(me, reverse: false, 6f);

        Assert.Equal((0, EntityHandle.None.Value), (me.State.ObserverMode, me.State.ObserverTarget));
        Assert.False(r.Observers.Start(me, ObserverMode.Roaming, 6f), "StartObserverMode refuses one who did not ask");
    }

    [Fact]
    public void A_killed_player_falls_not_solid_and_rests_once_on_the_ground()
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Die(0, 1f);
        r.Observers.Killed(me, 1f);

        Assert.Equal(MoveType.FlyGravity, (MoveType)r.Engine.AllOf("PlayerMovement.SetMoveType").Last().Args[1]!);
        Assert.Contains(r.Engine.AllOf("EntityPhysics.SetNotSolid"), c => (bool)c.Args[1]!);

        r.Observers.DeathThink(me, 1.5f);   // in the air
        Assert.Equal(MoveType.FlyGravity, (MoveType)r.Engine.AllOf("PlayerMovement.SetMoveType").Last().Args[1]!);

        me.State.Flags |= 1u;               // FL_ONGROUND
        r.Observers.DeathThink(me, 2f);
        r.Observers.DeathThink(me, 2.5f);
        Assert.Equal(MoveType.None, (MoveType)r.Engine.AllOf("PlayerMovement.SetMoveType").Last().Args[1]!);
        Assert.Equal(2, r.Engine.AllOf("PlayerMovement.SetMoveType").Count());
    }

    [Fact]
    public void Spectate_watches_in_cl_spec_mode_without_items()
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Clients.SpecMode = "7";

        Assert.True(r.Observers.Spectate(me, 1f));

        Assert.Equal((int)ObserverMode.Roaming, me.State.ObserverMode);
        Assert.True(me.Observer.EnterObserver, "m_bEnterObserver");
        Assert.Contains(r.Engine.AllOf("Players.RemoveAllItems"), c => (bool)c.Args[1]!);
    }

    [Theory]
    [InlineData("3")]   // OBS_MODE_FIXED is not a mode to ask for
    [InlineData("8")]
    [InlineData(null)]
    public void Spectate_with_a_cl_spec_mode_out_of_range_uses_the_last_mode(string? asked)
    {
        var r = new Rig();
        var me = r.Players[0];
        me.Observer.LastMode = ObserverMode.Chase;
        r.Clients.SpecMode = asked;

        r.Observers.Spectate(me, 1f);

        Assert.Equal((int)ObserverMode.Chase, me.State.ObserverMode);
    }

    [Fact]
    public void Spectate_is_refused_without_mp_allowspectators()
    {
        var r = new Rig();
        var me = r.Players[0];
        r.ConVars.Get<AllowSpectatorsConVar>()!.NotifyChanged("0", "1");

        Assert.False(r.Observers.Spectate(me, 1f));
        Assert.Equal(0, me.State.ObserverMode);
        Assert.Empty(r.Engine.AllOf("Players.RemoveAllItems"));
    }

    [Fact]
    public void Then_spec_next_sets_the_target_and_spec_mode_moves_to_in_eye()
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Clients.SpecMode = "7";
        r.Observers.Spectate(me, 1f);
        Assert.Equal(EntityHandle.None.Value, me.State.ObserverTarget);

        r.Observers.SpecNext(me, reverse: false, 1f);
        uint target = me.State.ObserverTarget;
        Assert.Equal(Handles[1].Value, target);

        r.Observers.SpecMode(me, null, 1f);
        Assert.Equal(((int)ObserverMode.InEye, target), (me.State.ObserverMode, me.State.ObserverTarget));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    public void Jointeam_a_playing_team_ends_observing_and_respawns(string team)
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Clients.SpecMode = "7";
        r.Observers.Spectate(me, 1f);

        Assert.Equal(JoinTeamResult.Playing, r.Observers.JoinTeam(me, team, 2f));

        Assert.Equal(0, me.State.ObserverMode);
        Assert.False(me.Observer.EnterObserver);
        Assert.Single(r.Engine.AllOf("Players.Respawn"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("x")]
    [InlineData(null)]
    public void Jointeam_with_no_such_team_is_refused_and_keeps_watching(string? team)
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Clients.SpecMode = "7";
        r.Observers.Spectate(me, 1f);

        Assert.Equal(JoinTeamResult.Invalid, r.Observers.JoinTeam(me, team, 2f));

        Assert.Equal((int)ObserverMode.Roaming, me.State.ObserverMode);
        Assert.Empty(r.Engine.AllOf("Players.Respawn"));
    }

    [Fact]
    public void Jointeam_one_is_spectate_and_a_playing_team_while_alive_respawns_nobody()
    {
        var r = new Rig();
        var me = r.Players[0];
        r.Clients.SpecMode = "6";

        Assert.Equal(JoinTeamResult.Playing, r.Observers.JoinTeam(me, "2", 1f));
        Assert.Empty(r.Engine.AllOf("Players.Respawn"));

        Assert.Equal(JoinTeamResult.Spectating, r.Observers.JoinTeam(me, "1", 1f));
        Assert.Equal((int)ObserverMode.PointOfInterest, me.State.ObserverMode);
    }

    [Fact]
    public void An_observer_is_not_respawned_by_a_button_and_a_dead_player_is()
    {
        var r = new Rig();
        r.Die(0, 1f);
        Assert.True(Observers.MayRespawn(r.Players[0]), "a dead HL2MP player comes back on a button");
        r.Clients.SpecMode = "7";
        r.Observers.Spectate(r.Players[0], 2f);
        Assert.False(Observers.MayRespawn(r.Players[0]), "CHL2MP_Player::PlayerDeathThink skips an observer");
    }
}
