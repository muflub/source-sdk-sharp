//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the player slot walk ===============================//
// SS_REASON b  an interface resolved by name at load
//
// plan_entity.md Phase 4, step 4e (Utilities). The five members of
// game/server/util.cpp's slot-walk family, moved to the shim whole.
//
// WHY THEY BELONG HERE. Step 4e's paragraph asks that every utility the shim
// still calls be either managed or a shim mechanism function taking handles.
// This family already met that rule before the step began, and not by accident:
// it exists BECAUSE the four stock spellings it replaces return a player
// pointer, and this build has no class for one, so all four answer "nobody"
// whatever a console argument says. Every member here is keyed on a SLOT
// instead — the identity the ENGINE states, which both hierarchies share and
// neither owns — and not one of them returns an entity of any kind.
//
// So what was left was the address. util.cpp is one of the files Phase 4 is
// driving to zero, and six of the shim's twenty-nine cross-edges into it were
// these; they are the shim's own mechanism sitting in stock's translation unit.
//
// WHAT IS NOT HERE, AND THAT IS THE POINT. The RULE — how a typed name is
// compared, when two matches are ambiguous, how a console argument is read —
// is player_name_match.h, has no engine in it and is gtested on its own. This
// file is only the WALK: which slots are occupied, and what name, userid and
// flags each holds. Splitting them that way is what let the rule be tested at
// all, and moving the walk does not put them back together.
//
// EVERY DECLARATION STAYS IN game/server/util.h, so no caller changed.
//
//=============================================================================//

#include "ss_util.h"   // ss::INDEXENT, and engine / eiface.h
#include "inetchannelinfo.h"

extern CGlobalVars *gpGlobals;   // shell_engineboundary.cpp

// The rule half. Engine-free and gtested; this file is the walk that feeds it.
#include "player_name_match.h"

// The slot table: what answers for a player in this build.
#include "shell_standalone_player.h"
#include "ss_string.h"


//-----------------------------------------------------------------------------
// FINDING A PLAYER BY WHAT SOMEBODY TYPED, answered as a slot. The long form of
// why the answer is a slot rather than a pointer is on the declarations in
// util.h; the short form is that the four stock spellings return CBasePlayer *
// and this build has none, so all four answer "nobody" whatever you type.
//
// The rule itself -- the comparisons, the ambiguity, and the reading of a
// console argument -- is in player_name_match.h with no engine in it, and is
// gtested. What is here is only the walk: which slots are occupied, and what
// name and userid each holds.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Purpose: the name of the player in a slot, on whichever hierarchy holds them
// Input  : nSlot - 1-based
// Output : the name, or "" for an empty slot -- never NULL, so a comparison
//          against it cannot be skipped by accident
//-----------------------------------------------------------------------------
const char *UTIL_PlayerNameBySlot( int nSlot )
{
	if ( nSlot < 1 || nSlot > gpGlobals->maxClients )
		return "";

	// The slot table, which is the fact the engine stated. PlayerName() is the
	// string ClientPutInServer was handed, which is what GetPlayerName() reads
	// out of m_PlayerState.netname on the SDK side -- the same name, reached
	// without asking what class is in the slot.
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromIndex( nSlot ) )
		return pStandalone->PlayerName();

	// No CBasePlayer arm: server.so builds none, so UTIL_PlayerByIndex answered
	// NULL here on every path (plan_server_novalve.md P5 wave 8b).
	return "";
}

//-----------------------------------------------------------------------------
// Purpose: the userid of the player in a slot, asked of the ENGINE
//
// engine->GetPlayerUserId takes an edict and knows nothing about game classes,
// so it is the one source of this fact that both hierarchies share. Stock asked
// CBasePlayer::GetUserID(), which is this call wrapped in a class that does not
// exist here -- the wrapper was the whole problem.
//
// Output : the userid, or 0. Zero is the engine's "the world" and is never a
//          live player, so it doubles as "nobody" safely.
//-----------------------------------------------------------------------------
int UTIL_PlayerUserIdBySlot( int nSlot )
{
	if ( nSlot < 1 || nSlot > gpGlobals->maxClients )
		return 0;

	edict_t *pEdict = ss::INDEXENT( nSlot );

	if ( !pEdict || pEdict->IsFree() )
		return 0;

	return engine->GetPlayerUserId( pEdict );
}

//-----------------------------------------------------------------------------
// Purpose: the entity flags of the player in a slot, on either hierarchy
// Output : the flags, or 0 for an empty slot. FL_FAKECLIENT is the bit anything
//          here reads -- "is this a bot" -- and it is on both kinds.
//-----------------------------------------------------------------------------
static int UTIL_PlayerFlagsBySlot( int nSlot )
{
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromIndex( nSlot ) )
		return pStandalone->MvGetFlags();

	// The SDK arm only. This function is already dual-hierarchy -- the
	// CStandalonePlayer arm above answers MvGetFlags for every player in this
	// mod -- so the CBasePlayer disposition lane U is deciding does not reach
	// here: the arm goes whatever that decision turns out to be, and the
	// documented "0 for an empty slot" is what is left.

	return 0;
}

//-----------------------------------------------------------------------------
// Purpose: whether anybody is in this slot at all, on either hierarchy
//-----------------------------------------------------------------------------
static bool UTIL_PlayerSlotOccupied( int nSlot )
{
	// A standalone player, or nobody: UTIL_PlayerByIndex, which this also
	// asked, is NULL on every path in a server.so with no CBasePlayer
	// (plan_server_novalve.md P5 wave 8b).
	return CStandalonePlayer::FromIndex( nSlot ) != NULL;
}

int UTIL_PlayerSlotByName( const char *pName )
{
	if ( !pName || !pName[0] )
		return PlayerNameMatch::NOBODY;

	// A Search_t rather than an early return, so that two players who have
	// chosen the same name answer AMBIGUOUS instead of whichever slot came
	// first. Stock returns the first, which is a coin toss the person typing
	// cannot see -- and names are not unique on a server that does not rename
	// duplicates, which this one does not.
	PlayerNameMatch::Search_t search;

	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		if ( !UTIL_PlayerSlotOccupied( i ) )
			continue;

		if ( PlayerNameMatch::ExactMatch( UTIL_PlayerNameBySlot( i ), pName ) )
			search.Offer( i );
	}

	return search.Result();
}

int UTIL_PlayerSlotByPartialName( const char *pName )
{
	if ( !pName || !pName[0] )
		return PlayerNameMatch::NOBODY;

	PlayerNameMatch::Search_t search;

	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		if ( !UTIL_PlayerSlotOccupied( i ) )
			continue;

		if ( PlayerNameMatch::PartialMatch( UTIL_PlayerNameBySlot( i ), pName ) )
			search.Offer( i );
	}

	return search.Result();
}

int UTIL_PlayerSlotByUserId( int nUserId )
{
	// Zero is the world. Answering it would hand back whichever slot the engine
	// has not yet assigned a userid to, which is a live slot during a join.
	if ( nUserId == 0 )
		return PlayerNameMatch::NOBODY;

	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		if ( !UTIL_PlayerSlotOccupied( i ) )
			continue;

		if ( UTIL_PlayerUserIdBySlot( i ) == nUserId )
			return i;
	}

	return PlayerNameMatch::NOBODY;
}

//-----------------------------------------------------------------------------
// Purpose: one console argument, read every way stock reads one
//
// Stock's priority order, kept: a pounded userid, then a partial name, then a
// bare number as a userid -- and a collision between two readings is ambiguous,
// except that the bare-number reading is only tried when nothing else matched
// and so can never collide.
//
// THE TWO STEAM-ID READINGS ARE GONE, and say so rather than silently not
// matching. UTIL_PlayerBySteamID has answered NULL since 13c ("there are no
// CBasePlayers to enumerate") and this server runs -insecure on a LAN, where no
// client has a Steam identity to match against. A bracketed or STEAM_-prefixed
// argument is therefore refused BY NAME instead of falling through to the
// partial-name search, which would otherwise silently match a player whose name
// happened to contain it.
//-----------------------------------------------------------------------------
int UTIL_PlayerSlotByCommandArg( const char *pArg )
{
	const PlayerNameMatch::ArgReading_t reading = PlayerNameMatch::ReadArg( pArg );

	if ( reading.bEmpty )
		return PlayerNameMatch::NOBODY;

	if ( reading.bBracketed || reading.bLegacySteamId )
	{
		Warning( "%s looks like a Steam ID, and this server has no Steam identities to "
		         "match it against -- it runs -insecure on a LAN. Name the player, or "
		         "use #<userid>.\n", pArg );
		return PlayerNameMatch::NOBODY;
	}

	PlayerNameMatch::Search_t search;

	if ( reading.bPoundUserId )
		search.Offer( UTIL_PlayerSlotByUserId( reading.nUserId ) );

	search.Offer( UTIL_PlayerSlotByPartialName( pArg ) );

	// The bare number, last and only when nothing above matched -- stock's
	// "does not count as ambiguous with higher priority items".
	if ( reading.bAllDigits && search.Result() == PlayerNameMatch::NOBODY )
		search.Offer( UTIL_PlayerSlotByUserId( reading.nUserId ) );

	return search.Result();
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : playerIndex - 
//			ping - 
//			packetloss - 
//-----------------------------------------------------------------------------
void UTIL_GetPlayerConnectionInfo( int playerIndex, int& ping, int &packetloss )
{
	// A REAL CLIENT IN THIS SLOT, on whichever hierarchy holds them. This asked
	// UTIL_PlayerByIndex for a CBasePlayer and this build has none, so the else
	// branch below ran for every slot and every player's ping was 0 -- the
	// fifth member of this file's slot-walk family, and the one with nothing
	// visible to give it away.
	//
	// Everything the calculation actually needs was already addressed by INDEX:
	// engine->GetPlayerNetInfo and engine->GetClientConVarValue both take one
	// and neither knows what a CBasePlayer is. The pointer was there for one
	// bit -- IsBot, which is FL_FAKECLIENT on the player's flags -- and that bit
	// is on the standalone player too.
	//
	// NOTHING IN THIS BUILD CALLS THIS, and it is fixed anyway rather than
	// declared dead: CPlayerResource::UpdateConnectedPlayer, its only compiled
	// caller, returns early because the managed scoreboard is the authority
	// (13c), and there is no managed ping. Leaving the last member of a family
	// broken while its four neighbours are fixed is precisely how this one
	// stayed hidden -- the next person to put a ping on a HUD would get zero and
	// no error.
	const bool bRealClient = UTIL_PlayerSlotOccupied( playerIndex ) &&
	                         ( UTIL_PlayerFlagsBySlot( playerIndex ) & FL_FAKECLIENT ) == 0;

	INetChannelInfo *nci = engine->GetPlayerNetInfo(playerIndex);

	if ( nci && bRealClient )
	{
		float latency = nci->GetAvgLatency( FLOW_OUTGOING ); // in seconds
		
		// that should be the correct latency, we assume that cmdrate is higher 
		// then updaterate, what is the case for default settings
		const char * szCmdRate = engine->GetClientConVarValue( playerIndex, "cl_cmdrate" );
		
		int nCmdRate = MAX( 1, ss::AtoI( szCmdRate ) );
		latency -= (0.5f/nCmdRate) + ( gpGlobals->interval_per_tick * 1.0f )   /* shareddefs.h TICKS_TO_TIME( 1.0f ) */; // correct latency

		// in GoldSrc we had a different, not fixed tickrate. so we have to adjust
		// Source pings by half a tick to match the old GoldSrc pings.
		latency -= ( gpGlobals->interval_per_tick * 0.5f )   /* shareddefs.h TICKS_TO_TIME( 0.5f ) */;

		ping = latency * 1000.0f; // as msecs
		ping = clamp( ping, 5, 1000 ); // set bounds, dont show pings under 5 msecs
		
		packetloss = 100.0f * nci->GetAvgLoss( FLOW_INCOMING ); // loss in percentage
		packetloss = clamp( packetloss, 0, 100 );
	}
	else
	{
		ping = 0;
		packetloss = 0;
	}
}


//-----------------------------------------------------------------------------
// UTIL_PlayerByIndex AND UTIL_SDKPlayerFromEdict WERE HERE, and are DELETED
// (P5 lane E). Both answered NULL on every path: server.so builds no
// CBasePlayer, so no edict can hold one (UTIL_SDKPlayerFromEdict's own comment
// said so). Their last caller, shell_standalone_player.cpp's
// `!UTIL_PlayerByIndex( i )`, went with engineapi's ad131c0, and
// shell_clientsteps.cpp's SDK ear-position step answers 0 without asking. The
// slot-keyed answer for a player is CStandalonePlayer::FromIndex / FromEdict.
//-----------------------------------------------------------------------------
