//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — who hears it ========================================//
// SS_REASON a  a vtable the engine calls through
//
// game/server/recipientfilter.cpp, moved here whole by plan_entity.md step 4b.
//
// WHY IT IS A SHIM FILE AND NOT STOCK. A recipient filter answers one question
// -- "which connected clients does this message reach" -- and its whole state
// is a list of PLAYER SLOTS. It decides nothing about the game; it is the
// mechanism a decision made in C# is delivered through, which is §6's own test
// for what may stay native. Nothing in it constructs, owns or names an entity
// any more: the four remaining bodies that took a CBaseEntity * are the
// soundscript constructors stock callers still spell, and the HANDLE-shaped
// constructors beside them are what this port's own code uses.
//
// WHAT LEFT WITH THE MOVE, and none of it was reachable:
//
//   * the `#ifndef SS_CBASEENTITY_ALIAS` prediction-suppression block in
//     AddRecipientByPlayerIndex, and the ToBasePlayer cull in
//     UsePredictionRules. Both read IPredictionSystem's suppress host, which is
//     NULL on every path in this build -- physics_main.cpp's writer is inside
//     `#ifndef SOURCESHARP`, player.cpp is not in the vpc at all, and
//     takedamageinfo.cpp's ApplyMultiDamage only saves and restores whatever it
//     read. suppress_host_dead_test.cpp holds that as a fact over the compiled
//     TU list, so the day a writer appears the test is red and names these two
//     sites rather than leaving them silently missing.
//   * the SDK tail of CPASAttenuationFilter::Filter. Every recipient index
//     names a connected client and every connected client is a standalone
//     player, so the arm above it had already `continue`d for all of them.
//
// SIX §3.16 CAST SITES WENT WITH THOSE THREE BLOCKS: two C-style casts to a
// base entity pointer, two ToBasePlayer calls and two uses of the base
// entity's static Instance. They are spelled in prose here on purpose --
// tools/entity-census counts SPELLINGS over the file's text, so a comment
// quoting a cast is a cast as far as the gate is concerned, and a shim file
// that quotes what it deleted has not removed it from the count.
//
//=============================================================================//
#include "ss_util.h"            // engine, ss::INDEXENT

// The engine's globals (connected in shell_gamedll.cpp); cbase.h supplied them.
extern CGlobalVars *gpGlobals;
#include "ss_recipient_filter.h"

#include <algorithm>
// team.h: removed with team.cpp, which is out of the build entirely
// (server_csharp.vpc). Nothing here had named a CTeam since
// CTeamRecipientFilter's constructor went -- see the header's note and the
// deleted-constructor marker further down -- so this was an include kept alive
// by a class that had already left.
#include "closecaption_recipient.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

// The soundscript system, which is what CBaseEntity::LookupSoundLevel forwarded
// to. Declared the same way SoundEmitterSystem.cpp declares it.
extern ISoundEmitterSystemBase *soundemitterbase;
#include "shell_standalone_player.h"
// The slot table, for the handle-shaped CPASAttenuationFilter constructors
// below: a handle is the one identity both entity hierarchies share, so a
// filter built from one crops correctly around a source that has no
// CBaseEntity to ask.
#include "sourcesharp.h"
#include "shell_resolve.h"

// g_RecipientFilterPredictionSystem WAS HERE: a bare IPredictionSystem whose
// one effect was to join g_pPredictionSystems at static init. Nothing on the
// server walks that list (nm over every server object: no SuppressEvents,
// SuppressHostEvents or CDisablePredictionFiltering), and nothing here read it
// after the prediction rules above went, so it went too (P5 lane SP).

void ss::CRecipientFilter::AddAllPlayers( void )
{
	m_Recipients.clear();

	// By slot, not by UTIL_PlayerByIndex: that helper answers only for players
	// on the SDK hierarchy, and under SourceSharp the players are not. A slot
	// in 1..maxClients holds a player edict or nothing — the engine reserves
	// those indices — so a non-free edict there IS a connected player,
	// whichever hierarchy its entity lives on.
	int i;
	for ( i = 1; i <= gpGlobals->maxClients; i++ )
	{
		edict_t *pEdict = ss::INDEXENT( i );
		if ( !pEdict || pEdict->IsFree() || !pEdict->GetUnknown() )
			continue;

		AddRecipientByPlayerIndex( i );
	}
}

void ss::CRecipientFilter::AddRecipientsByPVS( const Vector& origin )
{
	if ( gpGlobals->maxClients == 1 )
	{
		AddAllPlayers();
	}
	else
	{
		CBitVec< ABSOLUTE_PLAYER_LIMIT > playerbits;
		engine->Message_DetermineMulticastRecipients( false, origin, playerbits );
		AddPlayersFromBitMask( playerbits );
	}
}

void ss::CRecipientFilter::AddRecipientsByPAS( const Vector& origin )
{
	if ( gpGlobals->maxClients == 1 )
	{
		AddAllPlayers();
	}
	else
	{
		CBitVec< ABSOLUTE_PLAYER_LIMIT > playerbits;
		engine->Message_DetermineMulticastRecipients( true, origin, playerbits );
		AddPlayersFromBitMask( playerbits );
	}
}

// CTeamRecipientFilter's constructor was here; deleted, see the header's note.

//-----------------------------------------------------------------------------
// THE HANDLE-SHAPED CONSTRUCTOR. See recipientfilter.h for why it is not a
// wrapper over the pointer form.
//
// WHY THE CENTRE AND NOT THE ORIGIN, said with the number rather than the
// principle. Stock's four pointer constructors take
// entity->GetSoundEmissionOrigin(), whose default body (baseentity.h) is
// WorldSpaceCenter(). SourceSharpEntity_t::Center() is that same quantity for
// both hierarchies -- origin plus the middle of the OBB. A brush entity's
// m_vecOrigin is 0,0,0 unless the mapper gave it an origin brush, so taking
// Origin() here would crop a door's move loop around the MAP ORIGIN and hand
// it to whoever happens to be in the PAS of that point. The sound itself is
// unaffected either way when the entity has an edict -- the engine attaches it
// by index -- which is exactly what makes the wrong answer invisible.
//
// CBeam is the one class in stock that overrides GetSoundEmissionOrigin away
// from the centre (beam_shared.h, to the beam's midpoint). Nothing reaches this
// constructor with a beam: its callers are the port's own emitters, and a beam
// on this side is a temp entity rather than an entity that makes noise. Named
// here rather than left as a silent difference.
//-----------------------------------------------------------------------------
ss::CPASAttenuationFilter::CPASAttenuationFilter( const CBaseHandle &hSource, const char *lookupSound )
{
	const SourceSharpEntity_t ent =
		SourceSharp_ResolveEntity( (uint32_t)hSource.ToInt() );

	// An unresolvable source is heard by nobody. CPASFilter's default
	// constructor has already left the recipient list empty; returning is what
	// keeps it that way.
	if ( !ent.IsValid() )
		return;

	const Vector vecCenter = ent.Center();

	AddRecipientsByPAS( vecCenter );

	const soundlevel_t level = soundemitterbase->LookupSoundLevel( lookupSound );
	Filter( vecCenter, SNDLVL_TO_ATTN( level ) );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : origin - 
//			ATTN_NORM - 
//-----------------------------------------------------------------------------
void ss::CPASAttenuationFilter::Filter( const Vector& origin, float attenuation /*= ATTN_NORM*/ )
{
	// Don't crop for attenuation in single player
	if ( gpGlobals->maxClients == 1 )
		return;

	// CPASFilter adds them by pure PVS in constructor
	if ( attenuation <= 0 )
		return;

	// Now remove recipients that are outside sound radius
	Vector vecRelative;

	int c = GetRecipientCount();
	
	for ( int i = c - 1; i >= 0; i-- )
	{
		int index = GetRecipientIndex( i );

		// The standalone player's ears, the same arm CServerGameClients::
		// ClientEarPosition already carries. Instance() below answers NULL for
		// a parallel-hierarchy player, so before the recipient filters were
		// fixed this loop never ran at all -- the filter was EMPTY, c was 0,
		// and the Assert could not fire. Populating the filters made the loop
		// run and the assert fire on the first sound near a player.
		//
		// The `continue` on the assert path is not harmless: it KEEPS the
		// recipient, so a sound that should have been cropped by distance was
		// audible at any range. Cropping this arm restores that.
		if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict(
		         engine->PEntityOfEntIndex( index ) ) )
		{
			// Neither HLTV nor a replay bot can be a standalone player, so the
			// exemption below does not apply here.
			VectorSubtract( pStandalone->EyePosition(), origin, vecRelative );
			if ( VectorLength( vecRelative ) >
			     ( 2 * SOUND_NORMAL_CLIP_DIST ) / attenuation )
			{
				RemoveRecipientByPlayerIndex( index );
			}

			continue;
		}

		// STOCK'S SDK TAIL WAS HERE, and every recipient had already taken the
		// arm above it. A recipient index names a CONNECTED CLIENT, and every
		// connected client in this build is a standalone player -- so the
		// `continue` above has already cropped or kept each one and this tail
		// could not be reached.
		//
		// Its three asks all died with the hierarchy they were about:
		// CBaseEntity::Instance is a static of a class this port does not build
		// players on; IsPlayer is a game question the engine never asks; and
		// EarPosition is CBasePlayer's. ToBasePlayer is not a retype for it
		// either -- it answers NULL for a player on the parallel hierarchy by
		// construction.
		//
		// The HLTV/Replay exemption went with it and is not lost: neither can be
		// a standalone player, which is what the arm above already records.
	}
}

//-----------------------------------------------------------------------------
// plan_baseentity.md Phase 13 precondition (e): moved here from
// CBaseEntity::RemoveRecipientsIfNotCloseCaptioning.
//
// It was a STATIC member of CBaseEntity that never touched an entity. Its whole
// subject is a recipient filter and a per-client convar; the class it hung off
// supplied nothing but a namespace, and that namespace is being deleted. Same
// move, and the same reason, as the CPASAttenuationFilter constructors that
// came here when baseentity.h left this file's include list.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Purpose: For each client who appears to be a valid recipient, checks the client has disabled CC and if so, removes them from 
//  the recipient list.
// Input  : filter - 
//-----------------------------------------------------------------------------
void ss::RecipientFilter_RemoveIfNotCloseCaptioning( ss::CRecipientFilter& filter )
{
	int c = filter.GetRecipientCount();
	for ( int i = c - 1; i >= 0; --i )
	{
		int playerIndex = filter.GetRecipientIndex( i );

		// THE FIFTEENTH SITE, and it was not in the hand-taken inventory of
		// fourteen in docs/divergences.md -- sdk_player_cast_test.cpp found it
		// the first time it ran, which is the whole argument for that test
		// existing. Same shape as the other fourteen:
		//
		//     a static_cast to a player pointer, wrapped round the base
		//     entity's static Instance of the slot, followed by
		//     `if ( !player ) continue;`
		//
		// (spelled in prose rather than in code, for the reason the file
		// banner gives: the cast census reads this file's text.)
		//
		// NULL today for the standalone player, so the `continue` ALWAYS fires
		// and this function removes nobody: close captions go to every
		// recipient regardless of what their closecaption convar says. At Phase
		// 13 step 5 the NULL check passes instead, and RemoveRecipient's body
		// is `player->entindex()` -- a virtual call through the wrong vtable.
		//
		// The site already HAS the slot. Stock converted it to a pointer so
		// that RemoveRecipient could convert it back to a slot, and
		// RemoveRecipientByPlayerIndex is that function with the round trip
		// taken out. Nothing else here reads `player`.
#if !defined( _XBOX )
		// The rule -- and the empty-string case that is NOT the same as "0" --
		// is closecaption_recipient.h, with its own tests. See the header:
		// atoi("") and atoi("0") are both zero and mean opposite things.
		const char *cvarvalue = engine->GetClientConVarValue( playerIndex, "closecaption" );
		Assert( cvarvalue );
		const bool bRemove = CloseCaption_ShouldRemoveRecipient( cvarvalue );
#else
		static ConVar *s_pCloseCaption = NULL;
		if ( !s_pCloseCaption )
		{
			s_pCloseCaption = cvar->FindVar( "closecaption" );
			if ( !s_pCloseCaption )
			{
				Error( "XBOX couldn't find closecaption convar!!!" );
			}
		}

		// No "not reported yet" state here: the convar is this machine's own.
		const bool bRemove = ( s_pCloseCaption->GetInt() == 0 );
#endif
		if ( bRemove )
		{
			filter.RemoveRecipientByPlayerIndex( playerIndex );
		}
	}
}
