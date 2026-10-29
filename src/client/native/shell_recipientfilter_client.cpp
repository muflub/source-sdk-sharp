//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The client's recipient filter: which connected clients a message reaches, as
// a list of player slots. What stays is what the engine reads (the
// IRecipientFilter slots) and the adds the client's callers make: by PVS, by
// PAS, every player, and the local player.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "c_recipientfilter.h"

#include "local_recipient.h"

C_RecipientFilter::C_RecipientFilter()
{
	Reset();
}

C_RecipientFilter::~C_RecipientFilter() {}

void C_RecipientFilter::Reset()
{
	m_bReliable = false;
	m_Recipients.RemoveAll();
	m_bUsingPredictionRules = false;
	m_bIgnorePredictionCull = false;
}

bool C_RecipientFilter::IsReliable() const
{
	return m_bReliable;
}

int C_RecipientFilter::GetRecipientCount() const
{
	return m_Recipients.Size();
}

int C_RecipientFilter::GetRecipientIndex( int slot ) const
{
	if ( slot < 0 || slot >= GetRecipientCount() )
		return -1;

	return m_Recipients[slot];
}

// The viewer, by the index the engine names: C_BasePlayer::GetLocalPlayer()
// answers NULL in this mod. See local_recipient.h.
static int LocalPlayerSlot()
{
	return engine ? engine->GetLocalPlayer() : -1;
}

void C_RecipientFilter::AddAllPlayers()
{
	m_Recipients.RemoveAll();
	AddRecipientByPlayerIndex( LocalPlayerSlot() );
}

// The add every other add funnels through; the viewer rule is in
// local_recipient.h, and this keeps the duplicate check.
void C_RecipientFilter::AddRecipientByPlayerIndex( int playerindex )
{
	if ( !LocalRecipient_ShouldHold( playerindex, LocalPlayerSlot() ) )
		return;

	// Already in list
	if ( m_Recipients.Find( playerindex ) != m_Recipients.InvalidIndex() )
		return;

	m_Recipients.AddToTail( playerindex );
}

void C_RecipientFilter::AddRecipientsByPVS( const Vector &origin )
{
	AddAllPlayers();
}

void C_RecipientFilter::AddRecipientsByPAS( const Vector &origin )
{
	AddAllPlayers();
}

CLocalPlayerFilter::CLocalPlayerFilter()
{
	AddRecipientByPlayerIndex( LocalPlayerSlot() );
}
