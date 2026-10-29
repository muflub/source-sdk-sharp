//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// The one place IGameEntity is cast down to CStandaloneEntity. Sound because
// every IGameEntity in this build is a CStandaloneEntity. Callers test the
// pointer before asking.

#include "vphysics_interface.h"

#include "shell_standalone_player.h"
#include "standalone_movetype.h"

static CStandaloneEntity* ShellOf(IGameEntity* pEnt)
{
	return SourceSharp_ShellOf(pEnt);
}

static const CStandaloneEntity* ShellOf(const IGameEntity* pEnt)
{
	return SourceSharp_ShellOf(pEnt);
}

const char* StandaloneEntity_Name(const IGameEntity* pEnt)
{
	return ShellOf(pEnt)->GetEntityName();
}

int StandaloneEntity_TakeDamage(IGameEntity* pEnt, const ss::CTakeDamageInfo& info)
{
	return ShellOf(pEnt)->TakeDamage(info);
}

bool StandaloneEntity_IsPlayer(const IGameEntity* pEnt)
{
	return ShellOf(pEnt)->IsPlayer();
}

int StandaloneEntity_MoveType(const IGameEntity* pEnt)
{
	// SourceSharp_PlayerOf takes a non-const shell; nothing here writes through it.
	CStandaloneEntity* pShell = ShellOf(const_cast<IGameEntity*>(pEnt));
	CStandalonePlayer* pPlayer = pShell->IsPlayer() ? SourceSharp_PlayerOf(pShell) : NULL;

	return StandaloneMoveType_Report(pPlayer != NULL, pPlayer ? (int)pPlayer->MvGetMoveType() : 0,
									 pShell->GetPhysics() != NULL, pShell->Block().Head.MoveType);
}

IPhysicsObject* StandaloneEntity_PhysicsObject(const IGameEntity* pEnt)
{
	return ShellOf(pEnt)->GetPhysics();
}

// game_entity_relays.h's server half.
int GameEntity_ModelIndex(const IGameEntity* pEnt)
{
	return pEnt ? ShellOf(pEnt)->GetModelIndex() : 0;
}

CBaseHandle GameEntity_RefHandle(const IGameEntity* pEnt)
{
	if (!pEnt)
		return CBaseHandle();

	// The shell's own conversion to IHandleEntity, made with the layout visible.
	return ShellOf(pEnt)->GetRefEHandle();
}

bool StandaloneEntity_ApplyMapKey(IGameEntity* pEnt, const char* szKeyName, const char* szValue)
{
	return ShellOf(pEnt)->KeyValue(szKeyName, szValue);
}

void StandaloneEntity_Place(IGameEntity* pEnt, const Vector* pOrigin, const QAngle* pAngles)
{
	CStandaloneEntity* pShell = ShellOf(pEnt);

	if (pOrigin)
		pShell->SetAbsOrigin(*pOrigin);

	if (pAngles)
		pShell->SetAbsAngles(*pAngles);
}

void StandaloneEntity_PlaceAndSpawn(IGameEntity* pEnt, const Vector* pOrigin, const QAngle* pAngles)
{
	StandaloneEntity_Place(pEnt, pOrigin, pAngles);

	ShellOf(pEnt)->Spawn();
}

void StandaloneEntity_HoldMoveVelocity(IGameEntity* pEnt, const Vector& vecVelocity)
{
	ShellOf(pEnt)->HoldMoveVelocity(vecVelocity);
}

Vector StandaloneEntity_Velocity(const IGameEntity* pEnt)
{
	return ShellOf(pEnt)->MoveVelocity();
}
