//========= SourceSharp — the three gates baseentity.cpp kept in a class =====//
//
// Three more process-wide variables that were declared `static` members of
// CBaseEntity / C_BaseEntity and DEFINED in the two files this lane owns,
// game/server/baseentity.cpp and game/client/c_baseentity.cpp. Not one of them
// is entity state: there is one of each per DLL, they are written by the frame
// loop and by scoped brackets around blocks of code, and no entity ever
// consults its own copy because there are no copies. The class supplied a
// namespace and nothing else -- entity_debug_globals.h's six finding exactly.
//
//     s_bAbsQueriesValid            baseentity.cpp:100, c_baseentity.cpp:92
//                                   whether GetAbsOrigin and friends may be
//                                   asked right now. False across a Restore,
//                                   when the hierarchy is half built; the
//                                   readers Assert on it.
//     s_bAbsRecomputationEnabled    c_baseentity.cpp:93 (client only)
//                                   whether CalcAbsolutePosition may run.
//                                   Push/Pop bracket a block; the stack that
//                                   saves it is already a file-static over
//                                   there and stays there.
//     s_bInterpolate                c_baseentity.cpp:94 (client only)
//                                   whether interpolation runs this frame.
//                                   Recomputed once per frame in
//                                   InterpolateServerEntities from
//                                   cl_interpolate, demo playback and net
//                                   timeout.
//
// WHY THEY HAD TO MOVE, and it is not the reason the rest of the removal set
// moves. plan_baseentity.md Phase 13 makes `CBaseEntity` an alias for
// `IGameEntity`. An out-of-line FUNCTION definition written `CBaseEntity::Foo()`
// under an alias is a compile error in one file. A static DATA member is worse:
// `bool CBaseEntity::s_bAbsQueriesValid = true;` is rejected, the member stays
// undeclared at that scope, and every later mention of it is rejected in turn,
// so one line becomes hundreds of diagnostics with a single cause and the
// file's real errors are scattered among them. Fourteen of these existed at
// e806bce7 and all fourteen were in this lane's two files.
//
// This used to say it "stops the translation unit PARSING". It does not -- the
// diagnostic is fully recoverable and gcc keeps going. Measured: armed,
// baseentity.cpp:296 is followed by 283 further diagnostics in the same TU,
// 573 in the file; 915 followed the first on the client. The correction is
// written up in src/sourcesharp/test/baseentity_statics_test.cpp's header
// block. The move was worth making for the volume; nobody should plan around
// a TU that is never lost.
//
// HEADER-ONLY, VIA ACCESSORS, for entity_debug_globals.h's reasons verbatim: a
// .cpp would mean editing server_csharp.vpc and client_csharp.vpc, which are
// the integration lane's; an `inline` variable (C++17) is one object across the
// program, and for a scalar with a constant initialiser it is
// constant-initialised, so there is no guard variable and no thread-safe-init
// branch and the read costs what the member read cost.
//
// ONE OBJECT PER DLL, exactly as before. server.so and client.so each compile
// their own copy of this header and each had its own `CBaseEntity::` /
// `C_BaseEntity::` object before. Nothing crosses, and nothing is supposed to:
// unlike the prediction seed, whose whole job is that the two sides agree,
// these three describe the state of one DLL's own frame.
//
// ALL THREE ARE DECLARED ON BOTH SIDES rather than behind #ifdef CLIENT_DLL.
// Only s_bAbsQueriesValid is reached by both; the other two are read and
// written only by client code. An inline function nobody calls emits nothing,
// so the unused pair costs the server no storage and no initialiser, and an
// unconditional header is one fewer arm to keep in sync than the alternative.
//
// THE THREAD RULE IS NOT HERE, DELIBERATELY. C_BaseEntity::IsAbsQueriesValid
// and IsAbsRecomputationsEnabled answer `true` unconditionally off the main
// thread -- "@MULTICORE: always allow in worker threads, assume higher level
// code is handling correctly". That is a policy about who may ask, not about
// what the flag holds, and it stays in the class-scope functions in
// c_baseentity.cpp where it already lives. Moving it here would apply it to the
// server too, which never had it, and would silently change what
// CBaseEntity::IsAbsQueriesValid() answers on a worker thread.
//
// Shared header: both server.so and client.so compile against it.
//
//=============================================================================//
#ifndef ENTITY_ABS_QUERY_STATE_H
#define ENTITY_ABS_QUERY_STATE_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Whether absolute-transform queries may be asked right now.
//
// true at rest. SetAbsQueriesValid( false ) brackets the periods when the
// hierarchy is incomplete -- save/restore is the one in the stock tree -- and
// the readers Assert rather than returning anything different, so this is a
// debugging gate rather than a behaviour switch.
//-----------------------------------------------------------------------------
inline bool s_bAbsQueriesValid = true;
inline bool &EntityAbs_QueriesValidRef()
{
	return s_bAbsQueriesValid;
}

//-----------------------------------------------------------------------------
// Whether CalcAbsolutePosition may recompute a coordinate frame right now.
//
// Client only. true at rest; PushEnableAbsRecomputations / Pop bracket a block
// with it, and the eight-deep save stack that Push/Pop use is a file-static in
// c_baseentity.cpp -- it is not a class member and it does not move.
//-----------------------------------------------------------------------------
inline bool s_bAbsRecomputationEnabled = true;
inline bool &EntityAbs_RecomputationEnabledRef()
{
	return s_bAbsRecomputationEnabled;
}

//-----------------------------------------------------------------------------
// Whether entity interpolation runs this frame.
//
// Client only. Recomputed at the top of every
// C_BaseEntity::InterpolateServerEntities from cl_interpolate, then forced
// false for timedemo playback, for a paused engine, and when the net channel
// has heard nothing for half a second. true at rest so that the very first
// frame, before that function has ever run, behaves as it did.
//-----------------------------------------------------------------------------
inline bool s_bInterpolate = true;
inline bool &EntityInterp_EnabledRef()
{
	return s_bInterpolate;
}

//-----------------------------------------------------------------------------
// THE CLIENT'S SIX ACCESSORS, LIFTED OUT OF THE CLASS SCOPE.
//
// c_baseentity.cpp defined all six as `static` members of C_BaseEntity
// (c_baseentity.h:1272-1283). A static member takes no `this`, and none of
// these reads one: they read the three storages above and an eight-deep save
// stack. The class was supplying a namespace, exactly as it was for the
// seventeen sound statics, the seven physics statics and the five in
// entity_shared_statics.h. Under `using CBaseEntity = IGameEntity;` an alias is
// not a class scope, so `void C_BaseEntity::SetAbsQueriesValid(...)` is not a
// definition -- it is a compile error.
//
// THE BODIES DO NOT MOVE, and stay in game/client/c_baseentity.cpp. Four of
// them reach that translation unit's own file-statics -- g_bAbsRecomputationStack
// and g_iAbsRecomputationStackPos, c_baseentity.cpp:114-115 -- and all six reach
// ThreadInMainThread() and Assert(). Exporting that state to move six bodies
// would be a bigger change than the lift and would buy nothing. Only the SCOPE
// each definition is written in moves, exactly as it did for GameSound_*,
// PhysicsLink_* and EntitySim_IsSimulatingOnAlternateTicks.
//
// THE SIX CLASS-SCOPE NAMES SURVIVE AS INLINE FORWARDERS IN c_baseentity.h,
// not in a quarantine .cpp. baseentity_shared_forwarders.cpp existed because
// its five forwarders had to be out of line -- and it has since been retired
// outright, its callers moved to the free functions, which is the end state
// these six reach without ever needing a file of their own. An inline forwarder
// in the class body is strictly better here: it emits NO symbol, so
// the lift takes six off c_baseentity.o instead of moving six into another
// file, and it is deleted by the same edit that deletes the class body at step
// 4 rather than needing a file removed separately.
//
// CLIENT_DLL-GUARDED, and that guard is doing work. The SERVER also has
// SetAbsQueriesValid and IsAbsQueriesValid -- inline in baseentity.h:2115-2124 --
// and they are NOT these. They have no ThreadInMainThread() arm, because the
// server never had one. If these declarations were visible to the server, a
// server file could call EntityAbs_AreQueriesValid() and silently acquire a
// worker-thread behaviour the server has never had; with the guard it is a
// compile error naming the right thing.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL

// @MULTICORE: all six are no-ops or answer `true` off the main thread --
// "always allow in worker threads, assume higher level code is handling
// correctly". That rule lives in the bodies rather than in the storage above,
// because the storage is shared with the server and the rule is not.
void EntityAbs_SetQueriesValid( bool bValid );
bool EntityAbs_AreQueriesValid();

// Push/Pop bracket a block of code; the eight-deep save stack they use is a
// file-static in c_baseentity.cpp and does not move. Overflow and underflow
// both Assert and change nothing, which is what they did as members.
void EntityAbs_PushEnableRecomputations( bool bEnable );
void EntityAbs_PopEnableRecomputations();

// Frame-level only -- Asserts that no Push is outstanding.
void EntityAbs_SetRecomputationsEnabled( bool bEnable );
bool EntityAbs_AreRecomputationsEnabled();

#endif // CLIENT_DLL

#endif // ENTITY_ABS_QUERY_STATE_H
