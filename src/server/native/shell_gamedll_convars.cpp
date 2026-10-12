//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// Purpose: The one engine ConVar handle the server reads through a global.

// SOURCESHARP: this is what survives of game/server/game.cpp, whose row Phase
// 1d deleted from server_csharp.vpc -- and, since plan_shim.md Phase 2c, it is
// all that survives.
//
// WHAT LEFT WITH THE ROW IN PHASE 1d, and why it was dead:
//
//   CGameDLL_ConVarAccessor + InitializeCvars' ConVar_Register call. The shell
//   has the same accessor -- shell_gamedll.cpp's RegisterConCommandBase
//   override, which its banner already describes as "stock's
//   CGameDLL_ConVarAccessor, transliterated". Two accessors is one too many,
//   and WHICH of the two was dead was a matter of call order rather than of
//   content:
//
//     DLLInit                        shell_gamedll.cpp:193
//       -> SourceSharp_AcquireEngineInterfaces
//         -> InitializeCvars         shell_engineboundary.cpp:199
//           -> ConVar_Register( FCVAR_GAMEDLL, &g_ConVarAccessor )   <-- first
//     ...
//     ConVar_Register( FCVAR_GAMEDLL, this )                         <-- :258
//
//   tier1/convar.cpp:63 returns immediately when s_bRegistered is already set,
//   so the SECOND of those two calls had always been the no-op and the shell's
//   override had never run. Deleting game.cpp inverted that: :258 became the
//   first and only registration, and the shell's copy became the live one.
//
// WHAT LEFT IN PHASE 2c: THE SEVENTEEN DECLARATIONS.
//
//   They are SourceSharp.Server/GameDllConVars.cs now, seventeen [ConVar]
//   classes with stock's names, defaults, flags and help text. Phase 1d kept
//   them here because a ConVar is reached from outside this DLL BY NAME and no
//   link-line census can see such a reader; the managed RegisterConVar
//   constructs a real native ConVar in the engine's registry, so every one of
//   those readers still resolves:
//
//     displaysoundlist   SourceSharp.Server/SoundEnt.cs:623, FindConVarValue
//     mapcyclefile       the ENGINE (remote-access map-cycle queries)
//     mp_teamplay        game/mod_test/cfg-seed/settings.scr:79, 86, 93, 100, 107
//     mp_weaponstay      -- the dedicated server's settings dialog
//     mp_forcerespawn
//     mp_footsteps
//     mp_flashlight
//
//   THE THREE NATIVE READERS plan_shim.md 7 SCHEDULED THIS MOVE BEHIND DO NOT
//   EXIST. That section names displaysoundlist <- shell_soundent.cpp, footsteps
//   <- a file Phase 1b deleted, and suitvolume <- shell_sentences.cpp, and puts
//   the move in Phase 5 to wait for them. Re-measured on this branch over the
//   debug link line (tools/shim-census, 118 server objects, 0 ghosts, joined
//   per symbol with nm -C -u), this object has exactly three importers:
//
//     physics.o             g_pDeveloper
//     shell_soundscape.o    g_pDeveloper
//     shell_engineboundary.o   _Z15InitializeCvarsv
//
//   Not one of the seventeen ConVar symbols is imported by anything. The two
//   files 7 names mention them in COMMENTS only -- shell_soundent.cpp:64 and
//   shell_sentences.cpp:67 -- which is exactly the shape a symbol census cannot
//   tell from a reader, and exactly why this was re-measured rather than taken
//   from the plan.
//
//   MapCycleFileChangedCallback WENT WITH THEM AND IS NOT RE-CREATED MANAGED.
//   Its whole body was GameRules()->ResetMapCycleTimeStamp(), and the only
//   gamerules this build has declares that method as an empty override
//   (shell_gamerules.h:326). The callback fired into nothing. A managed
//   OnChanged calling nothing would be a copy of a no-op wearing the look of a
//   preserved behaviour.
//
// WHAT IS STILL HERE, and it is one pointer.
//
//   g_pDeveloper. physics.cpp:2152 and shell_soundscape.cpp:110 dereference it,
//   `developer` is ENGINE-owned so FindVar answers it whether or not this DLL
//   has registered yet, and InitializeCvars keeps its name and its one call
//   site (shell_engineboundary.cpp:199) because that is where stock's DLLInit
//   called it.

#include "icvar.h"


// Engine Cvars
const ConVar	*g_pDeveloper = NULL;

//-----------------------------------------------------------------------------
// Called once from SourceSharp_AcquireEngineInterfaces
// (shell_engineboundary.cpp:199), which is where stock's DLLInit called it.
//
// It registers nothing: ConVar_Register at shell_gamedll.cpp:258 does that, for
// every ConVar in this DLL, and it is the only such call left.
//-----------------------------------------------------------------------------
void InitializeCvars( void )
{
	g_pDeveloper	= cvar->FindVar( "developer" );
}
