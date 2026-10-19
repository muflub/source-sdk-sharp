//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shared shell ========================================//
// SS_REASON c  static-init registration
//
// client_base.vpc and server_base.vpc are libraries, not a runnable game: they
// reference a set of symbols every mod is expected to supply. This file
// provides the ones needed by BOTH sides.
//
// These are deliberately thin. The base classes (CAmmoDef, ...)
// are already compiled into client_base/server_base, so the shells instantiate
// them rather than reimplementing anything. No HL2MP or SDK-template code is
// involved. See plan.md Part 8.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#else
#include "tier1/convar.h"
#include "tier0/dbg.h"
#endif
#ifdef CLIENT_DLL
#include "igamemovement.h"
#else
#include "ss_gamemovement.h"	// the server's CMoveData / IGameMovement are server.so's own
using ss::CMoveData;
using ss::IGameMovement;
#endif
#ifdef CLIENT_DLL
// The server has no IPredictionSystem: its effects and temp entities read the
// suppress host through ss::TePrediction (ss_te_prediction.h).
#include "ipredictionsystem.h"
#endif
#ifdef CLIENT_DLL
#include "usermessages.h"
#endif
#include "shell_diagnostics.h"
#include "tier0/icommandline.h"
#include "ss_string.h"
#include "ss_command_line.h"
#include "filesystem.h"	// FilesSizeOnPath / FilesReadOnPath
extern IFileSystem *filesystem;	// each DLL's own (shell_engineboundary.cpp, shell_clientdll.cpp)


//-----------------------------------------------------------------------------
// Default field of view. Referenced by view/vehicle code in the base.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// STAYS NATIVE, and this is the reason rather than an oversight.
//
// §6's rule is that no ConVar's default or semantics is declared natively, and
// this lane moved forty-three of the shim's declarations to managed code for
// exactly that reason. This one cannot follow them YET, and the obstacle is a
// link error rather than a judgement: game/client/view.cpp holds
//
//     extern ConVar default_fov;
//
// and reads it three times (view.cpp:317, 584, 689). An `extern ConVar` needs a
// native ConVar OBJECT of that name at link time, and a managed `[ConVar]`
// declaration does not provide one. Moved anyway as a probe, the client link
// failed with `view.o: undefined reference to \`default_fov'`.
//
// view.cpp is a Phase 7 file and this lane does not edit it. The move is one
// retype of those three reads away -- `extern ConVar` to a by-name ref -- and
// that ask is written down in
// .claude/entity-port/requests/policy-knobs.md rather than done here.
//
// Kept on BOTH sides deliberately. It is FCVAR_REPLICATED, so the two DLLs'
// declarations are partners; moving the server's while the client's stayed
// would split a replicated pair across two mechanisms for no gain.
//
// THE SERVER'S HALF MOVED ANYWAY, plan_server_novalve.md P2.3b: server.so is to
// declare no ConVar natively, and nothing on the server link line reads this
// one (view.cpp is client-only). SourceSharp.Server.DefaultFovConVar carries
// the same name, default and flags, as the movement ConVars' server halves
// already do against movevars_shared.cpp's client ones.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
ConVar default_fov( "default_fov", "90", FCVAR_REPLICATED | FCVAR_CHEAT,
	"Default field of view." );
#endif

//-----------------------------------------------------------------------------
// Movement. IT LEFT THIS FILE AT STEP 8f-igamemovement, and the two lines that
// were here are the reason `gamemovement.cpp` could not leave the build.
//
// They were `static CGameMovement g_GameMovement;` and an
// EXPOSE_SINGLE_INTERFACE_GLOBALVAR over it, so the interface the engine
// resolves by name was tied to a class 6,040 lines of stock movement defined --
// which `tools/4a-owes gamemovement` reported as two of the six symbols the shim
// owed that file. Both are now
// sourcesharp/game/shared/shell_gamemovement.cpp's, over a standalone
// CShellGameMovement that implements IGameMovement itself and answers from the
// managed loop. `g_pGameMovement` is defined there too.
//-----------------------------------------------------------------------------

// Needed on BOTH sides: the server runs player movement, and the client
// re-runs it during prediction (prediction.cpp references this directly).
static CMoveData g_MoveData;
CMoveData *g_pMoveData = &g_MoveData;

// Ammo types: SourceSharp_RegisterAmmoType is the client's alone (an
// IShimStandaloneClient slot since plan_server_native_v2 T2), with its
// CAmmoDef and GetAmmoDef, in src/client/native/shell_client.cpp. The
// server's refusing body went with IShimWeapons: only the client registers.

//-----------------------------------------------------------------------------
// Weapon script parsing -- GONE from the server, and this is where it was.
//
// CreateWeaponInfo is the hook a mod overrides so weapon_parse.cpp's
// ReadWeaponDataFromFileForSlot can allocate a game-specific struct. It had
// exactly ONE caller in this build: weapon_parse.cpp:103. plan_server_all.md
// Wave 1 evicted weapon_parse.cpp from the server -- nothing named a single
// one of its symbols, and its whole database is a file-local CUtlDict nobody
// asks -- so this factory had nobody left to serve.
//
// It stays on the CLIENT, where weapon_parse_default.cpp already supplies its
// own and this file never did (the `#ifndef CLIENT_DLL` it used to sit inside
// said so).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// User messages: NOTHING HERE ANY MORE, and that is the end state.
//
// This file used to define `RegisterUserMessages()`, which stock's
// usermessages.cpp called from a file-static constructor -- before DLLInit, and
// long before the CLR existed, which is why the list could not be managed. It
// called `RegisterHapticMessages()` and nothing else after the standard set had
// already moved to C#.
//
// Phase 5a step 2 emptied the body (the six haptics are the first six
// [UserMessage] declarations now) and step 3 removed its only caller along with
// usermessages.cpp itself. Since plan_side_isolation P2 the list is C#'s
// registry, and native code only forwards to it.
//
//-----------------------------------------------------------------------------
// The voice manager's policy helper is gone. g_pVoiceGameMgrHelper's only
// reader was CVoiceGameMgr (voice_gamemgr.cpp), which neither DLL compiles; both
// globals ledgers ruled it DEAD ("the linker drops it").
//-----------------------------------------------------------------------------

// The weapon-type table (fire rate, spread, pellets per classname) went in
// plan_server_native_v2 T2 with SourceSharp_RegisterWeaponType: its three
// readers had gone, so it was written at startup and read by nothing.

//-----------------------------------------------------------------------------
// IShimEngine::FilesSizeOnPath / FilesReadOnPath: a whole file off one named
// search path ("GAME", "BSP", "MOD", ...), both sides (C9: the client's text
// parsing moves to managed, and its map particle manifest lives on "BSP").
// Open/Size/Close, because Size( path ) answers 0 for a missing file. The size
// is -1 for a file that is not there (0 is a real, empty file); the read
// answers the bytes read, or -1 for a missing file or one larger than the
// buffer (refused, never truncated).
//-----------------------------------------------------------------------------
int32_t SourceSharp_FilesSizeOnPath( const char *pszPath, const char *pszPathId )
{
	if ( !pszPath || !pszPath[0] || !pszPathId || !filesystem )
		return -1;
	FileHandle_t hFile = filesystem->Open( pszPath, "rb", pszPathId );
	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;
	const unsigned int nSize = filesystem->Size( hFile );
	filesystem->Close( hFile );
	return nSize > 0x7fffffffu ? -1 : (int32_t)nSize;
}

int32_t SourceSharp_FilesReadOnPath( const char *pszPath, const char *pszPathId, void *pOut, int32_t nOutSize )
{
	if ( !pszPath || !pszPath[0] || !pszPathId || !pOut || nOutSize < 0 || !filesystem )
		return -1;
	FileHandle_t hFile = filesystem->Open( pszPath, "rb", pszPathId );
	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;
	const unsigned int nSize = filesystem->Size( hFile );
	if ( nSize > (unsigned int)nOutSize )
	{
		filesystem->Close( hFile );
		return -1;
	}
	const int nRead = filesystem->Read( pOut, (int)nSize, hFile );
	filesystem->Close( hFile );
	return nRead;
}
