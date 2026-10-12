using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_bones_native_zero 2.1: stock's CCollisionEvent::ShouldCollide
/// rules (game/server/physics.cpp), managed. The cases are physics_shell_test's
/// rule-table facts (PhysicsShellFixture), carried over clause by clause.
/// </summary>
public class PhysicsSolverRulesTests
{
    private readonly PhysicsSolverRules _rules = new();

    // physics_shell_test's PlainSide: an owned, solid, moveable vphysics object
    // that every rule admits.
    private static CollisionSide Plain() => new()
    {
        Valid = true,
        CollisionGroup = 0,
        Solid = 6,
        SolidFlags = 0,
        MoveType = 6,
        SolidMask = 0xFFFFFFFFu,
        Contents = 0xFFFFFFFFu,
        Moveable = true,
    };

    private int Eval(CollisionSide a, CollisionSide b, CollisionPairFacts f = default)
        => _rules.Evaluate(a, b, f, out _);

    private CollisionRefusal Why(CollisionSide a, CollisionSide b, CollisionPairFacts f = default)
    {
        _rules.Evaluate(a, b, f, out CollisionRefusal why);
        return why;
    }

    [Fact]
    public void The_default_group_matrix_is_the_generated_stock_one()
    {
        // physics_shell.h's s_nStockGroupRows, which collision_rules_test pins
        // to stock's predicate over all four hundred pairs.
        uint[] stock =
        [
            0x000FABFFu, 0x00020001u, 0x00000001u, 0x000EAAC1u, 0x000EABE1u,
            0x000C23F1u, 0x000EABB9u, 0x000EB3F9u, 0x000CABF1u, 0x000EE3F9u,
            0x00000000u, 0x000E8959u, 0x00000080u, 0x000E83F9u, 0x000E8200u,
            0x000EEBD9u, 0x00000001u, 0x000EEADBu, 0x000EEBF9u, 0x000EEBF9u,
        ];

        for (int g0 = 0; g0 < 20; g0++)
            for (int g1 = 0; g1 < 20; g1++)
                Assert.True(((stock[g0] >> g1) & 1) == 1 == _rules.GroupsCollide(g0, g1), $"group {g0} vs {g1}");
    }

    /// <summary>
    /// physics_shell.h's SourceSharpPhysicsShell_PinRuleLiteralsToEngineEnums,
    /// carried over with the rules (plan_physics_bones_native_zero 2.3): the
    /// literals the clauses test are the engine's own values, held through the
    /// managed enums CollisionGroupParityTests and the movement facts pin to
    /// the headers. A MoveType shifting by one would make "AI movers" a rule
    /// about MOVETYPE_NOCLIP.
    /// </summary>
    [Fact]
    public void The_rule_literals_are_the_engines_own_values()
    {
        Assert.Equal(PhysicsSolverRules.NotSolidFlag, (int)SolidFlags.NotSolid);
        Assert.Equal(PhysicsSolverRules.TriggerFlag, (int)SolidFlags.Trigger);
        Assert.Equal(PhysicsSolverRules.SolidNone, (int)SolidType.None);
        Assert.Equal(PhysicsSolverRules.SolidBsp, (int)SolidType.Bsp);
        Assert.Equal(PhysicsSolverRules.SolidVPhysics, (int)SolidType.VPhysics);
        Assert.Equal(PhysicsSolverRules.MoveTypeVPhysics, (int)MoveType.VPhysics);
        Assert.Equal(PhysicsSolverRules.MoveTypePush, (int)MoveType.Push);
        Assert.Equal(PhysicsSolverRules.MoveTypeNone, (int)MoveType.None);
    }

    [Fact]
    public void A_group_outside_the_matrix_answers_stocks_fallthrough()
    {
        Assert.True(_rules.GroupsCollide(20, 0));
        Assert.True(_rules.GroupsCollide(0, 20));
        Assert.True(_rules.GroupsCollide(-1, 0));
    }

    [Fact]
    public void An_unowned_pair_collides()
    {
        var a = Plain();
        var b = Plain() with { Valid = false };
        Assert.Equal(1, Eval(a, b));
        Assert.Equal(1, Eval(b, a));
    }

    [Fact]
    public void An_owner_and_what_it_owns_do_not_collide()
    {
        var f = new CollisionPairFacts { OwnerPair = true };
        Assert.Equal(0, Eval(Plain(), Plain(), f));
        Assert.Equal(CollisionRefusal.Owner, Why(Plain(), Plain(), f));
    }

    [Fact]
    public void Forced_collision_comes_before_the_owner_rule()
        => Assert.Equal(1, Eval(Plain(), Plain(), new CollisionPairFacts { OwnerPair = true, ForcedCollision = true }));

    [Fact]
    public void One_hierarchy_does_not_collide_with_itself()
    {
        var f = new CollisionPairFacts { SameRoot = true };
        Assert.Equal(0, Eval(Plain(), Plain(), f));
        Assert.Equal(CollisionRefusal.SameRoot, Why(Plain(), Plain(), f));
    }

    // func_clip_vphysics (A5): not solid, a brush, carrying the force flag.
    private static CollisionSide ClipBrush() => Plain() with
    {
        SolidFlags = 0x0004, MoveType = 0, Moveable = false, GameFlags = 0x1000,
    };

    [Fact]
    public void A_clip_brush_stops_a_moveable_physics_object()
    {
        Assert.Equal(1, Eval(ClipBrush(), Plain()));
        Assert.Equal(1, Eval(Plain(), ClipBrush()));
    }

    [Fact]
    public void A_clip_brush_lets_a_player_through()
    {
        var player = Plain() with { MoveType = 2, Shadow = true };
        Assert.Equal(0, Eval(ClipBrush(), player));
        Assert.Equal(CollisionRefusal.NotSolid, Why(ClipBrush(), player));
    }

    [Fact]
    public void A_clip_brush_lets_a_pinned_object_through()
        => Assert.Equal(0, Eval(ClipBrush(), Plain() with { Moveable = false }));

    [Fact]
    public void One_entitys_two_objects_collide_unless_the_flag_says_not()
    {
        var f = new CollisionPairFacts { SameEntity = true };
        Assert.Equal(1, Eval(Plain(), Plain(), f));
        Assert.Equal(0, Eval(Plain() with { GameFlags = 0x8000 }, Plain(), f));
        Assert.Equal(0, Eval(Plain(), Plain() with { GameFlags = 0x8000 }, f));
        Assert.Equal(CollisionRefusal.SelfNoCollide, Why(Plain(), Plain() with { GameFlags = 0x8000 }, f));
    }

    [Fact]
    public void Two_objects_constrained_to_the_world_do_not_collide()
    {
        Assert.Equal(0, Eval(Plain() with { GameFlags = 0x2 }, Plain() with { GameFlags = 0x2 }));
        Assert.Equal(1, Eval(Plain() with { GameFlags = 0x2 }, Plain()));
    }

    [Fact]
    public void The_static_clause_beats_a_forced_collision()
        => Assert.Equal(0, Eval(Plain() with { GameFlags = 0x2 }, Plain() with { GameFlags = 0x2 },
                                new CollisionPairFacts { ForcedCollision = true }));

    [Fact]
    public void A_forced_collision_beats_everything_after_it()
    {
        var b = Plain() with { SolidFlags = 0x4, Contents = 0 };
        Assert.Equal(0, Eval(Plain(), b));
        Assert.Equal(1, Eval(Plain(), b, new CollisionPairFacts { ForcedCollision = true }));
    }

    [Fact]
    public void A_vehicle_wheel_refusal_is_honoured_before_the_forced_list()
    {
        var f = new CollisionPairFacts { WheelRefused = true, ForcedCollision = true };
        Assert.Equal(0, Eval(Plain(), Plain(), f));
        Assert.Equal(CollisionRefusal.Wheel, Why(Plain(), Plain(), f));
    }

    [Fact]
    public void An_ai_mover_does_not_collide_with_something_that_cannot_move()
    {
        var mover = Plain() with { MoveType = 7 };
        var pinned = Plain() with { Moveable = false };
        Assert.Equal(0, Eval(mover, pinned));
        Assert.Equal(0, Eval(pinned, mover));
        Assert.Equal(CollisionRefusal.AiMoverImmovable, Why(mover, pinned));
        Assert.Equal(1, Eval(mover, Plain()));
    }

    [Fact]
    public void Two_ai_movers_do_not_collide()
    {
        Assert.Equal(0, Eval(Plain() with { MoveType = 7 }, Plain() with { MoveType = 7 }));
        Assert.Equal(CollisionRefusal.AiMoverPair, Why(Plain() with { MoveType = 7 }, Plain() with { MoveType = 7 }));
    }

    [Fact]
    public void Movetype_push_is_the_only_ai_mover()
    {
        var pinned = Plain() with { Moveable = false };
        for (int mt = 0; mt < 12; mt++)
            Assert.True((mt == 7) == (Eval(Plain() with { MoveType = mt }, pinned) == 0), $"movetype {mt}");
    }

    [Fact]
    public void Two_shadow_controlled_objects_do_not_collide()
    {
        Assert.Equal(0, Eval(Plain() with { Shadow = true }, Plain() with { Shadow = true }));
        Assert.Equal(CollisionRefusal.ShadowPair, Why(Plain() with { Shadow = true }, Plain() with { Shadow = true }));
        Assert.Equal(1, Eval(Plain() with { Shadow = true }, Plain()));
    }

    [Fact]
    public void Solid_none_collides_with_nothing()
    {
        Assert.Equal(0, Eval(Plain(), Plain() with { Solid = 0 }));
        Assert.Equal(0, Eval(Plain() with { Solid = 0 }, Plain()));
        Assert.Equal(CollisionRefusal.SolidType, Why(Plain(), Plain() with { Solid = 0 }));
        for (int solid = 1; solid < 7; solid++)
            Assert.Equal(1, Eval(Plain(), Plain() with { Solid = solid }));
    }

    [Fact]
    public void Not_solid_collides_with_nothing()
    {
        Assert.Equal(0, Eval(Plain(), Plain() with { SolidFlags = 0x4 }));
        Assert.Equal(CollisionRefusal.NotSolid, Why(Plain(), Plain() with { SolidFlags = 0x4 }));
    }

    [Fact]
    public void A_vphysics_trigger_still_feels_something_solid()
    {
        var trigger = Plain() with { SolidFlags = 0x4, Trigger = true };
        Assert.Equal(1, Eval(trigger, Plain()));
        Assert.Equal(1, Eval(Plain(), trigger));
        Assert.Equal(0, Eval(trigger, Plain() with { SolidFlags = 0x4 }));
    }

    [Fact]
    public void An_fsolid_trigger_only_admits_vphysics_solids_and_bsp()
    {
        var trig = Plain() with { SolidFlags = 0x8 };
        Assert.Equal(1, Eval(trig, Plain() with { Solid = 6, MoveType = 0 }));
        Assert.Equal(1, Eval(trig, Plain() with { Solid = 1, MoveType = 0 }));
        Assert.Equal(1, Eval(trig, Plain() with { Solid = 2, MoveType = 6 }));
        Assert.Equal(0, Eval(trig, Plain() with { Solid = 2, MoveType = 2 }));
        Assert.Equal(CollisionRefusal.Trigger, Why(Plain() with { Solid = 2, MoveType = 2 }, trig));
    }

    [Fact]
    public void The_group_matrix_refuses_debris_against_debris()
    {
        // COLLISION_GROUP_DEBRIS (1) hits world and static only.
        Assert.Equal(0, Eval(Plain() with { CollisionGroup = 1 }, Plain() with { CollisionGroup = 1 }));
        Assert.Equal(CollisionRefusal.Group, Why(Plain() with { CollisionGroup = 1 }, Plain() with { CollisionGroup = 1 }));
        Assert.Equal(1, Eval(Plain() with { CollisionGroup = 1 }, Plain()));
    }

    [Fact]
    public void Contents_are_checked_against_the_other_sides_solid_mask()
    {
        Assert.Equal(0, Eval(Plain() with { Contents = 0x1 }, Plain() with { SolidMask = 0x2 }));
        Assert.Equal(0, Eval(Plain() with { Contents = 0x2, SolidMask = 0x2 }, Plain() with { Contents = 0x1, SolidMask = 0x2 }));
        Assert.Equal(CollisionRefusal.Contents, Why(Plain() with { Contents = 0x1 }, Plain() with { SolidMask = 0x2 }));
    }

    [Fact]
    public void A_pair_in_the_collision_hash_is_refused_last()
    {
        Assert.Equal(0, Eval(Plain(), Plain(), new CollisionPairFacts { PairInHash = true }));
        Assert.Equal(CollisionRefusal.PairInHash, Why(Plain(), Plain(), new CollisionPairFacts { PairInHash = true }));
        Assert.Equal(1, Eval(Plain(), Plain(), new CollisionPairFacts { PairInHash = true, ForcedCollision = true }));
    }

    [Fact]
    public void An_admitted_pair_answers_allowed()
        => Assert.Equal(CollisionRefusal.Allowed, Why(Plain(), Plain()));
}
