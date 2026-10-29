using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CGameRules::RadiusDamage (gamerules.cpp): a victim the blast's trace
/// reaches takes a traced hit (DispatchTraceAttack). An entity that answers
/// one itself, a pane of glass, takes it in place of the damage, and a glass
/// window blows out whole. The triggers along each victim's line that answer
/// a shot take it too (TraceAttackToTriggers). Found in the glass check of
/// 2026-10-28: a grenade by a window only cracked it here.
/// </summary>
public class BlastTraceAttackTests
{
    private static readonly EntityHandle Pane = new(0x0003_0101u);
    private static readonly EntityHandle Attacker = new(0x0001_0001u);

    private sealed class Across : ITraceAttacked
    {
        public int Crossed;

        public void TraceAttack(EntityHandle attacker, float damage, DamageType damageType, Vector start, Vector end)
            => Crossed++;

        public bool ShotThrough(Vector start, Vector end, out Vector hit)
        {
            hit = end;
            return true;
        }
    }

    private static (RecordingEngine Engine, ScriptedEntityRegistry Registry) Blasted(bool reached,
        IEntityRoster<ITraceAttacked> triggers)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(BlastTraceAttackTests).Assembly);
        registry.Services = Services(out var entities);
        entities.Strict = false;
        registry.Spawn(Pane, "test_trace_attacked");

        var engine = new RecordingEngine { Strict = false }.FillsSphere(Pane);
        engine.Returns("Entities.GetIndex", 5)
              .Returns("WorldQuery.TraceLine", new TraceResult(true, reached ? 0.5f : 1f, new Vector(50f, 0f, 0f),
                                                               new Vector(-1f, 0f, 0f), reached ? Pane : EntityHandle.None));
        var sounds = new SoundEnt(engine, engine, engine);
        sounds.Initialize(1, 0f);

        new BlastSweep(new RecordingLog(), engine, engine, engine, engine, engine, sounds,
                       directory: registry, traceAttacked: triggers)
            .Apply(Vector.Zero, 100f, 200f, DamageType.Blast, Attacker, Attacker, EntityHandle.None);
        return (engine, registry);
    }

    [Fact]
    public void A_victim_the_blast_reaches_that_answers_a_traced_hit_takes_one_instead_of_damage()
    {
        var (engine, registry) = Blasted(reached: true, new EntityRoster<ITraceAttacked>());

        Assert.Single(((TestTraceAttacked)registry.Get(Pane)!).Hits);
        Assert.False(engine.Called("Damage.TakeDamage"));
    }

    [Fact]
    public void One_the_blast_does_not_reach_by_trace_is_damaged()
    {
        var (engine, registry) = Blasted(reached: false, new EntityRoster<ITraceAttacked>());

        Assert.Empty(((TestTraceAttacked)registry.Get(Pane)!).Hits);
        Assert.True(engine.Called("Damage.TakeDamage"));
    }

    [Fact]
    public void The_triggers_along_a_victims_line_take_the_blast()
    {
        var across = new Across();
        var triggers = new EntityRoster<ITraceAttacked>();
        triggers.Add(across);

        Blasted(reached: false, triggers);

        Assert.Equal(1, across.Crossed);
    }
}
