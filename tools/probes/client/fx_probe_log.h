//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// tools/probes/client/fx_probe_log.h -- lane c2-fx's probe logger, for the probe
// copies of fork TUs (probe_copies.cmake). One line per call to
// SOURCESHARP_PROBE_LOG, tagged "fx"; the body is in fx_probes_debug.cpp.

#pragma once

void FxProbeLog( const char *pFmt, ... ) __attribute__( ( format( printf, 1, 2 ) ) );

// random->RandomFloat / RandomInt, drawn from the same engine stream and logged
// ("fxr f <lo> <hi> <value>", "fxr i <lo> <hi> <value>"): a probe copy says
// FxProbeRandomFloat( where stock says random->RandomFloat(.
float FxProbeRandomFloat( float flLow, float flHigh );
int FxProbeRandomInt( int iLow, int iHigh );
