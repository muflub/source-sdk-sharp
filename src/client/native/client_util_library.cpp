//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — plan_entity.md step 7f =========================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// THE CLIENT UTILITY LIBRARY: the six things `game/client/cdll_util.cpp`
// still owed the shim, moved whole, with their declarations left where they
// were so not one caller changed.
//
// WHY THIS FILE EXISTS. Step 7f's gate is "cross-edges zero on the client"
// over the shared movement and collision residue, and `cdll_util.cpp` held six
// of them:
//
//     ScreenWidth()  ScreenHeight()  TextMessageGet()  VarArgs()
//     UpdateLocalPlayerVisionFlags()  g_MakingDevShots
//
// Every one is client-DLL mechanism the engine and VGUI drive, which
// plan_entity.md §6 lists as legitimately native — the screen size comes from
// the surface, the text message from the engine, and `VarArgs` is a printf
// into a static buffer. None of them decides anything about what the game
// does, so none is policy this step could have moved into managed code
// instead; what they were was STOCK-OWNED, and that is what changes here.
//
// THE VISION-FLAG FAMILY MOVES WHOLE, and that is not tidiness. Its two
// counters are file statics:
//
//     static int g_nLocalPlayerVisionFlagsWeaponsCheck;
//     static int g_nLocalPlayerVisionFlags;
//
// `UpdateLocalPlayerVisionFlags` writes them and `GetLocalPlayerVisionFilterFlags`
// reads them. Moving the writer alone would have meant EXPORTING the two
// statics so the reader could still see them — which trades one cross-edge for
// two and puts a mutable global on the boundary. The reader, the writer, the
// predicate they serve and the ConVar the predicate publishes to therefore all
// live here.
//
// `C_BasePlayer::GetLocalPlayer()` is NOT a stock symbol in this build — it is
// defined in `sourcesharp/game/client/shell_player_orphans.cpp`, checked with
//
//     nm -C --defined-only --print-file-name <every client object> \
//       | grep 'C_BasePlayer::GetLocalPlayer'
//
// which names that shim object and no other. So the move converts no edge: the
// call was shim->stock inside cdll_util.cpp's object and is shim->shim here.
// It answers NULL in this mod anyway (the local player is C_StandalonePlayer),
// which is why both counters stay zero — that is the answer the mod already
// gets and this move does not change it.
//
//=======================================================================//

#include "cbase.h"

#include "c_baseplayer.h"
#include "cdll_util.h"
#include "gamerules.h"
#include "hud.h"
#include "itextmessage.h"

#include <stdarg.h>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// Set by the -makedevshots command line, read by cdll_util.cpp's own
// `AllowedDrawing` test. The DEFINITION moves; the `extern bool` declaration
// stays in game/client/cdll_util.h, so every reader is unchanged.
//-----------------------------------------------------------------------------
bool g_MakingDevShots = false;

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
// THE VISION-FLAG FAMILY. The ConVar comes with it because
// IsLocalPlayerUsingVisionFilterFlags is the only thing that writes it and the
// only thing that reads it back is the shader the same call publishes for.
//-----------------------------------------------------------------------------
ConVar localplayer_visionflags( "localplayer_visionflags", "0", FCVAR_DEVELOPMENTONLY );

// NOTE: cache these because this gets executed hundreds of times per frame
static int g_nLocalPlayerVisionFlagsWeaponsCheck = 0;
static int g_nLocalPlayerVisionFlags = 0;

int GetLocalPlayerVisionFilterFlags( bool bWeaponsCheck /*= false */ )
{
	return bWeaponsCheck ? g_nLocalPlayerVisionFlagsWeaponsCheck : g_nLocalPlayerVisionFlags;
}

void UpdateLocalPlayerVisionFlags()
{
	g_nLocalPlayerVisionFlagsWeaponsCheck = 0;
	g_nLocalPlayerVisionFlags = 0;
	C_BasePlayer *pPlayer = C_BasePlayer::GetLocalPlayer();
	if ( pPlayer )
	{
		g_nLocalPlayerVisionFlagsWeaponsCheck = pPlayer->GetVisionFilterFlags( true );
		g_nLocalPlayerVisionFlags = pPlayer->GetVisionFilterFlags( false );
	}
}

bool IsLocalPlayerUsingVisionFilterFlags( int nFlags, bool bWeaponsCheck /* = false */ )
{
	int nLocalPlayerFlags = GetLocalPlayerVisionFilterFlags( bWeaponsCheck );

	if ( !bWeaponsCheck )
	{
		// We can only modify the RJ flags with normal checks that won't take the forced kill cam flags that can happen in weapon checks
		int nRJShaderFlags = nLocalPlayerFlags;
		if ( nRJShaderFlags != 0 && GameRules() && !GameRules()->AllowMapVisionFilterShaders() )
		{
			nRJShaderFlags = 0;
		}

		if ( nRJShaderFlags != localplayer_visionflags.GetInt() )
		{
			localplayer_visionflags.SetValue( nRJShaderFlags );
		}
	}

	return ( nLocalPlayerFlags & nFlags ) == nFlags;
}

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
