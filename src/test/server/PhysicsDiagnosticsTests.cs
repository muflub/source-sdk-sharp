using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// vphysics as the diagnostics read it: objects by pointer, each with a
/// readout a test sets, and every simulate and air-density write recorded.
/// </summary>
public sealed class FakeInspect : IVPhysicsInspect
{
    public readonly List<nint> All = new();
    public readonly List<nint> Active = new();
    public readonly Dictionary<nint, PhysicsObjectReadout> Readouts = new();
    public readonly List<string> Calls = new();
    public float Density = 2f;

    public nint[] Objects(nint environment) => environment == 0 ? [] : All.ToArray();

    public nint[] ActiveObjects(nint environment) => environment == 0 ? [] : Active.ToArray();

    public PhysicsObjectReadout Read(nint o)
        => Readouts.TryGetValue(o, out PhysicsObjectReadout r) ? r : default;

    public Vector Position(nint o) => Read(o).Position;

    public int MaterialIndex(nint o) => 0;

    public void Simulate(nint environment, float deltaTime) => Calls.Add($"Simulate {deltaTime}");

    public float AirDensity(nint environment) => Density;

    public void SetAirDensity(nint environment, float density) => Calls.Add($"SetAirDensity {density}");

    public readonly Dictionary<nint, ushort> Callbacks = new();

    public void RecheckCollisionFilter(nint o) => Calls.Add($"Recheck 0x{(long)o:x}");

    public ushort CallbackFlags(nint o) => Callbacks.TryGetValue(o, out ushort f) ? f : (ushort)0;

    public void SetCallbackFlags(nint o, ushort flags)
    {
        Calls.Add($"SetCallbackFlags 0x{(long)o:x} {flags:x4}");
        Callbacks[o] = flags;
    }

    /// <summary>A moveable, colliding object at <paramref name="z"/>, listed.</summary>
    public void Add(nint o, string name, float z = 0f, nint collide = 0x7000, bool collisions = true,
                    bool isStatic = false)
    {
        All.Add(o);
        Readouts[o] = new PhysicsObjectReadout(name, isStatic, collisions, !isStatic, false, 0xFFFFFFFFu,
                                               collide, 25f, 12.5f, new Vector(1f, 2f, z), Vector.Zero,
                                               Vector.Zero, 0);
    }
}

public sealed class RecordingReportSink : IPhysWorldReportSink
{
    public readonly List<string> Lines = new();

    public void Emit(string text) => Lines.Add(text);
}

/// <summary>
/// plan_physics_bones_native_zero 2.3: the SS-PHYSWORLD report, managed. Facts
/// hand-derived from native's SourceSharp_PhysWorldReport (physics.cpp at
/// bc21e4d46): its three early lines, the per-object lines (four of each at
/// most) and the summary line it emits last.
/// </summary>
public class PhysWorldReportTests
{
    private const nint Env = 0x10;
    private static readonly EntityHandle A = new(0x0001_0010u);
    private static readonly EntityHandle B = new(0x0001_0011u);

    private readonly OpenSolverWorld _world = new();
    private readonly FakeSolverObjects _objects = new();
    private readonly FakeInspect _inspect = new();
    private readonly RecordingReportSink _sink = new();
    private readonly PhysicsSolver _solver;
    private nint _env = Env;
    private nint _worldObject = OpenSolverWorld.World;

    public PhysWorldReportTests()
    {
        _solver = new PhysicsSolver(_world, _objects, new PhysicsSolverRules(),
                                    new EntityPairCollisions(_world, _objects), new PhysicsPenetrationLedger(),
                                    new SolverClock(), _ => { }, new RecordingCrushes(), () => 0, _ => { });
        _objects.At(OpenSolverWorld.World).Static = true;
        _objects.At(OpenSolverWorld.World).Moveable = false;
        _inspect.Add(OpenSolverWorld.World, "world", isStatic: true, collide: 0x9000);
    }

    private PhysWorldReport Report(int live = 0, int outlived = 0)
        => new(_inspect, () => _env, () => _worldObject, _solver, _sink, () => (live, outlived), () => false);

    private static int RowsSet()
    {
        uint[] rows = CollisionRules.BuildMatrix();
        int set = 0;
        for (int g = 0; g < 20 && g < rows.Length; g++)
            set += rows[g] != 0 ? 1 : 0;
        return set;
    }

    [Fact]
    public void With_no_environment_it_says_so_and_stops()
    {
        _env = 0;
        Report().Run();
        Assert.Equal(["[SS-PHYSWORLD] physenv=(nil) world=0xf000 NO ENVIRONMENT\n"], _sink.Lines);
    }

    [Fact]
    public void With_no_world_object_every_prop_falls()
    {
        _worldObject = 0;
        _inspect.Add(0x2000, "crate");
        Report().Run();
        Assert.Equal(["[SS-PHYSWORLD] physenv=0x10 world=(nil) objects=2 NO WORLD OBJECT -- every prop falls\n"],
                     _sink.Lines);
    }

    [Fact]
    public void A_healthy_world_is_one_summary_line()
    {
        _objects.Owned(0x2000, A);
        _inspect.Add(0x2000, "crate");
        _inspect.Add(0x2001, "pillar", isStatic: true);
        Report(live: 1, outlived: 0).Run();

        string line = Assert.Single(_sink.Lines);
        Assert.Equal(
            $"[SS-PHYSWORLD] physenv=0x10 world=0xf000 objects=3 worldStatic=1 worldEnabled=1 worldContents=ffffffff " +
            $"worldIsOwned=1 worldIsWorld=1 ruleRowsSet={RowsSet()} unownedCollides=1 collidableSolids=fffffffe " +
            "moveable=1 shadowed=0 refusedByTheWorld=0 collisionsOff=0 objectsBelowTheMap=0 liveShadows=1 " +
            "outlivedTheirEnvironment=0\n",
            line);
    }

    [Fact]
    public void Collisions_off_is_counted_and_the_first_four_are_named()
    {
        for (int i = 0; i < 5; i++)
            _inspect.Add(0x2000 + i, $"hull{i}", collisions: false);
        Report().Run();

        var named = _sink.Lines.Where(l => l.StartsWith("[SS-PHYSWORLD-NOCOLLIDE]", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, named.Count);
        Assert.Equal("[SS-PHYSWORLD-NOCOLLIDE] obj=0x2000 name=hull0 static=0 moveable=1 contents=ffffffff\n", named[0]);
        Assert.Contains(" collisionsOff=5 ", _sink.Lines[^1]);
    }

    [Fact]
    public void An_object_below_the_map_is_named_with_who_shares_its_collide()
    {
        _objects.Owned(0x2000, A);
        _world.Facts[A] = new SolverEntity(5, 6, 0, 6, false, EntityHandle.None, EntityHandle.None);
        _inspect.Add(0x2000, "crate", z: -6000f, collide: 0x7000);
        _inspect.Add(0x2001, "crate2", collide: 0x7000);
        _inspect.Add(0x2002, "crate3", collide: 0x7000);
        Report().Run();

        string fell = Assert.Single(_sink.Lines, l => l.StartsWith("[SS-PHYSWORLD-FELL]", StringComparison.Ordinal));
        Assert.Equal(
            "[SS-PHYSWORLD-FELL] obj=0x2000 name=crate at=(1.0 2.0 -6000.0) collisions=1 asleep=0 valid=1 solid=6 " +
            "solidFlags=00000000 moveType=6 group=5 contents=ffffffff solidMask=0200400b collide=0x7000 " +
            "sharesCollide=2 mass=25.0 sphere=12.50\n",
            fell);
        Assert.Contains(" objectsBelowTheMap=1 ", _sink.Lines[^1]);
    }

    [Fact]
    public void A_refused_object_prints_every_input_the_rule_read()
    {
        _objects.Owned(0x2000, A);
        _world.Facts[A] = new SolverEntity(0, 6, PhysicsSolverRules.NotSolidFlag, 6, false, EntityHandle.None,
                                           EntityHandle.None);
        _inspect.Add(0x2000, "ghost");
        Report().Run();

        string why = Assert.Single(_sink.Lines, l => l.StartsWith("[SS-PHYSWORLD-REFUSED]", StringComparison.Ordinal));
        Assert.Equal(
            "[SS-PHYSWORLD-REFUSED] obj=0x2000 name=ghost valid=1 solid=6 solidFlags=00000004 moveType=6 group=0 " +
            "contents=ffffffff solidMask=0200400b gameFlags=00000000 moveable=1 trigger=0 shadow=0 sameEntity=0 " +
            "pairInHash=0 | world solid=6 solidFlags=00000000 moveType=0 group=0 contents=ffffffff solidMask=0200400b\n",
            why);
        Assert.Contains(" refusedByTheWorld=1 ", _sink.Lines[^1]);
    }

    [Fact]
    public void Only_four_refusals_are_named_but_all_are_counted()
    {
        for (int i = 0; i < 6; i++)
        {
            var e = new EntityHandle(0x0001_0020u + (uint)i);
            _objects.Owned(0x2000 + i, e);
            _world.Facts[e] = new SolverEntity(0, 0, 0, 6, false, EntityHandle.None, EntityHandle.None);
            _inspect.Add(0x2000 + i, $"none{i}");
        }

        Report().Run();
        Assert.Equal(4, _sink.Lines.Count(l => l.StartsWith("[SS-PHYSWORLD-REFUSED]", StringComparison.Ordinal)));
        Assert.Contains(" refusedByTheWorld=6 ", _sink.Lines[^1]);
    }

    [Fact]
    public void A_shadow_controlled_owned_object_is_counted_as_shadowed()
    {
        _objects.Owned(0x2000, A).Shadow = true;
        _inspect.Add(0x2000, "door");
        _objects.At(0x2001).Shadow = true;   // unowned: no side, not counted
        _inspect.Add(0x2001, "loose");
        Report().Run();
        Assert.Contains(" shadowed=1 ", _sink.Lines[^1]);
    }

    [Fact]
    public void The_automatic_report_runs_only_when_the_convar_is_on()
    {
        bool on = false;
        var report = new PhysWorldReport(_inspect, () => _env, () => _worldObject, _solver, _sink, () => (0, 0),
                                         () => on);
        report.OnLevelInitPostEntity("dm_x");
        Assert.Empty(_sink.Lines);

        on = true;
        report.OnLevelInitPostEntity("dm_x");
        Assert.Single(_sink.Lines);
    }

    [Theory]
    [InlineData(0L, "(nil)")]
    [InlineData(0x7f001234L, "0x7f001234")]
    public void Pointers_print_as_glibc_prints_them(long pointer, string text)
        => Assert.Equal(text, PhysWorldReport.P((nint)pointer));
}

/// <summary>
/// plan_physics_bones_native_zero 2.3: physics_budget and its
/// PhysForceEntityToSleep, managed. Facts from native's physics_debug.cpp and
/// physics.cpp at bc21e4d46 (stock's physics.cpp).
/// </summary>
public class PhysicsBudgetTests
{
    private const nint Env = 0x10;
    private readonly FakeInspect _inspect = new();
    private readonly List<string> _msg = new();
    private readonly List<string> _dev = new();
    private readonly List<nint> _slept = new();
    private readonly Dictionary<nint, uint?> _owners = new();
    private double _now;

    private PhysicsBudget Budget()
        => new(_inspect, o => { _slept.Add(o); _inspect.Calls.Add($"Sleep {o:x}"); },
               o => _owners.TryGetValue(o, out uint? key) ? key : null,
               o => ($"prop_{o:x}", $"name_{o:x}"),
               () => _now += 0.001, _msg.Add, _dev.Add);

    private void Active(nint o, uint? owner)
    {
        _inspect.Add(o, $"o{o:x}");
        _inspect.Active.Add(o);
        _owners[o] = owner;
    }

    [Fact]
    public void Nothing_active_is_nothing_simulated()
    {
        Budget().Run(Env, 0.015f);
        Assert.Empty(_inspect.Calls);
        Assert.Empty(_msg);
    }

    [Fact]
    public void One_object_per_owner_and_none_for_an_unowned_one()
    {
        Active(0x2000, 7);
        Active(0x2001, 7);
        Active(0x2002, null);
        Active(0x2003, 9);
        Budget().Run(Env, 0.015f);

        Assert.Equal(2, _msg.Count);
        Assert.StartsWith("prop_2000 (name_2000): ", _msg[0], StringComparison.Ordinal);
        Assert.StartsWith("prop_2003 (name_2003): ", _msg[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Each_timed_step_follows_sleeping_everyone_before_it()
    {
        Active(0x2000, 1);
        Active(0x2001, 2);
        Active(0x2002, 3);
        Budget().Run(Env, 0.015f);

        Assert.Equal(
            ["Simulate 0.015", "Simulate 0.015", "Sleep 2000", "Simulate 0.015", "Sleep 2000", "Sleep 2001",
             "Simulate 0.015"],
            _inspect.Calls);
    }

    [Fact]
    public void The_line_is_stocks_with_the_share_of_the_total()
    {
        Active(0x2000, 1);
        Budget().Run(Env, 0.015f);

        // One owner, whose step cost what the full one did: its time clamps to
        // 0.00001 s and the total to at least 0.001 s, so it is 1% of it.
        Assert.Equal("prop_2000 (name_2000): 0.010ms (1.000%) @ (1.000000, 2.000000, 0.000000)\n",
                     Assert.Single(_msg));
    }

    [Fact]
    public void Putting_an_entity_to_sleep_says_so_under_developer_2()
    {
        Active(0x2000, 1);
        Active(0x2001, 2);
        Budget().Run(Env, 0.015f);
        Assert.Equal(["Putting entity to sleep: prop_2000\n"], _dev);
        Assert.Equal([(nint)0x2000], _slept);
    }
}

/// <summary>plan_physics_bones_native_zero 2.3: air_density, managed (stock's CC_AirDensity).</summary>
public class AirDensityTests
{
    private readonly FakeInspect _inspect = new();
    private readonly List<string> _msg = new();

    [Fact]
    public void Between_levels_it_does_nothing()
    {
        AirDensity.Run("", 0, _inspect, _msg.Add);
        AirDensity.Run("3", 0, _inspect, _msg.Add);
        Assert.Empty(_msg);
        Assert.Empty(_inspect.Calls);
    }

    [Fact]
    public void With_no_argument_it_prints_the_current_density()
    {
        AirDensity.Run("", 0x10, _inspect, _msg.Add);
        Assert.Equal(["air_density <value>\nCurrent air density is 2.00\n"], _msg);
    }

    [Theory]
    [InlineData("1.5", "SetAirDensity 1.5")]
    [InlineData("abc", "SetAirDensity 0")]
    [InlineData("4x 9", "SetAirDensity 4")]
    public void An_argument_is_read_as_atof_reads_it(string args, string call)
    {
        AirDensity.Run(args, 0x10, _inspect, _msg.Add);
        Assert.Equal([call], _inspect.Calls);
        Assert.Empty(_msg);
    }
}

/// <summary>plan_physics_bones_native_zero 2.3: PhysForceEntityToSleep is managed's.</summary>
public class PhysicsForceToSleepTests
{
    [Fact]
    public void A_moveable_object_is_cleared_then_slept_and_a_fixed_one_is_left_alone()
    {
        var world = new OpenSolverWorld();
        var objects = new FakeSolverObjects();
        var solver = new PhysicsSolver(world, objects, new PhysicsSolverRules(),
                                       new EntityPairCollisions(world, objects), new PhysicsPenetrationLedger(),
                                       new SolverClock(), _ => { }, new RecordingCrushes(), () => 0, _ => { });
        objects.At(0x30).Moveable = false;

        solver.ForceEntityToSleep(0x20);
        solver.ForceEntityToSleep(0x30);
        solver.ForceEntityToSleep(0);

        Assert.Equal(["Clear 20", "Sleep 20"], objects.Calls);
    }
}

/// <summary>
/// The [SS-PHYSWORLD-NAN] line, managed (plan_physics_bones_native_zero 2.5):
/// native's format, read by tools/defect-shadow-probe's NAN_REFUSALS.
/// </summary>
public class PhysWorldNanTests
{
    [Fact]
    public void A_guard_that_refused_nothing_says_so_in_natives_words()
        => Assert.Equal("[SS-PHYSWORLD-NAN] refusals=0 firstSite=-1 firstValue=(0.000000 0.000000 0.000000)\n",
                        PhysWorldNan.Line(new PhysicsNanGuard()));

    [Fact]
    public void The_first_refusal_names_its_site_and_value_and_later_ones_only_count()
    {
        var guard = new PhysicsNanGuard();
        guard.Accept(new Vector(float.NaN, 1f, 2f), PhysicsNanGuard.SiteShadowState);
        guard.Accept(new Vector(5e8f, 0f, 0f), PhysicsNanGuard.SiteShadowSnap);

        string line = PhysWorldNan.Line(guard);
        Assert.StartsWith("[SS-PHYSWORLD-NAN] refusals=2 firstSite=1 firstValue=(NaN 1.000000 2.000000)", line);
        Assert.Matches(new System.Text.RegularExpressions.Regex(@"\[SS-PHYSWORLD-NAN\] refusals=(\d+) firstSite=(-?\d+)"), line);
    }
}
