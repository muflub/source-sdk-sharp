using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>A struck entity that answers the traced hit itself, as stock's CBreakableSurface does.</summary>
[ScriptedEntity("test_trace_attacked")]
public sealed class TestTraceAttacked : ScriptedEntity, ITraceAttacked
{
    public readonly List<(float Damage, Vector End)> Hits = new();

    public void TraceAttack(EntityHandle attacker, float damage, DamageType damageType, Vector start, Vector end)
        => Hits.Add((damage, end));

    public bool ShotThrough(Vector start, Vector end, out Vector hit)
    {
        hit = default;
        return false;
    }
}

/// <summary>
/// CBaseEntity::FireBullets' DispatchTraceAttack and TraceAttackToTriggers
/// (baseentity_shared.cpp), through the real trigger pull: a round that lands
/// in an entity answering the traced hit itself hands it the hit rather than
/// damaging it, and every trigger along the round's path that answers a shot
/// takes it too. Found in a glass A/B on ss_sandbox (2026-10-28): every shot
/// after the first through a broken pane did nothing here.
/// </summary>
public class BulletTraceAttackTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Struck = new(0x0002_0009u);
    private static readonly Vector Landed = new(64f, 0f, 64f);

    /// <summary>A trigger across every shot, recording where each crossed it.</summary>
    private sealed class Across : ITraceAttacked
    {
        public readonly List<(Vector Start, Vector End)> Shots = new();

        public void TraceAttack(EntityHandle attacker, float damage, DamageType damageType, Vector start, Vector end)
            => Shots.Add((start, end));

        public bool ShotThrough(Vector start, Vector end, out Vector hit)
        {
            hit = (start + end) * 0.5f;
            return true;
        }
    }

    private static (Arsenal Arsenal, RecordingEngine Engine, ScriptedEntityRegistry Registry) Armed(
        IEntityRoster<ITraceAttacked> triggers, string weapon = "weapon_pistol", bool hit = true)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f), new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(hit, hit ? 0.001f : 1f, hit ? Landed : new Vector(75f, 0f, 64f),
                                            new Vector(-1f, 0f, 0f), hit ? Struck : EntityHandle.None, 13, 0, HitGroup.Generic))
            .Returns("WorldQuery.PointContents", Contents.Empty)
            .Returns("WorldQuery.TraceBox", new SurfaceTraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None, -1, 0))
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.RegisterAll(typeof(BulletTraceAttackTests).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.Spawn(Struck, "test_trace_attacked");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(), traceAttacked: triggers);
        arsenal.GiveWeapon(Player, weapon);
        return (arsenal, engine, registry);
    }

    private static void Fire(Arsenal arsenal, RecordingEngine engine)
    {
        engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd { Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
        Assert.True(engine.Called("Sound.EntityEmitSound"), "the pistol fired");
    }

    [Fact]
    public void A_round_hands_the_traced_hit_to_what_answers_it_instead_of_damaging_it()
    {
        var (arsenal, engine, registry) = Armed(new EntityRoster<ITraceAttacked>());

        Fire(arsenal, engine);

        var hit = Assert.Single(((TestTraceAttacked)registry.Get(Struck)!).Hits);
        Assert.Equal(Landed, hit.End);
        Assert.True(hit.Damage > 0f);
        Assert.False(engine.Called("Damage.ApplyAt"), "and it is not damaged as well");
    }

    [Fact]
    public void A_round_crosses_the_triggers_along_its_path()
    {
        var across = new Across();
        var triggers = new EntityRoster<ITraceAttacked>();
        triggers.Add(across);
        var (arsenal, engine, _) = Armed(triggers);

        Fire(arsenal, engine);

        var shot = Assert.Single(across.Shots);
        Assert.Equal(new Vector(0f, 0f, 64f), shot.Start);
        Assert.Equal((new Vector(0f, 0f, 64f) + Landed) * 0.5f, shot.End);
    }

    [Fact]
    public void A_swing_hands_the_traced_hit_to_what_answers_it()
    {
        var (arsenal, engine, registry) = Armed(new EntityRoster<ITraceAttacked>(), "weapon_crowbar");

        Fire(arsenal, engine);

        Assert.Single(((TestTraceAttacked)registry.Get(Struck)!).Hits);
        Assert.False(engine.Called("Damage.ApplyAt"));
    }

    /// <summary>Swing walks the triggers along its line, and Hit along the ray again.</summary>
    [Fact]
    public void A_swing_that_connects_crosses_the_triggers_twice_and_a_miss_once()
    {
        var connects = new Across();
        var triggers = new EntityRoster<ITraceAttacked>();
        triggers.Add(connects);
        var (arsenal, engine, _) = Armed(triggers, "weapon_crowbar");
        Fire(arsenal, engine);
        Assert.Equal(2, connects.Shots.Count);

        var misses = new Across();
        var missTriggers = new EntityRoster<ITraceAttacked>();
        missTriggers.Add(misses);
        var (missArsenal, missEngine, _) = Armed(missTriggers, "weapon_crowbar", hit: false);
        Fire(missArsenal, missEngine);
        Assert.Single(misses.Shots);
    }
}
