//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The player's native state and its blob. The blob's layout is a C# declaration
// (PlayerNetState); every offset is asked by field name and cached per resolve
// generation, which a managed reload bumps. A field that does not resolve syncs
// as nothing, with one warning.
#pragma once

#include "basehandle.h"
#include "imovementplayer.h"
#include "generic_payload.h"
#include "ground_handle_wire.h"
#include "sourcesharp.h"

// Bumped by a managed reload on both sides; stale field sets re-ask before use.
unsigned int SourceSharp_PlayerSyncGeneration();
void         SourceSharp_PlayerSyncInvalidate( const char *pReason );

// The resolved placements: whole-storage fields keep an offset, narrow ones the
// packed placement the C# packer chose.
struct SourceSharpPlayerFields_t
{
	unsigned int nGeneration;   // 0 = never resolved

	// Whole words.
	int nHealth, nMaxSpeed, nFallVelocity, nDuckTime, nDuckJumpTime, nJumpTime;
	int nStepSize, nWaterJumpTime, nSurfaceFriction, nOldForwardMove;
	int nFlags, nOldButtons, nTickBase, nGroundEntity;

	// Packed.
	SourceSharpPackedField_t Alive, Ducked, Ducking, InDuckJump, AllowAutoMovement;
	SourceSharpPackedField_t WaterLevel, WaterType, MoveType;

	// Wide.
	int nOrigin, nVelocity, nBaseVelocity, nViewOffset;
	int nPunchAngle, nPunchAngleVel, nLadderNormal, nWaterJumpVel;

	SourceSharpPlayerFields_t() { memset( this, 0, sizeof( *this ) ); }

	bool IsCurrent() const
	{
		return nGeneration != 0 && nGeneration == SourceSharp_PlayerSyncGeneration();
	}
};

// Asks the managed side for every placement. False, with one warning, when the
// class is not published on this side yet; the caller retries on the next sync.
//
// This side's layout asks. Both answer -1 for a field that is not published.
struct SourceSharpNetStateAsk_t
{
	int32_t ( *pfnFieldOffset )( const char *pClassName, const char *pFieldName );
	int32_t ( *pfnFieldBits )( const char *pClassName, const char *pFieldName );
};

#ifndef CLIENT_DLL
#include "imanaged_areas.h"

// Read at the point of use, never cached: the area goes with each unload.
inline int32_t SourceSharp_ServerNetStateFieldOffset( const char *pClassName, const char *pFieldName )
{
	IManagedNetState *pNetState = ManagedNetState();
	return pNetState ? pNetState->NetStateFieldOffset( pClassName, pFieldName ) : -1;
}

inline int32_t SourceSharp_ServerNetStateFieldBits( const char *pClassName, const char *pFieldName )
{
	IManagedNetState *pNetState = ManagedNetState();
	return pNetState ? pNetState->NetStateFieldBits( pClassName, pFieldName ) : -1;
}
#else
#include "imanaged_areas_client.h"

// Read at the point of use, never cached: the area goes with each unload.
inline int32_t SourceSharp_ClientNetStateFieldOffset( const char *pClassName, const char *pFieldName )
{
	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();
	return pStandalone ? pStandalone->NetStateFieldOffset( pClassName, pFieldName ) : -1;
}

inline int32_t SourceSharp_ClientNetStateFieldBits( const char *pClassName, const char *pFieldName )
{
	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();
	return pStandalone ? pStandalone->NetStateFieldBits( pClassName, pFieldName ) : -1;
}
#endif

// This side's asks into *pAsk; false, and *pAsk untouched, outside a load.
inline bool SourceSharp_NetStateAsk( SourceSharpNetStateAsk_t *pAsk )
{
#ifndef CLIENT_DLL
	if ( !pAsk || !ManagedNetState() )
		return false;
	pAsk->pfnFieldOffset = &SourceSharp_ServerNetStateFieldOffset;
	pAsk->pfnFieldBits   = &SourceSharp_ServerNetStateFieldBits;
#else
	if ( !pAsk || !ManagedStandaloneClient() )
		return false;
	pAsk->pfnFieldOffset = &SourceSharp_ClientNetStateFieldOffset;
	pAsk->pfnFieldBits   = &SourceSharp_ClientNetStateFieldBits;
#endif
	return true;
}

bool SourceSharpPlayerFields_Resolve( SourceSharpPlayerFields_t *pFields );

// The ground handle travels as an entry index word, never a resolved pointer.
// Encode only: each side resolves the index through its own entity list. The
// asserts check ground_handle_wire.h's restated widths; they sit inside the body
// because COMPILE_TIME_ASSERT declares a fixed name.
inline int SourceSharpPlayerSync_EncodeGround( const CBaseHandle &handle )
{
	COMPILE_TIME_ASSERT( GROUNDWIRE_INDEX_BITS == MAX_EDICT_BITS );
	COMPILE_TIME_ASSERT( GROUNDWIRE_SERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS );

	if ( !handle.IsValid() )
		return GROUNDWIRE_NONE;

	return SourceSharpGroundWire_Encode( handle.GetEntryIndex() );
}

#ifndef CLIENT_DLL
// The server's engine side to the blob: origin, health and alive, the words
// native holds. The managed command loop publishes the rest.
void SourceSharpPlayerSync_WriteEngineSide( unsigned char *pBlob,
                                            const SourceSharpPlayerFields_t &fields,
                                            const Vector &vecOrigin, int nHealth, bool bAlive );
#endif
