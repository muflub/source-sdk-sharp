//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The client's IGameMovement, its CMoveData copy and its servicing calls around the managed
// movement loop. A member of the client DLL object; the bodies are in shell_movement.cpp.

#pragma once

#include <stdint.h>

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

class CBasePlayer;

#include "PlayerState.h"
#include "igamemovement.h"
#include "imovementplayer.h"
#include "gametrace.h"
#include "sourcesharp.h"

class CClientEngineInterfaces;
class CClientEntityList;
struct CManagedAreasClient;

class CShellGameMovement : public IGameMovement
{
public:
	CShellGameMovement(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					   CClientEntityList& entityList);
	virtual ~CShellGameMovement();

	// Stock's signatures; nothing calls them, the interface is published by name.
	virtual void ProcessMovement(CBasePlayer* pPlayer, CMoveData* pMove) OVERRIDE;
	virtual void StartTrackPredictionErrors(CBasePlayer* pPlayer) OVERRIDE;
	virtual void FinishTrackPredictionErrors(CBasePlayer* pPlayer) OVERRIDE;

	static IMovementPlayer* Player(CBasePlayer* pPlayer) { return reinterpret_cast<IMovementPlayer*>(pPlayer); }

	virtual void DiffPrint(PRINTF_FORMAT_STRING char const* fmt, ...) OVERRIDE;
	virtual Vector GetPlayerMins(bool ducked) const OVERRIDE;
	virtual Vector GetPlayerMaxs(bool ducked) const OVERRIDE;
	virtual Vector GetPlayerViewOffset(bool ducked) const OVERRIDE;

	bool RunManagedCommand();

	// IShimMovement, forwarding to the servicing calls mid-crossing. Outside a crossing they
	// refuse: a zero trace with fraction 1 would read as "nothing in the way".
	void MoveTrace(const float* pStart, const float* pEnd, const float* pMins, const float* pMaxs, int32_t nMask,
				   int32_t nCollisionGroup, SourceSharpMoveTraceResult* pOut);
	int32_t MoveEffect(int32_t nKind, const float* pXYZ, float flValue, int32_t nValue);

	// The client's replay bracket: fills the constant words of the crossing and marks this
	// mover as running a command, so the slots answer for it. player stays NULL.
	int32_t BeginClientCommand(uint32_t hPlayer, SourceSharpMoveState* pOut);
	void EndClientCommand(int32_t bRan, int32_t* pOutGroundIndex);

	void FillConstants(struct SourceSharpMoveState* pState);
	void WorldTrace(const float* pStart, const float* pEnd, const float* pMins, const float* pMaxs, int nMask,
					int nCollisionGroup, struct SourceSharpMoveTraceResult* pOut);
	int WorldEffect(int nKind, const float* pXYZ, float flValue, int nValue);

	// The client replay never calls ProcessMovement, so it sets the move data here.
	void SetMoveData(CMoveData* pMove) { mv = pMove; }

	IHandleEntity* LastGround() const { return m_pLastGround; }

	IMovementPlayer* player;

private:
	// The client's whole-loop crossing, one per command.
	bool ProcessMovementManaged(SourceSharpMoveState* pState);

	IHandleEntity* GroundFromTrace(const trace_t& tr);

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	CClientEntityList& m_EntityList;

	// Set only for the duration of one crossing.
	bool m_bActive = false;

	void ResetDuckLatched();

	CMoveData* mv;

	int m_nOldWaterLevel;
	int m_nOnLadder;

	// The last standable ground a serviced trace found: the loop carries only whether
	// there is ground, not which entity. NULL until the first trace.
	IHandleEntity* m_pLastGround;

	// The last serviced trace, so the touch list can be replayed against it.
	trace_t m_LastTrace;
};
