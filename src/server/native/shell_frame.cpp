//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
//
// IShimFrame: one irreducible step of CServerGameDLL::GameFrame or PreClientUpdate per
// method. The order, conditions and early returns are in ServerGameDll.cs; a step is
// never a decision.

#include "server_state.h"
#include "server_engine.h"
#include "eiface.h"
#include "igamerules.h"
#include "ishim_frame.h"

#include "shell_standalone_player.h"
#include "datacache/imdlcache.h"

// The managed row's stages take its lock through Begin/EndModelCacheLock.

// Defined in shell_standalone_base.cpp.
void StandaloneEntities_Finish();

void UpdateAllClientData();

// Stock's global, acquired by SourceSharp_AcquireEngineInterfaces.

extern CGlobalVars *gpGlobals;

bool EntitySim_IsSimulatingOnAlternateTicks();

// Not behind _DEBUG, as in stock.
void DrawAllDebugOverlays();

int SourceSharp_DrainEntitySlots();

namespace
{

class CShimFrame : public IShimFrame
{
public:
	// No fields: the object exists only to give the forwarder table an address.

	// gameinterface.cpp:1326. C# does the branching.
	virtual int32_t IsSimulatingOnAlternateTicks() OVERRIDE { return EntitySim_IsSimulatingOnAlternateTicks() ? 1 : 0; }

	// For the `tickcount & 1` test; Globals is unavailable on this path.
	virtual int32_t TickCount() OVERRIDE { return gpGlobals->tickcount; }

	// A pair, so the alternate-tick arithmetic stays in C# beside stock's.
	virtual float GetFrameTime() OVERRIDE { return gpGlobals->frametime; }

	virtual void SetFrameTime( float flFrameTime ) OVERRIDE { gpGlobals->frametime = flFrameTime; }

	// gameinterface.cpp:1389.
	virtual void UpdateAllClientData() OVERRIDE { ::UpdateAllClientData(); }

	// gameinterface.cpp:1391-1394. The null test stays native: g_pGameRules does not exist
	// between levels.
	virtual void EndGameFrame() OVERRIDE
	{
		if ( g_pGameRules )
			g_pGameRules->EndGameFrame();
	}

	virtual void GamestatsUpdateConnection() OVERRIDE { ServerGameStats()->UpdateConnection(); }

	// PreClientUpdate, gameinterface.cpp:1440.
	virtual void DrawAllDebugOverlays() OVERRIDE { ::DrawAllDebugOverlays(); }

	// The hierarchy's player commands run, then the managed thinks, then Finish composes
	// parented children and drains deferred deletes. C# owns that order.
	virtual void StandalonePlayerCommands() OVERRIDE { CStandalonePlayer::RunAllQueuedCommands(); }

	virtual void StandaloneFinish() OVERRIDE { StandaloneEntities_Finish(); }

	// After the managed frame, at the position ServerSystems gives it.
	virtual void DrainEntitySlots() OVERRIDE { SourceSharp_DrainEntitySlots(); }

	virtual void BeginModelCacheLock() OVERRIDE { ServerMdlCache()->BeginLock(); }

	virtual void EndModelCacheLock() OVERRIDE { ServerMdlCache()->EndLock(); }
};

} // namespace

static CShimFrame g_ShimFrame;

// The internalFactory's row for this area. The forwarder table is generated from
// ishim_frame.h with its own slot count.
void *ShimFrame_Acquire()
{
	return static_cast<IShimFrame *>( &g_ShimFrame );
}
