using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>vphysics as the physics frame calls it: every call logged, answers a test sets.</summary>
public sealed class FakeFrameCalls : IVPhysicsFrameCalls
{
    public readonly List<string> Calls = new();
    public nint[] Active = [];
    public Vector GravityValue = new(0, 0, -600);
    public float TimestepValue = 0.015f;
    public readonly HashSet<nint> Asleep = new();
    public readonly HashSet<nint> Fixed = new();
    public readonly HashSet<nint> Static = new();
    public readonly Dictionary<nint, float> Masses = new();
    public readonly Dictionary<nint, ushort> Flags = new();
    public readonly Dictionary<nint, ushort> Callbacks = new();
    public readonly Dictionary<nint, nint> Data = new();
    public readonly Dictionary<nint, (bool, bool, Vector)> Shadows = new();
    public readonly Dictionary<nint, SnapshotContact[]> Snapshots = new();
    public Vector PlayerImpulse;

    public void Simulate(nint environment, float deltaTime) => Calls.Add($"Simulate {deltaTime}");
    public void ResetSimulationClock(nint environment) => Calls.Add("ResetSimulationClock");
    public void DebugCheckContacts(nint environment) { }

    public int ActiveObjects(nint environment, ref nint[] buffer)
    {
        if (buffer.Length < Active.Length)
            buffer = new nint[Active.Length];
        Active.CopyTo(buffer, 0);
        return Active.Length;
    }

    public Vector Gravity(nint environment) => GravityValue;
    public float Timestep(nint environment) => TimestepValue;
    public bool IsAsleep(nint o) => Asleep.Contains(o);
    public readonly Dictionary<nint, Vector> ObjectVelocities = new();
    public Vector Velocity(nint o) => ObjectVelocities.TryGetValue(o, out Vector v) ? v : Vector.Zero;
    public bool IsMoveable(nint o) => !Fixed.Contains(o);
    public bool IsStatic(nint o) => Static.Contains(o);
    public float Mass(nint o) => Masses.TryGetValue(o, out float m) ? m : 10f;
    public ushort GameFlags(nint o) => Flags.TryGetValue(o, out ushort f) ? f : (ushort)0;
    public ushort CallbackFlags(nint o) => Callbacks.TryGetValue(o, out ushort f) ? f : (ushort)0;
    public nint GameData(nint o) => Data.TryGetValue(o, out nint d) ? d : 0;
    public (bool Has, bool PhysicallyControlled, Vector LastImpulse) ShadowController(nint o)
        => Shadows.TryGetValue(o, out var s) ? s : (false, false, Vector.Zero);
    public Vector PlayerControllerLastImpulse(nint controller) => PlayerImpulse;

    public int Contacts(nint o, Span<SnapshotContact> into)
    {
        var all = Snapshots.TryGetValue(o, out var c) ? c : [];
        for (int i = 0; i < all.Length && i < into.Length; i++)
            into[i] = all[i];
        return all.Length;
    }
}

public sealed class FakeFrameNative : IPhysicsFrameNative
{
    public nint Env = 0xE000;
    public readonly Dictionary<nint, uint> Owned = new();
    public readonly List<string> Calls = new();

    public nint Environment() => Env;

    public readonly Dictionary<nint, Vector> Velocities = new();

    public int UpdateOwners(ReadOnlySpan<nint> objects, ReadOnlySpan<float> velocities, Span<uint> owners)
    {
        int n = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            nint o = objects[i];
            Velocities[o] = new Vector(velocities[i * 3], velocities[i * 3 + 1], velocities[i * 3 + 2]);
            Calls.Add($"Update {o:x}");
            if (Owned.TryGetValue(o, out uint h))
                owners[n++] = h;
        }

        return n;
    }
}

public sealed class FakeFrameClock : IPhysicsFrameClock
{
    public float FrameTime { get; set; } = 0.015f;
    public float TickInterval { get; set; } = 0.015f;
    public bool AlternateTicks { get; set; }
    public double Seconds { get; set; }
}

/// <summary>
/// plan_physics_bones_native_zero 2.3: the server's physics frame, managed.
/// Facts hand-derived from native's CPhysicsHook::FrameUpdatePostEntityThink,
/// PhysFrame, PhysTimescale and CSourceSharpPhysicsEnvironment::ClampStep
/// (physics.cpp and physics_shell.h at bc21e4d46; stock's physics.cpp).
/// </summary>
public class PhysicsFrameTests
{
    private static readonly EntityHandle P1 = new(0x0001_0001u), P2 = new(0x0001_0002u);

    private readonly FakeFrameCalls _v = new();
    private readonly FakeFrameNative _native = new();
    private readonly FakeFrameClock _clock = new();
    private readonly FakeShadowNative _shadowNative = new();
    private readonly FakeShadowCalls _shadowCalls = new();
    private readonly List<PhysicsEvent> _posted = new();
    private readonly List<string> _msg = new();
    private readonly List<EntityHandle> _players = new();
    private float _timescale = 1f;
    private bool _speeds;
    private int _ensured;

    private PhysicsFrame Frame()
    {
        var bodies = new PlayerShadowBodies(_shadowNative, _shadowCalls, () => true, _ => { });
        var stress = new PlayerShadowStress(_v, new OpenSolverWorld());
        var frame = new PhysicsFrame(_v, _native, _clock, bodies, stress, () => _players, _posted.Add,
                                     () => _timescale, () => _speeds, _msg.Add, () => { _ensured++; _v.Calls.Add("Ensure"); });
        return frame;
    }

    [Theory]
    [InlineData(0.015f, 1f, 0.015f)]
    [InlineData(0.25f, 1f, 0.1f)]      // a hitch is held to 100 ms
    [InlineData(0.05f, 3f, 0.1f)]      // the timescale cannot buy past the ceiling
    [InlineData(0.05f, 0.5f, 0.025f)]  // applied between the clamps
    [InlineData(1.5f, 1f, 0f)]         // an interrupt is thrown away
    [InlineData(-0.01f, 1f, 0f)]       // so is a clock that ran backwards
    public void The_step_is_clamped_as_stock_clamps_it(float delta, float scale, float expected)
        => Assert.Equal(expected, PhysicsFrame.ClampStep(delta, scale), 6);

    [Fact]
    public void With_no_environment_nothing_steps()
    {
        _native.Env = 0;
        Frame().Frame();
        Assert.Empty(_v.Calls);
    }

    [Fact]
    public void Between_a_levels_pre_and_post_entity_init_nothing_steps()
    {
        PhysicsFrame frame = Frame();
        ((IGameLifecycle)frame).OnLevelInitPreEntity("dm_x");
        frame.Frame();
        Assert.True(frame.Paused);
        Assert.Empty(_v.Calls);

        ((IGameLifecycle)frame).OnLevelInitPostEntity("dm_x");
        frame.Frame();
        Assert.Contains("Simulate 0.015", _v.Calls);
    }

    [Fact]
    public void A_frame_steps_one_tick_and_a_paused_game_steps_zero()
    {
        PhysicsFrame frame = Frame();
        frame.Frame();
        _clock.FrameTime = 0f;
        frame.Frame();
        Assert.Equal(["Simulate 0.015", "Simulate 0"], _v.Calls.Where(c => c.StartsWith("Simulate", StringComparison.Ordinal)));
    }

    [Fact]
    public void Alternate_ticks_step_twice()
    {
        _clock.AlternateTicks = true;
        Frame().Frame();
        Assert.Equal(2, _v.Calls.Count(c => c == "Simulate 0.015"));
    }

    [Fact]
    public void A_timescale_change_resets_the_clock_once_and_scales_the_step()
    {
        PhysicsFrame frame = Frame();
        _timescale = 0.5f;
        frame.Frame();
        frame.Frame();
        Assert.Equal(1, _v.Calls.Count(c => c == "ResetSimulationClock"));
        Assert.Equal(2, _v.Calls.Count(c => c == "Simulate 0.0075"));
    }

    [Fact]
    public void An_interrupt_says_so_and_steps_nothing()
    {
        _clock.TickInterval = 2f;
        Frame().Frame();
        Assert.Equal(["Reset physics clock\n"], _msg);
        Assert.Contains("Simulate 0", _v.Calls);
    }

    [Fact]
    public void Managed_callbacks_are_on_the_environment_before_it_steps()
    {
        Frame().Frame();
        Assert.True(_v.Calls.IndexOf("Ensure") < _v.Calls.IndexOf("Simulate 0.015"));
        Assert.Equal(1, _ensured);
    }

    [Fact]
    public void Each_active_owner_is_updated_and_each_entitys_own_object_reports_an_update()
    {
        _v.Active = [0x100, 0x101, 0x102];
        _native.Owned[0x100] = 0x0001_0010u;
        _native.Owned[0x102] = 0x0001_0012u;
        Frame().Frame();

        Assert.Equal(["Update 100", "Update 101", "Update 102"], _native.Calls);
        Assert.Equal([0x0001_0010u, 0x0001_0012u],
                     _posted.Where(e => e.Kind == PhysicsEventKind.Update).Select(e => e.Entity0));
        Assert.All(_posted, e => Assert.Equal(PhysicsEvent.NoEntity, e.Entity1));
    }

    /// <summary>
    /// STOCK UPDATES EVERY ACTIVE OBJECT (physics.cpp's PhysFrame calls
    /// VPhysicsUpdate on each one GetActiveObjects lists). vphysics lists an
    /// object asleep only on the frame it froze (CSleepObjects: frozen objects
    /// leave the list at the next Simulate's UpdateSleepObjects), and that frame
    /// is where its final pose appears. Which objects are written is the
    /// frame's decision, and it is every one.
    /// </summary>
    [Fact]
    public void An_object_that_froze_this_step_is_still_handed_to_the_owner_update()
    {
        _v.Active = [0x100];
        _v.Asleep.Add(0x100);
        _native.Owned[0x100] = 0x0001_0010u;
        Frame().Frame();

        Assert.Equal(["Update 100"], _native.Calls);
    }

    /// <summary>
    /// The native half carries the frame's decision out and makes none: the
    /// shell's UpdatePhysics writes the object's pose onto the entity without
    /// asking whether it is asleep or static (the asleep-twice skip was a
    /// native decision, and wrong for an object woken and frozen inside one
    /// step).
    /// </summary>
    [Fact]
    public void Each_active_object_is_handed_over_with_its_solver_velocity()
    {
        // The velocity the entity's riders' ground reads is the frame's
        // decision: the object's own, as stock's GetVelocity for a simulated
        // entity. Native holds it (StandaloneEntity_Velocity reads it per trace).
        _v.Active = [0x100, 0x101];
        _v.ObjectVelocities[0x100] = new Vector(10, 0, 0);
        _v.ObjectVelocities[0x101] = new Vector(0, -5, 2);
        Frame().Frame();

        Assert.Equal(new Vector(10, 0, 0), _native.Velocities[0x100]);
        Assert.Equal(new Vector(0, -5, 2), _native.Velocities[0x101]);
    }

    /// <summary>
    /// The movement's per-trace ground read answers the velocity managed
    /// holds on the entity, and picks nothing: the pick between the solver and
    /// the entity's own member was native's (shell_velocity_home.h) and is
    /// managed's (the velocity the frame hands over above, and
    /// EntityPhysicsMotion's routing for an entity with no object).
    /// </summary>
    [Fact]
    public void The_grounds_velocity_relay_reads_the_held_value_and_picks_nothing()
    {
        string code = AbiParityTests.CodeOnly(File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server", "standalone_relays.cpp")));
        int at = code.IndexOf("Vector StandaloneEntity_Velocity( const IGameEntity *pEnt )", StringComparison.Ordinal);
        Assert.True(at >= 0, "StandaloneEntity_Velocity is in standalone_relays.cpp");
        int open = code.IndexOf('{', at);
        string body = code[open..code.IndexOf("\n}", open, StringComparison.Ordinal)];

        Assert.Contains("MoveVelocity()", body);
        Assert.DoesNotContain("GetPhysics", body);
        Assert.DoesNotContain("VelocityHome", body);
    }

    [Fact]
    public void The_shells_update_asks_the_object_nothing_but_its_pose()
    {
        string code = AbiParityTests.CodeOnly(File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server", "shell_standalone_base.cpp")));
        int at = code.IndexOf("void CStandaloneEntity::UpdatePhysics()", StringComparison.Ordinal);
        Assert.True(at >= 0, "CStandaloneEntity::UpdatePhysics is in shell_standalone_base.cpp");
        int open = code.IndexOf('{', at);
        string body = code[open..code.IndexOf("\n}", open, StringComparison.Ordinal)];

        Assert.Contains("GetPosition(", body);
        Assert.DoesNotContain("IsAsleep", body);
        Assert.DoesNotContain("IsStatic", body);
        Assert.DoesNotContain("m_bPhysicsWasAwake", code);
    }

    private void AwakeBody(EntityHandle player, nint stand)
    {
        _players.Add(player);
        _shadowNative.Player = new PlayerShadowFacts
        {
            HasShadow = 1, BodyCurrent = 1, State = PlayerShadow.StateWalk,
            Stand = stand, Crouch = stand + 1, Controller = 0xCC, Ground = 0xFFFFFFFFu,
        };
    }

    [Fact]
    public void A_player_with_an_awake_hull_reports_its_stress_then_its_shadow_update()
    {
        AwakeBody(P1, 0x300);
        _v.Masses[0x300] = 85f;
        _v.Active = [0x300];
        Frame().Frame();

        Assert.Equal([PhysicsEventKind.Stress, PhysicsEventKind.ShadowUpdate],
                     _posted.Select(e => e.Kind));
        PhysicsEvent stress = _posted[0];
        Assert.Equal(P1.Value, stress.Entity0);
        Assert.Equal(85f, stress.Energy);
    }

    [Fact]
    public void An_asleep_hull_reports_neither()
    {
        AwakeBody(P1, 0x300);
        _shadowCalls.Masses[0x300] = 85f;
        _shadowCalls.Asleep.Add(0x300);
        Frame().Frame();
        Assert.Empty(_posted);
    }

    [Fact]
    public void Players_report_in_index_order_every_stress_before_any_shadow_update()
    {
        AwakeBody(P2, 0x300);
        _players.Add(P1);   // the same facts answer both: two awake players
        Frame().Frame();

        Assert.Equal([(PhysicsEventKind.Stress, P1.Value), (PhysicsEventKind.Stress, P2.Value),
                      (PhysicsEventKind.ShadowUpdate, P1.Value), (PhysicsEventKind.ShadowUpdate, P2.Value)],
                     _posted.Select(e => (e.Kind, e.Entity0)));
    }

    [Fact]
    public void Phys_speeds_prints_the_step_and_its_running_average()
    {
        _speeds = true;
        _v.Active = [0x100];
        Frame().Frame();
        Assert.Single(_msg);
        Assert.StartsWith("Physics:   1 objects, ", _msg[0], StringComparison.Ordinal);
        Assert.Contains("ms / AVG: ", _msg[0]);
    }
}

/// <summary>
/// plan_physics_bones_native_zero 2.3: CBasePlayer::VPhysicsUpdate's stress,
/// managed. Facts hand-derived from native's CStandalonePlayer::ShadowStress
/// (shell_standalone_player.cpp at bc21e4d46): which contacts count, how each
/// is read, the subject's impulse and the flags.
/// </summary>
public class PlayerShadowStressTests
{
    private const nint Shadow = 0x300, Env = 0xE000, Floor = OpenSolverWorld.World;
    private static readonly EntityHandle Crate = new(0x0001_0040u);
    private static readonly Vector Down = new(0, 0, -1), Up = new(0, 0, 1);

    private readonly FakeFrameCalls _v = new();
    private readonly OpenSolverWorld _world = new();
    private readonly PlayerShadowStress _stress;
    private PlayerShadowFacts _facts = new() { Controller = 0xCC, MoveType = 2, Ground = 0xFFFFFFFFu };

    public PlayerShadowStressTests()
    {
        _stress = new PlayerShadowStress(_v, _world);
        _v.Masses[Shadow] = 85f;
        _v.Data[0x500] = PhysicsGameData.FromHandle(Crate);
        _v.Masses[0x500] = 600f;
        _v.Fixed.Add(Floor);
        _world.Facts[Crate] = new SolverEntity(0, 6, 0, 6, false, EntityHandle.None, EntityHandle.None);
    }

    private void Contact(nint other, float force, Vector normal)
        => _v.Snapshots[Shadow] = [.. (_v.Snapshots.TryGetValue(Shadow, out var c) ? c : []), new SnapshotContact(other, force, Vector.Zero, normal)];

    // Hand-derived: the floor pushes 6000 up, the crate 6000 down; the squeeze
    // (the smaller side) is 6000, over gravity 600: 10 kg received. Two
    // pushers' slots (world, crate) make it non-static; 600 kg >= 500 is large.
    [Fact]
    public void A_heavy_vphysics_crate_squeezing_a_player_into_the_floor_is_received_as_ten_kilograms()
    {
        Contact(Floor, 6000f, Up);
        Contact(0x500, 6000f, Down);
        (float received, float mass, uint flags) = _stress.Measure(Env, Shadow, in _facts);
        Assert.Equal(85f, mass);
        Assert.Equal(10f, received, 4);
        Assert.Equal(PhysicsEvent.StressNonStatic | PhysicsEvent.StressLargeObject, flags);
    }

    // STATIC PROPS ARE THE WORLD'S (docs/todo.md "Lane C leftovers"): stock's
    // static props carry the world entity (physics.cpp:422), and
    // CalculateObjectStress (physics_impact_damage.cpp:541) puts any contact
    // that is not moveable in slot 0, the static slot, BEFORE it reads the
    // owner; the owner (and the ground and physically-controlled tests) only
    // ever weigh a moveable pusher. So a static prop under the player is the
    // world floor exactly, whoever owns it, and the walk needs no owner rule
    // for it: the crate squeezing the player into one reads the floor's 10 kg.
    [Fact]
    public void A_static_prop_under_a_squeezed_player_is_the_world_floor()
    {
        const nint StaticProp = 0x600;   // NULL game data, as ours are made
        _v.Fixed.Add(StaticProp);
        _v.Static.Add(StaticProp);
        Contact(StaticProp, 6000f, Up);
        Contact(0x500, 6000f, Down);
        (float received, _, uint flags) = _stress.Measure(Env, Shadow, in _facts);

        _v.Snapshots.Clear();
        Contact(Floor, 6000f, Up);
        Contact(0x500, 6000f, Down);
        Assert.Equal(_stress.Measure(Env, Shadow, in _facts), (received, 85f, flags));
        Assert.Equal(10f, received, 4);
        Assert.Equal(PhysicsEvent.StressNonStatic | PhysicsEvent.StressLargeObject, flags);
    }

    [Fact]
    public void A_push_from_one_side_only_squeezes_nothing()
    {
        Contact(0x500, 6000f, Down);
        Assert.Equal(0f, _stress.Measure(Env, Shadow, in _facts).Received);
    }

    [Fact]
    public void A_crate_the_player_stands_on_is_not_a_large_object()
    {
        _facts.Ground = Crate.Value;
        Contact(0x500, 6000f, Up);
        Assert.Equal(PhysicsEvent.StressNonStatic, _stress.Measure(Env, Shadow, in _facts).Flags);
    }

    [Fact]
    public void A_player_held_object_pushes_as_a_friend_and_its_force_is_dropped()
    {
        _v.Flags[0x500] = PlayerShadowStress.PlayerHeld;
        Contact(Floor, 6000f, Up);
        Contact(0x500, 6000f, Down);
        (float received, _, uint flags) = _stress.Measure(Env, Shadow, in _facts);
        Assert.Equal(0u, flags);
        Assert.Equal(0f, received);
    }

    [Fact]
    public void A_contact_with_no_force_is_skipped()
    {
        Contact(0x500, 0f, Down);
        Assert.Equal((0f, 85f, 0u), _stress.Measure(Env, Shadow, in _facts));
    }

    [Fact]
    public void A_pusher_that_is_neither_vphysics_nor_physically_controlled_passes_no_force_to_a_shadow()
    {
        _world.Facts[Crate] = new SolverEntity(0, 6, 0, 7, false, EntityHandle.None, EntityHandle.None);   // MOVETYPE_PUSH
        Contact(Floor, 6000f, Up);
        Contact(0x500, 6000f, Down);
        Assert.Equal(0f, _stress.Measure(Env, Shadow, in _facts).Received);

        _v.Shadows[0x500] = (true, true, Vector.Zero);   // ...unless physically controlled
        Assert.Equal(10f, _stress.Measure(Env, Shadow, in _facts).Received, 4);
    }

    [Fact]
    public void A_penetrating_hull_says_so()
    {
        _v.Flags[Shadow] = PlayerShadowStress.Penetrating;
        Assert.Equal(PhysicsEvent.StressPenetrating, _stress.Measure(Env, Shadow, in _facts).Flags);
    }

    // Hand-derived: the floor's 10000 up is contact 1; 70 crate contacts of 100
    // down follow, of which 63 fit the 64. Down 6300 < up 10000: 6300 / 600.
    [Fact]
    public void Only_the_first_sixty_four_pushing_contacts_are_summed()
    {
        Contact(Floor, 10000f, Up);
        for (int i = 0; i < 70; i++)
            Contact(0x500, 100f, Down);
        Assert.Equal(10.5f, _stress.Measure(Env, Shadow, in _facts).Received, 3);
    }

    // Hand-derived: crate 6000 down, floor 3000 up: inner 3000, outer 3000.
    // The controller's input, |50| / 0.015 * 85 = 283333, outweighs inner, so
    // inner grows by 3000 * (1 - 3000 / 283333) and outer shrinks by as much:
    // 3000 * 3000 / 283333 / 600 = 0.05294 kg received, against 5 without.
    [Fact]
    public void The_player_controllers_last_impulse_resists_the_push_when_the_hull_is_one()
    {
        _v.PlayerImpulse = new Vector(0, 0, 50);
        Contact(Floor, 3000f, Up);
        Contact(0x500, 6000f, Down);
        Assert.Equal(5f, _stress.Measure(Env, Shadow, in _facts).Received, 4);

        _v.Callbacks[Shadow] = PlayerShadowStress.IsPlayerController;
        Assert.Equal(0.05294f, _stress.Measure(Env, Shadow, in _facts).Received, 4);
    }

    [Fact]
    public void A_shadow_controllers_impulse_wins_over_the_player_controllers()
    {
        _v.PlayerImpulse = new Vector(0, 0, 50);
        _v.Callbacks[Shadow] = PlayerShadowStress.IsPlayerController;
        _v.Shadows[Shadow] = (true, false, Vector.Zero);
        Contact(Floor, 3000f, Up);
        Contact(0x500, 6000f, Down);
        Assert.Equal(5f, _stress.Measure(Env, Shadow, in _facts).Received, 4);
    }
}
