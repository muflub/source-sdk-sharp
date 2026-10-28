//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — plan_entity.md step 7f =========================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// THE CLIENT UTILITY LIBRARY: what `game/client/cdll_util.cpp` still owed the
// shim, moved whole, with the declarations left where they were so not one
// caller changed:
//
//     ScreenWidth()  ScreenHeight()  TextMessageGet()  VarArgs()
//
// Every one is client-DLL mechanism the engine and VGUI drive, which
// plan_entity.md §6 lists as legitimately native — the screen size comes from
// the surface, the text message from the engine, and `VarArgs` is a printf
// into a static buffer. None of them decides anything about what the game
// does.
//
// WHAT LEFT (lane M1-B, 2026-10-25). The vision-flag family
// (UpdateLocalPlayerVisionFlags, its two counters, the reader and the
// predicate), g_MakingDevShots, the shared-random family, CheckEntityVelocity,
// CheckEmitReasonablePhysicsSpew, EntityTracer_ComputeStartPosition and
// UTIL_ParticleTracer had no importer on client.so's link edge (nm -u over
// its objects and the two .so files it links), and the vision family's one
// call read C_BasePlayer::GetLocalPlayer(), which answered NULL. Deleted
// rather than ruled: code nothing calls decides nothing.
//
//=======================================================================//

#include "cbase.h"

#include "cdll_util.h"
#include "gamerules.h"
#include "hud.h"
#include "itextmessage.h"
// The block from util_shared_library.cpp at the end of this file: the managed
// violence convars.
#include "managed_convar_ref.h"
// The sound block from sound_emitter.cpp at the end of this file.
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
extern ISoundEmitterSystemBase *soundemitterbase;   // shell_clientdll.cpp

#include <stdarg.h>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// Purpose: Performs a var args printf into a static return buffer
// Input  : *format -
//			... -
// Output : char
//-----------------------------------------------------------------------------
char *VarArgs( const char *format, ... )
{
	va_list		argptr;
	static char		string[1024];

	va_start (argptr, format);
	Q_vsnprintf (string, sizeof( string ), format,argptr);
	va_end (argptr);

	return string;
}

//-----------------------------------------------------------------------------
// localplayer_visionflags: the engine-protocol name the post-process plan and
// the managed vision filter read by name (ConVarRef). Its stock writer,
// IsLocalPlayerUsingVisionFilterFlags, and the two counters it read went with
// C_BasePlayer::GetLocalPlayer, which answered NULL here: no object imported
// any of them (nm -u over client.so's link edge, lane M1-B).
//-----------------------------------------------------------------------------
ConVar localplayer_visionflags( "localplayer_visionflags", "0", FCVAR_DEVELOPMENTONLY );

//-----------------------------------------------------------------------------
// The engine's own message table. A one-line passthrough, and it is here
// rather than managed for the reason §6 gives: it is resolved by the engine
// interface and returns a pointer into the engine's own storage.
//-----------------------------------------------------------------------------
client_textmessage_t *TextMessageGet( const char *pName )
{
	return engine->TextMessageGet( pName );
}

//-----------------------------------------------------------------------------
// Purpose: ScreenHeight returns the height of the screen, in pixels
// Output : int
//-----------------------------------------------------------------------------
int ScreenHeight( void )
{
	int w, h;
	GetHudSize( w, h );
	return h;
}

//-----------------------------------------------------------------------------
// Purpose: ScreenWidth returns the width of the screen, in pixels
// Output : int
//-----------------------------------------------------------------------------
int ScreenWidth( void )
{
	int w, h;
	GetHudSize( w, h );
	return w;
}

//=============================================================================//
// FROM util_shared_library.cpp, WHERE THEY WERE #ifdef CLIENT_DLL.
//
// plan_server_native_v2 T2 (2026-10-18): a shared TU keeps only what both DLLs
// import (user ruling 2026-10-12), and these were compiled only into the
// client. Moved verbatim, between two files of the same relocated-Valve-code
// ruling. The four violence refs are this file's own: the shared file keeps
// its four for UTIL_ShouldShowBlood, which the server calls.
//=============================================================================//

static CManagedConVarRef	violence_hblood( "violence_hblood" );
static CManagedConVarRef	violence_hgibs( "violence_hgibs" );
static CManagedConVarRef	violence_ablood( "violence_ablood" );
static CManagedConVarRef	violence_agibs( "violence_agibs" );

bool UTIL_IsLowViolence( void )
{
	// These convars are no longer necessary -- the engine is the final arbiter of
	// violence settings -- but they're here for legacy support and for testing low
	// violence when the engine is in normal violence mode.
	if ( !violence_hblood.GetBool() || !violence_ablood.GetBool() || !violence_hgibs.GetBool() || !violence_agibs.GetBool() )
		return true;

	return engine->IsLowViolence();
}

//=============================================================================//
// FROM sound_emitter.cpp, WHERE IT WAS #ifdef CLIENT_DLL (plan_server_native_v2
// T2, 2026-10-18): ReloadSoundEntriesInList called CSoundEmitterSystem's member
// of that name, a one-line forwarder to soundemitterbase with no other caller,
// so it makes that call itself and the member went. The other five that moved
// here with it -- GameSound_Emit's name-and-handle overload, the handle overload
// of GameSound_GetParameters, GameSound_Prefetch, SENTENCEG_Lookup and
// Hack_FixEscapeChars -- left when the client's emit path went to C#
// (SourceSharp.Client.Sounds): no client object imports any of them.
//=============================================================================//

void ReloadSoundEntriesInList( IFileList *pFilesToReload )
{
	soundemitterbase->ReloadSoundEntriesInList( pFilesToReload );
}
