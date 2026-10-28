//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON b  an interface resolved by name at load
//
// IShimConVarsClient's two slots.
//
// Watch: tier1 calls every installed global change callback for every convar,
// so a watched name is forwarded to managed's ConVarChanged under the id this
// answered. The table survives a managed reload and never shrinks.
//
// Values: floats managed decided, stored under names native code reads without
// crossing.

#include "tier1/strtools.h"
#include "icvar.h"
#include "sourcesharp_entrypoints.h"
#include "shell_convars_client.h"

namespace
{
const int kMaxWatched = 32;
const int kNameBytes = 64;

// The watched names; an id is SOURCESHARP_CONVAR_WATCH_ID_BASE + the index.
char g_szWatched[ kMaxWatched ][ kNameBytes ];
int g_nWatched = 0;

// The decided values: a name and its float, set by managed.
struct DecidedValue_t
{
	char szName[ kNameBytes ];
	float flValue;
};
const int kMaxDecided = 8;
DecidedValue_t g_Decided[ kMaxDecided ];
int g_nDecided = 0;
} // namespace

int32_t SourceSharp_ConVarsClientWatch( const char *pName )
{
	if ( !pName || !pName[0] || !g_pCVar || !g_pCVar->FindVar( pName ) )
		return -1;

	for ( int i = 0; i < g_nWatched; i++ )
	{
		if ( !V_stricmp( g_szWatched[ i ], pName ) )
			return SOURCESHARP_CONVAR_WATCH_ID_BASE + i;
	}

	if ( g_nWatched >= kMaxWatched || V_strlen( pName ) >= kNameBytes )
		return -1;

	V_strncpy( g_szWatched[ g_nWatched ], pName, kNameBytes );
	SourceSharp_EnsureConVarChangeCallback();
	return SOURCESHARP_CONVAR_WATCH_ID_BASE + g_nWatched++;
}

int32_t SourceSharp_ClientWatchedConVarId( const char *pName )
{
	if ( !pName )
		return -1;
	for ( int i = 0; i < g_nWatched; i++ )
	{
		if ( !V_stricmp( g_szWatched[ i ], pName ) )
			return SOURCESHARP_CONVAR_WATCH_ID_BASE + i;
	}
	return -1;
}

int32_t SourceSharp_ConVarsClientSetValue( const char *pName, float flValue )
{
	if ( !pName || !pName[0] || V_strlen( pName ) >= kNameBytes )
		return 0;

	for ( int i = 0; i < g_nDecided; i++ )
	{
		if ( !V_stricmp( g_Decided[ i ].szName, pName ) )
		{
			g_Decided[ i ].flValue = flValue;
			return 1;
		}
	}

	if ( g_nDecided >= kMaxDecided )
		return 0;

	V_strncpy( g_Decided[ g_nDecided ].szName, pName, kNameBytes );
	g_Decided[ g_nDecided ].flValue = flValue;
	g_nDecided++;
	return 1;
}

bool SourceSharp_ClientDecidedValue( const char *pName, float *pOut )
{
	for ( int i = 0; i < g_nDecided; i++ )
	{
		if ( !V_stricmp( g_Decided[ i ].szName, pName ) )
		{
			*pOut = g_Decided[ i ].flValue;
			return true;
		}
	}
	return false;
}
