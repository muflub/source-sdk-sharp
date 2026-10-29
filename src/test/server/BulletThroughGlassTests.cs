using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CBaseEntity::FireBullets' glass rule and HandleShotImpactingGlass
/// (baseentity_shared.cpp, ai_basenpc.cpp). A round that lands on the glass of
/// a func_breakable sends glass shards in. A trace back from 16 units beyond
/// finds the far side, which gets the round's mark and shards. Then the round
/// is fired again from there, unspread, for the rest of its distance. A
/// breakable the mapper made bulletproof, or a surface that is not glass,
/// stops it. Found in the glass check of 2026-10-28: the rule was ported
/// (BulletGlass) and nothing called it, so dm_lockdown's windows stopped every
/// round.
/// </summary>
public class BulletThroughGlassTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Window = new(0x0002_000Bu);
    private static readonly EntityHandle Wall = new(0x0002_000Cu);

    private static readonly Vector Eye = new(0f, 0f, 64f);
    private static readonly Vector Front = new(64f, 0f, 64f);
    private static readonly Vector Back = new(68f, 0f, 64f);

    private const int GlassProp = 28, ConcreteProp = 13;

    private sealed class Shards : TempEntsDouble
    {
        public readonly List<Vector> At = new();

        public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
        {
            if (effectName == "GlassImpact")
                At.Add(data.Origin);
        }
    }

    private sealed class Rig
    {
        public required RecordingEngine Engine;
        public required Shards Shards;

        public (Vector Start, Vector End)[] Traces => Engine.AllOf("WorldQuery.TraceLineSurface")
            .Select(c => (c.ArgAs<Vector>(0), c.ArgAs<Vector>(1))).ToArray();
    }

    private static Rig Fired(int surfaceProp = GlassProp, int spawnFlags = 0, bool farSide = true)
    {
        var answers = new Queue<SurfaceTraceResult>(new[]
        {
            new SurfaceTraceResult(true, 0.01f, Front, new Vector(-1f, 0f, 0f), Window, surfaceProp, 0),
            farSide
                ? new SurfaceTraceResult(true, 0.75f, Back, new Vector(1f, 0f, 0f), Window, surfaceProp, 0)
                : new SurfaceTraceResult(false, 1f, Front, Vector.Zero, EntityHandle.None, -1, 0),
            new SurfaceTraceResult(true, 0.05f, new Vector(200f, 0f, 64f), new Vector(-1f, 0f, 0f), Wall, ConcreteProp, 0),
        });

        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", Eye)
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f), new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("WorldQuery.PointContents", Contents.Empty)
            .Returns("Clock.Now", 10f);
        engine.ReturnsEachCall("WorldQuery.TraceLineSurface",
                               () => answers.Count > 0 ? answers.Dequeue() : new SurfaceTraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None, -1, 0));
        engine.ReturnsEachCall("WorldQuery.SurfacePropGameMaterial",
                               () => engine.Calls.Last().ArgAs<int>(0) == GlassProp ? SurfaceMaterial.Glass : SurfaceMaterial.Rocks);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entities);
        entities.Strict = false;
        entities.Returns("Entities.GetSpawnFlags", spawnFlags);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.KeyValue(Window, EntityNames.FuncBreakable, "material", "0");
        registry.KeyValue(Window, EntityNames.FuncBreakable, "health", "50");
        registry.Spawn(Window, EntityNames.FuncBreakable);

        var shards = new Shards();
        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups()) { Temps = shards };
        arsenal.GiveWeapon(Player, "weapon_pistol");

        engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd { Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
        Assert.True(engine.Called("Sound.EntityEmitSound"), "the pistol fired");
        return new Rig { Engine = engine, Shards = shards };
    }

    [Fact]
    public void A_round_through_a_glass_window_carries_on_from_behind_it()
    {
        var rig = Fired();

        var traces = rig.Traces;
        Assert.Equal(3, traces.Length);
        Assert.Equal(16f, (traces[1].Start - Front).Length, 3);
        Assert.Equal(Front, traces[1].End);
        Assert.Equal(Back, traces[2].Start);
        Assert.Equal(new[] { Front, Back }, rig.Shards.At);
        Assert.Contains(rig.Engine.AllOf("Damage.ApplyAt"), c => c.ArgAs<EntityHandle>(0) == Wall);
    }

    /// <summary>A way back that finds no far side (fraction 1) stops it, after the shards going in.</summary>
    [Fact]
    public void A_round_that_finds_no_far_side_stops()
    {
        var rig = Fired(farSide: false);

        Assert.Equal(2, rig.Traces.Length);
        Assert.Equal(new[] { Front }, rig.Shards.At);
    }

    [Fact]
    public void A_window_made_bulletproof_stops_the_round()
    {
        var rig = Fired(spawnFlags: BulletGlass.NoBulletPenetration);

        Assert.Single(rig.Traces);
        Assert.Empty(rig.Shards.At);
    }

    [Fact]
    public void A_breakable_that_is_not_glass_stops_the_round()
    {
        var rig = Fired(surfaceProp: ConcreteProp);

        Assert.Single(rig.Traces);
    }
}
