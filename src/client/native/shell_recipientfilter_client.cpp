//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — who hears it, on the client ========================//
// SS_REASON a  a vtable the engine calls through
//
// game/client/c_recipientfilter.cpp, moved here whole by plan_entity.md step
// 7e. It is the CLIENT MIRROR of the file step 4b moved on the server
// (sourcesharp/game/server/shell_recipientfilter.cpp), and it moves for the
// same reason and with the same argument: a recipient filter answers "which
// connected clients does this message reach", its whole state is a list of
// PLAYER SLOTS, and it decides nothing about the game. It is the mechanism a
// managed decision is delivered through, which is plan_entity.md §6's own test
// for what may stay native.
//
// WHY STEP 7E AND NOT SOMEBODY ELSE. Step 4b's handover names the cost its own
// move landed: moving the shared SoundEmitterSystem.cpp under the shim for the
// server reclassified the same object on the CLIENT, and six of that object's
// undefined symbols became client cross-edges. THREE of the six are this file's
// -- C_RecipientFilter::CopyFrom, C_RecipientFilter::MakeReliable and
// RecipientFilter_RemoveIfNotCloseCaptioning -- and sound_emitter.o is their
// ONLY shim consumer, measured with `nm -C -u` over all ninety client shim
// objects. Moving this file is what makes those three stop being cross-edges,
// and 4b said in as many words that step 7 is where they die.
//
// IT COSTS NOTHING TO MOVE, and that was measured rather than assumed. The
// object's own undefined symbols that resolve in a stock client TU are sixteen,
// every one of them the vtable floor cbase.h drags in (CNewParticleEffect's
// virtuals, DefaultRenderBoundsWorldspace), and every one of the sixteen is
// ALREADY a cross-edge from another shim object. The census counts DISTINCT
// symbols, so a second consumer of an existing edge adds nothing:
//
//     comm -12 <sorted nm -u of this object ∩ stock defs> <the census edge list>
//         -> 16 of 16 already present, 0 new.
//
// THE ENTITY-SHAPED SIGNATURES THAT REMAIN are IRecipientFilter's own, declared
// in game/client/c_recipientfilter.h, and they are types in a parameter list
// rather than entity work: AddRecipient/RemoveRecipient ask the player for
// MvEntIndex(), which is IMovementPlayer's and which every hierarchy answers,
// and AddRecipientsByTeam/RemoveRecipientsByTeam never look at the team at all.
// Nothing here constructs, owns, resolves or casts an entity. `tools/entity-census`
// reads a shim file's TEXT, so this is stated rather than left to be rediscovered.
//
// WHAT IS LEFT, 2026-10-25. The members no object on the client link edge
// imports left with no reader (nm -u): CopyFrom, MakeReliable, AddRecipient,
// RemoveRecipient, the two team adds, AddPlayersFromBitMask, the prediction
// rules (UsePredictionRules and its three accessors) and
// RecipientFilter_RemoveIfNotCloseCaptioning. Their callers -- sound_emitter.o
// and the stock TUs above -- went before them. What stays is what the engine
// reads (the IRecipientFilter slots) and the four adds the client's callers
// make: by PVS, by PAS, every player, and the local player's filter.
//
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "c_recipientfilter.h"
#include "tier1/utlvector.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "collisionproperty.h"
#include "local_recipient.h"

// memdbgon must be the last include file in a .cpp file!!!

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
C_RecipientFilter::C_RecipientFilter()
{
	Reset();
}

C_RecipientFilter::~C_RecipientFilter()
{
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void C_RecipientFilter::Reset( void )
{
	m_bReliable			= false;
	m_Recipients.RemoveAll();
	m_bUsingPredictionRules = false;
	m_bIgnorePredictionCull = false;
}

bool C_RecipientFilter::IsReliable( void ) const
{
	return m_bReliable;
}

int C_RecipientFilter::GetRecipientCount( void ) const
{
	return m_Recipients.Size();
}

int C_RecipientFilter::GetRecipientIndex( int slot ) const
{
	if ( slot < 0 || slot >= GetRecipientCount() )
		return -1;

	return m_Recipients[ slot ];
}

//-----------------------------------------------------------------------------
// The viewer, as the engine names them: an INDEX, stated before any entity has
// decoded. C_BasePlayer::GetLocalPlayer() answers NULL in this mod -- the
// viewer is a C_StandalonePlayer -- so asking it made every client filter
// empty and every predicted temp entity get suppressed. See local_recipient.h.
//-----------------------------------------------------------------------------
static int LocalPlayerSlot()
{
	return engine ? engine->GetLocalPlayer() : -1;
}

void C_RecipientFilter::AddAllPlayers( void )
{
	m_Recipients.RemoveAll();
	AddRecipientByPlayerIndex( LocalPlayerSlot() );
}

//-----------------------------------------------------------------------------
// The add every other add funnels through.
//
// The rule -- one viewer, named by slot -- is in local_recipient.h so it can be
// tested off the client. What is left here is the duplicate check, which is
// about this filter's storage and nothing else.
//-----------------------------------------------------------------------------
void C_RecipientFilter::AddRecipientByPlayerIndex( int playerindex )
{
	if ( !LocalRecipient_ShouldHold( playerindex, LocalPlayerSlot() ) )
		return;

	// Already in list
	if ( m_Recipients.Find( playerindex ) != m_Recipients.InvalidIndex() )
		return;

	m_Recipients.AddToTail( playerindex );
}

void C_RecipientFilter::AddRecipientsByPVS( const Vector& origin )
{
	AddAllPlayers();
}

void C_RecipientFilter::AddRecipientsByPAS( const Vector& origin )
{
	AddAllPlayers();
}

CLocalPlayerFilter::CLocalPlayerFilter()
{
	AddRecipientByPlayerIndex( LocalPlayerSlot() );
}

