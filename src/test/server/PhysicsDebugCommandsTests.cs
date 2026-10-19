using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// glibc's %f, which the SS-PHYSWORLD probes' lines were printed with and the
/// tools that parse them expect. Expected strings are glibc's own output.
/// </summary>
public class PrintfFixedTests
{
    [Fact]
    public void An_ordinary_float_prints_six_decimals_of_its_double()
    {
        // Two readings from a native ss_physworld_object line on dm_lockdown.
        Assert.Equal("18.304001", PrintfFixed.F6(18.304001f));
        Assert.Equal("-3618.219971", PrintfFixed.F6(-3618.219971f));
    }

    [Fact]
    public void A_negative_zero_keeps_its_sign()
        => Assert.Equal("-0.000000", PrintfFixed.F6(-0f));

    [Fact]
    public void A_tie_at_the_seventh_decimal_rounds_to_even()
    {
        Assert.Equal("0.007812", PrintfFixed.F6(0.0078125f));   // 1/128
        Assert.Equal("0.023438", PrintfFixed.F6(0.0234375f));   // 3/128
    }

    [Fact]
    public void A_large_value_prints_every_integer_digit()
    {
        Assert.Equal("32768.000000", PrintfFixed.F6(32768f));
        Assert.Equal("10000000000.000000", PrintfFixed.F6(1e10f));
    }

    [Fact]
    public void Non_finite_values_take_glibcs_spellings()
    {
        Assert.Equal("nan", PrintfFixed.F6(BitConverter.UInt32BitsToSingle(0x7FC00000u)));
        Assert.Equal("-nan", PrintfFixed.F6(BitConverter.UInt32BitsToSingle(0xFFC00000u)));
        Assert.Equal("inf", PrintfFixed.F6(float.PositiveInfinity));
        Assert.Equal("-inf", PrintfFixed.F6(float.NegativeInfinity));
    }
}

/// <summary>
/// ss_physworld_recheck, _bounds and _object, managed: facts from native's
/// physics_debug.cpp at a594c5e14, the lines and sinks each wrote.
/// </summary>
public class PhysWorldProbesTests
{
    private const nint Env = 0x10;
    private const nint Terrain = 0x7700;

    private readonly FakeInspect _inspect = new();
    private readonly RecordingReportSink _sink = new();
    private readonly List<string> _console = new();
    private readonly List<nint> _hullsAsked = new();
    private readonly Dictionary<nint, (Vector, Vector)> _hulls = new();
    private nint _env = Env;

    private PhysWorldProbes Probes() => new(_inspect, () => _env, Hull, i => i == 3 ? Terrain : 0, _sink, _console.Add);

    private (Vector Mins, Vector Maxs) Hull(nint collide)
    {
        _hullsAsked.Add(collide);
        return _hulls.TryGetValue(collide, out var h) ? h : (new Vector(-1f, -1f, -1f), new Vector(1f, 1f, 1f));
    }

    private void Put(nint o, string? name, Vector at, Vector angles = default, nint collide = 0x7000,
                     bool isStatic = false, bool asleep = false, Vector velocity = default, float mass = 25f,
                     ushort gameFlags = 0)
    {
        _inspect.All.Add(o);
        _inspect.Readouts[o] = new PhysicsObjectReadout(name, isStatic, true, !isStatic, asleep, 0xFFFFFFFFu,
                                                        collide, mass, 1f, at, angles, velocity, gameFlags);
    }

    // ---- ss_physworld_recheck ----

    [Fact]
    public void Recheck_reasks_every_moveable_object_and_counts_them()
    {
        Put(0x2000, "crate", Vector.Zero);
        Put(0x2001, "pillar", Vector.Zero, isStatic: true);
        _inspect.All.Add(0);
        Put(0x2002, "barrel", Vector.Zero);

        Probes().Recheck();

        Assert.Equal(["Recheck 0x2000", "Recheck 0x2002"], _inspect.Calls);
        Assert.Equal(["[SS-PHYSWORLD-RECHECK] re-asked the filter for 2 moveable object(s)\n"], _sink.Lines);
    }

    [Fact]
    public void Recheck_with_no_environment_says_so_on_the_console_alone()
    {
        _env = 0;
        Probes().Recheck();
        Assert.Equal(["[SS-PHYSWORLD] no environment\n"], _console);
        Assert.Empty(_sink.Lines);
    }

    // ---- ss_physworld_bounds ----

    [Fact]
    public void Bounds_with_no_environment_is_unusable()
    {
        _env = 0;
        Probes().Bounds();
        Assert.Equal(["[SS-PHYSWORLD-BOUNDS] usable=0 why=no-environment\n"], _sink.Lines);
    }

    [Fact]
    public void Bounds_in_a_healthy_world_is_one_summary_line()
    {
        Put(0x2000, "crate", new Vector(100f, 200f, 300f));
        _inspect.All.Add(0);
        Put(0x2001, "pillar", new Vector(-32767f, 0f, 0f), isStatic: true);

        Probes().Bounds();

        Assert.Equal(["[SS-PHYSWORLD-BOUNDS] objects=3 outOfBounds=0 limit=32768.000000\n"], _sink.Lines);
    }

    [Fact]
    public void Bounds_names_an_object_past_the_map_edge_with_every_reading()
    {
        Put(0x2000, "crate", Vector.Zero);
        Put(0x2001, "lost", new Vector(1f, 40000f, 3f), new Vector(0f, 90f, 0f), asleep: true);

        Probes().Bounds();

        Assert.Equal(
            ["[SS-PHYSWORLD-OUTOFBOUNDS] index=1 name='lost' pos=(1.000000 40000.000000 3.000000) " +
             "ang=(0.000000 90.000000 0.000000) hull=(-1.000000 -1.000000 -1.000000)..(1.000000 1.000000 1.000000) " +
             "finite=1 inBounds=0 saneHull=1 static=0 asleep=1\n",
             "[SS-PHYSWORLD-BOUNDS] objects=2 outOfBounds=1 limit=32768.000000\n"],
            _sink.Lines);
    }

    [Fact]
    public void Bounds_names_a_non_finite_angle()
    {
        Put(0x2000, null, Vector.Zero, new Vector(float.PositiveInfinity, 0f, 0f));

        Probes().Bounds();

        Assert.StartsWith("[SS-PHYSWORLD-OUTOFBOUNDS] index=0 name='(null)' pos=(0.000000 0.000000 0.000000) " +
                          "ang=(inf 0.000000 0.000000) ", _sink.Lines[0]);
        Assert.Contains(" finite=0 inBounds=1 saneHull=1 ", _sink.Lines[0]);
    }

    [Fact]
    public void Bounds_names_an_absurd_hull_where_the_object_is()
    {
        Put(0x2000, "huge", Vector.Zero, collide: 0x7100);
        _hulls[0x7100] = (new Vector(-4e7f, 0f, 0f), new Vector(1f, 1f, 1f));

        Probes().Bounds();

        Assert.Contains(" hull=(-40000000.000000 0.000000 0.000000)..(1.000000 1.000000 1.000000) finite=1 inBounds=1 saneHull=0 ",
                        _sink.Lines[0]);
    }

    [Fact]
    public void Bounds_never_asks_the_hull_of_a_virtual_terrain_collide()
    {
        // vphysics segfaults in CollideGetAABB on a virtual mesh.
        Put(0x2000, "vdisp_0003", Vector.Zero, collide: Terrain, isStatic: true);
        Put(0x2001, "crate", Vector.Zero, collide: 0x7000);
        Put(0x2002, "shapeless", Vector.Zero, collide: 0);

        Probes().Bounds();

        Assert.Equal([(nint)0x7000], _hullsAsked);
        Assert.Equal(["[SS-PHYSWORLD-BOUNDS] objects=3 outOfBounds=0 limit=32768.000000\n"], _sink.Lines);
    }

    [Fact]
    public void Bounds_names_eight_and_counts_them_all()
    {
        for (int i = 0; i < 10; i++)
            Put(0x2000 + i, $"lost{i}", new Vector(0f, 0f, -50000f));

        Probes().Bounds();

        Assert.Equal(8, _sink.Lines.Count(l => l.StartsWith("[SS-PHYSWORLD-OUTOFBOUNDS] ", StringComparison.Ordinal)));
        Assert.Equal("[SS-PHYSWORLD-BOUNDS] objects=10 outOfBounds=10 limit=32768.000000\n", _sink.Lines[^1]);
    }

    // ---- ss_physworld_object ----

    [Fact]
    public void Object_with_no_argument_prints_its_usage()
    {
        Probes().Object([]);
        Assert.Equal(["[SS-PHYSWORLD-OBJECT] usage: ss_physworld_object <name substring>\n"], _sink.Lines);
    }

    [Fact]
    public void Object_with_no_environment_says_so()
    {
        _env = 0;
        Probes().Object(["crate"]);
        Assert.Equal(["[SS-PHYSWORLD-OBJECT] no environment\n"], _sink.Lines);
    }

    [Fact]
    public void Object_prints_each_match_then_the_count()
    {
        Put(0x2000, "prop_physics", new Vector(-3618.219971f, 4103.310059f, 18.304001f), velocity: new Vector(0f, 0f, -0f),
            asleep: true, mass: 150f, gameFlags: 0x0a1);
        Put(0x2001, "player_stand", Vector.Zero);

        Probes().Object(["PROP"]);

        Assert.Equal(
            ["[SS-PHYSWORLD-OBJECT] name='prop_physics' pos=(-3618.219971 4103.310059 18.304001) " +
             "vel=(0.000000 0.000000 -0.000000) asleep=1 moveable=1 mass=150.000000 gameFlags=00a1\n",
             "[SS-PHYSWORLD-OBJECT] matched=1 of 2 for 'PROP'\n"],
            _sink.Lines);
    }

    [Fact]
    public void Object_star_lists_every_object_the_nameless_included()
    {
        Put(0x2000, "crate", Vector.Zero);
        Put(0x2001, null, Vector.Zero);
        _inspect.All.Add(0);

        Probes().Object(["*"]);

        Assert.StartsWith("[SS-PHYSWORLD-OBJECT] name='' pos=", _sink.Lines[1]);
        Assert.Equal("[SS-PHYSWORLD-OBJECT] matched=2 of 3 for '*'\n", _sink.Lines[^1]);
    }

    [Fact]
    public void Object_with_an_empty_request_matches_nothing()
    {
        Put(0x2000, "crate", Vector.Zero);
        Probes().Object([""]);
        Assert.Equal(["[SS-PHYSWORLD-OBJECT] matched=0 of 1 for ''\n"], _sink.Lines);
    }
}

/// <summary>physics_report_active and physics_select, managed (stock physics.cpp).</summary>
public class PhysicsActiveCommandsTests
{
#if DEBUG
    /// <summary>
    /// physics_debug.cpp's six, each declared once by a managed command class
    /// and by no native-bodied row: the names every tool drives are kept.
    /// </summary>
    [Fact]
    public void The_six_physics_debug_commands_are_managed_and_none_is_native_bodied()
    {
        string[] names = ["ss_physworld_recheck", "ss_physworld_bounds", "ss_physworld_object",
                          "physics_highlight_active", "physics_report_active", "physics_select"];
        var declared = typeof(PhysWorldProbes).Assembly.GetTypes()
            .Select(t => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ConCommandAttribute>(t)?.Name)
            .Where(n => n is not null)
            .ToList();
        var native = NativeCommands.Server.Concat(NativeCommands.Debug).Select(c => c.Name).ToList();

        Assert.True(declared.Count > 50, $"read only {declared.Count} managed command declarations");
        foreach (string name in names)
        {
            Assert.Single(declared, n => n == name);
            Assert.DoesNotContain(name, native);
        }
    }
#endif

    private static readonly EntityHandle A = new(0x0001_0010u);
    private static readonly EntityHandle B = new(0x0001_0011u);

    [Fact]
    public void Report_names_each_active_object_with_an_owner()
    {
        var lines = new List<string>();
        int n = PhysicsActiveReport.Run([0x2000, 0x2001, 0x2002],
            o => o switch
            {
                0x2000 => ("func_physbox", "fan"),
                0x2002 => ("prop_physics", "prop_physics"),
                _ => null,
            },
            lines.Add);

        Assert.Equal(2, n);
        Assert.Equal(["func_physbox - fan\n", "prop_physics - prop_physics\n"], lines);
    }

    [Fact]
    public void Report_names_an_unnamed_owner_by_its_model_as_stock_CallbackReport()
    {
        Assert.Equal("models/props_junk/wood_crate001a.mdl",
                     PhysicsActiveReport.DebugName("", "models/props_junk/wood_crate001a.mdl"));
        Assert.Equal("models/props_junk/wood_crate001a.mdl",
                     PhysicsActiveReport.DebugName(null, "models/props_junk/wood_crate001a.mdl"));
    }

    [Fact]
    public void Report_names_a_named_owner_by_its_name()
        => Assert.Equal("crate", PhysicsActiveReport.DebugName("crate", "models/props_junk/wood_crate001a.mdl"));

    [Fact]
    public void Select_with_no_argument_is_what_the_player_looks_at()
    {
        Assert.Equal([A], PhysicsSelect.Subjects([], () => A, _ => [], _ => []));
        Assert.Empty(PhysicsSelect.Subjects([], () => EntityHandle.None, _ => [], _ => []));
    }

    [Fact]
    public void Select_reads_a_name_before_a_classname()
    {
        Assert.Equal([A], PhysicsSelect.Subjects(["crate"], () => B, _ => [A], _ => [B]));
        Assert.Equal([B], PhysicsSelect.Subjects(["prop_physics"], () => A, _ => [], _ => [B]));
    }

    [Fact]
    public void Mark_toggles_the_test_bit_and_keeps_the_rest()
    {
        var inspect = new FakeInspect();
        inspect.Callbacks[0x2000] = 0x0001;
        inspect.Callbacks[0x2001] = 0x8010;

        int n = PhysicsSelect.Mark([A, B, new EntityHandle(0x0001_0012u)],
                                   h => h == A ? 0x2000 : h == B ? 0x2001 : 0, inspect);

        Assert.Equal(2, n);
        Assert.Equal((ushort)0x8001, inspect.Callbacks[0x2000]);
        Assert.Equal((ushort)0x0010, inspect.Callbacks[0x2001]);
    }
}
