using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// env_laser against stock's CEnvLaser (EnvLaser.cpp) over CBeam's server
/// half. Found beside stock (2026-10-27): with no target it shot 4096 units
/// ahead instead of holding its last end; it traced shots past itself
/// instead of what is solid; it sparked every tick with a sound whatever its
/// flags; it had no end sprite; no texture warned and stayed.
/// </summary>
public class EnvLaserTests
{
    private static readonly EntityHandle Owner = new(0x0004_0702u);
    private static readonly EntityHandle Drawn = new(0x0004_0701u);
    private static readonly EntityHandle Glow = new(0x0004_0703u);
    private static readonly EntityHandle Target = new(0x0004_0710u);
    private static readonly EntityHandle Parent = new(0x0004_0720u);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private static readonly Vector At = new(0f, 0f, 64f);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public float Now = 10f;
        public TraceResult? Trace;
        public readonly Dictionary<string, EntityHandle> Names = new() { ["dst"] = Target };

        public EnvLaser Entity => (EnvLaser)Registry.Get(Owner)!;
        public SsBeam Beam => (SsBeam)Registry.Get(Drawn)!;
        public EnvSprite Sprite => (EnvSprite)Registry.Get(Glow)!;
        public readonly ThinkScheduler Thinks = new();
        private readonly List<ScheduledThink> _due = new();

        public void Think()
        {
            _due.Clear();
            Thinks.TakeDue(Now, _due);
            foreach (var due in _due)
                Entity.OnThink();
        }

        public void Input(string name) => Registry.Input(Owner, name, "", 0f, EntityHandle.None);

        public EngineCall[] Damage => Engine.AllOf("Damage.ApplyAt").ToArray();
        public EngineCall[] Sparks => Engine.AllOf("Effects.SparkEffect").ToArray();
        public EngineCall LastTrace => Engine.AllOf("WorldQuery.TraceLine").Last();
    }

    private static Rig Spawned(int flags, params (string Key, string Value)[] keys)
        => Spawned(flags, named: true, keys);

    private static Rig Spawned(int flags, bool named, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvLaser).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IServerDecals>(new RecordingDecals().Answers("BigShot", 4));
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        var made = new Queue<EntityHandle>();
        var origins = new Dictionary<EntityHandle, Vector> { [Owner] = At, [Target] = new(200f, 0f, 64f) };
        engine.Returns("Precache.PrecacheModel", 7)
              .ReturnsEachCall("Entities.GetSpawnFlags", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Owner ? flags : 0)
              .ReturnsEachCall("Clock.Now", () => rig.Now)
              .Returns("Host.RandomInt", 0)
              .ReturnsEachCall("Entities.IsValid", () => engine.Calls.Last().ArgAs<EntityHandle>(0) != EntityHandle.None)
              .ReturnsEachCall("Entities.Create", () => made.Count > 0 ? made.Dequeue() : EntityHandle.None)
              .ReturnsEachCall("Entities.FindAllByName", () =>
                  rig.Names.TryGetValue((string)engine.Calls.Last().Args[0]!, out var h) ? new List<EntityHandle> { h } : new List<EntityHandle>())
              .ReturnsEachCall("Entities.GetOrigin", () => origins.GetValueOrDefault(engine.Calls.Last().ArgAs<EntityHandle>(0)))
              .ReturnsEachCall("Entities.GetParent", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Owner ? Parent : EntityHandle.None)
              .ReturnsEachCall("Entities.GetClassName", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player ? "player" : "worldspawn")
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player)
              .ReturnsEachCall("Entities.GetIndex", () => (int)(engine.Calls.Last().ArgAs<EntityHandle>(0).Value & 0x0FFF))
              .Returns("Entities.GetAngles", Vector.Zero)
              .ReturnsEachCall("WorldQuery.TraceLine", () =>
                  rig.Trace ?? new TraceResult(false, 1f, engine.Calls.Last().ArgAs<Vector>(1), Vector.Zero, EntityHandle.None));

        registry.Spawn(Drawn, EntityNames.Beam);
        registry.KeyValue(Glow, EntityNames.EnvSprite, "model", "sprites/glow01.vmt");
        registry.Spawn(Glow, EntityNames.EnvSprite);
        made.Enqueue(Drawn);
        made.Enqueue(Glow);

        if (named)
            registry.KeyValue(Owner, EntityNames.EnvLaser, "targetname", "laser");
        registry.KeyValue(Owner, EntityNames.EnvLaser, "texture", "sprites/laserbeam.spr");
        registry.KeyValue(Owner, EntityNames.EnvLaser, "LaserTarget", "dst");
        foreach (var (key, value) in keys)
            registry.KeyValue(Owner, EntityNames.EnvLaser, key, value);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Owner, EntityNames.EnvLaser);
        return rig;
    }

    //-------------------------------------------------------------------------
    // Spawn
    //-------------------------------------------------------------------------

    [Fact]
    public void A_laser_with_no_texture_goes()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvLaser).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        registry.Spawn(Owner, EntityNames.EnvLaser);
        registry.Directory.FreeRemoved();

        Assert.Null(registry.Get(Owner));
    }

    [Fact]
    public void The_texture_is_precached_as_given()
    {
        var rig = Spawned(1);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Precache.PrecacheModel" && (string)c.Args[0]! == "sprites/laserbeam.spr");
    }

    [Fact]
    public void Width_is_held_to_stocks_limit_at_both_ends_and_scroll_to_its_range()
    {
        var rig = Spawned(1, ("width", "200"), ("TextureScroll", "150"));

        Assert.Equal(102.3f, rig.Beam.State.Width, 3);
        Assert.Equal(102.3f, rig.Beam.State.EndWidth, 3);
        Assert.Equal(100f, rig.Beam.State.ScrollRate);
    }

    [Fact]
    public void Noise_is_read_as_a_whole_number()
    {
        var rig = Spawned(1, ("NoiseAmplitude", "2.9"));

        Assert.Equal(2f, rig.Beam.State.Noise);
    }

    [Fact]
    public void Brightness_defaults_to_full()
    {
        var rig = Spawned(1);

        Assert.Equal(255, rig.Beam.State.Brightness);
    }

    [Fact]
    public void A_named_laser_not_started_on_waits_hidden()
    {
        var rig = Spawned(0);

        Assert.False(rig.Beam.Visible);
        Assert.Equal(-1f, rig.Entity.NextThinkDelay());
    }

    [Fact]
    public void An_unnamed_laser_fires_from_the_start_and_every_tick()
    {
        var rig = Spawned(0, named: false);

        Assert.True(rig.Beam.Visible);
        Assert.Equal(0f, rig.Entity.NextThinkDelay());
        Assert.Equal(new Vector(200f, 0f, 64f), rig.Beam.State.End);
    }

    //-------------------------------------------------------------------------
    // Striking
    //-------------------------------------------------------------------------

    [Fact]
    public void It_traces_what_is_solid_from_itself_to_the_target_ignoring_nothing()
    {
        var rig = Spawned(1);

        var trace = rig.LastTrace;
        Assert.Equal(At, trace.ArgAs<Vector>(0));
        Assert.Equal(new Vector(200f, 0f, 64f), trace.ArgAs<Vector>(1));
        Assert.Equal(TraceMask.Solid, trace.ArgAs<TraceMask>(2));
        Assert.Equal(EntityHandle.None, trace.ArgAs<EntityHandle>(3));
    }

    [Fact]
    public void The_beam_ends_where_the_trace_stopped()
    {
        var rig = Spawned(1);
        rig.Trace = new TraceResult(true, 0.5f, new Vector(100f, 0f, 64f), Vector.Zero, Player);

        rig.Think();

        Assert.Equal(At, rig.Beam.State.Start);
        Assert.Equal(new Vector(100f, 0f, 64f), rig.Beam.State.End);
    }

    [Fact]
    public void With_its_target_gone_it_fires_where_it_last_ended()
    {
        var rig = Spawned(1);
        rig.Trace = new TraceResult(true, 0.5f, new Vector(120f, 0f, 64f), Vector.Zero, Player);
        rig.Think();
        rig.Names.Clear();
        rig.Trace = null;

        rig.Think();

        Assert.Equal(new Vector(120f, 0f, 64f), rig.LastTrace.ArgAs<Vector>(1));
    }

    [Fact]
    public void With_no_target_from_the_start_it_ends_where_it_stands()
    {
        var rig = Spawned(1, ("LaserTarget", "nobody"));

        Assert.Equal(At, rig.LastTrace.ArgAs<Vector>(1));
        Assert.Equal(At, rig.Beam.State.End);
    }

    [Fact]
    public void Damage_waits_a_tenth_of_a_second_from_turning_on_then_scales_with_the_time()
    {
        var rig = Spawned(1, ("damage", "50"));
        rig.Trace = new TraceResult(true, 0.5f, new Vector(100f, 0f, 64f), Vector.Zero, Player);

        rig.Now = 10.05f;
        rig.Think();
        Assert.Empty(rig.Damage);

        rig.Now = 10.2f;
        rig.Think();
        var hit = Assert.Single(rig.Damage);
        Assert.Equal(Player, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(50f * 0.2f, hit.ArgAs<float>(2), 3);
    }

    [Fact]
    public void Sparks_only_at_the_flagged_end_without_sound_when_it_burns()
    {
        var rig = Spawned(1 | 0x20);
        rig.Trace = new TraceResult(true, 0.5f, new Vector(100f, 0f, 64f), Vector.Zero, Player);
        Assert.Empty(rig.Sparks);

        rig.Now = 10.2f;
        rig.Think();

        var spark = Assert.Single(rig.Sparks);
        Assert.Equal(new Vector(100f, 0f, 64f), spark.ArgAs<Vector>(0));
        Assert.False(spark.ArgAs<bool>(3));
    }

    [Fact]
    public void Unflagged_it_never_sparks()
    {
        var rig = Spawned(1);

        rig.Now = 10.2f;
        rig.Think();

        Assert.Empty(rig.Sparks);
    }

    //-------------------------------------------------------------------------
    // Inputs and the end sprite
    //-------------------------------------------------------------------------

    [Fact]
    public void Toggle_turns_it_on_and_off()
    {
        var rig = Spawned(0);

        rig.Input("Toggle");
        Assert.True(rig.Beam.Visible);
        Assert.Equal(0f, rig.Entity.NextThinkDelay());

        rig.Input("Toggle");
        Assert.False(rig.Beam.Visible);
        Assert.Equal(-1f, rig.Entity.NextThinkDelay());
    }

    [Fact]
    public void The_end_sprite_is_a_glow_in_the_lasers_colour_under_its_parent()
    {
        var rig = Spawned(1, ("EndSprite", "sprites/glow01.vmt"), ("rendercolor", "255 32 16"), ("renderamt", "200"));

        string Key(string key) => (string)rig.Engine.Calls.Last(c => c.Member == "Entities.SetKeyValue"
            && c.ArgAs<EntityHandle>(0) == Glow && (string)c.Args[1]! == key).Args[2]!;
        Assert.Equal("sprites/glow01.vmt", Key("model"));
        Assert.Equal("3", Key("rendermode"));
        Assert.Equal("255 32 16", Key("rendercolor"));
        Assert.Equal("200", Key("renderamt"));
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.SetParent"
            && c.ArgAs<EntityHandle>(0) == Glow && c.ArgAs<EntityHandle>(1) == Parent);
    }

    [Fact]
    public void The_end_sprite_sits_where_the_beam_ends_and_goes_out_with_it()
    {
        var rig = Spawned(0, ("EndSprite", "sprites/glow01.vmt"));
        rig.Trace = new TraceResult(true, 0.5f, new Vector(100f, 0f, 64f), Vector.Zero, Player);

        rig.Input("TurnOn");
        Assert.True(rig.Sprite.Sprite.Visible);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.SetOrigin"
            && c.ArgAs<EntityHandle>(0) == Glow && c.ArgAs<Vector>(1).Equals(new Vector(100f, 0f, 64f)));

        rig.Input("TurnOff");
        Assert.False(rig.Sprite.Sprite.Visible);
    }

    [Fact]
    public void Removing_the_laser_removes_its_end_sprite()
    {
        var rig = Spawned(1, ("EndSprite", "sprites/glow01.vmt"));

        rig.Entity.OnRemoved();

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.Remove" && c.ArgAs<EntityHandle>(0) == Glow);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.Remove" && c.ArgAs<EntityHandle>(0) == Drawn);
    }
}
