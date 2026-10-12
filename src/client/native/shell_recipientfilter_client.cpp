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
#include "cbase.h"
#include "ipredictionsystem.h"
#include "c_recipientfilter.h"
#include "prediction.h"
#include "local_recipient.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

static IPredictionSystem g_RecipientFilterPredictionSystem;

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
// Input  : src - 
//-----------------------------------------------------------------------------
void C_RecipientFilter::CopyFrom( const C_RecipientFilter& src )
{
	m_bReliable = src.IsReliable();
	m_bInitMessage = src.IsInitMessage();

	m_bUsingPredictionRules = src.IsUsingPredictionRules();
	m_bIgnorePredictionCull = src.IgnorePredictionCull();

	int c = src.GetRecipientCount();
	for ( int i = 0; i < c; ++i )
	{
		m_Recipients.AddToTail( src.GetRecipientIndex( i ) );
	}
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

void C_RecipientFilter::MakeReliable( void )
{
	m_bReliable = true;
}

bool C_RecipientFilter::IsReliable( void ) const
{
	return m_bReliable;
}

int C_RecipientFilter::GetRecipientCount( void ) const
{
	return m_Recipients.Size();
}

int	C_RecipientFilter::GetRecipientIndex( int slot ) const
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

void C_RecipientFilter::AddRecipient( C_BasePlayer *player )
{
	Assert( player );

	if ( !player )
		return;

	// Which slot, asked through IMovementPlayer rather than through the entity
	// — the same rewrite CRecipientFilter::AddRecipient carries on the server.
	AddRecipientByPlayerIndex( player->MvEntIndex() );
}

//-----------------------------------------------------------------------------
// The add every other add funnels through.
//
// The rule -- one viewer, named by slot, suppressed on a re-prediction -- is in
// local_recipient.h so it can be tested off the client. What is left here is
// the duplicate check, which is about this filter's storage and nothing else.
//-----------------------------------------------------------------------------
void C_RecipientFilter::AddRecipientByPlayerIndex( int playerindex )
{
	if ( !LocalRecipient_ShouldHold( playerindex, LocalPlayerSlot(),
	                                 m_bUsingPredictionRules,
	                                 g_RecipientFilterPredictionSystem.CanPredict() ) )
	{
		return;
	}

	// Already in list
	if ( m_Recipients.Find( playerindex ) != m_Recipients.InvalidIndex() )
		return;

	m_Recipients.AddToTail( playerindex );
}

void C_RecipientFilter::RemoveRecipient( C_BasePlayer *player )
{
	if ( !player )
		return;

	int index = player->MvEntIndex();

	// Remove it if it's in the list
	m_Recipients.FindAndRemove( index );
}

void C_RecipientFilter::AddRecipientsByTeam( C_Team *team )
{
	AddAllPlayers();
}

void C_RecipientFilter::RemoveRecipientsByTeam( C_Team *team )
{
	Assert ( 0 );
}

void C_RecipientFilter::AddPlayersFromBitMask( CBitVec< ABSOLUTE_PLAYER_LIMIT >& playerbits )
{
	// The engine set these bits, and it set them for CLIENTS -- resolving the
	// viewer back to a C_BasePlayer only re-asked a question the engine had
	// already answered, and answered "nobody" for a standalone viewer.
	const int nLocal = LocalPlayerSlot();

	if ( nLocal < 1 || nLocal >= ABSOLUTE_PLAYER_LIMIT )
		return;

	// only add the local player on client side
	if ( !playerbits[ nLocal ] )
		return;

	AddRecipientByPlayerIndex( nLocal );
}

void C_RecipientFilter::AddRecipientsByPVS( const Vector& origin )
{
	AddAllPlayers();
}

void C_RecipientFilter::AddRecipientsByPAS( const Vector& origin )
{
	AddAllPlayers();
}

void C_RecipientFilter::UsePredictionRules( void )
{
	if ( m_bUsingPredictionRules )
		return;

	if ( !prediction->InPrediction() )
	{
		Assert( 0 );
		return;
	}

	const int nLocal = LocalPlayerSlot();
	if ( nLocal < 1 )
	{
		Assert( 0 );
		return;
	}

	m_bUsingPredictionRules = true;

	// Cull list now, if needed
	int c = GetRecipientCount();
	if ( c == 0 )
		return;

	if ( !g_RecipientFilterPredictionSystem.CanPredict() )
	{
		m_Recipients.FindAndRemove( nLocal );
	}
}

bool C_RecipientFilter::IsUsingPredictionRules( void ) const
{
	return m_bUsingPredictionRules;
}

bool C_RecipientFilter::IgnorePredictionCull( void ) const
{
	return m_bIgnorePredictionCull;
}

void C_RecipientFilter::SetIgnorePredictionCull( bool ignore )
{
	m_bIgnorePredictionCull = ignore;
}

CLocalPlayerFilter::CLocalPlayerFilter()
{
	AddRecipientByPlayerIndex( LocalPlayerSlot() );
}

//-----------------------------------------------------------------------------
// plan_baseentity.md Phase 13 precondition (e): moved here from
// C_BaseEntity::RemoveRecipientsIfNotCloseCaptioning. See the header.
//-----------------------------------------------------------------------------
void RecipientFilter_RemoveIfNotCloseCaptioning( C_RecipientFilter& filter )
{
	extern ConVar closecaption;
	if ( !closecaption.GetBool() )
	{
		filter.Reset();
	}
}
