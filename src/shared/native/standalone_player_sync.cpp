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
#if defined(CLIENT_DLL)
#include "c_playerlocaldata.h"
#else
#include "ss_playerlocaldata.h"
#endif
#include "PlayerState.h"
#include "standalone_player_sync.h"

bool SourceSharpPlayerFields_Resolve(SourceSharpPlayerFields_t* pFields, unsigned int nGeneration,
									 NetStateArea_t* pArea)
{
	if (!pFields || !pArea)
		return false;

	const SourceSharpNetStateAsk_t ask = {*pArea};

	SourceSharpNetClassResolve_t resolve = {ask, "player", true};

	pFields->nHealth = resolve.Offset("Health");
	pFields->nMaxSpeed = resolve.Offset("MaxSpeed");
	pFields->nFallVelocity = resolve.Offset("FallVelocity");
	pFields->nDuckTime = resolve.Offset("DuckTime");
	pFields->nDuckJumpTime = resolve.Offset("DuckJumpTime");
	pFields->nJumpTime = resolve.Offset("JumpTime");
	pFields->nStepSize = resolve.Offset("StepSize");
	pFields->nWaterJumpTime = resolve.Offset("WaterJumpTime");
	pFields->nSurfaceFriction = resolve.Offset("SurfaceFriction");
	pFields->nOldForwardMove = resolve.Offset("OldForwardMove");
	pFields->nFlags = resolve.Offset("Flags");
	pFields->nOldButtons = resolve.Offset("OldButtons");
	pFields->nTickBase = resolve.Offset("TickBase");
	pFields->nGroundEntity = resolve.Offset("GroundEntity");

	pFields->Alive = resolve.Packed("Alive");
	pFields->Ducked = resolve.Packed("Ducked");
	pFields->Ducking = resolve.Packed("Ducking");
	pFields->InDuckJump = resolve.Packed("InDuckJump");
	pFields->AllowAutoMovement = resolve.Packed("AllowAutoMovement");
	pFields->WaterLevel = resolve.Packed("WaterLevel");
	pFields->WaterType = resolve.Packed("WaterType");
	pFields->MoveType = resolve.Packed("MoveType");

	pFields->nOrigin = resolve.Offset("Origin");
	pFields->nVelocity = resolve.Offset("Velocity");
	pFields->nBaseVelocity = resolve.Offset("BaseVelocity");
	pFields->nViewOffset = resolve.Offset("ViewOffset");
	pFields->nPunchAngle = resolve.Offset("PunchAngle");
	pFields->nPunchAngleVel = resolve.Offset("PunchAngleVel");
	pFields->nLadderNormal = resolve.Offset("LadderNormal");
	pFields->nWaterJumpVel = resolve.Offset("WaterJumpVel");

	if (!resolve.m_bAll)
	{
		Warning("[SourceSharp] player sync: player's fields did not all resolve — "
				"is PlayerNetState published on this side?\n");
		return false;
	}

	pFields->nGeneration = nGeneration;
	return true;
}

// memcpy per field: the blob's layout is C#'s, so nothing may assume adjacency.
static inline void WriteI32(unsigned char* pBlob, int nOffset, int nValue)
{
	if (nOffset >= 0)
		memcpy(pBlob + nOffset, &nValue, sizeof(nValue));
}

static inline void WriteVec(unsigned char* pBlob, int nOffset, const Vector& vec)
{
	if (nOffset >= 0)
	{
		float xyz[3] = {vec.x, vec.y, vec.z};
		memcpy(pBlob + nOffset, xyz, sizeof(xyz));
	}
}

#ifndef CLIENT_DLL
void SourceSharpPlayerSync_WriteEngineSide(unsigned char* pBlob, const SourceSharpPlayerFields_t& fields,
										   unsigned int nGeneration, const Vector& vecOrigin, int nHealth, bool bAlive)
{
	if (!pBlob || !fields.IsCurrent(nGeneration))
		return;

	WriteI32(pBlob, fields.nHealth, nHealth);
	SourceSharpPayload_WritePacked(pBlob, fields.Alive, bAlive ? 1u : 0u);
	WriteVec(pBlob, fields.nOrigin, vecOrigin);
}
#endif
