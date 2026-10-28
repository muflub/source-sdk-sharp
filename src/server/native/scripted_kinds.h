//========= SourceSharp — what each scripted kind installs ====================//
//
// The one decision `SourceSharp_RegisterScriptedEntity` makes: given the kind a
// C# class declared, what does its standalone class get installed with. That is
// a lookup — four values per kind — and it is here rather than in a `switch`
// for a reason worth writing down, because the `switch` it replaces shipped a
// null dereference for its whole life.
//
// The old shape was seven `case`s, each ending in `return`, with
//
//     CSourceSharpScriptedClass *pClass = NULL;
//     ...
//     EntityFactoryDictionary()->InstallFactory( pClass, pClass->GetClassName() );
//
// after them. `pClass` was never assigned, so that last line would have
// dereferenced NULL — and the only thing stopping it was that every `case`
// returned before reaching it. A `default:` that fell through, an eighth kind
// added without a `case`, or a `break` typed instead of a `return` would each
// have turned a registration call into a segfault at DLL init. Nothing warned:
// gcc does not diagnose unreachable code, and "assigned in no branch" is not a
// use of an uninitialised variable as far as the compiler is concerned.
//
// A total function returning a struct cannot have a tail, cannot have an
// unassigned result, and can be tested without an engine — which is what
// scripted_kinds_test.cpp does. See src/sourcesharp/test/scripted_kinds_test.cpp.
//
// No engine headers on purpose, so the test needs no running Source. The solid
// type and flag values are `const.h`'s, restated as literals and pinned back to
// the real enums by COMPILE_TIME_ASSERTs in shell_scripted.cpp — the one place
// that has both this header and `const.h`. A value that drifted would be caught
// at compile time rather than by a map that stopped colliding.
//
//=============================================================================//
#pragma once

// Which shell a C#-declared class is instantiated as. Must match
// ScriptedEntityKind in C#.
//
// The numbers cross the ABI and are therefore fixed forever, gaps included.
// **4 is a gap**: it was SURFACE, and no C# class ever declared it —
// `func_breakable_surf` declares Brush, which installs identically. It is
// reserved rather than reused so an old managed build cannot register a
// different kind than it meant to.
enum SourceSharpScriptedKind
{
	SOURCESHARP_SCRIPTED_LOGICAL = 0,
	SOURCESHARP_SCRIPTED_ITEM    = 1,
	SOURCESHARP_SCRIPTED_BRUSH   = 2,
	SOURCESHARP_SCRIPTED_PROP    = 3,

	// 4 is retired. See above.

	// An item that flies rather than waiting to be walked into.
	SOURCESHARP_SCRIPTED_PROJECTILE = 5,

	// Something the client DRAWS and nothing touches: no model, no collision,
	// networked purely so the far side has state to draw from.
	//
	// The gap the others leave. Logical is server-only and never reaches a
	// client at all; every other kind is networked because it has a model, and
	// carries the collision that goes with having one. A beam has neither -- it
	// is two endpoints and an appearance -- and before this there was no kind
	// that could say so.
	SOURCESHARP_SCRIPTED_EFFECT = 6,

	// A connecting client's avatar (Phase 9a). The one kind whose instances
	// are never made by the map or by C# — the ENGINE causes one, by handing
	// ClientPutInServer an edict in 1..maxplayers — and the one kind whose
	// native shell is not the plain CStandaloneEntity but CStandalonePlayer,
	// which additionally implements IMovementPlayer so the movement loop can
	// move it.
	SOURCESHARP_SCRIPTED_PLAYER = 7,
};

//-----------------------------------------------------------------------------
// What SourceSharp_InstallStandaloneClass is handed for a kind.
//-----------------------------------------------------------------------------
struct SourceSharpScriptedKindParams_t
{
	bool bServerOnly;      // no edict, never sent to a client
	int  nSolidType;       // SolidType_t
	int  nSolidFlags;      // FSOLID_*
	bool bTouchEveryTick;  // every frame of overlap, rather than the edges only
	bool bPlayer;          // instantiated as CStandalonePlayer (Phase 9a)

	// NO SHELL AT ALL -- plan_entity.md step 2e, and §3.3:
	//
	//   "Entities the engine cannot see have no shell at all. A logic_relay, a
	//    filter, a math_counter, a point_template, a phys_constraint are managed
	//    objects that exist only in the directory. They take handles from the
	//    2048-8191 range, with serials the directory owns."
	//
	// It reads as a fifth answer beside the four above and it is not: it says
	// there is nothing for the other four to be ABOUT. A class that answers
	// true installs no factory, so nothing native can construct one -- which is
	// how "stops creating a shell" is enforced by construction rather than by
	// every creation site remembering to check.
	//
	// It follows from the other three rather than being a new decision. §3.3:
	// "A shell is created only for an entity that is networked, in the
	// partition, or attached to a physics object." bServerOnly says not
	// networked; SOLID_NONE with FSOLID_NOT_SOLID says not in the partition;
	// and a logical entity has no physics object. LOGICAL is the one kind for
	// which all three hold, which is why this is exactly bServerOnly today --
	// and it is a field of its own so that a future kind that is server-only
	// for some OTHER reason cannot silently lose its shell.
	bool bShellLess;
};

// const.h's values, restated. Pinned by COMPILE_TIME_ASSERT in shell_scripted.cpp.
enum
{
	SOURCESHARP_KIND_SOLID_NONE     = 0,       // SOLID_NONE
	SOURCESHARP_KIND_SOLID_BSP      = 1,       // SOLID_BSP
	SOURCESHARP_KIND_SOLID_BBOX     = 2,       // SOLID_BBOX
	SOURCESHARP_KIND_SOLID_VPHYSICS = 6,       // SOLID_VPHYSICS

	SOURCESHARP_KIND_FSOLID_NOT_SOLID     = 0x0004, // FSOLID_NOT_SOLID
	SOURCESHARP_KIND_FSOLID_TRIGGER       = 0x0008, // FSOLID_TRIGGER
	SOURCESHARP_KIND_FSOLID_NOT_STANDABLE = 0x0010, // FSOLID_NOT_STANDABLE
};

//-----------------------------------------------------------------------------
// Total: every int has an answer, and the answer for anything unrecognised is
// LOGICAL's.
//
// Falling back rather than failing is the behaviour the switch had — its
// `default:` shared LOGICAL's case — and it is the right one: a managed build
// newer than the native half would otherwise register nothing at all, and a
// server-only entity with no collision is the kind that can be wrong most
// harmlessly.
//-----------------------------------------------------------------------------
inline SourceSharpScriptedKindParams_t SourceSharp_ScriptedKindParams( int nKind )
{
	SourceSharpScriptedKindParams_t p;

	// One kind answers yes to each; assigning the common answer here keeps every
	// case below total without each of them restating it.
	p.bPlayer    = false;
	p.bShellLess = false;

	switch ( nKind )
	{
	case SOURCESHARP_SCRIPTED_ITEM:
		// A model in the world you collect by walking through it, so it is
		// networked -- the client has to draw it -- and it is a TRIGGER rather
		// than something solid: the player should pass through and pick it up,
		// not bump into it.
		//
		// NOT_SOLID alongside TRIGGER is not a contradiction. It is how Source
		// says "in the trigger list, out of the solid one", which is exactly
		// what a pickup wants.
		p.bServerOnly     = false;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_BBOX;
		p.nSolidFlags     = SOURCESHARP_KIND_FSOLID_TRIGGER | SOURCESHARP_KIND_FSOLID_NOT_SOLID;
		p.bTouchEveryTick = true;
		return p;

	case SOURCESHARP_SCRIPTED_BRUSH:
		// A piece of the map: networked, because the client draws it, and
		// SOLID_BSP, because what it collides as is the submodel itself rather
		// than a box around it.
		//
		// Solid to begin with, which is what a wall, a door and a breakable all
		// are. The ones that are volumes rather than obstacles say so from C#
		// through SetSolidity, at spawn, before anything can walk into them.
		//
		// Edges only, not every tick: a trigger fires on the way in and not
		// sixty-six times a second afterwards.
		p.bServerOnly     = false;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_BSP;
		p.nSolidFlags     = 0;
		p.bTouchEveryTick = false;
		return p;

	case SOURCESHARP_SCRIPTED_PROP:
		// A model the solver moves, so it is networked -- including its ANGLES,
		// which nothing before it needed -- and SOLID_VPHYSICS, so what it
		// collides as is the hull the model was compiled with rather than a box.
		//
		// Not simulated from the start: scenery is static and a prop_physics
		// asks for simulation from its own OnSpawn, once the map's keys are read.
		p.bServerOnly     = false;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_VPHYSICS;
		p.nSolidFlags     = 0;
		p.bTouchEveryTick = false;
		return p;

	case SOURCESHARP_SCRIPTED_PROJECTILE:
		// A model in flight: networked so the client draws it, and a BOX rather
		// than the compiled hull -- a bolt is a stick and a rocket is a tube,
		// neither has useful physics data, and what matters is that it hits what
		// it was pointed at.
		//
		// NOT_STANDABLE because nothing may stand on one in flight: without it a
		// rocket passing under a player's feet is a lift.
		p.bServerOnly     = false;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_BBOX;
		p.nSolidFlags     = SOURCESHARP_KIND_FSOLID_NOT_STANDABLE;
		p.bTouchEveryTick = false;
		return p;

	case SOURCESHARP_SCRIPTED_EFFECT:
		// The only kind with nothing physical about it. Networked, because the
		// whole point is that the client draws it; SOLID_NONE and no trigger,
		// because there is nothing to walk into.
		p.bServerOnly     = false;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_NONE;
		p.nSolidFlags     = SOURCESHARP_KIND_FSOLID_NOT_SOLID;
		p.bTouchEveryTick = false;
		return p;

	case SOURCESHARP_SCRIPTED_PLAYER:
		// A connecting client. Networked — everyone must see everyone — and a
		// BOX, which is what a player has always collided as (SOLID_BBOX with
		// the standing or ducked hull). Touches report on the edges; the
		// movement code drives its own trigger sweep per command, exactly as
		// CBaseEntity's player does through PhysicsTouchTriggers.
		p.bServerOnly     = false;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_BBOX;
		p.nSolidFlags     = 0;
		p.bTouchEveryTick = false;
		p.bPlayer         = true;
		return p;

	case SOURCESHARP_SCRIPTED_LOGICAL:
	default:
		// No model, no collision, never sent to a client -- so it is the kind
		// that asks least of whatever is underneath it.
		//
		// Server-only: no edict, and an edict not spent on a logic_timer is an
		// edict the map can spend on something a player can see.
		//
		// AND NO SHELL AT ALL -- plan_entity.md step 2e. The three values above
		// are what the shell WOULD have been installed with, kept because they
		// are the honest description of the thing and because the reload path
		// re-describes a class it has already registered; nothing constructs
		// one. See bShellLess on the struct.
		p.bServerOnly     = true;
		p.nSolidType      = SOURCESHARP_KIND_SOLID_NONE;
		p.nSolidFlags     = SOURCESHARP_KIND_FSOLID_NOT_SOLID;
		p.bTouchEveryTick = false;
		p.bShellLess      = true;
		return p;
	}
}
