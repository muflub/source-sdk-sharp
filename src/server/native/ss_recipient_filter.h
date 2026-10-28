//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The server's recipient filters. The engine reads them only through
// IRecipientFilter; players are named by slot index (1..maxClients), never by pointer.

#pragma once

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

	virtual bool IsReliable() const { return m_bReliable; }
	virtual bool IsInitMessage() const { return m_bInitMessage; }
	virtual int GetRecipientCount() const { return (int)m_Recipients.size(); }
	virtual int GetRecipientIndex( int slot ) const;

	void Reset();

	void MakeReliable() { m_bReliable = true; }
	void MakeInitMessage() { m_bInitMessage = true; }

	// Every connected player's slot.
	void AddAllPlayers();
	// The players whose PVS / PAS contains the point (everyone on a one-player server).
	void AddRecipientsByPVS( const Vector &origin );
	void AddRecipientsByPAS( const Vector &origin );

	// One slot, 1..maxClients; anything else, or a slot already listed, is ignored.
	void AddRecipientByPlayerIndex( int playerindex );
	void RemoveAllRecipients() { m_Recipients.clear(); }

	void AddPlayersFromBitMask( CBitVec< ABSOLUTE_PLAYER_LIMIT > &playerbits );

	// Prediction rules: carried for the callers that set and read them.
	void UsePredictionRules() { m_bUsingPredictionRules = true; }
	bool IsUsingPredictionRules() const { return m_bUsingPredictionRules; }
	bool IgnorePredictionCull() const { return m_bIgnorePredictionCull; }
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
	CBroadcastRecipientFilter() { AddAllPlayers(); }
};

// Everyone connected, reliably.
class CReliableBroadcastRecipientFilter : public CBroadcastRecipientFilter
{
public:
	CReliableBroadcastRecipientFilter() { MakeReliable(); }
};

// Whoever can hear a point (its potentially audible set).
class CPASFilter : public CRecipientFilter
{
public:
	CPASFilter() {}
	explicit CPASFilter( const Vector &origin ) { AddRecipientsByPAS( origin ); }
};

// Whoever can see a point (its potentially visible set).
class CPVSFilter : public CRecipientFilter
{
public:
	explicit CPVSFilter( const Vector &origin ) { AddRecipientsByPVS( origin ); }
};

} // namespace ss
