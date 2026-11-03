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
#include "shell_gamemovement.h"
#include "shell_member_command.h"
#include "ishim_areas.h"

class CClientEngineInterfaces;
class CClientEntityList;
class CGameRulesClient;
class IMoveHelper;
struct CManagedAreasClient;

class CShellGameMovement : public IGameMovement, public IShimMovement
{
public:
	CShellGameMovement(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					   CClientEntityList& entityList, CGameRulesClient& gameRules);
	virtual ~CShellGameMovement();

	// The prediction bracket's helper, NULL outside it.
	void SetMoveHelper(IMoveHelper* pMoveHelper) { m_pMoveHelper = pMoveHelper; }

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
				   int32_t nCollisionGroup, SourceSharpMoveTraceResult* pOut) override;
	int32_t MoveEffect(int32_t nKind, const float* pXYZ, float flValue, int32_t nValue) override;

	// The client's replay bracket: fills the constant words of the crossing and marks this
	// mover as running a command, so the slots answer for it. player stays NULL.
	int32_t BeginClientCommand(uint32_t hPlayer, SourceSharpMoveState* pOut);
	void EndClientCommand(int32_t bRan, int32_t* pOutGroundIndex);

	// IShimMovement.
	float CurrentGravity() override { return m_MoveVars.sv_gravity.GetFloat(); }

	void RegisterMovementParams(const SourceSharpMovementParams* pParams) override;

	void FillConstants(struct SourceSharpMoveState* pState);
	void WorldTrace(const float* pStart, const float* pEnd, const float* pMins, const float* pMaxs, int nMask,
					int nCollisionGroup, struct SourceSharpMoveTraceResult* pOut);
	int WorldEffect(int nKind, const float* pXYZ, float flValue, int nValue);

	// The client replay never calls ProcessMovement, so it sets the move data here.
	void SetMoveData(CMoveData* pMove) { mv = pMove; }

	IHandleEntity* LastGround() const { return m_pLastGround; }

	IMovementPlayer* player;

private:
	struct CMoveVars
	{
		ConVar sv_gravity{"sv_gravity", "800", FCVAR_NOTIFY | FCVAR_REPLICATED, "World gravity."};
		ConVar sv_stopspeed{"sv_stopspeed", "100", FCVAR_NOTIFY | FCVAR_REPLICATED,
							"Minimum stopping speed when on ground."};
		ConVar sv_noclipaccelerate{"sv_noclipaccelerate", "5", FCVAR_NOTIFY | FCVAR_ARCHIVE | FCVAR_REPLICATED};
		ConVar sv_noclipspeed{"sv_noclipspeed", "5", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_specaccelerate{"sv_specaccelerate", "5", FCVAR_NOTIFY | FCVAR_ARCHIVE | FCVAR_REPLICATED};
		ConVar sv_specspeed{"sv_specspeed", "3", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_specnoclip{"sv_specnoclip", "1", FCVAR_ARCHIVE | FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_maxspeed{"sv_maxspeed", "320", FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_accelerate{"sv_accelerate", "10", FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_airaccelerate{"sv_airaccelerate", "10", FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_waterfriction{"sv_waterfriction", "1", FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_rollspeed{"sv_rollspeed", "200", FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_rollangle{"sv_rollangle", "0", FCVAR_NOTIFY | FCVAR_REPLICATED, "Max view roll angle"};
		ConVar sv_friction{"sv_friction", "4", FCVAR_NOTIFY | FCVAR_REPLICATED, "World friction."};
		ConVar sv_bounce{"sv_bounce", "0", FCVAR_NOTIFY | FCVAR_REPLICATED,
						 "Bounce multiplier for when physically simulated objects collide with other objects."};
		ConVar sv_maxvelocity{"sv_maxvelocity", "3500", FCVAR_REPLICATED,
							  "Maximum speed any ballistically moving object is allowed to attain per axis."};
		ConVar sv_stepsize{"sv_stepsize", "18", FCVAR_NOTIFY | FCVAR_REPLICATED};
		ConVar sv_skyname{"sv_skyname", "sky_urb01", FCVAR_ARCHIVE | FCVAR_REPLICATED,
						  "Current name of the skybox texture"};
		// Replicated, so both sides run the same movement. 0 selects a stock body this
		// build does not have, which is fatal with a named reason.
		ConVar sourcesharp_movement_managed{
			"sourcesharp_movement_managed", "1", FCVAR_REPLICATED | FCVAR_NOTIFY,
			"Run the ten ported CGameMovement methods in C# (1) or in the stock C++ they forward from (0)."};
	};

	void Refused(int nReason, int nDetail);
	void Fatal(int nReason, int nDetail);
	void ReportCommand(const CCommand& args);

	CMemberCommand<CShellGameMovement, &CShellGameMovement::ReportCommand> m_ReportCommand{
		*this, "cs_cl_move_managed_report",
		"cs_cl_move_managed_report -- how many times each ported movement method ran in C# on the CLIENT", 0};

	CMoveVars m_MoveVars;

	// Stock's numbers, so movement works from the first tick, before managed registers.
	SourceSharpMovementParams m_Params = {
		21.0f, // flJumpHeight — GAMEMOVEMENT_JUMP_HEIGHT
		600.0f, // flGravity
		4.0f, // flFriction
		10.0f, // flAccelerate
		10.0f, // flAirAccelerate
		75.0f, // flStopSpeed
		3500.0f, // flMaxVelocity
		18.0f, // flStepSize
		30.0f, // flAirSpeedCap — CGameMovement::GetAirSpeedCap
		0.33333333f, // flDuckSpeedScale — HandleDuckingSpeedCrop
		320.0f, // flMaxSpeed
	};

	// How many commands the managed movement loop ran, counted in the crossing so a build
	// where it never ran cannot pass the movement gate. Unsigned and allowed to wrap.
	unsigned int m_nManagedCommands = 0;

	// Commands that could not be run, counted by reason.
	unsigned int m_nRefusals[kMovementFatal_Count] = {};

	// The client's whole-loop crossing, one per command.
	bool ProcessMovementManaged(SourceSharpMoveState* pState);

	IHandleEntity* GroundFromTrace(const trace_t& tr);

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	CClientEntityList& m_EntityList;
	CGameRulesClient& m_GameRules;
	IMoveHelper* m_pMoveHelper = nullptr;

	// What the servicing methods read during the client's replay.
	CMoveData m_MoveData;

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
