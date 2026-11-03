using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// A TF player's spawn around the core's kit (ForceRespawn + Spawn): the
/// desired class taken before the kit, every condition gone, the class's
/// health and model after it, a fresh crit bucket, and the classless player
/// an observer.
/// </summary>
public sealed class TFPlayerSpawnsTests
{
    private static readonly EntityHandle Weapon = new(0x0004_0060u);

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly TFPlayerConditions Conditions;
        public readonly TFCritBuckets Buckets = new();
        public readonly TFPlayerSpawns Spawns;
        public readonly TFPlayerClass Class;
        public readonly Observers Observers;
        public readonly ConVarRegistry ConVars;

        public Rig()
        {
            var engine = Player.Engine;
            engine.Returns("Players.GetObserverMode", ObserverMode.None);
            engine.Returns("Entities.GetIndex", PlayerRig.Index);

            var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
            convars.RegisterAll(typeof(GameRules).Assembly);
            convars.RegisterAll(typeof(TFPlayerSpawns).Assembly);
            ConVars = convars;
            var directory = new NoPlayers();
            Observers = new Observers(Player.Log, directory, engine, engine, engine, engine, engine, engine, engine, convars);
            Conditions = new TFPlayerConditions(Player.Carriers, engine);
            Class = new TFPlayerClass(Player.Carriers, Player.Items);
            Player.Extra.Add(Class);
            Player.Join();
            Spawns = new TFPlayerSpawns(Player.Carriers, Player.Classes, engine, engine, engine, Conditions, Buckets,
                                        directory, Observers, new Carried(Weapon),
                                        convars.Get<TFSpawnGlowsDurationConVar>()!, Player.Log);
        }

        public void Spawn()
        {
            Spawns.OnSpawning(PlayerRig.Player, 0f);
            Spawns.OnSpawned(PlayerRig.Player, 0f);
        }
    }

    private sealed class Carried(EntityHandle weapon) : ITFCarriedWeapons
    {
        public EntityHandle WeaponOf(EntityHandle player) => weapon;
    }

    private sealed class NoPlayers : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>The death's respawn-override pair does not survive the spawn
    /// (<c>CTFPlayer::Spawn</c>'s <c>SetRespawnOverride(-1.f, NULL_STRING)</c>,
    /// tf_player.cpp:1089); the spawn walk's named-spot arm reads it before
    /// this hook runs.</summary>
    [Fact]
    public void The_spawn_clears_the_respawn_override_pair()
    {
        var rig = new Rig();
        var ov = new TFRespawnOverride(rig.Player.Carriers, new TF2.Tests.Rules.FakeClock { Now = 10f });
        ov.Attach(PlayerRig.Player);
        ov.Apply(30f, "red_room");

        rig.Spawns.OnSpawning(PlayerRig.Player, 0f);

        Assert.False(ov.HasOverride);
        Assert.Null(ov.Location);
    }

    [Fact]
    public void The_desired_class_is_the_class_before_the_kit()
    {
        var rig = new Rig();
        rig.Class.Desired = TFClass.HeavyWeapons;

        rig.Spawns.OnSpawning(PlayerRig.Player, 0f);

        Assert.Equal(TFClass.HeavyWeapons, rig.Player.Carriers.ClassOf(PlayerRig.Player));
        Assert.Equal(1, rig.Class.ClassChanges);
    }

    [Fact]
    public void Respawning_as_the_same_class_is_no_class_change()
    {
        var rig = new Rig();
        rig.Player.Carriers.SetClass(PlayerRig.Index, TFClass.Scout);
        rig.Class.Desired = TFClass.Scout;

        rig.Spawn();

        Assert.Equal(0, rig.Class.ClassChanges);
    }

    [Fact]
    public void The_spawn_consumes_a_granted_instant_spawn()
    {
        // CTFPlayer::Spawn's m_bAllowInstantSpawn = false (tf_player.cpp:3710):
        // the grant game_forcerespawn gave is spent by the next spawn.
        var rig = new Rig();
        rig.Spawn();
        rig.Class.AllowInstantSpawn = true;

        rig.Spawn();

        Assert.False(rig.Class.AllowInstantSpawn);
    }

    [Fact]
    public void The_items_go_before_the_kit()
    {
        var rig = new Rig();
        rig.Class.Desired = TFClass.Scout;

        rig.Spawns.OnSpawning(PlayerRig.Player, 0f);

        Assert.Equal(1, rig.Player.Engine.CountOf("Players.RemoveAllItems"));
    }

    [Fact]
    public void Every_condition_goes_at_the_spawn()
    {
        var rig = new Rig();
        rig.Class.Desired = TFClass.Scout;
        rig.Conditions.Add(PlayerRig.Player, TFCond.Burning, 10f);
        Assert.True(rig.Player.State.InCond(TFCond.Burning));

        rig.Spawns.OnSpawning(PlayerRig.Player, 0f);

        Assert.True(rig.Player.State.Conditions().IsEmpty);
    }

    [Fact]
    public void A_spawn_fills_the_cloak_meter()
    {
        // CTFPlayer::Spawn's SetSpyCloakMeter( 100.0f ) (tf_player.cpp:3712):
        // every spawn starts with a full watch, whatever the last life left.
        var rig = new Rig();
        rig.Class.Desired = TFClass.Spy;
        rig.Player.State.CloakMeter = 12f;

        rig.Spawn();

        Assert.Equal(100f, rig.Player.State.CloakMeter);
    }

    [Fact]
    public void The_spawn_conditions_the_team_glow_for_the_convar()
    {
        // CTFPlayer::Spawn's AddCond( TF_COND_TEAM_GLOWS,
        // tf_spawn_glows_duration.GetInt() ) (tf_player.cpp:3635-3636).
        var rig = new Rig();
        rig.ConVars.Get<TFSpawnGlowsDurationConVar>()!.Bind(-1, "tf_spawn_glows_duration", "3");
        rig.Class.Desired = TFClass.Scout;

        rig.Spawn();

        Assert.True(rig.Conditions.InCond(PlayerRig.Player, TFCond.TeamGlows));
        Assert.Equal(3f, rig.Player.Carriers.ConditionsOf(PlayerRig.Index)!.Duration(TFCond.TeamGlows));
    }

    [Fact]
    public void A_classless_spawn_conditions_no_team_glow()
    {
        // The add sits in Spawn's TF_STATE_ACTIVE arm: the welcome/observer
        // spawn never reaches it.
        var rig = new Rig();

        rig.Spawn();

        Assert.False(rig.Conditions.InCond(PlayerRig.Player, TFCond.TeamGlows));
    }

    [Fact]
    public void The_team_glow_expires_after_its_duration()
    {
        var rig = new Rig();
        rig.Class.Desired = TFClass.Soldier;

        rig.Spawn();
        Assert.True(rig.Conditions.InCond(PlayerRig.Player, TFCond.TeamGlows));

        // The condition think's countdown, which the spawn's duration feeds
        // (TFPlayerClass.Tick's set.Tick).
        rig.Player.Carriers.ConditionsOf(PlayerRig.Index)!.Tick(10f, 0, default);

        Assert.False(rig.Conditions.InCond(PlayerRig.Player, TFCond.TeamGlows));
    }

    [Theory]
    [InlineData(TFClass.Scout, 125, "models/player/scout.mdl")]
    [InlineData(TFClass.HeavyWeapons, 300, "models/player/heavy.mdl")]
    public void The_class_spawns_with_its_health_and_model(TFClass cls, int health, string model)
    {
        var rig = new Rig();
        rig.Class.Desired = cls;

        rig.Spawn();

        Assert.Equal(health, rig.Player.Engine.AllOf("Entities.SetHealth").Single().ArgAs<int>(1));
        Assert.Equal(model, rig.Player.Engine.AllOf("EntityRender.SetModel").Single().ArgAs<string>(1));
    }

    [Fact]
    public void The_kits_weapon_gets_a_fresh_crit_bucket()
    {
        var rig = new Rig();
        rig.Class.Desired = TFClass.Scout;
        var bucket = rig.Buckets.For(PlayerRig.Index, typeId: 4, initialTokens: 300f);
        bucket.Add(500f, 1000f);
        Assert.Equal(800f, bucket.Tokens);

        rig.Spawn();

        Assert.Equal(0, rig.Buckets.Count);
        Assert.Equal(300f, rig.Buckets.For(PlayerRig.Index, typeId: 4, initialTokens: 300f).Tokens);
    }

    [Fact]
    public void A_classless_player_observes_with_no_weapons()
    {
        var rig = new Rig();

        rig.Spawn();

        var strips = rig.Player.Engine.AllOf("Players.RemoveAllItems").ToList();
        Assert.Equal(2, strips.Count);   // ForceRespawn's, then the observer's
        Assert.True(strips[^1].ArgAs<bool>(1));
        Assert.Equal(0, rig.Player.Engine.CountOf("Entities.SetHealth"));
        Assert.Equal(0, rig.Player.Engine.CountOf("EntityRender.SetModel"));
    }

    [Theory]
    [InlineData(TFTeam.Unassigned, ObserverMode.Fixed)]
    [InlineData(TFTeam.Red, ObserverMode.Chase)]
    public void A_classless_managed_player_watches_fixed_before_a_team_and_chasing_on_one(TFTeam team, ObserverMode mode)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = TestServices.Services(out var engine);
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", PlayerRig.Index)
              .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
              .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
              .Returns("Players.GetTeam", (int)team)
              .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(new Vector(0f, 0f, 0f), false, false))
              .Ignores("Players.SetObserverMode", "PlayerMovement.SetMoveType", "EntityRender.SetVisible",
                       "Players.HolsterActiveWeapon", "Damage.SetTakeDamageMode", "Entities.Teleport",
                       "PlayerMovement.SnapEyeAngles", "EntityPhysics.SetNotSolid", "Players.RemoveAllItems");
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        registry.KeyValue(PlayerRig.Player, EntityNames.Player, "targetname", "p0");
        var watcher = (SsPlayer)registry.Get(PlayerRig.Player)!;
        watcher.State.BindLocal();
        watcher.State.Alive = true;
        watcher.State.ObserverTarget = EntityHandle.None.Value;

        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        convars.RegisterAll(typeof(TFPlayerSpawns).Assembly);
        var log = new RecordingLog();
        var observers = new Observers(log, registry, engine, engine, engine, engine, engine, engine, engine, convars);
        var carriers = new TFPlayerCarriers();
        var spawns = new TFPlayerSpawns(carriers, new PlayerRig().Classes, engine, engine, engine,
                                        new TFPlayerConditions(carriers, engine), new TFCritBuckets(),
                                        registry, observers, new Carried(EntityHandle.None),
                                        convars.Get<TFSpawnGlowsDurationConVar>()!, log);

        spawns.OnSpawned(PlayerRig.Player, 1f);

        // The chase cam with nobody to chase settles where CheckObserverSettings
        // puts it; what the player asked for is the mode the log names.
        Assert.NotEqual((int)ObserverMode.None, watcher.State.ObserverMode);
        Assert.Contains(log.Lines, l => l.Text.Contains($"has no class: observing ({mode})"));
    }
}
