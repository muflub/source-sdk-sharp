using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// ONE NUMBER PER THING THAT CAN HAPPEN TO AN ENTITY (plan_entity.md §3.4).
///
/// <para>
/// The C# half of <c>sourcesharp/game/shared/entity_op.h</c>. Before this the
/// shell reached managed code through thirteen separate ABI slots, one per
/// event, every one of them called unconditionally: every entity paid a
/// crossing for every think and every touch whether or not its class had
/// anything to say. §3.4 replaces them with one <c>Dispatch</c> slot and an
/// override bitmask per class, so a class that does not override an op costs
/// <b>zero</b> crossings for it.
/// </para>
///
/// <para>
/// <b>THE NUMBERS CROSS THE ABI AND ARE FIXED FOREVER.</b> They travel as a
/// bare <c>int32_t</c> in the <c>Dispatch</c> slot and as bit positions in the
/// mask <c>RegisterClassOps</c> hands over — exactly as
/// <see cref="ScriptedEntityKind"/> travels through
/// <c>RegisterScriptedEntity</c>. Renumbering one side turns every touch into a
/// think, and nothing would say so: the class would simply start receiving the
/// wrong callback, which reads as a bug in the C# and is not one. So they are
/// pinned numerically on BOTH sides — <c>EntityOpTests.OpNumbersAreFixed</c>
/// here and <c>EntityOpNumbers</c> in <c>test/entity_op_test.cpp</c> — and the
/// C++ test additionally reads this file and compares name for name, because
/// two independent lists of the same literals are two things that can drift
/// apart while both stay green.
/// </para>
///
/// <para>
/// <b>Direction.</b> Everything except <see cref="PositionChanged"/> is
/// INBOUND: the engine did something and managed code is being told.
/// <see cref="PositionChanged"/> is outbound — managed moved the entity and
/// native has partition, hierarchy and trigger work to do about it
/// (<c>.claude/entity-port/abi-requests/1b.md</c> §2). One vocabulary rather
/// than two: "what happened" is the same question whoever asks it.
/// </para>
/// </summary>
public enum EntityOp
{
    /// <summary>In the world and configured. CREATES the managed instance.</summary>
    Spawn = 0,

    /// <summary>A key from the map, delivered before the spawn. Also creates the instance.</summary>
    KeyValue = 1,

    /// <summary>An input fired at it, by name.</summary>
    Input = 2,

    /// <summary>Its think time came round.</summary>
    Think = 3,

    /// <summary>It is going. Drops the registry's row.</summary>
    Removed = 4,

    /// <summary>Something arrived in it.</summary>
    StartTouch = 5,

    /// <summary>Something is still in it, this tick.</summary>
    Touch = 6,

    /// <summary>Something left it.</summary>
    EndTouch = 7,

    /// <summary>It hit something, which for a projectile is the only event that matters.</summary>
    Impact = 8,

    /// <summary>A mover arrived where it was sent.</summary>
    MoveDone = 9,

    /// <summary>A mover met something it could not push.</summary>
    Blocked = 10,

    /// <summary>Damage. The answer is how much actually landed.</summary>
    Damage = 11,

    /// <summary>Its health reached zero.</summary>
    Killed = 12,

    /// <summary>Whether a managed filter admits a subject. Negative means "not a filter".</summary>
    FilterPasses = 13,

    /// <summary>
    /// OUTBOUND. Managed moved the entity; native moves it in the spatial
    /// partition, marks the two block offsets, carries the children and touches
    /// the triggers it is now in. Carries the PREVIOUS origin, because the
    /// trigger half is a question about where it was as well as where it is.
    /// </summary>
    PositionChanged = 14,

    /// <summary>
    /// The shell just created the entity's physics object, before the solver
    /// has simulated it (physics stock parity B4). What stock's CreateVPhysics
    /// decides at creation, such as starting asleep, is managed's to decide here.
    /// </summary>
    PhysicsCreated = 15,
}
