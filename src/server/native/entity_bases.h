//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// The four helper bases the SDK's point, logical and server-only entities
// derive from -- lifted out of baseentity.h ahead of that header's deletion.
//
// CPointEntity, CServerOnlyEntity, CServerOnlyPointEntity and CLogicalEntity
// are forty lines of convenience: a base class, a spawnflag mask, and a
// KeyValue that refuses mins/maxs. They have never been part of what
// CBaseEntity IS. But they lived at the bottom of baseentity.h, and 138 files
// in this tree name one of them as a base class, so deleting that header would
// have taken all 138 with it for no reason connected to the entity root at all.
//
// So they move first, on their own, while the move is still a move -- the
// classes are byte-identical to the ones that were there, and this file is
// reached from exactly where they used to be read from.
//
// WHY THIS FILE HAS NO INCLUDES
//
// It is included from game/shared/baseentity_shared.h's server branch, on the
// line after `#include "baseentity.h"`, which is how every server translation
// unit already sees these four classes: vpc force-includes game/server/cbase.h
// into every TU as the precompiled prelude, and cbase.h reaches
// baseentity_shared.h. Measured rather than assumed, with
// docs/typedef-dryrun/reach_pch.py, which folds the prelude's own dependency
// list into each object's -- without that fold the answer is 9 of 197 and it
// is wrong. After the move:
//
//   game/server/baseentity.h        197 of 197 server,   0 of 325 client
//   game/shared/baseentity_shared.h 197 of 197 server, 325 of 325 client
//   game/server/entity_bases.h      197 of 197 server,   0 of 325 client
//
// i.e. exactly the reach it had inside baseentity.h, on the side that has it.
//
// It deliberately does NOT include baseentity.h. An include of that header here
// would put this file straight back in the blast radius it was moved out of.
// The one thing it needs from its includer is a complete CBaseEntity, and the
// one place it is included from supplies exactly that.
//
// The out-of-line bodies WERE in entity_bases.cpp, which was never compiled
// (not on any CMake list, and it included a cbase.h the farm lacks) and is
// deleted with plan_server_native_min P1: armed, no caller exists for any of
// the four -- no ServerClass instantiates these classes, so the definitions
// could only satisfy a caller that is itself dead.
//
// WHY THIS FILE IS ALIAS-AGNOSTIC AND EVERY OTHER GAME HEADER IS NOT
//
// It is in the PRECOMPILED PRELUDE -- 197 of 197 server TUs, per the reach
// table above -- so a diagnostic in it is a diagnostic in every server object,
// attributed to a header nobody edited. Phase 13 step 4's trial flip measured
// exactly that: armed, 180 of 196 server TUs failed and 86 of them had nothing
// wrong except the three sites below (docs/typedef-dryrun/floor_server_armed.txt,
// summary_server_armed.txt). Three lines of prelude were burying the
// measurement of 2,741 real ones.
//
// MEASURED AFTER, rather than predicted: the same armed survey over the same
// 196 TUs reports 94 failing and 102 passing, and the unique site count does
// not move (2,727 both ways at this tip) -- 86 TUs unblocked, no site hidden.
// The prelude's own floor -- an empty TU compiled with the full server prelude
// and the alias armed, which is `survey_headerfloor server` -- is now ZERO,
// where it was these three. Restore the pre-lane-P text and it reads 3 again,
// so that zero is a measurement rather than a tool that stopped looking.
// (game/server/baseentity.h:93's `class CBaseEntity;` is the prelude's other
// site and carries the same guard now; it alone accounted for the 180.)
//
// So the three sites carry both arms, in the `#ifndef SS_CBASEENTITY_ALIAS` /
// `#else` idiom collisionproperty.h and particle_property.h already use for
// their friend declarations. The unarmed arm is the SDK text unchanged, byte
// for byte; the armed arm is derived below rather than guessed, and both are
// pinned by sourcesharp/test/entity_bases_alias_test.cpp and its armed twin
// entity_bases_alias_armed.cpp, which compile this header TWICE -- once against
// a stand-in for the SDK's CBaseEntity, once with SS_CBASEENTITY_ALIAS set and
// CBaseEntity a real alias for the real public/igameentity.h -- and assert what
// each arm answers.
//
//=============================================================================//
#pragma once

class CPointEntity : public IGameEntity
{
public:
	DECLARE_CLASS( CPointEntity, IGameEntity );

	void	Spawn( void );

	// WHAT THE ARMED ARM'S 0 IS, and why it is a computation rather than a guess.
	//
	// Unarmed, BaseClass is CBaseEntity and CBaseEntity::ObjectCaps()
	// (baseentity.cpp) has exactly three outcomes: FCAP_ACROSS_TRANSITION for a
	// parentless non-brush, 0 for a parentless brush, and the PARENT's caps
	// masked to the +USE bits for anything parented. This expression then
	// clears FCAP_ACROSS_TRANSITION -- so the first outcome contributes 0, the
	// second contributes 0, and only the third can be non-zero.
	//
	// Armed, BaseClass is IGameEntity, and the third outcome cannot arise: that
	// hierarchy has no parent to forward from. igameentity.h says so in its own
	// words under GetMoveParent -- "never crosses ... a handle-keyed registry
	// resolved through the virtual GetGameEntity() precisely so that no raw
	// parent pointer exists to hand out". With the only non-zero term absent,
	// the value of this expression over there IS 0, and 0 is what it says.
	//
	// The member stays rather than being guarded away, because the flip does not
	// delete this class: 138 files name one of these four as a base, and the
	// ones that override ObjectCaps call BaseClass::ObjectCaps() on it. Removing
	// it here would break them for a reason that started in this file.
	virtual int	ObjectCaps( void ) { return 0; }
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
private:
};

// Has a position + size
class CServerOnlyEntity : public IGameEntity
{
	DECLARE_CLASS( CServerOnlyEntity, IGameEntity );
public:
	// The `true` is CBaseEntity's bServerOnly, and all it does is
	// AddEFlags( EFL_SERVER_ONLY ) (baseentity.cpp). Armed there is no such
	// constructor and no such flag: IGameEntity has neither, and igameentity.h
	// names AddEFlags/IsEFlagSet among the members that never cross -- "EFlags
	// are the SDK's dirty-bit and lifetime bookkeeping ... every remaining site
	// is inside a CBaseEntity-only path". EFL_SERVER_ONLY in particular is the
	// SDK's own "this one is never networked", which is the DEFINING property of
	// the IGameEntity hierarchy rather than a bit it has to set. So the armed
	// arm is the same constructor with nothing left to do.
	CServerOnlyEntity() {}
	
	// Same computation as CPointEntity::ObjectCaps above, for the same reason.
	virtual int ObjectCaps( void ) { return 0; }
};

// Has only a position, no size
class CServerOnlyPointEntity : public CServerOnlyEntity
{
	DECLARE_CLASS( CServerOnlyPointEntity, CServerOnlyEntity );

public:
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
};

// Has no position or size
class CLogicalEntity : public CServerOnlyEntity
{
	DECLARE_CLASS( CLogicalEntity, CServerOnlyEntity );

public:
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
};

//-----------------------------------------------------------------------------
// WHERE THE DATADESC CHAIN ENDS FOR THE FOUR FREE-RIDERS.
//
// public/datamap.h's non-template `DataMapAccess( IGameEntity *, ... )` ends
// the chain for a class whose BaseClass IS the managed boundary. It cannot end
// it for a class whose BaseClass is one of the four above, and that is a
// DIFFERENT case rather than a smaller one:
//
//   None of these four declares a datadesc. So `CPointEntity::m_DataMap` is
//   not CPointEntity's -- name lookup walks straight through to the base's,
//   and today that resolves to CBaseEntity::m_DataMap. A derived class doing
//     BEGIN_DATADESC( CFoo )   with   DECLARE_CLASS( CFoo, CPointEntity )
//   emits DataMapAccess( (CPointEntity *)NULL, &p ). CPointEntity * is the
//   template's EXACT match, so the template wins, and its body names
//   &CPointEntity::m_DataMap -- the inherited member, which armed does not
//   exist. The IGameEntity * overload is never even a candidate worth
//   considering: it would need a derived-to-base conversion and loses.
//
// Measured at 19451ca6 with an armed survey, these are the ONLY server
// diagnostics of this shape outside game/server/baseentity.cpp itself:
//   'm_DataMap' is not a member of 'CPointEntity'   -- GameStats.o, client.o,
//                                                      sound.o, soundent.o,
//                                                      subs.o
//   'm_DataMap' is not a member of 'CLogicalEntity' -- physics_npc_solver.o,
//                                                      shell_entity_orphans.o
// CServerOnlyEntity and CServerOnlyPointEntity are the same free-riders and
// are covered here too: nothing chains a datadesc to them at this tip, so they
// emit no diagnostic yet, but they differ from the other two in nothing that
// matters and leaving them out would make the next class to derive from one a
// fresh compile error for a solved reason.
//
// NULL IS THE TRUTH, not a convenience. These four contribute no save fields:
// they have no datadesc, so the unarmed chain already runs PAST them to
// CBaseEntity's map. Armed, that map is on the managed side. So the value the
// chain should take is exactly the value BEGIN_DATADESC_NO_BASE has always
// meant, which is what datamap.h's IGameEntity * overload hands the classes
// one level up.
//
// THE GUARD IS GONE AND THE PARAGRAPH BELOW IS WHY IT MATTERED. These four
// overloads used to sit inside `#ifdef SS_CBASEENTITY_ALIAS`, and the whole of
// this note was about that guard being load-bearing. The close deleted every
// such arm in the compiled tree, so the overloads are unconditional now and the
// DISARMED tree the note protects against is not a configuration this tree can
// produce any more (the close's handover says so for all 254 files). The
// reasoning is kept because it is the reason the overloads are shaped as they
// are, and because it is what the next person needs if the guard ever comes
// back. It read: datamap.h's IGameEntity *
// overload is safe to leave unguarded because it is unreachable in an unarmed
// tree: nothing has IGameEntity for a BaseClass. These four overloads are NOT
// unreachable -- disarmed they would out-resolve the template for every class
// deriving from a free-rider and answer NULL where the correct answer is
// &CBaseEntity::m_DataMap. That is not a compile error anywhere; it is a
// SHIPPING save/restore chain silently truncated at the point entity, losing
// the base entity's saved fields for 138 files' worth of classes. So the guard
// is asserted by position relative to its #endif by a test, not eyeballed.
//
// datamap_t is FORWARD-DECLARED rather than included, for the reason under WHY
// THIS FILE HAS NO INCLUDES at the top: `#include "datamap.h"` here would put
// this prelude header straight back into the blast radius it was moved out of.
// A forward declaration is enough -- the overloads only form a datamap_t ** and
// assign NULL through it, neither of which needs a complete type -- and it
// matches public/datamap.h:274, which declares it exactly this way itself.
//
// It also has to be a declaration rather than an include because of WHERE this
// file is included from in the tests: sourcesharp/test/entity_bases_alias_armed.cpp
// includes it inside `namespace armed`, where an include of datamap.h would not
// even be legal. In the real build this redeclares the ::datamap_t that cbase.h
// has already supplied to all 197 server TUs, which is a no-op.
//-----------------------------------------------------------------------------

struct datamap_t;

inline void DataMapAccess( CPointEntity *, datamap_t **p )			{ *p = NULL; }
inline void DataMapAccess( CServerOnlyEntity *, datamap_t **p )		{ *p = NULL; }
inline void DataMapAccess( CServerOnlyPointEntity *, datamap_t **p ){ *p = NULL; }
inline void DataMapAccess( CLogicalEntity *, datamap_t **p )		{ *p = NULL; }
