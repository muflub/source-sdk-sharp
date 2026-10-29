// One number per thing that can happen to an entity. Ops travel through the
// Dispatch slot and as bits (1u << op) in a class's override mask, so a class
// pays no crossing for an op it does not override. The numbers are an ABI and
// must match SourceSharp.Abi/EntityOp.cs. No engine headers, for the tests.
#pragma once

enum EntityOp_t
{
	// Inbound: native tells managed.

	// Creates the managed instance, so it is always dispatched.
	ENTITY_OP_SPAWN = 0,

	// A map key, delivered before the spawn; also creates the managed instance.
	ENTITY_OP_KEY_VALUE = 1,

	ENTITY_OP_INPUT = 2,

	ENTITY_OP_THINK = 3,

	// Drops the registry row, so it is always dispatched.
	ENTITY_OP_REMOVED = 4,

	// All three land on C#'s single OnTouch but keep their own bits here.
	ENTITY_OP_START_TOUCH = 5,
	ENTITY_OP_TOUCH = 6,
	ENTITY_OP_END_TOUCH = 7,
	ENTITY_OP_IMPACT = 8,

	// A mover arrived where it was sent, or met something it could not push.
	ENTITY_OP_MOVE_DONE = 9,
	ENTITY_OP_BLOCKED = 10,

	// Answers how much damage actually landed.
	ENTITY_OP_DAMAGE = 11,

	ENTITY_OP_KILLED = 12,

	// Tri-state: negative means the entity is not a filter.
	ENTITY_OP_FILTER_PASSES = 13,

	// Outbound: managed tells native.

	// Managed moved the entity: native updates the partition, children and
	// triggers. Carries the previous origin for the trigger check.
	ENTITY_OP_POSITION_CHANGED = 14,

	// The physics object was just created, before the solver simulated it.
	ENTITY_OP_PHYSICS_CREATED = 15,

	// Not an op: the size of the counter array.
	ENTITY_OP_COUNT = 16,
};

#define ENTITY_OP_BIT( op ) ( 1u << (uint32_t)( op ) )

// Ops that are always dispatched: they create or drop the managed instance, or
// ask a question with no virtual to compute a bit from.
#define ENTITY_OP_ALWAYS_MASK                                                                                        \
	( ENTITY_OP_BIT( ENTITY_OP_SPAWN ) | ENTITY_OP_BIT( ENTITY_OP_KEY_VALUE ) | ENTITY_OP_BIT( ENTITY_OP_REMOVED ) | \
	  ENTITY_OP_BIT( ENTITY_OP_FILTER_PASSES ) )

// The default mask for a class whose RegisterClassOps never arrived: deliver everything.
#define ENTITY_OP_MASK_ALL 0xFFFFFFFFu
