//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the player's state, between the loop and the blob ==//
//
// Phase 9c5's join. The movement loop reads and writes NATIVE members — 9b
// kept the loop native on purpose — while the wire format and the prediction
// schema are ONE C# declaration (PlayerNetState). This header is the crossing
// between those two facts: after every command the native state is written
// into the blob at the offsets the declaration computed, and on every server
// update the client adopts the blob back into its native state before the
// replay runs on top.
//
// EVERY OFFSET IS ASKED, NEVER COPIED (Step 7's rule): resolved by field name
// through NetStateFieldOffset/NetStateFieldBits, once per resolve generation.
// A reload republishes the types and may move every field, so the generation
// is bumped from the reload path and the next sync re-asks. A field that does
// not resolve syncs as nothing — and says so once — rather than writing into
// whatever used to be there.
//
// Everything here speaks IMovementPlayer plus a small extras block, because
// the two player classes share the interface and almost nothing else: the
// members the loop touches are reached through the same Mv asks the loop
// itself makes, and the values the interface deliberately does not carry
// (origin, velocity, tick base, health, and the ground handle) travel beside
// it for the caller to apply with its own class's setters.
//
//=============================================================================//
#ifndef SOURCESHARP_STANDALONE_PLAYER_SYNC_H
#define SOURCESHARP_STANDALONE_PLAYER_SYNC_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "imovementplayer.h"
#include "generic_payload.h"
#include "ground_handle_wire.h"
#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// Bumped by the managed reload on both sides; every resolved field set whose
// stamp disagrees re-asks before its next use.
//-----------------------------------------------------------------------------
unsigned int SourceSharp_PlayerSyncGeneration();
void         SourceSharp_PlayerSyncInvalidate( const char *pReason );

//-----------------------------------------------------------------------------
// The resolved placements. Whole-storage fields keep a plain offset; the
// narrow ones keep the packed placement the C# packer chose for them.
//-----------------------------------------------------------------------------
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

// Asks the managed side for every placement. False — with one warning — when
// the class is not published on this side yet; the caller tries again on the
// next sync rather than remembering the failure.
//-----------------------------------------------------------------------------
// THE TWO LAYOUT ASKS, as this side reaches them (plan_engineapi_vtables E16).
// The resolve loops below and in world_net_sync.cpp are shared; what they call
// is not. The server asks its managed-built IManagedNetState, the client its
// IManagedStandaloneClient (plan_client_vtables CE5). Both answer -1 for a
// field that is not published.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The values IMovementPlayer deliberately does not carry.
//
// THE GROUND HANDLE is here rather than behind an Mv ask on purpose. The
// interface hands out a resolved IHandleEntity *, and resolving is exactly
// what must not happen on this path: the two sides keep a bare CBaseHandle,
// the blob carries its two numbers (ground_handle_wire.h), and each side puts
// the word back into its own member with its own list untouched. A crossing
// through a pointer would turn "the server was standing on entity 43" into
// "entity 43 does not exist on this client YET", which is a NULL ground and
// the very contradiction the field exists to remove.
//-----------------------------------------------------------------------------
struct SourceSharpPlayerExtras_t
{
	Vector vecOrigin;
	Vector vecVelocity;
	float  flMaxSpeed;
	int    nTickBase;
	int    nHealth;
	int    nGroundEntity;   // GROUNDWIRE_NONE when not standing on anything
	bool   bAlive;
};

//-----------------------------------------------------------------------------
// The ground handle, both ways, and the one place the engine's own bit widths
// are checked against the numbers ground_handle_wire.h restates. Inline here
// rather than in that header because it is where CBaseHandle and const.h are
// both in scope; the asserts are INSIDE the bodies because COMPILE_TIME_ASSERT
// declares a fixed name and two of them at file scope collide.
//
// ENCODE ONLY. There is no DecodeGround here on purpose: the word carries an
// entity INDEX and nothing else (ground_handle_wire.h says why, with the
// measurement), so turning it back into a handle means asking THIS SIDE'S
// entity list for the entity at that index and keeping the handle it hands
// back. The two sides ask different lists, so each does it for itself --
// C_StandalonePlayer::AdoptServerState is the only caller that needs to.
//-----------------------------------------------------------------------------
inline int SourceSharpPlayerSync_EncodeGround( const CBaseHandle &handle )
{
	COMPILE_TIME_ASSERT( GROUNDWIRE_INDEX_BITS == MAX_EDICT_BITS );
	COMPILE_TIME_ASSERT( GROUNDWIRE_SERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS );

	if ( !handle.IsValid() )
		return GROUNDWIRE_NONE;

	return SourceSharpGroundWire_Encode( handle.GetEntryIndex() );
}

// Native state -> blob. Called after every command on the server and after
// every replayed command on the client, so the blob IS the player at that
// command — which is what the ring copies and what the wire sends.
void SourceSharpPlayerSync_Write( unsigned char *pBlob,
                                  const SourceSharpPlayerFields_t &fields,
                                  IMovementPlayer *pPlayer,
                                  const SourceSharpPlayerExtras_t &extras );

#endif // SOURCESHARP_STANDALONE_PLAYER_SYNC_H
