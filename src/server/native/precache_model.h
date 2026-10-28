//========= SourceSharp — precaching a model, without an entity ===============//
//
// CBaseEntity::PrecacheModel and CBaseEntity::PrecacheModelComponents were
// STATIC members of CBaseEntity that never touched an entity. Between them they
// do three things -- warn about a late precache, call engine->PrecacheModel, and
// walk the returned model's sounds, particles and anim events -- and not one of
// those needs, reads or produces an entity. The class supplied a namespace and
// nothing else, and that namespace is what plan_baseentity.md Phase 13 deletes.
//
// Why this mattered enough to move: five of the port's own translation units
// asked CBaseEntity for nothing BUT this. shell_server.o and
// shell_standalone_base.o each had exactly one undefined reference to
// baseentity.o and it was CBaseEntity::PrecacheModel(char const*, bool) -- so a
// static with no entity in it was the whole of their dependency on the class.
// Both leave the consumer set with this move. The other three
// (shell_worldquery.o, shell_physicsobjects.o, world_adapter.o) shed a reference
// each but stay, because they ask for setters and, in world_adapter's case, for
// CWorld's base-class vtable.
//
// The declarations stay on CBaseEntity as one-line forwarders. 734 call sites
// across 262 SDK files reach them unqualified from inside member functions, and
// retyping those is not this move -- most of those files are evicted or ported
// before the typedef lands, and the ones that are not already consume
// baseentity.o for a dozen other reasons.
//
// Engine-facing, so it is NOT engine-free and does not pretend to be: it talks
// to engine, modelinfo and mdlcache directly. What it does not talk to is an
// entity.
//
//=============================================================================//
#pragma once
#define PRECACHE_MODEL_H

//-----------------------------------------------------------------------------
// Add a model to the level precache list and return its model index, or -1.
//
// Warns when called after precaching is closed, then hands the name to the
// engine and precaches the components of whatever came back.
//-----------------------------------------------------------------------------
int Precache_Model( const char *name, bool bPreload = true );

//-----------------------------------------------------------------------------
// Precache the sounds, particle systems and anim-event-owned assets belonging
// to an already-precached studio model. A no-op for a non-studio model.
//-----------------------------------------------------------------------------
void Precache_ModelComponents( int nModelIndex );
