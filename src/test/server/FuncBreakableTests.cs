using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_breakable against stock's CBreakable (func_break.cpp). Found
/// 2026-10-28: ours fired OnBreak and removed itself, and nothing else. No
/// noise, no gibs, no explosion, no damage modifiers or propdata. A
/// func_breakable with no health was breakable at 1 where stock's takes no
/// damage.
/// </summary>
public class FuncBreakableTests
{
    private static readonly EntityHandle Brush = new(0x0010_0101u);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const string File = @"
""PropData""
{
    ""Glass.Base""   { ""dmg.bullets"" ""1.0"" ""dmg.club"" ""1.0"" ""dmg.explosive"" ""0.1"" ""health"" ""5"" }
    ""Glass.Window"" { ""base"" ""Glass.Base"" ""dmg.explosive"" ""1.0"" ""dmg.bullets"" ""0.5"" ""health"" ""8"" }
    ""Stone.Huge""   { ""health"" ""200"" ""explosive_damage"" ""80"" ""explosive_radius"" ""128"" }
    ""BreakableModels""
    {
        ""GlassChunks"" { ""models/gibs/glass_shard01.mdl"" ""1"" }
        ""WoodChunks""  { ""models/gibs/wood_gib01a.mdl"" ""1"" }
    }
}
";

    private sealed class Gibs : TempEntsDouble
    {
        public readonly List<(QAngle Angles, Vector Size, Vector Velocity, int Model, int Flags)> Thrown = new();

        public override void BreakModel(TeFilter filter, float delay, Vector position, QAngle angles,
                                        Vector size, Vector velocity, int modelIndex,
                                        int randomization, int count, float time, int flags)
            => Thrown.Add((angles, size, velocity, modelIndex, flags));
    }

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required Gibs Gibs;

        // The engine's half of health: the last SetHealth, less what blows took since.
        private int _setsSeen, _set, _takenSince;

        public int Health
        {
            get
            {
                var sets = Engine.AllOf("Entities.SetHealth").ToArray();
                if (sets.Length != _setsSeen)
                {
                    _setsSeen = sets.Length;
                    _set = sets[^1].ArgAs<int>(1);
                    _takenSince = 0;
                }

                return _set - _takenSince;
            }
        }

        public BreakableBrush Entity => (BreakableBrush)Registry.Get(Brush)!;

        public int Hit(float damage, DamageType type, EntityHandle attacker = default)
        {
            int taken = Registry.Damaged(Brush, attacker, damage, (int)type);
            _ = Health;
            _takenSince += taken;
            if (Health <= 0 && taken > 0)
                Registry.Killed(Brush, attacker);
            return taken;
        }

        public string[] Sounds => Engine.AllOf("Sound.EntityEmitSoundAs").Select(c => c.ArgAs<string>(1)).ToArray();
    }

    private static Rig Spawned(params (string Key, string Value)[] keys)
        => Spawned(0, keys);

    private static Rig Spawned(int spawnFlags, params (string Key, string Value)[] keys)
        => SpawnedAs(EntityNames.FuncBreakable, spawnFlags, keys);

    private static Rig SpawnedAs(string className, int spawnFlags, params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);
        var gibs = new Gibs();
        services.AddInstance<ITempEnts>(gibs);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncBreakable).Assembly);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine, Gibs = gibs };
        engine.Returns("Files.ReadGameFile", File)
              .Returns("Entities.GetBoundsSize", new Vector(64f, 64f, 8f))
              .Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 32f))
              .Returns("Precache.PrecacheModel", 9)
              .ReturnsEachCall("Entities.GetHealth", () => rig.Health)
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player);
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        ((PropData)services.Require<IServerPropData>()).Load();

        engine.Returns("EntityPhysics.InitPhysics", true);
        registry.KeyValue(Brush, className, "origin", "0 0 0");
        foreach (var (key, value) in keys)
            registry.KeyValue(Brush, className, key, value);
        registry.Spawn(Brush, className);
        return rig;
    }

    /// <summary>func_breakdmg_bullet 0.5, func_breakdmg_club 1.5, func_breakdmg_explosive 1.25.</summary>
    [Theory]
    [InlineData(DamageType.Bullet, 10)]
    [InlineData(DamageType.Club, 30)]
    [InlineData(DamageType.Blast, 25)]
    public void Its_damage_is_scaled_by_the_func_breakdmg_convars(DamageType type, int taken)
        => Assert.Equal(taken, Spawned(("health", "100"), ("material", "1")).Hit(20f, type));

    /// <summary>propdata 24 is Glass.Window, over Glass.Base: bullets 0.5, club 1.0, explosive 1.0, health 8.</summary>
    [Fact]
    public void Its_propdata_section_sets_its_modifiers_and_health()
    {
        var rig = Spawned(("health", "100"), ("propdata", "24"));

        Assert.Equal(8, rig.Health);
        Assert.Equal(2, rig.Hit(4f, DamageType.Bullet));
        Assert.Equal(4, rig.Hit(4f, DamageType.Club));
    }

    [Fact]
    public void With_no_health_it_takes_no_damage_and_glass_keeps_one()
    {
        var glass = Spawned(("health", "0"), ("material", "0"));
        Assert.Equal(1, glass.Health);
        Assert.Equal(0, glass.Hit(50f, DamageType.Bullet));
        Assert.False(glass.Entity.TakesDamage);
        Assert.Equal(TakeDamageMode.No, glass.Engine.Last("Damage.SetTakeDamageMode")!.Value.ArgAs<TakeDamageMode>(1));

        var trigger = Spawned(1, ("health", "50"));
        Assert.Equal(0, trigger.Hit(50f, DamageType.Bullet));
    }

    [Fact]
    public void Unbreakable_glass_does_not_break()
    {
        var rig = Spawned(("health", "5"), ("material", "7"));

        rig.Registry.Input(Brush, "Break", "", 0f, EntityHandle.None);

        Assert.Equal(0, rig.Entity.OnBreak.FireCount);
        Assert.Equal(0, rig.Hit(50f, DamageType.Bullet));
    }

    /// <summary>
    /// Die: the material's noise, gibs from its chunk group (64x64x8: 11 of
    /// them, the first a master and the rest slaves), not solid, OnBreak from
    /// whoever broke it, and gone a tenth later.
    /// </summary>
    [Fact]
    public void Breaking_sounds_throws_its_gibs_goes_not_solid_and_is_removed()
    {
        var rig = Spawned(("health", "5"), ("material", "0"));

        rig.Hit(10f, DamageType.Club, attacker: Player);

        Assert.Contains("Breakable.Glass", rig.Sounds);
        Assert.Equal(11, rig.Gibs.Thrown.Count);
        Assert.Equal(0x01, rig.Gibs.Thrown[0].Flags);
        Assert.All(rig.Gibs.Thrown.Skip(1), g => Assert.Equal(0x01 | 0x80, g.Flags));
        Assert.All(rig.Gibs.Thrown, g => Assert.Equal(9, g.Model));
        Assert.True(rig.Engine.Last("EntityPhysics.SetNotSolid")!.Value.ArgAs<bool>(1));
        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
        Assert.True(rig.Registry.Directory.Think.IsScheduled(Brush, "BreakableRemove"));
    }

    [Theory]
    [InlineData("1", 0)]
    [InlineData("3", 5)]
    [InlineData("0", 11)]
    public void Its_performance_mode_trims_its_gibs(string mode, int gibs)
    {
        var rig = Spawned(("health", "5"), ("PerformanceMode", mode));

        rig.Registry.Input(Brush, "Break", "", 0f, EntityHandle.None);

        Assert.Equal(gibs, rig.Gibs.Thrown.Count);
    }

    [Fact]
    public void A_propdata_with_a_radius_explodes_as_it_breaks()
    {
        var stone = Spawned(("propdata", "15"));
        stone.Registry.Input(Brush, "Break", "", 0f, EntityHandle.None);
        Assert.True(stone.Engine.Called("Effects.ExplosionEffect"));

        var plain = Spawned(("health", "5"), ("explodemagnitude", "80"));
        plain.Registry.Input(Brush, "Break", "", 0f, EntityHandle.None);
        Assert.False(plain.Engine.Called("Effects.ExplosionEffect"));
    }

    [Fact]
    public void A_blow_that_does_not_break_it_makes_a_shard_noise()
    {
        var rig = Spawned(("health", "100"), ("material", "1"));

        rig.Hit(5f, DamageType.Club);

        Assert.Equal(new[] { "Breakable.MatWood" }, rig.Sounds);
        Assert.Equal(0, rig.Entity.OnBreak.FireCount);
    }

    [Fact]
    public void SetHealth_reports_the_ratio_and_breaks_at_none()
    {
        var rig = Spawned(("health", "40"));

        rig.Registry.Input(Brush, "SetHealth", "10", 10f, EntityHandle.None);
        Assert.Equal("0.25", rig.Entity.OnHealthChanged.LastValue);
        Assert.Equal(0, rig.Entity.OnBreak.FireCount);

        rig.Registry.Input(Brush, "RemoveHealth", "10", 10f, EntityHandle.None);
        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
    }

    /// <summary>SF_BREAK_TOUCH: a player running into it at 500 does 5, enough for health 5, and is cut for a quarter.</summary>
    [Fact]
    public void A_player_running_into_plate_glass_crashes_through()
    {
        var rig = Spawned(2, ("health", "5"));
        rig.Engine.Returns("Entities.GetVelocity", new Vector(500f, 0f, 0f));
        Assert.True(rig.Entity.TouchEveryTick);

        rig.Entity.OnTouch(Player);

        var hits = rig.Engine.AllOf("Damage.TakeDamage").Select(c => (c.ArgAs<EntityHandle>(0), c.ArgAs<float>(2), c.ArgAs<DamageType>(3))).ToArray();
        Assert.Equal(new[] { (Brush, 5f, DamageType.Crush), (Player, 1.25f, DamageType.Slash) }, hits);
    }

    [Fact]
    public void A_player_walking_into_it_does_not()
    {
        var rig = Spawned(2, ("health", "5"));
        rig.Engine.Returns("Entities.GetVelocity", new Vector(200f, 0f, 0f));

        rig.Entity.OnTouch(Player);

        Assert.False(rig.Engine.Called("Damage.TakeDamage"));
    }

    //-------------------------------------------------------------------------
    // func_pushable: CPushable, a CBreakable that is simulated.
    //-------------------------------------------------------------------------

    /// <summary>CPushable::CreateVPhysics: simulated at a mass of 30, breakable or not.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0x80)]
    public void A_pushable_is_simulated_at_a_mass_of_thirty(int flags)
    {
        var rig = SpawnedAs(EntityNames.FuncPushable, flags, ("health", "20"));

        Assert.True(rig.Engine.Called("EntityPhysics.InitPhysics"));
        Assert.Equal(30f, rig.Engine.Last("EntityPhysics.SetMass")!.Value.ArgAs<float>(1));
    }

    /// <summary>SF_PUSH_BREAKABLE: a CBreakable, its damage scaled, its break noisy and in gibs.</summary>
    [Fact]
    public void A_breakable_pushable_breaks_as_a_func_breakable_does()
    {
        var rig = SpawnedAs(EntityNames.FuncPushable, 0x80, ("health", "20"), ("material", "1"));

        Assert.Equal(30, rig.Hit(20f, DamageType.Club));

        Assert.Contains("Breakable.Crate", rig.Sounds);
        Assert.NotEmpty(rig.Gibs.Thrown);
        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
    }

    /// <summary>Without the flag it takes no damage (CPushable::OnTakeDamage), but its Break input still breaks it.</summary>
    [Fact]
    public void Furniture_takes_no_damage_but_breaks_when_told()
    {
        var rig = SpawnedAs(EntityNames.FuncPushable, 0, ("health", "20"), ("material", "1"));

        Assert.Equal(0, rig.Hit(100f, DamageType.Club));
        Assert.Equal(0, rig.Entity.OnBreak.FireCount);

        rig.Registry.Input(Brush, "Break", "", 0f, EntityHandle.None);
        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
        Assert.NotEmpty(rig.Gibs.Thrown);
    }
}
