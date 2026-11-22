using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;

using Xunit;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// CTFPlayer's observer (tf_player.cpp): the dead player watches once the
/// death cam is over (StateThinkDYING's StateTransition( TF_STATE_OBSERVER ),
/// :14010), starting in chase on the nearest teammate (StateEnterOBSERVER,
/// :13835); a spectator cycles in-eye, chase and roaming (SetObserverMode,
/// :13718, which skips point of interest and roaming for a player on a team).
/// Before these, a dead player saw the death cam until the respawn and the
/// spectator keys did nothing in it; a spectator started in the fixed mode a
/// classless welcome had left behind (live, ctf_2fort, 2026-11-15).
/// </summary>
public sealed class TFObserverTests
{
    private static readonly EntityHandle Watcher = new(0x0100_0001u);
    private static readonly EntityHandle Near = new(0x0100_0002u);
    private static readonly EntityHandle Far = new(0x0100_0003u);
    private static readonly EntityHandle Enemy = new(0x0100_0004u);

    private sealed class Hooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;

        public void Each(string what, Action<T> call)
        {
            foreach (T hook in all)
                call(hook);
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private sealed class Rig
    {
        public readonly RecordingEngine Engine;
        public readonly Observers Observers;
        public readonly TFObserverRules Rules;
        public readonly TFDeathCam DeathCam;
        public readonly Clock Clock = new();
        public readonly SpecFreezeTimeConVar FreezeTime;
        public readonly SpecFreezeTravelTimeConVar TravelTime = DeclaredConVars.Holding<SpecFreezeTravelTimeConVar>("0.4");
        public readonly Dictionary<EntityHandle, int> Teams = new()
        {
            [Watcher] = (int)TFTeam.Blue, [Near] = (int)TFTeam.Blue, [Far] = (int)TFTeam.Blue, [Enemy] = (int)TFTeam.Red,
        };
        public readonly Dictionary<EntityHandle, Vector> Origins = new()
        {
            [Watcher] = new Vector(0, 0, 0), [Near] = new Vector(100, 0, 0), [Far] = new Vector(900, 0, 0),
            [Enemy] = new Vector(10, 0, 0),
        };
        private readonly ScriptedEntityRegistry _registry;

        public Rig(bool rules = true, string freezeTime = "4.0")
        {
            FreezeTime = DeclaredConVars.Holding<SpecFreezeTimeConVar>(freezeTime);
            _registry = new ScriptedEntityRegistry(TestEngines.Server());
            _registry.RegisterAll(typeof(SsPlayer).Assembly);
            var services = Services(out Engine);
            var engine = Engine;
            Engine.Returns("Entities.IsValid", true)
                  .ReturnsEachCall("Entities.GetIndex", () => ((EntityHandle)engine.Calls[^1].Args[0]!).EntryIndex)
                  .Returns("Entities.GetEyeAngles", default(Vector))
                  .ReturnsEachCall("Players.GetTeam", () => Teams[(EntityHandle)engine.Calls[^1].Args[0]!])
                  .ReturnsEachCall("Entities.GetOrigin", () => Origins[(EntityHandle)engine.Calls[^1].Args[0]!])
                  .Ignores("Players.SetObserverMode", "PlayerMovement.SetMoveType", "EntityRender.SetVisible",
                           "Players.HolsterActiveWeapon", "Damage.SetTakeDamageMode", "Entities.Teleport",
                           "PlayerMovement.SnapEyeAngles", "EntityPhysics.SetNotSolid", "Players.RemoveAllItems",
                           "WorldQuery.EntityTraceHull");
            services.AddInstance<IEntityDirectory>(_registry);
            _registry.Services = services;
            foreach (var (handle, name) in new[] { (Watcher, "w"), (Near, "n"), (Far, "f"), (Enemy, "e") })
            {
                _registry.KeyValue(handle, EntityNames.Player, "targetname", name);
                Player(handle).State.BindLocal();
                Player(handle).State.Alive = true;
            }

            var log = new RecordingLog();
            var hooks = new List<IObserverRules>();
            Observers = new Observers(log, _registry, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                      DeclaredConVars.Fresh<ForceCameraConVar>(), DeclaredConVars.Fresh<AllowSpectatorsConVar>(),
                                      rules: new Hooks<IObserverRules>(rules ? [new Lazy(this)] : []));
            Rules = new TFObserverRules(_registry, Engine, Engine, new TFPlayerCarriers(), Observers,
                                        DeclaredConVars.Fresh<FadeToBlackConVar>(), TravelTime);
            DeathCam = new TFDeathCam(_registry, Observers, log, Clock, FreezeTime, TravelTime);
        }

        // The rules need the observers and the observers ask the rules: the
        // composition's IHooks is lazy the same way.
        private sealed class Lazy(Rig rig) : IObserverRules
        {
            public ObserverMode Filter(SsPlayer player, ObserverMode mode) => rig.Rules.Filter(player, mode);

            public ObserverMode Enter(SsPlayer player, ObserverMode asked, float now) => rig.Rules.Enter(player, asked, now);

            public bool ExitFreezeCam(SsPlayer player, float now) => rig.Rules.ExitFreezeCam(player, now);
        }

        public SsPlayer Player(EntityHandle handle) => (SsPlayer)_registry.Get(handle)!;

        public SsPlayer Me => Player(Watcher);

        public ObserverMode Mode => (ObserverMode)Me.State.ObserverMode;

        public SsPlayer? Target => Observers.TargetOf(Me);

        public void Die(float at) => Die(at, EntityHandle.None);

        public void Die(float at, EntityHandle killer)
        {
            Clock.Now = at;
            Me.State.Alive = false;
            DeathCam.OnPlayerDied(new PlayerDeath(Watcher, 1, killer, default, new EntityHandle(0x0100_0010u)));
        }
    }

    [Fact]
    public void The_death_cam_lasts_until_the_dying_is_over()
    {
        var r = new Rig();
        r.Die(at: 10f);

        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength - 0.1f);

        Assert.Equal(ObserverMode.DeathCam, r.Mode);
    }

    [Fact]
    public void A_dead_player_watches_in_chase_once_the_dying_is_over()
    {
        var r = new Rig();
        r.Die(at: 10f);

        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength);

        Assert.Equal(ObserverMode.Chase, r.Mode);
    }

    [Fact]
    public void A_dead_player_watches_the_nearest_teammate_first()
    {
        var r = new Rig();
        r.Die(at: 10f);

        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength);

        Assert.Same(r.Player(Near), r.Target);
    }

    [Fact]
    public void A_dead_player_cycles_chase_to_in_eye_and_never_roams()
    {
        var r = new Rig();
        r.Die(at: 10f);
        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength);

        var seen = new List<ObserverMode>();
        for (int i = 0; i < 3; i++)
        {
            r.Observers.SpecMode(r.Me, null, 20f);
            seen.Add(r.Mode);
        }

        // Chase +1 is point of interest, which is roaming outside Passtime, which
        // is in-eye on a team; in-eye +1 is chase.
        Assert.Equal([ObserverMode.InEye, ObserverMode.Chase, ObserverMode.InEye], seen);
    }

    [Fact]
    public void A_dead_player_steps_to_the_next_teammate()
    {
        var r = new Rig();
        r.Die(at: 10f);
        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength);
        var first = r.Target;

        r.Observers.SpecNext(r.Me, reverse: false, 20f);

        Assert.NotNull(first);
        Assert.NotNull(r.Target);
        Assert.NotSame(first, r.Target);
        Assert.Equal((int)TFTeam.Blue, r.Teams[r.Target!.Handle]);
    }

    // ---- the freeze cam (StateThinkDYING's freeze arm) -----------------------

    [Fact]
    public void A_death_by_a_player_watches_the_killer()
    {
        var r = new Rig();
        r.Die(at: 10f, killer: Enemy);

        Assert.Same(r.Player(Enemy), r.Target);
        Assert.Equal(ObserverMode.DeathCam, r.Mode);
    }

    [Fact]
    public void A_death_by_the_world_watches_nobody()
    {
        var r = new Rig();
        r.Me.State.ObserverTarget = Near.Value;   // a target left from before

        r.Die(at: 10f);

        Assert.Null(r.Target);
    }

    [Fact]
    public void The_freeze_cam_starts_on_the_killer_once_the_death_animation_is_over()
    {
        var r = new Rig();
        r.Die(at: 10f, killer: Enemy);

        r.DeathCam.Think(10f + TFDeathCam.DeathAnimationTime - 0.01f);
        Assert.Equal(ObserverMode.DeathCam, r.Mode);

        r.DeathCam.Think(10f + TFDeathCam.DeathAnimationTime);
        Assert.Equal(ObserverMode.FreezeCam, r.Mode);
        Assert.Same(r.Player(Enemy), r.Target);
    }

    [Fact]
    public void The_freeze_cam_holds_until_the_travel_and_the_freeze_are_over_then_watches()
    {
        var r = new Rig();
        r.Die(at: 10f, killer: Enemy);
        r.DeathCam.Think(12f);

        r.DeathCam.Think(16.39f);   // 10 + 2 + 0.4 + 4 = 16.4
        Assert.Equal(ObserverMode.FreezeCam, r.Mode);

        r.DeathCam.Think(16.4f);
        Assert.Equal(ObserverMode.Chase, r.Mode);
    }

    [Fact]
    public void The_freeze_times_are_the_convars()
    {
        var r = new Rig(freezeTime: "1");
        r.Die(at: 10f, killer: Enemy);
        r.DeathCam.Think(12f);

        r.DeathCam.Think(13.4f);   // 10 + 2 + 0.4 + 1

        Assert.Equal(ObserverMode.Chase, r.Mode);
    }

    [Fact]
    public void A_death_with_no_killer_never_freezes()
    {
        var r = new Rig();
        r.Die(at: 10f);

        r.DeathCam.Think(13f);

        Assert.Equal(ObserverMode.DeathCam, r.Mode);
    }

    [Fact]
    public void A_key_before_the_camera_arrives_does_not_leave_the_freeze_cam()
    {
        var r = new Rig();
        r.Die(at: 10f, killer: Enemy);
        r.DeathCam.Think(12f);

        r.Observers.SpecMode(r.Me, null, 12.8f);   // arrival: 10 + 2 + 0.4 + 0.5 = 12.9
        r.DeathCam.Think(12.85f);

        Assert.Equal(ObserverMode.FreezeCam, r.Mode);
    }

    [Fact]
    public void A_key_once_the_camera_has_arrived_aborts_into_watching()
    {
        var r = new Rig();
        r.Die(at: 10f, killer: Enemy);
        r.DeathCam.Think(12f);

        r.Observers.SpecNext(r.Me, reverse: false, 13f);
        r.DeathCam.Think(13.1f);

        Assert.Equal(ObserverMode.Chase, r.Mode);
        Assert.Equal((int)TFTeam.Blue, r.Teams[r.Target!.Handle]);
    }

    [Fact]
    public void A_respawned_player_is_not_moved_to_watching()
    {
        var r = new Rig();
        r.Die(at: 10f);
        r.Observers.Stop(r.Me);   // the wave's ForceRespawn

        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength);

        Assert.Equal(ObserverMode.None, r.Mode);
    }

    [Fact]
    public void A_spectator_starts_in_chase_not_the_welcome_fixed_mode()
    {
        var r = new Rig();
        r.Me.Observer.LastMode = ObserverMode.Fixed;   // the classless welcome's, stored by its Stop
        r.Teams[Watcher] = (int)TFTeam.Spectator;

        Assert.True(r.Observers.Spectate(r.Me, 5f));

        Assert.Equal(ObserverMode.Chase, r.Mode);
        Assert.NotNull(r.Target);
    }

    [Fact]
    public void A_spectator_cycles_in_eye_chase_and_roaming()
    {
        var r = new Rig();
        r.Teams[Watcher] = (int)TFTeam.Spectator;
        r.Observers.Spectate(r.Me, 5f);

        var seen = new List<ObserverMode>();
        for (int i = 0; i < 3; i++)
        {
            r.Observers.SpecMode(r.Me, null, 6f);
            seen.Add(r.Mode);
        }

        Assert.Equal([ObserverMode.Roaming, ObserverMode.InEye, ObserverMode.Chase], seen);
    }

    [Fact]
    public void A_spectator_may_watch_either_team()
    {
        var r = new Rig();
        r.Teams[Watcher] = (int)TFTeam.Spectator;

        Assert.Same(r.Player(Enemy), r.Rules.Nearest(r.Me, 5f));
    }

    [Fact]
    public void Fixed_with_a_target_is_in_eye()
    {
        var r = new Rig();
        r.Teams[Watcher] = (int)TFTeam.Spectator;
        r.Me.State.ObserverMode = (int)ObserverMode.Fixed;   // still in the welcome's fixed view

        Assert.Equal(ObserverMode.InEye, r.Rules.Enter(r.Me, ObserverMode.Fixed, 5f));
    }

    [Fact]
    public void A_dead_teamed_player_has_no_roaming()
        => Assert.Equal(ObserverMode.InEye, new Rig().Rules.Filter(new Rig().Me, ObserverMode.Roaming));

    [Fact]
    public void Point_of_interest_is_roaming_for_a_spectator()
    {
        var r = new Rig();
        r.Teams[Watcher] = (int)TFTeam.Spectator;

        Assert.Equal(ObserverMode.Roaming, r.Rules.Filter(r.Me, ObserverMode.PointOfInterest));
    }

    [Fact]
    public void A_dead_watcher_joining_a_team_is_not_respawned()
    {
        var r = new Rig();
        r.Engine.Ignores("Players.Respawn");
        r.Die(at: 10f);
        r.DeathCam.Think(10f + TeamRoundFlow.DeathAnimLength);

        r.Observers.JoinTeam(r.Me, "3", 20f);

        Assert.False(r.Engine.Called("Players.Respawn"), "only the wave brings a dead player back");
    }

    [Fact]
    public void No_two_frame_participants_of_the_server_mod_share_a_frame_slot()
    {
        // StageUniqueness refuses a collision when the frame driver is built,
        // and the managed server does not load (live: TFAutobalance and
        // TFDeathCam both at FrameOrder 0).
        var orders = typeof(TFDeathCam).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFrameLifecycle).IsAssignableFrom(t))
            .Select(t => (Type: t, Order: ((IFrameLifecycle)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).FrameOrder))
            .ToList();

        Assert.Contains(orders, o => o.Type == typeof(TFDeathCam));
        Assert.Empty(orders.GroupBy(o => o.Order).Where(g => g.Count() > 1)
                           .Select(g => string.Join(", ", g.Select(o => o.Type.Name))));
    }

    [Fact]
    public void The_server_mod_offers_its_observer_rules()
    {
        var types = typeof(TFObserverRules).Assembly.GetTypes()
            .Where(t => t.GetInterfaces().Contains(typeof(IObserverRules)))
            .ToList();

        Assert.Equal([typeof(TFObserverRules)], types);
    }
}
