//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The frame's native steps, as an interface acquired by name from
// SOURCESHARP_FACTORY_INTERNAL. The managed layout is generated from this header.
//
// Append-only: acquisition checks only the slot count, so an insert or reorder
// misbinds silently. No virtual destructor, which would take the first two slots;
// the object is a singleton never deleted through this interface.

#pragma once

#include "tier0/platform.h"

#define SOURCESHARP_SHIM_FRAME_VERSION "SourceSharpFrame001"

abstract_class IShimFrame
{
public:
	// A question only; C# does the branching.
	virtual int32_t IsSimulatingOnAlternateTicks() = 0;

	// gpGlobals->tickcount. Cold path: managed reads the bound CGlobalVars when it can.
	virtual int32_t TickCount() = 0;

	// gpGlobals->frametime, read.
	virtual float GetFrameTime() = 0;

	// gpGlobals->frametime, written.
	virtual void SetFrameTime(float flFrameTime) = 0;

	virtual void UpdateAllClientData() = 0;

	// The rules' end of frame. The g_pGameRules null test stays native: the rules
	// don't exist between levels.
	virtual void EndGameFrame() = 0;

	virtual void GamestatsUpdateConnection() = 0;

	// Not behind _DEBUG, as stock's; it draws managed code's NDebugOverlay calls.
	virtual void DrawAllDebugOverlays() = 0;

	// The standalone hierarchy's post-think steps; their order is decided in C#.
	virtual void StandalonePlayerCommands() = 0;
	virtual void StandaloneFinish() = 0;

	// One NetworkStateChanged per entity whose managed state was written this
	// frame. Runs after the managed frame and before the snapshot.
	virtual void DrainEntitySlots() = 0;

	// mdlcache->BeginLock() / EndLock(), taken around each system's stage.
	virtual void BeginModelCacheLock() = 0;
	virtual void EndModelCacheLock() = 0;

	// New methods go at the end.
};
