//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Client utility functions: screen size, the engine's text messages and VarArgs.
// SS_REASON h  the CoreCLR host and bootstrap dispatch

#include <stdio.h>
#include <stdlib.h>
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "hud.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier1/convar.h"
#include "tier1/iconvar.h"
#include "tier1/strtools.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "client_entity.h"
#include "eiface.h"

#include "managed_convar_ref.h"
extern ISoundEmitterSystemBase *soundemitterbase;

#include <stdarg.h>

// A printf into a static buffer.
char *VarArgs( const char *format, ... )
{
	va_list		argptr;
	static char		string[1024];

	va_start (argptr, format);
	Q_vsnprintf (string, sizeof( string ), format,argptr);
	va_end (argptr);

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

static CManagedConVarRef	violence_hblood( "violence_hblood" );
static CManagedConVarRef	violence_hgibs( "violence_hgibs" );
static CManagedConVarRef	violence_ablood( "violence_ablood" );
static CManagedConVarRef	violence_agibs( "violence_agibs" );

bool UTIL_IsLowViolence()
{
	// These convars are kept for legacy support and for testing low violence when the
	// engine is in normal violence mode; the engine is the final arbiter.
	if ( !violence_hblood.GetBool() || !violence_ablood.GetBool() || !violence_hgibs.GetBool() || !violence_agibs.GetBool() )
		return true;

	return engine->IsLowViolence();
}

void ReloadSoundEntriesInList( IFileList *pFilesToReload )
{
	soundemitterbase->ReloadSoundEntriesInList( pFilesToReload );
}
