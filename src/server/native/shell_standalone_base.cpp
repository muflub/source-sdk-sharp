//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the parallel entity hierarchy, implemented =========//
// SS_REASON a  a vtable the engine calls through
//
// The layers themselves. See shell_standalone.h for why this is a parallel
// hierarchy rather than a rewrite in place, and for how it maps onto the SDK's
// chain.
//
// Everything here is SHELL: what the engine demands and nothing that could be
// called a decision. The decisions belong to managed code, reached through the
// OnManaged* virtuals — which this file never implements, because which managed
// contract an entity speaks is a subclass's business and not the shell's.
//
//=============================================================================//

#include "ss_util.h"   // engine, and eiface.h / edict.h / gametrace.h
#include "ss_platform.h"   // SS_RETURN_ADDRESS
#include "shell_standalone.h"
#include "standalone_collision_rules.h"
#include "standalone_netserial.h"
#include "slot_table.h"
#include "standalone_model_bounds.h"
#include "shell_standalone_class.h"
#include "standalone_names.h"
#include "shell_model_name.h"
#include "generic_nettable.h"
#include "vphysics_interface.h"
#include "ss_physics_shared.h"   // physenv, physprops, ss::kPhysFlag* (lane SP)
#include "physicsowner.h"
#include "engine/ivmodelinfo.h"
#include "slot_table.h"

// SourceSharpEntity_t's third arm -- worldspawn, which is on neither of the two
// hierarchies this file otherwise knows about (plan_cworld_trace.md T3).
#include "ss_world.h"
#include "world_subject.h"
#include "engine/IEngineSound.h"
#include "tier0/icommandline.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "sourcesharp.h"
#include "managed_convar_ref.h"
#include "precache_model.h"
#include "native_command_body.h"
#include "ss_string.h"
#include "ss_cpp20.h"

#include <algorithm>
#include <vector>



#include "imanaged_areas.h"   // plan_engineapi_vtables E9: IManagedEntities
#include "ss_takedamageinfo.h"	// server.so's damage info

// What cbase.h and physics_shared.h declared for this file before P5 (lane E).
extern ISpatialPartition *partition;   // shell_engineboundary.cpp
extern IVModelInfo       *modelinfo;   // shell_engineboundary.cpp
extern CGlobalVars       *gpGlobals;   // shell_engineboundary.cpp

// The control block, Part 9. A slot outliving its entity is a pointer C# will
// write through into freed memory with the serial still matching, so the shell
// that frees the memory is the one that has to say so.
void SourceSharp_EntitySlotClosed( uint32_t hEntity );

//-----------------------------------------------------------------------------
// Every live one, because there is no gEntList to ask — its traversals answer
// CBaseEntity and correctly skip these. Small and walked once a frame; a real
// implementation would keep them ordered by due time rather than scanning.
//-----------------------------------------------------------------------------
static std::vector< CStandaloneEntity * > g_Standalones;

// THE FALL-BACK DEFERRED LIST -- plan_entity.md step 2c took the deferred
// removal itself into EntityDirectory, and what is left here is the path for a
// removal managed code did not take.
//
// Remove() asks the directory to mark first, and on a running server the
// directory always takes it, so this vector stays EMPTY for a whole map. It is
// not dead: a shell that was never bound on the managed side has no directory
// entry to mark, and a removal is not a thing to lose because a route was not
// there. Emptied after the frame's thinks, which is where it always was.
static std::vector< CStandaloneEntity * > g_PendingDeletes;

std::vector< CStandaloneEntity * > &StandaloneEntities() { return g_Standalones; }

//-----------------------------------------------------------------------------
// Who caused the call managed code is currently inside.
//
// Source hands an activator to every input, and an output fired in response is
// meant to carry it onward — that is how a trigger tells a game_text WHICH
// player walked in. Managed code has no way to name it: an output's Fire()
// takes no activator, because from that side the answer is always "whoever
// caused the thing I am handling".
//
// So the shell remembers it around each dispatch. The CBaseEntity shells learnt
// this the hard way: without it every managed output fired with the entity
// itself as the activator, and everything that checks one silently did nothing.
//-----------------------------------------------------------------------------
// An Activator_t rather than a CBaseEntity, which is the change that made a
// managed entity able to cause something. See activator.h: the two halves are
// carried together, the SDK half is what every unconverted reader still gets,
// and the other half is the one this hierarchy could never name.
static Activator_t g_CurrentActivator;

CStandaloneActivatorScope::CStandaloneActivatorScope( const Activator_t &activator )
	: m_Previous( g_CurrentActivator )
{
	g_CurrentActivator = activator;
}

// Restored rather than cleared: an input handler that fires an output which
// lands on another entity nests these, and clearing would lose the outer
// activator halfway through.
CStandaloneActivatorScope::~CStandaloneActivatorScope()
{
	g_CurrentActivator = m_Previous;
}

//-----------------------------------------------------------------------------
// THE resolve. Everything below it is a projection of its result.
//
// ONE entity-list lookup — the same one the trace filter and the event queue
// use, serial revalidated by LookupEntity — and then two virtual asks that
// partition the world exactly: GetGameEntity() answers only for the parallel
// hierarchy and GetBaseEntity() answers only for the SDK's, so at most one arm
// is ever set. This used to be two functions doing two lookups with a
// fall-through between them, and the fall-through is what made NULL ambiguous:
// it meant "wrong hierarchy, keep asking" on the way through and "gone" at the
// end, spelled identically.
//-----------------------------------------------------------------------------
IGameEntity *SourceSharpEntity_t::GameEntity() const
{
	if ( pBase )
		return pBase;
	return pStandalone ? pStandalone->GetGameEntity() : NULL;
}

SourceSharpEntity_t SourceSharp_ResolveEntity( uint32_t hEntity )
{
	SourceSharpEntity_t ent;
	ent.pBase       = NULL;
	ent.pStandalone = NULL;
	ent.pWorld      = NULL;

	if ( hEntity == INVALID_EHANDLE_INDEX )
		return ent;

	// UnsafeFromIndex, not Init( entry, serial ). The mod's own resolver warns
	// about this and I did it anyway: decomposing by hand does not round-trip,
	// because ENT_ENTRY_MASK comes from the SERIAL width rather than the entry
	// width. Getting it wrong is silent — the handle simply never resolves, and
	// the symptom was a think that never ran.
	IHandleEntity *pHandleEntity =
		SourceSharp_EntityFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );
	if ( !pHandleEntity )
	{
		// plan_entity.md step 2e -- ONE LINE OUTSIDE 2e's OWN FILES, and it is
		// the step's third gate clause. A handle whose ENTRY is in the
		// engine-invisible half and which this list does not resolve is either a
		// managed shell-less entity's (never in this list, by construction) or a
		// stale one to a dead server-only CBaseEntity. Both are worth counting;
		// see SourceSharp_NoteInvisibleRangeReach in shell_scripted.cpp.
		const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

		if ( nEntry >= MAX_EDICTS && nEntry < NUM_ENT_ENTRIES )
			// LEVEL 0 ONLY. __builtin_return_address(1) is what gcc calls
			// unsafe (-Wframe-address) and it means it literally: a frame
			// without a standard prologue gives garbage or faults. This is a
			// DIAGNOSTIC whose counter reads zero, so carrying a crash risk to
			// sharpen it would be the wrong trade. The immediate return address
			// still names the route; two routes sharing one immediate caller
			// merge, which is stated in shell_scripted.cpp.
			SourceSharp_NoteInvisibleRangeReach( SS_RETURN_ADDRESS() );

		return ent;
	}

	// The asks, not casts: IGameEntity and CBaseEntity are different addresses
	// for the same object, and the virtual crossing is what adjusts the
	// pointer. A CBaseEntity answers NULL from GetGameEntity and a standalone
	// entity answers NULL from GetBaseEntity, which is what separates the two
	// without a dynamic_cast on a path the entity API takes for every call.
	// THE WORLD FIRST, because it answers NULL to both asks below
	// (plan_cworld_trace.md T3) and the two of them would classify it as "gone"
	// -- which is the same NULL this resolver's own header says it stopped
	// being ambiguous about. One pointer compare against a singleton, and the
	// same third arm world_subject.h added for traces.
	ent.pWorld = SourceSharp_WorldFromEntityHandle( pHandleEntity );

	if ( ent.pWorld )
		return ent;

	IServerUnknown *pUnknown = static_cast< IServerUnknown * >( pHandleEntity );

	if ( IGameEntity *pGame = static_cast< IServerEntity * >( pUnknown )->GetGameEntity() )
		ent.pStandalone = SourceSharp_ShellOf( pGame );
	//=========================================================================
	// SOURCESHARP, Phase 13 step 5 — WHY THE pBase ARM AND EVERY ARM BELOW IT
	// IS GUARDED, ONCE, HERE.
	//
	// THE SDK ARM CANNOT BE TAKEN ARMED. baseentity.h's own note on the
	// game-entity handle crossing states the mechanism: GetGameEntity() "answers
	// NULL today and WILL ANSWER `this` AT STEP 5" for the SDK's tree, because
	// `using CBaseEntity = IGameEntity` makes every entity an IGameEntity. The
	// `if` above therefore succeeds for EVERY entity once the alias is armed,
	// pStandalone is always the arm taken, and pBase is NULL for the whole life
	// of the process. Route step 6 says the same thing from the other end: "the
	// GetBaseEntity() / EntityFromEntityHandle pair collapse".
	//
	// So each `if ( pBase )` below is not a branch with a missing answer — it is
	// a branch with no subject. Its parallel arm, already sitting underneath it,
	// is the whole function. That is why the guards are one line each with the
	// reason here rather than thirteen restatements: there is exactly one fact.
	//
	// The FIELD stays declared (shell_resolve.h:67). Armed it is an IGameEntity *
	// that is always NULL, which is what IsValid() and the diagnostic at the
	// bottom of this file already read it as.
	//=========================================================================

	return ent;
}

//-----------------------------------------------------------------------------
// The projections. Each is a field read on the one resolve, so none of them
// can disagree with it — the property the four resolve spellings never had.
//-----------------------------------------------------------------------------
CStandaloneEntity *SourceSharp_ResolveStandalone( uint32_t hEntity )
{
	return SourceSharp_ResolveEntity( hEntity ).pStandalone;
}

const Vector &SourceSharpEntity_t::Origin() const
{
	// THE WORLD IS AT THE ORIGIN, and it is worth an arm rather than letting
	// the fall-through below answer vec3_origin by accident: the two are the
	// same number for opposite reasons -- one is "where the world is", the
	// other is "nothing resolved" -- and a reader cannot tell which a bare
	// vec3_origin means.
	if ( pWorld )
		return pWorld->GetCollideable()->GetCollisionOrigin();


	return pStandalone ? pStandalone->GetAbsOrigin() : vec3_origin;
}

void SourceSharpEntity_t::Bounds( Vector *pOutMins, Vector *pOutMaxs ) const
{
	if ( pStandalone )
	{
		// Already origin-relative on this side: SetModelName takes them from the
		// submodel the map compiled, and WorldSpaceSurroundingBounds is these
		// plus the origin. So there is nothing to subtract.
		if ( pOutMins ) *pOutMins = pStandalone->OBBMins();
		if ( pOutMaxs ) *pOutMaxs = pStandalone->OBBMaxs();
		return;
	}

	if ( !pBase )
	{
		if ( pOutMins ) *pOutMins = vec3_origin;
		if ( pOutMaxs ) *pOutMaxs = vec3_origin;
		return;
	}

}

Vector SourceSharpEntity_t::Center() const
{

	Vector vecMins, vecMaxs;
	Bounds( &vecMins, &vecMaxs );

	return Origin() + ( vecMins + vecMaxs ) * 0.5f;
}

int SourceSharpEntity_t::SoundSourceIndex() const
{
	// entindex() rather than anything cleverer, because that is exactly what
	// CBaseEntity::EmitSound passes for itself.

	if ( !pStandalone )
		return SOUND_FROM_WORLD;

	// -1 is what EntIndex answers for an entity with no edict, and it is not a
	// sound source: SOUND_FROM_LOCAL_PLAYER has that value on the client.
	const int nIndex = pStandalone->EntIndex();
	return nIndex > 0 ? nIndex : SOUND_FROM_WORLD;
}

//-----------------------------------------------------------------------------
// The three things the ENGINE needs to store something ON an entity rather than
// loose in the world: which entity, which model, and the frame the model's own
// coordinates are in.
//
// Together rather than three unrelated accessors, because they have to agree.
// A decal stored against index N, model M, at a position expressed in some
// OTHER entity's frame is not a slightly wrong decal — it is a mark somewhere
// else entirely, and nothing downstream can notice.
//-----------------------------------------------------------------------------
int SourceSharpEntity_t::EntityIndex() const
{
	// Answered by definition rather than looked up: worldspawn IS entity 0,
	// which is the whole of what CWorld::RequiredEdictIndex says.
	if ( pWorld )
		return 0;


	if ( !pStandalone )
		return 0;

	// -1 is EntIndex's answer for a server-only entity, and the engine has no
	// slot by that number. 0 is the WORLD's index, which is what "not attached
	// to anything the client has heard of" has always meant engine-side.
	const int nIndex = pStandalone->EntIndex();
	return nIndex > 0 ? nIndex : 0;
}

int SourceSharpEntity_t::ModelIndex() const
{
	// Brush model 1, through the interface every hierarchy answers.
	if ( pWorld )
		return pWorld->GetModelIndex();


	return pStandalone ? pStandalone->GetModelIndex() : 0;
}

const matrix3x4_t &SourceSharpEntity_t::EntityToWorld() const
{

	// CollisionToWorldTransform is the entity frame ON THIS ARM and only on
	// this arm. SetAbsTransform above builds the block's WorldTransform with AngleMatrix from
	// the origin and the angles together, unconditionally, and this returns
	// exactly that member — there is no IsBoundsDefinedInEntitySpace branch to
	// drop the rotation the way CCollisionProperty's has. See
	// standalone_entity_frame_test.cpp, which pins both halves of that.
	if ( pStandalone )
		return pStandalone->CollisionToWorldTransform();

	// Nothing resolved. Identity, so a caller that transforms a point through
	// this without checking gets the point back unchanged — world space, which
	// is what "against no entity" means to every engine call that takes one of
	// these — rather than a garbage frame.
	static const matrix3x4_t s_matIdentity( 1.0f, 0.0f, 0.0f, 0.0f,
	                                        0.0f, 1.0f, 0.0f, 0.0f,
	                                        0.0f, 0.0f, 1.0f, 0.0f );
	return s_matIdentity;
}

const char *SourceSharpEntity_t::Classname() const
{
	// The world's name comes off its IServerNetworkable, which is the one face
	// all three hierarchies have and the same route AnyEntity_t takes for the
	// parallel one. Without it every enumeration in the game reported the world
	// with an EMPTY classname -- `./debug-api ents "*"` counted it as a
	// sixty-first "class" whose name was "", which reads as the gate passing.
	if ( pWorld )
		return pWorld->GetNetworkable()->GetClassName();


	return pStandalone ? pStandalone->GetClassname() : "";
}

const char *SourceSharpEntity_t::Name() const
{

	return pStandalone ? pStandalone->GetEntityName() : "";
}

IPhysicsObject *SourceSharpEntity_t::Physics() const
{

	return pStandalone ? pStandalone->GetPhysics() : NULL;
}

//-----------------------------------------------------------------------------
// The SDK arm's skeleton.
//
// GetBaseAnimating() and not dynamic_cast: the SDK declares that virtual on
// CBaseEntity returning 0 and overrides it on CBaseAnimating returning this,
// so it answers the identical question with no RTTI — and baseanimating.h uses
// it that way itself.
//
// The NULL guard is the whole reason this is one function. pBase is NULL for
// every entity on the parallel hierarchy, dynamic_cast< T * >( NULL ) is NULL,
// and a virtual call on NULL is not; a mechanical substitution at five call
// sites would have been five chances to leave it out.
//-----------------------------------------------------------------------------
CBaseAnimating *SourceSharpEntity_t::Animating() const
{
	// No parallel arm to fall through to, and none is owed: this asks for the
	// SDK's skeleton and armed there is no SDK entity to have one. NULL is
	// already this function's answer for every entity on the other hierarchy,
	// and every caller tests it -- the header says so in as many words.
	return NULL;
}

IHandleEntity *SourceSharpEntity_t::ToHandleEntity() const
{

	return pStandalone;
}

//-----------------------------------------------------------------------------
// The handle for an entity of either hierarchy.
//
// Written in terms of GetRefEHandle for both, because that is the one numbering
// the managed side uses — a standalone entity is not a second kind of thing with
// a second numbering, it is an entity C# can already name.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_HandleFromAnyEntity( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return INVALID_EHANDLE_INDEX;

	return (uint32_t)pHandleEntity->GetRefEHandle().ToInt();
}

//-----------------------------------------------------------------------------
// The next entity of a classname, continuing from one.
//
// gEntList's own FindEntityByClassname walks CBaseEntity pointers and correctly
// skips these, so iteration over "every trigger_multiple" would stop before
// reaching any of them. This is the second half of that walk.
//
// Linear, like the SDK's. The list is short and this is not on a hot path; what
// matters is that an entity outside the SDK can be found by the same question
// every other one answers.
//-----------------------------------------------------------------------------
CStandaloneEntity *StandaloneEntities_FindByClassname( CStandaloneEntity *pStartAfter,
                                                       const char *pClassName )
{
	if ( !pClassName || !pClassName[0] )
		return NULL;

	size_t nStart = 0;

	if ( pStartAfter )
	{
		std::vector< CStandaloneEntity * >::const_iterator it =
			std::find( g_Standalones.begin(), g_Standalones.end(), pStartAfter );

		// An entity that has since been removed ends the iteration rather than
		// restarting it. Restarting would hand the caller the same entities
		// again and never terminate.
		if ( it == g_Standalones.end() )
			return NULL;

		nStart = ( it - g_Standalones.begin() ) + 1;
	}

	// From part-way: after the entity the caller had.
	auto it = std::find_if( g_Standalones.begin() + nStart, g_Standalones.end(),
		[pClassName]( CStandaloneEntity *pEnt ) { return StandaloneNameMatches( pClassName, pEnt->GetClassname() ); } );

	return ( it != g_Standalones.end() ) ? *it : NULL;
}


//-----------------------------------------------------------------------------
// Allocated the way the engine allocates an entity.
//
// PvAllocEntPrivateData returns ZEROED memory and the SDK depends on that
// everywhere: CBaseEntityOutput has a destructor and no constructor, so its
// action list is NULL only because the allocation was. Plain new left it as
// whatever the heap had, and the destructor walked that as a list and freed it.
//-----------------------------------------------------------------------------
void *CStandaloneEntity::operator new( size_t nBytes, int nBlobBytes )
{
	// Longer than the object, by WHAT THIS CLASS DECLARED rather than by what
	// the greediest class on the hierarchy declared (Part 18).
	//
	// The props are still resolved against SourceSharp_StandaloneBlobOffset()
	// once at DLL init, and that is still a constant — the blob BASE has not
	// moved and cannot, because it is past a fixed-size object. What is per
	// class is how far the blob EXTENDS, and the entity is only ever encoded
	// through its own tier's table, whose last payload prop is the last word of
	// this length. So no prop of the table this entity reports ever addresses
	// past the end of this allocation, which is the whole invariant.
	//
	// nBytes is ignored deliberately: it is the size of whichever subclass is
	// being made, and the offset the props were built against is a constant.
	// Asserting instead of trusting, because the failure would be silent.
	Assert( (int)nBytes <= SourceSharp_StandaloneBlobOffset() );

	// A negative or absurd length is refused up to the smallest tier THAT HOLDS
	// THE ENGINE BLOCK rather than trusted. A caller that got this wrong would
	// otherwise hand PvAllocEntPrivateData a length shorter than the table it
	// is about to be encoded through, and nothing between here and SV_PackEntity
	// looks.
	//
	// The floor moved in step 1b and it had to: it used to be tier 0's
	// thirty-two bytes, and the block the constructor writes is three hundred
	// and fifty-two. See SourceSharp_StandaloneMinTier.
	if ( nBlobBytes < SourceSharp_StandaloneMinBlobBytes() )
		nBlobBytes = SourceSharp_StandaloneMinBlobBytes();

	return engine->PvAllocEntPrivateData(
		SourceSharp_StandaloneBlobOffset() + nBlobBytes );
}

//-----------------------------------------------------------------------------
// The class's own networked state, past the declared members.
//-----------------------------------------------------------------------------
unsigned char *CStandaloneEntity::DataBlob()
{
	return (unsigned char *)this + SourceSharp_StandaloneBlobOffset();
}

void CStandaloneEntity::NetworkStateChanged()
{
	// PART 9's ORACLE WAS A COUNTER HERE AND IT IS GONE (plan_shim.md §4a item
	// 10). `g_nSourceSharpStateChangedCalls` counted every call so that
	// `cs_sv_dirty_stats` could sample it either side of a hundred
	// /entity/<index>/payload polls and prove a blob read had stopped marking
	// the edict. That reader left in Phase 1, and a counter nothing samples is
	// not an oracle -- it is a write no one can read. The proof it took stands
	// in the commit that took it; re-taking it needs the reader back, not the
	// write kept alive on the chance.
	if ( m_pEdict )
		m_pEdict->StateChanged();
}

//-----------------------------------------------------------------------------
// A BLOCK WRITE, ANNOUNCED AS THE ONE OFFSET IT ACTUALLY MOVED (step 1b).
//
// plan.md §10 and plan_entity.md's gate for this step. The engine has two ways
// of being told an entity changed and they are not two spellings of one thing:
//
//   StateChanged()          sets FL_FULL_EDICT_CHANGED. The local network
//                           backdoor then copies EVERY prop in the table to the
//                           listen server's own client rather than the handful
//                           that moved -- and this hierarchy's tables are the
//                           six shared props plus one int32 prop per four bytes
//                           of blob, which is 128 props at the smallest tier.
//   StateChanged( offset )  records the offset. LocalTransfer_TransferEntity
//                           maps it back to a prop index through
//                           CSendTablePrecalc::m_PropOffsetToIndexMap and
//                           copies that prop alone.
//
// The difference is invisible to every functional test ever written, because
// both deliver the right VALUE. What the full form costs is bandwidth and delta
// work, every frame, for the life of the build -- which is why the gate for
// this step is a check that the mark did NOT widen, rather than a check that
// the value arrived.
//
// ONE OFFSET, NOT A RANGE, and the reason is the engine's arithmetic rather
// than ours. MapPropOffsetsToIndices matches a recorded offset against a
// SendProp's offset EXACTLY. A Vector is ONE SendProp of type DPT_Vector at the
// vector's own offset -- not three -- so marking origin+4 and origin+8 as well
// would not name any typed prop. It would name the generic payload words that
// happen to overlie those bytes, and MAX_CHANGE_OFFSETS is NINETEEN: a move
// that marked origin, angles and the twelve words of the world transform would
// spend eighteen of the nineteen on one call and the next thing to change
// anything would tip the edict into FL_FULL_EDICT_CHANGED anyway. Marking
// exactly the field's own offset is both the cheapest and the only rule that
// stays under that ceiling.
//
// A null edict is a server-only entity, which has no client to tell.
//-----------------------------------------------------------------------------
void CStandaloneEntity::MarkBlockChanged( int nBlockOffset )
{
	Assert( nBlockOffset >= 0 && nBlockOffset < (int)sizeof( EngineBlock_t ) );

	if ( !m_pEdict )
		return;

	m_pEdict->StateChanged( (unsigned short)( BlockBase() + nBlockOffset ) );
}

void CStandaloneEntity::operator delete( void *pMem )
{
	engine->FreeEntPrivateData( pMem );
}

//-----------------------------------------------------------------------------
// The placement delete that pairs with the sized new. Only ever reached if a
// constructor threw, which nothing on this hierarchy does — but a compiler that
// cannot find it is right to warn, because without it that path would leak.
//-----------------------------------------------------------------------------
void CStandaloneEntity::operator delete( void *pMem, int )
{
	engine->FreeEntPrivateData( pMem );
}

//-----------------------------------------------------------------------------
// How long THIS entity's blob is (Part 18).
//
// From the tier the factory allocated it at, not from a global. Straight off
// the tier table rather than stored as a second member, so there is one number
// and not two that could disagree.
//-----------------------------------------------------------------------------
int CStandaloneEntity::BlobBytes() const
{
	return SourceSharp_TierBytes( m_iTier );
}

//-----------------------------------------------------------------------------
// CBaseHandle IS FOUR BYTES AND NOTHING ELSE, which is what makes BlockHandle
// and BlockOwner overlays rather than copies.
//
// Asserted rather than assumed because the failure would be silent in the worst
// direction: a CBaseHandle with a second member would read four bytes of
// whatever follows Handle in the block -- ClassnameIndex -- as its serial, and
// every handle on the hierarchy would resolve to nothing with nothing to say
// why.
//-----------------------------------------------------------------------------
static_assert( sizeof( CBaseHandle ) == sizeof( uint32_t ),
	"CBaseHandle is no longer one uint32; EngineBlockHead_t::Handle cannot overlay it" );

//-----------------------------------------------------------------------------
// Where the block starts, measured from the object base (step 1b).
//
// The blob's base IS the block's base -- §3.2 puts the block at offset zero of
// the blob -- so there is one number and not two that could drift.
//-----------------------------------------------------------------------------
int CStandaloneEntity::BlockBase()
{
	return SourceSharp_StandaloneBlobOffset();
}

//-----------------------------------------------------------------------------
// THE SMALLEST TIER A SHELL ON THIS HIERARCHY CAN BE ALLOCATED AT (step 1b).
//
// This exists because step 1a moved the engine block into the blob, and the
// blob's floor was a number chosen when the blob held nothing but a class's
// own declaration.
//
// The tier table starts at THIRTY-TWO BYTES. The block is three hundred and
// fifty-two. Every path that reached the floor -- and there is one that is
// reached in a normal run, ResolveTier answering 0 for a classname the type
// table has not published yet -- would have allocated thirty-two bytes and then
// had this class's constructor write three hundred and fifty-two into it. Not a
// wrong value: three hundred and twenty bytes past the end of an engine
// allocation, in the constructor, before the entity is bound to anything that
// could report it.
//
// A class that DECLARES state was never at risk: NetStateGenerator lays every
// declared field after the block, so its layout is at least 352 and the
// smallest tier that fits it is the same one this function answers. What was at
// risk is precisely the class that declares nothing, which is most map
// entities.
//
// Searched rather than written down as `4`. The tier table is in a shared
// header and rows have been added to it twice; a constant here would be right
// until the day somebody inserts a row, and then it would be an index into a
// different table.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneMinTier()
{
	const int nCount = SourceSharp_TierCount();

	for ( int i = 0; i < nCount; i++ )
	{
		if ( SourceSharp_TierBytes( i ) >= (int)sizeof( EngineBlock_t ) )
			return i;
	}

	// No tier holds the block at all, which is a build that cannot run. Said
	// loudly and answered with the largest, because a silent 0 here is the
	// overrun this whole function exists to stop.
	Warning( "[SourceSharp] standalone: NO TIER holds the %d-byte engine block; "
	         "the largest is %d bytes\n",
		(int)sizeof( EngineBlock_t ), SourceSharp_TierBytes( nCount - 1 ) );

	return nCount - 1;
}

int SourceSharp_StandaloneMinBlobBytes()
{
	return SourceSharp_TierBytes( SourceSharp_StandaloneMinTier() );
}

#if defined( _MSC_VER )
// 'this' in the initialiser list below is deliberate. The build disables C4355
// (/wd4355, as Valve's VPC does), but tier1/convar.h ends with
// `#pragma warning( default : 4355 )`, which turns it back on for the rest of
// any file that includes it.
#pragma warning( disable : 4355 )
#endif
CStandaloneEntity::CStandaloneEntity()
	  // FIRST, and the header declares it first for the same reason: a member
	  // initialiser runs in DECLARATION order rather than in the order it is
	  // written here, so a pointer written late here and declared late there
	  // would be set after anything that reached through it.
	: m_pEngineBlock( reinterpret_cast< EngineBlock_t * >(
	      reinterpret_cast< unsigned char * >( this ) + BlockBase() ) ),
	  m_nSpawnFlags( 0 ),
	  m_iHealth( 0 ),
	  m_vecSpawnOrigin( vec3_origin ),
	  m_angSpawnRotation( 0, 0, 0 ),
	  m_pPhysics( NULL ),
	  m_vecVelocity( vec3_origin ),
	  m_vecMoveVelocity( vec3_origin ),
	  m_flGravityScale( 0.0f ),
	  m_nBouncesLeft( 0 ),
	  m_angVelocity( 0, 0, 0 ),
	  m_pClass( NULL ),
	  m_pServerClass( NULL ),

	  // The smallest tier THAT HOLDS THE ENGINE BLOCK until the factory says
	  // otherwise, which is what an entity made outside PostConstruct would get.
	  // Never -1: every reader treats this as an index into the tier table.
	  //
	  // It was tier 0 until step 1b, and tier 0 is thirty-two bytes -- shorter
	  // than the block this constructor is about to write.
	  m_iTier( SourceSharp_StandaloneMinTier() ),
	  m_pEdict( NULL ),
	  m_bOwnsEdict( true ),
	  m_Partition( PARTITION_INVALID_HANDLE )
{
	// THE BLOCK'S DEFAULTS, WRITTEN OUT (step 1b).
	//
	// PvAllocEntPrivateData hands back ZEROED memory and thirteen values that
	// used to be member initialisers now live in it, so the ones whose default
	// is not a zero have to be said here or they are silently wrong.
	//
	// THE HANDLES ARE THE SHARP EDGE. CBaseHandle's default constructor sets
	// INVALID_EHANDLE_INDEX, which is 0xFFFFFFFF; the zero the allocator leaves
	// is index 0 with serial 0, and index 0 is THE WORLD. An entity between
	// allocation and SetRefEHandle would have claimed to be the world, and
	// every resolve of it would have answered a real entity rather than none.
	EngineBlockHead_t &head = m_pEngineBlock->Head;

	head.Handle              = INVALID_EHANDLE_INDEX;
	head.ParentHandle        = INVALID_EHANDLE_INDEX;
	head.OwnerHandle         = INVALID_EHANDLE_INDEX;
	head.ClassnameIndex      = 0;
	head.ModelIndex          = 0;
	head.CollisionModelIndex = 0;
	head.Origin              = vec3_origin;
	head.Angles              = QAngle( 0, 0, 0 );
	head.Mins.Init( -16, -16, -16 );
	head.Maxs.Init(  16,  16,  16 );

	// Nothing, until an entity says otherwise. A logic_timer that answered a
	// trace would be an invisible wall in the middle of the map, and most of
	// what moves here first is exactly that kind of entity.
	head.SolidType      = SOLID_NONE;
	head.SolidFlags     = FSOLID_NOT_SOLID;
	head.CollisionGroup = COLLISION_GROUP_NONE;

	head.MoveType   = 0;
	head.Flags      = 0;
	head.Effects    = 0;
	head.WaterLevel = 0;

	// Nothing hangs off it yet. Managed code owns this word from here on --
	// SourceSharp.Server's HierarchyDriver writes it on every link made and
	// dropped -- and the only thing native asks of it is whether it is zero, at
	// the tail of a position write. See EngineBlock.cs on ChildCount.
	head.ChildCount = 0;

	SetIdentityMatrix( head.WorldTransform );

	// Zeroed whole, not field by field. PVSInfo_t carries a POINTER —
	// m_pClusters — that BuildEntityClusterList writes through, and setting
	// only m_nAreaNum left it as whatever the allocator had. The block's own
	// storage is zeroed by PvAllocEntPrivateData, but a shell is also rebuilt
	// in place on a player reconnect, so this says it rather than inheriting it.
	memset( BlockPVSInfo(), 0, sizeof( PVSInfo_t ) );
	m_szName[0] = 0;
	m_szModel[0] = 0;
	m_szClassname[0] = 0;
	m_nTypeId        = 0;
	m_nSchemaHash    = 0;
	m_nPayloadWords  = 0;

	// The animation clock, in the block (step 6d). A rate of ONE and not zero,
	// which is the one of these three the zeroed blob does not already give:
	// a playback rate of zero freezes the cycle at the sequence's first frame
	// forever, and every entity would be a statue with no error anywhere.
	Block().Client.Sequence     = 0;
	Block().Client.PlaybackRate = 1.0f;
	Block().Client.AnimTime     = 0.0f;

	// ONE, which is "as the artist built it": the zero a memset would leave
	// reads to player movement as a model with no extent. The skeleton's own
	// copy is managed (ServerSkeletons), and starts at one too.
	m_flModelScale        = 1.0f;
}

CStandaloneEntity::~CStandaloneEntity()
{
	// FIRST, and before the handle is torn down below: the blob lives past this
	// object and goes with it, and a managed state holding a pointer into it
	// revalidates by comparing a serial in the control block. Closing the slot
	// here is what turns that state's next access into a named exception rather
	// than a write into memory the allocator has already handed to something
	// else. After BlockHandle().Term() the entry index is no longer available to
	// name, which is why this cannot be tidied further down.
	SourceSharp_EntitySlotClosed( (uint32_t)BlockHandle().ToInt() );

	// And the slot table, here for the same stated reason: after
	// BlockHandle().Term() below the entry index is no longer available to name.
	//
	// Belt and braces with SetRefEHandle's unbind, on purpose. RemoveEntity
	// further down calls SetRefEHandle( INVALID_EHANDLE ) and would unbind
	// through that path -- but only for an entity that is ON the list. A
	// destructor that ran without one would otherwise leave a dangling binding,
	// and the next entity to take that entry would be REFUSED by Bind, a
	// refusal cs_sv_slot_table counts and nothing else would explain.
	if ( BlockHandle().IsValid() )
		SourceSharp_SlotUnbind( BlockHandle().GetEntryIndex() );

	// Every match, where FindAndRemove took the first: an entity is on each list
	// at most once (added once, by its constructor; queued only when absent).
	ss::erase( g_Standalones, this );
	ss::erase( g_PendingDeletes, this );

	// THE LINKS AND THE LAST LOOK ARE NOT HERE ANY MORE -- plan_entity.md step
	// 2c. StandaloneHierarchy_EntityRemoved( this ) and OnManagedRemoved() both
	// stood here; both were POLICY, and both are managed calls now, made by
	// EntityDirectory's removal pass BEFORE it asks for the shell to be freed.
	// Nothing moved in the frame: that pass runs where StandaloneEntities_Finish
	// drained the native list, which is where this destructor was reached from.
	//
	// WHAT THIS DESTRUCTOR HOLDS IS WHAT §3.5 SAYS IT SHOULD: the unbinds above,
	// the entity list and the partition below, the edict last -- plus the solver
	// object, which is the same kind of thing as the partition handle. A native
	// object owns a native object; freeing one is not a decision anybody makes,
	// and taking it out would leak every physics object on a level change, where
	// the walk below deletes shells that were never marked.
	DestroyPhysics();

	if ( BlockHandle().IsValid() )
	{
		// THE ENTRY IS ALREADY UNBOUND by the block at the top of this
		// destructor (1h landed it, 1c wrote it), and there is no longer a
		// second registration to undo.
		//
		// STEP 1e LOOKED AT MOVING THE UNBIND DOWN HERE AND DID NOT. The
		// argument for moving it is a window: everything between the two
		// points -- DestroyPhysics, and StandaloneHierarchy_EntityRemoved and
		// OnManagedRemoved before step 2c moved them out -- runs with the
		// entity list still resolving this
		// entity and the slot table no longer resolving it, and the table's
		// whole job is to be the list's answer without a cast. The argument
		// against is that 1h's arrangement is the one with a live gate behind
		// it, and nothing observable turns on the difference today. Recorded
		// in .claude/entity-port/requests/1e.md rather than changed on an
		// argument, because a teardown-ordering change with no failing test
		// is exactly the kind that is discovered later.
		//
		// THE LIST REMOVAL IS GONE -- plan_entity.md §3.3, lane entlist-server.
		// This read the entity list's RemoveEntity( BlockHandle() ) and it did
		// three things: unbound the entry, bumped its serial so every handle to
		// this entity went stale, and called SetRefEHandle( INVALID_EHANDLE ).
		// The slot table's Unbind at the top of this destructor does the first
		// two -- it is the SAME rule, taken from the same line
		// (entitylist_base.cpp:251) -- and the third was only ever how the list
		// told the entity it had been removed, which an entity inside its own
		// destructor does not need to be told.
		BlockHandle().Term();
	}

	if ( m_Partition != PARTITION_INVALID_HANDLE )
	{
		::partition->DestroyHandle( m_Partition );
		m_Partition = PARTITION_INVALID_HANDLE;
	}

	// The clusters BuildEntityClusterList allocated. PVSInfo_t carries a pointer
	// and the engine owns what is behind it; CServerNetworkProperty's destructor
	// makes exactly this call, and without it every networked entity leaks its
	// cluster list for the life of the map.
	engine->CleanUpEntityClusterList( BlockPVSInfo() );

	if ( m_pEdict )
	{
		// SetEdict( NULL ) BEFORE RemoveEdict, and the order is the whole of it.
		//
		// RemoveEdict returns the edict to the engine's free list. The edict
		// still holds m_pUnk — a pointer to THIS, which is about to be freed —
		// and the next CreateEdict that recycles the slot hands it to
		// FreeContainingEntity, which dereferences it.
		//
		// The crash is inside GetContainingEntity, during entity creation, with
		// nothing in the stack naming the entity that actually caused it: the
		// one that died some time earlier. CServerNetworkProperty::DetachEdict
		// is these two lines in this order, and this is why.
		m_pEdict->SetEdict( NULL, false );
		m_pEdict->m_pNetworkable = NULL;

		// Only an edict this entity created. A player's edict is the ENGINE's —
		// allocated for the connecting client, freed with the client — and
		// stock never removes it either: ClientDisconnect unhooks the pointers
		// exactly as above and leaves the slot to the engine (Phase 9a).
		if ( m_bOwnsEdict )
			engine->RemoveEdict( m_pEdict );

		m_pEdict = NULL;
	}
}

//-----------------------------------------------------------------------------
// The player's edict, in transit from ClientPutInServer to PostConstruct
// (Phase 9a). See shell_standalone.h for the contract; the CBasePlayer
// precedent is s_PlayerEdict, player.cpp:474.
//-----------------------------------------------------------------------------
static edict_t *s_pPendingPlayerEdict = NULL;

void SourceSharp_SetPendingPlayerEdict( edict_t *pEdict )
{
	if ( s_pPendingPlayerEdict && pEdict )
	{
		// Two players cannot be mid-construction at once — the engine calls
		// ClientPutInServer one client at a time — so a pending edict already
		// standing means the previous claim never happened.
		Warning( "[SourceSharp] standalone player: pending edict overwritten before it was claimed\n" );
	}

	s_pPendingPlayerEdict = pEdict;
}

edict_t *SourceSharp_ClaimPendingPlayerEdict()
{
	edict_t *pEdict = s_pPendingPlayerEdict;
	s_pPendingPlayerEdict = NULL;
	return pEdict;
}

void CStandaloneEntity::SetClassname( const char *pName )
{
	ss::StrCopy( m_szClassname, pName, sizeof( m_szClassname ) );

	// HERE and nowhere else. The id is a pure function of the name, so the one
	// place the name is set is the one place the id can be set without the two
	// being able to disagree — and an entity whose id names another class does
	// not fail, it pairs with that class's managed half and decodes its bytes
	// through that class's layout.
	m_nTypeId = SourceSharp_HashClassName( m_szClassname );
}

uint32_t CStandaloneEntity::ManagedHandle() const
{
	const CBaseHandle &h = GetRefEHandle();
	return h.IsValid() ? h.ToInt() : 0;
}

const model_t *CStandaloneEntity::GetCollisionModel()
{
	return Block().Head.CollisionModelIndex > 0 ? modelinfo->GetModel( Block().Head.CollisionModelIndex ) : NULL;
}

int CStandaloneEntity::EntIndex() const
{
	return m_pEdict ? engine->IndexOfEdict( m_pEdict ) : -1;
}

//-----------------------------------------------------------------------------
// What it looks like, applied now rather than recorded for Spawn.
//
// The CBaseEntity shells record and apply during their own Spawn, because that
// is where they precache. This one cannot: managed code sets a model from its
// OnSpawn, which the shell calls at the END of its Spawn, so recording it would
// mean waiting for a moment that has already passed.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetModelName( const char *pModel )
{
	if ( !pModel || !pModel[0] )
		return;

	ss::StrCopy( m_szModel, pModel, sizeof( m_szModel ) );

	// A leading '*' is a BRUSH model — a submodel of the map, "*23" being the
	// twenty-third one the compiler carved out. It is already in the BSP the
	// engine loaded, so it is looked up rather than precached; asking to
	// precache it is asking to load a file that does not exist.
	Block().Head.ModelIndex = SourceSharp_IsBrushModelName( m_szModel )
		? modelinfo->GetModelIndex( m_szModel )
		: Precache_Model( m_szModel );

	if ( Block().Head.ModelIndex <= 0 )
	{
		Warning( "[SourceSharp] %s: no model '%s'\n", GetClassname(), m_szModel );
		return;
	}

	// What it collides as, kept separately because hiding it must not stop it
	// being a shape.
	Block().Head.CollisionModelIndex = Block().Head.ModelIndex;

	// A brush carries its own size, and it is not centred on its origin: a door
	// is a slab somewhere in the map and its origin is usually at the world's.
	// Taking the bounds from the model is what makes it collide and be drawn
	// where the mapper actually drew it.
	//
	// A PLAYER is the exception, and was silently not one. Its movement hull is
	// what every question about standing is answered against, and Spawn installs
	// it one line before handing over to managed code whose first act is
	// SetModel -- so the hull was replaced by the citizen mesh's box on every
	// spawn, putting the reported feet six units below the real ones. See
	// standalone_model_bounds.h; measured on ss_sandbox's belts_lift, which
	// rose through its own passenger because of it.
	if ( const model_t *pModelPtr = modelinfo->GetModel( Block().Head.ModelIndex ) )
	{
		Vector vecMins, vecMaxs;
		modelinfo->GetModelBounds( pModelPtr, vecMins, vecMaxs );

		if ( StandaloneModel_AdoptsBounds( IsPlayer(), vecMins != vecMaxs ) )
			SetCollisionBounds( vecMins, vecMaxs );
	}

	// The client is told, because the model index is networked and nothing else
	// on this path marks the edict dirty. TWO offsets: the drawn index the
	// client decodes and the collision index beside it, which is a block field
	// like any other and is carried by the generic payload word that overlies
	// it.
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( ModelIndex ) );
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( CollisionModelIndex ) );
}

//-----------------------------------------------------------------------------
// Whether it is drawn — EF_NODRAW, and NOT the model index (step 8e-render).
//
// THIS USED TO WRITE `Block().Head.ModelIndex = 0`, and that is the defect step
// 7a measured and could not fix from an effects step. "Invisible" and
// "shapeless" were the same state on this hierarchy, so an invisible managed
// brush reached the client with no model at all: C_StandaloneEntity::GetModel
// answers NULL for index 0, GetCollisionModelIndex answers the same zero, and
// the three shims that draw INSIDE such a brush — dust, smoke, precipitation —
// all open with `pOwner->GetModel(); if ( !pModel ) return;`. 7a's
// cs_cl_dust_report read two func_dust volumes on ss_sandbox with
// `block ModelIndex 0  modelinfo->GetModel no`, permanently, and FuncDust.cs's
// own comment says why that is fatal: the client asks the brush "is this point
// inside you" for every mote it considers, and a collideable with no shape
// answers no every time.
//
// So this does what stock does, in the storage that already existed for it.
// m_fEffects is a CBaseEntity SendProp, EF_NODRAW is the bit in it that means
// "don't draw entity" (const.h:289), and EngineBlockHead.Effects has carried the
// comment "the EF_* effects bits" since step 1b — written once to 0 at spawn and
// read nowhere until now (step 8b named that dead word, and its
// TheEngineBlocksEffectsWordIsWrittenOnlyToZero fact is replaced by
// TheEngineBlocksEffectsWordIsTheVisibilitySwitch).
//
// THE MODEL INDEX IS STILL RESOLVED, and on both arms. It is what the entity
// looks like, not whether it is looked at, and a hidden brush that is later
// shown must not need a second lookup to get its shape back. Recomputed from
// the remembered name rather than stashed, because the index is a level-load
// fact and re-resolving it is a string table lookup, not a precache.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetVisible( bool bVisible )
{
	if ( !m_szModel[0] )
		return;

	// A LOOKUP, not a precache. The model was precached when the entity spawned
	// and showing it again is only asking the string table what index it got —
	// calling PrecacheModel here would be a precache after level load, which the
	// engine warns about and is right to.
	const int nIndex = modelinfo->GetModelIndex( m_szModel );

	if ( nIndex != Block().Head.ModelIndex )
	{
		Block().Head.ModelIndex = nIndex;
		MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( ModelIndex ) );
	}

	const int nEffects = bVisible ? ( Block().Head.Effects & ~EF_NODRAW )
	                              : ( Block().Head.Effects |  EF_NODRAW );

	if ( nEffects != Block().Head.Effects )
	{
		Block().Head.Effects = nEffects;
		MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Effects ) );
	}
}

//-----------------------------------------------------------------------------
// Where it is and which way it faces, written together.
//
// One function rather than two because the TRANSFORM is neither of them on its
// own: it is built from both, so a setter touching only its own half has to
// rebuild from the other half anyway. Splitting them is precisely what left
// the transform without a rotation — SetAbsAngles assigned the angles and
// stopped, and the only thing that ever rebuilt the matrix was this function's
// PositionMatrix, which writes column 3 and leaves the other three as whatever
// SetIdentityMatrix put there at construction.
//
// So CollisionToWorldTransform answered a rotation-free frame for every rotated
// entity on the hierarchy, and everything reading it — a trace against a
// rotated brush, a query asking what space one occupies — was working from a
// frame that did not describe the entity. AngleMatrix is what the CLIENT has
// always done in C_StandaloneEntity::OnPositionChanged; the two sides answering
// differently about the same entity is the whole of the bug.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetAbsTransform( const Vector &vecOrigin, const QAngle &angles )
{
	const Vector vecPrevOrigin = Block().Head.Origin;

	Block().Head.Origin = vecOrigin;
	Block().Head.Angles = angles;

	OnPositionChanged( vecPrevOrigin );
}

//-----------------------------------------------------------------------------
// EVERYTHING THAT FOLLOWS FROM A MOVE, once the block has been written
// (plan_entity.md step 1b; .claude/entity-port/abi-requests/1b.md §2).
//
// This was the tail of SetAbsTransform and is now its own function for one
// reason: managed code is going to write the block itself, and when it does it
// needs to be able to say "I moved it" without going through a native setter
// that would write the position again. ENTITY_OP_POSITION_CHANGED is that
// statement, and this is its handler -- SourceSharp_EntityDispatch calls
// exactly this.
//
// NOT A COPY OF THE TAIL. SetAbsTransform above calls it, so the op path and
// the native path are the same code and cannot drift; every move on this
// hierarchy already runs through here, which is what makes the op's handler
// something the live gate exercises rather than something only a test does.
//
// vecPrevOrigin is what the trigger half needs and is the one thing that cannot
// be recovered once the block has been written, which is why the op carries it.
//-----------------------------------------------------------------------------
void CStandaloneEntity::OnPositionChanged( const Vector &vecPrevOrigin )
{
	AngleMatrix( Block().Head.Angles, Block().Head.Origin, Block().Head.WorldTransform );

	if ( m_Partition != PARTITION_INVALID_HANDLE )
		::partition->ElementMoved( m_Partition, Block().Head.Origin + Block().Head.Mins, Block().Head.Origin + Block().Head.Maxs );

	// The client is told only if there is a client to tell -- MarkBlockChanged
	// answers that -- and it is told about EXACTLY the two fields that moved.
	//
	// THE WORLD TRANSFORM IS NOT ONE OF THEM, deliberately. It is a value
	// DERIVED from the two above and nothing on the client decodes it: the
	// client shell rebuilds its own from the origin and angles it did decode,
	// in C_StandaloneEntity::OnPositionChanged, with the same AngleMatrix call
	// three lines up. Marking it would put twelve words of matrix on the local
	// backdoor for a number the receiver is about to recompute, and would spend
	// twelve of the edict's nineteen change offsets doing it.
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Origin ) );
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Angles ) );

	// Anything carried by this entity, moved now rather than when it is next
	// read. That is what makes parenting here ordering-free: by the moment
	// anybody can observe this new position, the children already have theirs.
	StandaloneHierarchy_ParentMoved( this );

	// And then ask what it has moved into. LAST, because the answer is about
	// where the entity now is: asking before the partition and the children
	// have been updated would sweep the volume it was in a moment ago.
	//
	// Here rather than once a frame, because this is the one funnel every move
	// on this hierarchy goes through — the solver's, a scripted move, a flight,
	// a parent dragging a child — and CBaseEntity's equivalent is likewise
	// immediate rather than deferred. A trigger fired a frame late is a trigger
	// a fast prop has already left.
	TouchTriggers( vecPrevOrigin );
}

//-----------------------------------------------------------------------------
// The public setters, which differ from the funnel above by one thing: they mean
// "the world has been told where this is", so a parented entity's offset from
// its parent has to be measured again.
//
// CBaseEntity::SetAbsOrigin draws exactly this line, and the composition
// deliberately does not cross it — it writes through SetAbsTransform, because the
// offset is what it computed the new absolute FROM.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetAbsOrigin( const Vector &vecOrigin )
{
	SetAbsTransform( vecOrigin, Block().Head.Angles );
	StandaloneHierarchy_ChildMoved( this );
}

void CStandaloneEntity::SetAbsAngles( const QAngle &angles )
{
	SetAbsTransform( Block().Head.Origin, angles );
	StandaloneHierarchy_ChildMoved( this );
}

void CStandaloneEntity::SetCollisionBounds( const Vector &vecMins, const Vector &vecMaxs )
{
	Block().Head.Mins = vecMins;
	Block().Head.Maxs = vecMaxs;

	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Mins ) );
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Maxs ) );

	if ( m_Partition != PARTITION_INVALID_HANDLE )
		::partition->ElementMoved( m_Partition, Block().Head.Origin + Block().Head.Mins, Block().Head.Origin + Block().Head.Maxs );
}

//-----------------------------------------------------------------------------
// THE OFF SWITCH, AND IT IS INSTRUMENTATION DEBT WITH A NAMED REMOVAL OWNER.
//
// `sourcesharp_collision_rules_recheck 0` is the build before this lane's fix:
// the setters below write the block and tell vphysics nothing, which is what
// they did. One process, two arms, same box, same binary -- and the failure
// this fix is aimed at is a coin flip, which is exactly the shape where two
// builds at two times measure the box as much as the change. Kept for the same
// reason step 3f kept sourcesharp_player_shadow.
//
// Removal owner: whoever measures the rate this closes, or the Phase 3 exit,
// whichever comes first. It goes, and the call sites go back to the
// one-argument SourceSharpCollisionRulesChanged.
//-----------------------------------------------------------------------------
// DECLARED IN C# (SourceSharp.Server.CollisionRulesRecheckConVar),
// plan_server_novalve.md P2.3b. Unregistered reads as 1, stock's behaviour:
// an unresolved ref's 0 would silently select the old build's arm.
static CManagedConVarRef sourcesharp_collision_rules_recheck( "sourcesharp_collision_rules_recheck" );

static bool CollisionRulesRecheck()
{
	return !sourcesharp_collision_rules_recheck.IsValid() || sourcesharp_collision_rules_recheck.GetBool();
}

//-----------------------------------------------------------------------------
// WHEN THE SOLIDITY IS SET RELATIVE TO THE OBJECT'S CREATION is managed's
// (EntityPhysicsCreation, sourcesharp_solidity_before_physics): stock's order
// sets it first, while the shell holds no object, so the recheck below is a
// no-op at spawn.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetSolidity( SolidType_t nSolidType, int nSolidFlags )
{
	Block().Head.SolidType  = nSolidType;
	Block().Head.SolidFlags = nSolidFlags;

	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( SolidType ) );
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( SolidFlags ) );

	UpdatePartition();

	// AND TELL VPHYSICS, which UpdatePartition does not: the partition is the
	// ENGINE's index of where things are, and the collision filter is IVP's
	// cached answer to "may these two touch". Stock's CBaseEntity::SetSolid
	// reaches CollisionRulesChanged for exactly this reason and its comment is
	// quoted in the header. Without it a prop whose solidity changed after its
	// physics object was already in the solver keeps whatever answer vphysics
	// happened to cache first, for the rest of its life.
	SourceSharpCollisionRulesChanged( m_pPhysics, CollisionRulesRecheck() );
}

//-----------------------------------------------------------------------------
// The two one-line setters that used to be inline in the header.
//
// Out of line now for one reason only: they WRITE the block, and a write to the
// block is announced. An inline body in the header could do that too, but the
// announcement is the thing this step is about and it belongs beside the other
// twelve rather than hidden in a declaration.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetCollisionGroup( int nGroup )
{
	Block().Head.CollisionGroup = nGroup;
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( CollisionGroup ) );

	// The collision group is read by the rule table's group matrix, so changing
	// it changes what the filter would answer -- SetSolidity's reason exactly,
	// and stock's SetCollisionGroup calls CollisionRulesChanged for it too.
	SourceSharpCollisionRulesChanged( m_pPhysics, CollisionRulesRecheck() );
}

void CStandaloneEntity::SetModelIndex( int index )
{
	Block().Head.ModelIndex = index;
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( ModelIndex ) );
}

//-----------------------------------------------------------------------------
// Which spatial partition lists it belongs to.
//
// CCollisionProperty::UpdatePartition decides this for every stock entity and
// the rule is followed here verbatim, because the ENGINE reads these lists and
// does not care who filled them. Getting the mask wrong is silent in the worst
// way: a trigger in the solid list is an invisible wall, and a trigger in
// neither is one the engine enumerates past every tick without ever reporting.
//
//   NON_STATIC  everything solid or trigger, which is what a general query
//               "what is near this point" walks.
//   SOLID       things a trace should hit.
//   TRIGGER     things SolidMoved reports overlaps with, which is where a
//               pickup's touch actually comes from.
//-----------------------------------------------------------------------------
void CStandaloneEntity::UpdatePartition()
{
	const bool bIsSolid = ( Block().Head.SolidType != SOLID_NONE ) || ( Block().Head.SolidFlags & FSOLID_TRIGGER ) != 0;

	if ( !bIsSolid )
	{
		// Out entirely. An entity that has stopped being solid and stayed in
		// the lists is found by every query that should now miss it.
		if ( m_Partition != PARTITION_INVALID_HANDLE )
		{
			::partition->DestroyHandle( m_Partition );
			m_Partition = PARTITION_INVALID_HANDLE;
		}

		return;
	}

	// CreateHandle takes an IHandleEntity — not a CBaseEntity — so this needs
	// nothing from the SDK. CCollisionProperty does exactly this for every
	// stock entity; the difference is only who owns the handle.
	if ( m_Partition == PARTITION_INVALID_HANDLE )
	{
		m_Partition = ::partition->CreateHandle( static_cast< IHandleEntity * >(
			static_cast< IServerEntity * >( this ) ) );
	}

	::partition->Remove( PARTITION_ENGINE_NON_STATIC_EDICTS | PARTITION_ENGINE_SOLID_EDICTS
	                   | PARTITION_ENGINE_TRIGGER_EDICTS, m_Partition );

	SpatialPartitionListMask_t mask = PARTITION_ENGINE_NON_STATIC_EDICTS;

	if ( !( Block().Head.SolidFlags & FSOLID_NOT_SOLID ) )
		mask |= PARTITION_ENGINE_SOLID_EDICTS;

	if ( Block().Head.SolidFlags & FSOLID_TRIGGER )
		mask |= PARTITION_ENGINE_TRIGGER_EDICTS;

	::partition->Insert( mask, m_Partition );
	::partition->ElementMoved( m_Partition, Block().Head.Origin + Block().Head.Mins, Block().Head.Origin + Block().Head.Maxs );
}

//-----------------------------------------------------------------------------
// THE FRAME'S OVERLAP REPORT -- plan_entity.md step 2b, and 3.8's "the trigger
// overlap report is one crossing per frame: native walks the partition for
// every solid-trigger shell and hands managed the list of overlapping pairs;
// managed diffs it against the touch link table".
//
// WHAT USED TO BE HERE AND WHY IT IS NOT. Touch, StartTouch, EndTouch and
// UpdateTouchLinks were a link table and a diff: a per-entity CUtlVector of
// who is inside, a frame stamp on each entry, an expiry sweep, a
// bTouchEveryTick branch, and three ENTITY_OP dispatches -- one INBOUND
// CROSSING PER TOUCHING PAIR PER FRAME. Every one of those is a decision about
// the game ("is this an arrival or the same overlap again", "does this class
// hear about every tick or only the edges") and 3.5 puts the table in
// EntityDirectory for that reason.
//
// So this side keeps the two things that are genuinely native: WHO OVERLAPS
// WHOM, which only the spatial partition knows, and the solver's enter/leave,
// which only VPhysics reports.
//
// THE PARTITION WALK IS THE ENGINE'S OWN, and that is deliberate rather than
// lazy. engine->TriggerMoved enumerates
// PARTITION_ENGINE_SOLID_EDICTS against the trigger's bounds and then applies
// ShouldTouchTrigger, FSOLID_USE_TRIGGER_BOUNDS, IsBoxIntersectingRay and --
// for a brush trigger -- ClipRayToCollideable against the real brush. Writing
// a second copy of that here would be a second answer to the question
// "is this inside that", and the failure mode of getting it wrong is a trigger
// that is found, measured and discarded, which from outside the game looks
// exactly like a trigger whose outputs are not wired, which is precisely why
// the walk is delegated rather than reproduced.
//-----------------------------------------------------------------------------

namespace
{
	// TWO LISTS, AND THE SECOND ONE IS THE REASON THIS IS NOT ONE.
	//
	// g_Pending ACCUMULATES: the engine reports an overlap the moment either
	// side moves, which for the player is once per usercmd and for a mover is
	// once per position write, and those arrive throughout the frame -- before
	// the report is taken, and after it.
	//
	// g_Report is the frame's ANSWER, built once and then held. It is NOT
	// consumed by being read, and that is the whole point: a caller may ask
	// twice (growing its buffer after a short answer does exactly that), and a
	// frame boundary that does not advance gpGlobals->framecount would make a
	// second ask see an emptied list. Either way the second answer would be
	// missing pairs the first one had, and a pair missing from a report is a
	// DEPARTURE to the diff on the other side -- a trigger firing OnEndTouch on
	// somebody who never moved. So the answer is idempotent within a frame and
	// nothing about reading it changes it.
	//
	// A flat array of handle PAIRS rather than a vector of a struct, because
	// that is the shape the ABI hands over and declaring the struct twice would
	// be the drift a fixed-width mirror exists to prevent: element 2n is one
	// end, 2n+1 the other.
	std::vector< uint32_t > g_Pending;
	std::vector< uint32_t > g_Report;

	// Which frame g_Report was built for.
	int g_ReportFrame = -1;

	// The pairs VPhysics is holding open.
	//
	// The two sources of "something is in me" have opposite shapes and always
	// have: the engine renews an overlap every tick and going quiet MEANS gone,
	// while the solver announces entry and exit once each and says nothing
	// between. That used to be a bPhysicsHeld flag on a link, with its own
	// interaction with the frame stamp. Here it is nothing of the sort -- a
	// held pair is simply REPORTED EVERY FRAME until the solver says it has
	// ended, so the managed diff sees a continuing overlap and needs no idea
	// that a solver exists.
	std::vector< uint32_t > g_SolverHeld;

	// The most pairs the list will hold between two reports.
	//
	// A CEILING AND NOT A SIZE. The list is drained once per frame, so in a
	// running game it holds one frame's overlaps -- a few dozen on the shipped
	// content. It grows without bound only if nothing is draining it, which is
	// what a managed side that does not call EntityOverlapReport looks like: a
	// half-merged ABI, an older assembly, a build where the frame participant
	// was dropped. That would be a slow leak for the length of a map with
	// nothing anywhere to say so, and §7's rule is that a refusal is printed.
	const int kMaxOverlapPairs = 8192;

	bool g_bOverlapsOverflowed = false;

	// A pair, appended once. The engine can report the same two entities twice
	// in one frame (a trigger that moved AND a solid that moved into it), and
	// so can the eager path plus the per-frame walk. Managed absorbs a
	// duplicate -- a link already stamped this frame is not a second arrival --
	// but sending it is bytes across the boundary for nothing.
	void OverlapAppend( std::vector< uint32_t > &list, uint32_t hOne, uint32_t hTwo )
	{
		if ( hOne == INVALID_EHANDLE_INDEX || hTwo == INVALID_EHANDLE_INDEX || hOne == hTwo )
			return;

		if ( (int)list.size() >= kMaxOverlapPairs * 2 )
		{
			// ONCE. A per-append message from a list nobody is draining is a
			// console nobody can read.
			if ( !g_bOverlapsOverflowed )
			{
				g_bOverlapsOverflowed = true;
				Warning( "[SourceSharp/Touch] the overlap list reached %d pairs without being "
				         "read. Managed code is not calling EntityOverlapReport, so no touch, "
				         "trigger or pickup will fire.\n", kMaxOverlapPairs );
			}

			return;
		}

		for ( size_t i = 0; i + 1 < list.size(); i += 2 )
		{
			if ( ( list[i] == hOne && list[i + 1] == hTwo )
			  || ( list[i] == hTwo && list[i + 1] == hOne ) )
				return;
		}

		list.push_back( hOne );
		list.push_back( hTwo );
	}

	// Everything the eager reports have gathered since the last time, moved into
	// the frame's answer and deduplicated on the way in.
	void OverlapDrainPending( std::vector< uint32_t > &into )
	{
		for ( size_t i = 0; i + 1 < g_Pending.size(); i += 2 )
			OverlapAppend( into, g_Pending[i], g_Pending[i + 1] );

		g_Pending.clear();
	}

	void OverlapForget( std::vector< uint32_t > &list, uint32_t hOne, uint32_t hTwo )
	{
		for ( size_t i = 0; i + 1 < list.size(); i += 2 )
		{
			if ( ( list[i] == hOne && list[i + 1] == hTwo )
			  || ( list[i] == hTwo && list[i + 1] == hOne ) )
			{
				list.erase( list.begin() + i, list.begin() + i + 2 );
				return;
			}
		}
	}
}

//-----------------------------------------------------------------------------
// Everything the report holds, dropped. A level change: every handle in it
// names an entity of the map that is going.
//-----------------------------------------------------------------------------
void StandaloneEntities_ClearOverlaps()
{
	g_Pending.clear();
	g_Report.clear();
	g_SolverHeld.clear();
	g_ReportFrame = -1;
	g_bOverlapsOverflowed = false;
}

//-----------------------------------------------------------------------------
// Something is standing in it.
//
// Reported by the engine through IServerGameEnts::MarkEntitiesAsTouching, and
// now RECORDED rather than dispatched. The shell has no opinion about what a
// touch means and -- since step 2b -- no opinion about whether it is a new one
// either: both are managed code's, and this is the sentence "the engine says
// these two overlap" with nothing added to it.
//
// STILL CALLED, and it is not redundant with the per-frame walk below. The
// engine reports an overlap the moment either side MOVES, which for the player
// is once per usercmd (shell_standalone_player.cpp asks on every command) and
// for a mover is once per position write. Those land in the same list the walk
// fills, are deduplicated into it, and cost no crossing -- so a touch found
// mid-frame is carried to the frame's one report rather than dropped.
//-----------------------------------------------------------------------------
void CStandaloneEntity::Touch( const Toucher_t &toucher )
{
	if ( !toucher.IsValid() )
		return;

	OverlapAppend( g_Pending, GetRefEHandle().ToInt(), toucher.GetRefEHandle().ToInt() );
}

//-----------------------------------------------------------------------------
// The solver says something entered a trigger volume of ours.
//
// Only VPhysics calls this, and only for an object inside a physics trigger --
// see CCollisionEvent::ObjectEnterTrigger. It cannot be folded into Touch above
// because the two report different things: an overlap report is renewed every
// tick, so one call is indistinguishable from the last call before something
// left, while this is said exactly once.
//
// The pair is therefore HELD -- reported every frame from now until EndTouch --
// which is what makes a solver overlap look to the managed diff exactly like an
// engine one that keeps being renewed. The old shape (a bPhysicsHeld flag on a
// link, skipped by the expiry sweep) said the same thing in the place that no
// longer keeps links.
//-----------------------------------------------------------------------------
void CStandaloneEntity::StartTouch( const Toucher_t &toucher )
{
	if ( !toucher.IsValid() )
		return;

	OverlapAppend( g_SolverHeld, GetRefEHandle().ToInt(), toucher.GetRefEHandle().ToInt() );
}

//-----------------------------------------------------------------------------
// And that it has left.
//
// The hold comes off. Whether the two are still touching is not decided here:
// if the engine also reports them by AABB this frame the pair is in the list
// anyway and the link stands, and if it does not, the managed sweep reports the
// departure. That disjunction used to be four lines of frame-stamp comparison
// in this file; it is the diff's ordinary behaviour now.
//-----------------------------------------------------------------------------
void CStandaloneEntity::EndTouch( const Toucher_t &toucher )
{
	if ( !toucher.IsValid() )
		return;

	OverlapForget( g_SolverHeld, GetRefEHandle().ToInt(), toucher.GetRefEHandle().ToInt() );
}

//-----------------------------------------------------------------------------
// THE ONE CALL, AND THE SPAN OF PAIRS -- SourceSharpEngineApi::EntityOverlapReport.
//
// Asks the engine, once per frame, which solids are standing inside each
// standalone TRIGGER, adds whatever the eager reports and the solver have
// contributed, and hands the lot over as pairs of handles.
//
// WHY THE TRIGGERS AND NOT EVERY SOLID. The relation is symmetric and the
// engine offers both directions of it -- TriggerMoved enumerates the solid list
// for one trigger, SolidMoved enumerates the trigger list for one solid -- so
// walking every trigger finds every (trigger, solid) pair there is, and walking
// the solids as well would find each of them a second time. Triggers are the
// smaller set by a wide margin, which is why that is the direction taken; the
// pairing rule is stock's own (CBaseEntity::PhysicsTouchTriggers: a moving
// trigger is not checked against other triggers).
//
// Returns the number of PAIRS found, which may exceed nMaxPairs -- in which
// case only nMaxPairs were written and the caller is expected to come back with
// a bigger buffer. Coming back is safe because the list is built once per
// FRAME and answered as many times as it is asked for: see g_Report and
// g_ReportFrame -- reading it does not consume it.
// Truncating silently would report a departure for every pair past the end.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityOverlapReport( uint32_t *pOutPairs, int32_t nMaxPairs )
{
	if ( gpGlobals->framecount != g_ReportFrame )
	{
		g_ReportFrame = gpGlobals->framecount;
		g_Report.clear();

		// FIRST what the eager reports found during this frame's movement.
		// They are why a player who walked into a trigger and stopped is
		// reported for the moment they arrived rather than for where the walk
		// below happens to find them.
		OverlapDrainPending( g_Report );

		for ( CStandaloneEntity *pEnt : g_Standalones )
		{
			// Only the triggers, and only ones the engine can be asked about.
			// TouchTriggers refuses the rest by itself; the test is repeated
			// here so the loop does not pay a call per non-trigger entity in
			// the map, which is nearly all of them.
			if ( !( pEnt->GetSolidFlags() & FSOLID_TRIGGER ) )
				continue;

			pEnt->TouchTriggers( pEnt->GetAbsOrigin() );
		}

		// The walk's own findings arrive by the same door every other overlap
		// report does -- MarkEntitiesAsTouching -> CStandaloneEntity::Touch --
		// so they are in g_Pending too.
		OverlapDrainPending( g_Report );

		for ( size_t i = 0; i + 1 < g_SolverHeld.size(); i += 2 )
			OverlapAppend( g_Report, g_SolverHeld[i], g_SolverHeld[i + 1] );
	}

	const int32_t nPairs = (int32_t)g_Report.size() / 2;

	if ( pOutPairs && nMaxPairs > 0 )
	{
		const int32_t nWrite = ( nPairs < nMaxPairs ) ? nPairs : nMaxPairs;

		for ( int32_t i = 0; i < nWrite * 2; i++ )
			pOutPairs[i] = g_Report[i];
	}

	return nPairs;
}

//-----------------------------------------------------------------------------
// This entity as something that can walk into another.
//
// The handle half is reached through IServerEntity and the interface half is
// this, and neither cast can be made from the other — which is exactly why
// Toucher_t cannot be built anywhere that only has an IGameEntity.
//-----------------------------------------------------------------------------
Toucher_t CStandaloneEntity::AsToucher()
{
	Toucher_t toucher =
	{
		static_cast< IHandleEntity * >( static_cast< IServerEntity * >( this ) ),
		NULL,
		this
	};

	return toucher;
}

//-----------------------------------------------------------------------------
// Which triggers this is standing in, asked of the engine.
//
// CBaseEntity::PhysicsTouchTriggers verbatim, including the rule that a moving
// TRIGGER is not checked against other triggers: the engine reads these two
// calls and does not care who made them, and getting the pairing wrong is what
// doubles every touchlink in the map.
//
// The bail-outs are the same ones too. A SOLID_BSP with no brush model is the
// console-created brush already recorded in docs/divergences.md, and asking the
// engine to sweep a volume that does not exist is how that divergence would
// have become a crash rather than a shrug.
//-----------------------------------------------------------------------------
void CStandaloneEntity::TouchTriggers( const Vector &vecPrevOrigin )
{
	// No edict means a server-only entity, and the engine indexes both of these
	// calls by one. Not in the partition means the world cannot see it yet —
	// which is the state every entity is in between construction and Spawn, and
	// asking the engine to sweep for it then is asking about an entity it has
	// not been told exists.
	if ( !m_pEdict || m_Partition == PARTITION_INVALID_HANDLE )
		return;

	const bool bIsTrigger = ( Block().Head.SolidFlags & FSOLID_TRIGGER ) != 0;
	const bool bIsSolid   = ( Block().Head.SolidType != SOLID_NONE ) && !( Block().Head.SolidFlags & FSOLID_NOT_SOLID );

	if ( Block().Head.SolidType == SOLID_BSP && Block().Head.CollisionModelIndex <= 0 )
		return;

	if ( bIsTrigger )
	{
		engine->TriggerMoved( m_pEdict, true );
		return;
	}

	if ( bIsSolid )
		engine->SolidMoved( m_pEdict, this, &vecPrevOrigin, true );
}

//-----------------------------------------------------------------------------
// The volume the engine tests trigger overlap against.
//
// NOT a point. This returned the origin for both corners — a zero-size box —
// which is what CCollisionProperty does only for an entity with no bounds at
// all. Every trigger on this hierarchy was in the trigger list, enumerated
// every tick, and overlapped nothing, because the box it was tested against had
// no volume in it.
//
// Nothing reports that. The trigger is found, measured, and discarded, and from
// outside the game it looks exactly like a trigger whose outputs are not wired.
//-----------------------------------------------------------------------------
void CStandaloneEntity::WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const
{
	if ( pMins ) *pMins = Block().Head.Origin + Block().Head.Mins;
	if ( pMaxs ) *pMaxs = Block().Head.Origin + Block().Head.Maxs;
}

void CStandaloneEntity::WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs )
{
	if ( pMins ) *pMins = Block().Head.Origin + Block().Head.Mins;
	if ( pMaxs ) *pMaxs = Block().Head.Origin + Block().Head.Maxs;
}

//-----------------------------------------------------------------------------
// Bound to its class and given a handle, before the map hands it a key.
//
// Two things happen here and they are both about TIMING rather than about what
// an entity is.
//
// The handle, because the managed instance is keyed by it and is created on the
// first keyvalue offered. An entity whose handle only arrived at Spawn had
// already dropped every value the map wrote — and a dropped key looks exactly
// like a key the map never had, which is why this was silent for so long in the
// CBaseEntity shells too.
//
// The edict, because an entity the client will see needs one from the moment it
// is in the entity list: AddNetworkableEntity is indexed BY the edict.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The handle store, and the one line that is not a store.
//
// Stock's CBaseEntity::SetRefEHandle mirrors the handle's serial into
// edict()->m_NetworkSerialNumber, and that mirror is what tells the engine an
// edict slot changed occupants: SV_PackEntity reuses the previous occupant's
// PackedEntity and ChangeFrameList whenever the serial it snapshotted still
// matches. This override stored the handle and mirrored nothing, so a freed
// edict reused by the next standalone entity was packed as a CONTINUATION of
// the dead one — an engine abort when their tiers' prop counts differ
// ("SnagChangeFrameList mismatched number of props"), a silently inherited
// delta baseline when they match. The rule and the full story live in
// standalone_netserial.h; the repro was two console commands on dm_lockdown.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetRefEHandle( const CBaseHandle &handle )
{
	// The ten bits restated in the engine-free header, pinned to the ten bits
	// stock masks with. If const.h ever changes, this line is the report.
	COMPILE_TIME_ASSERT( STANDALONE_NETSERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS );

	// THE SLOT TABLE ADOPTS THE HANDLE THE ENTITY LIST ASSIGNED rather than
	// issuing a second, disagreeing one (plan_entity.md §3.3), and
	// SourceSharp_SlotBindAt does the edict mirror that used to be the whole of
	// this function -- so there is one store here and not two.
	//
	// LANDED BY STEP 1h, AND IT IS STEP 1e's LINE. The bind is written out in
	// .claude/entity-port/requests/1c.md §2a, addressed to 1d and 1e; 1h cannot
	// wait for it, because 1h is what makes an object's game data a handle and
	// a handle nothing binds resolves to NULL -- every managed prop would stop
	// being simulated the moment the registry it replaced was deleted. See
	// .claude/entity-port/status/1h.md.
	//
	// THE UNBIND FIRST. SetRefEHandle is called more than once per entity: the
	// entity list calls it with INVALID_EHANDLE on removal
	// (entitylist_base.cpp:244), and BindAt REFUSES an entry that is already
	// bound. Without the unbind the second call silently does nothing and the
	// entry keeps pointing at an object that is about to be freed.
	//
	// `this` IS THE CONCRETE CStandaloneEntity * (step 1e). The table
	// type-erases what it is given and SourceSharp_ShellFromHandle
	// static_casts it back, so a BASE pointer in -- an IHandleEntity *, an
	// IServerEntity * -- is a pointer out that is wrong by the base's offset,
	// at every -W level, with nothing to report it. The wrapper's parameter
	// type is what makes that a compile error rather than a run-time one, and
	// it is why nothing on this line casts.
	if ( BlockHandle().IsValid() )
		SourceSharp_SlotUnbind( BlockHandle().GetEntryIndex() );

	BlockHandle() = handle;

	// The handle is a block field like any other, so the write says so. It is
	// not a typed SendProp -- the wire's copy of an entity's identity is the
	// edict index and the mirrored serial, not this -- so what the mark names
	// is the generic payload word overlying it.
	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Handle ) );

	if ( handle.IsValid() )
	{
		SourceSharp_SlotBindAt( handle, this, m_pEdict );
	}
	else
	{
		// Stock mirrors the invalid handle's serial too, deliberately --
		// "immediately overwritten by the next occupant's real serial", see
		// standalone_netserial.h -- so dropping this store would be a behaviour
		// change with no evidence behind it.
		StandaloneNetSerial_Mirror( m_pEdict, (unsigned long)handle.GetSerialNumber() );
	}
}

void CStandaloneEntity::PostConstruct( const StandaloneClassDesc_t *pClass )
{
	m_pClass = pClass;

	// THE SAME FIELD THE FACTORY SIZED THE ALLOCATION FROM (Part 18), which is
	// the whole reason the tier lives on the desc rather than being looked up
	// again here. Two lookups could return different answers — the type table
	// is refilled on a managed reload — and the two answers would be a blob of
	// one length under a table of another, with nothing reporting it.
	// THE FLOOR IS THE BLOCK'S TIER, not tier 0 (step 1b). The tier is what
	// BlobBytes() answers and what the ServerClass is chosen by, and operator
	// new has just allocated at least this much -- so answering 0 here would
	// hand C# a length of thirty-two for an allocation of five hundred and
	// twelve and encode the entity through a table addressing eight words of a
	// blob that is a hundred and twenty-eight long.
	if ( pClass )
		m_iTier = ( pClass->iTier >= 0 ) ? pClass->iTier
		                                 : SourceSharp_StandaloneMinTier();

	// THE ONE INVARIANT PER-CLASS ALLOCATION CAN BREAK, checked rather than
	// argued (Part 18).
	//
	// C# is handed this entity's blob and its length and writes its declared
	// layout into it. The tier is chosen as the smallest that FITS that layout,
	// so blob >= layout holds by construction — but only if the tier was
	// resolved from the published type, and an entity created before its type
	// was published gets the floor instead. That is the one path to a blob
	// shorter than the bytes C# will write, and the payload agreement check
	// cannot see it: it compares props against the blob and both would sit
	// inside the short one.
	//
	// Loud and once per class. Nothing is thrown and the entity still exists,
	// because refusing to construct here would leave the map with a hole and
	// this is a startup-ordering bug rather than a per-instance one — but it
	// must not be silent, because the symptom is a heap overrun somewhere else.
	// FROM THE DESC, NOT FROM GetClassname(). SetClassname runs further down
	// this function, so the entity has no name yet at this point — asking it
	// for one returned the empty string, ByName found nothing, and the check
	// could not fire at all. Found by provoking it: pinning every class to
	// tier 0 produced no warning, which is how a check that cannot fail looks
	// from the outside.
	const char *pName = ( pClass && pClass->pClassName ) ? pClass->pClassName : "";
	const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ByName( pName );

	if ( pType && pType->nLayoutBytes > BlobBytes() )
	{
		static bool s_bWarnedShortBlob = false;

		if ( !s_bWarnedShortBlob )
		{
			s_bWarnedShortBlob = true;
			Warning( "[SourceSharp] standalone '%s': allocated a %d-byte blob (tier %d) for a "
			         "class whose published layout is %d bytes. Its managed state would write "
			         "past the end of the allocation. This is a startup ORDERING fault -- the "
			         "entity was created before its type was published.\n",
				pName, BlobBytes(), m_iTier, pType->nLayoutBytes );
		}
	}

	if ( pClass && pClass->pClassName )
		SetClassname( pClass->pClassName );

	// What kind of thing it is, before the map says anything. Set here rather
	// than at Spawn because a keyvalue may change it and Spawn is what applies
	// the result.
	if ( pClass )
	{
		Block().Head.SolidType  = pClass->nSolidType;
		Block().Head.SolidFlags = pClass->nSolidFlags;
	}

	// Server-only: no edict, no ServerClass, never sent. What CServerOnlyEntity
	// means, and CBaseEntity::PostConstructor makes exactly the same choice at
	// exactly this point.
	// A SERVER-ONLY SHELL IS A STATE THIS BUILD CANNOT REACH, AND IT SAYS SO
	// RATHER THAN BINDING ONE -- plan_entity.md §3.3 and §7, lane
	// entlist-server. This read
	//
	//     SetRefEHandle( <the entity list>->AddNonNetworkableEntity( this ) )
	//
	// and it was the one registration that needed the LIST rather than an
	// index: AddNonNetworkableEntity takes a free entry off the engine-invisible
	// half's free list (entitylist_base.cpp:190) and that half is the managed
	// EntityDirectory's -- §3.3 gives it "serials the directory owns", and
	// EntityDirectory.AllocateInvisible hands entries out DOWNWARD from 8191
	// precisely to stay clear of the list's upward one. Rebuilding the list's
	// allocator here would put a third allocator on one range.
	//
	// IT DOES NOT HAVE TO BE REBUILT, because nothing takes this arm. Step 2e
	// made every server-only class shell-less: SOURCESHARP_SCRIPTED_LOGICAL is
	// the only kind with bServerOnly set (scripted_kinds.h:243) and it also sets
	// bShellLess, and SourceSharp_RegisterScriptedEntity returns BEFORE
	// installing a factory for a shell-less class (shell_scripted.cpp:133). A
	// class with no factory cannot be constructed by any of the four creation
	// routes, so no CStandaloneEntity is ever built for one.
	//
	// So this is a declared, printed refusal and not a silent no-op. If the
	// shell-less rule ever regresses, the Warning names the class and the entity
	// holds an invalid handle -- rather than a handle from an allocator that
	// disagrees with the directory's, which is the failure that would be
	// invisible until two entities shared an entry.
	if ( pClass && pClass->bServerOnly )
	{
		Warning( "[SourceSharp] standalone '%s' is a SERVER-ONLY class with a shell. "
		         "This build has no route that creates one (step 2e made every "
		         "server-only class shell-less), so there is no engine-invisible "
		         "entry to bind it at: the engine-invisible half of the handle "
		         "range belongs to the managed EntityDirectory. The entity is "
		         "constructed and holds NO handle -- nothing will resolve it.\n",
			pClass->pClassName ? pClass->pClassName : "?" );

		g_Standalones.push_back( this );
		return;
	}

	// A player binds to the edict the engine already allocated for the
	// connecting client (Phase 9a); everything else creates its own. Claimed
	// unconditionally so a pending edict can never leak onto the next entity.
	m_pEdict = SourceSharp_ClaimPendingPlayerEdict();
	m_bOwnsEdict = ( m_pEdict == NULL );

	if ( !m_pEdict )
		m_pEdict = engine->CreateEdict();

	if ( !m_pEdict )
	{
		Warning( "[SourceSharp] standalone: no free edict for '%s'\n", GetClassname() );
		return;
	}

	m_pEdict->SetEdict( this, true );
	m_pEdict->m_EdictIndex = engine->IndexOfEdict( m_pEdict );

	// The line that cost an afternoon.
	//
	// SetEdict above stores only m_pUnk. The edict keeps a SECOND, separate
	// pointer to the networkable, and CBaseEdict::GetNetworkable, AreaNum and
	// GetClassName all read that one — unguarded. CBaseEntity::PostConstructor
	// assigns it by hand, one line, with no comment saying it must:
	//
	//     edict()->m_pNetworkable = NetworkProp();
	//
	// Without it the entity spawns, holds a resolving handle and answers a
	// trace perfectly well, and then the engine calls AreaNum() on a null
	// pointer the moment it tries to SEND the thing. The crash is a jump to
	// address zero deep in the engine, which names nothing.
	m_pEdict->m_pNetworkable = this;

	// INTO THE SLOT TABLE, WHICH IS WHAT MAKES A HANDLE TO IT RESOLVE.
	//
	// THE KEYSTONE LINE -- plan_entity.md §3.3, lane entlist-server. This read
	//
	//     SetRefEHandle( <the entity list>->AddNetworkableEntity( this, index ) )
	//
	// and lane p4-flip's finding was that this line, its server-only twin above
	// and CWorld::AttachToEdictZero were what made CGlobalEntityList "not the
	// SDK's list any more" but "the port's own handle table": every managed
	// shell in the mod registered here and every handle to one resolved through
	// the result. §3.3's table is the replacement, and this is the line that
	// hands it the job.
	//
	// TWO STEPS RATHER THAN ONE, AND THAT IS WHY IT IS SourceSharp_SlotIssue AND
	// NOT SourceSharp_SlotBind. AddEntityAtSlot issued the serial, stored the
	// pointer, and then called SetRefEHandle from INSIDE itself
	// (entitylist_base.cpp:236) -- so this line called SetRefEHandle twice for
	// every entity in the map, once from the list and once on the returned
	// handle. Here SetRefEHandle is the single binder: it unbinds the entry the
	// shell held, stores the handle and binds it. A caller that had bound first
	// would be REFUSED by BindAt for an entry that is legitimately its own.
	//
	// THE ENTRY IS THE EDICT INDEX, which is the same slot the list used --
	// AddNetworkableEntity asserts the index is below MAX_EDICTS and binds
	// there. Nothing about which slot an entity gets has changed.
	//
	// AN INVALID HANDLE BACK means the entry was already occupied, which is a
	// bug rather than a condition: an edict the engine just handed out cannot
	// hold another shell. SetRefEHandle stores it either way, and
	// cs_sv_slot_table's refusal counter is where it shows up.
	const int nEntry = engine->IndexOfEdict( m_pEdict );
	const CBaseHandle hIssued = SourceSharp_SlotIssue( nEntry );

	// A REFUSED ISSUE IS A DECLARED, PRINTED REFUSAL AND NOT A SILENT NOTHING
	// (§7). CGlobalEntityList only Assert()ed this (entitylist_base.cpp:216) and
	// an assert compiles out, so stock's answer to an occupied slot in a
	// shipping build was to overwrite it and give two live entities one handle.
	//
	// The refusal is louder than the overwrite because what follows it is
	// silent: an entity with an invalid handle is CONSTRUCTED, SPAWNED and
	// STANDING IN THE MAP, and nothing resolves it. Every find-by-name walk
	// stops at it -- SourceSharp_FindEntityByName answers with the handle it
	// found and the managed loop reads INVALID as "no more" -- so the symptom is
	// an entity that exists, moves and collides but cannot be named, which
	// reads exactly like a mapper's typo.
	if ( !hIssued.IsValid() )
	{
		Warning( "[SourceSharp] standalone '%s': entry %d could not issue a "
		         "handle -- it is out of range or ALREADY BOUND to another "
		         "shell. This entity will exist and resolve to nothing: no "
		         "handle to it resolves and no find-by-name reaches it. "
		         "cs_sv_slot_table's refusal counter is the other half of this.\n",
			( pClass && pClass->pClassName ) ? pClass->pClassName : GetClassname(), nEntry );
	}

	SetRefEHandle( hIssued );

	g_Standalones.push_back( this );
}

//-----------------------------------------------------------------------------
// Placed in the world, after every key has been applied.
//-----------------------------------------------------------------------------
void CStandaloneEntity::Spawn()
{
	// Remembered before anything can move it. Movement is described relative to
	// where the mapper drew the entity, and by the time something asks, the
	// current origin is no longer that.
	m_vecSpawnOrigin   = Block().Head.Origin;
	m_angSpawnRotation = Block().Head.Angles;

	// The map's "angles" key as well as its "origin": both were recorded by
	// KeyValue rather than applied, and this is the moment they become the
	// frame everything else reads.
	AngleMatrix( Block().Head.Angles, Block().Head.Origin, Block().Head.WorldTransform );

	// A model, if this entity has one. The shell does not care whether it does:
	// that is what StandaloneAnimating means in C#, and here it is an index that
	// may stay zero.
	// The map's "model" key, if it wrote one. Through the same function managed
	// code uses, so there is one place that precaches and one place that tells
	// the client.
	if ( m_szModel[0] )
	{
		char szModel[ sizeof( m_szModel ) ];
		ss::StrCopy( szModel, m_szModel, sizeof( szModel ) );
		SetModelName( szModel );
	}

	if ( m_pEdict )
	{
		// Without this the entity exists and is never sent: the engine only
		// transmits what it has been told changed.
		m_pEdict->StateChanged();

		// And without THIS it is never sent either, for a completely different
		// reason that looks identical from outside.
		//
		// CheckTransmit masks the edict's state flags down to
		// DONTSEND|ALWAYS|PVSCHECK|FULLCHECK and decides from those alone.
		// SetEdict leaves only FL_EDICT_FULL, which is none of them — so the
		// mask comes out zero, every branch falls through, and the entity is
		// skipped every frame.
		//
		// CBaseEntity declares this through SetTransmitState, called from
		// UpdateTransmitState during spawn. Nothing calls it for an entity that
		// is not one, so it says so itself: send me when I am in somebody's PVS.
		m_pEdict->ClearTransmitState();
		m_pEdict->m_fStateFlags |= FL_EDICT_PVSCHECK;

		// Which visibility clusters it is in.
		//
		// Without this the engine is handed a zeroed PVSInfo_t while deciding
		// whether to send the entity, and CServerNetworkProperty does exactly
		// this call for every stock entity — through RecomputePVSInformation,
		// driven by an edict dirty flag this entity has nobody to set.
		engine->BuildEntityClusterList( m_pEdict, BlockPVSInfo() );
	}

	// Into the spatial partition, which is what a trace and a trigger overlap
	// both actually query — and only for something that should be found. An
	// entity with no shape is not a thing to hit, and putting one in the
	// partition makes an invisible obstacle out of a timer.
	UpdatePartition();

	OnManagedSpawn();

	// A class whose kind the solver moves gets its physics object from
	// managed, after its OnSpawn (ScriptedEntityRegistry.Spawn ->
	// EntityPhysicsCreation.Spawned); the shell only holds it (SwapPhysics).
}

//-----------------------------------------------------------------------------
// The map's keys.
//
// Both sides see every key, deliberately, and that is not the same as trying
// one and falling back to the other. The CBaseEntity shells learnt this twice:
// asking the engine first meant a class declaring its own "speed" never saw the
// map's, because the datadesc claimed the name; asking managed code first meant
// the engine stopped receiving "health" and "model", and a barrel with no
// health is not breakable.
//
// They are not really the same key twice. The shell's m_iHealth and a managed
// class's health field both want the map's number, and both should have it.
//-----------------------------------------------------------------------------
bool CStandaloneEntity::KeyValue( const char *szKeyName, const char *szValue )
{
	// TWO STATEMENTS AND NOT ONE `||`, and the order is the one this function
	// has always had: the shell's arm first, then the offer, and the offer is
	// UNCONDITIONAL. `a || b` would skip the offer for every key the shell
	// claims -- "origin", "targetname", "model" and the rest -- and a class
	// that reacts to being named would stop hearing about it.
	const bool bShell   = KeyValueShellOnly( szKeyName, szValue );
	const bool bManaged = OnManagedKeyValue( szKeyName, szValue );

	return bShell || bManaged;
}

//-----------------------------------------------------------------------------
// The shell's own arm, without the offer -- plan_entity.md step 2d.
//
// The map spawner is managed now and has already given every key to
// ScriptedEntityRegistry from managed memory, so the offer above would be a
// second delivery AND a crossing. This is the half it wants. See
// shell_standalone.h for why the if-chain is not copied.
//-----------------------------------------------------------------------------
bool CStandaloneEntity::KeyValueShellOnly( const char *szKeyName, const char *szValue )
{
	bool bShell = false;

	if ( !ss::StrICmp( szKeyName, "origin" ) )
	{
		// Recorded rather than applied: Spawn() is what places it, and applying
		// it here would put the entity in the world before it had the rest of
		// its keys.
		float x = 0.0f, y = 0.0f, z = 0.0f;
		if ( sscanf( szValue, "%f %f %f", &x, &y, &z ) == 3 )
			Block().Head.Origin.Init( x, y, z );

		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "angles" ) )
	{
		float p = 0.0f, y = 0.0f, r = 0.0f;
		if ( sscanf( szValue, "%f %f %f", &p, &y, &r ) == 3 )
			Block().Head.Angles.Init( p, y, r );

		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "targetname" ) )
	{
		// What an output looks this entity up by. Without it the entity can be
		// placed in a map and never addressed.
		ss::StrCopy( m_szName, szValue, sizeof( m_szName ) );
		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "parentname" ) )
	{
		// Claimed HERE, beside origin and targetname, rather than by a managed
		// class — because parenting is not something a class opts into. Every
		// entity a map can place can be parented, so a key claimed in one class
		// would have to be claimed in all of them, and the one that forgot would
		// silently drop a mapper's link.
		//
		// Recorded rather than resolved, for two reasons at once: the entity it
		// names may not have been created yet, and neither end has the position
		// the offset is measured from until Spawn has run.
		// StandaloneHierarchy_ResolveParentNames finishes it in a pass of its own.
		StandaloneHierarchy_SetParentName( this, szValue );
		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "parentattachment" ) )
	{
		// Claimed beside parentname for the same reason, and recorded rather
		// than resolved for one more: an attachment is a name looked up on the
		// PARENT's model, and at parse time neither the parent nor its model is
		// known. It may also arrive BEFORE parentname — a map writes its keys in
		// whatever order Hammer wrote them — so it is held on its own until
		// there is a link to put it in.
		StandaloneHierarchy_SetParentAttachmentName( this, szValue );
		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "spawnflags" ) )
	{
		m_nSpawnFlags = atoi( szValue );
		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "health" ) )
	{
		m_iHealth = atoi( szValue );
		bShell = true;
	}
	else if ( !ss::StrICmp( szKeyName, "model" ) )
	{
		// Recorded, not applied. Precaching must happen during level load and so
		// must this — but Spawn is where the entity enters the world, and a
		// model index set before that is a number with nothing to draw it.
		ss::StrCopy( m_szModel, szValue, sizeof( m_szModel ) );
		bShell = true;
	}

	// THE OUTPUT-CONNECTION ARM WAS HERE, AND IS GONE -- plan_entity.md step 2a.
	//
	// It matched the key against the output names the class had declared and
	// called m_Outputs[ nSlot ].ParseEventAction( szValue ), building a native
	// CEventAction list. Both halves of that are managed now:
	// ScriptedEntityRegistry.KeyValue asks the class's own output list --
	// which is where the names came from in the first place, so the round trip
	// through StandaloneOutputIndex was asking C# a question C# had answered --
	// and EventAction.Parse does the parse. The list hangs on the entity's row
	// in EntityDirectory, where a hot reload can read it and put it back; the
	// native one could not be reached at all.
	//
	// NOTHING REPLACES IT HERE, and the key still reaches managed code: the
	// offer below is unconditional and always was.

	// THE OFFER TO MANAGED CODE IS NOT HERE ANY MORE; it is in KeyValue above,
	// which is this function plus that one line. Nothing else changed.
	return bShell;
}

//-----------------------------------------------------------------------------
// An input from the map.
//
// The shell claims none of its own. "Kill" and "SetHealth" used to be handled
// here and they are game rules wearing engine clothing: what killing means, and
// whether an entity's health may be set from outside, are exactly the decisions
// this whole exercise is about moving.
//-----------------------------------------------------------------------------
bool CStandaloneEntity::AcceptInput( const char *szInputName, const Activator_t &activator,
                                     IGameEntity *pCaller, const ss::variant_t &Value )
{
	CStandaloneActivatorScope scope( activator );

	return OnManagedInput( szInputName, activator, Value );
}

int CStandaloneEntity::TakeDamage( const ss::CTakeDamageInfo &info )
{
	// The shell makes no decision about damage. It asks, applies the answer to
	// health, and reports death — armour, resistance and invulnerability are
	// all rules, and rules are managed code's.
	//
	// The managed side answers how much actually landed, which is also how it
	// says "none": a class that refuses damage returns zero, and there is no
	// second mechanism for that.
	const int nTaken = OnManagedDamage( info );

	if ( nTaken <= 0 )
		return 0;

	m_iHealth -= nTaken;

	if ( m_iHealth <= 0 )
		OnManagedKilled( info.GetAttacker() );

	return nTaken;
}

//-----------------------------------------------------------------------------
// CStandaloneEntity::FireNamedOutput WAS HERE, AND IS DELETED -- plan_entity.md
// step 2a.
//
// It resolved an output name against the list the managed class had declared,
// picked the current activator out of a native scope, and called
// COutputEvent::FireOutput -- which walked a CEventAction list and posted into
// g_EventQueue. Every step of that is managed now (SourceSharp.Abi/EntityIo.cs
// and EntityDirectory's I/O half), and the one caller was the
// FireScriptedOutput ABI slot, which is deleted with it.
//
// WHAT THE MOVE BOUGHT, in the terms plan_entity.md §5 counts in: an output
// fired by a C# entity and delivered to another C# entity cost TWO crossings --
// out through FireScriptedOutput and back in through ENTITY_OP_INPUT -- and
// now costs none. The activator scope this function set up is a field on the
// queued event.
//-----------------------------------------------------------------------------

//=============================================================================//
// Being simulated.
//=============================================================================//

//-----------------------------------------------------------------------------
// WHICH ENTITY OWNS WHICH PHYSICS OBJECT IS NOT ANSWERED HERE ANY MORE.
//
// It used to be a file-scope CUtlVector< CStandaloneEntity * > walked per
// callback, with PhysicsStandaloneOwner and PhysicsStandaloneToucher scanning
// it for the object. plan_entity.md step 1h deletes it: the object carries its
// own owner in its game data now (the packed handle plus one -- see
// sourcesharp/game/shared/physics_gamedata.h; managed's EntityPhysicsCreation
// writes it), and the
// slot table of §3.3 turns that back into the shell with the serial checked.
//
// Both functions still exist, because physicsowner.h still calls them; they
// live in shell_physicsobjects.cpp, which is the file that owns making an
// entity physical. There is no state here to keep in step with anything.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// An entity's physics object is MADE by managed (EntityPhysicsCreation: the
// solid, the surface, the params, static or moveable, asleep, debris, the
// solidity order) and HELD here (SwapPhysics), because the engine and the
// shell read it every frame. Its teardown stays here with the hold.
//-----------------------------------------------------------------------------
void CStandaloneEntity::DestroyPhysics()
{
	if ( !m_pPhysics )
		return;

	// The owner goes before the object does. DestroyObject is DEFERRED while
	// the solver is stepping (the environment queues it), so the object outlives
	// this call and VPhysics may still hand it to a callback -- and the entity
	// is about to be gone. Clearing the game data makes that window answer
	// "nobody" at the first question rather than relying on the slot table to
	// notice, and the serial check behind it is then belt to this braces.
	m_pPhysics->SetGameData( NULL );

	// Through physenv, not delete: the object belongs to the solver and the
	// solver keeps lists of it.
	if ( physenv )
		physenv->DestroyObject( m_pPhysics );

	m_pPhysics = NULL;

	// The object's velocity was held for it (PhysicsUpdateOwners); with the
	// object gone the entity moves at its own member again, as before it had one.
	m_vecMoveVelocity = m_vecVelocity;
}

//-----------------------------------------------------------------------------
// The solver's answer, copied onto the entity, for every object managed's
// PhysicsFrame hands over: stock's PhysFrame calls VPhysicsUpdate on each
// active object, and so does this.
//-----------------------------------------------------------------------------
void CStandaloneEntity::VPhysicsUpdate( IPhysicsObject *pObject )
{
	// The object the solver names, which is this entity's — the loop hands it
	// over rather than making the entity look it up, and the two agreeing is
	// worth checking rather than assuming.
	if ( pObject == m_pPhysics )
		UpdatePhysics();
}

void CStandaloneEntity::UpdatePhysics()
{
	// WHICH OBJECTS ARE WRITTEN IS MANAGED'S (2026-10-12, plan_physics_bones_perf
	// C2): every object the solver's active list names, as stock's. The native
	// "asleep and asleep last frame" skip that stood here went: vphysics lists an
	// object asleep only on the frame it froze (CSleepObjects drops it at the
	// next Simulate), which is the frame its final pose appears, and the skip
	// fired only for an object woken and frozen inside one step -- the one case
	// where skipping left it where it was. A static object is never active.
	if ( !m_pPhysics )
		return;

	Vector vecOrigin;
	QAngle angRotation;
	m_pPhysics->GetPosition( &vecOrigin, &angRotation );

	// Both halves in one write. Assigning the block's angles directly and then
	// calling SetAbsOrigin was the path that made this the most visible case:
	// a physics prop is the entity most likely to be rotated and the entity
	// whose position is recomputed most often, so it spent every frame with a
	// transform that disagreed with the angles it was networking.
	SetAbsTransform( vecOrigin, angRotation );
}

//=============================================================================//
// Flying.
//=============================================================================//

void CStandaloneEntity::SetVelocity( const Vector &vecVelocity )
{
	// m_bFlying WAS SET HERE AND IS DELETED -- plan_entity.md step 2g. It said
	// "was this velocity set as a flyer's or as a mover's", because two owners
	// wrote this member and only a bit could tell them apart. Both owners are
	// SourceSharp.Server/EntityMotion.cs now and it answers the question from
	// which set the entity is in, so the bit had one reader left and then none.
	m_vecVelocity = vecVelocity;

	// And the velocity a rider's ground reads: managed calls this for every
	// velocity set, beside the object's own when there is one, as stock's
	// Teleport writes both, so the rider sees it this tick.
	m_vecMoveVelocity = vecVelocity;
}

void CStandaloneEntity::SetOwnerHandle( uint32_t hOwner )
{
	BlockOwner() = ( hOwner == INVALID_EHANDLE_INDEX )
		? CBaseHandle()
		: CBaseHandle::UnsafeFromIndex( (int)hOwner );

	MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( OwnerHandle ) );
}

//-----------------------------------------------------------------------------
// The owner as the trace filter wants it, or NULL if there is none or it has
// since died.
//
// IHandleEntity rather than CBaseEntity, because that is what the filter
// compares and because an owner on this hierarchy is not a CBaseEntity at all.
// PassServerEntityFilter checks pointer equality first, which is the only test
// that has to work here.
//-----------------------------------------------------------------------------
IHandleEntity *CStandaloneEntity::OwnerHandleEntity() const
{
	return SourceSharp_EntityFromHandle( BlockOwner() );
}

//-----------------------------------------------------------------------------
// FLIGHT IS NOT HERE ANY MORE -- plan_entity.md step 2f.
//
// UpdateFlight integrated a world-space velocity, traced the step, and decided
// from the trace's answer whether the entity had arrived, bounced or stopped.
// Every one of those is a decision about what the game does, and §6's rule is
// that a decision belongs in C#. It is SourceSharp.Server/EntityMotion.cs now,
// run once per frame from StandaloneHierarchy.OnThink at exactly the point
// StandaloneEntities_Update below used to call this -- after the touch links
// and the movers, before the thinks.
//
// WHAT WENT WITH IT, so a reader looking for it does not conclude it was lost:
//
//   the trace              still native, still MASK_SHOT, but through the
//                          ABI's TraceLine slot, which C# calls with the
//                          projectile passed through. The one thing it cannot
//                          yet be told is the SECOND skip (the owner);
//                          EntityMotion says what it does instead and
//                          .claude/entity-port/abi-requests/2f.md asks for it.
//
//   the impact             was OnManagedImpact, one INBOUND crossing per
//                          projectile per wall. The managed integrator already
//                          holds the entity, so "you hit this" is a virtual
//                          call and costs nothing. OnManagedImpact and
//                          ENTITY_OP_IMPACT therefore have no caller left;
//                          both live in files this step does not own and the
//                          request above asks for them to be retired.
//
//   m_bFlying              WAS still written by SetVelocity, so that
//                          shell_mover_velocity.h could ask whose velocity a
//                          stop may clear. Step 2g took the mover into the same
//                          managed class, so there are no longer two owners to
//                          tell apart and the bit is deleted.
//
//   m_flGravityScale       still written, still read by EntityGetGravity for
//   m_nBouncesLeft         the save state. The managed integrator carries its
//                          own copy, told to it at launch; these are the
//                          mirror the ABI reads back.
//-----------------------------------------------------------------------------

//=============================================================================//
// Moving.
//=============================================================================//

//-----------------------------------------------------------------------------
// MOVETYPE_PUSH IS NOT HERE ANY MORE -- plan_entity.md step 2g.
//
// MoveToPosition, MoveToAngles, StopMoving, BlockerAt and UpdateMovement were
// this file's mover. Between them they decided where a brush was going, how far
// it got this frame, whether somebody standing in its way should stop it,
// whether that somebody was a passenger rather than an obstruction, how fast the
// brush was actually travelling, and when it had arrived. Every one of those is
// a decision about what the game does, which is §6's definition of policy, so
// all of it is SourceSharp.Server/EntityMotion.cs -- the push half of the same
// class step 2f put the flight half in. The arithmetic is
// SourceSharp.Abi/MoverMath.cs, engine-free and checked by fact.
//
// WHAT WENT WITH IT, so a reader looking for it does not conclude it was lost:
//
//   the blocker walk    was ::partition->EnumerateElementsInBox over the
//                       DESTINATION box, through CAnyEntitiesEnum. C# has
//                       EntitiesInSphere and no box query, so it asks with the
//                       box's own circumscribed sphere and applies the box test
//                       itself (MoverMath.Blocks). That is a LOOSER walk and a
//                       SLOWER one -- SourceSharp_EntitiesInSphere walks every
//                       standalone entity where the partition walked a tree.
//                       .claude/entity-port/abi-requests/2g.md asks for the box.
//
//   standalone_riders.h the rider-or-blocker rule. MoverMath.IsRider and
//                       MoverMath.Blocks, with the same four units of tolerance
//                       and the same reason for it; the header and its gtest are
//                       deleted, and the facts are EntityMotionPushTests'.
//
//   shell_mover_velocity.h  MoverMath.Velocity, unchanged arithmetic.
//                       StandaloneMover_OwnsVelocity is GONE rather than ported:
//                       it existed because two owners wrote m_vecVelocity and
//                       only m_bFlying could tell them apart. Both owners are
//                       one managed class now, so the question is answered by
//                       which set an entity is in, and m_bFlying with it.
//
//   OnManagedMoveDone   were one INBOUND crossing per arrival and per refused
//   OnManagedBlocked    step. The managed integrator already holds the entity,
//                       so both are virtual calls now and neither has a caller
//                       left in C++. They live in shell_dispatch.cpp, which this
//                       step does not own; the request above asks for them.
//
//   cs_sv_movers_blocked  the same convar, declared in C# beside the code that
//                       prints for it (EntityMotion.cs, MoversBlockedConVar).
//
//   THE RIDERS          are the thing that was NEVER here. Stock's push sweeps
//                       the brush and drags what it meets; this file did not,
//                       so a managed mover carried nobody and four hand-written
//                       classes did it for themselves in their own thinks. The
//                       integrator does it for every mover now, which is what
//                       made the live gate's `lift` step passable.
//
// SetAngularVelocity BELOW IS NOT PART OF THAT and stays: m_angVelocity is what
// EntityGetAngularVelocity reads back for a non-simulated entity, which is a
// sensor's question rather than the integrator's. The managed side holds the
// number it integrates and writes this one through, the same split step 2f made
// for gravity scale and bounces.
//-----------------------------------------------------------------------------
void CStandaloneEntity::SetAngularVelocity( const QAngle &angPerSecond )
{
	m_angVelocity = angPerSecond;
}


//-----------------------------------------------------------------------------
// Deferred, always.
//
// Deleting an entity inside its own damage handler or its own think frees it
// while the caller still holds it — the bullet path is still holding the trace
// that names it, and the destructor pulls it out of the partition and the
// entity list while the engine may be walking either. UTIL_Remove exists for
// exactly this reason.
//
// THE MARK IS MANAGED NOW -- plan_entity.md step 2c. §3.5 puts removal in the
// EntityDirectory: Remove(handle) marks, and the directory frees at the end of
// the frame with ONE DestroyShell crossing for the whole batch. WHEN an entity
// dies is a decision and §6 keeps decisions out of C++; what is left here is the
// delete, which is mechanism.
//
// THE NATIVE LIST SURVIVES AS A FALL-BACK, AND IT IS NOT DEAD BELT AND BRACES.
// The directory answers "not mine" for a shell it never bound, and a removal is
// not a thing to lose because a route was not there. On a running server this
// branch is not taken at all: cs_sv_crossings' ServerEntityRemoveRequested
// counts the native callers left, and the only one is mapentities.cpp's
// template-source removal, which dies with that file in step 2d.
//
// MANAGED CODE REMOVING SOMETHING DOES NOT COME THROUGH HERE. Engine.RemoveEntity
// marks in the directory directly (EntityRemovalRoutes), so the common path
// costs no crossing in either direction where it used to cost one out and one
// back.
//-----------------------------------------------------------------------------
void CStandaloneEntity::Remove()
{
	const CBaseHandle &handle = GetRefEHandle();

	if ( handle.IsValid() )
	{
		IManagedEntities *pApi = ManagedEntities();   // plan_engineapi_vtables E9

		if ( pApi &&
		     pApi->EntityRemoveRequested( (uint32_t)handle.ToInt() ) != 0 )
			return;
	}

	if ( std::find( g_PendingDeletes.begin(), g_PendingDeletes.end(), this ) == g_PendingDeletes.end() )
		g_PendingDeletes.push_back( this );
}

//-----------------------------------------------------------------------------
// THE SHELLS BEHIND A FRAME'S REMOVALS, FREED -- plan_entity.md step 2c, the
// engine API's DestroyShell, and the ONE crossing a frame of removals costs.
//
// The directory has already told each of these entities it is going, taken its
// children, and dropped its own binding. What is left is the delete.
//
// A HANDLE THAT NO LONGER RESOLVES IS SKIPPED AND IS NOT AN ERROR. Managed code
// frees in mark order, and a parent freed earlier in the same batch takes its
// children with it -- so a child can appear in a batch it is already gone from
// by the time this reaches it. The resolve is what makes that safe: it is one
// entity-list lookup with the serial revalidated, so a handle whose entry has
// been recycled answers the NEW occupant's identity and fails the compare
// rather than freeing a live entity.
//-----------------------------------------------------------------------------
void SourceSharp_DestroyShell( const uint32_t *pHandles, int32_t nCount )
{
	if ( !pHandles || nCount <= 0 )
		return;

	for ( int32_t i = 0; i < nCount; i++ )
	{
		const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( pHandles[ i ] );

		if ( ent.pStandalone )
			delete ent.pStandalone;
	}
}

//-----------------------------------------------------------------------------
// THE FIRST HALF OF THE STANDALONE FRAME: what every entity's think should see.
//
// Touch links and the movers, for every shell. Who has LEFT is computed
// before the thinks so that a think asking "is anybody standing in me" sees
// this frame's answer rather than last frame's.
//
// FLIGHT LEFT THIS LOOP at step 2f and is a managed pass now; see the block
// where UpdateFlight used to be. StandaloneHierarchy.OnThink runs it between
// this call and the thinks, which is where it ran here.
//
// THIS USED TO RUN THE THINKS TOO, and step 1g took them out. The loop asked
// each shell whether its m_flNextThink had come and called Think() on the ones
// whose had; Think() crossed into managed code. That is one crossing per
// thinking entity per frame, which plan_entity.md §3.4 forbids. The schedule is
// now a managed heap (SourceSharp.Abi/ThinkScheduler.cs) which the one
// GameFrame crossing runs, and this side keeps only the mechanism.
//
// SPLIT IN TWO BECAUSE THE ORDER AROUND THE THINKS IS LOAD-BEARING, and both
// halves of it are. The hierarchy compose must run AFTER the thinks (a think is
// one of the things that can advance a parent's sequence, and a child placed
// before it sits on last frame's pose) and the deferred deletes must run after
// them too (an entity that decides to die inside its own think must not be
// freed while something still holds it, and must not survive the frame either).
// Putting the managed pass before this whole function would break the first
// constraint; putting it after would break both. So the frame is
// Update -> managed thinks -> Finish, and the ordering decision is in C#
// beside every other one this build makes -- which is IEngineFrame's own rule:
// a step is never a decision.
//-----------------------------------------------------------------------------
void StandaloneEntities_Update()
{
	// AND NOW IT DOES NOTHING, and that is the end of a three-step demolition
	// rather than an oversight. This loop had three lines in it:
	//
	//   the think walk       step 1g: one crossing per thinking entity, which
	//                        §3.4 forbids by name. A managed heap now.
	//   the touch sweep      step 2b: expiring a link is a diff, and the diff
	//                        is managed.
	//   pEnt->UpdateMovement()  step 2g: the mover, which is this file's block
	//                        above.
	//
	// The body is kept as this comment rather than the function deleted, because
	// SourceSharp_FrameStandaloneUpdate is an ABI slot and removing a slot is a
	// shape change no lane may make on its own. NOTHING CALLS IT: the managed
	// frame stopped asking for this step in the same commit
	// (StandaloneHierarchy.OnThink), so it costs no crossing either.
	// .claude/entity-port/abi-requests/2g.md asks the integrator to delete the
	// step, the slot and this function together.
}

//-----------------------------------------------------------------------------
// THE SECOND HALF: what has to happen after the thinks have run.
//
// The attachment-parented children, and then the deletions.
//-----------------------------------------------------------------------------
void StandaloneEntities_Finish()
{
	// THE ATTACHMENT-PARENTED CHILDREN ARE NOT COMPOSED HERE ANY MORE, and
	// nothing was dropped: they are composed by managed code, immediately
	// before this call.
	//
	// The sweep is the one thing on this hierarchy that is not eager -- an
	// attachment frame moves because the CLOCK moved, and no position setter
	// fires for that -- and it has to run AFTER the thinks and BEFORE the
	// deletes. That is still exactly where it runs: the registry is
	// SourceSharp.Abi/Hierarchy.cs now, and SourceSharp.Server's
	// StandaloneHierarchy.OnThink drives it between the directory's think pass
	// and this function. The order is unchanged; what went is the crossing a
	// native sweep would have needed to reach a managed registry.

	// THE FRAME'S REMOVALS ARE NOT DRAINED HERE ANY MORE, and nothing was
	// dropped: the list is EntityDirectory's, and its free pass runs in exactly
	// this position -- immediately after this call, in the same
	// StandaloneHierarchy.OnThink that composes the attachments immediately
	// before it. What is drained below is the FALL-BACK list, which holds only
	// removals the directory refused (see CStandaloneEntity::Remove) and is
	// empty on a running server.
	//
	// By index from the END, for the same reason StandaloneEntities_LevelShutdown
	// below says: ~CStandaloneEntity removes itself from THIS list, so deleting
	// element 0 shifts every later element down one and the forward walk then
	// skips whatever slid into the slot it just vacated.
	//
	// Latent for as long as nothing removed two standalone entities in the same
	// frame. point_template removes ALL of a group's sources at once, and with
	// three of them the middle one survived: claimed, un-spawnable, and still
	// standing in the world answering to a name the copies were supposed to
	// inherit.
	//
	// Drained from the tail rather than walked down by index, and that is the
	// part parenting added: removing a parent removes its children, so a
	// destructor can APPEND to this list. A downward walk has already passed the
	// tail by the time the entry arrives there, and the RemoveAll below would
	// then discard it — leaving a child that was told to go still standing in the
	// world with nothing left holding a request to remove it. Taking the tail
	// each time terminates because ~CStandaloneEntity pulls itself out.
	while ( !g_PendingDeletes.empty() )
		delete g_PendingDeletes.back();

	g_PendingDeletes.clear();
}

//-----------------------------------------------------------------------------
// Removes every standalone entity, at level change.
//
// gEntList cleans up the CBaseEntity world between maps and cannot see these,
// so without this an entity from the previous map survives into the next one —
// holding an edict, answering to its name, and thinking.
//-----------------------------------------------------------------------------
void StandaloneEntities_LevelShutdown()
{
	// THE PARENT LINKS ARE ALREADY GONE, and the order that mattered is kept.
	// They had to be emptied before any destructor here could find a child to
	// orphan -- a level's worth of entities each removing the next is
	// bookkeeping for a world that is about to stop existing, and it would queue
	// deferred deletes that nothing is left to drain. The registry is managed
	// now, and SourceSharp.Server's StandaloneHierarchy empties it in
	// OnLevelShutdownPreEntity immediately before calling this.

	// By index from the end: the destructor removes the entity from the list it
	// is being walked.
	for ( int i = (int)g_Standalones.size() - 1; i >= 0; i-- )
		delete g_Standalones[i];

	g_Standalones.clear();
	g_PendingDeletes.clear();

	// AND THE OVERLAP REPORT -- step 2b. Every handle in it names an entity of
	// the map that has just been destroyed. Left standing, the first report of
	// the NEXT map would hand managed code a set of pairs made of dead handles,
	// and a handle whose entry has been re-occupied resolves to its new tenant.
	StandaloneEntities_ClearOverlaps();
}

// cs_sv_handle_cross_check, the slot table checked against the compiler on
// real entities, is a diagnostic: it lives in shell_standalone_base_debug.cpp,
// which only a debug build compiles.
