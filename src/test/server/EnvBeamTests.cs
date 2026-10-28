using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// env_beam against stock's CEnvBeam (EnvBeam.cpp) and CBeam's server half
/// (beam_shared.cpp). Found beside stock (2026-10-27): only the beam that
/// stays was here, with no damage, touch, sparks, decals or inputs; a beam
/// with a life lit and stayed instead of striking; no texture warned and
/// stayed instead of going.
/// </summary>
public class EnvBeamTests
{
    private static readonly EntityHandle Owner = new(0x0004_0602u);
    private static readonly EntityHandle Drawn = new(0x0004_0601u);
    private static readonly EntityHandle StartPoint = new(0x0004_0610u);
    private static readonly EntityHandle EndProp = new(0x0004_0611u);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private sealed class Beams : TempEntsDouble
    {
        public readonly List<string> Sent = new();
        public TeBeamLook Last;

        public override void BeamPoints(TeFilter filter, float delay, Vector start, Vector end, in TeBeamLook look)
        {
            Sent.Add($"points {start} {end}");
            Last = look;
        }

        public override void BeamEntPoint(TeFilter filter, float delay, int startEntity, Vector start, int endEntity,
                                          Vector end, in TeBeamLook look)
        {
            Sent.Add($"entpoint {startEntity} {endEntity}");
            Last = look;
        }

        public override void BeamEnts(TeFilter filter, float delay, int startEntity, int endEntity, in TeBeamLook look)
        {
            Sent.Add($"ents {startEntity} {endEntity}");
            Last = look;
        }

        public override void BeamRing(TeFilter filter, float delay, int startEntity, int endEntity, in TeBeamLook look,
                                      int flags = 0)
        {
            Sent.Add($"ring {startEntity} {endEntity}");
            Last = look;
        }
    }

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required Beams Temps;
        public required RecordingDecals Decals;
        public float Now = 10f;
        public TraceResult Trace;
        public readonly Queue<TraceResult> Traces = new();
        public readonly Queue<float> Floats = new();

        public EnvBeam Entity => (EnvBeam)Registry.Get(Owner)!;
        public SsBeam? Beam => Registry.Get(Drawn) as SsBeam;
        public readonly ThinkScheduler Thinks = new();
        private readonly List<ScheduledThink> _due = new();

        /// <summary>The engine's dispatch: what is due runs, and taking it off the schedule is the engine's.</summary>
        public void Think()
        {
            _due.Clear();
            Thinks.TakeDue(Now, _due);
            foreach (var due in _due)
            {
                if (due.Context is null)
                    Entity.OnThink();
                else
                    Entity.OnThink(due.Context);
            }
        }

        public void Strike() => Entity.OnThink("Strike");
        public void Input(string name, string value = "") => Registry.Input(Owner, name, value, float.TryParse(value, out float f) ? f : 0f, EntityHandle.None);

        public EngineCall[] Damage => Engine.AllOf("Damage.ApplyAt").ToArray();
    }

    private static Rig Spawned(int flags, params (string Key, string Value)[] keys)
        => Spawned(flags, named: true, keys);

    private static Rig Spawned(int flags, bool named, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvBeam).Assembly);
        var services = Services(out var engine);
        var temps = new Beams();
        var decals = new RecordingDecals().Answers("Scorch", 4);
        services.AddInstance<ITempEnts>(temps);
        services.AddInstance<IServerDecals>(decals);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine, Temps = temps, Decals = decals };
        bool made = false;
        var names = new Dictionary<string, EntityHandle> { ["src"] = StartPoint, ["dst"] = EndProp };
        var origins = new Dictionary<EntityHandle, Vector>
        {
            [StartPoint] = new(0f, 0f, 0f), [EndProp] = new(100f, 0f, 0f), [Player] = new(50f, 0f, -36f),
        };
        engine.Returns("Precache.PrecacheModel", 7)
              .Returns("Entities.GetSpawnFlags", flags)
              .ReturnsEachCall("Clock.Now", () => rig.Now)
              .ReturnsEachCall("Host.RandomFloat", () => rig.Floats.Count > 0 ? rig.Floats.Dequeue() : 0f)
              .Returns("Host.RandomInt", 0)
              .ReturnsEachCall("Entities.IsValid", () =>
                  engine.Calls.Last().ArgAs<EntityHandle>(0) is var h && h != EntityHandle.None && (made || h != Drawn))
              .ReturnsEachCall("Entities.Create", () => { made = true; return Drawn; })
              .ReturnsEachCall("Entities.FindByName", () => names.GetValueOrDefault((string)engine.Calls.Last().Args[0]!, EntityHandle.None))
              .ReturnsEachCall("Entities.FindAllByName", () =>
                  names.TryGetValue((string)engine.Calls.Last().Args[0]!, out var h) ? new List<EntityHandle> { h } : new List<EntityHandle>())
              .ReturnsEachCall("Entities.GetOrigin", () => origins.GetValueOrDefault(engine.Calls.Last().ArgAs<EntityHandle>(0)))
              .ReturnsEachCall("EntityRender.GetModelName", () =>
                  engine.Calls.Last().ArgAs<EntityHandle>(0) == StartPoint ? "" : "models/props_c17/oildrum001.mdl")
              .ReturnsEachCall("Entities.GetClassName", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == EndProp ? EntityNames.PropPhysics : "info_target")
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player)
              .ReturnsEachCall("Entities.GetIndex", () => (int)(engine.Calls.Last().ArgAs<EntityHandle>(0).Value & 0x0FFF))
              .ReturnsEachCall("Entities.GetBounds", () => (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)))
              .Returns("Entities.GetAngles", Vector.Zero)
              .ReturnsEachCall("WorldQuery.TraceLine", () => rig.Traces.Count > 0 ? rig.Traces.Dequeue() : rig.Trace)
              .Returns("WorldQuery.EntitiesInSphere.into", new[] { Player });

        registry.Spawn(Drawn, EntityNames.Beam);
        made = false;
        if (named)
            registry.KeyValue(Owner, EntityNames.EnvBeam, "targetname", "beam");
        registry.KeyValue(Owner, EntityNames.EnvBeam, "texture", "sprites/laserbeam");
        registry.KeyValue(Owner, EntityNames.EnvBeam, "LightningStart", "src");
        registry.KeyValue(Owner, EntityNames.EnvBeam, "LightningEnd", "dst");
        foreach (var (key, value) in keys)
            registry.KeyValue(Owner, EntityNames.EnvBeam, key, value);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Owner, EntityNames.EnvBeam);
        return rig;
    }

    //-------------------------------------------------------------------------
    // Spawn
    //-------------------------------------------------------------------------

    [Fact]
    public void A_beam_with_no_texture_goes()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvBeam).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        registry.Spawn(Owner, EntityNames.EnvBeam);
        registry.Directory.FreeRemoved();

        Assert.Null(registry.Get(Owner));
    }

    [Fact]
    public void The_texture_is_precached_as_its_material()
    {
        var rig = Spawned(0);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Precache.PrecacheModel" && (string)c.Args[0]! == "sprites/laserbeam.vmt");
    }

    [Fact]
    public void Noise_width_and_scroll_are_held_to_stocks_limits()
    {
        var rig = Spawned(1, ("NoiseAmplitude", "90"), ("BoltWidth", "200"), ("TextureScroll", "150"));

        Assert.Equal(64f, rig.Beam!.State.Noise);
        Assert.Equal(102.3f, rig.Beam.State.Width, 3);
        Assert.Equal(100f, rig.Beam.State.ScrollRate);
    }

    [Fact]
    public void A_named_beam_not_started_on_waits_hidden()
    {
        var rig = Spawned(0);

        Assert.False(rig.Beam!.Visible);
        Assert.Equal(-1f, rig.Entity.NextThinkDelay());
    }

    [Fact]
    public void An_unnamed_beam_draws_from_the_start()
    {
        var rig = Spawned(0, named: false);

        Assert.True(rig.Beam!.Visible);
        Assert.Equal(0f, rig.Entity.NextThinkDelay());
    }

    [Fact]
    public void Turning_it_on_shows_it_and_sparks_at_the_flagged_ends()
    {
        var rig = Spawned(0x10);

        rig.Input("TurnOn");

        Assert.True(rig.Beam!.Visible);
        var sparks = rig.Engine.AllOf("Effects.SparkEffect").ToArray();
        Assert.Single(sparks);
        Assert.Equal(new Vector(0f, 0f, 0f), sparks[0].ArgAs<Vector>(0));
        Assert.False(sparks[0].ArgAs<bool>(3), "no sound: g_pEffects->Sparks plays none");
    }

    [Fact]
    public void The_beam_follows_an_entity_end_and_holds_a_point_end()
    {
        var rig = Spawned(1);

        rig.Think();

        Assert.Equal(new Vector(0f, 0f, 0f), rig.Beam!.State.Start);
        Assert.Equal(new Vector(100f, 0f, 0f), rig.Beam.State.End);
    }

    //-------------------------------------------------------------------------
    // Damage, decals and touch
    //-------------------------------------------------------------------------

    [Fact]
    public void Damage_is_dealt_every_tenth_of_a_second_for_the_time_since_the_last()
    {
        var rig = Spawned(1, ("damage", "50"));
        rig.Trace = new TraceResult(true, 0.5f, new Vector(50f, 0f, 0f), Vector.Zero, Player);

        rig.Now = 10.05f;
        rig.Think();
        Assert.Empty(rig.Damage);

        rig.Now = 10.2f;
        rig.Think();
        var hit = Assert.Single(rig.Damage);
        Assert.Equal(Player, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(50f * 0.2f, hit.ArgAs<float>(2), 3);
        Assert.Equal(DamageType.EnergyBeam, hit.ArgAs<DamageType>(3));
    }

    [Theory]
    [InlineData("0", DamageType.Dissolve)]
    [InlineData("1", DamageType.Dissolve | DamageType.Shock)]
    public void Dissolvetype_makes_it_dissolve(string dissolve, DamageType type)
    {
        var rig = Spawned(1, ("damage", "50"), ("dissolvetype", dissolve));
        rig.Trace = new TraceResult(true, 0.5f, new Vector(50f, 0f, 0f), Vector.Zero, Player);

        rig.Now = 10.2f;
        rig.Think();

        Assert.Equal(type, Assert.Single(rig.Damage).ArgAs<DamageType>(3));
    }

    [Fact]
    public void A_beam_with_decals_marks_a_brush_it_burns()
    {
        var rig = Spawned(1 | 0x40, ("damage", "50"), ("decalname", "Scorch"));
        var wall = new EntityHandle(0x0000_0000u);
        rig.Engine.ReturnsEachCall("EntityRender.GetModelName", () =>
            rig.Engine.Calls.Last().ArgAs<EntityHandle>(0) == wall ? "maps/ss_sandbox.bsp" : "");
        rig.Trace = new TraceResult(true, 0.5f, new Vector(50f, 0f, 0f), Vector.Zero, wall);

        rig.Now = 10.2f;
        rig.Think();

        Assert.Equal(4, Assert.Single(rig.Engine.AllOf("Effects.PaintDecal")).ArgAs<int>(0));
    }

    [Fact]
    public void A_player_in_the_beam_fires_OnTouchedByEntity_and_the_beam_stops_updating()
    {
        var rig = Spawned(1, ("TouchType", "1"));

        rig.Think();

        Assert.Equal(1, rig.Entity.OnTouchedByEntity.FireCount);
        Assert.Equal(-1f, rig.Entity.NextThinkDelay());
    }

    [Fact]
    public void An_npc_only_beam_lets_a_player_through()
    {
        var rig = Spawned(1, ("TouchType", "2"));

        rig.Think();

        Assert.Equal(0, rig.Entity.OnTouchedByEntity.FireCount);
        Assert.Equal(0f, rig.Entity.NextThinkDelay());
    }

    [Theory]
    [InlineData(0f, 0f, 36f, true)]
    [InlineData(0f, 40f, 36f, false)]
    [InlineData(200f, 0f, 36f, false)]
    public void The_touch_ray_meets_a_box_on_its_length(float x, float y, float z, bool hits)
    {
        bool hit = EnvBeam.SlabIntersect(new Vector(-50f, 0f, 36f), new Vector(100f, 0f, 0f),
                                         new Vector(x - 16f, y - 16f, z - 36f), new Vector(x + 16f, y + 16f, z + 36f), out float t);

        Assert.Equal(hits, hit);
        if (hits)
            Assert.Equal(0.34f, t, 3);
    }

    //-------------------------------------------------------------------------
    // Strikes
    //-------------------------------------------------------------------------

    [Fact]
    public void A_beam_with_a_life_strikes_first_a_second_in_when_unnamed()
    {
        var rig = Spawned(0, named: false, ("life", "0.5"));

        Assert.Equal(1f, rig.Entity.NextThinkDelay("Strike"), 3);
        Assert.False(rig.Engine.Called("Entities.Create"), "a striking beam draws no beam");
    }

    [Fact]
    public void A_strike_between_an_entity_and_a_point_is_a_beam_from_the_point()
    {
        var rig = Spawned(1, ("life", "0.5"), ("StrikeTime", "2"));

        rig.Strike();

        Assert.Equal(["entpoint 0 " + (EndProp.Value & 0x0FFF)], rig.Temps.Sent);
        Assert.Equal(0.5f, rig.Temps.Last.Life);
        Assert.Equal(2.5f, rig.Entity.NextThinkDelay("Strike"), 3);
    }

    [Fact]
    public void A_random_beam_waits_a_random_share_of_its_strike_time()
    {
        var rig = Spawned(1 | 0x04, ("life", "0.5"), ("StrikeTime", "2"));
        rig.Floats.Enqueue(1.25f);

        rig.Strike();

        Assert.Equal(1.75f, rig.Entity.NextThinkDelay("Strike"), 3);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Host.RandomFloat" && c.ArgAs<float>(0) == 0f && c.ArgAs<float>(1) == 2f);
    }

    [Fact]
    public void A_ring_between_a_point_and_an_entity_does_not_strike()
    {
        var rig = Spawned(1 | 0x08, ("life", "0.5"));

        rig.Strike();

        Assert.Empty(rig.Temps.Sent);
    }

    [Fact]
    public void A_strike_that_damages_deals_a_whole_seconds_worth()
    {
        var rig = Spawned(1, ("life", "0.5"), ("damage", "30"));
        rig.Trace = new TraceResult(true, 0.5f, new Vector(50f, 0f, 0f), Vector.Zero, Player);

        rig.Strike();

        Assert.Equal(30f, Assert.Single(rig.Damage).ArgAs<float>(2), 3);
    }

    [Fact]
    public void StrikeOnce_strikes()
    {
        var rig = Spawned(0, ("life", "0.5"));

        rig.Input("StrikeOnce");

        Assert.Single(rig.Temps.Sent);
    }

    [Fact]
    public void With_no_end_it_zaps_from_its_start_to_a_wall_in_its_radius()
    {
        var rig = Spawned(1, ("life", "0.5"), ("Radius", "256"), ("LightningEnd", ""));
        rig.Trace = new TraceResult(true, 0.5f, new Vector(128f, 0f, 0f), Vector.Zero, EntityHandle.None);
        rig.Floats.Enqueue(1f);

        rig.Strike();

        Assert.Equal([$"points {new Vector(0f, 0f, 0f)} {new Vector(128f, 0f, 0f)}"], rig.Temps.Sent);
    }

    [Fact]
    public void With_neither_end_it_zaps_between_two_walls_on_opposite_sides()
    {
        var rig = Spawned(1, ("life", "0.5"), ("Radius", "256"), ("LightningStart", ""), ("LightningEnd", ""));
        // Directions +x then -x (opposite, so the second is kept), each to a wall, in sight of each other.
        foreach (float f in new[] { 1f, 0f, 0f, -1f, 0f, 0f })
            rig.Floats.Enqueue(f);
        rig.Traces.Enqueue(new TraceResult(true, 0.5f, new Vector(128f, 0f, 0f), Vector.Zero, EntityHandle.None));
        rig.Traces.Enqueue(new TraceResult(true, 0.5f, new Vector(-128f, 0f, 0f), Vector.Zero, EntityHandle.None));
        rig.Traces.Enqueue(new TraceResult(false, 1f, new Vector(-128f, 0f, 0f), Vector.Zero, EntityHandle.None));

        rig.Strike();

        Assert.Equal([$"points {new Vector(128f, 0f, 0f)} {new Vector(-128f, 0f, 0f)}"], rig.Temps.Sent);
    }

    [Fact]
    public void A_zap_to_a_wall_closer_than_a_tenth_of_the_radius_is_tried_again()
    {
        var rig = Spawned(1, ("life", "0.5"), ("Radius", "256"), ("LightningEnd", ""));
        rig.Traces.Enqueue(new TraceResult(true, 0.05f, new Vector(10f, 0f, 0f), Vector.Zero, EntityHandle.None));
        rig.Traces.Enqueue(new TraceResult(true, 0.5f, new Vector(128f, 0f, 0f), Vector.Zero, EntityHandle.None));

        rig.Strike();

        Assert.Equal([$"points {new Vector(0f, 0f, 0f)} {new Vector(128f, 0f, 0f)}"], rig.Temps.Sent);
    }

    [Fact]
    public void A_decal_name_without_the_decals_flag_marks_nothing()
    {
        var rig = Spawned(1, ("damage", "50"), ("decalname", "Scorch"));
        var wall = new EntityHandle(0x0000_0000u);
        rig.Engine.ReturnsEachCall("EntityRender.GetModelName", () =>
            rig.Engine.Calls.Last().ArgAs<EntityHandle>(0) == wall ? "maps/ss_sandbox.bsp" : "");
        rig.Trace = new TraceResult(true, 0.5f, new Vector(50f, 0f, 0f), Vector.Zero, wall);

        rig.Now = 10.2f;
        rig.Think();

        Assert.False(rig.Engine.Called("Effects.PaintDecal"));
    }

    //-------------------------------------------------------------------------
    // CBeam's inputs
    //-------------------------------------------------------------------------

    [Fact]
    public void Width_sets_both_widths_and_the_colour_inputs_one_channel_each()
    {
        var rig = Spawned(1, ("rendercolor", "10 20 30"));

        rig.Input("Width", "6");
        rig.Input("ColorGreenValue", "300");
        rig.Input("Noise", "3");

        Assert.Equal((6f, 6f), (rig.Beam!.State.Width, rig.Beam.State.EndWidth));
        Assert.Equal((10, 255, 30), ((int)rig.Beam.State.Red, (int)rig.Beam.State.Green, (int)rig.Beam.State.Blue));
        Assert.Equal(3f, rig.Beam.State.Noise);
    }
}
