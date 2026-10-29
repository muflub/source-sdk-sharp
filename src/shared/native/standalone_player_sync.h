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

#ifdef CLIENT_DLL
// The client's managed generation (CManagedSide::Generation), for its field sets.
unsigned int SourceSharp_PlayerSyncGeneration();
#endif

// The resolved placements: whole-storage fields keep an offset, narrow ones the
// packed placement the C# packer chose.
struct SourceSharpPlayerFields_t
{
	unsigned int nGeneration; // 0 = never resolved

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

	SourceSharpPlayerFields_t() { memset(this, 0, sizeof(*this)); }

	// Resolved under the managed generation now current; a reload bumps it
	// (CManagedSide::Generation), and a stale set re-asks before use.
	bool IsCurrent(unsigned int nCurrent) const { return nGeneration != 0 && nGeneration == nCurrent; }
};

// Asks the managed side for every placement. False, with one warning, when the
// class is not published on this side yet; the caller retries on the next sync.
//
#ifndef CLIENT_DLL
#include "imanaged_areas.h"
typedef IManagedNetState NetStateArea_t;
#else
#include "imanaged_areas_client.h"
typedef IManagedStandaloneClient NetStateArea_t;
#endif

// This side's layout asks, over the load's net-state area. Both answer -1 for a field
// that is not published.
struct SourceSharpNetStateAsk_t
{
	NetStateArea_t& m_Area;

	int32_t FieldOffset(const char* pClassName, const char* pFieldName) const
	{
		return m_Area.NetStateFieldOffset(pClassName, pFieldName);
	}

	int32_t FieldBits(const char* pClassName, const char* pFieldName) const
	{
		return m_Area.NetStateFieldBits(pClassName, pFieldName);
	}
};

// pArea is the load's net-state area, read by the caller at the point of use: it goes
// with each unload, and NULL (outside a load) answers false.
bool SourceSharpPlayerFields_Resolve(SourceSharpPlayerFields_t* pFields, unsigned int nGeneration,
									 NetStateArea_t* pArea);

// The ground handle travels as an entry index word, never a resolved pointer.
// Encode only: each side resolves the index through its own entity list. The
// asserts check ground_handle_wire.h's restated widths; they sit inside the body
// because COMPILE_TIME_ASSERT declares a fixed name.
inline int SourceSharpPlayerSync_EncodeGround(const CBaseHandle& handle)
{
	COMPILE_TIME_ASSERT(GROUNDWIRE_INDEX_BITS == MAX_EDICT_BITS);
	COMPILE_TIME_ASSERT(GROUNDWIRE_SERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS);

	if (!handle.IsValid())
		return GROUNDWIRE_NONE;

	return SourceSharpGroundWire_Encode(handle.GetEntryIndex());
}

#ifndef CLIENT_DLL
// The server's engine side to the blob: origin, health and alive, the words
// native holds. The managed command loop publishes the rest.
void SourceSharpPlayerSync_WriteEngineSide(unsigned char* pBlob, const SourceSharpPlayerFields_t& fields,
										   unsigned int nGeneration, const Vector& vecOrigin, int nHealth, bool bAlive);
#endif
