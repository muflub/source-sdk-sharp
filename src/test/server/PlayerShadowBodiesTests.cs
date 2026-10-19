using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>Native's half of the shadow as a test sets it: one player's facts, and every call logged.</summary>
public sealed class FakeShadowNative : IPlayerShadowNative
{
    public PlayerShadowFacts Player;
    public bool Exists = true;
    public bool AdoptSucceeds = true;
    public nint Env = 0xE000;
    public readonly List<string> Calls = new();

    public bool Facts(EntityHandle player, out PlayerShadowFacts facts)
    {
        facts = Player;
        return Exists;
    }

    public bool Adopt(EntityHandle player, nint stand, nint crouch, nint controller)
    {
        Calls.Add($"Adopt {stand:x} {crouch:x} {controller:x}");
        if (!AdoptSucceeds)
            return false;
        Player.HasShadow = 1;
        Player.BodyCurrent = 1;
        Player.Stand = stand;
        Player.Crouch = crouch;
        Player.Controller = controller;
        Player.State = -1;
        return true;
    }

    public void Release(EntityHandle player)
    {
        Calls.Add("Release");
        Player.HasShadow = 0;
        Player.Stand = Player.Crouch = Player.Controller = 0;
        Player.State = -1;
    }

    public void Commit(EntityHandle player, int state, bool afterCommand)
    {
        Calls.Add($"Commit {state} {afterCommand}");
        Player.State = state;
    }

    public nint Environment() => Env;
}

/// <summary>The touched entities as a test sets them: movetype, solid, flags and object per handle.</summary>
public sealed class FakeTouchWorld : IPlayerTouchWorld
{
    public readonly Dictionary<EntityHandle, (int MoveType, int Solid, int SolidFlags, nint Object)> Entities = new();

    public bool TryEntity(EntityHandle entity, out int moveType, out int solidType, out int solidFlags, out nint physicsObject)
    {
        bool found = Entities.TryGetValue(entity, out var e);
        (moveType, solidType, solidFlags, physicsObject) = e;
        return found;
    }
}

/// <summary>vphysics as the shadow calls it: answers a test sets, every call logged in order.</summary>
public sealed class FakeShadowCalls : IVPhysicsShadowCalls
{
    public readonly List<string> Calls = new();
    public readonly List<VPhysicsObjectParams> Params = new();
    public nint NextObject = 0x100;
    public bool CreateFails, CreateCrouchFails, ControllerFails, NoCollision;
    public readonly Dictionary<nint, Vector> Positions = new();
    public readonly Dictionary<nint, float> Masses = new();
    public readonly HashSet<nint> Moveable = new();
    public bool ShadowUpdated, Frozen, InContact;
    public Vector ShadowVel;

    public nint BoxCollide(Vector mins, Vector maxs)
    {
        Calls.Add($"BoxCollide {mins} {maxs}");
        return NoCollision ? 0 : (nint)(0xC000 + Calls.Count);
    }

    public int SurfaceIndex(string name)
    {
        Calls.Add($"SurfaceIndex {name}");
        return 42;
    }

    public nint CreatePolyObject(nint environment, nint collide, int surfaceProps, Vector origin, in VPhysicsObjectParams p)
    {
        Params.Add(p);
        bool fail = CreateFails || (CreateCrouchFails && Params.Count == 2);
        Calls.Add($"CreatePolyObject {surfaceProps} {origin}");
        return fail ? 0 : NextObject++;
    }

    public void DestroyObject(nint environment, nint o) => Calls.Add($"DestroyObject {o:x}");

    public nint CreatePlayerController(nint environment, nint o)
    {
        Calls.Add($"CreatePlayerController {o:x}");
        return ControllerFails ? 0 : 0xCC;
    }

    public void DestroyPlayerController(nint environment, nint c) => Calls.Add($"DestroyPlayerController {c:x}");
    public void SetCallbackFlags(nint o, ushort flags) => Calls.Add($"SetCallbackFlags {o:x} {flags:x}");
    public void SetPosition(nint o, Vector origin, bool teleport) => Calls.Add($"SetPosition {o:x} {origin} {teleport}");
    public void SetVelocity(nint o, Vector linear, Vector? angular) => Calls.Add($"SetVelocity {o:x} {linear} {(angular is null ? "null" : angular.ToString())}");
    public void EnableCollisions(nint o, bool enable) => Calls.Add($"EnableCollisions {o:x} {enable}");
    public Vector Position(nint o) => Positions.TryGetValue(o, out Vector v) ? v : Vector.Zero;
    public float Mass(nint o) => Masses.TryGetValue(o, out float m) ? m : 0f;
    public bool IsMoveable(nint o) => Moveable.Contains(o);
    public readonly HashSet<nint> Asleep = new();

    public bool IsAsleep(nint o) => Asleep.Contains(o);
    public void Update(nint c, Vector position, Vector velocity, float arrival, bool onGround, nint ground)
        => Calls.Add($"Update {c:x} {position} {velocity} {arrival} {onGround} {ground:x}");
    public void SetObject(nint c, nint o) => Calls.Add($"SetObject {c:x} {o:x}");
    public void StepUp(nint c, float height) => Calls.Add($"StepUp {c:x} {height}");
    public void Jump(nint c) => Calls.Add($"Jump {c:x}");
    public void SetPushLimits(nint c, float mass, float speed) => Calls.Add($"SetPushLimits {c:x} {mass} {speed}");
    public (bool Updated, Vector Position) ShadowPosition(nint c) => (ShadowUpdated, Vector.Zero);
    public Vector ShadowVelocity(nint c) => ShadowVel;
    public bool WasFrozen(nint c) => Frozen;
    public bool IsInContact(nint c) => InContact;
}

/// <summary>
/// plan_physics_bones_native_zero 2.5: the player shadow's vphysics half,
/// managed. Facts hand-derived from native's CStandalonePlayer::InitVCollision,
/// SetVCollisionState, ShadowProbe, ShadowApply and TeleportShadow
/// (shell_standalone_player.cpp at bc21e4d46), which are stock's
/// CBasePlayer::InitVCollision / SetupVPhysicsShadow / SetVCollisionState /
/// UpdateVPhysicsPosition / TeleportEntity's player arm (player.cpp).
/// </summary>
public class PlayerShadowBodiesTests
{
    private static readonly EntityHandle P = new(0x0001_0001u);
    private static readonly Vector Origin = new(10, 20, 30);
    private static readonly Vector Velocity = new(1, 2, 3);
    private static readonly Vector StandMin = new(-16, -16, 0), StandMax = new(16, 16, 72);
    private static readonly Vector CrouchMin = new(-16, -16, 0), CrouchMax = new(16, 16, 36);

    private readonly FakeShadowNative _native = new();
    private readonly FakeShadowCalls _v = new();
    private readonly FakeTouchWorld _touch = new();
    private readonly List<string> _warnings = new();
    private bool _enabled = true;
    private readonly PlayerShadowBodies _bodies;

    public PlayerShadowBodiesTests()
    {
        _bodies = new PlayerShadowBodies(_native, _v, () => _enabled, _warnings.Add, _touch);
        _native.Player = new PlayerShadowFacts
        {
            State = -1,
            Origin = Origin,
            Velocity = Velocity,
            Flags = PlayerShadowBodies.FlOnGround,
            FrameTime = 0.015f,
            TickInterval = 0.015f,
            Ground = 0xFFFFFFFFu,
        };
    }

    private bool Init() => _bodies.Init(P, StandMin, StandMax, CrouchMin, CrouchMax, 85f, 350f, 50f);

    private void GiveBody(int state = PlayerShadow.StateWalk)
    {
        _native.Player.HasShadow = 1;
        _native.Player.BodyCurrent = 1;
        _native.Player.Stand = 0x100;
        _native.Player.Crouch = 0x101;
        _native.Player.Controller = 0xCC;
        _native.Player.State = state;
    }

    private static readonly string[] WalkState =
    [
        $"SetPosition 100 {Origin} True", $"SetVelocity 100 {Velocity} null", "EnableCollisions 101 False",
        "SetObject cc 100", "EnableCollisions 100 True",
    ];

    // ---- Init -----------------------------------------------------------

    [Fact]
    public void A_handle_that_names_no_player_builds_nothing()
    {
        _native.Exists = false;
        Assert.False(Init());
        Assert.Empty(_v.Calls);
        Assert.Empty(_native.Calls);
    }

    [Fact]
    public void With_the_switch_off_no_body_is_built()
    {
        _enabled = false;
        Assert.False(Init());
        Assert.Empty(_v.Calls);
    }

    [Fact]
    public void Between_levels_or_with_no_collision_table_no_body_is_built()
    {
        _native.Env = 0;
        Assert.False(Init());
        Assert.Empty(_v.Calls);

        _native.Env = 0xE000;
        _v.NoCollision = true;
        Assert.False(Init());
        Assert.DoesNotContain(_v.Calls, c => c.StartsWith("CreatePolyObject", StringComparison.Ordinal));
    }

    [Fact]
    public void A_non_finite_origin_is_refused_and_recorded_as_the_creation_site()
    {
        _native.Player.Origin = new Vector(float.NaN, 0, 0);
        Assert.False(Init());
        Assert.DoesNotContain(_v.Calls, c => c.StartsWith("CreatePolyObject", StringComparison.Ordinal));
        Assert.Equal(1u, _bodies.Nan.Refusals);
        Assert.Equal(PhysicsNanGuard.SiteShadowCreate, _bodies.Nan.FirstSite);
        Assert.Single(_warnings);
    }

    [Fact]
    public void A_body_is_two_box_hulls_a_controller_aimed_at_the_player_and_the_stand_hull_in_play()
    {
        Assert.True(Init());

        Assert.Equal(
        [
            $"BoxCollide {StandMin} {StandMax}", $"BoxCollide {CrouchMin} {CrouchMax}", "SurfaceIndex player",
            $"CreatePolyObject 42 {Origin}", $"CreatePolyObject 42 {Origin}",
            "SetCallbackFlags 100 11", "SetCallbackFlags 101 11",
            "CreatePlayerController 100", "SetPushLimits cc 350 50",
            $"Update cc {Origin} {Vector.Zero} 0.015 True 0",
            .. WalkState,
        ], _v.Calls);
        Assert.Equal(["Adopt 100 101 cc", "Commit 0 False"], _native.Calls);
        Assert.Equal(1, _bodies.Built);
    }

    [Fact]
    public void The_hulls_are_made_with_stocks_parameters()
    {
        Assert.True(Init());
        VPhysicsObjectParams p = _v.Params[0];
        Assert.Equal(85f, p.Mass);
        Assert.Equal(1e24f, p.Inertia);
        Assert.Equal(0.1f, p.Damping);
        Assert.Equal(0.1f, p.RotDamping);
        Assert.Equal(0.05f, p.RotInertiaLimit);
        Assert.Equal(0f, p.DragCoefficient);
        Assert.Equal(0f, p.Volume);
        Assert.Equal(0, p.EnableCollisions);
        Assert.Equal((nint)0, p.MassCenterOverride);
        Assert.Equal(PhysicsGameData.FromHandle(P), p.GameData);
        Assert.Equal(P, PhysicsGameData.Owner(p.GameData));
        Assert.Equal("player_stand", Marshal.PtrToStringUTF8(_v.Params[0].Name));
        Assert.Equal("player_crouch", Marshal.PtrToStringUTF8(_v.Params[1].Name));
    }

    [Fact]
    public void A_ducking_player_gets_the_crouch_hull_in_play()
    {
        _native.Player.Flags |= PlayerShadowBodies.FlDucking;
        Assert.True(Init());
        Assert.Equal(
        [
            $"SetPosition 101 {Origin} True", $"SetVelocity 101 {Velocity} null", "EnableCollisions 100 False",
            "SetObject cc 101", "EnableCollisions 101 True",
        ], _v.Calls.TakeLast(5));
        Assert.Equal("Commit 1 False", _native.Calls[^1]);
    }

    [Fact]
    public void A_hull_that_will_not_build_takes_the_other_with_it()
    {
        _v.CreateCrouchFails = true;
        Assert.False(Init());
        Assert.Contains("DestroyObject 100", _v.Calls);
        Assert.DoesNotContain(_v.Calls, c => c.StartsWith("CreatePlayerController", StringComparison.Ordinal));
        Assert.Empty(_native.Calls);
    }

    [Fact]
    public void A_controller_that_will_not_build_takes_both_hulls_with_it()
    {
        _v.ControllerFails = true;
        Assert.False(Init());
        Assert.Equal(["DestroyObject 100", "DestroyObject 101"], _v.Calls.TakeLast(2));
        Assert.Empty(_native.Calls);
    }

    [Fact]
    public void A_player_gone_before_the_body_is_held_takes_the_whole_body_with_it()
    {
        _native.AdoptSucceeds = false;
        Assert.False(Init());
        Assert.Equal(["DestroyPlayerController cc", "DestroyObject 100", "DestroyObject 101"], _v.Calls.TakeLast(3));
        Assert.Equal(0, _bodies.Built);
    }

    [Fact]
    public void Asking_again_is_a_respawn_and_teleports_the_body_rather_than_building_another()
    {
        GiveBody(PlayerShadow.StateCrouch);
        Assert.True(Init());
        Assert.Equal(WalkState, _v.Calls);
        Assert.Equal(["Commit -1 False", "Commit 0 False"], _native.Calls);
        Assert.Equal(0, _bodies.Built);
    }

    [Fact]
    public void A_body_from_the_previous_level_is_dropped_and_a_new_one_built()
    {
        GiveBody();
        _native.Player.BodyCurrent = 0;
        Assert.True(Init());
        Assert.Equal("Release", _native.Calls[0]);
        Assert.Contains("Adopt 100 101 cc", _native.Calls);
        Assert.Equal(1, _bodies.Built);
    }

    // ---- shadow_priming_pin_test's cases, carried (2.5) -------------------
    //
    // native's gtest pinned InitVCollision's text; the body is managed's now,
    // so each case is held over the calls it makes.

    private int IndexOf(string prefix) => _v.Calls.FindIndex(c => c.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void The_priming_update_is_still_there_between_the_controller_and_the_state()
    {
        Assert.True(Init());
        int create = IndexOf("CreatePlayerController");
        int prime = IndexOf("Update cc");
        int state = IndexOf("SetPosition 100");
        Assert.True(create >= 0 && prime > create && state > prime, string.Join(" | ", _v.Calls));
    }

    [Fact]
    public void The_priming_update_passes_a_zero_velocity_because_that_is_what_clears_m_enable()
    {
        Assert.True(Init());
        Assert.StartsWith($"Update cc {Origin} {Vector.Zero} ", _v.Calls[IndexOf("Update cc")], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PlayerShadowBodies.FlDucking)]
    [InlineData(PlayerShadowBodies.FlOnGround | PlayerShadowBodies.FlDucking)]
    public void The_priming_update_is_not_conditional_on_anything_the_player_is_doing(int flags)
    {
        _native.Player.Flags = flags;
        Assert.True(Init());
        Assert.Equal(1, _v.Calls.Count(c => c.StartsWith("Update cc", StringComparison.Ordinal)));
    }

    [RepoSourceFact("src/sourcesharp/managed/SourceSharp.Server.Interop/PlayerShadowBodies.cs")]
    public void The_reason_travels_with_the_call_and_not_only_in_a_handover()
    {
        string text = File.ReadAllText(RepoSourceFactAttribute.Find(
            "src/sourcesharp/managed/SourceSharp.Server.Interop/PlayerShadowBodies.cs")!);
        int prime = text.IndexOf("_v.Update(controller, f.Origin, Vector.Zero, f.FrameTime", StringComparison.Ordinal);
        Assert.True(prime > 0, "the priming Update is gone");
        string window = text.Substring(Math.Max(0, prime - 600), 600).ToLowerInvariant();
        Assert.Contains("rash", window);
        Assert.Contains("m_targetposition", window);
    }

    // ---- Probe ----------------------------------------------------------

    [Fact]
    public void A_player_with_no_body_probes_as_nothing()
    {
        Assert.Null(_bodies.Probe(P));
        Assert.Equal(0, _bodies.Updates);
    }

    [Fact]
    public void The_probe_carries_natives_facts_and_vphysics_own_reads()
    {
        GiveBody();
        _bodies.Memory.SetOldOrigin(P, new Vector(9, 9, 9));
        _native.Player.OutWishVel = new Vector(5, 0, 0);
        _native.Player.OutStepHeight = 2f;
        _native.Player.MaxSpeed = 320f;
        TouchCrate();
        _v.Positions[0x100] = new Vector(11, 21, 31);
        _v.Masses[0x100] = 85f;
        _v.ShadowUpdated = true;
        _v.ShadowVel = new Vector(7, 7, 7);
        _v.Frozen = true;
        _v.InContact = true;

        PlayerShadowState s = _bodies.Probe(P, out _, afterCommand: true)!.Value;

        Assert.Equal(1, s.HasShadow);
        Assert.Equal(0, s.Enabled);   // the step writes it
        Assert.Equal(Origin, s.Origin);
        Assert.Equal(new Vector(9, 9, 9), s.OldOrigin);
        Assert.Equal(new Vector(5, 0, 0), s.OutWishVel);
        Assert.Equal(2f, s.OutStepHeight);
        Assert.Equal(320f, s.MaxSpeed);
        Assert.Equal(PlayerShadow.StateWalk, s.State);
        Assert.Equal(1, s.HasShadowPosition);
        Assert.Equal(new Vector(11, 21, 31), s.ShadowOrigin);
        Assert.Equal(1, s.PhysicsUpdated);
        Assert.Equal(new Vector(7, 7, 7), s.ShadowVelocity);
        Assert.Equal(1, s.WasFrozen);
        Assert.Equal(0, s.PenetratingMover);
        Assert.Equal(85f, s.ShadowMass);
        Assert.Equal(PlayerShadow.ContactController | PlayerShadow.ContactTouched, s.ContactBits);
        Assert.Equal(0.015f, s.FrameTime);
        Assert.Equal((1, 1, 1), (_bodies.Updates, _bodies.Contacts, _bodies.Touches));
    }

    // ---- stock's CBasePlayer::Touch, over the command's touch list ----------

    private static readonly EntityHandle Crate = new(0x0002_0040u);
    private const nint CrateObject = 0x500;

    /// <summary>The move ran into a moveable physics crate: MOVETYPE_VPHYSICS, SOLID_VPHYSICS, moveable object.</summary>
    private void TouchCrate(int moveType = 6, int solid = 6, int flags = 0, bool moveable = true, bool wasGround = false)
    {
        _touch.Entities[Crate] = (moveType, solid, flags, CrateObject);
        if (moveable)
            _v.Moveable.Add(CrateObject);
        _native.Player.Touched = 1;
        _native.Player.TouchedGround = wasGround ? 1u : 0u;
        _native.Player.TouchedEntities[0] = Crate.Value;
    }

    private bool TouchedAfterCommand()
        => (_bodies.Probe(P, out _, afterCommand: true)!.Value.ContactBits & PlayerShadow.ContactTouched) != 0;

    [Fact]
    public void A_moveable_physics_object_the_move_ran_into_is_a_touch()
    {
        GiveBody();
        TouchCrate();
        Assert.True(TouchedAfterCommand());
        Assert.True(_bodies.Memory.TouchedPhysics(P));
    }

    [Theory]
    [InlineData(5, 6, 0, true, false)]    // MOVETYPE_STEP: not simulated
    [InlineData(6, 2, 0, true, false)]    // SOLID_BBOX
    [InlineData(6, 6, 0x0008, true, false)] // FSOLID_TRIGGER
    [InlineData(6, 6, 0, false, false)]   // a motion-disabled or static object
    [InlineData(6, 6, 0, true, true)]     // the ground it stood on: leaning is not shoving
    public void Stocks_four_tests_and_the_ground_refuse_a_touch(int moveType, int solid, int flags, bool moveable, bool ground)
    {
        GiveBody();
        TouchCrate(moveType, solid, flags, moveable, ground);
        Assert.False(TouchedAfterCommand());
    }

    [Fact]
    public void The_recoverys_probe_reads_what_the_command_left()
    {
        // m_touchedPhysObject lasts from the command until the next one clears
        // it, so VPhysicsShadowUpdate (the recovery's probe) sees it.
        GiveBody();
        TouchCrate();
        TouchedAfterCommand();

        _native.Player.Touched = 0; // whatever native's list says now
        PlayerShadowState s = _bodies.Probe(P)!.Value;
        Assert.NotEqual(0, s.ContactBits & PlayerShadow.ContactTouched);

        // and the next command, touching nothing, clears it
        Assert.False(TouchedAfterCommand());
    }

    [Fact]
    public void A_noclip_player_has_no_shadow_position_and_no_shadow_mass()
    {
        GiveBody(PlayerShadow.StateNoClip);
        PlayerShadowState s = _bodies.Probe(P)!.Value;
        Assert.Equal(0, s.HasShadowPosition);
        Assert.Equal(0f, s.ShadowMass);
        Assert.Equal((1, 0, 0), (_bodies.Updates, _bodies.Contacts, _bodies.Touches));
    }

    [Theory]
    [InlineData(6, true, 400f)]    // a moveable MOVETYPE_VPHYSICS ground: its mass
    [InlineData(6, false, 0f)]     // not moveable: not physics ground
    [InlineData(7, true, 0f)]      // MOVETYPE_PUSH: not physics ground
    public void The_ground_is_physics_only_when_a_moveable_vphysics_object(int moveType, bool moveable, float mass)
    {
        GiveBody();
        _native.Player.Ground = 0x0001_0040u;
        _native.Player.GroundMoveType = moveType;
        _native.Player.GroundObject = 0x500;
        _v.Masses[0x500] = 400f;
        if (moveable)
            _v.Moveable.Add(0x500);

        Assert.Equal(mass, _bodies.Probe(P)!.Value.GroundPhysicsMass);
    }

    // ---- Apply ----------------------------------------------------------

    private static PlayerShadowPlan Plan(Vector position, Vector wish, float arrival = 0.015f, bool onGround = true)
        => new() { Valid = 1, State = PlayerShadow.StateWalk, Position = position, WishVelocity = wish, Arrival = arrival, OnGround = onGround ? 1 : 0 };

    private void Apply(in PlayerShadowPlan plan) => _bodies.Apply(P, in _native.Player, in plan);

    // ---- stock's one m_oldOrigin (player.cpp:4851, :8338, :8487) -----------

    [Fact]
    public void The_probe_reads_the_one_old_origin()
    {
        GiveBody();
        _bodies.Memory.SetOldOrigin(P, new Vector(4, 5, 6));

        Assert.Equal(new Vector(4, 5, 6), _bodies.Probe(P)!.Value.OldOrigin);
    }

    [Fact]
    public void A_command_leaves_where_it_put_the_player_as_the_old_origin()
    {
        // PostThinkVPhysics' last line: m_oldOrigin = GetAbsOrigin().
        GiveBody();
        Apply(Plan(new Vector(1, 1, 1), new Vector(320, 320, 320)));

        Assert.Equal(Origin, _bodies.Memory.OldOrigin(P));
    }

    [Fact]
    public void The_cost_meter_times_each_apply()
    {
        GiveBody();
        Apply(Plan(new Vector(1, 1, 1), new Vector(320, 320, 320)));
        Apply(default);

        Assert.Equal(2, _bodies.ApplyCost.Calls);
        Assert.True(_bodies.ApplyCost.TotalTicks > 0, "a timed apply has a duration");
    }

    [Fact]
    public void An_invalid_plan_does_nothing()
    {
        GiveBody();
        Apply(default);
        Assert.Empty(_v.Calls);
        Assert.Empty(_native.Calls);
    }

    [Fact]
    public void A_destroy_plan_releases_the_body_and_does_nothing_else()
    {
        GiveBody();
        Apply(new PlayerShadowPlan { Valid = 1, Destroy = 1 });
        Assert.Equal(["Release"], _native.Calls);
        Assert.Empty(_v.Calls);
    }

    [Fact]
    public void A_plain_command_commits_then_aims_the_controller()
    {
        GiveBody();
        Apply(Plan(new Vector(1, 1, 1), new Vector(320, 320, 320)));
        Assert.Equal(["Commit 0 True"], _native.Calls);
        Assert.Equal([$"Update cc {new Vector(1, 1, 1)} {new Vector(320, 320, 320)} 0.015 True 0"], _v.Calls);
    }

    [Fact]
    public void A_hull_change_is_applied_first_and_committed_with_the_command()
    {
        GiveBody(PlayerShadow.StateWalk);
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.State = PlayerShadow.StateCrouch;
        plan.StateChanged = 1;
        plan.StateOrigin = Origin;
        plan.StateVelocity = Velocity;
        Apply(plan);

        Assert.Equal(
        [
            $"SetPosition 101 {Origin} True", $"SetVelocity 101 {Velocity} null", "EnableCollisions 100 False",
            "SetObject cc 101", "EnableCollisions 101 True",
        ], _v.Calls.Take(5));
        Assert.Equal(["Commit 1 True"], _native.Calls);
    }

    [Fact]
    public void A_non_finite_hull_change_is_refused_and_the_old_hull_stays_in_play()
    {
        GiveBody(PlayerShadow.StateWalk);
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.State = PlayerShadow.StateCrouch;
        plan.StateChanged = 1;
        plan.StateOrigin = new Vector(0, float.PositiveInfinity, 0);
        Apply(plan);

        Assert.Equal(["Commit 0 True"], _native.Calls);
        Assert.Equal(PhysicsNanGuard.SiteShadowState, _bodies.Nan.FirstSite);
        Assert.Single(_warnings);
    }

    [Fact]
    public void A_climb_steps_the_controller_up_and_jumps()
    {
        GiveBody();
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.StepUp = PlayerShadow.StepClimb;
        plan.StepHeight = 3f;
        Apply(plan);
        Assert.Equal(["StepUp cc 3", "Jump cc"], _v.Calls.Take(2));
    }

    [Fact]
    public void A_tall_step_teleports_the_active_hull_to_the_player_and_jumps()
    {
        GiveBody();
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.StepUp = PlayerShadow.StepTeleport;
        Apply(plan);
        Assert.Equal([$"SetPosition 100 {Origin} True", "Jump cc"], _v.Calls.Take(2));
    }

    [Fact]
    public void A_step_with_no_hull_in_play_is_not_taken()
    {
        GiveBody(PlayerShadow.StateNoClip);
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.State = PlayerShadow.StateNoClip;
        plan.StepUp = PlayerShadow.StepClimb;
        plan.StepHeight = 3f;
        Apply(plan);
        Assert.DoesNotContain(_v.Calls, c => c.StartsWith("StepUp", StringComparison.Ordinal) || c.StartsWith("Jump", StringComparison.Ordinal));
    }

    [Fact]
    public void A_snap_places_the_hull_and_counts()
    {
        GiveBody();
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.Snap = 1;
        plan.SnapOrigin = new Vector(5, 5, 5);
        plan.SnapVelocity = new Vector(1, 0, 0);
        Apply(plan);
        Assert.Equal([$"SetPosition 100 {new Vector(5, 5, 5)} True", $"SetVelocity 100 {new Vector(1, 0, 0)} null"], _v.Calls.Take(2));
        Assert.Equal(1, _bodies.Snaps);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0.015f)]
    [InlineData(0f, float.PositiveInfinity, 0.015f)]
    [InlineData(0f, 0f, float.NaN)]
    [InlineData(4e7f, 0f, 0.015f)]
    public void A_non_finite_controller_update_is_refused_after_the_commit(float x, float wish, float arrival)
    {
        GiveBody();
        Apply(Plan(new Vector(x, 0, 0), new Vector(wish, 0, 0), arrival));
        Assert.Equal(["Commit 0 True"], _native.Calls);
        Assert.DoesNotContain(_v.Calls, c => c.StartsWith("Update", StringComparison.Ordinal));
        Assert.Single(_warnings);
    }

    [Fact]
    public void A_rideable_ground_is_handed_to_the_controller()
    {
        GiveBody();
        _native.Player.Ground = 0x0001_0040u;
        _native.Player.GroundMoveType = 6;
        _native.Player.GroundObject = 0x500;
        _v.Moveable.Add(0x500);
        PlayerShadowPlan plan = Plan(Origin, Vector.Zero);
        plan.RideableGround = 1;
        Apply(plan);
        Assert.EndsWith(" 500", _v.Calls[^1], StringComparison.Ordinal);
    }

    // ---- Teleport -------------------------------------------------------

    [Fact]
    public void A_teleport_moves_the_active_hull_re_aims_the_controller_and_can_stop_it()
    {
        GiveBody();
        var to = new Vector(100, 0, 0);
        _bodies.Teleport(P, to, zeroVelocity: true);
        Assert.Equal(
        [
            $"SetPosition 100 {to} True", $"Update cc {to} {Vector.Zero} 0.015 True 0",
            $"SetVelocity 100 {Vector.Zero} {Vector.Zero}",
        ], _v.Calls);
    }

    [Fact]
    public void A_teleport_with_no_hull_in_play_moves_nothing()
    {
        GiveBody(PlayerShadow.StateNoClip);
        _bodies.Teleport(P, Origin, zeroVelocity: false);
        Assert.Empty(_v.Calls);
    }
}

/// <summary>native's SourceSharpAcceptPhysicsVector and its ledger, managed.</summary>
public class PhysicsNanGuardTests
{
    [Theory]
    [InlineData(0f, true)]
    [InlineData(-16384f, true)]
    [InlineData(3.2767e7f, true)]
    [InlineData(3.2768e7f, false)]
    [InlineData(-4e7f, false)]
    [InlineData(float.NaN, false)]
    [InlineData(float.NegativeInfinity, false)]
    public void A_coordinate_is_finite_inside_two_thousand_maps(float f, bool finite)
        => Assert.Equal(finite, PhysicsNanGuard.IsFiniteCoord(f));

    [Fact]
    public void The_first_refusal_is_kept_and_every_one_counted()
    {
        var guard = new PhysicsNanGuard();
        Assert.True(guard.Accept(new Vector(1, 2, 3), PhysicsNanGuard.SiteShadowState));
        Assert.Equal(PhysicsNanGuard.SiteNone, guard.FirstSite);

        Assert.False(guard.Accept(new Vector(float.NaN, 0, 0), PhysicsNanGuard.SiteControllerUpdate));
        Assert.False(guard.Accept(new Vector(0, 5e7f, 0), PhysicsNanGuard.SiteShadowSnap));

        Assert.Equal(2u, guard.Refusals);
        Assert.Equal(PhysicsNanGuard.SiteControllerUpdate, guard.FirstSite);
        Assert.True(float.IsNaN(guard.FirstValue.X));
    }
}

/// <summary>objectparams_t's and SourceSharpPlayerShadowFacts' offsets, held here.</summary>
public class PlayerShadowLayoutTests
{
    [Theory]
    [InlineData(nameof(VPhysicsObjectParams.MassCenterOverride), 0)]
    [InlineData(nameof(VPhysicsObjectParams.Mass), 8)]
    [InlineData(nameof(VPhysicsObjectParams.Inertia), 12)]
    [InlineData(nameof(VPhysicsObjectParams.Damping), 16)]
    [InlineData(nameof(VPhysicsObjectParams.RotDamping), 20)]
    [InlineData(nameof(VPhysicsObjectParams.RotInertiaLimit), 24)]
    [InlineData(nameof(VPhysicsObjectParams.Name), 32)]
    [InlineData(nameof(VPhysicsObjectParams.GameData), 40)]
    [InlineData(nameof(VPhysicsObjectParams.Volume), 48)]
    [InlineData(nameof(VPhysicsObjectParams.DragCoefficient), 52)]
    [InlineData(nameof(VPhysicsObjectParams.EnableCollisions), 56)]
    public void Object_params_are_at_the_headers_offsets(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<VPhysicsObjectParams>(field));

    [Fact]
    public void Object_params_are_the_headers_size()
        => Assert.Equal(VPhysicsObjectParams.SizeInBytes, Marshal.SizeOf<VPhysicsObjectParams>());

    [Theory]
    [InlineData(nameof(PlayerShadowFacts.HasShadow), 4)]
    [InlineData(nameof(PlayerShadowFacts.State), 12)]
    [InlineData(nameof(PlayerShadowFacts.Stand), 16)]
    [InlineData(nameof(PlayerShadowFacts.Controller), 32)]
    [InlineData(nameof(PlayerShadowFacts.Origin), 40)]
    [InlineData(nameof(PlayerShadowFacts.OutStepHeight), 76)]
    [InlineData(nameof(PlayerShadowFacts.Ground), 100)]
    [InlineData(nameof(PlayerShadowFacts.GroundObject), 112)]
    [InlineData(nameof(PlayerShadowFacts.Touched), 120)]
    [InlineData(nameof(PlayerShadowFacts.TouchedGround), 124)]
    [InlineData(nameof(PlayerShadowFacts.TouchedEntities), 128)]
    public void The_facts_are_at_natives_offsets(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<PlayerShadowFacts>(field));

    [Fact]
    public void The_facts_are_natives_size()
        => Assert.Equal(PlayerShadowFacts.SizeInBytes, Marshal.SizeOf<PlayerShadowFacts>());
}
