//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimDiagnostics: the players' physics-shadow counts and the dev API's exit recorders
// (exit_watch.cpp, whose signal-handler state is the kernel's to reach). One object, the
// DLL tier's.

#pragma once

#include "ishim_areas.h"

class CServerPlayersDll;

class CServerDiagnostics final : public IShimDiagnostics
{
public:
	explicit CServerDiagnostics( const CServerPlayersDll &players )
		: m_Players( players )
	{
	}

	CServerDiagnostics( const CServerDiagnostics & ) = delete;
	CServerDiagnostics &operator=( const CServerDiagnostics & ) = delete;

	int32_t PlayerShadowCounts( int32_t *pOut, int32_t nMax ) override;
	int32_t ExitWatchArm( const char *pRecordPath ) override;
	int32_t ExitWatchRead( int32_t *pOut, int32_t nMax ) override;
	int32_t ExitWatchDisarm() override;

private:
	const CServerPlayersDll &m_Players;
};
