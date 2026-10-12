//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — where a player's tick base is put ==================//
//
// CBasePlayer::AdjustPlayerTimeBase (player.cpp), as a rule with no player in
// it. The tick base is the clock a player's commands run at, and the client
// predicts it: every move of it the client could not predict is a TickBase
// misprediction and shifts curtime under every timer the weapon machine keeps.
//
// Stock moves it only on a frame that simulates commands, and only when the
// batch would END outside a window around tickcount + the correction. The
// port that stood here re-based on idle frames too, and whenever the base
// itself drifted from tickcount -- which a listen server's command-less
// frames make it do.
//
//=============================================================================//

#ifndef SOURCESHARP_TICKBASE_RULE_H
#define SOURCESHARP_TICKBASE_RULE_H

// The tick base to run nSimulationTicks commands from. nCorrectionTicks is
// TIME_TO_TICKS( sv_clockcorrection_msecs / 1000 ), stock's 60 ms.
inline int TickBase_Adjust( int nTickBase, int nTickCount, int nSimulationTicks,
                            int nSimTicksThisFrame, int nCorrectionTicks, bool bSinglePlayer )
{
	if ( nSimulationTicks <= 0 )
		return nTickBase;

	if ( bSinglePlayer )
		return nTickCount - nSimulationTicks + nSimTicksThisFrame;

	const int nIdealFinalTick     = nTickCount + nCorrectionTicks;
	const int nEstimatedFinalTick = nTickBase + nSimulationTicks;

	if ( nEstimatedFinalTick > nIdealFinalTick + nCorrectionTicks ||
	     nEstimatedFinalTick < nIdealFinalTick - nCorrectionTicks )
		return nIdealFinalTick - nSimulationTicks + nSimTicksThisFrame;

	return nTickBase;
}

#endif // SOURCESHARP_TICKBASE_RULE_H
