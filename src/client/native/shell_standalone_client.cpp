//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's parallel hierarchy, implemented =======//
// SS_REASON a  a vtable the engine calls through
//
// See shell_standalone_client.h for the tree and why it mirrors the server's.
//
// Most of this file is drawing, because drawing is what the client half is for.
// The server decides; this side shows.
//
//=============================================================================//

#include "cbase.h"
#include "shell_standalone_client.h"
#include "standalone_renderable_handle.h"

// plan_entity.md Phase 5a. §3.3's slot table and the five resolvers that
// replaced this file's twelve cl_entitylist sites; shell_cliententitylist.h
// says which question each of them is for.
#include "slot_table.h"
#include "shell_cliententitylist.h"
#include "shell_surface_render.h"
#include "standalone_nettable.h"
#include "said_once_per_id.h"
#include "clientleafsystem.h"
#include "shell_effect_ledger.h"
#include "iclientshadowmgr.h"
#include "ivrenderview.h"
#include "bone_accessor.h"
#include "animation.h"
#include "cdll_bounded_cvars.h"
#include "datacache/imdlcache.h"
#include "iviewrender_beams.h"
#include "beam_flags.h"

#include "dlight.h"
#include "iefx.h"

#include "view.h"
#include "c_baseplayer.h"
#include "c_recipientfilter.h"

// SOUND_FROM_WORLD, for the animation events that outlive the entity that
// fired them.
#include "engine/IEngineSound.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "generic_payload.h"
#include "generic_nettable.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE5: IManagedStandaloneClient

// The lifted precache/tick/prediction/tracer statics. They used to be
// reached as CBaseEntity::Foo() through one-line forwarders in
// game/shared/baseentity_shared_forwarders.cpp; that quarantine is retired
// and the calls below name the free functions directly.
#include "entity_shared_statics.h"

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the calls below name GameSound_* directly.
#include "gamesound.h"
#include "clientleafsystem.h"
#include "shell_beam_render.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//=============================================================================//
// C_StandaloneEntity — the C_BaseEntity layer.
//=============================================================================//

//-----------------------------------------------------------------------------
// Every live entity on this hierarchy, so something can walk them once a frame.
//
// The client has no equivalent of the server's g_Standalones because until now
// it needed none: an entity was told everything it had to know by the engine,
// as it happened. Interpolation is the first thing that has to run on a FRAME
// rather than on a packet, and a frame is not addressed to any entity.
//
// cl_entitylist is not this list: it is indexed by entity number and holds
// every networkable, most of which are C_BaseEntity and interpolate themselves.
//-----------------------------------------------------------------------------
static CUtlVector< C_StandaloneEntity * > g_StandaloneClients;

//-----------------------------------------------------------------------------
// The one entity in the viewmodel pass — the LOCAL player's viewmodel. One
// per client by construction: managed code enables the pass exactly for the
// ss_viewmodel whose OwnerIndex is the local player, and there is one such.
// Held so the per-frame eye pin does not walk the list to find it.
//-----------------------------------------------------------------------------
static C_StandaloneEntity *s_pLocalViewModel = NULL;

//-----------------------------------------------------------------------------
// THE VIEWER'S OWN AVATAR (step 6f), which used to be C_StandalonePlayer's
// s_pLocalStandalonePlayer and is the same latch by the same rule: the engine
// names its own client's INDEX, and the entity wearing that index is the
// viewer -- the exact mirror of c_baseplayer.cpp:823.
//
// NULL is a legal and common answer: between maps, before the player entity has
// arrived, and on a client watching a demo. Every caller has always had to
// survive it -- C_BasePlayer::GetLocalPlayer answered NULL in the same moments.
//-----------------------------------------------------------------------------
static C_StandaloneEntity *s_pLocalPlayerShell = NULL;

C_StandaloneEntity *SourceSharp_LocalPlayerShell()
{
	return s_pLocalPlayerShell;
}

C_StandaloneEntity::C_StandaloneEntity( int nEntNum, int nSerialNum, ClientClass *pClass,
                                        int iTier )
	// THE BLOCK IS NOT IN THIS LIST, and that is the shape of step 1b's change
	// on this side. Sixteen members left the shell for it; their defaults are
	// written in the body below, under one comment that says why each one is
	// not simply a zero. A member initialiser cannot name a sub-object of a
	// member, so there is nowhere here for them to be even if we wanted them.
	: m_nEntIndex( nEntNum ),
	  m_bHasManaged( false ),
	  m_bDrawReported( false ),
	  m_ModelInstance( MODEL_INSTANCE_INVALID ),
	  m_hRenderHandle( INVALID_CLIENT_RENDER_HANDLE ),

	  // No shadow yet, and not dirty: there is nothing on the manager's list to
	  // be dirty ABOUT. CClientShadowMgr::CreateShadow opens with
	  // Assert( !pRenderable->IsShadowDirty() ), so starting this true would
	  // fire on the first prop in a _DEBUG build.
	  m_ShadowHandle( CLIENTSHADOW_INVALID_HANDLE ),
	  m_bShadowDirty( false ),

	  // The think list's invalid handle (its unsigned short InvalidIndex), as a
	  // value: client_thinklist.cpp left client.so in C8 (ClientC8Evicted).
	  m_hThink( (ClientThinkHandle_t)(uintp)0xFFFF ),

	  // Before anything below can be asked for it, and specifically before the
	  // entity joins the list at the end of this constructor.
	  m_pClientClass( pClass ),

	  // From the create function that made this entity, which is the only
	  // thing on this side that knows the tier — and it knows it before any
	  // prop has been decoded. Clamped rather than trusted: it indexes the tier
	  // table, and out of range there is a wild read rather than a wrong length.
	  m_iTier( ( iTier >= 0 && iTier < SourceSharp_TierCount() ) ? iTier : 0 ),
	  m_bPlayerShell( false ),
	  m_nPredictedHealth( -1 ),
	  m_bLocalPlayer( false ),
	  m_bPredictionDriving( false ),
	  m_pSurface( NULL ),
	  // Skin 0 and size 1: what every entity that never asks keeps, and what
	  // the whole hierarchy was hard-wired to before it could be asked. One
	  // rather than zero for the scale is not a default, it is the only value
	  // that means anything — see SetModelScale.
	  m_flModelScale( 1.0f ),
	  m_nOverlayModelIndex( -1 ),
	  m_bIgnoreZ( false ),
	  m_nPublishedModelIndex( -1 ),
	  m_nPublishedEffects( -1 ),
	  m_nAppliedPolicy( 0 ),
	  m_flSeqDuration( 0.0f ),
	  m_bSeqLooping( false ),

	  // Networked in. Zero until the first update, which is the honest answer:
	  // an entity that has not been told what it is has no type to pair with.
	  m_nTypeId( 0 ),
	  m_nSchemaHash( 0 ),
	  m_nPayloadWords( 0 ),
	  m_nResolvedTypeId( 0xFFFFFFFFu ),

	  // Never a real type id, so the first EnsureManagedHalf always asks.
	  // The same sentinel m_nResolvedTypeId uses, and for the same reason:
	  // zero is the id of an entity that has not been told what it is, and
	  // a latch initialised to it would answer "already tried" for exactly
	  // that entity.
	  m_nManagedTriedTypeId( 0xFFFFFFFFu )
{
	// THE BLOCK'S DEFAULTS, WRITTEN OUT (plan_entity.md §3.2, the client half of
	// step 1b).
	//
	// operator new below hands back ZEROED memory and sixteen values that used
	// to be member initialisers now live in this struct, so the ones whose
	// default is not a zero have to be said here or they are silently wrong.
	//
	// THE HANDLES ARE THE SHARP EDGE, and the argument is the server's word for
	// word. CBaseHandle's default constructor sets INVALID_EHANDLE_INDEX, which
	// is 0xFFFFFFFF; the zero the allocator leaves is index 0 with serial 0, and
	// INDEX 0 IS THE WORLD. Between this constructor's first instruction and the
	// Init() below, anything that read GetRefEHandle would be told this entity
	// is the world — and on this side that reader is the engine, which calls
	// GetClientUnknownFromHandle on a trace path that dereferences the answer
	// with no null test.
	EngineBlockHead_t &head = Block().Head;

	head.Handle       = INVALID_EHANDLE_INDEX;
	head.ParentHandle = INVALID_EHANDLE_INDEX;
	head.OwnerHandle  = INVALID_EHANDLE_INDEX;

	// The stand-in box every entity on this hierarchy wore before a model
	// arrived, and it is not a zero: a zero-sized OBB is a collideable the
	// partition sorts into nothing and a renderable culled by a point.
	head.Mins.Init( -16, -16, -16 );
	head.Maxs.Init(  16,  16,  16 );

	SetIdentityMatrix( head.WorldTransform );

	// The CLIENT TAIL — the render state this side computes and the engine
	// reads back per draw. Same rule: only the non-zeroes are written.
	EngineBlockClientTail_t &tail = Block().Client;

	// Opaque and unmodulated until something says otherwise. Four channels
	// rather than one because §3.2's tail carries a colour, and a zeroed colour
	// is an entity drawn black rather than an entity drawn plainly.
	tail.RenderColorR = 255;
	tail.RenderColorG = 255;
	tail.RenderColorB = 255;
	tail.RenderColorA = 255;

	// What ComputeFxBlend answers for an entity that has asked for nothing.
	tail.FxBlend = 255;

	// Real time, not stopped. A rate of zero is a sequence whose cycle never
	// advances, which draws as a model frozen on its first frame.
	tail.PlaybackRate = 1.0f;

	// SHADOW CHOICE, which is §3.2's own word for this field: the value a
	// managed class asked for (a ShadowCastChoice), NOT the ShadowType_t the
	// policy computes from it. The two are different facts — "I asked for auto"
	// and "it is casting nothing" is the pair that names a problem — and only
	// the first is state. Written even though AUTO is zero today, because which
	// integer AUTO is belongs to the managed enum and not to this allocator.
	tail.ShadowChoice = STANDALONE_SHADOW_CHOICE_AUTO;

	// NO MODEL, AND NO STUDIO HEADER READ YET — and neither of those is a zero
	// (plan_entity.md step 6b). Zero is mod_bad, which is a model that failed to
	// load; -1 is GetModel() == NULL, which is an entity that has none. Zero
	// sequences is a model that CANNOT MOVE and earns a baked shadow; -1 is a
	// studio header whose sequences have not been paged in, which earns none
	// until they have. A zeroed tail would tell the render policy that every
	// entity, before its first update, wears a broken model with no animation —
	// and it would answer for it.
	tail.ModelType           = -1;
	tail.StudioSequenceCount = -1;

	// NOTHING IS SET UP OVER THE POSE ANY MORE -- plan_entity.md step 6a. Two
	// CInterpolatedVars used to be pointed at Head.Origin and Head.Angles here,
	// each owning its field: filing the networked value into a history and
	// writing the drawn one back into the same place. Both histories are managed
	// rings now, keyed by this entity's handle, and the block is what they read
	// and write -- so there is nothing to bind on this side.
	BlockHandle().Init( nEntNum, nSerialNum );
	m_szClassname[0] = 0;

	// Bound — into §3.3's slot table, which is where the engine's
	// IClientEntityList now answers from, and (while the port seam lasts) into
	// the stock list as well. Creating the object through the ClientClass is
	// only half of it: the engine looks the entity up to unpack into it, so
	// without this it is constructed and then never given any of the server's
	// data. The symptom of missing it is that PostDataUpdate never runs — the
	// object exists, holds nothing, and nothing says why.
	//
	// ONE CALL because the two halves may not come apart; shell_cliententitylist.h
	// carries the argument, and `this` is passed as the concrete shell because
	// the table stores what it is given.
	SourceSharp_ClientEntityBind( this, nEntNum, nSerialNum );

	g_StandaloneClients.AddToTail( this );
}

C_StandaloneEntity::~C_StandaloneEntity()
{
	// The eye pin must not chase a freed entity.
	if ( s_pLocalViewModel == this )
		s_pLocalViewModel = NULL;

	// Nor must the view, the ears or the managed replay (step 6f). Same rule,
	// same reason: every caller of SourceSharp_LocalPlayerShell survives NULL
	// and none of them survives a dangling pointer.
	if ( s_pLocalPlayerShell == this )
		s_pLocalPlayerShell = NULL;

	delete m_pSurface;
	m_pSurface = NULL;

	// A body this entity was is managed's (SourceSharp.Client's ClientRagdolls),
	// taken apart when the managed half hears the removal.

	RemoveFromLeafSystem();

	// AND THE BEAMS ATTACHED TO THIS ENTITY, which nothing was reaping.
	//
	// C_BaseEntity::Term does this (c_baseentity.cpp:1103) and a shell is not a
	// C_BaseEntity, so beams aimed at a managed entity outlived it. For an
	// ordinary beam that is a few frames of a beam drawn to a stale point; for a
	// FBEAM_FOREVER one -- a beam created with a life of zero -- it is
	// permanent, because the per-frame retire loop skips FOREVER beams and
	// RecomputeBeamEndpoints answers false for a dead endpoint rather than
	// freeing it. The verdict (beam_reaper.h's, before the C2 beam port) is
	// BeamSystem.KillDeadBeams in C#; `beams` forwards the handle there.
	//
	// BY THE HANDLE, which is what Beam_t::entity[] holds (beamdraw.h:112), and
	// BEFORE SourceSharp_ClientEntityUnbind below: the reap only compares handle
	// values, so it would still be correct after, but "while the entity is still
	// itself" is the order every other teardown line here uses and the one a
	// reader can check.
	//
	// `beams` is the client's own global and is NULL between ShutdownBeams and
	// the next InitBeams; a shell freed in that window has nothing to reap.
	if ( beams )
	{
		beams->KillDeadBeams( GetRefEHandle() );
	}

	if ( m_ModelInstance != MODEL_INSTANCE_INVALID )
	{
		modelrender->DestroyInstance( m_ModelInstance );
		m_ModelInstance = MODEL_INSTANCE_INVALID;
	}

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	if ( m_bHasManaged && pStandalone )
		pStandalone->OnStandaloneRemoved( (uint32_t)BlockHandle().ToInt() );

	// Unbound, both halves, by the handle this shell still holds — the entry is
	// only nameable until the stock list's RemoveEntity invalidates it.
	SourceSharp_ClientEntityUnbind( this );

	g_StandaloneClients.FindAndFastRemove( this );
}

//-----------------------------------------------------------------------------
// The first update is where the classname arrives, and therefore the first
// moment the managed class can be chosen.
//
// PostDataUpdate, not OnDataChanged. The engine calls this one directly, right
// after unpacking; OnDataChanged is only called for entities that ASKED to be
// told, which C_BaseEntity does by registering a data-change event from its own
// PostDataUpdate. An entity that is not one never registers, so its
// OnDataChanged is never called — and nothing says so.
//
// Not the constructor either: the engine builds the object before it has any of
// the server's data, so at that point this entity does not yet know what it is.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE MANAGED HALF, MADE AS SOON AS THIS ENTITY KNOWS WHAT IT IS
// (plan_entity.md §3.6, step 5c).
//
// Idempotent, and called from two places: the type id's RECV PROXY, which fires
// the moment that prop decodes, and PostDataUpdate, which is the belt to the
// proxy's braces. Whichever runs first makes the entity; the other sees
// m_bHasManaged and returns.
//
// WHY A PROXY AND NOT SIMPLY THE TOP OF PostDataUpdate. §3.6 says the create
// function "creates the managed entity immediately rather than on first data
// update", and the gate for this step is that AN ENTITY EXISTS IN MANAGED CODE
// BEFORE ITS FIRST DATA UPDATE. PostDataUpdate is called AFTER RecvTable_Decode
// has written the whole update (create fn, PreDataUpdate, RecvTable_Decode,
// PostDataUpdate), so a
// create at the top of it is still a create after the first update landed.
// The proxy runs DURING the decode, on the type id, which is prop 5 of the eight
// shared props and therefore decoded BEFORE the payload words that carry this
// entity's own state.
//
// WHAT THIS SIDE CANNOT DO, SAID PLAINLY. §3.6's sentence continues "because
// the class descriptor now travels with the create call", and on the client it
// does not and cannot: CreateClientClassFn takes an entity number and a serial
// and nothing else (`CreateClientClassFn` in public/client_class.h), and there
// is ONE ClientClass PER TIER rather than per class -- which §3.6 itself
// keeps ("one per tier, as now"). So the create function knows the tier and
// cannot know the classname; a
// table per map classname would give it one, and §3.6 explicitly leaves that as
// an option for later. THE DIVERGENCE IS STATED RATHER THAN ACCIDENTAL: the
// earliest moment this side can know its class is the type id decoding, and
// this is that moment.
//
// IT IS ALSO NO LONGER A ONE-SHOT ON DATA_UPDATE_CREATED, and that was a real
// hole rather than a tidy-up. The create used to sit inside
// `if ( updateType == DATA_UPDATE_CREATED )`, so an entity whose first update
// carried a type id this client had not published yet -- the ordinary case for
// a class published late, and the case the "not published on this client"
// message below is about -- never got a managed half afterwards, however many
// updates arrived with the type known. Retrying is what makes the message a
// diagnostic rather than a verdict.
//-----------------------------------------------------------------------------
bool C_StandaloneEntity::EnsureManagedHalf()
{
	// WHAT THE ENTITY IS, resolved from four bytes instead of read from
	// sixty-four (Part 6). m_szClassname is no longer networked; the type id
	// is, and the published type table is what turns one into the other.
	//
	// Every update rather than only the first, because the id is what arrived
	// and this is the one place it is turned into anything — an update that
	// carried a changed id with a stale name would pair the entity with the
	// wrong managed class, which is the failure the hash exists to prevent one
	// level up.
	if ( m_szClassname[0] == 0 || m_nTypeId != m_nResolvedTypeId )
	{
		const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ById( m_nTypeId );

		m_nResolvedTypeId = m_nTypeId;
		Q_strncpy( m_szClassname, pType ? pType->szClass : "", sizeof( m_szClassname ) );

		if ( !pType && m_nTypeId != 0 )
		{
			// A type the server has and this build does not. Not an error, and
			// usually not even a defect: a server-only entity class (a prop, an
			// item, a trigger — anything whose logic lives entirely on the
			// server) is the port's normal shape, its shell still draws by
			// model index, and it has no managed half here BY DESIGN. Said once
			// per id — a SET of ids, not a last-id latch: two unpublished
			// entities alternating updates defeated the latch and each warned
			// every frame. See said_once_per_id.h.
			static CSaidOncePerId s_SaidUnknown;

			if ( s_SaidUnknown.FirstTime( m_nTypeId ) )
			{
				Msg( "[SourceSharp] standalone %d: type id 0x%08x is not published on this "
				     "client; it has no managed half here (fine for a server-only class; "
				     "said once per type)\n", m_nEntIndex, m_nTypeId );
			}
		}
	}

	// LATCHED ON THE TYPE ID, AND THAT IS A CROSSING COUNT RATHER THAN
	// TIDINESS.
	//
	// Most entities on this hierarchy have NO managed half by design -- a prop,
	// an item, a trigger, anything whose logic lives entirely on the server --
	// and for those OnStandaloneCreate answers 0 every time it is asked. Retried
	// on every update, that is one crossing per such entity per update forever,
	// which on dm_lockdown is most of 473 entities. The retry above is what
	// closes the "type published late" hole; this is what stops it from being
	// paid for by the entities the hole was never about.
	//
	// KEYED ON THE TYPE ID, not on a bare bool, so the retry the hole needs
	// survives: an entity whose id CHANGES is a different class and is asked
	// again. An entity whose id has not changed has already been offered to
	// managed code under exactly the name it would be offered under now.
	//
	// The classname-empty case never reaches here at all -- it returns above --
	// and it is the one that must keep retrying, because the resolve at the top
	// of this function re-runs for as long as the name is empty, which is how a
	// type published after the entity arrived is eventually paired.
	if ( m_bHasManaged || m_szClassname[0] == 0 || m_nTypeId == m_nManagedTriedTypeId )
		return m_bHasManaged;

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	// NOT LATCHED, deliberately: there is no managed runtime yet, so this is not
	// an answer about this entity. Latching here would leave every entity made
	// before the runtime attached permanently without a managed half.
	if ( !pStandalone )
		return false;

	m_nManagedTriedTypeId = m_nTypeId;

	m_bHasManaged = pStandalone->OnStandaloneCreate( (uint32_t)BlockHandle().ToInt(),
	                                                 m_szClassname ) != 0;

	return m_bHasManaged;
}

void C_StandaloneEntity::PostDataUpdate( DataUpdateType_t updateType )
{
	// WHEN this update is from, into the block, before anything reads the pose
	// as a position. The managed interpolation pass keys its history off it --
	// see NoteNetworkedPosition -- and it runs one stage later, at
	// FRAME_NET_UPDATE_POSTDATAUPDATE_END.
	NoteNetworkedPosition();

	// Every update, not just the first: the origin is networked and the leaf
	// system indexes by it. An entity that moved and did not say so is drawn
	// where it used to be, or culled against a leaf it has left.
	OnPositionChanged();

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	// FIRST, and it may already have happened: the type id's recv proxy calls
	// this during the decode, which is before this function runs at all. What
	// is left here is the belt to that proxy's braces -- an update in which the
	// type id did not change carries no type id prop, so the proxy does not
	// fire, and an entity that could not be paired on its first update is paired
	// on the one after the type is published.
	const bool bHadManaged = m_bHasManaged;

	EnsureManagedHalf();

	const bool bJustMade = m_bHasManaged && !bHadManaged;

	if ( updateType == DATA_UPDATE_CREATED )
	{
		// THE LOCAL-PLAYER LATCH (step 6f). The engine names its own client's
		// index; the entity wearing it is the viewer. Held here rather than in
		// a player subclass because there is no player subclass any more -- the
		// player's state is the managed player's, and what is left on this side
		// is the shell every entity has plus this one bit.
		if ( m_bPlayerShell && engine->GetLocalPlayer() == entindex() )
		{
			m_bLocalPlayer = true;
			s_pLocalPlayerShell = this;

			Msg( "[SourceSharp] standalone player: local player is entity %d\n", entindex() );
		}

		// Into the renderer's list, once the model index has arrived — which is
		// the same update, and the reason this is not in the constructor.
		AddToLeafSystem();

		// A class that needs more than a brush model drawn gets the object that
		// knows how. Only one does, and it is asked for by classname because a
		// renderer written in C++ has to know which class it is for.
		if ( !m_pSurface && m_szClassname[0] &&
		     !Q_stricmp( m_szClassname, SOURCESHARP_SURFACE_CLASSNAME ) )
		{
			m_pSurface = new CSourceSharpSurfaceRenderer;

			// Said once per surface. Whether the renderer was ever attached is
			// the first question when glass draws as a plain brush, and it
			// cannot be answered from outside the game.
			Msg( "[SourceSharp] standalone(client): '%s' (%d) has a surface renderer\n",
				m_szClassname, m_nEntIndex );
		}
	}

	// Before the spawn callback, because the class's OnSpawn is managed code and
	// may ask whether it is dormant.
	//
	// ON A CREATE, OR ON THE UPDATE THAT FINALLY MADE THE MANAGED HALF. The
	// second arm is new with the retry above: an entity paired three updates
	// late would otherwise be handed a spawn it never got a dormancy for.
	if ( updateType == DATA_UPDATE_CREATED || bJustMade )
		PushDormancy();

	// SPAWN FIRES WHEN THE HALF IS MADE, not when the shell is created, and for
	// the overwhelmingly common entity those are the same update. Keyed off
	// bJustMade rather than m_bHasManaged so it stays exactly once per managed
	// half — m_bHasManaged is true on every subsequent update too.
	if ( bJustMade && pStandalone )
		pStandalone->OnStandaloneSpawn( (uint32_t)BlockHandle().ToInt() );

	// THE GATE (Part 4, Part 14 Step 6), asked ONCE and answered for both
	// readers of this blob.
	//
	// Once, because SourceSharpSchema_Accept COUNTS refused updates and warns on
	// the first of each type: asking it twice for one update would report one
	// disagreement as two and make the tally mean nothing.
	//
	// Nothing reads the blob through a layout this build does not recognise —
	// see SourceSharpSchema_Accept for why a silent decode through the wrong
	// offsets is worse than no decode at all.
	const bool bSchemaOk = BlobBytes() > 0
	                    && SourceSharpSchema_Accept( m_nTypeId, m_nSchemaHash, m_nEntIndex, NULL );

	// Every update: which panes are left is the whole of what changes about a
	// pane of glass, and it changes one shot at a time. The managed half decides
	// the cracks from the same bits and pushes them back through
	// SourceSharp_SurfaceSetEdges.
	//
	// BEHIND THE GATE SINCE STEP 8, and it was outside it until then. Step 7's
	// live test found the renderer reading and drawing a refused entity through
	// offsets the fingerprint had just called wrong, and the trade recorded at
	// the time — glass drawn as an uncracked brush rather than a wrongly-cracked
	// one — undersold it. The renderer does not merely display these bytes: it
	// takes Wide and High as LOOP BOUNDS. They are unsigned chars read straight
	// out of the blob, so a mismatched layout puts an arbitrary 0..255 in each,
	// and the pane-bit lookup indexes a 32-byte stack array with
	// (w + h*16) >> 5 — up to 134. Refusing is not cosmetic here; it is the
	// difference between drawing nothing and reading half a kilobyte off the
	// end of a stack buffer.
	//
	// An unbroken brush is also the honest picture of "this build cannot read
	// this entity", and it is exactly what an entity with no renderer draws.
	if ( m_pSurface && bSchemaOk )
		m_pSurface->Update( this, updateType == DATA_UPDATE_CREATED );

	// AFTER the create, so a class sees its state at the same moment it first
	// hears about itself rather than one update later — and on EVERY update,
	// because a value that changes is the whole reason a class declares one.
	//
	// Only for classes with a managed half and only when there is state to
	// hand over: the blob is one size for every entity on the hierarchy, so a
	// class that declared nothing would otherwise be handed its neighbours'
	// bytes to make sense of.
	//
	// Refusing rather than warning, because the managed half would otherwise
	// have already read the bytes by the time anything said they were the wrong
	// bytes.
	if ( m_bHasManaged && pStandalone && bSchemaOk )
	{
		// THIS entity's length (Part 18). C# decodes the blob against the
		// layout of the class it paired with, and a length from another
		// class's tier would either hide its tail or hand it bytes past the
		// end of the allocation.
		pStandalone->OnStandaloneDataChanged( (uint32_t)BlockHandle().ToInt(), DataBlob(),
		                                      BlobBytes(), (int32_t)updateType );
	}

	// LAST, and it used to be RefreshShadow() for the reason stock's
	// OnDataChanged opens with CreateShadow(): "do it here so that objects can
	// change shadowcasting state". The reason survives and the call does not.
	//
	// WHAT CHANGED IS WHO DECIDES. The model index is networked and the
	// engine's decoder writes it straight into the block, so nothing on this
	// side runs to notice it moved -- an entity created with no model and given
	// one three updates later has nothing to cast a shadow with when it is
	// created and everything to cast one with afterwards. This is where that is
	// noticed, and what it does is publish the facts and bump the serial; the
	// managed pass at the end of this network update decides, and the apply
	// sweep at PreRender hands the answer to the shadow manager.
	//
	// GUARDED BY THE INDEX, not called unconditionally, because the studio
	// header comes out of mdlcache and an entity that is networked every tick
	// would otherwise pay for that read every tick to learn nothing.
	if ( Block().Head.ModelIndex != m_nPublishedModelIndex )
		PublishModelFacts();

	// AND THE EFFECTS WORD, on exactly the same argument (step 8e-render).
	//
	// EF_NODRAW is networked into Block().Head.Effects by the engine's decoder
	// and nothing on this side runs when it lands. ClientRenderPolicy.Drawn
	// reads it, and the managed pass only looks at entities whose input serial
	// has moved -- so a func_dust told to hide, which never moves and whose
	// model index never changes again, would carry the bit in its block and go
	// on being drawn forever. This is the ONE place that notices, and it is a
	// comparison rather than an unconditional bump because a player is networked
	// every tick and would otherwise re-decide its whole render policy every
	// tick to learn nothing.
	if ( Block().Head.Effects != m_nPublishedEffects )
	{
		m_nPublishedEffects = Block().Head.Effects;
		BumpPolicyInputs();
	}
}

//-----------------------------------------------------------------------------
// Left or re-entered the client's PVS.
//
// A dormant entity must not be drawn, and the leaf system is what would draw
// it — so it comes out of that list rather than being asked to decline every
// frame. C_BaseEntity does the same thing through SetDormant.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::NotifyShouldTransmit( ShouldTransmitState_t state )
{
	switch ( state )
	{
	case SHOULDTRANSMIT_END:
		SetDormant( true );
		break;

	case SHOULDTRANSMIT_START:
		SetDormant( false );
		break;

	default:
		break;
	}
}

//-----------------------------------------------------------------------------
// Into the block, and out to the managed side.
//
// THE LEAF-SYSTEM CALLS THAT USED TO BE THE OTHER HALF OF THIS ARE GONE
// (plan_entity.md step 6b). Dormancy is a policy INPUT: managed code decides
// from it whether the entity draws, writes the answer into the block, and the
// apply sweep is what moves the renderable in or out. Calling
// RemoveFromLeafSystem here as well would be the shell acting on an input
// before the decision that input feeds had been made -- which is the shape §3.2
// exists to remove, and which would have taken a dormant entity out of the leaf
// system with its render group still saying it was in.
//
// THE FRAME IS NOT LOST. NotifyShouldTransmit runs during the network update;
// the managed pass runs at FRAME_NET_UPDATE_POSTDATAUPDATE_END, which is the
// end of that same update; the sweep runs at PreRender, before anything draws.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::SetDormant( bool bDormant )
{
	if ( ( Block().Client.Dormant != 0 ) == bDormant )
		return;

	Block().Client.Dormant = bDormant ? 1 : 0;
	BumpPolicyInputs();

	PushDormancy();
}

//-----------------------------------------------------------------------------
// Tell the managed side what the block's dormancy now says.
//
// plan_crossings.md Phase X3. The engine owns this flag -- NotifyShouldTransmit
// is an IClientNetworkable method only the engine calls, so no port ever moves
// the writer into managed code -- and managed read it back fifty-one times a
// frame (A11). The write is rarer than the frame by three orders of magnitude,
// so the write pushes and the reads become a dictionary lookup.
//
// CALLED FROM THE WRITE, not from a frame. Its staleness is therefore zero
// frames by construction and the only way the mirror can be wrong is a write
// site that does not call this -- which is a grep, and is gated by
// cs_cl_mirror_check comparing every live entity's mirror against the slot.
//
// THE THREE CALL SITES ARE THE THREE WRITE SITES: the two arms above, and the
// creation push, which carries whatever the flag says at that moment rather
// than the constructor's false. An entity can be told it left the PVS before
// its first data update has named a classname, and therefore before the
// managed half of it exists at all.
//
// m_bHasManaged, exactly like OnStandaloneRemoved: only an entity that will get
// a removal may enter the mirror, because handles are recycled and an entry
// nothing clears would be inherited by the next entity to wear the number.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::PushDormancy( void )
{
	if ( !m_bHasManaged )
		return;

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	if ( pStandalone )
		pStandalone->OnStandaloneDormancyChanged( (uint32_t)BlockHandle().ToInt(),
		                                          Block().Client.Dormant );
}

//-----------------------------------------------------------------------------
// The transform, and telling the renderer the entity has moved.
//
// RenderableToWorldTransform is what modelrender and the leaf system read; the
// origin on its own is only where it came from.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// How far behind the server this hierarchy draws.
//
// The same answer C_BaseEntity gives for a networked, unpredicted entity in
// multiplayer: the client's own interp amount, plus a tick so that a value
// arriving a tick late still has something to blend towards. Nothing here is
// ever predicted or client-created, so those branches of stock's version have
// no case to answer.
//-----------------------------------------------------------------------------
static float StandaloneInterpolationAmount()
{
	// Qualified, because it is a static on C_BaseEntity and this hierarchy is
	// not one — it is asking the SDK a question about the tick rate, not
	// inheriting anything.
	const int nServerTickMultiple = EntitySim_IsSimulatingOnAlternateTicks() ? 2 : 1;

	return TICKS_TO_TIME( TIME_TO_TICKS( GetClientInterpAmount() ) + nServerTickMultiple );
}

//-----------------------------------------------------------------------------
// WHEN the value that just arrived is from.
//
// Stamped with the server time of the packet it came in, NOT with curtime.
// curtime is when the CLIENT got round to looking, which drifts against the
// tick the server actually moved the entity on — and interpolating between two
// samples whose timestamps are wrong is a smooth curve through the wrong
// places. engine->GetLastTimeStamp() is the packet's own server time, and an
// entity that did not change in that packet is not given one, because
// PostDataUpdate is not called for it.
//
// This is the one thing a C_BaseEntity does differently: it networks
// m_flSimulationTime per entity and uses that. Standalone entities do not send
// one, and the packet time is the same answer for every entity that changed in
// it — which is all of them, here.
//
// ONE STORE, AND IT IS THE WHOLE FUNCTION (plan_entity.md step 6a). It used to
// file the pose into two CInterpolatedVar histories as well. The histories are
// managed rings now and they read this value: an entity whose SimulationTime
// has moved since the last pass is exactly the set PostDataUpdate was called
// for, so the stamp IS the "there is a new sample here" signal and no second
// one is needed.
//
// The teleport argument went with them. It was `updateType ==
// DATA_UPDATE_CREATED`, and the managed side reaches the same conclusion from
// the same fact by a route that cannot go stale: an entity being created is a
// HANDLE the ring table has never seen in that slot, and a handle carries a
// serial number precisely so a recycled slot is not mistaken for the entity
// that used to be in it.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::NoteNetworkedPosition()
{
	// WHEN THE SERVER SAYS THIS POSE IS FROM, into the client tail. §3.2 lists
	// it among the animation inputs the bone code reads, and it is the value
	// the comment above this function spends a paragraph explaining: the
	// packet's own server time, because standalone entities network no
	// per-entity simulation time of their own.
	Block().Client.SimulationTime = engine->GetLastTimeStamp();
}

//-----------------------------------------------------------------------------
// What has to happen once a frame's interpolation has moved this entity.
//
// THE ARITHMETIC IS NOT HERE ANY MORE (plan_entity.md step 6a). The managed
// pass has already written Head.Origin, Head.Angles and the client tail's
// render pose by the time this runs — it runs at FRAME_RENDER_START, which is
// where stock interpolates its own entities too. What is left is the two things
// that are not decisions: rebuilding the transform the renderer and the
// partition read, and telling the leaf system the renderable moved. Both are
// engine calls, so both stay where the engine is.
//
// CALLED FOR EVERY ENTITY THE PASS WRITES, ONCE A FRAME, which is what the old
// Interpolate was called for and in the same place.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::PostInterpolate( float flCurrentTime )
{
	// THIS FRAME'S CYCLE, into the client tail. §3.2 puts the animation inputs
	// the bone code reads in the tail, and the cycle is the one of them that is
	// a function of the clock rather than of a value somebody set: it is
	// computed from AnimTime, PlaybackRate and the sequence's own duration, all
	// of which are already here.
	//
	// STILL NATIVE, AND NAMED AS DEBT RATHER THAN DEFENDED. It is animation
	// state, which is step 6d's, and moving it here would have made this step
	// two ports instead of one. Once per entity per frame, in the one place
	// that already runs once per entity per frame.
	Block().Client.Cycle = SequenceCycle( flCurrentTime );

	OnPositionChanged();
}

//-----------------------------------------------------------------------------
// THE DRAWN POSE, PUBLISHED INTO THE CLIENT TAIL (plan_entity.md §3.2).
//
// The head carries where the entity is according to the last update; the tail
// carries where it is being DRAWN this frame. Keeping them apart is what lets
// GetRenderOrigin and GetCollisionOrigin both be one load without either one
// lying, and it is the whole reason §3.2 gives the tail a pose of its own
// rather than pointing the renderer at the head.
//
// THE TAIL HAS A TRANSFORM OF ITS OWN AS OF STEP 6b, and that is the split 6a
// handed over. RenderableToWorldTransform used to return Head.WorldTransform,
// the same matrix CollisionToWorldTransform returns -- so the renderer and the
// partition read one transform between them and a renderable could not be DRAWN
// anywhere but where it COLLIDED. MoveToEye is the case that proves it: pinning
// the viewmodel to the eye had to move Head.Origin, which moved the entity's
// collision origin to the eye as well, every frame, for an entity that is not
// where it is drawn by design.
//
// What did NOT change is interpolation writing both. The interpolated pose IS
// the entity's position on the client -- stock interpolates m_vecOrigin itself,
// and C_BaseEntity's GetRenderOrigin and CollisionProp's GetCollisionOrigin
// both answer GetAbsOrigin -- so a head left on the last snapshot would be a
// divergence from stock rather than a tidier split. The tail's own transform is
// what a renderable drawn ELSEWHERE uses, and today that is the viewmodel.
//
// A FUNCTION RATHER THAN TWO LINES IN OnPositionChanged, AND THAT IS NOT
// TIDINESS. There are two paths that move an entity, not one: OnPositionChanged
// is the ordinary one, and MoveToEye is the viewmodel's, which deliberately
// does NOT go through it because a viewmodel-pass renderable is collated from
// its own list and re-leafing it is work with no effect. Written inline in
// OnPositionChanged alone, the viewmodel's render pose would never be published
// and the local player's weapon would draw at wherever the server last said the
// entity was -- which for a viewmodel pinned to the eyes is a weapon left
// behind in the world.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::PublishRenderPose()
{
	// A BODY THE SOLVER OWNS PUBLISHES NOTHING, and this guard is 6e's, added
	// with the branch it took out of GetRenderOrigin.
	//
	// THE TWO ARE ONE CHANGE. GetRenderOrigin used to ask the solver directly
	// (`m_pRagdoll ? RagdollOrigin() : ...`), so it did not matter that this
	// function overwrote the tail with the NETWORKED origin a moment later --
	// nothing read the tail for a ragdoll. Now the physics step writes the
	// solver's pose into the tail and the render path reads it, so the same
	// overwrite would put a corpse back where the server last said it was on
	// every position update: a body that falls over and then snaps upright,
	// once per update, for as long as the entity is transmitted.
	//
	// Left out of the guard deliberately: the LEAF LINK below. Which leaf a
	// renderable is in was not updated per step for a ragdoll before 6e either
	// -- the generous cull cube is what covers a body that walks out of its
	// leaf -- and adding a per-body per-step re-link here would be a new cost
	// with no observed defect behind it. Recorded rather than taken.
	if ( IsRagdoll() )
		return;

	Block().Client.RenderOrigin = Block().Head.Origin;
	Block().Client.RenderAngles = Block().Head.Angles;

	// The RENDER transform, from the pose just published. A COPY of the head's
	// rather than a second AngleMatrix, because the two poses are equal by the
	// three lines above and building the same matrix twice per entity per frame
	// is the arithmetic this split was careful not to add. MoveToEye is the one
	// caller for which they are NOT equal, and it builds its own.
	Block().Client.RenderTransform = Block().Head.WorldTransform;
}

void C_StandaloneEntity::OnPositionChanged()
{
	// DID THE POSE ACTUALLY CHANGE? The shadow work below needs the answer and
	// nothing else here does, so it is taken from state that already exists
	// rather than from a new member: Head.WorldTransform is written in exactly
	// ONE place on this side -- the next line -- so it is a faithful record of
	// the pose this function last saw, and it carries the angles as well as the
	// origin.
	//
	// THIS FUNCTION IS NOT CALLED ON A MOVE. It is called once per entity per
	// FRAME, from PostInterpolate, whether or not anything moved; the comment
	// there says so. Stock's equivalent (CBaseEntity::InvalidatePhysicsRecursive)
	// is called on an actual invalidation, so telling the shadow manager
	// unconditionally here is NOT what stock does -- it is a hundred entities
	// dirtying their shadows every frame. Measured, on dm_lockdown, over the
	// same fifteen-second probe with the same twenty samples:
	//
	//     unconditional   texturesDrawn 174562   resolve asks 3043338
	//     this guard      see the numbers in .claude/entity-port/status/7d.md
	//
	// An exact comparison, which is stock's own idiom for this question --
	// CClientShadowMgr::UpdateShadow decides whether to re-project with
	// `origin != shadow.m_LastOrigin`, not with a tolerance.
	matrix3x4_t matPrevPose;
	MatrixCopy( Block().Head.WorldTransform, matPrevPose );

	AngleMatrix( Block().Head.Angles, Block().Head.Origin, Block().Head.WorldTransform );

	const bool bPoseMoved =
		( memcmp( &matPrevPose, &Block().Head.WorldTransform, sizeof( matrix3x4_t ) ) != 0 );

	PublishRenderPose();

	ShellLeaf_Moved( ClientLeafSystem(), this );

	// AND THE SHADOW MANAGER, WHICH NOTHING WAS TELLING. plan_entity.md step 7d.
	//
	// A SILENT LOSS OF EXACTLY THE 5e SHAPE: something the BASE did FOR the
	// entity rather than TO it, which no compiler, no `make check` and no live
	// gate could see. On the client `CBaseEntity::InvalidatePhysicsRecursive`
	// ends its position arm with three calls (baseentity_shared.cpp:1506):
	//
	//     MarkRenderHandleDirty();
	//     g_pClientShadowMgr->AddToDirtyShadowList( this );
	//     g_pClientShadowMgr->MarkRenderToTextureShadowDirty( GetShadowHandle() );
	//
	// and `CCollisionProperty::MarkSurroundingBoundsDirty` makes the first two
	// again for a rotation that invalidates the surrounding box
	// (collisionproperty.cpp:1531). A C_StandaloneEntity is not a CBaseEntity,
	// so NONE of them ran for a managed entity. The first was already covered:
	// ShellLeaf_Moved above is RenderableChanged, which is what
	// MarkRenderHandleDirty exists to cause. THE OTHER TWO WERE NOT.
	//
	// WHAT THAT COST, MEASURED rather than argued. CClientShadowMgr only
	// re-projects shadows on its DIRTY LIST (UpdateDirtyShadows), and the only
	// thing that ever put a managed entity's shadow on it was CreateShadow's own
	// one-shot `AddToDirtyShadowList( shadowHandle, true )`. So every managed
	// entity's shadow was projected ONCE, at the position it was created at, and
	// never again. A crate spawned with ent_create and sampled twenty times over
	// ten seconds by cs_cl_shadow_report:
	//
	//     projectedFrom (-3843.0 3263.4 63.2)   -- frozen, all twenty samples
	//     renderOrigin  (-3835.3 3237.4 22.4)   -- where the crate actually is
	//
	// Forty-one units of z and twenty-six of xy between a prop and its own
	// shadow, for as long as the map is loaded. Nothing warns, nothing asserts,
	// and the frame still renders -- which is why this needed a number to find.
	//
	// Both calls self-guard (AddToDirtyShadowList returns for an already-dirty
	// renderable and for CLIENTSHADOW_INVALID_HANDLE; MarkRenderToTextureShadowDirty
	// refuses bogus handles), so this is stock's own shape and not a guarded
	// copy of it.
	// THE TWO CALLS ARE NOT GUARDED THE SAME WAY, and the asymmetry is the
	// whole of the measurement above.
	//
	// AddToDirtyShadowList is UNCONDITIONAL, because the manager does the real
	// comparison itself: UpdateShadow re-projects only when
	// `pRenderable->GetRenderOrigin() != shadow.m_LastOrigin`, so a shadow put
	// on the dirty list for an entity that did not move costs one resolve and a
	// vector compare and then stops. That matters for A RAGDOLL, whose render
	// pose is written by the PHYSICS STEP and not by this function --
	// PublishRenderPose returns early for one -- so bPoseMoved is FALSE for a
	// body that is visibly falling over. Guarding this call on bPoseMoved would
	// have frozen exactly the shadows most obviously in motion, which is the
	// bug this whole block exists to fix, reintroduced for one class.
	//
	// MarkRenderToTextureShadowDirty IS guarded, because it does not compare
	// anything: it sets SHADOW_FLAGS_TEXTURE_DIRTY, and DrawRenderToTextureShadow
	// then re-renders the shadow's texture whether or not the projection
	// changed. Unguarded that is every shadow's texture re-rendered every frame.
	//
	// An ANIMATING source is unaffected by the guard, and not because the
	// re-projection refreshes the texture -- it does not. Grepped rather than
	// assumed: SHADOW_FLAGS_TEXTURE_DIRTY is set in exactly three places
	// (clientshadowmgr.cpp:1604 RTT init, :1915 CreateShadow, :3140
	// MarkRenderToTextureShadowDirty) and BuildRenderToTextureShadow is not one
	// of them. What covers an animating model is that the flag is never CLEARED
	// for one: DrawRenderToTextureShadow's :3902 clears it only
	// `if ( (shadow.m_Flags & SHADOW_FLAGS_ANIMATING_SOURCE) == 0 )`. So an
	// animating entity -- a ragdoll included -- is permanently texture-dirty by
	// stock's own design and is redrawn every frame regardless of this call.
	if ( g_pClientShadowMgr )
	{
		g_pClientShadowMgr->AddToDirtyShadowList( this );

		if ( bPoseMoved )
			g_pClientShadowMgr->MarkRenderToTextureShadowDirty( GetShadowHandle() );
	}
}

void C_StandaloneEntity::AddToLeafSystem()
{
	// THE GROUP IS A LOAD (plan_entity.md step 6b, §3.2).
	//
	// It used to be CHOSEN here, by a RenderGroup() that read three native
	// members and returned one of three RenderGroup_t values. That was a
	// decision about what the game draws and where, so it is C#'s: managed code
	// writes Client.RenderGroup and this reads it. The value's meaning has not
	// changed -- it is still the record of what the leaf system was actually
	// told, because the leaf system takes the group at insertion and never
	// re-asks.
	//
	// The COMPILE_TIME_ASSERTs are the pin between ClientRenderGroup in
	// SourceSharp.Abi and the engine's enum. This is the one place both are in
	// scope, which is exactly the argument shadow_cast_policy.h used to make
	// about ShadowType_t before its rule moved to C# too.
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_OPAQUE_ENTITY      == 7 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_TRANSLUCENT_ENTITY == 8 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_VIEW_MODEL_OPAQUE  == 10 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_OPAQUE_BRUSH       == 12 );

	const RenderGroup_t group = (RenderGroup_t)Block().Client.RenderGroup;

	if ( IsInLeafSystem() )
	{
		// The group is re-set rather than assumed: a model arriving after the
		// entity did can change what kind of thing this is to draw.
		ClientLeafSystem()->SetRenderGroup( RenderHandle(), group );
		ClientLeafSystem()->RenderableChanged( RenderHandle() );
	}
	else
	{
		ShellLeaf_Add( ClientLeafSystem(), this, group );
	}

	// And into the shadow manager's list, if it is the sort of thing that
	// casts. BOTH arms, which is why the early return above became an else:
	// every caller of this is a place where what the entity IS has just
	// changed — a model arrived, it came back into the PVS, it became a
	// viewmodel — and those are exactly the changes the policy reads.
	// The re-add arm is the one a model arriving late takes, and it is the one
	// a prop placed by cs_sv_propshot goes through.
	RefreshShadow();
}

//-----------------------------------------------------------------------------
// In or out, to match ShouldDraw. See the header for why this had to exist.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::UpdateVisibility()
{
	const bool bWanted = ShouldDraw();
	const bool bPresent = RenderHandle() != INVALID_CLIENT_RENDER_HANDLE;

	if ( bWanted == bPresent )
		return;

	if ( bWanted )
		AddToLeafSystem();
	else
		RemoveFromLeafSystem();
}

void C_StandaloneEntity::RemoveFromLeafSystem()
{
	// The shadow goes first, exactly as C_BaseEntity::RemoveFromLeafSystem does
	// it — and ABOVE the early return, which is the one difference. A shadow
	// left behind is projected from an entity the renderer has stopped drawing:
	// a dark patch on the floor under nothing, which is what a dormant prop
	// would leave every time it left the PVS. Stock can afford the return
	// because its two handles are created together; here the destructor is the
	// last chance to give the manager its handle back, and a renderable that
	// never made it into the leaf system would take that chance away.
	DestroyShadow();

	if ( !IsInLeafSystem() )
		return;

	ShellLeaf_Remove( ClientLeafSystem(), this );
	RenderHandle() = INVALID_CLIENT_RENDER_HANDLE;
}

//-----------------------------------------------------------------------------
// The model, resolved from the index the server sent.
//
// modelinfo answers for an index the client precached during level load, which
// is the same list the server built — so the index means the same thing on both
// sides. That agreement is the whole reason a model can be networked as four
// bytes rather than a path.
//-----------------------------------------------------------------------------
const model_t *C_StandaloneEntity::GetModel( void ) const
{
	return Block().Head.ModelIndex > 0 ? modelinfo->GetModel( Block().Head.ModelIndex ) : NULL;
}

//-----------------------------------------------------------------------------
// game_entity_relays.h's CLIENT half STOOD HERE: GameEntity_ModelIndex,
// GameEntity_RefHandle and GameEntity_EntIndex, each a cast to this class and
// one member read. Their last client callers were fork ragdoll.cpp and fork
// physics.cpp, and both left client.so; nm over the kept client objects found
// no importer (2026-10-18). The server's half (standalone_relays.cpp) stays.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// standalone_relays_client.h's THREE DEFINITIONS ARE GONE, and so is the header.
//
// ClientGameEntity_Model, ClientGameEntity_IsDormant and
// ClientGameEntity_RenderAngles each took an IGameEntity * and static_cast it to
// C_StandaloneEntity *, which is sound only while this class is the only linked
// implementor of that interface. Every one of their six call sites wanted a fact
// that an ENGINE interface already answers on both hierarchies, so each site now
// asks it there and the crossing does not happen at all:
//
//   c_te_legacytempents.cpp  FizzEffect's parameter is an IClientRenderable, and
//                            GetModel / GetRenderAngles are its own pure virtuals
//   c_rope.cpp               the entity list resolves the endpoint HANDLE to
//                            IClientNetworkable, whose IsDormant this was
//   ragdoll.cpp              the model comes from GameEntity_ModelIndex, which
//                            the same function's caller already reads
//
// The SHARED relays in game_entity_relays.h are the server's alone now: their
// client half went with its last callers (2026-10-18). The shell still declares
// IGameEntity, and with it the six inert slots, because the client's native
// code uses IGameEntity as its entity type (the effect data, the impact
// effects, the entity list's SDK lookups, GetGameEntity); see
// docs/client-shell.md and docs/todo.md.
//-----------------------------------------------------------------------------

bool C_StandaloneEntity::ShouldDraw( void )
{
	// A LOAD AND ONE LIVE FACT (plan_entity.md step 6b, §3.2).
	//
	// The three rules that used to be here -- the viewmodel's, the
	// self-drawing entity's and "having a model IS the question" -- are
	// ClientRenderPolicy.Drawn's, in C#, decided when their inputs change and
	// written into Client.Drawn. This is a getter the renderer asks per draw,
	// so it cannot cross and it cannot be native policy either; it is answered
	// from memory managed code wrote.
	//
	// THE SECOND TERM IS THE ONE THAT COULD NOT BE PRECOMPUTED, and it is worth
	// being precise about why it is not a rule left behind here. WHICH entities
	// are hidden while the camera can see their owner is the decision, and that
	// is Client.HideWhenOwnerSeen -- managed code's, and today the viewmodel's
	// alone. WHETHER the camera can see its owner right now is an engine fact
	// solved during view setup, after every stage a managed pass can run on and
	// immediately before CViewRender asks this question of the viewmodel list.
	// A Client.Drawn that had folded it in would be one frame stale: a pair of
	// arms on screen for a frame after every `thirdperson`.
	const EngineBlockClientTail_t &tail = Block().Client;

	if ( tail.Drawn == 0 )
		return false;
	//-------------------------------------------------------------------------
	// THE VIEWER'S OWN BODY, which is the one model a client must NOT draw most
	// of the time.
	//
	// Stock's C_BasePlayer::ShouldDraw opens with the same refusal
	// (`!IsLocalPlayer() || input->CAM_IsThirdPerson()`) and this build never
	// reaches it. EVERY OTHER PLAYER falls through and draws by whether it has
	// a model, which is why the bug reads as "the player model does not work"
	// from a single listen server: the only player in the level is the one
	// entity that must not draw.
	//
	// SEES OWNER rather than IS THIRD PERSON, for the reason
	// SourceSharp_ThirdPersonSeesOwner gives: a camera pushed back onto the eye
	// by a wall is first person, and a body drawn for it is a screen full of
	// skull.
	//
	// It was C_StandalonePlayer::ShouldDraw until step 6f deleted that class;
	// the question it asked is the same one and the flag it asks it of is set
	// by the same rule.
	//-------------------------------------------------------------------------
	// 6b moved these two into the block (m_bDormant -> Client.Dormant,
	// m_bCustomDraw -> Client.CustomDraw); 6f wrote this arm against the old
	// members. Reading the block is the merge, not a rewrite of either.
	// tail.ModelType, NOT GetModel(). 6b moved "has this a model" into the block
	// (ModelType == -1 is exactly GetModel() == NULL, written at :1198/:1208)
	// and its fact forbids ShouldDraw asking the engine again, because whether a
	// model-less entity draws is a decision and a beam has none by design. 6f
	// wrote this arm against the old question. Same answer, read from the block.
	if ( m_bLocalPlayer )
		return tail.Dormant == 0 && tail.ModelType != -1 && SourceSharp_ThirdPersonSeesOwner();

	// A beam is the exception, and it is why the test below is not the whole
	// story any more: it has no model by design — it IS a drawing — so asking
	// whether it has one answers no for something that must draw every frame.
	if ( tail.CustomDraw != 0 )
		return tail.Dormant == 0;

	return tail.HideWhenOwnerSeen == 0 || !SourceSharp_ThirdPersonSeesOwner();
}

//-----------------------------------------------------------------------------
// THE MANAGED REPLAY'S ANSWER, ONCE A FRAME (plan_entity.md step 6f).
//
// Everything native still asks about the viewer arrives here and nowhere else:
// the eye offset, the view punch, and -- when the replay actually ran -- where
// it says the player is.
//
// THE ORIGIN IS WRITTEN ONLY WHILE DRIVING, and the pairing with
// WantsInterpolation is the whole of why. Interpolation draws an entity at a
// point read from the networked PAST; a replay computes where the player is
// NOW. Both writing the same field is a fight the frame rate decides, so
// exactly one of them writes at a time: the flag turns the entity out of the
// interpolation pass's list on the same frame the origin starts coming from
// here.
//
// The rest of the record is written EVERY frame, driving or not. It is the
// decoded state when nothing was replayed, which is the right answer for a
// client with prediction switched off -- and a frame that skipped the write
// would leave the eye wherever the last replayed frame put it, which is a view
// that stops following the player the moment cs_cl_player_predict goes to 0.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::PredictionDrive( const SourceSharpClientPlayerView &view )
{
	m_vecViewOffset.Init( view.vecViewOffset[0], view.vecViewOffset[1], view.vecViewOffset[2] );
	m_angPunch.Init( view.angPunch[0], view.angPunch[1], view.angPunch[2] );
	m_angPunchVel.Init( view.angPunchVel[0], view.angPunchVel[1], view.angPunchVel[2] );
	m_nPredictedHealth = view.nHealth;

	const bool bDriving = view.bDriving != 0;

	m_bPredictionDriving = bDriving;

	if ( !bDriving )
		return;

	Block().Head.Origin.Init( view.vecOrigin[0], view.vecOrigin[1], view.vecOrigin[2] );

	// The transform, the leaf and the collision bounds all hang off the origin
	// and none of them re-reads it. The native replay called exactly this after
	// writing the same field.
	OnPositionChanged();
}

//-----------------------------------------------------------------------------
// Bounds, in the entity's own space.
//
// From the model when there is one, because a bounding box that does not
// contain the model is a model that vanishes when its box leaves the screen —
// which reads as flickering rather than as wrong bounds.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::GetRenderBounds( Vector &mins, Vector &maxs )
{
	// The two endpoints, not a box around the origin. A beam culled by a point
	// vanishes whenever its emitter leaves the view, which looks exactly like a
	// beam that stopped being drawn.
	if ( Block().Client.CustomDraw )
	{
		mins = Block().Client.RenderMins;
		maxs = Block().Client.RenderMaxs;
		return;
	}

	// A JOINTED BODY IS NOT ITS MODEL'S BOX. The model's bounds describe the
	// mesh in its bind pose; a ragdoll's limbs go wherever the solver puts
	// them, which is routinely well outside it. Stock's CRagdoll answers a cube
	// of the model's own radius for exactly this reason -- deliberately
	// generous, because the alternative is a body culled while an arm is still
	// on screen -- and tightens it to the exact hull only once every element
	// has gone to sleep and stopped being able to leave the box.
	//
	// READ OUT OF THE TAIL since 6e, and not recomputed: the physics step
	// writes this box beside the origin it writes, so a body's bounds and the
	// place they are centred on can never be a step apart.
	if ( IsRagdoll() )
	{
		mins = Block().Client.RenderMins;
		maxs = Block().Client.RenderMaxs;
		return;
	}

	if ( const model_t *pModel = GetModel() )
	{
		modelinfo->GetModelRenderBounds( pModel, mins, maxs );

		// GROWN WITH THE MODEL, which is C_BaseAnimating::GetRenderBounds'
		// last two lines. The model's own bounds are the artist's, at size 1;
		// an entity drawn at twice that with the box it had at one is culled
		// while half of it is still on screen, and the symptom is a scaled
		// prop that blinks out as you walk past rather than anything that
		// mentions a bounding box.
		//
		// The custom-draw branch above is deliberately NOT scaled: managed code
		// that declares its own bounds has declared what it occupies, already
		// in world units, and multiplying a caller's own answer by a number it
		// did not know about is the kind of correction that cannot be undone
		// from the outside.
		mins *= m_flModelScale;
		maxs *= m_flModelScale;
		return;
	}

	mins = Block().Head.Mins;
	maxs = Block().Head.Maxs;
}

void C_StandaloneEntity::CreateModelInstance()
{
	if ( m_ModelInstance == MODEL_INSTANCE_INVALID )
		m_ModelInstance = modelrender->CreateInstance( this );
}

//-----------------------------------------------------------------------------
// PUBLISHING THE MODEL FACTS THE SHADOW RULE IS DECIDED FROM
// (plan_entity.md step 6b).
//
// The RULE is ClientRenderPolicy.Shadow's, in C#, and shadow_cast_policy.h --
// which held it, and its sixteen gtests -- is gone. What is left here is what
// could not go: opening a studio header is mdlcache's business and asking
// modelinfo what kind of model an index names is the engine's. So the shell
// reads the facts and writes them into the block, and the decision over them is
// made where §3.2 says decisions are made.
//
// CALLED WHEN THE MODEL INDEX CHANGES AND NOT PER FRAME. The index is networked
// and the engine's decoder writes it into the block directly, so nothing here
// runs to notice; the comparison against m_nPublishedModelIndex in
// PostDataUpdate is what notices. That is also why the read is affordable at
// all: the studio header comes out of the cache once per model change per
// entity rather than once per shadow question.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::PublishModelFacts()
{
	m_nPublishedModelIndex = Block().Head.ModelIndex;

	EngineBlockClientTail_t &tail = Block().Client;

	// The state before anything is known, restated rather than assumed: this
	// runs again every time the model changes, and a model that went away has
	// to take the previous model's counts with it.
	//
	// -1 AND NOT ZERO, TWICE, and neither is a tidiness choice. Zero is mod_bad
	// -- a model that failed to load -- and this is "there is no model". Zero
	// sequences is a model that cannot move and earns a baked shadow; -1 is a
	// studio header whose sequences have not been paged in and earns none yet.
	tail.ModelType                 = -1;
	tail.StudioSequenceCount       = -1;
	tail.StudioPoseParameterCount  = 0;
	tail.StudioBoneControllerCount = 0;
	tail.StudioIkChainCount        = 0;

	const model_t *pModel = GetModel();

	if ( pModel )
	{
		tail.ModelType = (int32_t)modelinfo->GetModelType( pModel );

		// The studio header is only opened for a studio model. A brush entity
		// has none, and asking the cache for one on every model change of every
		// door in the map buys nothing -- the policy refuses a non-studio model
		// on the model type alone.
		if ( tail.ModelType == mod_studio )
		{
			MDLCACHE_CRITICAL_SECTION();

			MDLHandle_t hModel = modelinfo->GetCacheHandle( pModel );
			studiohdr_t *pRenderHdr =
				( hModel != MDLHANDLE_INVALID ) ? mdlcache->GetStudioHdr( hModel ) : NULL;

			if ( pRenderHdr )
			{
				CStudioHdr studioHdr;
				studioHdr.Init( pRenderHdr, mdlcache );

				if ( studioHdr.IsValid() && studioHdr.SequencesAvailable() )
				{
					tail.StudioSequenceCount       = studioHdr.GetNumSeq();
					tail.StudioPoseParameterCount  = studioHdr.GetNumPoseParameters();
					tail.StudioBoneControllerCount = studioHdr.numbonecontrollers();
					tail.StudioIkChainCount        = studioHdr.numikchains();
				}
			}
		}
	}

	BumpPolicyInputs();
}

//-----------------------------------------------------------------------------
// What kind of shadow this entity casts: a LOAD.
//
// The decision is ClientRenderPolicy.Shadow's and it has already been made; the
// engine asks this through the shadow manager, per renderable, which is exactly
// the shape §3.2 answers from memory rather than from a rule run on the spot.
//
// The COMPILE_TIME_ASSERTs are what keeps the managed ShadowCast enum and
// ShadowType_t the same integers. This is the one place both are in scope --
// the same argument shadow_cast_policy.h made for having them here when it
// still held the rule.
//-----------------------------------------------------------------------------
ShadowType_t C_StandaloneEntity::ShadowCastType()
{
	COMPILE_TIME_ASSERT( (int)SHADOWS_NONE                      == 0 );
	COMPILE_TIME_ASSERT( (int)SHADOWS_SIMPLE                    == 1 );
	COMPILE_TIME_ASSERT( (int)SHADOWS_RENDER_TO_TEXTURE         == 2 );
	COMPILE_TIME_ASSERT( (int)SHADOWS_RENDER_TO_TEXTURE_DYNAMIC == 3 );

	return (ShadowType_t)Block().Client.ShadowType;
}

//-----------------------------------------------------------------------------
// Give the client shadow manager this entity's shadow, take it away, or leave
// it alone — whichever the block currently says.
//
// C_BaseEntity::CreateShadow, and the shape is stock's because the hazard is:
// the handle is the manager's only route back to a renderable, so a handle
// kept past the entity is a dangling projection and a handle never taken is no
// shadow at all. Stock calls its version from OnDataChanged; this hierarchy is
// not an entity the engine sends data-change events to (see PostDataUpdate),
// so it is called from the render-policy apply sweep, which runs for every
// entity whose managed half has just decided something new about it.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::RefreshShadow()
{
	// Nothing to cast onto, and nowhere to register: a shadow created before
	// the manager exists is a null dereference rather than a missing shadow.
	if ( !g_pClientShadowMgr )
		return;

	const ShadowType_t shadowType = ShadowCastType();

	if ( shadowType == SHADOWS_NONE )
	{
		DestroyShadow();
		return;
	}

	// Already has one. The manager recomputes the KIND itself from the flags it
	// was given, exactly as it does for stock — an entity whose model changed
	// from a studio model to a brush model loses the shadow above rather than
	// swapping it, which is stock's behaviour and its limitation both.
	if ( m_ShadowHandle != CLIENTSHADOW_INVALID_HANDLE )
		return;

	// THE FLAGS ARE A LOAD TOO. Which bits a shadow of this kind wants is
	// C_BaseEntity::CreateShadow's two lines and they are ClientRenderPolicy's
	// now, beside the type that decides them: getting either wrong is a shadow
	// that exists and is the wrong KIND, which no error reports and only a
	// photograph shows. Pinned here because here is where both declarations are
	// in scope.
	COMPILE_TIME_ASSERT( (int)SHADOW_FLAGS_SHADOW                == ( 1 << 1 ) );
	COMPILE_TIME_ASSERT( (int)SHADOW_FLAGS_USE_RENDER_TO_TEXTURE == ( 1 << 2 ) );
	COMPILE_TIME_ASSERT( (int)SHADOW_FLAGS_ANIMATING_SOURCE      == ( 1 << 3 ) );

	// ClientEntityHandle_t( BlockHandle() ) is what GetClientHandle() is on
	// C_BaseEntity. The manager looks the renderable back up through it —
	// ClientEntityList().GetClientRenderableFromHandle — and this entity is in
	// that list from its constructor, so the round trip lands back on `this`.
	// A _DEBUG build asserts exactly that inside AddToDirtyShadowList.
	m_ShadowHandle = g_pClientShadowMgr->CreateShadow( ClientEntityHandle_t( BlockHandle() ),
	                                                  Block().Client.ShadowFlags );
}

void C_StandaloneEntity::DestroyShadow()
{
	if ( m_ShadowHandle == CLIENTSHADOW_INVALID_HANDLE )
		return;

	g_pClientShadowMgr->DestroyShadow( m_ShadowHandle );
	m_ShadowHandle = CLIENTSHADOW_INVALID_HANDLE;

	// The manager clears this on the way out of its dirty list, but only for a
	// shadow it still holds. Cleared here too so the flag can never outlive the
	// handle it describes: a renderable that says "dirty" with no shadow is one
	// AddToDirtyShadowList will decline to touch forever after.
	m_bShadowDirty = false;
}

//-----------------------------------------------------------------------------
// A managed class overruling the model. See ClientRenderPolicy.Shadow.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::SetShadowCast( int nChoice )
{
	// Anything unrecognised is AUTO rather than an error. The value crosses the
	// ABI from managed code, and the failure to avoid is a number from a newer
	// C# side turning into a shadow decision nobody wrote.
	if ( nChoice != STANDALONE_SHADOW_CHOICE_NEVER && nChoice != STANDALONE_SHADOW_CHOICE_SIMPLE )
		nChoice = STANDALONE_SHADOW_CHOICE_AUTO;

	if ( nChoice == Block().Client.ShadowChoice )
		return;

	Block().Client.ShadowChoice = nChoice;

	// The block, and nothing else. RefreshShadow used to be called from here
	// with the note "now, not at the next update" -- an entity that is not
	// networked every tick would otherwise keep the shadow it was told to drop
	// for as long as it stood still. That argument still holds and this still
	// answers it: bumping the input serial is what puts the entity in front of
	// the managed pass at the end of this network update, and the apply sweep
	// at PreRender is what hands the answer to the shadow manager. Both happen
	// before the next frame is drawn, which is sooner than "the next update"
	// ever was.
	BumpPolicyInputs();
}

//-----------------------------------------------------------------------------
// THE SKELETON IS MANAGED (plan physics_bones_native_zero 1.2, client half).
//
// What stood here -- EnsureBones, MergeBonesFrom, EnsureAttachments, the
// attachment and hitbox reads over the bone cache, the pose parameters, the
// layer table and the sequence clock, through bone_library.cpp -- is
// SourceSharp.Client's ClientSkeletons over SourceSharp.Shared.Bones'
// ClientBoneDriver. Managed code asks it directly (the engine bindings'
// skeleton route); the ENGINE still asks through the IClientRenderable and
// ICollideable virtuals below, and each is a thunk into IManagedBonesClient
// that hands over this entity's engine block -- the model, the networked and
// drawn pose, the viewmodel bit, the hitbox set -- so an ask costs its one
// crossing and no read back.
//
// A RAGDOLL'S BONES ARE MANAGED'S TOO: the skeleton asks the client's
// ragdolls (SourceSharp.Client's ClientRagdolls) for a body's solver bones
// itself, so no ask carries them (plan_physics_bones_native_zero Phase 3).
//
// With no managed side (a load that failed) there is no skeleton: every slot
// is the entity's own transform, which is what EnsureBones' failure answered.
//-----------------------------------------------------------------------------

// physprops is no longer this side's need: the hitbox trace names its
// surface in managed code (ClientSkeletons, through IPhysicsSurfaceProps).
#include "standalone_bone_trace.h"
#include "standalone_bone_batch.h"

//-----------------------------------------------------------------------------
// THE HITBOX TEST, the server twin's thunk (shell_standalone_hierarchy.cpp)
// over this side's interface: the ray and the trace cross flat
// (standalone_bone_trace.h), the block by pointer.
//
// TRUE IS NOT "THE RAY HIT": it means "I have hitboxes and tested them", and
// tr.DidHit() is the hit. False -- no managed side, no model, no hitbox set --
// leaves the trace untouched and the engine keeps its box. The surface NAME
// stays native: managed says whether the hit named it "**studio**".
//-----------------------------------------------------------------------------
bool C_StandaloneEntity::TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pBones )
		return false;

	float flRay[ SS_BONE_RAY_FLOATS ];

	for ( int i = 0; i < 3; i++ )
	{
		flRay[ SS_BONE_RAY_START_X + i ]        = ray.m_Start[i];
		flRay[ SS_BONE_RAY_DELTA_X + i ]        = ray.m_Delta[i];
		flRay[ SS_BONE_RAY_START_OFFSET_X + i ] = ray.m_StartOffset[i];
		flRay[ SS_BONE_RAY_EXTENTS_X + i ]      = ray.m_Extents[i];
	}

	flRay[ SS_BONE_RAY_IS_RAY ]   = ray.m_IsRay ? 1.0f : 0.0f;
	flRay[ SS_BONE_RAY_IS_SWEPT ] = ray.m_IsSwept ? 1.0f : 0.0f;

	float   flTrace[ SS_BONE_TRACE_FLOATS ];
	int32_t nTrace[ SS_BONE_TRACE_INTS ];

	for ( int i = 0; i < 3; i++ )
	{
		flTrace[ SS_BONE_TRACE_START_X + i ]        = tr.startpos[i];
		flTrace[ SS_BONE_TRACE_END_X + i ]          = tr.endpos[i];
		flTrace[ SS_BONE_TRACE_PLANE_NORMAL_X + i ] = tr.plane.normal[i];
	}

	flTrace[ SS_BONE_TRACE_PLANE_DIST ] = tr.plane.dist;
	flTrace[ SS_BONE_TRACE_FRACTION ]   = tr.fraction;

	nTrace[ SS_BONE_TRACE_PLANE_TYPE ]        = tr.plane.type;
	nTrace[ SS_BONE_TRACE_CONTENTS ]          = tr.contents;
	nTrace[ SS_BONE_TRACE_ALL_SOLID ]         = tr.allsolid ? 1 : 0;
	nTrace[ SS_BONE_TRACE_START_SOLID ]       = tr.startsolid ? 1 : 0;
	nTrace[ SS_BONE_TRACE_HIT_GROUP ]         = tr.hitgroup;
	nTrace[ SS_BONE_TRACE_HIT_BOX ]           = tr.hitbox;
	nTrace[ SS_BONE_TRACE_PHYSICS_BONE ]      = tr.physicsbone;
	nTrace[ SS_BONE_TRACE_SURFACE_FLAGS ]     = tr.surface.flags;
	nTrace[ SS_BONE_TRACE_SURFACE_PROPS ]     = tr.surface.surfaceProps;
	nTrace[ SS_BONE_TRACE_SURFACE_IS_STUDIO ] = 0;

	if ( !pBones->TestHitboxes( GetRefEHandle().ToInt(), &Block(), flRay, (int32_t)fContentsMask,
	                            flTrace, nTrace, gpGlobals->curtime, gpGlobals->framecount ) )
		return false;

	for ( int i = 0; i < 3; i++ )
	{
		tr.startpos[i]     = flTrace[ SS_BONE_TRACE_START_X + i ];
		tr.endpos[i]       = flTrace[ SS_BONE_TRACE_END_X + i ];
		tr.plane.normal[i] = flTrace[ SS_BONE_TRACE_PLANE_NORMAL_X + i ];
	}

	tr.plane.dist           = flTrace[ SS_BONE_TRACE_PLANE_DIST ];
	tr.fraction             = flTrace[ SS_BONE_TRACE_FRACTION ];
	tr.plane.type           = (byte)nTrace[ SS_BONE_TRACE_PLANE_TYPE ];
	tr.contents             = nTrace[ SS_BONE_TRACE_CONTENTS ];
	tr.allsolid             = nTrace[ SS_BONE_TRACE_ALL_SOLID ] != 0;
	tr.startsolid           = nTrace[ SS_BONE_TRACE_START_SOLID ] != 0;
	tr.hitgroup             = nTrace[ SS_BONE_TRACE_HIT_GROUP ];
	tr.hitbox               = nTrace[ SS_BONE_TRACE_HIT_BOX ];
	tr.physicsbone          = (short)nTrace[ SS_BONE_TRACE_PHYSICS_BONE ];
	tr.surface.flags        = (unsigned short)nTrace[ SS_BONE_TRACE_SURFACE_FLAGS ];
	tr.surface.surfaceProps = (unsigned short)nTrace[ SS_BONE_TRACE_SURFACE_PROPS ];

	if ( nTrace[ SS_BONE_TRACE_SURFACE_IS_STUDIO ] )
		tr.surface.name = "**studio**";

	return true;
}

//-----------------------------------------------------------------------------
// THE FRAME'S SKELETONS IN ONE CROSSING (plan_physics_bones_perf A2). The
// frame's first SetupBones ask asks managed for the frame's batch
// (IManagedBonesClient::BoneBatch): the skeletons it expects the frame to
// draw, posed at this clock, in a table it owns (standalone_bone_batch.h).
// Every later ask this frame that the table holds an entry posed for -- the
// same entity, frame, clock and block inputs, a mask inside the posed one --
// is copied out with no crossing; anything else crosses as before. The table
// is asked again each frame, so a reload's new managed side is asked too.
//-----------------------------------------------------------------------------
namespace
{
struct BoneBatchState_t
{
	int nFrame;
	const SourceSharpBoneBatch_t *pBatch;
};

BoneBatchState_t g_BoneBatch = { -1, NULL };

const SourceSharpBoneBatch_t *FrameBoneBatch( IManagedBonesClient *pBones )
{
	if ( g_BoneBatch.nFrame != gpGlobals->framecount )
	{
		g_BoneBatch.nFrame = gpGlobals->framecount;
		g_BoneBatch.pBatch = static_cast<const SourceSharpBoneBatch_t *>(
			pBones->BoneBatch( gpGlobals->framecount, gpGlobals->curtime ) );
	}

	return g_BoneBatch.pBatch;
}
} // namespace

//-----------------------------------------------------------------------------
// The renderer's entry point: the engine's array, written in place by
// managed. A NULL pBoneToWorldOut is a request to pose and nothing else
// (C_BaseAnimating's contract through CalcAttachments); the answer is then
// whether there are bones, and true otherwise.
//-----------------------------------------------------------------------------
bool C_StandaloneEntity::SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask,
                                     float currentTime )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pBones )
	{
		for ( int i = 0; pBoneToWorldOut && i < nMaxBones; i++ )
			MatrixCopy( Block().Head.WorldTransform, pBoneToWorldOut[i] );

		return pBoneToWorldOut != NULL;
	}

	const SourceSharpBoneBatchEntry_t *pPosed = SourceSharp_BoneBatchEntry(
		FrameBoneBatch( pBones ), (uint32_t)GetRefEHandle().ToInt(), boneMask, currentTime,
		gpGlobals->framecount, Block().Head.ModelIndex, Block().Client.ViewModel, Block().Head.Origin.Base(),
		Block().Head.Angles.Base(), Block().Client.RenderOrigin.Base(), Block().Client.RenderAngles.Base() );

	if ( pPosed )
	{
		// Managed's answer, posed ahead: its bones, then the entity's own
		// transform in every slot past them (what the crossing writes).
		const_cast<SourceSharpBoneBatchEntry_t *>( pPosed )->nAnswered++;

		if ( !pBoneToWorldOut )
			return pPosed->nBones > 0;

		const float *pMatrices = g_BoneBatch.pBatch->pMatrices + pPosed->nFirstMatrix * SS_BONE_BATCH_MATRIX_FLOATS;
		const int nCopy = MIN( pPosed->nBones, nMaxBones );

		if ( nCopy > 0 )
			memcpy( pBoneToWorldOut[0].Base(), pMatrices, sizeof( matrix3x4_t ) * nCopy );

		for ( int i = nCopy; i < nMaxBones; i++ )
			MatrixCopy( Block().Head.WorldTransform, pBoneToWorldOut[i] );

		return true;
	}

	float *pOut = ( pBoneToWorldOut && nMaxBones > 0 ) ? pBoneToWorldOut[0].Base() : NULL;
	const int32_t nOut = pOut ? nMaxBones : 0;
	int32_t nAnswer;

	nAnswer = pBones->SetupBones( GetRefEHandle().ToInt(), &Block(), pOut, nOut, boneMask, currentTime,
	                              gpGlobals->framecount );

	// Managed writes every slot it is handed whenever it answers; a refusal
	// (no block, a throw) leaves the array as the engine passed it, so the
	// entity's own transform goes in rather than whatever was on the stack.
	if ( !nAnswer && pBoneToWorldOut )
	{
		for ( int i = 0; i < nMaxBones; i++ )
			MatrixCopy( Block().Head.WorldTransform, pBoneToWorldOut[i] );

		return true;
	}

	return nAnswer != 0;
}

//-----------------------------------------------------------------------------
// By name, and the answer is ONE-BASED: attachment 0 is the engine's "no
// attachment", which is also the answer for a name the model lacks.
//-----------------------------------------------------------------------------
int C_StandaloneEntity::LookupAttachment( const char *pAttachmentName )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pAttachmentName || !pBones )
		return -1;

	return pBones->LookupAttachment( GetRefEHandle().ToInt(), &Block(), pAttachmentName );
}

bool C_StandaloneEntity::GetAttachment( int number, matrix3x4_t &matrix )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pBones )
		return false;

	return pBones->GetAttachment( GetRefEHandle().ToInt(), &Block(), number, matrix.Base(),
	                              gpGlobals->curtime, gpGlobals->framecount ) != 0;
}

bool C_StandaloneEntity::GetAttachment( int number, Vector &origin, QAngle &angles )
{
	matrix3x4_t attachmentToWorld;

	if ( !GetAttachment( number, attachmentToWorld ) )
	{
		// The entity's own place rather than stack fungus, which is stock's
		// answer to the same failure and the only one a caller that ignores the
		// return value survives.
		origin = Block().Head.Origin;
		angles = Block().Head.Angles;

		return false;
	}

	MatrixAngles( attachmentToWorld, angles );
	MatrixPosition( attachmentToWorld, origin );

	return true;
}

//-----------------------------------------------------------------------------
// Drawing.
//
// The whole of what C_BaseEntity::DrawModel refuses to do, and the refusal is
// worth reading carefully because it does not mean what it says:
//
//     "All studio models must be derived from C_BaseAnimating"
//
// That is C_BaseEntity declining to do work it has no bones for. The ENGINE has
// no such rule — modelrender->DrawModelEx takes a ModelRenderInfo_t whose only
// entity-shaped field is an IClientRenderable *, which is this. So a studio
// model draws here by asking the same function C_BaseAnimating asks, with the
// same information, and nothing in between notices the difference.
//
// Brush models ARE handled, below. They were not, once, for the reason this
// comment used to give — render->DrawBrushModelEx takes an IClientEntity * and
// this was not one — and that is why the shell now implements IClientEntity and
// GetIClientEntity() returns `this`. See the note on it in the header.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CUSTOM DRAWS OF A VIEW, IN ONE CROSSING (plan_client_novalve C7 item 7).
//
// A custom-draw entity (a sprite, a beam, a rope) asked managed to draw it from
// DrawModel, one crossing each: at the live gate's render stand that was 16 of
// the client's 36 crossings a frame. Now the leaf system's list for a view is
// captured as it is built (SourceSharp_StandaloneDrawBatchCapture, from the
// executor's BuildRenderablesList hook): its custom-draw entities, and the
// blend each will be drawn under (DrawTranslucentRenderable's fx blend, 1 for
// an opaque one). The first of them to reach DrawModel asks managed ONCE for
// all (IManagedStandaloneClient::OnStandaloneDrawBatch); managed prepares what
// it can ahead -- a sprite's plan, stored here by FxSpriteDrawStore; a beam's
// next life -- and answers -1 for what must still draw in place (a rope). Each
// DrawModel then carries out its prepared draw with no crossing.
//
// THE SPRITE'S PLAN IS ONLY GOOD UNDER THE BLEND IT WAS MADE FOR, so a sprite
// whose DrawModel finds another blend in force falls back to the crossing.
//-----------------------------------------------------------------------------
namespace
{
struct DrawBatchEntry_t
{
	C_StandaloneEntity *pEntity;
	uint32_t            hHandle;
	float               flBlend;
	int32_t             nResult;
};

CUtlVector<DrawBatchEntry_t> g_DrawBatch;
bool g_bDrawBatchAsked = false;
bool g_bDrawBatchPreparing = false;

int FindInDrawBatch( const C_StandaloneEntity *pEntity )
{
	for ( int i = 0; i < g_DrawBatch.Count(); ++i )
		if ( g_DrawBatch[i].pEntity == pEntity )
			return i;
	return -1;
}

C_StandaloneEntity *StandaloneFromRenderable( IClientRenderable *pRenderable )
{
	IClientUnknown *pUnknown = pRenderable ? pRenderable->GetIClientUnknown() : NULL;
	uint32_t nWord = 0;
	if ( !RenderableEntityHandleWord( pUnknown, nWord ) )
		return NULL;
	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( nWord );
	return ( pEntity && static_cast<IClientRenderable *>( pEntity ) == pRenderable ) ? pEntity : NULL;
}
} // namespace

void SourceSharp_StandaloneDrawBatchCapture( const CClientRenderablesList &list )
{
	g_DrawBatch.RemoveAll();
	g_bDrawBatchAsked = false;
	SourceSharp_SpriteStoredClear();

	for ( int g = 0; g < RENDER_GROUP_COUNT; ++g )
	{
		const bool bTranslucent = ( g == RENDER_GROUP_TRANSLUCENT_ENTITY );
		for ( int i = 0; i < list.m_RenderGroupCounts[g]; ++i )
		{
			C_StandaloneEntity *pEntity = StandaloneFromRenderable( list.m_RenderGroups[g][i].m_pRenderable );
			if ( !pEntity || !pEntity->Block().Client.CustomDraw )
				continue;
			const float flBlend = bTranslucent ? (float)( pEntity->GetFxBlend() / 255.0f ) : 1.0f;
			// DrawTranslucentRenderable draws nothing at a blend of zero, so
			// nothing is prepared for it.
			if ( flBlend <= 0.0f )
				continue;
			DrawBatchEntry_t entry = { pEntity, (uint32_t)pEntity->BlockHandle().ToInt(), flBlend, -1 };
			g_DrawBatch.AddToTail( entry );
		}
	}
}

float SourceSharp_StandaloneDrawBatchBlend( IClientEntity *pEntity, float flFallback )
{
	if ( !g_bDrawBatchPreparing )
		return flFallback;
	for ( int i = 0; i < g_DrawBatch.Count(); ++i )
		if ( static_cast<IClientEntity *>( g_DrawBatch[i].pEntity ) == pEntity )
			return g_DrawBatch[i].flBlend;
	return flFallback;
}

static void AskDrawBatch()
{
	g_bDrawBatchAsked = true;
	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();
	const int n = g_DrawBatch.Count();
	if ( !pStandalone || n == 0 )
		return;

	CUtlVector<uint32_t> handles;
	CUtlVector<int32_t> results;
	handles.SetCount( n );
	results.SetCount( n );
	for ( int i = 0; i < n; ++i )
	{
		handles[i] = g_DrawBatch[i].hHandle;
		results[i] = -1;
	}
	g_bDrawBatchPreparing = true;
	g_SourceSharpEffects.nDrawBatches++;
	pStandalone->OnStandaloneDrawBatch( handles.Base(), n, results.Base() );
	g_bDrawBatchPreparing = false;
	for ( int i = 0; i < n; ++i )
		g_DrawBatch[i].nResult = results[i];
}

int C_StandaloneEntity::DrawModel( int flags )
{
	// Before the model check, deliberately: a beam has none and everything
	// below assumes one.
	if ( Block().Client.CustomDraw )
	{
		const int nBatch = FindInDrawBatch( this );
		if ( nBatch >= 0 )
		{
			if ( !g_bDrawBatchAsked )
				AskDrawBatch();
			const DrawBatchEntry_t &entry = g_DrawBatch[nBatch];
			if ( entry.nResult >= 0 )
			{
				const int nStored = SourceSharp_SpriteExecuteStored( static_cast<IClientEntity *>( this ), render->GetBlend() );
				if ( nStored >= 0 )
				{
					g_SourceSharpEffects.nDrawBatchedSprites++;
					return nStored;
				}
				if ( nStored == -2 )
					return entry.nResult;
				// A stored sprite under another blend: draw it the long way.
			}
			g_SourceSharpEffects.nDrawBatchFallbacks++;
		}

		IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

		return pStandalone
			? pStandalone->OnStandaloneDraw( (uint32_t)BlockHandle().ToInt() ) : 0;
	}

	const model_t *pModel = GetModel();

	if ( !pModel )
		return 0;

	const int nModelType = modelinfo->GetModelType( pModel );

	if ( nModelType != mod_studio && nModelType != mod_brush )
	{
		// Sprites, which nothing on this hierarchy uses and the SDK itself
		// stopped supporting outside legacy temp ents. Said once per entity
		// rather than once per frame, which is the difference between a warning
		// and a wall of text at 300fps.
		if ( !m_bDrawReported )
		{
			m_bDrawReported = true;
			Warning( "[SourceSharp] standalone(client): '%s' is neither a studio "
			         "nor a brush model; nothing will draw it\n",
				modelinfo->GetModelName( pModel ) );
		}

		return 0;
	}

	// Said once per entity, because "is this being drawn at all" is the only
	// question that cannot be answered from outside the game. A model that draws
	// in the wrong place is visible; one that is never asked to draw looks
	// exactly like one that is drawn and invisible, and the two have nothing in
	// common to fix.
	if ( !m_bDrawReported )
	{
		m_bDrawReported = true;
		DevMsg( "[SourceSharp] standalone(client): '%s' drawing '%s' at (%.0f %.0f %.0f) "
		        "angles (%.1f %.1f %.1f)\n",
			m_szClassname, modelinfo->GetModelName( pModel ),
			Block().Head.Origin.x, Block().Head.Origin.y, Block().Head.Origin.z,
			Block().Head.Angles.x, Block().Head.Angles.y, Block().Head.Angles.z );
	}

	//-------------------------------------------------------------------------
	// A brush model, which is a piece of the map rather than a thing in it.
	//
	// Drawn by the world renderer rather than the model renderer, and through
	// an interface that names an entity: DrawBrushModelEx takes an
	// IClientEntity *, which is why this shell implements one at all. The
	// origin and angles are passed alongside because the engine transforms the
	// submodel by them — a door is the same brushes at a different place.
	//-------------------------------------------------------------------------
	if ( nModelType == mod_brush )
	{
		ERenderDepthMode depthMode = DEPTH_MODE_NORMAL;

		if ( flags & STUDIO_SSAODEPTHTEXTURE )
			depthMode = DEPTH_MODE_SSA0;
		else if ( flags & STUDIO_SHADOWDEPTHTEXTURE )
			depthMode = DEPTH_MODE_SHADOW;

		if ( depthMode != DEPTH_MODE_NORMAL )
		{
			render->DrawBrushModelShadowDepth( this, (model_t *)pModel,
			                                   Block().Head.Origin, Block().Head.Angles, depthMode );
		}
		else
		{
			// Two-pass means the renderer is drawing opaque and translucent
			// faces in separate passes and telling us which one this is. A
			// brush with both — glass in a frame — drawn all at once in either
			// pass sorts wrongly against the rest of the world.
			DrawBrushModelMode_t mode = DBM_DRAW_ALL;

			if ( flags & STUDIO_TWOPASS )
			{
				mode = ( flags & STUDIO_TRANSPARENCY ) ? DBM_DRAW_TRANSLUCENT_ONLY
				                                       : DBM_DRAW_OPAQUE_ONLY;
			}

			// A surface that has shattered draws its own faces: the renderer
			// installs itself for the length of this call and the engine hands
			// it each brush surface instead of drawing it. Unbroken, it
			// installs nothing and this is an ordinary brush.
			if ( m_pSurface )
				m_pSurface->BeginDraw();

			// The fade, applied here rather than left to GetFxBlend. A brush
			// model is drawn by the WORLD renderer, which reads the blend it
			// was last handed rather than asking the renderable -- so a faded
			// brush that only answered GetFxBlend drew at whatever the previous
			// entity happened to leave set.
			if ( Block().Client.FadeStyle != 0 )
				render->SetBlend( Block().Client.FxBlend / 255.0f );

			render->DrawBrushModelEx( this, (model_t *)pModel, Block().Head.Origin, Block().Head.Angles, mode );

			// Uninstalled unconditionally: it is process-global state, and
			// leaving it set would hand every brush in the world to this
			// entity's renderer.
			if ( m_pSurface )
				m_pSurface->EndDraw();

			// The second model, opaque and behind the first. See
			// StandaloneSetOverlayModel: a fake window is a fading pane with
			// something solid behind it, and the two must be drawn by one
			// entity so that they sort together.
			if ( m_nOverlayModelIndex > 0 )
			{
				model_t *pOverlay = (model_t *)modelinfo->GetModel( m_nOverlayModelIndex );

				if ( pOverlay && modelinfo->GetModelType( pOverlay ) == mod_brush )
				{
					render->SetBlend( 1.0f );
					render->DrawBrushModelEx( this, pOverlay, Block().Head.Origin, Block().Head.Angles, mode );
				}
			}

			// Put back, because it is renderer-global: a brush left at half
			// blend fades whatever the world renderer draws next.
			if ( Block().Client.FadeStyle != 0 || m_nOverlayModelIndex > 0 )
				render->SetBlend( 1.0f );
		}

		return 1;
	}

	// A model instance, before the draw and on every draw.
	//
	// C_BaseAnimating::DrawModel calls this in the same place with the comment
	// "Necessary for lighting blending", and that is what it is: the instance
	// is where the engine keeps this renderable's LIGHTING STATE between
	// frames, so a draw without one relights from scratch every frame and can
	// never blend from what it was.
	//
	// The method below has existed since the studio render did, with a header
	// comment saying exactly this — and nothing called it. GetModelInstance()
	// therefore answered MODEL_INSTANCE_INVALID for every entity on this
	// hierarchy, forever, and the engine drew them down a different lighting
	// path from every stock prop. Nothing reported it: an invalid instance is a
	// legal value that means "this renderable does not keep one", so the model
	// drew, at very nearly the right colour.
	//
	// What "very nearly" is worth was measured by Phase 5's gate: 0.32% of
	// pixels against a floor of 0.0000%, almost all of them ±1 or ±2, spread
	// over the barrel's curved body and clean on its flat top. That is the
	// shape of a lighting difference and not of a geometry one, which is what
	// pointed here after a sub-pixel shift, the angle quantisation and the
	// physics prop's baked static lighting had each been measured and ruled
	// out.
	//
	// It is also where DECALS would go: modelrender keeps them per instance, so
	// until this line a bullet hole had nowhere to be recorded.
	CreateModelInstance();

	ModelRenderInfo_t info;
	info.origin       = GetRenderOrigin();
	info.angles       = GetRenderAngles();
	info.pRenderable  = this;
	info.pModel       = pModel;
	// The viewmodel is drawn and LIT at its render pose, as EnsureBones poses
	// it. Without the explicit lighting origin the engine lit it at the
	// owner's feet and it drew black (textured under mat_fullbright 1).
	const bool bViewModel = Block().Client.ViewModel != 0;
	info.pModelToWorld= bViewModel ? &Block().Client.RenderTransform : &Block().Head.WorldTransform;
	info.instance     = GetModelInstance();
	if ( bViewModel )
		info.pLightingOrigin = &Block().Client.RenderOrigin;
	info.entity_index = entindex();
	info.skin         = GetSkin();
	info.body         = GetBody();
	// FROM THE BLOCK, not a literal. Step 4d found this hard-coded on both
	// sides and named 6d as the owner; the field is EngineBlockClientTail's
	// HitboxSet, beside the sequence, and the server's TestHitboxes reads the
	// same word. Which set a model wears is a decision (a head shootable only
	// with the helmet off), so it is managed code's to make.
	info.hitboxset    = Block().Client.HitboxSet;
	info.flags        = flags;

	// DrawModelEx rather than DrawModelSetup + DrawModelExecute, which is the
	// pair C_BaseAnimating::InternalDrawModel calls. The two were MEASURED
	// against each other while hunting the prop-shot residual and are the same
	// picture to the pixel (0.3178% against 0.3190%, within the run-to-run
	// floor), so the one call stays. See docs/divergences.md.
	return modelrender->DrawModelEx( info );
}

void C_StandaloneEntity::WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const
{
	if ( pMins ) *pMins = Block().Head.Origin + Block().Head.Mins;
	if ( pMaxs ) *pMaxs = Block().Head.Origin + Block().Head.Maxs;
}

void C_StandaloneEntity::WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs )
{
	Vector vecMins, vecMaxs;
	GetRenderBounds( vecMins, vecMaxs );

	// GetRenderOrigin() rather than Block().Head.Origin, and for a jointed body they
	// are different places: nothing updates the networked origin once the
	// solver owns the entity, so adding the ragdoll's BOUNDS to the thrown-from
	// ORIGIN is a box of the right size in the wrong place -- the half-fix that
	// is worse than either half.
	//
	// Identical for every entity that is not a ragdoll, which is what makes it
	// safe to write plainly here instead of behind a branch: GetRenderOrigin()
	// answers the client tail's RenderOrigin for all of them, and the tail's
	// pose is published from the head every time the entity moves.
	const Vector &vecOrigin = GetRenderOrigin();

	if ( pMins ) *pMins = vecOrigin + vecMins;
	if ( pMaxs ) *pMaxs = vecOrigin + vecMaxs;
}

//-----------------------------------------------------------------------------
// The table and the class, mirroring the server's by hand.
//
// The NAME is what pairs them. The engine matches a ClientClass to a
// ServerClass by string, and a server class with no partner is what made the
// game unjoinable — so this file existing is the fix, not anything it does.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The four props every entity on the hierarchy receives, appended to an array.
//
// Written once and used twice, exactly as the server writes them once and uses
// them twice — by the shared class and by each per-class table. They go FIRST
// in every table, so a class that adds fields extends rather than renumbers.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE TYPE ID'S RECV PROXY, and it is the whole of how this side meets §3.6's
// "creates the managed entity immediately rather than on first data update".
//
// A recv proxy runs INSIDE RecvTable_Decode, at the moment its own prop is
// unpacked. The type id is prop 5 of the eight shared props (see
// standalone_nettable.h) and the payload words that carry the entity's own
// state are props 8 and after, so this fires BEFORE a single byte of this
// entity's state has been written -- which is what "before its first data
// update" means on a side whose entities are made by the engine and told what
// they are afterwards.
//
// pStruct IS THE SHELL. RecvTable_Decode is handed ent->GetDataTableBasePtr(),
// and this hierarchy's returns `this`; every offset in this table is taken from
// the same base, so if that were ever untrue every prop would already be
// writing to the wrong place.
//
// The store first and the call second, because EnsureManagedHalf reads
// m_nTypeId. The value is written exactly as RecvProxy_Int32ToInt32 writes it.
//-----------------------------------------------------------------------------
static void RecvProxy_StandaloneTypeId( const CRecvProxyData *pData, void *pStruct,
                                        void *pOut )
{
	*( (uint32 *)pOut ) = (uint32)pData->m_Value.m_Int;

	SourceSharp_ClientShellFromRecvStruct( pStruct )->EnsureManagedHalf();
}

static void BuildStandaloneRecvProps( RecvProp *pOut, int &nOut )
{
	C_StandaloneEntity *pNull = NULL;

	// Our own offsets, in the shared list's order — the mirror of the server's,
	// and the ONLY thing about this table that is written twice. See
	// standalone_nettable.h: the names and the order the handshake pairs on
	// have exactly one copy, so they cannot drift.
	int nOffsets[ STANDALONE_NET_FIELD_COUNT ];
	// THE FIRST FOUR ARE IN THE ENGINE BLOCK (plan_entity.md §3.2), which is
	// where §3.2 says the engine's decoder writes the networked part of the
	// block directly — "because the recv props address it", and this is them
	// addressing it.
	//
	// THE SAME ARITHMETIC THE SERVER'S SendProp BUILDER USES, spelled the same
	// way: BlockBase() plus the field's offset inside the block. The two
	// NUMBERS differ, and must — the block sits at a different place in the two
	// shells — but a prop's offset is only ever applied to the side that
	// declared it, and what has to match across the wire is the NAME and the
	// ORDER, which standalone_nettable.h holds once for both.
	nOffsets[ STANDALONE_NET_FIELD_ORIGIN ]       = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( Origin );
	nOffsets[ STANDALONE_NET_FIELD_ANGLES ]       = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( Angles );
	nOffsets[ STANDALONE_NET_FIELD_MODELINDEX ]   = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( ModelIndex );
	// The EF_* bits, step 8e-render. The engine's decoder writes them straight
	// into the block, which is why nothing on this side runs when they change --
	// see the Effects arm at the tail of PostDataUpdate, which is what notices.
	nOffsets[ STANDALONE_NET_FIELD_EFFECTS ]      = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( Effects );
	// The discontinuity counter a teleport bumps. The engine's decoder writes it
	// straight into the block, and the reader is MANAGED -- Interpolation's
	// sample pass compares it against OldInterpolationFrame beside it and throws
	// the entity's history away when the two differ. Nothing on this side runs
	// when it changes, which is why there is no arm for it in PostDataUpdate.
	nOffsets[ STANDALONE_NET_FIELD_INTERPFRAME ]  = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( InterpolationFrame );
	nOffsets[ STANDALONE_NET_FIELD_TYPEID ]       = (int)( (intp)&pNull->m_nTypeId );
	nOffsets[ STANDALONE_NET_FIELD_SCHEMAHASH ]   = (int)( (intp)&pNull->m_nSchemaHash );
	nOffsets[ STANDALONE_NET_FIELD_PAYLOADWORDS ] = (int)( (intp)&pNull->m_nPayloadWords );
	// THE SOLIDITY PAIR, received at the tail with their list rows, in THIS
	// side's block layout. The engine's decoder writes them straight into the
	// block, and the reader is the shell itself: GetSolid / GetSolidFlags
	// answer from these words the way the server twin does, which is what lets
	// the impact clip stop at a managed prop instead of passing through a
	// client that was never told the prop was solid. Nothing on this side runs
	// when they change, which is why there is no arm for them in
	// PostDataUpdate — the answers are loads, not cached state.
	nOffsets[ STANDALONE_NET_FIELD_SOLIDTYPE ]    = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( SolidType );
	nOffsets[ STANDALONE_NET_FIELD_SOLIDFLAGS ]   = C_StandaloneEntity::BlockBase()
	                                              + SS_CLIENT_BLOCK_HEAD_OFFSET( SolidFlags );

	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		switch ( pFields[i].kind )
		{
		case STANDALONE_NET_VECTOR:
			pOut[ nOut++ ] = RecvPropVector( pFields[i].pName, nOffsets[i], 12 );
			break;

		case STANDALONE_NET_INT:
			// The type id alone carries a proxy; the rest keep the fast
			// int-to-int one. See RecvProxy_StandaloneTypeId for why, and note
			// that the SERVER's prop is unchanged — a recv proxy is one side's
			// business and nothing about it is on the wire.
			pOut[ nOut++ ] = ( i == STANDALONE_NET_FIELD_TYPEID )
				? RecvPropInt( pFields[i].pName, nOffsets[i], 4, 0,
				               RecvProxy_StandaloneTypeId )
				: RecvPropInt( pFields[i].pName, nOffsets[i], 4 );
			break;

		case STANDALONE_NET_STRING:
			pOut[ nOut++ ] = RecvPropString( pFields[i].pName, nOffsets[i], pFields[i].nBits );
			break;
		}
	}
}

//=============================================================================//
// The instance blob.
//
// ONE SIZE PER CLASS SINCE PART 18 — the mirror of the server's, and this side
// has it easier: there is one create function per tier, so the tier is known at
// the moment of construction with nothing to look up and nothing to agree.
//
// The global length and its setter are gone for the reason the server's are:
// left compiling, they would answer a length true of one class and wrong for
// every other, and reading a blob at the wrong length reports nothing. Removing
// the symbol makes an unconverted caller a link error.
//
// The BASE stays a global and stays a constant — it is the offset of the blob
// within the object, and every tier's recv props were resolved against it.
//=============================================================================//
int SourceSharp_StandaloneBlobOffset()
{
	// ONE SHELL SINCE STEP 6f. The recv props write the blob at one offset for
	// the whole hierarchy, and until 6f that offset had to clear a player
	// SUBCLASS as well -- C_StandalonePlayer carried sixty members of native
	// movement state past the base's end, and an offset that did not clear them
	// had the engine decode the payload straight over the player's own members
	// (see the server's twin for the crash that named the rule). The subclass
	// is gone: its state is the managed player's, and this class is the only
	// shell a client entity can be.
	return (int)ALIGN_VALUE( sizeof( C_StandaloneEntity ), 16 );
}

//-----------------------------------------------------------------------------
// CBaseHandle IS FOUR BYTES AND NOTHING ELSE, which is what makes BlockHandle()
// an overlay rather than a copy. The server's twin, spelled the same.
//
// Asserted rather than assumed because the failure would be silent in the worst
// direction: a CBaseHandle with a second member would read four bytes of
// whatever follows Handle in the block -- ClassnameIndex -- as its serial, and
// every handle on the hierarchy would resolve to nothing with nothing to say
// why. On this side that reader is the ENGINE, on a trace path that
// dereferences the answer with no null test.
//-----------------------------------------------------------------------------
static_assert( sizeof( CBaseHandle ) == sizeof( uint32_t ),
	"CBaseHandle is no longer one uint32; EngineBlockHead_t::Handle cannot overlay it" );

//-----------------------------------------------------------------------------
// Where the engine block sits inside the shell. See the header for why it is a
// member here and the blob's prefix on the server.
//
// Taken through a null pointer rather than offsetof(), which is the idiom
// BuildStandaloneRecvProps has always used for the other three props' offsets:
// this class derives from three interfaces and is not standard-layout, so
// offsetof on it is conditionally supported and warns.
//-----------------------------------------------------------------------------
int C_StandaloneEntity::BlockBase()
{
	C_StandaloneEntity *pNull = NULL;

	return (int)( (intp)&pNull->m_EngineBlock );
}

void *C_StandaloneEntity::operator new( size_t nBytes, int nBlobBytes )
{
	Assert( (int)nBytes <= SourceSharp_StandaloneBlobOffset() );

	// Refused up to the smallest tier rather than trusted, as on the server:
	// a length shorter than the table decoding into it is the engine writing
	// past the allocation, and nothing between here and the recv props looks.
	//
	// AND THE FLOOR IS STILL TIER 0 HERE, WHERE THE SERVER'S HAD TO MOVE TO
	// SourceSharp_StandaloneMinTier(). That is not an oversight and it is worth
	// saying out loud, because the two lines look like a mirror that slipped.
	//
	// Step 1b moved the server's floor because the server's ENGINE BLOCK IS THE
	// BLOB'S PREFIX: a blob floored at tier 0's thirty-two bytes had the
	// constructor write three hundred and fifty-two into it, which is a
	// 320-byte overrun of an engine allocation before the entity is bound to
	// anything that could report it. This side's block is a MEMBER of the shell
	// (see C_StandaloneEntity::BlockBase), so nothing the block's defaults write
	// lands in the blob at all, and the blob still holds only what the class
	// declared — which is what tier 0 has always been the floor FOR.
	//
	// If the block is ever moved into the blob on this side, THIS LINE IS THE
	// ONE THAT HAS TO MOVE WITH IT, and standalone_client_layout_test.cpp's
	// TheFloorHoldsWhateverThisSidePutsInTheBlob pins the pair.
	if ( nBlobBytes < SourceSharp_TierBytes( 0 ) )
		nBlobBytes = SourceSharp_TierBytes( 0 );

	const int nTotal = SourceSharp_StandaloneBlobOffset() + nBlobBytes;

	// Zeroed, because the recv props write only the words that changed and a
	// class reading its own state before the first update would otherwise read
	// the heap's leavings — which C# would decode as state.
	void *pMem = malloc( nTotal );
	if ( pMem )
		V_memset( pMem, 0, nTotal );

	return pMem;
}

void C_StandaloneEntity::operator delete( void *pMem )
{
	free( pMem );
}

// Pairs with the sized new for a constructor that threw. Nothing here throws,
// but without it that path would leak and the compiler is right to say so.
void C_StandaloneEntity::operator delete( void *pMem, int )
{
	free( pMem );
}

unsigned char *C_StandaloneEntity::DataBlob()
{
	return (unsigned char *)this + SourceSharp_StandaloneBlobOffset();
}

//-----------------------------------------------------------------------------
// How long this entity's blob is (Part 18). Straight off the tier table, so
// there is one number rather than a second member that could disagree with the
// length the allocation was actually made at.
//-----------------------------------------------------------------------------
int C_StandaloneEntity::BlobBytes() const
{
	return SourceSharp_TierBytes( m_iTier );
}

void C_StandaloneEntity::SetSurfaceEdges( const unsigned char *pEdges, int nCount )
{
	if ( m_pSurface )
		m_pSurface->SetEdges( pEdges, nCount );
}

//=============================================================================//
// The tiers, client half (Part 7, Part 14 Step 6).
//
// ONE ClientClass PER TIER for the whole hierarchy, where there used to be one
// shared class plus one per class that declared state. Each is the eight real
// props followed by that tier's payload words, and every entity here arrives
// through whichever one ITS OWN CLASS's declaration lands in — which is also
// how this side learns an entity's tier, since the create function it came
// through is the only thing that knows before a single prop has been decoded.
//
// The create-function POOL is gone with them. It was sixty-four trampolines
// carrying a class index, and it ran out twice — silently, with the join dying
// while naming an innocent class. A tier needs one function, they are written
// out at the tier bound, and there is nothing left to exhaust.
//=============================================================================//
static RecvProp    *g_pTierProps[ SOURCESHARP_TIER_MAX ]   = { NULL };
static RecvTable   *g_pTierTables[ SOURCESHARP_TIER_MAX ]  = { NULL };
static ClientClass *g_pTierClasses[ SOURCESHARP_TIER_MAX ] = { NULL };
static bool         g_bTiersRegistered  = false;

static IClientNetworkable *CreateStandaloneTier( int iTier, int entnum, int serialNum )
{
	// ONE local feeding both halves, as the server's factory does: the blob is
	// allocated at this tier's length and the entity is told the same tier, so
	// the recv props decoding into it cannot belong to a different one.
	C_StandaloneEntity *pEntity = new ( SourceSharp_TierBytes( iTier ) )
		C_StandaloneEntity( entnum, serialNum, g_pTierClasses[ iTier ], iTier );

	// An entity in 1..maxclients is a PLAYER — the index range the engine
	// reserves for its clients, stated before a single prop has decoded, and
	// the only fact the trampoline has (Phase 9a). The server's factory chooses
	// by the class desc; this side cannot, because the classname arrives with
	// the first update, after construction.
	//
	// A FLAG SINCE STEP 6f, NOT A TYPE. It used to construct a
	// C_StandalonePlayer here, and that subclass existed to hold the movement
	// state the client's native replay ran on. The replay is managed and so is
	// the state, so what is left of "this one is a player" is one bit -- which
	// is what §3.16 asked for, and the rule that sets it is still written down
	// exactly once, here.
	pEntity->SetPlayerShell( entnum >= 1 && entnum <= gpGlobals->maxClients );

	return pEntity;
}

static IClientNetworkable *CreateStandaloneTier0( int e, int s ) { return CreateStandaloneTier( 0, e, s ); }
static IClientNetworkable *CreateStandaloneTier1( int e, int s ) { return CreateStandaloneTier( 1, e, s ); }
static IClientNetworkable *CreateStandaloneTier2( int e, int s ) { return CreateStandaloneTier( 2, e, s ); }
static IClientNetworkable *CreateStandaloneTier3( int e, int s ) { return CreateStandaloneTier( 3, e, s ); }
static IClientNetworkable *CreateStandaloneTier4( int e, int s ) { return CreateStandaloneTier( 4, e, s ); }
static IClientNetworkable *CreateStandaloneTier5( int e, int s ) { return CreateStandaloneTier( 5, e, s ); }
static IClientNetworkable *CreateStandaloneTier6( int e, int s ) { return CreateStandaloneTier( 6, e, s ); }

static void RegisterStandaloneTiers()
{
	if ( g_bTiersRegistered )
		return;

	g_bTiersRegistered = true;

	// One per tier, DECLARED at the tier bound so a tier added without a
	// function here does not compile rather than leaving a NULL the engine
	// would call on the first entity of that tier. The old per-class pool ran
	// out at eight twice, silently, and the join then died naming an innocent
	// class; this cannot run out because it cannot be short.
	static CreateClientClassFn s_Create[ SOURCESHARP_TIER_MAX ] =
	{
		CreateStandaloneTier0, CreateStandaloneTier1, CreateStandaloneTier2,
		CreateStandaloneTier3, CreateStandaloneTier4, CreateStandaloneTier5,
		CreateStandaloneTier6,
	};

	int nShared = 0;
	StandaloneNetFields( &nShared );

	// The BLOB, not a shadow. There is only one prop set now, so the bytes the
	// engine unpacks are the bytes C# reads — which is what Step 5 said Step 6
	// would finish.
	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	for ( int iTier = 0; iTier < SourceSharp_TierCount(); iTier++ )
	{
		const int nWords = SourceSharp_TierWords( iTier );
		const char *pClassName = SourceSharp_TierClassName( SS_SHELL_STANDALONE, iTier );
		const char *pTableName = SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier );

		if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBlobBase, nWords ) )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d word(s) will not fit at blob "
			         "base %d; NOT registered\n", pTableName, nWords, nBlobBase );
			continue;
		}

		g_pTierProps[ iTier ] = new RecvProp[ nShared + nWords ];

		int nBuilt = 0;
		BuildStandaloneRecvProps( g_pTierProps[ iTier ], nBuilt );

		if ( SourceSharpPayload_BuildRecvProps( g_pTierProps[ iTier ] + nBuilt,
		                                        nWords, nBlobBase ) != nWords )
		{
			Warning( "[SourceSharp] standalone tier '%s': payload build refused %d word(s); "
			         "not registered\n", pTableName, nWords );
			delete[] g_pTierProps[ iTier ];
			g_pTierProps[ iTier ] = NULL;
			continue;
		}

		nBuilt += nWords;

		g_pTierTables[ iTier ] = new RecvTable( g_pTierProps[ iTier ], nBuilt, pTableName );
		g_pTierClasses[ iTier ] = new ClientClass( pClassName, s_Create[ iTier ],
		                                           NULL, g_pTierTables[ iTier ] );

		// The recv half of Part 1A's three-way conjunction. The engine's local
		// (listen-server) transfer checks BOTH ends, so a correct send prop paired with a wrong recv
		// prop is still the slow list — and a server-only dump reports
		// everything fine regardless.
		int nSlow = 0;

		for ( int i = 0; i < nWords; i++ )
		{
			if ( !SourceSharpPayload_IsFastRecvProxy( g_pTierTables[ iTier ]->GetProp( nShared + i ) ) )
				nSlow++;
		}

		Msg( "[SourceSharp] standalone(client) tier %d '%s' -> '%s': %d real + %d payload "
		     "word(s) = %d prop(s), %d payload byte(s) at blob base %d\n",
			iTier, pClassName, pTableName, nShared, nWords, nBuilt,
			SourceSharp_Tiers( NULL )[ iTier ].nBytes, nBlobBase );

		if ( nSlow > 0 )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d of %d payload word(s) did NOT "
			         "resolve to m_Int32ToInt32 and would be copied slowly\n",
				pTableName, nSlow, nWords );
		}
	}

	// The floor is per entity now and applied in operator new, exactly as on
	// the server. See the note there.
}

//-----------------------------------------------------------------------------
// Whether any published class of this shell ARRIVES through that tier.
//
// NOT whether the tier is registered — every one is, so the class list the
// handshake negotiates is the full set. A registered, empty tier is the
// ordinary state and means "room to grow into".
//
// A READING of the published type table since Part 18, because more than one
// tier is live at once now and "iTier == the hierarchy's one tier" cannot say
// so. The mirror of the server's SourceSharp_TierHasPublishedTypes, over this
// module's own copy of the table.
//-----------------------------------------------------------------------------
bool SourceSharp_StandaloneTierIsInUse( int iTier )
{
	RegisterStandaloneTiers();

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() || g_pTierClasses[ iTier ] == NULL )
		return false;

	const CSourceSharpTypeTable &types = SourceSharp_NetTypes();

	for ( int i = 0; i < types.Count(); i++ )
	{
		const SourceSharpNetType_t *pType = types.At( i );

		if ( pType && pType->nShell == SS_SHELL_STANDALONE && pType->iTier == iTier )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// Hands a surface the cracked edges C# decided it should draw.
//
// Resolved through the client's entity list, which is where a handle means
// something on this side. An entity with no surface renderer is not an error:
// the call is made from a managed class, and a managed class does not know
// whether native gave its entity one.
//-----------------------------------------------------------------------------
void SourceSharp_SurfaceSetEdges( uint32_t hEntity, const unsigned char *pEdges, int32_t nCount )
{
	// By ENTRY, not by rebuilding the handle.
	//
	// A CBaseHandle packs an entry and a serial, and reconstructing one from the
	// packed integer means agreeing with the engine about the split — which is
	// one more thing to be wrong about for no benefit. The entry alone names a
	// live entity, and an entity that has gone answers NULL either way.
	const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

	// FROM THE SLOT TABLE, which is why there is no cast here any more. The
	// dynamic_cast this replaces asked the RTTI whether an IClientNetworkable
	// happened to be one of ours; the table KNOWS, because binding is what put
	// the pointer there (§3.16 family 5).
	C_StandaloneEntity *pEntity = SourceSharp_ClientShellFromIndex( nEntry );

	if ( pEntity )
		pEntity->SetSurfaceEdges( pEdges, nCount );
}

//-----------------------------------------------------------------------------
// Where a C#-declared field lives inside the entity, by name.
//
// ASKED OF C#, not looked up in a copy (plan_nettable_generic.md Step 7). This
// used to read native's own record of the field list — shipped over the ABI one
// field at a time by NetFieldBegin/Add/Commit, kept in a per-class vector, and
// refreshed on a managed reload. That record was the last thing the description
// ABI was for, and it was a second copy of a layout C# computes: two copies
// drift, and this one would have drifted silently, because a wrong offset reads
// a plausible number out of the field beside it.
//
// The BLOB BASE is still ours. C# answers where the field sits inside the blob,
// which is the same number on both sides; where the blob sits inside the object
// is each shell's own business, because CStandaloneEntity and C_StandaloneEntity
// are unrelated classes. That is the same split the payload props have always
// had — what the two sides agree on is the word index, never the offset.
//
// -1 for a class or a field this build does not declare, and for a managed side
// that is not up. The caller must handle it: reading offset zero instead would
// be worse than not drawing.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneFieldOffset( const char *pClassName, const char *pFieldName )
{
	if ( !pClassName || !pFieldName )
		return -1;

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	if ( !pStandalone )
		return -1;

	const int nInBlob = pStandalone->NetStateFieldOffset( pClassName, pFieldName );

	if ( nInBlob < 0 )
		return -1;

	return SourceSharp_StandaloneBlobOffset() + nInBlob;
}

//-----------------------------------------------------------------------------
// Where inside that offset's dword the field's bits are: the shift in the low
// byte, the width above it, and -1 for anything this build cannot answer.
//
// THE SECOND HALF OF A PLACEMENT, since Part 10's packing landed. An offset was
// the whole answer while every field took its storage whole; four of
// func_breakable_surf's now share word zero, so a caller that stopped at the
// offset and read a byte would get whichever field starts there. Asked once
// beside the offset, by the same caller, and kept for the life of the process.
//
// Not folded into the offset call because they are two answers and one of them
// is already a composite. A single int carrying a blob offset AND a shift AND a
// width is three fields in thirty-two bits, decoded by hand on both sides of the
// ABI — which is exactly the kind of packing this file's own layout stopped
// doing by hand.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneFieldBits( const char *pClassName, const char *pFieldName )
{
	if ( !pClassName || !pFieldName )
		return -1;

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	if ( !pStandalone )
		return -1;

	return pStandalone->NetStateFieldBits( pClassName, pFieldName );
}

//-----------------------------------------------------------------------------
// The hierarchy's shared blob size, declared by C# before any class on it is
// described — the mirror of the server's, and it must stay one.
//
// The NUMBER is the same on both sides because it comes from the same shared
// declaration list, and so is the TIER it picks, because the rule that turns
// one into the other has one copy in generic_nettable.h. The OFFSETS it turns
// into are not the same: CStandaloneEntity and C_StandaloneEntity are unrelated
// classes with different declared members, so the blob base differs. That is
// fine and always was — the engine's local (listen-server) transfer copies each
// prop from its send offset to its recv offset independently. What must agree across the pair is the word COUNT and the prop
// NAMES.
//-----------------------------------------------------------------------------
int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes )
{
	RegisterStandaloneTiers();

	const int iTier = SourceSharp_TierForSize( nBlobBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] standalone(client): %d declared blob byte(s) exceeds the "
		         "largest tier; the hierarchy has NO table it can use\n", nBlobBytes );
		return -1;
	}

	// PART 18: THIS PICKS NO TIER FOR ANYBODY, on this side either. It is the
	// ceiling check and the log line; each class is sized to its own tier by
	// the create function that makes it.
	const int nWords = SourceSharp_TierWords( iTier );

	Msg( "[SourceSharp] standalone(client): the greediest declaration is %d byte(s) -> tier "
	     "%d '%s' (%d bytes, %d payload word(s)); every OTHER class is sized to its own "
	     "tier, blob base %d\n",
		nBlobBytes, iTier, SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier ),
		SourceSharp_TierBytes( iTier ), nWords, SourceSharp_StandaloneBlobOffset() );

	return nWords;
}

ClientClass *C_StandaloneEntity::GetClientClass()
{
	return m_pClientClass;
}

//-----------------------------------------------------------------------------
// Same window as the server's: the engine collects the class list once, at DLL
// init, and a class created later is not in it.
//-----------------------------------------------------------------------------
#include "shell_clientsystem.h"	// plan_cworld_trace.md T4

class CSourceSharpStandaloneClientSystem : public CClientSystem
{
public:
	CSourceSharpStandaloneClientSystem()
		: CClientSystem( "CSourceSharpStandaloneClientSystem" ) {}

	virtual bool Init() OVERRIDE
	{
		// Unconditional, and it has to agree with the server's decision to be
		// unconditional too. A client that registered these while the server
		// did not would be the same mismatch pointing the other way.
		RegisterStandaloneTiers();
		return true;
	}
};

static CSourceSharpStandaloneClientSystem g_StandaloneClientSystem;

// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_StandaloneClient() { return &g_StandaloneClientSystem; }

//-----------------------------------------------------------------------------
// THE INTERPOLATION LIST -- plan_entity.md step 6a, and the whole of what the
// managed interpolation pass asks this side for.
//
// The engine blocks of every entity the pass may write, and how far behind the
// server this client draws. ONE CALL A PASS: the managed side then reads the
// networked pose out of each block and writes the drawn one back, which is
// arithmetic over memory the two sides already share. That is what keeps the
// step's crossing cost flat in the map's entity count -- a per-entity accessor
// here would be the one thing in the loop that scaled with it.
//
// THE FILTER IS STILL NATIVE, and it is WantsInterpolation(): everything except
// the predicted local player and the local viewmodel, both of whose positions
// are computed rather than received. It stays because the viewmodel bit is
// state that nothing has published yet, and it is named as debt rather than
// defended -- it is a decision about what gets drawn where, so it belongs in
// C#. Step 6f owns the predicted player and Phase 10 the viewmodel.
//
// The vector is a file-scope static rebuilt by each call, so the answer is
// valid until the next one and no allocation happens per frame after the first
// map's worth of entities has sized it.
//-----------------------------------------------------------------------------
static CUtlVector< void * > g_StandaloneInterpBlocks;

void **SourceSharp_ClientInterpBlocks( int32_t *pOutCount, float *pOutInterpAmount )
{
	if ( pOutCount )
		*pOutCount = 0;

	if ( pOutInterpAmount )
		*pOutInterpAmount = 0.0f;

	g_StandaloneInterpBlocks.RemoveAll();

	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
	{
		if ( g_StandaloneClients[i]->WantsInterpolation() )
			g_StandaloneInterpBlocks.AddToTail( &g_StandaloneClients[i]->Block() );
	}

	if ( g_StandaloneInterpBlocks.Count() == 0 )
		return NULL;

	if ( pOutCount )
		*pOutCount = g_StandaloneInterpBlocks.Count();

	if ( pOutInterpAmount )
		*pOutInterpAmount = StandaloneInterpolationAmount();

	return g_StandaloneInterpBlocks.Base();
}

//-----------------------------------------------------------------------------
// What happens to every entity AFTER the frame's interpolation has moved it.
//
// PreRender rather than Update, and the difference matters: Update runs with
// the frame's own timing and PreRender runs immediately before the world is
// drawn, which is the point at which "where is this entity now" has to be
// answered.
//
// THE ARITHMETIC MOVED AND THIS DID NOT (step 6a). The managed pass runs one
// step earlier, at FRAME_NET_UPDATE_POSTDATAUPDATE_END for the samples and
// FRAME_RENDER_START for the poses -- which is where stock interpolates its own
// entities, through OnRenderStart -> C_BaseEntity::InterpolateServerEntities.
// This walk still runs where it always did, so the transform rebuild and the
// leaf-system notify happen at exactly the moment in the frame they used to.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE RENDER-POLICY LIST -- plan_entity.md step 6b, and the mirror of 6a's.
//
// EVERY standalone client entity's engine block, unfiltered, and that is the
// one difference from SourceSharp_ClientInterpBlocks above. That list is
// filtered by WantsInterpolation(), which excludes the viewmodel and the
// predicted local player -- and the viewmodel is precisely the entity whose
// render group, draw answer and shadow answer differ most from everything
// else's. A policy pass over the interpolation list would decide the policy of
// every entity except the ones that need deciding.
//
// ONE CALL A PASS, for 6a's reason: a per-entity accessor would put a call
// across the boundary in a loop that runs over every entity every frame, and
// the step's cost would scale with the map's entity count.
//
// The vector is a file-scope static rebuilt by each call, so the answer is
// valid until the next one and no allocation happens per frame after the first
// map's worth of entities has sized it.
//-----------------------------------------------------------------------------
static CUtlVector< void * > g_StandalonePolicyBlocks;

void **SourceSharp_ClientPolicyBlocks( int32_t *pOutCount )
{
	if ( pOutCount )
		*pOutCount = 0;

	g_StandalonePolicyBlocks.RemoveAll();

	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
		g_StandalonePolicyBlocks.AddToTail( &g_StandaloneClients[i]->Block() );

	if ( g_StandalonePolicyBlocks.Count() == 0 )
		return NULL;

	if ( pOutCount )
		*pOutCount = g_StandalonePolicyBlocks.Count();

	return g_StandalonePolicyBlocks.Base();
}

//-----------------------------------------------------------------------------
// WHAT THE SHELL DOES WITH A DECISION IT HAS BEEN HANDED.
//
// "The shell's leaf and shadow calls become block-driven" (step 6b's gate) is
// this function. The leaf system and the client shadow manager are engine
// mechanism -- they own lists the shell has to keep in step -- so telling them
// stays native; deciding what to tell them does not, and does not happen here.
//
// EDGE-TRIGGERED OFF A SERIAL, so it is not per-frame work. Managed code bumps
// Client.PolicyOutputs when and only when it writes new choices; an entity
// whose choices have not moved costs one load and one compare. That is what
// makes §3.9's "written when its inputs change, not per frame" true of the
// whole loop and not just of the half that runs in C#.
//
// THE SERIAL IS COMPARED, NOT TESTED FOR NON-ZERO. A block whose memory was
// recycled carries whatever the last entity in that slot left, and the managed
// side's serial is a counter of its own rather than an increment of what it
// read -- so the pair can never coincide on a value this shell has already
// applied for a different entity.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::ApplyRenderPolicy()
{
	if ( Block().Client.PolicyOutputs == m_nAppliedPolicy )
		return;

	m_nAppliedPolicy = Block().Client.PolicyOutputs;

	// In or out first, because the two calls below are about a renderable that
	// is IN. UpdateVisibility adds through AddToLeafSystem, which sets the
	// group and refreshes the shadow, so an entity that has just started
	// drawing is fully done by the time this returns.
	const bool bWasIn = IsInLeafSystem();

	UpdateVisibility();

	if ( !IsInLeafSystem() || !bWasIn )
		return;

	// Still in, and was already: the group may have moved between the leaves
	// and the viewmodel list, and the shadow may have appeared or gone.
	AddToLeafSystem();
}

static void StandaloneClients_ApplyRenderPolicy()
{
	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
		g_StandaloneClients[i]->ApplyRenderPolicy();
}

static void StandaloneClients_PostInterpolate()
{
	const float flCurrentTime = gpGlobals->curtime;

	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
	{
		if ( g_StandaloneClients[i]->WantsInterpolation() )
			g_StandaloneClients[i]->PostInterpolate( flCurrentTime );
	}
}

class CSourceSharpStandaloneInterpSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpStandaloneInterpSystem()
		: CClientSystemPerFrame( "CSourceSharpStandaloneInterpSystem" ) {}

	virtual void PreRender() OVERRIDE
	{
		// POLICY FIRST, POSE SECOND, and the order is load-bearing. The managed
		// policy pass ran one stage ago, at FRAME_NET_UPDATE_POSTDATAUPDATE_END;
		// this hands its answers to the leaf system, which is where an entity
		// that has just started drawing is INSERTED. PostInterpolate then tells
		// the leaf system the renderable moved -- a call that does nothing for a
		// renderable not yet in it.
		StandaloneClients_ApplyRenderPolicy();

		StandaloneClients_PostInterpolate();
	}

	// THE LIST DOES NOT OUTLIVE A MAP. It holds raw block pointers into shells
	// the level shutdown deletes, and a pass that ran between the delete and the
	// next rebuild would read freed memory. Emptied here rather than trusted to
	// be rebuilt first.
	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		g_StandaloneInterpBlocks.Purge();
		g_StandalonePolicyBlocks.Purge();
	}
};

static CSourceSharpStandaloneInterpSystem g_StandaloneInterpSystem;

// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_StandaloneInterp() { return &g_StandaloneInterpSystem; }

//-----------------------------------------------------------------------------
// An entity that draws itself from managed code.
//
// Two things at once, deliberately: the flag that makes ShouldDraw say yes, and
// the box the leaf system culls by. They are set together because they are
// learnt together — a class knows it draws, and knows what it occupies, at the
// same moment and from the same state.
//
// Pushed rather than asked for because GetRenderBounds is called every frame
// while the bounds change only when the thing being drawn does.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneSetCustomDraw( uint32_t hEntity, const float *pMinsXYZ,
                                          const float *pMaxsXYZ )
{
	if ( !pMinsXYZ || !pMaxsXYZ )
		return;

	// By ENTRY, as SourceSharp_SurfaceSetEdges above explains.
	const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

	// FROM THE SLOT TABLE, which is why there is no cast here any more. The
	// dynamic_cast this replaces asked the RTTI whether an IClientNetworkable
	// happened to be one of ours; the table KNOWS, because binding is what put
	// the pointer there (§3.16 family 5).
	C_StandaloneEntity *pEntity = SourceSharp_ClientShellFromIndex( nEntry );

	if ( !pEntity )
		return;

	pEntity->SetCustomDraw( Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ),
	                        Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ) );
}

//-----------------------------------------------------------------------------
// One dynamic light, refreshed.
//
// A dlight is a light the renderer applies at draw time, out of a small fixed
// pool the engine owns. What C# decides is everything ABOUT one -- where it is,
// what colour, how far it reaches, whether it is a cone, whether it lights the
// world or only models -- and none of that is renderer detail. What lives here
// is the pool.
//
// CL_AllocDlight is keyed and reuses the slot for a key it has already seen, so
// asking again with the same key MOVES a light rather than adding another. That
// is the whole reason the key is in the struct: without it a light that follows
// something would leave one behind every frame until the pool was full.
//
// Elights are a separate pool and a separate call. They light models and never
// the world, which is much cheaper, and stock switches to them for exactly the
// lights that had asked for no world illumination.
//-----------------------------------------------------------------------------
void SourceSharp_DynamicLightUpdate( const SourceSharpDynamicLight *pLight )
{
	if ( !pLight || !effects )
		return;

	dlight_t *dl = pLight->IsElight
		? effects->CL_AllocElight( pLight->Key )
		: effects->CL_AllocDlight( pLight->Key );

	if ( !dl )
		return;

	dl->origin.Init( pLight->OriginXYZ[0], pLight->OriginXYZ[1], pLight->OriginXYZ[2] );
	dl->m_Direction.Init( pLight->DirectionXYZ[0], pLight->DirectionXYZ[1],
	                      pLight->DirectionXYZ[2] );

	dl->radius = pLight->Radius;

	dl->color.r        = (unsigned char)clamp( pLight->Red,   0, 255 );
	dl->color.g        = (unsigned char)clamp( pLight->Green, 0, 255 );
	dl->color.b        = (unsigned char)clamp( pLight->Blue,  0, 255 );
	dl->color.exponent = (signed char)clamp( pLight->Exponent, -127, 127 );

	dl->style       = (unsigned char)clamp( pLight->Style, 0, 63 );
	dl->flags       = (unsigned char)pLight->Flags;
	dl->m_InnerAngle = pLight->InnerAngle;
	dl->m_OuterAngle = pLight->OuterAngle;
	dl->minlight    = 0;

	dl->die = gpGlobals->curtime + pLight->LiveFor;

	// HOW FAST IT FADES, and it arrived with the struct at C-Phase 3.
	//
	// Every light this ABI carried before then is REFRESHED every frame by
	// whatever owns it, and a light rewritten sixty times a second cannot decay
	// -- so this was left alone and dlight_t's own zero stood. DT_TEDynamicLight
	// is the first one that is placed and left: c_te_dynamiclight.cpp asks the
	// pool once and sets die and decay together off the wire. Setting it
	// unconditionally is what makes the temp entity light FADE instead of
	// holding full brightness and vanishing, and it changes nothing for the
	// refreshed lights, which pass zero -- which is what CL_AllocDlight already
	// hands back on a fresh slot.
	dl->decay = pLight->Decay;
}

//-----------------------------------------------------------------------------
// Puts a light out now.
//
// Its own call rather than "stop refreshing it", because the light keeps
// burning for whatever its last LiveFor said -- and an entity that has been
// removed wants its light gone in the same frame rather than a tenth of a
// second later.
//
// Both pools are asked, because this side does not track which one a key went
// into and CL_AllocDlight on a key that is not lit simply hands back a free
// slot that then expires immediately. Cheap, and it cannot leave one lit.
//-----------------------------------------------------------------------------
void SourceSharp_DynamicLightRelease( int32_t nKey )
{
	if ( !effects )
		return;

	if ( dlight_t *dl = effects->CL_AllocDlight( nKey ) )
		dl->die = gpGlobals->curtime;

	if ( dlight_t *el = effects->CL_AllocElight( nKey ) )
		el->die = gpGlobals->curtime;
}

//-----------------------------------------------------------------------------
// Which fade rule this entity follows, and over what band.
//
// Pushed, not asked for: what an entity fades AT changes when the server says
// so, while the fade itself is read on the render path every frame.
//
// Re-inserting into the leaf system is the part that is easy to miss. Which
// list a renderable is in is decided when it is added, so an entity that
// becomes translucent after it was added stays in the opaque list and is drawn
// in the wrong pass -- which shows as the world disappearing behind a
// half-faded brush rather than as anything to do with alpha.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// WHETHER THE SOLVER OWNS THIS ENTITY'S POSE. The client's ragdolls are
// SourceSharp.Client's (plan_physics_bones_native_zero Phase 3): creation, the
// solver step, the tail write and the teardown are managed's, and this shell
// keeps the one fact two of its render reads need, which managed sets through
// StandaloneSetRagdoll.
//-----------------------------------------------------------------------------
bool C_StandaloneEntity::IsRagdoll() const
{
	return m_bRagdollPose;
}

void C_StandaloneEntity::SetRagdollPose( bool bRagdoll )
{
	m_bRagdollPose = bRagdoll;

	// The render box is the body's from here, and the leaf system holds the
	// one the entity was standing in.
	if ( bRagdoll )
		AddToLeafSystem();
}

//-----------------------------------------------------------------------------
// How big this entity wears its model. The client's half of the server's
// CStandaloneEntity::SetModelScale, guard for guard.
//
// The bone cache goes with it, and here that is doing more work than it looks
// like: the scale is in BoneCacheKey_t, but a prop that has settled changes
// nothing else in that key ever again, so without this line a crate told to
// grow would keep the skeleton it was built with until it moved — which for a
// resting prop is for the rest of the map.
//
// Also the LEAF SYSTEM, which SetFade's own comment explains the general shape
// of: which leaves a renderable is in is decided from its bounds, and
// GetRenderBounds now multiplies by this. A model that doubles in size without
// re-inserting keeps the extent it was added with, and the symptom is a big
// prop that disappears when its OLD box leaves the view.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::SetModelScale( float flScale )
{
	if ( flScale <= 0.0f )
	{
		Warning( "[SourceSharp] model scale %f refused on entity %d: a scale of zero "
		         "or less is a model drawn as nothing and picked by nothing\n",
			flScale, entindex() );
		return;
	}

	if ( flScale == m_flModelScale )
		return;

	// A store: the managed skeleton decided (ClientSkeletons.SetModelScale)
	// and rebuilds at this size; what native keeps is the number its render
	// bounds read.
	m_flModelScale = flScale;

	AddToLeafSystem();
}

void C_StandaloneEntity::SetFade( int nStyle, float flStartDist, float flEndDist,
                                  float flMinAlpha )
{
	// INTO THE BLOCK, AND NOTHING ELSE (plan_entity.md step 6b). The
	// AddToLeafSystem that used to hang off "did it start or stop fading" was
	// the shell noticing that the render GROUP had changed -- which is now the
	// managed policy's to notice, out of these same four values, and the apply
	// sweep's to act on. Four members left the shell for the block with this.
	EngineBlockClientTail_t &tail = Block().Client;

	tail.FadeStyle         = nStyle;
	tail.FadeStartDistance = flStartDist;
	tail.FadeEndDistance   = flEndDist;
	tail.FadeMinAlpha      = flMinAlpha;

	BumpPolicyInputs();
}

//-----------------------------------------------------------------------------
// This frame's alpha, from the distance to the viewer.
//
// Two rules, because stock has two and they are not variations on one another.
// Neither is reimplemented from first principles -- each is the arithmetic of
// the function it came from, transcribed because both take a C_BaseEntity and
// nothing on this hierarchy is one:
//
//   Lod          ComputeDistanceFade in cdll_util.cpp. Ramps on SQUARED
//                distance to the entity's centre, 255 down to 0. The squared
//                ramp is why a func_lod holds its opacity for most of the band
//                and then goes quickly; a linear ramp would be a visibly
//                different entity.
//
//   PortalWindow C_FuncAreaPortalWindow::GetDistanceBlend. Ramps LINEARLY on
//                distance to the nearest face, and upward: most transparent up
//                close, solid once the viewer has backed away. Face distance
//                rather than centre because a window is a pane a room wide and
//                what matters is how far you are from the glass.
//
// What is NOT carried across from UTIL_ComputeEntityFade is the screen-size
// fades it also folds in. Those are driven by a per-model fade scale the level
// designer set, and nothing on this hierarchy carries one.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::ComputeFxBlend( void )
{
	const EngineBlockClientTail_t &fade = Block().Client;

	if ( fade.FadeStyle == 0 )
	{
		// The constant one SetBlending pushed, 255 for everything that has not
		// asked. It is renderamt for a sprite, and a fade rule -- when there is
		// one -- replaces it rather than multiplying with it: stock's
		// GetFxBlend answers one number, and two rules writing it would be one
		// of them winning silently.
		Block().Client.FxBlend = Block().Client.RenderColorA;
		return;
	}

	Vector vecMins, vecMaxs;
	WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );

	float flDist;

	if ( fade.FadeStyle == 2 )
	{
		flDist = CalcDistanceToAABB( vecMins, vecMaxs, CurrentViewOrigin() );
	}
	else
	{
		Vector vecCenter;
		VectorAdd( vecMins, vecMaxs, vecCenter );
		vecCenter *= 0.5f;

		flDist = CurrentViewOrigin().DistTo( vecCenter );
	}

	// The zoom correction both stock functions apply is skipped: the FOV
	// factor lived on C_BasePlayer, the standalone player has no zoom yet,
	// and the factor for an unzoomed player is exactly 1. When zoom lands on
	// the standalone player's declared state, this is where it multiplies in.

	if ( fade.FadeStyle == 2 )
	{
		const float flBlend = RemapValClamped( flDist, fade.FadeStartDistance, fade.FadeEndDistance,
		                                       fade.FadeMinAlpha, 1.0f );

		Block().Client.FxBlend = clamp( (int)( flBlend * 255.0f + 0.5f ), 0, 255 );
		return;
	}

	float flMinDist = fade.FadeStartDistance;
	float flMaxDist = fade.FadeEndDistance;

	if ( flMinDist > flMaxDist )
		::V_swap( flMinDist, flMaxDist );

	// Squared, as stock is, so the ramp is on squared distance too. That is not
	// a linear fade in world units and is deliberately not made into one.
	flMinDist *= flMinDist;
	flMaxDist *= flMaxDist;

	const float flDistSq = flDist * flDist;

	if ( flDistSq <= flMinDist )
	{
		Block().Client.FxBlend = 255;
		return;
	}

	if ( flDistSq >= flMaxDist || flMaxDist <= flMinDist )
	{
		Block().Client.FxBlend = ( flMaxDist <= flMinDist ) ? 255 : 0;
		return;
	}

	const float flFalloff = 255.0f / ( flMaxDist - flMinDist );
	Block().Client.FxBlend = clamp( (int)( flFalloff * ( flMaxDist - flDistSq ) ), 0, 255 );
}

//-----------------------------------------------------------------------------
// The fade rule and the second model, from managed code. Both resolve the way
// every other per-entity client call in this file does.
//-----------------------------------------------------------------------------
C_StandaloneEntity *SourceSharp_StandaloneFromHandle( uint32_t hEntity )
{
	const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

	// The slot table, not the RTTI -- see the two sites above.
	return SourceSharp_ClientShellFromIndex( nEntry );
}

static C_StandaloneEntity *ResolveStandaloneClient( uint32_t hEntity )
{
	return SourceSharp_StandaloneFromHandle( hEntity );
}

//-----------------------------------------------------------------------------
// THIS SIDE'S ANSWER TO "what index is this entity", which had none.
//
// EntityGetIndex was wired under #ifndef CLIENT_DLL only, so the slot was NULL
// over here and EntityHandle.Index answered -1 for every client entity. That is
// not a cosmetic gap: ClientStandaloneRegistry records an instance in its
// index map only `if ( index >= 0 )`, so the map was ALWAYS EMPTY and
// IClientStandalones.GetByIndex never matched anything. Everything built on it
// silently took its miss branch -- a corpse looking up the player it came from
// found nobody and collapsed from a generic idle instead of the pose they died
// in, with nothing anywhere saying so.
//
// The index is already in the handle -- SourceSharp_StandaloneFromHandle masks
// it out to do the lookup -- but it is read off the ENTITY here rather than
// masked out again, so that a handle naming something this side does not have
// answers -1 instead of an index it cannot honour.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientEntityGetIndex( uint32_t hEntity )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		return pEntity->entindex();

	return -1;
}

void SourceSharp_StandaloneSetFade( uint32_t hEntity, int32_t nStyle, float flStartDist,
                                    float flEndDist, float flMinAlpha )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetFade( nStyle, flStartDist, flEndDist, flMinAlpha );
}

void SourceSharp_StandaloneSetOverlayModel( uint32_t hEntity, int32_t nModelIndex )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetOverlayModel( nModelIndex );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S NATIVE BONE SLOTS ARE RETIRED (plan physics_bones_native_zero
// 1.2, client half). The IShimModels / IShimStandaloneClient bodies that
// answered managed's bone questions out of the native skeleton -- the merge
// source, the attachments, the bone count, the pose parameters, the sequence
// and its layers, the ground speed, the procedural counts, the matrices, the
// subject tests -- went with their rows: ClientSkeletons answers every one
// through the engine bindings' skeleton route.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// How this instance wears its model: which skin, and at what size.
//
// TWO SLOTS, and neither of them is new in the way it first looks.
//
// The SKIN reuses EntitySetSkin, which has existed since env_beverage wanted a
// soda can in six colours. It had one implementation, on the server, and that
// implementation writes m_nSkin on a native CBaseAnimating -- STOCK's member,
// which is a different thing from this shell's skin and always was; no
// entity on this hierarchy has. So the slot was not missing, it was INERT here,
// and the managed call that reached it read as working. This is its client arm.
//
// The SCALE is a new slot and is asked of BOTH sides, because the server builds
// a skeleton too and scaled_collision_rule.h reads the number. See
// StandaloneSetModelScale in sourcesharp.h.
//
// PUSHED rather than networked as props of their own, which is the decision
// worth recording. The obvious shape is two more SendProps in
// standalone_nettable.h — and that is two more props on EVERY entity on this
// hierarchy, forever, to serve the handful that are ever scaled or reskinned.
// The payload already carries a managed class's declared state, so a class that
// wants a skin declares one and it is on the wire for nothing; what was
// missing was only somewhere for the value to LAND. This is that somewhere,
// and it is the same road SetFade and SetOverlayModel already take.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Managed's ragdolls own this entity's pose now, or no longer do (plan
// physics_bones_native_zero Phase 3). A store and a re-leaf; no policy.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneSetRagdoll( uint32_t hEntity, int32_t bRagdoll )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetRagdollPose( bRagdoll != 0 );
}

void SourceSharp_EntitySetSkin( uint32_t hEntity, int32_t nSkin )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetSkin( (int)nSkin );
}

void SourceSharp_StandaloneSetModelScale( uint32_t hEntity, float flScale )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetModelScale( flScale );
}

//-----------------------------------------------------------------------------
// The client's half of EntityGetModelName, which had none.
//
// The slot was wired inside the gamesystem's `#ifndef CLIENT_DLL`, so on this
// side the pointer was null and Engine.EntityGetModelName answered an empty
// string to everything -- silently, because every managed call is guarded on
// null. Found the way that trap is always found: a report that named a model
// for every entity said "(no model)" for all 193 of them, including the ones
// visibly wearing one.
//
// The name comes from the MODEL rather than from a stored string, because that
// is all this side has: the client is told a model INDEX and resolves it, and
// there is no keyvalue here for it to have kept.
//-----------------------------------------------------------------------------
void SourceSharp_ClientEntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';

	C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity );

	if ( !pEntity )
		return;

	if ( const model_t *pModel = pEntity->GetModel() )
		Q_strncpy( pOut, modelinfo->GetModelName( pModel ), nOutSize );
}

//-----------------------------------------------------------------------------
// SourceSharp_StandaloneAttachmentCount IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_StandaloneAttachmentName IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Dormant, so that a report can say so.
//
// A handle that resolves to nothing answers 0 — NOT dormant — because absent
// and stale are different things and the caller learns about absence from the
// entity not being in the listing at all.
//-----------------------------------------------------------------------------
int32_t SourceSharp_StandaloneIsDormant( uint32_t hEntity )
{
	C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity );

	return ( pEntity && pEntity->IsDormant() ) ? 1 : 0;
}

//-----------------------------------------------------------------------------
// Every live entity of a given classname, as renderables.
//
// The one thing on this side that has to be asked by CLASS rather than by
// handle, and the reason is not this hierarchy's: the client renderer keeps
// per-kind lists of entities it has to sweep before drawing a frame — the
// reflective glass list is one, and it is walked once per view to decide
// whether a reflection pass is needed at all. A stock entity joins such a list
// in its constructor, by being of the right C++ type. Nothing here has a type
// to be of, so the sweep has to be able to ask.
//
// Filled into a caller-supplied array rather than answered as a list, so that
// nothing here allocates on a path the renderer runs per view.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneGetRenderables( const char *pClassName,
                                          IClientRenderable **pOut, int nMax )
{
	if ( !pClassName || !pOut || nMax <= 0 )
		return 0;

	int nFound = 0;

	for ( int i = 0; i < g_StandaloneClients.Count() && nFound < nMax; i++ )
	{
		C_StandaloneEntity *pEntity = g_StandaloneClients[i];

		if ( !pEntity || pEntity->IsDormant() )
			continue;

		if ( Q_stricmp( pEntity->m_szClassname, pClassName ) != 0 )
			continue;

		// No model means nothing to reflect in, trace against, or draw. Every
		// caller of this would have to make the same check.
		if ( !pEntity->GetModel() )
			continue;

		pOut[ nFound++ ] = pEntity;
	}

	return nFound;
}

//-----------------------------------------------------------------------------
// The list itself, for the one walker that is not per classname: the
// prediction wiring (shell_prediction.cpp, Phase 9c1) sweeps every hierarchy
// entity once per prediction update to find the ones whose class is tracked.
// Count/At rather than handing out the vector, so the list stays owned here.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneClientCount()
{
	return g_StandaloneClients.Count();
}

C_StandaloneEntity *SourceSharp_StandaloneClientAt( int nIndex )
{
	if ( nIndex < 0 || nIndex >= g_StandaloneClients.Count() )
		return NULL;

	return g_StandaloneClients[ nIndex ];
}

//-----------------------------------------------------------------------------
// THE HIERARCHY'S LEVEL-SHUTDOWN TEARDOWN, AND NOTHING ELSE DESTROYS A SHELL.
//
// WHY THIS FUNCTION EXISTS, and it is a measurement rather than a tidy-up.
// Until it did, the only thing that tore a C_StandaloneEntity down at level
// shutdown was CClientEntityList::Release() walking the STOCK list and calling
// Release() on every row -- and a shell is only in that list because
// SourceSharp_ClientEntityBind puts it there through
// SourceSharp_LegacyListBind. The flip's client removal set takes
// game/client/cliententitylist.cpp with it, so armed there is no stock list, no
// walk, and no teardown.
//
// Lane `f-list` deleted that bind before this function existed and ran the
// game: SEGFAULT inside the client shadow manager
// (clientshadowmgr.cpp:1977, "Assertion Failed: m_Shadows.IsValidIndex(handle)")
// on the SECOND map load, 0 of 5 runs, against 3 of 3 passing on the untouched
// tip in the same two hours on the same box. Every shell survived the level
// change holding leaf-system and shadow handles into subsystems the shutdown
// had just reset, and the next map double-destroyed a shadow. TWO static
// instruments -- the linker's undefined list and an nm sweep of all 272 kept
// objects -- had both said the work was finished.
//
// THE SERVER'S TWIN IS StandaloneEntities_LevelShutdown (shell_standalone_base.cpp)
// and this is deliberately the same shape rather than a second design: the same
// registry-of-every-live-shell, walked from the END because the destructor
// removes the entity from the list being walked, and a RemoveAll after it.
//
// Release() AND NOT `delete`, and that is the one place the two differ. Release
// is the virtual the stock list's walk called -- C_StandaloneEntity::Release is
// `delete this` -- so this is the SAME destruction spelling arriving from a new
// caller rather than a second spelling of it. Two spellings of one destruction
// is exactly how the server's world release became a silent no-op and cost a
// SIGSEGV on the second map load for weeks (world_release_test.cpp).
//
// IT RUNS IN BOTH CONFIGURATIONS, on purpose. Unarmed it runs BEFORE the stock
// list's Release, so it destroys every shell itself and the stock walk finds
// none of ours left -- which means the SHIPPING build exercises this path every
// level change, and the armed build changes only what is left for the stock
// walk to do (nothing). A teardown that only ran armed would be a teardown no
// live gate could reach until the whole flip landed.
//
// THE COUNT IS RETURNED because the caller asserts on it: "the teardown
// destroyed N shells" is a positive reading, and an absence -- no crash, a
// census that happens to be zero -- is the thing this port has been burned by.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneClientsLevelShutdown()
{
	const int nDestroyed = g_StandaloneClients.Count();

	// By index from the end: ~C_StandaloneEntity calls FindAndFastRemove on
	// this list, which moves the tail into the vacated slot -- and walking down
	// from the tail is the one direction for which that is always the element
	// just freed. A forward walk would skip whatever slid into the slot.
	for ( int i = g_StandaloneClients.Count() - 1; i >= 0; i-- )
	{
		if ( C_StandaloneEntity *pShell = g_StandaloneClients[ i ] )
			pShell->Release();
	}

	// Belt and braces, exactly as the server's twin keeps it: if any destructor
	// failed to pull itself out, the pointer left behind is dangling and the
	// next level's first walk would follow it.
	g_StandaloneClients.RemoveAll();

	// The two per-frame block arrays hold BARE POINTERS INTO the shells that
	// have just been freed (&entity->Block()), and they are rebuilt from the
	// list each frame -- so a frame that ran between here and the next level's
	// first rebuild would read freed memory. The interp system purges them at
	// LevelShutdownPostEntity, which is AFTER this; emptying them here is what
	// makes the window empty rather than short.
	g_StandaloneInterpBlocks.RemoveAll();
	g_StandalonePolicyBlocks.RemoveAll();

	return nDestroyed;
}

//-----------------------------------------------------------------------------
// THE MODEL-CACHE FLUSH, OVER THIS HIERARCHY -- lane diag-visibility, and the
// parallel half of what shell_mdlcache.cpp already does on the server.
//
// WHAT WAS MEASURED. CShellClientDLL::InvalidateMdlCache walked
// ClientEntityList().FirstBaseEntity()/NextBaseEntity(), which answers
// C_BaseEntity. C_StandaloneEntity is
// `: public IClientEntity, public ICollideable, public IGameEntity` -- NOT a
// C_BaseEntity -- so that walk never yielded a single shell and the flush
// reached ZERO managed entities. Every m_CachedBones on this side survived a
// model-cache flush, holding matrices built from a studiohdr_t mdlcache had
// freed. Reachable from `flush` and `flush_locked` (both FCVAR_CHEAT) and
// from the engine's model reload.
//
// ONLY THIS HALF LIVES HERE, and the split is not arbitrary. The SDK half needs
// ClientEntityList(), and client_entity_resolve_test.cpp's clause-2 gate says no
// compiled TU may name the stock client list -- with shell_clientdll.cpp waived
// by name for exactly this ("ITERATION AND RELEASE, not a lookup ... these four
// cannot be routed through it and leave when the list does"). Writing the whole
// walk here would have meant adding a SECOND file to that waiver list, which is
// widening a gate to fit a change rather than fitting the change to the gate.
// So the SDK half stays where it already was and where it is already excused,
// and this function is what it calls.
//
// IT ANSWERS HOW MANY, and that is the whole reason the slot's return type
// changed from void: the engine flushing its cache is invisible from outside,
// so an empty body and a working one were indistinguishable -- which is exactly
// the shape of defect this lane exists to close. C# prints the number.
//-----------------------------------------------------------------------------
int32_t SourceSharp_StandaloneClientsInvalidateBoneCaches( void )
{
	// THE CACHE IS MANAGED NOW (plan physics_bones_native_zero 1.2, client
	// half): ClientSkeletons drops its model images and invalidates every
	// skeleton from the managed module's InvalidateMdlCache, and counts them
	// itself. Nothing on this side holds a matrix built from the studio data
	// being flushed.
	return 0;
}

void SourceSharp_ActivateOccluder( int32_t nOccluderIndex, int32_t bActive )
{
	if ( nOccluderIndex < 0 )
		return;

	engine->ActivateOccluder( nOccluderIndex, bActive != 0 );
}

void C_StandaloneEntity::SetCustomDraw( const Vector &mins, const Vector &maxs )
{
	Block().Client.CustomDraw  = 1;
	Block().Client.RenderMins  = mins;
	Block().Client.RenderMaxs  = maxs;

	// The "first time only" AddToLeafSystem that used to be here is gone with
	// the flag: whether an entity with no model is DRAWN is exactly what
	// ClientRenderPolicy.Drawn decides from this bit, and the apply sweep is
	// what puts it in the leaf system on the answer. Repeated calls -- which
	// this has, because bounds change while the flag does not -- cost one
	// serial bump and one skipped decision.
	BumpPolicyInputs();
}

//-----------------------------------------------------------------------------
// How an entity that draws ITSELF is sorted, blended and depth-tested.
//
// The group is chosen when the renderable is INSERTED, so a change to it has to
// re-insert -- the same rule SetFade already follows, and the reason both call
// AddToLeafSystem rather than only setting a field. IgnoresZBuffer and the
// alpha are read per frame and need no re-insertion.
//-----------------------------------------------------------------------------
void C_StandaloneEntity::SetBlending( bool bTranslucent, bool bIgnoreZ, int nAlpha )
{
	Block().Client.Translucent = bTranslucent ? 1 : 0;
	m_bIgnoreZ     = bIgnoreZ;
	// Clamped and then NARROWED DELIBERATELY: the block's colour is four bytes,
	// which is the engine's color32 and not the managed Color32 (four ints).
	// The cast is written out so the narrowing is a decision rather than a
	// warning somebody turned off.
	Block().Client.RenderColorA = (uint8_t)clamp( nAlpha, 0, 255 );

	// The group and the transparency both fall out of this bit, and both are
	// the policy's. See SetFade for why the re-insert is the sweep's job now.
	BumpPolicyInputs();
}

void SourceSharp_StandaloneSetBlending( uint32_t hEntity, int32_t bTranslucent,
                                        int32_t bIgnoreZ, int32_t nAlpha )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->SetBlending( bTranslucent != 0, bIgnoreZ != 0, nAlpha );
}

//-----------------------------------------------------------------------------
// Whether this entity casts a shadow. See StandaloneSetShadowCast in
// sourcesharp.h and ClientRenderPolicy.Shadow.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneSetShadowCast( uint32_t hEntity, int32_t nChoice )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->SetShadowCast( (int)nChoice );
}

//-----------------------------------------------------------------------------
// What it is casting, and what it was ASKED to cast. Two answers because they
// are two different facts and the pair is what names a problem: "auto" plus
// "casting nothing" is a model with nothing to cast with, while "simple" plus
// "casting nothing" is an entity that is not being drawn at all.
//
// The third fact is whether the client shadow manager actually holds a handle,
// which is the one the Phase 5 gate would have answered instantly: the shell's
// ShadowCastType() said SHADOWS_SIMPLE for a year while the manager held
// nothing, and no report anywhere could tell the two apart.
//-----------------------------------------------------------------------------
int32_t SourceSharp_StandaloneShadowCast( uint32_t hEntity, int32_t *pOutChoice,
                                          int32_t *pOutHasShadow )
{
	if ( pOutChoice )
		*pOutChoice = STANDALONE_SHADOW_CHOICE_AUTO;

	if ( pOutHasShadow )
		*pOutHasShadow = 0;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return -1;

	if ( pOutChoice )
		*pOutChoice = pEntity->ShadowCastChoice();

	if ( pOutHasShadow )
		*pOutHasShadow = pEntity->HasShadow() ? 1 : 0;

	return (int32_t)pEntity->ShadowCastType();
}

//-----------------------------------------------------------------------------
// A client entity's ORIGIN, for the slot the server has always filled and this
// side never did -- the twin of the angles below, and the last of the pair.
//
// The hole was KNOWN and WAIVED rather than merely silent: SideAuditTests
// carried "IEngineEntities.GetOrigin is backed by Server-only slot
// 'EntityGetOrigin'" with the cost written beside it -- every color_correction
// measuring its falloff from the world origin and every spotlight_end pooling
// its light at the map origin, because C# asking a client entity where it is
// got (0,0,0) and no error.
//
// It also lays a trap for anything DIAGNOSING with it, which is how this came
// back into view: a zero answer over here is indistinguishable from an entity
// that really is at the map origin, and reads exactly like one.
//
// GetAbsOrigin, matching the angles below and ClientEntityOrigin's own read.
//-----------------------------------------------------------------------------
void SourceSharp_ClientEntityGetOrigin( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
	{
		const Vector &vecOrigin = pEntity->GetAbsOrigin();

		pOutXYZ[0] = vecOrigin.x;
		pOutXYZ[1] = vecOrigin.y;
		pOutXYZ[2] = vecOrigin.z;
	}
}

//-----------------------------------------------------------------------------
// A client entity's angles, for the slot the server has always filled and this
// side never did.
//
// The hole was silent in the exact way AbiParityTests warns about: the slot is
// declared, Engine.cs guards the call on NULL, and C# asking a client entity
// for its angles got (0,0,0) with no error. For the player's animation that is
// a body permanently facing world north — every strafe blend picking its
// direction relative to a facing nobody has.
//-----------------------------------------------------------------------------
void SourceSharp_ClientEntityGetAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
	{
		const QAngle &ang = pEntity->GetAbsAngles();

		pOutPYR[0] = ang.x;
		pOutPYR[1] = ang.y;
		pOutPYR[2] = ang.z;
	}
}

//-----------------------------------------------------------------------------
// SourceSharp_EntitySequenceDuration IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------


//-----------------------------------------------------------------------------
// SourceSharp_EntitySequenceEventCount IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_EntitySequenceEvent IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//=============================================================================//
// The viewmodel pass and the playing sequence (Phase 10's render half).
//
// Phase 10 proved the STATE right — an smg1 burst held byte-for-byte across
// tens of thousands of ring saves — and stopped short of the render: the
// viewmodel entity existed, predicted, and drew nothing on purpose. These are
// the two mechanisms the render was missing. Policy stays managed: C# decides
// WHICH entity is the local viewmodel and WHAT sequence it plays; native
// supplies the pass, the eye pin and the clock, because those are the
// renderer's and the renderer is the engine's.
//=============================================================================//

void C_StandaloneEntity::SetViewModelPass( bool bEnable )
{
	if ( ( Block().Client.ViewModel != 0 ) == bEnable )
		return;

	Block().Client.ViewModel = bEnable ? 1 : 0;

	if ( bEnable )
	{
		// The draw-nothing custom draw was the placeholder for exactly this
		// render; the real one starts where it ends.
		Block().Client.CustomDraw = 0;
		s_pLocalViewModel = this;
	}
	else if ( s_pLocalViewModel == this )
	{
		s_pLocalViewModel = NULL;
	}

	// Re-group: the policy answers ViewModelOpaque for this bit and the apply
	// sweep's SetRenderGroup moves the renderable between the leaves and the
	// viewmodel list when the group crosses that line.
	BumpPolicyInputs();
}

float C_StandaloneEntity::SequenceCycle( float flNow ) const
{
	if ( m_flSeqDuration <= 0.0f )
		return 0.0f;

	float flCycle = ( flNow - Block().Client.AnimTime ) * Block().Client.PlaybackRate / m_flSeqDuration;

	if ( flCycle < 0.0f )
		return 0.0f;

	if ( m_bSeqLooping )
		return flCycle - (int)flCycle;

	// A finished one-shot holds its last frame, exactly as stock clamps the
	// cycle — a fire animation that wrapped back to the ready pose on its own
	// would look like a shot that never happened.
	return MIN( flCycle, 0.999f );
}


void C_StandaloneEntity::MoveToEye( const Vector &vecEye, const QAngle &angEye )
{
	// THE TAIL ALONE, AS OF STEP 6b, and that is the point of the tail having a
	// transform of its own. This used to write Block().Head.Origin and
	// Block().Head.WorldTransform as well, because RenderableToWorldTransform
	// returned the head's matrix -- so pinning the viewmodel to the eye also
	// moved the entity's COLLISION origin to the eye, every frame, for the one
	// entity in the level that is deliberately not drawn where it is.
	//
	// NOT OnPositionChanged: RenderableChanged is for re-leafing, and a
	// viewmodel-pass renderable is collated from its own list, never from the
	// leaves.
	Block().Client.RenderOrigin = vecEye;
	Block().Client.RenderAngles = angEye;

	// Its own AngleMatrix rather than a copy of the head's, which is the one
	// place in the port where the two poses genuinely differ.
	AngleMatrix( Block().Client.RenderAngles, Block().Client.RenderOrigin,
	             Block().Client.RenderTransform );
}

void SourceSharp_StandaloneViewModelToEye( const Vector &vecEye, const QAngle &angEye )
{
	if ( s_pLocalViewModel )
		s_pLocalViewModel->MoveToEye( vecEye, angEye );
}

//-----------------------------------------------------------------------------
// The ABI doors. Managed code names entities by handle; these resolve and
// forward, exactly as every Standalone* slot above does.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneSetViewModel( uint32_t hEntity, int32_t bEnable )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->SetViewModelPass( bEnable != 0 );
}

void C_StandaloneEntity::StoreSequence( int nSequence, float flPlaybackRate, float flAnimTime,
                                        float flDuration, bool bLooping )
{
	Block().Client.Sequence     = nSequence;
	Block().Client.PlaybackRate = flPlaybackRate;
	Block().Client.AnimTime     = flAnimTime;
	m_flSeqDuration             = flDuration;
	m_bSeqLooping               = bLooping;
}

void SourceSharp_StandaloneStoreSequence( uint32_t hEntity, int32_t nSequence, float flPlaybackRate,
                                          float flAnimTime, float flDuration, int32_t bLooping )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->StoreSequence( nSequence, flPlaybackRate, flAnimTime, flDuration, bLooping != 0 );
}

//-----------------------------------------------------------------------------
// The entity's engine block, for the managed skeleton (plan
// physics_bones_native_zero 1.2, client half): which model it wears, where it
// stands and is drawn, whether it is the local viewmodel. The client twin of
// the server's EntityGetEngineBlock; NULL and 0 for a handle that names no
// standalone entity.
//-----------------------------------------------------------------------------
void *SourceSharp_StandaloneEngineBlock( uint32_t hEntity, int32_t *pOutBytes )
{
	if ( pOutBytes )
		*pOutBytes = 0;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return NULL;

	if ( pOutBytes )
		*pOutBytes = (int32_t)sizeof( EngineBlock_t );

	return &pEntity->Block();
}

//-----------------------------------------------------------------------------
// A noise, made by the client, because an animation event said so.
//
// The first sound this side has ever been able to make. Every sound slot on
// this ABI is filled under #ifndef CLIENT_DLL, so IEngineSound resolves out of
// the client container, compiles, is called, and does nothing at all — which
// is a worse failure than a missing method, because it looks like working
// code.
//
// CLocalPlayerFilter is the right filter and it works here, which was not
// always true: it used to ask C_BasePlayer::GetLocalPlayer(), which has
// answered NULL since the standalone player became the viewer, so every client
// filter came out empty. It asks engine->GetLocalPlayer() now — an index,
// stated by the engine, needing no entity to have decoded. See
// local_recipient.h.
//
// SOUND_FROM_WORLD when the handle does not resolve rather than a refusal: a
// sound with an origin is a sound whether or not the thing that asked for it
// is still there, and an animation event fired on the frame an entity went
// away is exactly the case.
//-----------------------------------------------------------------------------
void SourceSharp_ClientEmitSound( uint32_t hEntity, const char *pszSoundName,
                                  const float *pOriginXYZ )
{
	if ( !pszSoundName || !pszSoundName[0] )
		return;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	const int iEntIndex = pEntity ? pEntity->entindex() : SOUND_FROM_WORLD;

	CLocalPlayerFilter filter;

	if ( pOriginXYZ )
	{
		const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );

		GameSound_Emit( filter, iEntIndex, pszSoundName, &vecOrigin );
	}
	else
	{
		GameSound_Emit( filter, iEntIndex, pszSoundName );
	}
}

//=============================================================================//
// THE CLIENT SKELETON PROBE AND REPLAY ARE GONE (plan physics_bones_native_zero
// 1.2, client half). cs_cl_boneprobe's dump/merge/layer arms and
// cs_cl_bonereplay were an oracle for the native skeleton and bone_library.cpp
// this side no longer poses with; the arms the live gate drives (mergeauto,
// report) are SourceSharp.Client's managed cs_cl_boneprobe.
//=============================================================================//

// standalone_nettable.h names shareddefs.h's NOINTERP_PARITY_MAX_BITS for R5 server TUs.
COMPILE_TIME_ASSERT( STANDALONE_NOINTERP_PARITY_BITS == NOINTERP_PARITY_MAX_BITS );

// And the same for standalone_nettable.h's copy of const.h's FSOLID_MAX_BITS,
// the solidity pair's width. This TU sees const.h (its FSOLID_NOT_SOLID uses
// prove it), so the pair's wire width is pinned to the engine's own number for
// the FSOLID_* bits here, where a disagreement is a build error.
// As ints: an unnamed enum's enumerator against SolidFlags_t's is an
// -Wenum-compare error under the sniper container's gcc.
COMPILE_TIME_ASSERT( (int)STANDALONE_SOLID_FLAGS_MAX_BITS == (int)FSOLID_MAX_BITS );
