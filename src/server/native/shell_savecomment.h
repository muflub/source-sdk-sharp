//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//=============================================================================//
// THE ONE SAVE-CLUSTER OUT-PARAMETER, FILLED -- docs/plan-audit.md D16.
//
// WHY THIS EXISTS, AND IT IS NOT A CHANGE OF MIND ABOUT THE REFUSAL.
// CShellGameDll refuses the save cluster and goes on refusing it. Fourteen of
// those fifteen methods return void or a value the shell chooses, so a refusal
// that does nothing is a complete answer. GetSaveComment is the exception: the
// engine hands it a stack buffer to FILL --
//
//     char comment[80];
//     serverGameDLL->GetSaveComment( comment, sizeof( comment ), ... );
//
// -- and a refusal that returns without touching it leaves the engine holding
// 80 bytes of uninitialised stack that it then treats as the save's title.
// Refusing to do the work is a decision; leaving an out-parameter undefined is
// not one, it is a contract the shell silently breaks.
//
// AND IT IS REACHED. This was recorded as unreachable, which measured false:
// the engine's gate is `sv.IsMultiplayer()`, this mod's own
// CServerGameClients::GetPlayerLimits sets `minplayers = 1`, and a server
// launched `-maxplayers 1` answers `save` by calling GetSaveComment and
// SaveInit -- both counted live, three times each, one pair per save command.
// docs/divergences.md, "Saving and level transitions are gone", carries the
// transcript and the numbers.
//
// WHAT IS NOT CARRIED. Stock's body (game/server/gameinterface.cpp, deleted at
// plan_gamedll_after.md Phase A9 step 3) opens with a linear search of
// gTitleComments -- a table of HL2 chapter titles keyed by BSP name prefix, so
// that `d1_trainstation_01` saves as "Point Insertion". Not one of its rows
// matches a map this mod ships, and the table is 60-odd lines of single-player
// campaign text. The fallback arm of stock's own function is what is carried:
// when no title matches, stock uses the map name, which is what every map here
// would get from the table anyway. The formatting below -- the 64-column pad,
// the 64-character truncation, the minute rollover and the 999:59 clamp -- is
// stock's, character for character, because the save UI parses it by column.
//
// ENGINE-FREE, for shell_gamedll_refusals.h's reason: this is a property of a
// BODY -- that a short name is padded so the clock starts at a fixed column,
// that a long one is cut at 64, that 90 seconds reads as one minute thirty --
// and a property of a body can only be checked by running it. shell_gamedll.cpp
// includes gameinterface.h and cbase.h's world and can never be linked into a
// gtest, so the formatting lives here and
// sourcesharp/test/shell_savecomment_test.cpp compiles THIS header. Nothing
// there is a stand-in: it is the shipped function.
//
// snprintf and not Q_snprintf, for the same reason: Q_snprintf is vstdlib's and
// would drag the engine back in. The two agree on everything this uses -- both
// null-terminate, both honour the width and precision -- and the difference is
// their return value on truncation, which this does not read.
//=============================================================================//

#pragma once

#include <stdio.h>

//-----------------------------------------------------------------------------
// Fill the engine's save-comment buffer.
//
// pOut/nMaxLength are the engine's, exactly as IServerGameDLL::GetSaveComment
// receives them. A NULL buffer or a non-positive length WRITES NOTHING and is
// not an error -- there is no buffer to say anything in, and inventing one to
// complain into is how a defensive check becomes a crash.
//
// A NULL map name is a defined case rather than a dereference: the engine's
// STRING( gpGlobals->mapname ) is never NULL today, and "never NULL today" is
// the sentence this port has already been wrong about several times over.
//-----------------------------------------------------------------------------
inline void ShellGameDll_FormatSaveComment( char *pOut, int nMaxLength,
                                            const char *pMapName,
                                            float flMinutes, float flSeconds,
                                            bool bNoTime )
{
	if ( pOut == NULL || nMaxLength <= 0 )
		return;

	const char *pName = ( pMapName != NULL ) ? pMapName : "";

	if ( bNoTime )
	{
		snprintf( pOut, (size_t)nMaxLength, "%-64.64s", pName );
		pOut[ nMaxLength - 1 ] = '\0';
		return;
	}

	int nMinutes = (int)flMinutes;
	int nSeconds = (int)flSeconds;

	// Stock's clamp, comment and all: "Wow, this guy/gal must suck...!". The
	// format is %03d, so a four-digit minute count would shift every column
	// after it and the UI would read the clock out of the wrong place.
	//
	// AND STOCK CLAMPS BEFORE IT ROLLS THE SECONDS OVER, which means the clamp
	// does not actually bound what is printed: 999 minutes and 120 seconds
	// still prints 1001. That is stock's order and it is kept, because this
	// function's whole contract is to produce the bytes stock produced -- a
	// column the UI parses is not the place to be quietly better than the
	// thing being replaced. The case is pinned by name in
	// shell_savecomment_test.cpp so it is a recorded carry rather than a
	// missed edge.
	if ( nMinutes >= 1000 )
	{
		nMinutes = 999;
		nSeconds = 59;
	}

	const int nMinutesAdd = ( nSeconds / 60 );
	nSeconds %= 60;

	snprintf( pOut, (size_t)nMaxLength, "%-64.64s %03d:%02d",
	          pName, ( nMinutes + nMinutesAdd ), nSeconds );
	pOut[ nMaxLength - 1 ] = '\0';
}
