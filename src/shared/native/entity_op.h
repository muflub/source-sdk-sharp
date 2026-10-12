//========= SourceSharp — the entity op vocabulary ============================//
//
// ONE NUMBER PER THING THAT CAN HAPPEN TO AN ENTITY (plan_entity.md §3.4).
//
// Before this header the shell reached managed code through thirteen separate
// ABI slots, one per event, each called unconditionally: every entity paid a
// managed<->native crossing for every think, every touch and every keyvalue
// whether or not its C# class had anything to say about them. §3.4 replaces
// that with one slot, `Dispatch( handle, op, args )`, and an OVERRIDE BITMASK
// per class — so a class that does not override an op costs ZERO crossings for
// it, which is the mechanism the plan's crossing budget is paid back with.
//
// The bit is `1u << op`. That is the whole reason these are small contiguous
// integers rather than a string or a hash: the shell's test is one AND against
// a word it already has in the class descriptor it already holds.
//
//-----------------------------------------------------------------------------
// THE NUMBERS CROSS THE ABI AND ARE THEREFORE FIXED FOREVER, gaps included.
//
// They travel as a bare `int32_t` in the `Dispatch` slot and as bit positions
// in the mask `RegisterClassOps` hands over, exactly as
// `SourceSharpScriptedKind` travels through `RegisterScriptedEntity`
// (scripted_kinds.h). Renumbering one side turns every touch into a think and
// nothing anywhere would say so — a class would simply start receiving the
// wrong callback, which reads as a bug in the C# and is not one.
//
// So they are pinned NUMERICALLY ON BOTH SIDES:
//
//   C++  src/sourcesharp/test/entity_op_test.cpp   EntityOpNumbers.*
//   C#   SourceSharp.Tests/EntityOpTests.cs        OpNumbersAreFixed
//
// and the C++ test additionally READS `SourceSharp.Abi/EntityOp.cs` off the
// filesystem and compares name for name, because two independent lists of the
// same literals are two things that can drift apart while both tests stay
// green. A pin on one side alone cannot see a renumbering on the other; that
// is the whole failure this pair exists to catch.
//
//-----------------------------------------------------------------------------
// DIRECTION. Most ops are INBOUND: the engine did something to the shell and
// managed code is being told. `ENTITY_OP_POSITION_CHANGED` is OUTBOUND —
// managed code moved the entity and native has partition, hierarchy and trigger
// work to do about it (plan_entity.md step 1b, .claude/entity-port/abi-requests
// /1b.md §2). One vocabulary rather than two, because "what happened" is the
// same question whoever is asking it, and a second enum would be a second thing
// to keep numbered.
//
// No engine headers on purpose, so entity_op_test.cpp needs no running Source.
//
//=============================================================================//
#ifndef SOURCESHARP_ENTITY_OP_H
#define SOURCESHARP_ENTITY_OP_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>

//-----------------------------------------------------------------------------
// The ops. Must match `EntityOp` in SourceSharp.Abi/EntityOp.cs.
//-----------------------------------------------------------------------------
enum EntityOp_t
{
	// --- inbound: native tells managed -------------------------------------

	// The entity is in the world and configured. This is what CREATES the
	// managed instance, which is why it is in ENTITY_OP_ALWAYS_MASK below.
	ENTITY_OP_SPAWN = 0,

	// A key from the map, delivered BEFORE the spawn. Also creates the managed
	// instance -- Source parses every keyvalue before it spawns anything, so
	// this is usually the first any managed code hears of the entity.
	ENTITY_OP_KEY_VALUE = 1,

	// An input fired at it, by name.
	ENTITY_OP_INPUT = 2,

	// Its think time came round.
	ENTITY_OP_THINK = 3,

	// It is going. Drops the registry's row, which is why it is always
	// dispatched: eliding it would leak one managed instance per entity.
	ENTITY_OP_REMOVED = 4,

	// Something arrived in it, is still in it, or hit it. Three ops rather than
	// one because the shell fires them at three different rates and a class may
	// want the edges without the sixty-six-a-second middle; all three land on
	// C#'s single OnTouch, so all three share one override bit's SOURCE (see
	// EntityOp.cs) while keeping their own bit here.
	ENTITY_OP_START_TOUCH = 5,
	ENTITY_OP_TOUCH = 6,
	ENTITY_OP_END_TOUCH = 7,
	ENTITY_OP_IMPACT = 8,

	// A mover arrived where it was sent, or met something it could not push.
	ENTITY_OP_MOVE_DONE = 9,
	ENTITY_OP_BLOCKED = 10,

	// Damage, and the answer is how much actually landed.
	ENTITY_OP_DAMAGE = 11,

	// Its health reached zero.
	ENTITY_OP_KILLED = 12,

	// Whether a managed filter admits a subject. Tri-state: negative means the
	// entity asked about is not a filter at all, which only C# can say.
	ENTITY_OP_FILTER_PASSES = 13,

	// --- outbound: managed tells native ------------------------------------

	// Managed code moved the entity. Native does what the tail of
	// CStandaloneEntity::SetAbsTransform does: move it in the spatial
	// partition, mark the two block offsets, carry the children, and touch the
	// triggers it is now in. Carries the PREVIOUS origin, because the trigger
	// half is a question about where it was as well as where it is.
	ENTITY_OP_POSITION_CHANGED = 14,

	// The shell just created the entity's physics object, before the solver has
	// simulated it. Managed decides what stock's CreateVPhysics decided at
	// creation (a start-asleep prop sleeps it). Physics stock parity B4.
	ENTITY_OP_PHYSICS_CREATED = 15,

	// One past the last. Not an op: it is the size of the counter array and the
	// bound the shell range-checks against.
	ENTITY_OP_COUNT = 16,
};

//-----------------------------------------------------------------------------
// The bit for an op, which is the whole of what the mask is.
//-----------------------------------------------------------------------------
#define ENTITY_OP_BIT( op ) ( 1u << (uint32_t)( op ) )

//-----------------------------------------------------------------------------
// THE OPS THE BIT CANNOT TURN OFF.
//
// Not "the ones nobody wanted to optimise" — the ones where the managed side
// does BOOKKEEPING and not merely a call to a virtual the class may not have
// overridden:
//
//   SPAWN, KEY_VALUE  create the managed instance and bind its var sets. Elide
//                     them and the entity has no C# object at all, so every
//                     later op finds nothing and the class stops existing.
//   REMOVED           drops the registry row. Elide it and the registry keeps
//                     an instance for every entity the map ever had -- the
//                     exact leak ~CStandaloneScriptedEntity was written for.
//   FILTER_PASSES     is asked ABOUT an entity by a third party, and its answer
//                     distinguishes "did not pass" from "not a filter". There
//                     is no ScriptedEntity virtual to reflect over, so there is
//                     nothing for a bit to be computed FROM.
//
// Stated as a constant rather than as four `if`s so that both sides can assert
// it, and so a future op added to the elidable set is a change to one line.
//-----------------------------------------------------------------------------
#define ENTITY_OP_ALWAYS_MASK                          \
	( ENTITY_OP_BIT( ENTITY_OP_SPAWN )                 \
	| ENTITY_OP_BIT( ENTITY_OP_KEY_VALUE )             \
	| ENTITY_OP_BIT( ENTITY_OP_REMOVED )               \
	| ENTITY_OP_BIT( ENTITY_OP_FILTER_PASSES ) )

//-----------------------------------------------------------------------------
// WHAT A CLASS THAT HAS NOT REGISTERED ITS OPS COSTS: everything.
//
// The default is all-ones and not zero, and the direction matters more than it
// looks. A class whose RegisterClassOps never arrived -- managed startup that
// failed, a reload in flight, a native build newer than the managed one --
// keeps working and merely pays the crossings it used to pay. The other default
// would silently stop delivering touches and thinks to entities that do
// override them, which is a game that is broken in a way no build error and no
// warning names.
//-----------------------------------------------------------------------------
#define ENTITY_OP_MASK_ALL 0xFFFFFFFFu

#endif // SOURCESHARP_ENTITY_OP_H
