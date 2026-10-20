//========= SourceSharp — the globals that were wearing a class scope ========//
//
// Six process-wide variables that were declared `static` members of CBaseEntity
// and C_BaseEntity. Not one of them is entity state: there is one of each per
// server, they are read and written by console commands and by the frame loop,
// and no entity ever consults its own copy because there are no copies. The
// class supplied a namespace and nothing else.
//
//     m_bInDebugSelect / m_nDebugPlayer     which player is picking entities
//                                           out of the world with the debug
//                                           overlays, and whether that is on
//     m_bDebugPause / m_nDebugSteps         the entity I/O single-stepper
//     sm_bAccurateTriggerBboxChecks         engine trigger-check policy
//     sm_bDisableTouchFuncs                 suppress touch callbacks across a
//                                           level transition
//
// plan_baseentity.md Phase 13 deletes that namespace. A static data member of a
// class that becomes `using CBaseEntity = IGameEntity` does not merely fail to
// link -- it is rejected at compile time, and so is every later mention of the
// member, turning one line into hundreds of diagnostics. So these have to live
// somewhere, and "somewhere" is a header with no entity in it.
//
// This used to say it "stops parsing, everywhere, at once". It does not: the
// diagnostic is recoverable and the translation unit is never lost. Measured
// and written up in src/sourcesharp/test/baseentity_statics_test.cpp's header
// block. The move is still right, for the volume rather than for a TU loss
// that does not happen.
//
// HEADER-ONLY, VIA ACCESSORS, and that is not a stylistic choice. Adding a .cpp
// would mean editing server_csharp.vpc and client_csharp.vpc, which are owned by
// the integration lane rather than by this one. Each storage is a C++17 `inline`
// variable (a function-local static inside an inline accessor until every target
// moved to C++17 on 2026-09-29): one object across the program, and for a scalar
// with a constant initialiser constant-initialised -- no guard variable, no
// thread-safe-init branch, so the read costs what the old member read cost.
//
// Accessors rather than references-to-globals so that "who writes this" is
// answerable by grepping for one name, which was never true of a public static.
//
//=============================================================================//
#ifndef ENTITY_DEBUG_GLOBALS_H
#define ENTITY_DEBUG_GLOBALS_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Debug entity selection (m_bInDebugSelect / m_nDebugPlayer) went in
// plan_server_native_v2 T6: nothing in either DLL set either one, so the
// overlay viewer they gated was never established (shell_debugoverlays.cpp).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The entity I/O single-stepper, behind ent_pause / ent_step. Paused, the event
// queue stops firing outputs; each step lets a fixed number through.
//
// These four functions are CBaseEntity::Debug_Pause, Debug_IsPaused,
// Debug_SetSteps, Debug_Step and Debug_ShouldStep, moved verbatim -- same
// bodies, same order of operations. Debug_Step decrements THEN reports, which
// is why "steps remaining" and "may fire" are separate questions.
//-----------------------------------------------------------------------------
inline bool s_bDebugPause = false;
inline bool &EntityDebug_PausedRef()
{
	return s_bDebugPause;
}

inline int s_nDebugSteps = 1;
inline int &EntityDebug_StepsRef()
{
	return s_nDebugSteps;
}

inline void EntityDebug_Pause( bool bPause ) { EntityDebug_PausedRef() = bPause; }
inline bool EntityDebug_IsPaused()           { return EntityDebug_PausedRef(); }
inline void EntityDebug_SetSteps( int n )    { EntityDebug_StepsRef() = n; }

// Consume one step. Answers whether any remain AFTER consuming, exactly as
// CBaseEntity::Debug_Step did.
inline bool EntityDebug_Step()
{
	int &nSteps = EntityDebug_StepsRef();
	if ( nSteps > 0 )
	{
		nSteps--;
	}
	return ( nSteps > 0 );
}

// May the queue fire right now? Not paused, or paused with steps left.
inline bool EntityDebug_ShouldStep()
{
	return ( !EntityDebug_PausedRef() || EntityDebug_StepsRef() > 0 );
}

//-----------------------------------------------------------------------------
// Policy flags. Both were public statics that anything could set; both are
// really engine-facing switches with one writer each.
//-----------------------------------------------------------------------------

// SOLID_BBOX entities get a fully accurate trigger-vs-bbox check when set.
// False is the legacy ep1 behaviour. Passed straight to engine->SolidMoved and
// engine->TriggerMoved.
inline bool s_bAccurate = true;
inline bool &EntityDebug_AccurateTriggerBboxChecksRef()
{
	return s_bAccurate;
}

inline bool EntityDebug_AccurateTriggerBboxChecks()        { return EntityDebug_AccurateTriggerBboxChecksRef(); }
inline void EntityDebug_SetAccurateTriggerBboxChecks( bool b ) { EntityDebug_AccurateTriggerBboxChecksRef() = b; }

// Suppresses PhysicsTouch and PhysicsStartTouch calls. Set across a level
// transition so that entities arriving in their new positions do not fire touch
// callbacks for overlaps they were already in. Never written on the client,
// where it is a permanent false.
inline bool s_bDisableTouchFuncs = false;
inline bool &EntityDebug_TouchFuncsDisabledRef()
{
	return s_bDisableTouchFuncs;
}

inline bool EntityDebug_TouchFuncsDisabled()          { return EntityDebug_TouchFuncsDisabledRef(); }
inline void EntityDebug_SetTouchFuncsDisabled( bool b ) { EntityDebug_TouchFuncsDisabledRef() = b; }

#endif // ENTITY_DEBUG_GLOBALS_H
