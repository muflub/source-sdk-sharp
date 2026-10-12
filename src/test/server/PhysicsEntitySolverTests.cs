using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A9: stock's CPhysicsEntitySolver (physics_npc_solver.cpp:
/// 365-467) for debris the penetration ledger handed over.
/// </summary>
public class PhysicsEntitySolverTests
{
    private static readonly EntityHandle Crate = new(0x0001_0010);
    private static readonly EntityHandle Plank = new(0x0001_0020);

    private sealed class World : IEntitySolverWorld
    {
        public readonly HashSet<(uint, uint)> Disabled = new();
        public readonly Dictionary<EntityHandle, CollisionGroup> Groups = new();
        public readonly List<EntityHandle> Rechecked = new();
        public readonly HashSet<EntityHandle> Gone = new();
        public readonly List<string> Calls = new();

        private static (uint, uint) Key(EntityHandle a, EntityHandle b)
            => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

        public bool PairDisabled(EntityHandle a, EntityHandle b) => Disabled.Contains(Key(a, b));

        public void SetPairEnabled(EntityHandle a, EntityHandle b, bool enabled)
        {
            Calls.Add(enabled ? "enable" : "disable");
            if (enabled) Disabled.Remove(Key(a, b)); else Disabled.Add(Key(a, b));
        }

        public CollisionGroup GetGroup(EntityHandle entity)
            => Groups.TryGetValue(entity, out var g) ? g : throw new InvalidOperationException("no group for " + entity);

        public void SetGroup(EntityHandle entity, CollisionGroup group)
        {
            Calls.Add($"group {entity.Value:x} {group}");
            Groups[entity] = group;
        }

        public void RecheckContactPoints(EntityHandle entity)
        {
            Calls.Add("recheck");
            Rechecked.Add(entity);
        }

        public bool Exists(EntityHandle entity) => !Gone.Contains(entity);
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private static (PhysicsEntitySolver, World) Make() => Make(new Clock());

    private static (PhysicsEntitySolver, World) Make(Clock clock)
    {
        var world = new World();
        world.Groups[Crate] = CollisionGroup.None;
        world.Groups[Plank] = CollisionGroup.Debris;
        return (new PhysicsEntitySolver(world, clock), world);
    }

    /// <summary>The unload and the load: the old build's OnSave, a fresh solver's OnRestore.</summary>
    private static PhysicsEntitySolver Reloaded(PhysicsEntitySolver before, World world, Clock clock, float restoreAt)
    {
        var record = new ReloadRecord("SourceSharp.Server.PhysicsEntitySolver");
        Assert.IsAssignableFrom<ISaveCustom>(before).OnSave(new SaveRecordAccess(record, record.Owner, new RecordingLog()));

        clock.Now = restoreAt;
        var after = new PhysicsEntitySolver(world, clock);
        var engine = TestServices.Fake();
        Assert.IsAssignableFrom<ISaveCustom>(after).OnRestore(new SaveRecordAccess(record, record.Owner, new RecordingLog(),
                                                                                 new SavedHandles(engine, engine)));
        return after;
    }

    [Fact]
    public void ASolverDisablesThePairAndMakesTheDebrisDebrisWithItsContactsRechecked()
    {
        var (solver, world) = Make();

        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);

        Assert.True(world.PairDisabled(Crate, Plank));
        Assert.Equal(CollisionGroup.Debris, world.Groups[Plank]);
        Assert.Equal(new[] { Plank }, world.Rechecked);
        Assert.Equal(CollisionGroup.None, world.Groups[Crate]);   // the mover is untouched
        Assert.Equal(1, solver.Active);
    }

    [Fact]
    public void TheBlockerIsTheDebrisSideWhicheverOrderTheLedgerGaveThePair()
    {
        // Stock swaps so the second entity is the debris (UpdatePenetrateEvents).
        var (solver, world) = Make();

        solver.Solve(Plank, Crate, PhysicsEvent.SolverDebris0, now: 10f);

        Assert.Equal(new[] { Plank }, world.Rechecked);
    }

    [Fact]
    public void AnAlreadyDisabledPairGetsNoSolver()
    {
        // EntityPhysics_CreateSolver returns NULL when PhysEntityCollisionsAreDisabled.
        var (solver, world) = Make();
        world.Disabled.Add((Crate.Value, Plank.Value));

        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        solver.Solve(Plank, Crate, PhysicsEvent.SolverDebris0, now: 10f);   // either order

        Assert.Empty(world.Calls);
        Assert.Equal(0, solver.Active);
    }

    [Fact]
    public void BeforeASecondHasPassedNothingIsUndone()
    {
        var (solver, world) = Make();
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);

        solver.Update(now: 10.9f);

        Assert.True(world.PairDisabled(Crate, Plank));
        Assert.Equal(1, solver.Active);
    }

    [Fact]
    public void AfterASecondThePairCollidesAgainAndTheDebrisGetsItsGroupBack()
    {
        var (solver, world) = Make();
        world.Groups[Plank] = CollisionGroup.Interactive;   // a group the solver must restore, not Debris
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        Assert.Equal(CollisionGroup.Debris, world.Groups[Plank]);   // the control: it was changed

        solver.Update(now: 11.01f);

        Assert.False(world.PairDisabled(Crate, Plank));
        Assert.Equal(CollisionGroup.Interactive, world.Groups[Plank]);
        Assert.Equal(0, solver.Active);
    }

    [Fact]
    public void AGoneMoverStillGivesTheDebrisItsGroupBack()
    {
        // UpdateOnRemove: re-enable only when both exist; restore the blocker's group if it does.
        var (solver, world) = Make();
        world.Groups[Plank] = CollisionGroup.Interactive;
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        Assert.Equal(CollisionGroup.Debris, world.Groups[Plank]);   // the control: it was changed
        world.Gone.Add(Crate);
        world.Calls.Clear();

        solver.Update(now: 11.01f);

        Assert.DoesNotContain("enable", world.Calls);
        Assert.Equal(CollisionGroup.Interactive, world.Groups[Plank]);
    }

    [Fact]
    public void AGoneDebrisIsLeftAlone()
    {
        var (solver, world) = Make();
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        Assert.NotEmpty(world.Calls);   // the control: the solver acted
        world.Gone.Add(Plank);
        world.Calls.Clear();

        solver.Update(now: 11.01f);

        Assert.Empty(world.Calls);
        Assert.Equal(0, solver.Active);
    }

    [Fact]
    public void ALevelChangeDropsEverySolver()
    {
        var (solver, world) = Make();
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        Assert.Equal(1, solver.Active);   // the control

        solver.Clear();

        Assert.Equal(0, solver.Active);
    }

    [Fact]
    public void ASolverCrossesAReloadAndStillGivesThePairAndTheGroupBack()
    {
        // physics_npc_solver.cpp:400-406: every field is DATADESC. The native
        // pair hash and the group outlive the module, so an emptied list would
        // leave the pair disabled and the plank Debris for the rest of the level.
        var clock = new Clock { Now = 10f };
        var (solver, world) = Make(clock);
        world.Groups[Plank] = CollisionGroup.Interactive;
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        Assert.Equal(CollisionGroup.Debris, world.Groups[Plank]);   // the control: it was changed

        clock.Now = 10.4f;
        var after = Reloaded(solver, world, clock, restoreAt: 10.4f);
        Assert.Equal(1, after.Active);

        after.Update(now: 11.01f);

        Assert.False(world.PairDisabled(Crate, Plank));
        Assert.Equal(CollisionGroup.Interactive, world.Groups[Plank]);
        Assert.Equal(0, after.Active);
    }

    [Fact]
    public void AReloadedSolverKeepsTheSecondsItHadLeftNotTheClockTimeItWasDue()
    {
        // m_cancelTime is FIELD_TIME: stored relative, so a restore whose clock
        // reads differently still waits out what was left (0.6 s here).
        var clock = new Clock { Now = 10f };
        var (solver, world) = Make(clock);
        solver.Solve(Crate, Plank, PhysicsEvent.SolverDebris1, now: 10f);
        clock.Now = 10.4f;

        var after = Reloaded(solver, world, clock, restoreAt: 50f);

        after.Update(now: 50.5f);
        Assert.True(world.PairDisabled(Crate, Plank));
        Assert.Equal(1, after.Active);

        after.Update(now: 50.61f);
        Assert.False(world.PairDisabled(Crate, Plank));
        Assert.Equal(0, after.Active);
    }
}
