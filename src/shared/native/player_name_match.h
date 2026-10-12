//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — finding a player by the name somebody typed ========//
//
// The rule underneath UTIL_PlayerByName and UTIL_PlayerByPartialName, stated
// over SLOTS instead of over CBasePlayer pointers.
//
// WHY IT HAD TO MOVE. Both stock functions walk the player slots, ask
// UTIL_PlayerByIndex for a CBasePlayer, and `continue` on NULL — and in this
// build UTIL_PlayerByIndex answers NULL for every slot BY CONSTRUCTION, because
// server_csharp.vpc evicts player.cpp and no CBasePlayer exists to be in one
// (see UTIL_SDKPlayerFromEdict in util.h). So both walk to the end and answer
// "nobody" whatever you type, and the loop body — the comparison this header
// is — never runs. The same shape as the recipient filters and the view
// effects: the rule was never wrong, it was never reached.
//
// AND STOCK'S ANSWER CANNOT BE FIXED IN PLACE, which is the part worth
// writing down. Those functions return CBasePlayer *. There is no CBasePlayer
// for them to return here and there is no cast that would make one — Verdict D
// spent fifteen sites establishing that a C-cast of an entity to CBasePlayer is
// exactly the thing that starts lying at Phase 13 step 5. So the answer changes
// TYPE: a SLOT is a fact the engine states, both hierarchies have one, and
// every consumer of "which player" in this tree already takes one
// (UTIL_PlayerActivatorBySlot, CSingleUserRecipientFilter, UTIL_ScreenFadeSlot).
//
// AMBIGUOUS IS NOT NOBODY. Stock's partial-name search returns NULL both when
// nothing matched and when two things did, so "no such player" and "say which
// one you mean" arrive as the same silence — a seventh instance of the same
// family, in the one function whose whole job is to answer a person. They are
// separate values here.
//
// The comparisons are written out rather than taken from tier1: Q_stricmp and
// Q_stristr live in vstdlib, which an engine-free test binary does not link,
// and the rule is ASCII case-folding either way.
//
//=============================================================================//
#ifndef SOURCESHARP_PLAYER_NAME_MATCH_H
#define SOURCESHARP_PLAYER_NAME_MATCH_H
#ifdef _WIN32
#pragma once
#endif

namespace PlayerNameMatch
{

//-----------------------------------------------------------------------------
// What a search answers with. A slot is 1-based, so 0 is free to mean nobody
// and a negative value is free to mean something else went wrong.
//-----------------------------------------------------------------------------
const int NOBODY    =  0;
const int AMBIGUOUS = -1;

inline bool IsPlayer( int nResult )
{
	return nResult > 0;
}

inline char FoldCase( char c )
{
	return ( c >= 'A' && c <= 'Z' ) ? (char)( c - 'A' + 'a' ) : c;
}

//-----------------------------------------------------------------------------
// The whole name, case-insensitively. Q_stricmp( a, b ) == 0.
//-----------------------------------------------------------------------------
inline bool ExactMatch( const char *pName, const char *pWanted )
{
	if ( !pName || !pWanted )
		return false;

	while ( *pName && *pWanted )
	{
		if ( FoldCase( *pName ) != FoldCase( *pWanted ) )
			return false;

		++pName;
		++pWanted;
	}

	return *pName == 0 && *pWanted == 0;
}

//-----------------------------------------------------------------------------
// Anywhere in the name, case-insensitively. Q_stristr( name, wanted ) != NULL.
//
// An EMPTY needle matches everything, which is what strstr does and is why the
// callers below reject an empty argument before they get here — "match anybody"
// answers AMBIGUOUS on a two-player server and NOBODY on an empty one, and
// neither is what the person who typed nothing meant.
//-----------------------------------------------------------------------------
inline bool PartialMatch( const char *pName, const char *pWanted )
{
	if ( !pName || !pWanted )
		return false;

	if ( *pWanted == 0 )
		return true;

	for ( const char *pAt = pName; *pAt; ++pAt )
	{
		const char *a = pAt;
		const char *b = pWanted;

		while ( *a && *b && FoldCase( *a ) == FoldCase( *b ) )
		{
			++a;
			++b;
		}

		if ( *b == 0 )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// The walk's accumulator: offer it every slot whose name matched, and it
// answers the slot, NOBODY, or AMBIGUOUS.
//
// A TYPE rather than a `pFound` local, because the ambiguity rule is the half
// that gets dropped: stock writes it inline in one of its two searches and not
// in the other, and the difference between them is invisible at the call site.
//-----------------------------------------------------------------------------
struct Search_t
{
	int nFound;

	Search_t() : nFound( NOBODY ) {}

	void Offer( int nSlot )
	{
		if ( nSlot <= 0 )
			return;

		// The same slot twice is one player, not two. A caller walking slots
		// cannot offer a duplicate, but one merging two searches can — the
		// command-argument reading below does exactly that.
		if ( nFound == nSlot )
			return;

		nFound = ( nFound == NOBODY ) ? nSlot : AMBIGUOUS;
	}

	// Once ambiguous, always ambiguous: a later match cannot resolve an earlier
	// collision, and the search does not stop early because "how many matched"
	// is the thing being decided.
	int Result() const { return nFound; }
};

//-----------------------------------------------------------------------------
// How a console argument is READ, before any player is looked at.
//
// Stock's UTIL_PlayerByCommandArg tries several readings of one word in
// priority order and treats a collision between two of them as ambiguous. The
// classification is pure string work and is stated here; which of the readings
// this build can actually ANSWER is a separate question, and the answer is on
// the lookup itself (util.h) — there are no Steam IDs on an -insecure LAN
// server and UTIL_PlayerBySteamID has answered NULL since 13c.
//-----------------------------------------------------------------------------
struct ArgReading_t
{
	bool bEmpty;

	// Every character a digit. Stock reads this BOTH as a possible SteamID64
	// and, at the lowest priority, as a bare userid.
	bool bAllDigits;

	// '#' followed by digits, at least one.
	bool bPoundUserId;

	// Bracketed, as a rendered SteamID is: [U:1:1234].
	bool bBracketed;

	// Begins "STEAM_".
	bool bLegacySteamId;

	// The number, for the two userid readings. Zero when neither applies —
	// which is also the engine's "the world", so it is never a live userid.
	int nUserId;
};

inline ArgReading_t ReadArg( const char *pArg )
{
	ArgReading_t reading;
	reading.bEmpty         = true;
	reading.bAllDigits     = false;
	reading.bPoundUserId   = false;
	reading.bBracketed     = false;
	reading.bLegacySteamId = false;
	reading.nUserId        = 0;

	if ( !pArg || !pArg[0] )
		return reading;

	reading.bEmpty = false;

	int nLength = 0;
	while ( pArg[ nLength ] )
		++nLength;

	bool bDigitsAfterFirst = true;
	for ( int i = 1; i < nLength; ++i )
	{
		if ( pArg[ i ] < '0' || pArg[ i ] > '9' )
		{
			bDigitsAfterFirst = false;
			break;
		}
	}

	reading.bAllDigits   = bDigitsAfterFirst && pArg[0] >= '0' && pArg[0] <= '9';
	reading.bPoundUserId = nLength > 1 && pArg[0] == '#' && bDigitsAfterFirst;
	reading.bBracketed   = nLength >= 2 && pArg[0] == '[' && pArg[ nLength - 1 ] == ']';

	const char szSteam[] = "STEAM_";
	if ( nLength >= 6 )
	{
		reading.bLegacySteamId = true;
		for ( int i = 0; i < 6; ++i )
		{
			if ( pArg[ i ] != szSteam[ i ] )
			{
				reading.bLegacySteamId = false;
				break;
			}
		}
	}

	if ( reading.bPoundUserId || reading.bAllDigits )
	{
		const char *pDigits = reading.bPoundUserId ? pArg + 1 : pArg;

		// Deliberately unsaturated, matching V_atoi: a userid that overflows an
		// int is not a userid, and it will simply match nobody.
		for ( ; *pDigits; ++pDigits )
			reading.nUserId = reading.nUserId * 10 + ( *pDigits - '0' );
	}

	return reading;
}

} // namespace PlayerNameMatch

#endif // SOURCESHARP_PLAYER_NAME_MATCH_H
