//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Client utility functions: screen size, the engine's text messages and VarArgs.
// SS_REASON h  the CoreCLR host and bootstrap dispatch

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "hud.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

#include "client_entity.h"

#include "managed_convar_ref.h"
extern ISoundEmitterSystemBase *soundemitterbase;

// A printf into a static buffer.
char *VarArgs( const char *format, ... )
{
	va_list argptr;
	static char string[1024];

	va_start( argptr, format );
	Q_vsnprintf( string, sizeof( string ), format, argptr );
	va_end( argptr );

	return string;
}

// Read by name (ConVarRef) by the post-process plan and the managed vision filter.
ConVar localplayer_visionflags( "localplayer_visionflags", "0", FCVAR_DEVELOPMENTONLY );

client_textmessage_t *TextMessageGet( const char *pName )
{
	return engine->TextMessageGet( pName );
}

int ScreenHeight()
{
	int w, h;
	GetHudSize( w, h );
	return h;
}

int ScreenWidth()
{
	int w, h;
	GetHudSize( w, h );
	return w;
}

void ReloadSoundEntriesInList( IFileList *pFilesToReload )
{
	soundemitterbase->ReloadSoundEntriesInList( pFilesToReload );
}
