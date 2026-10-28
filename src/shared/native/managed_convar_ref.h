//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — reaching a ConVar that MANAGED code declared ========//
//
// plan_entity.md §6's ConVar row, and the standing rule that goes with it:
//
//     ConVar and ConCommand registration for values managed code declares —
//     the engine owns the registry. No ConVar's default or semantics is
//     declared natively.
//
// A ConVar's DECLARATION is policy: the name, the default, the flags and the
// help text are the tuning value itself, and while they sit in C++ managed code
// cannot see them. Its REGISTRY is mechanism: one table per process, owned by
// the engine, and both game DLLs put their convars in the same one.
//
// So the declaration moves to a `[ConVar]` class in the managed tree and the
// native readers that remain resolve the SAME convar out of the engine's
// registry by name. Nothing crosses the managed boundary to do it: a
// `ConVarRef` read is a load through a pointer into the engine's own ConVar,
// which is exactly what the `ConVar` object it replaces was doing.
//
// THE FAILURE THIS TYPE EXISTS TO PREVENT, and it is a silent one.
//
// `ConVarRef` resolves ONCE, in its constructor, and caches the answer —
// including a miss. A native `ConVar` is constructed at DLL load; a managed
// `[ConVar]` is registered later, when ConVarRegistry.RegisterAll runs during
// module startup. Any ConVarRef constructed in that window caches "no such
// convar" FOREVER, and then answers 0 / 0.0 / "" to every read for the rest of
// the process. Nothing warns after the first complaint, the value simply reads
// as zero, and a knob that reads as zero looks exactly like a knob somebody set
// to zero.
//
// CManagedConVarRef therefore re-tries the lookup while it is still unresolved,
// so a read that happens before managed startup costs a failed lookup and a
// read that happens after gets the real convar. Once resolved it is a plain
// cached pointer and never looks again — the steady-state cost against the
// `ConVar` object it replaced is one predictable branch.
//
// USE A FILE-SCOPE OR FUNCTION-LOCAL STATIC of this type in place of the
// `ConVar` that moved, with the SAME name string the managed class declares.
// Do NOT construct a native `ConVar` of that name as well: a second convar
// registered under one name is neither a compile error nor a link error, and
// as util_shared_library.cpp's own note on `developer` records, "neither the
// console nor the game says which one it read."
//
//=============================================================================//

#pragma once

#include "convar.h"

//-----------------------------------------------------------------------------
// A by-name handle on a convar whose declaration lives in managed code.
//
// The read accessors mirror ConVar's, so a call site that moved from
// `violence_hblood.GetBool()` to a ref of the same name did not otherwise
// change — which is the point: this replaces the DECLARATION, never the rule
// that reads it.
//-----------------------------------------------------------------------------
class CManagedConVarRef
{
public:
	explicit CManagedConVarRef( const char *pName )
		: m_pName( pName ), m_Ref( (IConVar *)NULL ), m_bResolved( false )
	{
		// Deliberately NOT resolved here. A file-scope instance of this class
		// is constructed at DLL load, which is before managed registration by
		// construction, so resolving now would cache the miss this whole type
		// exists to avoid.
	}

	// True once the engine has the convar. Callers that want to behave
	// differently when a knob is genuinely absent ask this; callers that just
	// want the value do not have to, because the getters answer the declared
	// zero until it appears.
	bool IsValid() const { return Resolve(); }

	bool GetBool() const { return GetInt() != 0; }

	int GetInt() const
	{
		return Resolve() ? m_Ref.GetInt() : 0;
	}

	float GetFloat() const
	{
		return Resolve() ? m_Ref.GetFloat() : 0.0f;
	}

	const char *GetString() const
	{
		return Resolve() ? m_Ref.GetString() : "";
	}

	void SetValue( int nValue )     { if ( Resolve() ) m_Ref.SetValue( nValue ); }
	void SetValue( float flValue )  { if ( Resolve() ) m_Ref.SetValue( flValue ); }
	void SetValue( const char *pV ) { if ( Resolve() ) m_Ref.SetValue( pV ); }

	const char *GetName() const { return m_pName; }

private:
	// Resolves on first success and never looks again. `true` from the cached
	// flag first, so the steady state is one branch and a load.
	//
	// bIgnoreMissing is TRUE on purpose: a miss here is the expected state
	// before managed startup, not a fault, and ConVarRef's own complaint would
	// otherwise be printed on every read until registration caught up.
	bool Resolve() const
	{
		if ( m_bResolved )
			return true;

		m_Ref.Init( m_pName, true );

		if ( !m_Ref.IsValid() )
			return false;

		m_bResolved = true;
		return true;
	}

	const char        *m_pName;
	mutable ConVarRef  m_Ref;
	mutable bool       m_bResolved;
};
