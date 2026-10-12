using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_bones_native_zero 2.1: managed's IPhysicsCollisionSolver
/// answers. ShouldCollide's facts are hand-derived from native's
/// CCollisionEvent::ShouldCollide_2 (physics.cpp at dcae1517d) and stock's
/// (game/server/physics.cpp:480-560): who owns each object, the world by
/// identity, the owner and hierarchy rules, the wheel rule and the pair set.
/// </summary>
public class PhysicsSolverTests
{
    private readonly OpenSolverWorld _world = new();
    private readonly FakeSolverObjects _objects = new();
    private readonly PhysicsSolver _solver;

    private static readonly EntityHandle A = new(0x0001_0010u);
    private static readonly EntityHandle B = new(0x0001_0011u);
    private static readonly EntityHandle C = new(0x0001_0012u);

    private static nint Obj(EntityHandle e) => OpenSolverWorld.ObjectOf(e);

    public PhysicsSolverTests()
    {
        _solver = new PhysicsSolver(_world, _objects, new PhysicsSolverRules(),
                                    new EntityPairCollisions(_world, _objects), new PhysicsPenetrationLedger(),
                                    new SolverClock(), _ => { }, new RecordingCrushes(), () => 0, _ => { });
        _objects.Owned(Obj(A), A);
        _objects.Owned(Obj(B), B);
        _objects.Owned(Obj(C), C);
        _objects.At(OpenSolverWorld.World).Static = true;
        _objects.At(OpenSolverWorld.World).Moveable = false;
    }

    private static SolverEntity Facts(int group = 0, int solid = 6, int solidFlags = 0, int moveType = 6,
                                      bool player = false, EntityHandle owner = default, EntityHandle parent = default)
        => new(group, solid, solidFlags, moveType, player, owner, parent);

    private const nint Unowned = 0x0777;

    [Fact]
    public void Decide_answers_as_the_ask_and_moves_no_counter()
    {
        // The cost bench times Decide: it must be the ask's answer, and leave
        // the ledger a live check reads alone.
        _world.Facts[A] = Facts(solidFlags: PhysicsSolverRules.NotSolidFlag);

        Assert.Equal(0, _solver.Decide(OpenSolverWorld.World, Obj(A), out CollisionRefusal why));
        Assert.Equal(CollisionRefusal.NotSolid, why);
        Assert.Equal(0, _solver.CollideAsks + _solver.CollideRefusals + _solver.Refusals(CollisionRefusal.NotSolid));

        Assert.Equal(0, _solver.ShouldCollide(OpenSolverWorld.World, Obj(A)));
        Assert.Equal(1, _solver.CollideAsks);
    }

    [Fact]
    public void An_unowned_object_collides_with_anything()
    {
        _world.Facts[A] = Facts(solidFlags: PhysicsSolverRules.NotSolidFlag);
        Assert.Equal(1, _solver.ShouldCollide(Unowned, Obj(A)));
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Unowned));
    }

    [Fact]
    public void An_unowned_object_is_answered_before_anything_is_read_of_either_side()
    {
        // Stock returns 1 on a missing entity before it reads a game flag
        // (physics.cpp:470). Handed both game data words, the ask reads no
        // object but the NULL side's IsStatic: a static object with no game
        // data is a static prop, which stock owns by the world (C8), so that
        // one read is the ownership question itself.
        nint gameDataA = _objects.At(Obj(A)).GameData;
        int before = _objects.Reads;

        Assert.Equal(1, _solver.ShouldCollide(Unowned, Obj(A), 0, gameDataA));
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Unowned, gameDataA, 0));

        Assert.Equal(2, _objects.Reads - before);
        Assert.Equal(2, _solver.CollideAsks);
    }

    [Fact]
    public void An_owned_ask_allocates_nothing()
    {
        // vphysics asks mid-step, several times a frame (plan_physics_bones_perf).
        _world.Facts[A] = Facts(parent: C);
        nint gameDataA = _objects.At(Obj(A)).GameData;
        nint gameDataB = _objects.At(Obj(B)).GameData;
        _solver.ShouldCollide(Obj(A), Obj(B), gameDataA, gameDataB);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int answer = _solver.ShouldCollide(Obj(A), Obj(B), gameDataA, gameDataB);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal((1, 0L), (answer, allocated));
    }

    [Fact]
    public void An_owned_pair_reads_its_objects()
    {
        // The positive arm of the fact above: the counter sees a full ask.
        nint gameDataA = _objects.At(Obj(A)).GameData;
        nint gameDataB = _objects.At(Obj(B)).GameData;
        int before = _objects.Reads;

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(B), gameDataA, gameDataB));
        Assert.True(_objects.Reads - before > 0, "an owned pair reads its objects");
    }

    [Fact]
    public void The_owner_is_the_game_data_vphysics_hands_with_the_ask()
    {
        // B's object, handed with A's game data: one entity with A's object, so
        // A's no-self-collision flag refuses. Read from the object instead, B
        // would own it and the pair would collide.
        _objects.At(Obj(A)).GameFlags = PhysicsSolverRules.NoSelfCollisionGameFlags;
        nint gameDataA = _objects.At(Obj(A)).GameData;
        nint gameDataB = _objects.At(Obj(B)).GameData;

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(B), gameDataA, gameDataB));
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), Obj(B), gameDataA, gameDataA));
        Assert.Equal(1, _solver.Refusals(CollisionRefusal.SelfNoCollide));
    }

    [Fact]
    public void The_world_is_owned_and_a_not_solid_entity_falls_through_it()
    {
        // The world's game data is NULL; it is known by identity, so it is an
        // owned side and the not-solid rule applies, where an unowned object
        // would have collided (a dropped weapon resting on the floor).
        _world.Facts[A] = Facts(solidFlags: PhysicsSolverRules.NotSolidFlag);
        Assert.Equal(0, _solver.ShouldCollide(OpenSolverWorld.World, Obj(A)));
        Assert.Equal(1, _solver.Refusals(CollisionRefusal.NotSolid));
    }

    // ---- static props are the world's (stock's PhysCreateWorld hands pWorld
    // ---- to CreateVPhysicsRepresentations, physics.cpp:422) ----------------

    private const nint StaticProp = 0x0888;

    private void GiveStaticProp()
    {
        // The engine's static prop manager made it with NULL game data (ours),
        // where stock's carries the world entity.
        _objects.At(StaticProp).Static = true;
        _objects.At(StaticProp).Moveable = false;
    }

    [Fact]
    public void A_not_solid_entity_falls_through_a_static_prop_as_through_the_world()
    {
        GiveStaticProp();
        _world.Facts[A] = Facts(solidFlags: PhysicsSolverRules.NotSolidFlag);

        Assert.Equal(0, _solver.ShouldCollide(StaticProp, Obj(A)));
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), StaticProp));
        Assert.Equal(0, _solver.CollideUnowned);
    }

    [Fact]
    public void A_push_mover_does_not_collide_with_a_static_prop()
    {
        // AI movers don't collide with the world/static/pinned objects.
        GiveStaticProp();
        _world.Facts[A] = Facts(moveType: 7);

        Assert.Equal(0, _solver.ShouldCollide(Obj(A), StaticProp));
    }

    [Fact]
    public void A_crate_still_rests_on_a_static_prop()
    {
        GiveStaticProp();
        _world.Facts[A] = Facts();

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), StaticProp));
    }

    [Fact]
    public void A_null_owner_that_is_not_static_stays_unowned()
    {
        // Only a static object with no game data is the world's: anything
        // else unowned collides with anything, as stock's NULL entity does.
        _world.Facts[A] = Facts(solidFlags: PhysicsSolverRules.NotSolidFlag);
        Assert.Equal(1, _solver.ShouldCollide(Unowned, Obj(A)));
        Assert.Equal(1, _solver.CollideUnowned);
    }

    [Fact]
    public void A_trigger_admits_the_world_as_a_vphysics_solid()
    {
        _world.Facts[A] = Facts(solidFlags: PhysicsSolverRules.TriggerFlag, moveType: 0, solid: 2);
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), OpenSolverWorld.World));
    }

    [Fact]
    public void Two_objects_of_one_entity_collide_unless_it_says_not()
    {
        const nint second = 0x2010;
        _objects.Owned(second, A);
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), second));

        _objects.At(second).GameFlags = PhysicsSolverRules.NoSelfCollisionGameFlags;
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), second));
        Assert.Equal(1, _solver.Refusals(CollisionRefusal.SelfNoCollide));
    }

    [Fact]
    public void An_owner_and_what_it_owns_do_not_collide()
    {
        _world.Facts[B] = Facts(owner: A);
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), Obj(B)));
        Assert.Equal(0, _solver.ShouldCollide(Obj(B), Obj(A)));
        Assert.Equal(2, _solver.Refusals(CollisionRefusal.Owner));
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(C)));
    }

    [Fact]
    public void The_owner_rule_needs_both_to_have_edicts()
    {
        // Stock: entindex() >= 0 on both. An entry in the engine-invisible
        // range has no edict.
        var invisible = new EntityHandle(0x0001_1FF0u);
        Assert.True(invisible.IsInvisibleRange);
        _objects.Owned(Obj(invisible), invisible);
        _world.Facts[invisible] = Facts(owner: A);

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(invisible)));
    }

    [Fact]
    public void One_hierarchy_does_not_collide_with_itself()
    {
        // A and B both hang (B through C) from C's root.
        _world.Facts[A] = Facts(parent: C);
        _world.Facts[B] = Facts(parent: A);
        Assert.Equal(0, _solver.ShouldCollide(Obj(B), Obj(C)));
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), Obj(B)));
        Assert.Equal(2, _solver.Refusals(CollisionRefusal.SameRoot));
    }

    [Fact]
    public void A_parent_that_is_gone_ends_the_chain_where_it_is()
    {
        _world.Facts[A] = Facts(parent: C);
        _world.Facts[B] = Facts(parent: C);
        _world.Gone.Add(C);

        // Each is then its own root: different roots, so they collide.
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(B)));
    }

    [Fact]
    public void A_disabled_pair_is_refused_last_and_the_world_can_be_one_end()
    {
        _solver.Pairs.SetEnabled(A, B, false);
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), Obj(B)));
        Assert.Equal(0, _solver.ShouldCollide(Obj(B), Obj(A)));
        Assert.Equal(2, _solver.Refusals(CollisionRefusal.PairInHash));

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), OpenSolverWorld.World));
        _solver.Pairs.SetEnabled(A, EntityHandle.None, false);
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), OpenSolverWorld.World));
    }

    [Fact]
    public void A_players_solid_mask_takes_the_player_clips()
    {
        // MASK_PLAYERSOLID has CONTENTS_PLAYERCLIP; MASK_SOLID does not.
        const uint playerClip = 0x10000;
        _objects.At(Obj(C)).Contents = playerClip;
        _world.Facts[A] = Facts(player: true, moveType: 2);

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(C)));
        Assert.Equal(0, _solver.ShouldCollide(Obj(B), Obj(C)));
        Assert.Equal(1, _solver.Refusals(CollisionRefusal.Contents));
    }

    [Fact]
    public void A_vehicle_wheel_does_not_collide_with_interactive_debris_but_does_with_the_world()
    {
        _objects.At(Obj(A)).CallbackFlags = PhysicsSolver.CallbackIsVehicleWheel;
        // Interactive debris (3) against a debris-friendly group, so only the
        // wheel rule can refuse.
        _world.Facts[B] = Facts(group: PhysicsSolver.GroupInteractiveDebris);

        Assert.Equal(0, _solver.ShouldCollide(Obj(A), Obj(B)));
        Assert.Equal(0, _solver.ShouldCollide(Obj(B), Obj(A)));
        Assert.Equal(2, _solver.Refusals(CollisionRefusal.Wheel));

        // The world is static: a wheel hits it.
        Assert.Equal(1, _solver.ShouldCollide(Obj(A), OpenSolverWorld.World));
    }

    /// <summary>
    /// CALLBACK_IS_VEHICLE_WHEEL is 0x0040 in vphysics_interface.h; 0x0020 is
    /// CALLBACK_GLOBAL_COLLIDE_STATIC, which vphysics sets on every new object.
    /// Read from the header, so the flag cannot drift from vphysics' own.
    /// </summary>
    [RepoSourceFact("src/public/vphysics_interface.h")]
    public void The_wheel_flag_is_the_headers_callback_is_vehicle_wheel()
    {
        string text = File.ReadAllText(RepoSourceFactAttribute.Find("src/public/vphysics_interface.h")!);
        var m = System.Text.RegularExpressions.Regex.Match(text, @"CALLBACK_IS_VEHICLE_WHEEL\s*=\s*0x([0-9a-fA-F]+)");
        Assert.True(m.Success, "vphysics_interface.h no longer declares CALLBACK_IS_VEHICLE_WHEEL");
        Assert.Equal((int)PhysicsSolver.CallbackIsVehicleWheel, Convert.ToInt32(m.Groups[1].Value, 16));
    }

    [Fact]
    public void An_object_with_vphysics_default_callback_flags_is_not_a_wheel()
    {
        // vphysics' defaults for a new object: GLOBAL_COLLISION | GLOBAL_FRICTION
        // | GLOBAL_TOUCH | GLOBAL_COLLIDE_STATIC | FLUID_TOUCH | DO_FLUID_SIMULATION.
        // Not a wheel, so the wheel rule cannot refuse it against interactive
        // debris (stock collides the pair: the group matrix admits it).
        const ushort defaults = 0x0001 | 0x0002 | 0x0004 | 0x0020 | 0x0100 | 0x1000;
        _objects.At(Obj(A)).CallbackFlags = defaults;
        _world.Facts[B] = Facts(group: PhysicsSolver.GroupInteractiveDebris);

        Assert.Equal(1, _solver.ShouldCollide(Obj(A), Obj(B)));
        Assert.Equal(0, _solver.Refusals(CollisionRefusal.Wheel));
    }

    [Fact]
    public void The_move_type_comes_from_the_entity()
    {
        // MOVETYPE_PUSH against something the solver will not move.
        _world.Facts[A] = Facts(moveType: PhysicsSolverRules.MoveTypePush);
        _objects.At(Obj(B)).Moveable = false;
        Assert.Equal(0, _solver.ShouldCollide(Obj(A), Obj(B)));
        Assert.Equal(1, _solver.Refusals(CollisionRefusal.AiMoverImmovable));
    }

    [Fact]
    public void Every_ask_is_counted_and_every_refusal_by_its_clause()
    {
        _world.Facts[B] = Facts(owner: A);
        _solver.ShouldCollide(Obj(A), Obj(B));
        _solver.ShouldCollide(Obj(A), Obj(C));
        _solver.ShouldCollide(Unowned, Obj(C));

        Assert.Equal(3, _solver.CollideAsks);
        Assert.Equal(1, _solver.CollideRefusals);
        Assert.Equal(1, _solver.Refusals(CollisionRefusal.Owner));
    }

    [Fact]
    public void The_answer_is_symmetric()
    {
        // Native's _DEBUG build asks both ways round and asserts they agree.
        _world.Facts[A] = Facts(group: 1);
        _world.Facts[B] = Facts(group: 1);
        _world.Facts[C] = Facts(solidFlags: PhysicsSolverRules.TriggerFlag, solid: 2, moveType: 0);
        nint[] all = [Obj(A), Obj(B), Obj(C), OpenSolverWorld.World, Unowned];
        foreach (nint x in all)
            foreach (nint y in all)
                Assert.Equal(_solver.ShouldCollide(x, y), _solver.ShouldCollide(y, x));
    }
}

/// <summary>
/// The entity pairs the solver refuses, owned by managed (stock's
/// g_EntityCollisionHash; native's SourceSharp_EntitySetPairCollisions and
/// EntityPairCollisionsDisabled at dcae1517d).
/// </summary>
public class EntityPairCollisionsTests
{
    private readonly OpenSolverWorld _world = new();
    private readonly FakeSolverObjects _objects = new();
    private readonly EntityPairCollisions _pairs;

    private static readonly EntityHandle A = new(0x0001_0010u);
    private static readonly EntityHandle B = new(0x0001_0011u);

    public EntityPairCollisionsTests() => _pairs = new EntityPairCollisions(_world, _objects);

    [Fact]
    public void A_disabled_pair_is_disabled_either_way_round_until_enabled()
    {
        Assert.False(_pairs.AreDisabled(A, B));
        Assert.True(_pairs.SetEnabled(A, B, false));
        Assert.True(_pairs.AreDisabled(A, B));
        Assert.True(_pairs.AreDisabled(B, A));

        Assert.True(_pairs.SetEnabled(B, A, true));
        Assert.False(_pairs.AreDisabled(A, B));
    }

    [Fact]
    public void Both_objects_re_ask_the_filter_unless_static()
    {
        _objects.At(OpenSolverWorld.World).Static = true;
        _pairs.SetEnabled(A, B, false);
        _pairs.SetEnabled(A, EntityHandle.None, false);

        Assert.Equal(new[]
        {
            $"Recheck {OpenSolverWorld.ObjectOf(A):x}", $"Recheck {OpenSolverWorld.ObjectOf(B):x}",
            $"Recheck {OpenSolverWorld.ObjectOf(A):x}",
        }, _objects.Calls);
    }

    [Fact]
    public void A_handle_that_names_nothing_is_the_world()
    {
        var gone = new EntityHandle(0x0002_0020u);
        _world.Gone.Add(gone);
        Assert.True(_pairs.SetEnabled(A, gone, false));
        Assert.True(_pairs.AreDisabled(A, EntityHandle.None));
        Assert.True(_pairs.IsDisabled(A.Value, EntityPairCollisions.WorldKey));
    }

    [Fact]
    public void One_object_with_itself_is_no_pair()
    {
        Assert.False(_pairs.SetEnabled(A, A, false));
        Assert.False(_pairs.SetEnabled(EntityHandle.None, EntityHandle.None, false));
        Assert.Equal(0, _pairs.Count);
    }

    [Fact]
    public void A_level_shutdown_forgets_every_pair()
    {
        var solver = new PhysicsSolver(_world, _objects, new PhysicsSolverRules(), _pairs,
                                       new PhysicsPenetrationLedger(), new SolverClock(), _ => { }, new RecordingCrushes(), () => 0, _ => { });
        _pairs.SetEnabled(A, B, false);

        ((IGameLifecycle)solver).OnLevelShutdown();

        Assert.False(_pairs.AreDisabled(A, B));
    }
}
