//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimFrame: one irreducible step of CServerGameDLL::GameFrame or PreClientUpdate per
// method. The order, conditions and early returns are in ServerGameDll.cs; a step is
// never a decision.

#pragma once

#include "ishim_frame.h"

class CGlobalVars;
class CServerDebugOverlays;
class IMDLCache;
class IUploadGameStats;

class CServerFrame final : public IShimFrame
{
public:
	CServerFrame( CGlobalVars *pGlobals, IUploadGameStats *pGameStats, IMDLCache *pMdlCache,
				  CServerDebugOverlays &overlays )
		: m_pGlobals( pGlobals )
		, m_pGameStats( pGameStats )
		, m_pMdlCache( pMdlCache )
		, m_Overlays( overlays )
	{
	}

	CServerFrame( const CServerFrame & ) = delete;
	CServerFrame &operator=( const CServerFrame & ) = delete;

	// gameinterface.cpp:1326. C# does the branching.
	int32_t IsSimulatingOnAlternateTicks() override;

	// For the `tickcount & 1` test; Globals is unavailable on this path.
	int32_t TickCount() override;

	// A pair, so the alternate-tick arithmetic stays in C# beside stock's.
	float GetFrameTime() override;
	void SetFrameTime( float flFrameTime ) override;

	// gameinterface.cpp:1389.
	void UpdateAllClientData() override;

	// gameinterface.cpp:1391-1394. The null test stays native: g_pGameRules does not exist
	// between levels.
	void EndGameFrame() override;

	void GamestatsUpdateConnection() override;

	// PreClientUpdate, gameinterface.cpp:1440.
	void DrawAllDebugOverlays() override;

	// The hierarchy's player commands run, then the managed thinks, then Finish composes
	// parented children and drains deferred deletes. C# owns that order.
	void StandalonePlayerCommands() override;
	void StandaloneFinish() override;

	// After the managed frame, at the position ServerSystems gives it.
	void DrainEntitySlots() override;

	// The managed row's stages take its lock through these.
	void BeginModelCacheLock() override;
	void EndModelCacheLock() override;

private:
	CGlobalVars *m_pGlobals;
	IUploadGameStats *m_pGameStats;
	IMDLCache *m_pMdlCache;
	CServerDebugOverlays &m_Overlays;
};
