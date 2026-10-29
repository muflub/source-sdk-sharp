//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON c  static-init registration

// The two sides have different cbase.h files, and some of these headers are not
// on the server include path.
#ifdef CLIENT_DLL
#include "client_class.h"
#include "movevars_shared.h"
#else
// The server half reads one managed convar.
#include "managed_convar_ref.h"
float GetCurrentGravity();
#endif

// some cvars used by player movement system
// Stock's non-HL2 default.
#define DEFAULT_GRAVITY_STRING "800"

float GetCurrentGravity()
{
#ifdef CLIENT_DLL
	return sv_gravity.GetFloat();
#else
	// The server's sv_gravity is declared in C#.
	static CManagedConVarRef s_Gravity( "sv_gravity" );
	return s_Gravity.GetFloat();
#endif
}

// The movement area's slot, on both sides, so neither answers a plausible managed fallback.
float SourceSharp_CurrentGravity()
{
	return GetCurrentGravity();
}

// The server declares these in C#.
#ifdef CLIENT_DLL
ConVar sv_gravity( "sv_gravity", DEFAULT_GRAVITY_STRING, FCVAR_NOTIFY | FCVAR_REPLICATED, "World gravity." );

ConVar sv_stopspeed( "sv_stopspeed", "100", FCVAR_NOTIFY | FCVAR_REPLICATED, "Minimum stopping speed when on ground." );

ConVar sv_noclipaccelerate( "sv_noclipaccelerate", "5", FCVAR_NOTIFY | FCVAR_ARCHIVE | FCVAR_REPLICATED );
ConVar sv_noclipspeed( "sv_noclipspeed", "5", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED );
ConVar sv_specaccelerate( "sv_specaccelerate", "5", FCVAR_NOTIFY | FCVAR_ARCHIVE | FCVAR_REPLICATED );
ConVar sv_specspeed( "sv_specspeed", "3", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED );
ConVar sv_specnoclip( "sv_specnoclip", "1", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED );

ConVar sv_maxspeed( "sv_maxspeed", "320", FCVAR_NOTIFY | FCVAR_REPLICATED );

// Stock's PC value; a shim TU carries no guard this build never takes.
ConVar sv_accelerate( "sv_accelerate", "10", FCVAR_NOTIFY | FCVAR_REPLICATED );

ConVar sv_airaccelerate( "sv_airaccelerate", "10", FCVAR_NOTIFY | FCVAR_REPLICATED );
ConVar sv_waterfriction( "sv_waterfriction", "1", FCVAR_NOTIFY | FCVAR_REPLICATED );
ConVar sv_rollspeed( "sv_rollspeed", "200", FCVAR_NOTIFY | FCVAR_REPLICATED );
ConVar sv_rollangle( "sv_rollangle", "0", FCVAR_NOTIFY | FCVAR_REPLICATED, "Max view roll angle" );

ConVar sv_friction( "sv_friction", "4", FCVAR_NOTIFY | FCVAR_REPLICATED, "World friction." );

ConVar sv_bounce( "sv_bounce", "0", FCVAR_NOTIFY | FCVAR_REPLICATED,
				  "Bounce multiplier for when physically simulated objects collide with other objects." );
ConVar sv_maxvelocity( "sv_maxvelocity", "3500", FCVAR_REPLICATED,
					   "Maximum speed any ballistically moving object is allowed to attain per axis." );
ConVar sv_stepsize( "sv_stepsize", "18", FCVAR_NOTIFY | FCVAR_REPLICATED );

ConVar sv_skyname( "sv_skyname", "sky_urb01", FCVAR_ARCHIVE | FCVAR_REPLICATED, "Current name of the skybox texture" );
#endif // CLIENT_DLL
