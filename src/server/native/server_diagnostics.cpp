//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimDiagnostics' bodies: the players' shadow counts and the exit recorders.

#include "server_diagnostics.h"
#include "server_state.h"
#include "exit_watch.h"

// The shadows still in the solver, and those that outlived their environment, which is
// recreated at every level change (CServerPlayersDll). Copies live, outlived into pOut.
int32_t CServerDiagnostics::PlayerShadowCounts( int32_t *pOut, int32_t nMax )
{
	if ( !pOut || nMax <= 0 )
		return 0;

	const int32_t values[] = { m_Players.m_nShadowsLive, m_Players.m_nShadowsOutlived };
	const int32_t nAll = int32_t( sizeof( values ) / sizeof( values[0] ) );
	const int32_t n = nMax < nAll ? nMax : nAll;
	for ( int32_t i = 0; i < n; ++i )
		pOut[i] = values[i];
	return n;
}

int32_t CServerDiagnostics::ExitWatchArm( const char *pRecordPath )
{
	return ExitWatch_Arm( pRecordPath );
}

int32_t CServerDiagnostics::ExitWatchRead( int32_t *pOut, int32_t nMax )
{
	return ExitWatch_Read( pOut, nMax );
}

int32_t CServerDiagnostics::ExitWatchDisarm()
{
	return ExitWatch_Disarm();
}
