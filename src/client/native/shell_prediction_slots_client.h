//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The prediction replay's slots: one command out of the engine's input ring, the movement
// bracket and the player's prediction drive. A member of the client DLL object.

#pragma once

#include <stdint.h>

#include "ishim_areas_client.h"
#include "shell_managed_areas_client.h"

class CStandaloneClients;

class CShellGameMovement;
class IMoveHelper;

class CClientEngineInterfaces;

class CPredictionSlotsClient final : public IShimPredictionClient
{
public:
	CPredictionSlotsClient(const CClientEngineInterfaces& engines, CStandaloneClients& standalone,
						   CShellGameMovement& gameMovement);

	CPredictionSlotsClient(const CPredictionSlotsClient&) = delete;
	CPredictionSlotsClient& operator=(const CPredictionSlotsClient&) = delete;

	int32_t ClientGetUserCmd(int32_t nCommandNumber, SourceSharpUserCmd* pOut) override;
	void ClientPlayerPredictionDrive(uint32_t hEntity, const SourceSharpClientPlayerView* pView) override;
	int32_t MoveBeginClientCommand(uint32_t hPlayer, int32_t nCommandNumber, SourceSharpMoveState* pOut) override;
	void MoveEndClientCommand(int32_t bRan, int32_t* pOutGroundIndex) override;

private:
	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
	CShellGameMovement& m_GameMovement;
	IMoveHelper* m_pMoveHelper;
};
