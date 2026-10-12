//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the server's recipient filters, as server.so owns them
//
// plan_server_novalve.md P5. The engine sees a recipient filter only as an
// IRecipientFilter (public/irecipientfilter.h), through its vtable: who the
// message goes to, and whether it is reliable or an init message. Everything
// else -- how the list is built (all players, the PAS or PVS of a point, a
// sound's audible range) -- is server.so's own business, so per the plan's
// naming rule these classes live in namespace ss and are built on public/
// headers alone. game/server/recipientfilter.h's classes of the same names are
// no longer defined anywhere in server.so.
//
// Players are named by slot index (1..maxClients), never by pointer: the old
// pointer-taking adds asked the player for its index and nothing else, and a
// player on the standalone hierarchy has an index but no CBasePlayer.
//
// shell_recipientfilter.cpp implements these; ss_recipient_filter_test holds
// the list logic.
//=============================================================================//

#ifndef SOURCESHARP_SS_RECIPIENT_FILTER_H
#define SOURCESHARP_SS_RECIPIENT_FILTER_H
#ifdef _WIN32
#pragma once
#endif

#include "irecipientfilter.h"
#include "const.h"
#include "soundflags.h"
#include "bitvec.h"
#include "basehandle.h"
#include "igameentity.h"
#include "mathlib/vector.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

#include <vector>

namespace ss
{

// A list of player slots, plus the two flags the engine asks for.
class CRecipientFilter : public IRecipientFilter
{
public:
	CRecipientFilter() = default;
	virtual ~CRecipientFilter() = default;

	// IRecipientFilter -- what the engine reads.
	virtual bool IsReliable( void ) const { return m_bReliable; }
	virtual bool IsInitMessage( void ) const { return m_bInitMessage; }
	virtual int GetRecipientCount( void ) const { return (int)m_Recipients.size(); }
	virtual int GetRecipientIndex( int slot ) const;

	void CopyFrom( const CRecipientFilter &src );
	void Reset( void );

	void MakeReliable( void ) { m_bReliable = true; }
	void MakeInitMessage( void ) { m_bInitMessage = true; }

	// Every connected player's slot.
	void AddAllPlayers( void );
	// The players whose PVS / PAS contains the point (everyone on a one-player server).
	void AddRecipientsByPVS( const Vector &origin );
	void AddRecipientsByPAS( const Vector &origin );

	// One slot, 1..maxClients; anything else, or a slot already listed, is ignored.
	void AddRecipientByPlayerIndex( int playerindex );
	void RemoveRecipientByPlayerIndex( int playerindex );
	void RemoveAllRecipients( void ) { m_Recipients.clear(); }

	void AddPlayersFromBitMask( CBitVec< ABSOLUTE_PLAYER_LIMIT > &playerbits );

	// Prediction rules: carried for the callers that set and read them.
	void UsePredictionRules( void ) { m_bUsingPredictionRules = true; }
	bool IsUsingPredictionRules( void ) const { return m_bUsingPredictionRules; }
	bool IgnorePredictionCull( void ) const { return m_bIgnorePredictionCull; }
	void SetIgnorePredictionCull( bool ignore ) { m_bIgnorePredictionCull = ignore; }

private:
	bool m_bReliable = false;
	bool m_bInitMessage = false;
	std::vector< int > m_Recipients;
	bool m_bUsingPredictionRules = false;
	bool m_bIgnorePredictionCull = false;
};

// One player, by slot.
class CSingleUserRecipientFilter : public CRecipientFilter
{
public:
	explicit CSingleUserRecipientFilter( int playerIndex ) { AddRecipientByPlayerIndex( playerIndex ); }
};

// Everyone connected.
class CBroadcastRecipientFilter : public CRecipientFilter
{
public:
	CBroadcastRecipientFilter( void ) { AddAllPlayers(); }
};

// Everyone connected, reliably.
class CReliableBroadcastRecipientFilter : public CBroadcastRecipientFilter
{
public:
	CReliableBroadcastRecipientFilter( void ) { MakeReliable(); }
};

// Whoever can hear a point (its potentially audible set).
class CPASFilter : public CRecipientFilter
{
public:
	CPASFilter( void ) {}
	explicit CPASFilter( const Vector &origin ) { AddRecipientsByPAS( origin ); }
};

// The PAS of a point, narrowed to the players within a sound's audible range.
class CPASAttenuationFilter : public CPASFilter
{
public:
	CPASAttenuationFilter( void ) {}
	CPASAttenuationFilter( IGameEntity *entity, soundlevel_t soundlevel );
	CPASAttenuationFilter( IGameEntity *entity, float attenuation = ATTN_NORM );
	CPASAttenuationFilter( const Vector &origin, soundlevel_t soundlevel )
		: CPASFilter( origin )
	{
		Filter( origin, SNDLVL_TO_ATTN( soundlevel ) );
	}
	CPASAttenuationFilter( const Vector &origin, float attenuation = ATTN_NORM )
		: CPASFilter( origin )
	{
		Filter( origin, attenuation );
	}
	CPASAttenuationFilter( IGameEntity *entity, const char *lookupSound );
	CPASAttenuationFilter( const Vector &origin, const char *lookupSound );
	CPASAttenuationFilter( const CBaseHandle &hSource, const char *lookupSound );
	CPASAttenuationFilter( IGameEntity *entity, const char *lookupSound, HSOUNDSCRIPTHANDLE &handle );
	CPASAttenuationFilter( const Vector &origin, const char *lookupSound, HSOUNDSCRIPTHANDLE &handle );

	// Drops the listed players farther from origin than the attenuation reaches.
	void Filter( const Vector &origin, float attenuation = ATTN_NORM );
};

// Whoever can see a point (its potentially visible set).
class CPVSFilter : public CRecipientFilter
{
public:
	explicit CPVSFilter( const Vector &origin ) { AddRecipientsByPVS( origin ); }
};

// Drops the players whose closecaption setting turns captions off.
void RecipientFilter_RemoveIfNotCloseCaptioning( CRecipientFilter &filter );

} // namespace ss

#endif // SOURCESHARP_SS_RECIPIENT_FILTER_H
