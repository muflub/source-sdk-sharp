//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON c  static-init registration
// SOURCESHARP: RELOCATED, NOT MOVED TO MANAGED CODE. plan_shim.md Phase 2c.
// REASON (c) -- static-init registration. Twenty-nine ConVar objects built by
// static initialisers, before this DLL's ConVar_Register call. That is the one
// thing a managed [ConVar] cannot be: ConVarRegistry.RegisterAll runs during
// managed startup, which is inside DLLInit and therefore after it.
// THE MANAGED PORT WAS WRITTEN AND IS NOT SHIPPED, and the reason is that the
// only instrument available could not certify it. What it measures is
// sv_gravity: this mod's movement tuning is 600 and
// SourceSharp_RegisterMovementParams writes it into the convar ONCE, during
// managed startup (shell_movement.cpp:213), deliberately as a default so that
// an operator's config -- read afterwards -- keeps what it sets. Read back off
// a live dm_lockdown with ./debug-api convar, that value is NOT STABLE ACROSS
// BUILDS of declarations that are identical:
//     c053cefe3, declarations native            sv_gravity = 600.000000
//     the same declarations, this file relocated sv_gravity = 800   (x3 runs)
// 800 is the declared default, so in the second build the movement parameter
// is being thrown away between the write and the first frame. NOTHING IN THIS
// COMMIT CHANGES A DECLARATION -- the file moved and gained this comment -- so
// whatever decides it is link or static-init order, and a port that also moves
// the declarations cannot be told from it. The move is therefore not made on
// an instrument that reads differently for two builds of the same code.
// WHAT IS SETTLED, and is NOT the reason this stayed: a managed declaration
// does carry FCVAR_REPLICATED and the engine does replicate it to a REMOTE
// client. Measured on a live two-process join, with the newcomer counted on the
// server (1 human -> 2): mp_forcecamera and mp_friendlyfire -- managed server
// half, native client half, which is exactly the shape this file would have
// taken -- both moved on the client, beside a native positive control
// (sv_gravity) and two negative controls that did not move (mp_teamplay,
// cs_sv_spawn_health). ConVars.cs:6-8's caution is about ASSEMBLY PLACEMENT, a
// declaration in Shared registering from both DLLs, and not about the flag.
// WHAT WAS TRIED AND MADE IT WORSE, recorded so the next lane does not repeat
// it. A managed declaration carries neither FCVAR_GAMEDLL nor FCVAR_CLIENTDLL
// exist when it runs -- and the engine says so once per convar:
//     For FCVAR_REPLICATED, ConVar must be defined in client and game .dlls (%s)
// Five of those are printed by every run of this build today, for the five
// replicated convars GameVars.cs declares. Adding the bit inside
// SourceSharp_EngineRegisterConVar silenced those five and raised the total
// from 5 to 29 by making every convar in THIS file start failing the same
// check. Measured, reverted, not shipped.
// FOR PHASE 5, WHICH OWNS THE CONVAR LEDGER: what has to be pinned before any
// of this file moves is WHICH WRITE decides sv_gravity between managed startup
// and the first frame. The managed global change callback cannot see it --
// SourceSharp_OnConVarChanged returns early until the managed side is loaded --
// so the instrument has to be native and placed before that guard.
// THAT INSTRUMENT NOW EXISTS, and this paragraph's reading of the defect has
// been superseded. Lane 3-gravity built it -- shell_movement_audit.{h,cpp},
// called from SourceSharp_OnConVarChanged ABOVE the guard -- and diagnosed the
// rest. docs/defect-convar-order.md is the answer; the three headlines are:
//     sv_gravity declaration in the build. 600 is a runtime write, so 800 is
//     simply what the convar holds whenever that write does not survive.
//     three of this very commit in both build modes, read 600. It is neither
//     between-build nor within-build; what produced the original three
//     readings is named as unsettled, with the experiment that would settle it.
//     the engine's link check: a managed declaration carries neither FCVAR_GAMEDLL nor
//     FCVAR_CLIENTDLL, so moving THIS FILE'S SERVER HALF alone leaves the
//     client's twenty-nine unlinked orphans holding 800 while the server holds
//     600 -- a prediction bug, silent, in one process. Move both halves in one
//     commit or leave the declaration here.


// step 7h: this file is compiled into BOTH DLLs, and the two sides have
// DIFFERENT cbase.h files. The client half of the step is done here -- the
// client TU names what it uses and is opted out of the client precompiled
// header in client_csharp.vpc. The server half is on the R5 include set
// (plan_server_novalve.md P5): it sees no cbase.h and no game/ header. Two of these
// headers (cdll_util.h, icliententityinternal.h) are under game/client, which
// is not on the server include path at all, so the guard is not tidiness.
#ifdef CLIENT_DLL
#include "client_class.h"
#include "movevars_shared.h"
#else
// P5 lane SP: the server half defines one function and reads one managed
// convar, so it includes the convar reference and declares the function
// movevars_shared.h declares (its server callers include that header).
#include "managed_convar_ref.h"
float GetCurrentGravity( void );
#endif

// stock's `#if defined( TF_CLIENT_DLL ) || defined( TF_DLL ) #include
// "tf_gamerules.h" #endif` STOOD HERE. Neither define is on either link line of
// this build, so the include was never taken; tools/dead-conditionals refuses a
// guard like that inside a shim TU, and this file is a shim TU now.


// some cvars used by player movement system
//
// THE #else ARM, WRITTEN OUT. Stock chose between "600" under
// HL2_DLL/HL2_CLIENT_DLL and "800" otherwise, and this is the ONE mod guard in
// this file whose two arms differ -- so collapsing it is a value decision and
// not a tidy-up, and the value is measured rather than assumed:
//
//     grep -oE '\-D[A-Za-z0-9_]+' src/_vpc_/ninja/sdk_csharp_debug.ninja
//         | sort -u | grep -E 'HL2|TF_|DOD|CSTRIKE|HL1MP|_XBOX'   ->  no match
//
// Neither define is on either link line, so "800" is what both DLLs have always
// compiled. See the banner for what this convar's value then does at runtime,
// which is a separate and still-open question.
#define DEFAULT_GRAVITY_STRING	"800"

float GetCurrentGravity( void )
{
	// stock's TF arm -- `sv_gravity.GetFloat() * TFGameRules()->GetGravityMultiplier()`
	// under `#if defined( TF_CLIENT_DLL ) || defined( TF_DLL )` -- is deleted
	// rather than guarded. Neither define is on either link line, tf_gamerules.h
	// is not included any more, and a shim TU may not carry a guard this build
	// never takes.
#ifdef CLIENT_DLL
	return sv_gravity.GetFloat();
#else
	// By name: the server's sv_gravity is C#'s declaration
	// (SourceSharp.Server/MoveVars.cs, plan_server_novalve.md P2.3).
	static CManagedConVarRef s_Gravity( "sv_gravity" );
	return s_Gravity.GetFloat();
#endif
}

// CLIENT ONLY FROM HERE TO THE #endif (plan_server_novalve.md P2.3). server.so
// declares these eighteen in C# (SourceSharp.Server/MoveVars.cs), with the same
// names, defaults, flags and help, and reads the few it still reads by name.
#ifdef CLIENT_DLL
ConVar	sv_gravity		( "sv_gravity", DEFAULT_GRAVITY_STRING, FCVAR_NOTIFY | FCVAR_REPLICATED, "World gravity." );

ConVar	sv_stopspeed	( "sv_stopspeed","100", FCVAR_NOTIFY | FCVAR_REPLICATED, "Minimum stopping speed when on ground." );

ConVar	sv_noclipaccelerate( "sv_noclipaccelerate", "5", FCVAR_NOTIFY | FCVAR_ARCHIVE | FCVAR_REPLICATED);
ConVar	sv_noclipspeed	( "sv_noclipspeed", "5", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED);
ConVar	sv_specaccelerate( "sv_specaccelerate", "5", FCVAR_NOTIFY | FCVAR_ARCHIVE | FCVAR_REPLICATED);
ConVar	sv_specspeed	( "sv_specspeed", "3", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED);
ConVar	sv_specnoclip	( "sv_specnoclip", "1", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED);

ConVar	sv_maxspeed		( "sv_maxspeed", "320", FCVAR_NOTIFY | FCVAR_REPLICATED);

// stock's `#ifdef _XBOX` arm declared sv_accelerate at "7". There is no _XBOX
// build here and there has not been one for a decade; the #else arm is what
// both DLLs compile, and a shim TU may not carry a guard this build never takes.
ConVar	sv_accelerate	( "sv_accelerate", "10", FCVAR_NOTIFY | FCVAR_REPLICATED);

ConVar	sv_airaccelerate(  "sv_airaccelerate", "10", FCVAR_NOTIFY | FCVAR_REPLICATED );    
ConVar	sv_waterfriction(  "sv_waterfriction", "1", FCVAR_NOTIFY | FCVAR_REPLICATED );      
ConVar	sv_rollspeed	( "sv_rollspeed", "200", FCVAR_NOTIFY | FCVAR_REPLICATED);
ConVar	sv_rollangle	( "sv_rollangle", "0", FCVAR_NOTIFY | FCVAR_REPLICATED, "Max view roll angle");

ConVar	sv_friction		( "sv_friction","4", FCVAR_NOTIFY | FCVAR_REPLICATED, "World friction." );

ConVar	sv_bounce		( "sv_bounce","0", FCVAR_NOTIFY | FCVAR_REPLICATED, "Bounce multiplier for when physically simulated objects collide with other objects." );
ConVar	sv_maxvelocity	( "sv_maxvelocity","3500", FCVAR_REPLICATED, "Maximum speed any ballistically moving object is allowed to attain per axis." );
ConVar	sv_stepsize		( "sv_stepsize","18", FCVAR_NOTIFY | FCVAR_REPLICATED );

ConVar	sv_skyname		( "sv_skyname", "sky_urb01", FCVAR_ARCHIVE | FCVAR_REPLICATED, "Current name of the skybox texture" );
#endif // CLIENT_DLL

// ELEVEN OF THE TWENTY-NINE LEFT HERE, plan_shim.md Phase 5 (lane 5d).
// Eighteen remain: seventeen with live native readers, and sv_skyname,
// which the engine resolves by name and which therefore stays for good.
//
// SourceSharp.Server/MoveVars.cs and SourceSharp.Client/MoveVars.cs declare
// them now -- both halves in one commit, because a replicated ConVar declared
// on one side only is a prediction bug that no compile or link step sees.
//
// WHAT SEPARATES THE TWELVE FROM THE SEVENTEEN STILL HERE is not the flag, the
// default or the meaning. It is whether a translation unit ON A LINK LINE still
// reads the declaration through movevars_shared.h's extern. Measured on this
// branch over the debug ninja graph's own build lines (322 sources), the eleven
// have ZERO such readers -- the header's extern was the only mention of each
// anywhere the build compiles. The seventeen that stay are read by
// shell_movement.cpp, shell_movement_audit.cpp, world_adapter.cpp,
// shell_gamerules.cpp and shell_motioncontrollers.cpp, and each of those reads
// has to become a CManagedConVarRef before its declaration can follow.
//
// THE BLOCKER THIS FILE'S BANNER RECORDS IS GONE. docs/defect-convar-order.md
// found that a managed declaration carried neither FCVAR_GAMEDLL nor
// FCVAR_CLIENTDLL, so the engine refused the client/server link and a
// moved convar sat unlinked at its own default. SourceSharp_EngineRegisterConVar
// ORs the right bit in now (sourcesharp_gamesystem.cpp:699,701).
//
// r_JeepViewZHeight, r_AirboatViewZHeight, r_VehicleViewDampen,
// r_JeepViewDampenFreq, r_JeepViewDampenDamp, r_AirboatViewDampenFreq,
// r_AirboatViewDampenDamp, sv_backspeed, sv_footsteps, sv_wateraccelerate and
// sv_waterdist were declared between here and sv_skyname above.
