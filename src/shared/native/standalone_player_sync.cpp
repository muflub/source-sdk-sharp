//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// Compiled into both DLLs, so the field list and byte arithmetic are the same code.

#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
// The client's player hierarchy completes C_FogController for playernet_vars.h.
#include "client_entity.h"
#endif
#if defined( CLIENT_DLL )
#include "c_playerlocaldata.h"
#else
#include "ss_playerlocaldata.h"
#endif
#include "PlayerState.h"
#include "standalone_player_sync.h"

// One warning per failed resolve attempt, not per field.
static int ResolveOffset( const SourceSharpNetStateAsk_t &ask, const char *pField, bool *pbAll )
{
	const int nOffset = ask.FieldOffset( "player", pField );

	if ( nOffset < 0 )
		*pbAll = false;

	return nOffset;
}

static SourceSharpPackedField_t ResolvePacked( const SourceSharpNetStateAsk_t &ask, const char *pField, bool *pbAll )
{
	const int nOffset = ask.FieldOffset( "player", pField );
	const int nBits = ask.FieldBits( "player", pField );

	SourceSharpPackedField_t field = SourceSharpPayload_Place( nOffset, nBits );

	if ( !field.IsResolved() )
		*pbAll = false;

	return field;
}

bool SourceSharpPlayerFields_Resolve( SourceSharpPlayerFields_t *pFields, unsigned int nGeneration,
									  NetStateArea_t *pArea )
{
	if ( !pFields || !pArea )
		return false;

	const SourceSharpNetStateAsk_t ask = { *pArea };

	bool bAll = true;

	pFields->nHealth = ResolveOffset( ask, "Health", &bAll );
	pFields->nMaxSpeed = ResolveOffset( ask, "MaxSpeed", &bAll );
	pFields->nFallVelocity = ResolveOffset( ask, "FallVelocity", &bAll );
	pFields->nDuckTime = ResolveOffset( ask, "DuckTime", &bAll );
	pFields->nDuckJumpTime = ResolveOffset( ask, "DuckJumpTime", &bAll );
	pFields->nJumpTime = ResolveOffset( ask, "JumpTime", &bAll );
	pFields->nStepSize = ResolveOffset( ask, "StepSize", &bAll );
	pFields->nWaterJumpTime = ResolveOffset( ask, "WaterJumpTime", &bAll );
	pFields->nSurfaceFriction = ResolveOffset( ask, "SurfaceFriction", &bAll );
	pFields->nOldForwardMove = ResolveOffset( ask, "OldForwardMove", &bAll );
	pFields->nFlags = ResolveOffset( ask, "Flags", &bAll );
	pFields->nOldButtons = ResolveOffset( ask, "OldButtons", &bAll );
	pFields->nTickBase = ResolveOffset( ask, "TickBase", &bAll );
	pFields->nGroundEntity = ResolveOffset( ask, "GroundEntity", &bAll );

	pFields->Alive = ResolvePacked( ask, "Alive", &bAll );
	pFields->Ducked = ResolvePacked( ask, "Ducked", &bAll );
	pFields->Ducking = ResolvePacked( ask, "Ducking", &bAll );
	pFields->InDuckJump = ResolvePacked( ask, "InDuckJump", &bAll );
	pFields->AllowAutoMovement = ResolvePacked( ask, "AllowAutoMovement", &bAll );
	pFields->WaterLevel = ResolvePacked( ask, "WaterLevel", &bAll );
	pFields->WaterType = ResolvePacked( ask, "WaterType", &bAll );
	pFields->MoveType = ResolvePacked( ask, "MoveType", &bAll );

	pFields->nOrigin = ResolveOffset( ask, "Origin", &bAll );
	pFields->nVelocity = ResolveOffset( ask, "Velocity", &bAll );
	pFields->nBaseVelocity = ResolveOffset( ask, "BaseVelocity", &bAll );
	pFields->nViewOffset = ResolveOffset( ask, "ViewOffset", &bAll );
	pFields->nPunchAngle = ResolveOffset( ask, "PunchAngle", &bAll );
	pFields->nPunchAngleVel = ResolveOffset( ask, "PunchAngleVel", &bAll );
	pFields->nLadderNormal = ResolveOffset( ask, "LadderNormal", &bAll );
	pFields->nWaterJumpVel = ResolveOffset( ask, "WaterJumpVel", &bAll );

	if ( !bAll )
	{
		Warning( "[SourceSharp] player sync: player's fields did not all resolve — "
				 "is PlayerNetState published on this side?\n" );
		return false;
	}

	pFields->nGeneration = nGeneration;
	return true;
}

// memcpy per field: the blob's layout is C#'s, so nothing may assume adjacency.
static inline void WriteI32( unsigned char *pBlob, int nOffset, int nValue )
{
	if ( nOffset >= 0 )
		memcpy( pBlob + nOffset, &nValue, sizeof( nValue ) );
}

static inline void WriteVec( unsigned char *pBlob, int nOffset, const Vector &vec )
{
	if ( nOffset >= 0 )
	{
		float xyz[3] = { vec.x, vec.y, vec.z };
		memcpy( pBlob + nOffset, xyz, sizeof( xyz ) );
	}
}

#ifndef CLIENT_DLL
void SourceSharpPlayerSync_WriteEngineSide( unsigned char *pBlob, const SourceSharpPlayerFields_t &fields,
											unsigned int nGeneration, const Vector &vecOrigin, int nHealth,
											bool bAlive )
{
	if ( !pBlob || !fields.IsCurrent( nGeneration ) )
		return;

	WriteI32( pBlob, fields.nHealth, nHealth );
	SourceSharpPayload_WritePacked( pBlob, fields.Alive, bAlive ? 1u : 0u );
	WriteVec( pBlob, fields.nOrigin, vecOrigin );
}
#endif
