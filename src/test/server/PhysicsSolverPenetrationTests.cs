using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A clock a test sets.</summary>
public sealed class SolverClock : IGameClock
{
    public float Now { get; set; }
}

/// <summary>
/// plan_physics_bones_native_zero 2.1: ShouldSolvePenetration and the
/// penetration ledger's frame, decided by managed mid-step (user ruling
/// 2026-10-11). Hand-derived from native's CCollisionEvent::ShouldSolvePenetration,
/// UpdatePenetrateEvents and PhysForceEntityToSleep (physics.cpp 1472-1522,
/// 1787-1876 at dcae1517d) and stock's (game/server/physics.cpp:800-1040).
/// </summary>
public class PhysicsSolverPenetrationTests
{
    private readonly OpenSolverWorld _world = new();
    private readonly FakeSolverObjects _objects = new();
    private readonly SolverClock _clock = new();
    private readonly List<PhysicsEvent> _posted = new();
    private readonly PhysicsSolver _solver;

    private static readonly EntityHandle A = new(0x0001_0010u);
    private static readonly EntityHandle B = new(0x0001_0011u);

    private static nint Obj(EntityHandle e) => OpenSolverWorld.ObjectOf(e);

    private const uint Penetrating = PhysicsSolver.PenetratingGameFlag;

    public PhysicsSolverPenetrationTests()
    {
        _solver = new PhysicsSolver(_world, _objects, new PhysicsSolverRules(), new EntityPairCollisions(_world, _objects),
                                    new PhysicsPenetrationLedger(), _clock, _posted.Add, new RecordingCrushes(), () => 0, _ => { });
        _objects.Owned(Obj(A), A);
        _objects.Owned(Obj(B), B);
        _objects.At(OpenSolverWorld.World).Static = true;
        _objects.At(OpenSolverWorld.World).Moveable = false;
    }

    private int Ask(nint o0, nint o1, float now)
    {
        _clock.Now = now;
        return _solver.ShouldSolvePenetration(o0, o1);
    }

    private void Frame(float now)
    {
        _clock.Now = now;
        _solver.FrameUpdate();
    }

    [Fact]
    public void The_owners_are_the_game_data_vphysics_hands_with_the_ask()
    {
        // Two objects handed one owner's game data are one entity with itself:
        // solved, never ledgered. Handed their own, the pair passes the guard.
        nint gameDataA = _objects.At(Obj(A)).GameData;
        nint gameDataB = _objects.At(Obj(B)).GameData;

        Assert.Equal(1, _solver.ShouldSolvePenetration(Obj(A), Obj(B), gameDataA, gameDataA));
        Assert.Equal(0, _solver.PenetrationPastGuard);

        Assert.Equal(1, _solver.ShouldSolvePenetration(Obj(A), Obj(B), gameDataA, gameDataB));
        Assert.Equal(1, _solver.PenetrationPastGuard);
    }

    [Fact]
    public void The_first_report_flags_both_and_solves()
    {
        Assert.Equal(1, Ask(Obj(A), Obj(B), 0f));
        Assert.Equal(Penetrating, _objects.At(Obj(A)).GameFlags & Penetrating);
        Assert.Equal(Penetrating, _objects.At(Obj(B)).GameFlags & Penetrating);
        Assert.Equal(1, _solver.Penetration.Count);
    }

    [Fact]
    public void The_world_is_a_side_but_is_never_flagged()
    {
        Assert.Equal(1, Ask(Obj(A), OpenSolverWorld.World, 0f));
        Assert.Equal(1, _solver.PenetrationPastGuard);
        Assert.Equal(0u, _objects.At(OpenSolverWorld.World).GameFlags);
        Assert.Equal(Penetrating, _objects.At(Obj(A)).GameFlags);
    }

    [Fact]
    public void Stuck_past_three_seconds_it_is_told_not_to_solve_once_and_slept_next_frame()
    {
        Ask(Obj(A), Obj(B), 0f);
        Assert.Equal(0, Ask(Obj(A), Obj(B), 3.1f));
        Assert.Equal(1, Ask(Obj(A), Obj(B), 3.2f));

        Frame(3.2f);

        Assert.Equal(new[]
        {
            $"Clear {Obj(A):x}", $"Sleep {Obj(A):x}", $"Clear {Obj(B):x}", $"Sleep {Obj(B):x}",
        }, _objects.Calls.Where(c => !c.StartsWith("SetGameFlags")));
    }

    [Fact]
    public void An_object_the_solver_does_not_move_is_not_slept()
    {
        _objects.At(Obj(B)).Moveable = false;
        Ask(Obj(A), Obj(B), 0f);
        Ask(Obj(A), Obj(B), 3.1f);

        Frame(3.1f);

        Assert.Equal(new[] { $"Clear {Obj(A):x}", $"Sleep {Obj(A):x}" },
                     _objects.Calls.Where(c => !c.StartsWith("SetGameFlags")));
    }

    [Fact]
    public void A_player_or_a_shadow_is_never_given_up_on()
    {
        _world.Facts[A] = new SolverEntity(0, 6, 0, 2, true, EntityHandle.None, EntityHandle.None);
        Ask(Obj(A), Obj(B), 0f);
        Assert.Equal(1, Ask(Obj(A), Obj(B), 3.1f));

        var c = new EntityHandle(0x0001_0012u);
        _objects.Owned(Obj(c), c).Shadow = true;
        Ask(Obj(c), Obj(B), 0f);
        Assert.Equal(1, Ask(Obj(c), Obj(B), 3.1f));
    }

    [Fact]
    public void Quiet_for_a_second_the_pair_is_dropped_and_both_flags_cleared()
    {
        Ask(Obj(A), Obj(B), 0f);
        Frame(0.5f);
        Assert.Equal(1, _solver.Penetration.Count);

        Frame(1.1f);

        Assert.Equal(0, _solver.Penetration.Count);
        Assert.Equal(0u, _objects.At(Obj(A)).GameFlags & Penetrating);
        Assert.Equal(0u, _objects.At(Obj(B)).GameFlags & Penetrating);
    }

    [Fact]
    public void Debris_stuck_in_a_moving_object_goes_to_the_entity_solver()
    {
        _world.Facts[A] = new SolverEntity((int)EngineCollisionGroup.Debris, 6, 0, 6, false, EntityHandle.None, EntityHandle.None);
        Ask(Obj(A), Obj(B), 0f);
        Ask(Obj(A), Obj(B), 0.6f);

        Frame(0.6f);

        var ev = Assert.Single(_posted);
        Assert.Equal(PhysicsEventKind.EntitySolver, ev.Kind);
        Assert.Equal(A.Value, ev.Entity0);
        Assert.Equal(B.Value, ev.Entity1);
        Assert.Equal(PhysicsEvent.SolverDebris0, ev.Flags);
        Assert.Equal(0, _solver.Penetration.Count);
        Assert.Equal(0u, _objects.At(Obj(A)).GameFlags & Penetrating);
    }

    [Fact]
    public void Debris_stuck_in_a_static_object_is_not_the_solvers()
    {
        _world.Facts[A] = new SolverEntity((int)EngineCollisionGroup.Debris, 6, 0, 6, false, EntityHandle.None, EntityHandle.None);
        _objects.At(Obj(B)).Static = true;
        Ask(Obj(A), Obj(B), 0f);
        Ask(Obj(A), Obj(B), 0.6f);

        Frame(0.6f);

        Assert.Empty(_posted);
    }

    [Fact]
    public void A_gone_entity_drops_its_pair_without_a_sleep()
    {
        Ask(Obj(A), Obj(B), 0f);
        Ask(Obj(A), Obj(B), 3.1f);
        _world.Gone.Add(B);

        Frame(3.1f);

        Assert.Equal(0, _solver.Penetration.Count);
        Assert.DoesNotContain(_objects.Calls, c => c.StartsWith("Sleep"));
    }

    [Fact]
    public void Between_pre_and_post_entity_init_every_penetration_is_left_to_the_solver()
    {
        var lifecycle = (IGameLifecycle)_solver;
        lifecycle.OnLevelInitPreEntity("dm_test");

        Assert.Equal(1, Ask(Obj(A), Obj(B), 0f));
        Assert.Equal(1, _solver.PenetrationPaused);
        Assert.Equal(0, _solver.Penetration.Count);

        lifecycle.OnLevelInitPostEntity("dm_test");
        Ask(Obj(A), Obj(B), 0f);
        Assert.Equal(1, _solver.Penetration.Count);
    }

    [Fact]
    public void One_entity_or_an_unowned_object_never_reaches_the_rules()
    {
        const nint second = 0x2010;
        _objects.Owned(second, A);
        Assert.Equal(1, Ask(Obj(A), second, 0f));
        Assert.Equal(1, Ask(0x0777, Obj(A), 0f));

        Assert.Equal(2, _solver.PenetrationAsks);
        Assert.Equal(0, _solver.PenetrationPastGuard);
    }

    [Fact]
    public void A_player_penetrated_by_a_vphysics_mover_says_so()
    {
        var player = new EntityHandle(0x0001_0013u);
        _world.Facts[player] = new SolverEntity(0, 2, 0, 2, true, EntityHandle.None, EntityHandle.None);
        _objects.Owned(Obj(player), player);

        Assert.False(_solver.PenetratedByMover(player));
        Ask(Obj(player), Obj(A), 0f);
        Assert.True(_solver.PenetratedByMover(player));

        // A partner that is not MOVETYPE_VPHYSICS does not count.
        _world.Facts[A] = new SolverEntity(0, 6, 0, 0, false, EntityHandle.None, EntityHandle.None);
        Assert.False(_solver.PenetratedByMover(player));
    }

    [Fact]
    public void A_level_shutdown_forgets_the_ledger()
    {
        Ask(Obj(A), Obj(B), 0f);
        ((IGameLifecycle)_solver).OnLevelShutdown();
        Assert.Equal(0, _solver.Penetration.Count);
    }
}
