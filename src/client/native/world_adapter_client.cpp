//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// The client's world, as a PERMANENT native adapter — the twin of the server's
// world_adapter.cpp, and lifted out of game/client/c_world.cpp the same way
// CWorld was lifted out of game/server/world.cpp.
//
// WHY THE WORLD STAYS A NATIVE OBJECT AT ALL is the server file's paragraph and
// it is the same reason on this side: the engine names entity 0 by INDEX and
// not by handle. `CreateClientClassFn` is handed `entnum` 0,
// the engine's world collideable for a trace is `GetClientEntity( 0 )`'s,
// and detailobjectsystem.cpp, WorldDimsProxy.cpp and viewpostprocess.cpp all
// ask for "the world" without having anything to ask it through. None of that
// is work outstanding; it is what the engine assumes.
//
// IT IS ON NEITHER HIERARCHY (plan_entity.md §3.11, as amended). c_world.h has
// the argument and the two NULL arms; this file is the allocation, the
// lifetime, and the engine's questions answered.
//
// WHAT IS NOT NATIVE IS ANY DECISION. The ten networked values are declared in
// SourceSharp.Shared.WorldNetState and live in a blob past this object; what
// each of the map's keys MEANS is WorldNet.ApplyKey's; and what this side DOES
// when they arrive is ClientModule.OnWorldStateChanged's, reached through one
// ABI slot. This file decides nothing.
//
// c_world.cpp is out of client_csharp.vpc as of step 5e and stays on disk for
// Phase 7 to delete, exactly as game/server/world.cpp does.
//
//=============================================================================//

#include "cbase.h"
#include "gamerules.h"
#include "c_world.h"
#include "world_subject.h"
#include "ivmodemanager.h"
#include "ivieweffects.h"
#include "shake.h"
#include "activitylist.h"
#include "decals.h"
#include "engine/ivmodelinfo.h"
#include "eventlist.h"
// NVNT haptic include for notification of world precache
#include "haptics/haptic_utils.h"
#include "shell_world_nettable_client.h"
#include "cliententitylist.h"
#include "shell_cliententitylist.h"
#include "cdll_client_int.h"
#include "datacache/imdlcache.h"
#include "c_baseentity_statics.h"
#include "imanaged_runtime_client.h"
#include "shell_managed_areas_client.h"
// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// WorldArrivalActions (SourceSharp.Client/WorldArrival.cs): what managed's
// answer to a world update asks this file to do.
enum
{
	kWorldArrival_NormalMode      = 1,
	kWorldArrival_Occlusion       = 2,
	kWorldArrival_ScreenFadeRange = 4,
};

#ifdef CWorld
#undef CWorld
#endif

// IGameRules, not the class. gamerules.h retyped the global to the interface
// the build actually asks for; this is where the CLIENT's copy of it lives.
//
// HERE BECAUSE c_world.cpp DEFINED IT AND NOTHING ELSE ON THIS SIDE DOES.
// gamerules.cpp's own definition is inside the `#else` of an `#ifdef
// CLIENT_DLL` (gamerules.cpp:186-191) and shell_gamerules.cpp's is inside a
// `#ifndef CLIENT_DLL`, so the client's single definition travelled with the
// world when the world moved. It is not the world's business and it is named
// here rather than quietly: it goes where the client's game rules go.
IGameRules *g_pGameRules = NULL;

static C_World *g_pClientWorld;

//=============================================================================
// THE COLLIDEABLE
//=============================================================================

CWorldClientCollideable::CWorldClientCollideable( C_World *pOuter )
{
	m_pOuter = pOuter;

	m_vecMins = vec3_origin;
	m_vecMaxs = vec3_origin;

	// THE TWO THE ENGINE ASSERTS ON. A debug engine asserts the world entity
	// is unrotated, reading GetCollisionOrigin() and GetCollisionAngles() on
	// every world trace.
	m_vecOrigin   = vec3_origin;
	m_angRotation = vec3_angle;
	SetIdentityMatrix( m_Transform );

	m_nSolidType  = SOLID_BSP;
	m_nSolidFlags = 0;
}

//-----------------------------------------------------------------------------
// THE OWNER, NOT `this`. The engine's trace takes this answer,
// casts it to IClientUnknown and asks GetIClientEntity() for the pointer that
// lands in trace_t::m_pEnt. Answering the
// collideable would put its vtable where an IClientUnknown's is expected.
//-----------------------------------------------------------------------------
IHandleEntity *CWorldClientCollideable::GetEntityHandle()
{
	return m_pOuter;
}

IClientUnknown *CWorldClientCollideable::GetIClientUnknown()
{
	return m_pOuter;
}

//-----------------------------------------------------------------------------
// Brush model 1, which is the world, on this side as on the server's.
//
// A CONSTANT HERE WHERE THE SERVER READS ITS OWNER'S m_nModelIndex, and the
// difference is the engine's: the server is TOLD its model index through
// IServerEntity::SetModelIndex and CWorld::Spawn writes the 1 itself, while
// nothing on the client ever set one — stock's C_World carried model index 0
// for the whole of its life, so GetCollisionModel() there answered NULL. 1 is
// the true answer to "which brush model is the level", it is the number the
// server's twin holds, and the two callers below are this file's own.
//-----------------------------------------------------------------------------
int CWorldClientCollideable::GetCollisionModelIndex()
{
	return 1;
}

const model_t *CWorldClientCollideable::GetCollisionModel()
{
	return modelinfo ? modelinfo->GetModel( GetCollisionModelIndex() ) : NULL;
}

//=============================================================================
// THE WORLD
//=============================================================================

//-----------------------------------------------------------------------------
// THE SINGLETON, MADE ONCE PER PROCESS AND REUSED FOR EVERY MAP — which is the
// client's arrangement and NOT the server's, and the difference is worth
// stating because world_release_test.cpp exists for the server's version of
// this question.
//
// On the server a CWorld is created per map by CreateEntityByName and freed by
// the entity purge; an unfreed one makes the SECOND map load fault. On the
// client there is no purge and no factory-per-map: ClientWorldFactoryInit
// allocates one at DLL init, SourceSharp_ClientWorldFactory hands the SAME
// object back on every level load, and ClientWorldFactoryShutdown frees it at
// DLL shutdown. Stock did exactly this and this port keeps it — the object is
// not what changed.
//
// WHAT MUST THEREFORE BE UNDONE PER MAP IS THE BINDING, and that is Release()
// below. A singleton that kept the first map's handle would answer the second
// map's lookups with the first map's serial, which is the reload bug the
// SERIAL in the handle exists to make impossible.
//-----------------------------------------------------------------------------
void ClientWorldFactoryInit()
{
	g_pClientWorld = new C_World;
}

void ClientWorldFactoryShutdown()
{
	delete g_pClientWorld;
	g_pClientWorld = NULL;
}

//-----------------------------------------------------------------------------
// THE FACTORY, NAMEABLE FROM ANOTHER FILE.
//
// IMPLEMENT_CLIENTCLASS_FACTORY took its address from inside c_world.cpp; the
// ClientClass is built in shell_world_nettable_client.cpp at DLL init, from the
// C# declaration, so the address is taken from there.
//
// IT RETURNS THE OBJECT AND NOT A MEMBER. The engine's create path wants an
// IClientNetworkable *, this class IS one, and the pointer the engine keeps has
// to be the same object trace_t::m_pEnt will hold — see c_world.h for why the
// client cannot make the server's split.
//-----------------------------------------------------------------------------
IClientNetworkable *SourceSharp_ClientWorldFactory( int entnum, int serialNum )
{
	if ( g_pClientWorld == NULL )
	{
		Warning( "[SourceSharp] worldspawn arrived with no client world object. "
		         "ClientWorldFactoryInit has not run.\n" );
		return NULL;
	}

	if ( !g_pClientWorld->Init( entnum, serialNum ) )
		return NULL;

	return g_pClientWorld;
}

//-----------------------------------------------------------------------------
// THERE IS NO IMPLEMENT_CLIENTCLASS_FACTORY AND NO BEGIN_RECV_TABLE HERE.
//
// Stock's pair built a static ClientClass __g_C_WorldClientClass and a DT_World
// of ten typed RecvProps addressed by RECVINFO -- i.e. by offsetof into this
// class. Both are gone. shell_world_nettable_client.cpp builds the ClientClass
// and every one of its props from SourceSharp.Shared.WorldNetState, the same
// declaration the server's SendTable is built from, and this hands it over.
//-----------------------------------------------------------------------------
ClientClass *C_World::GetClientClass()
{
	return SourceSharp_WorldClientClass();
}

//-----------------------------------------------------------------------------
// WHERE THE BLOB SITS INSIDE A C_World.
//
// Here rather than in shell_world_nettable_client.cpp because sizeof() is a
// fact of the translation unit that has the class definition -- the same split
// the server makes between world_adapter.cpp and shell_world_nettable.cpp.
//
// IT IS NOT THE SERVER'S NUMBER AND MUST NOT BE. A CWorld and a C_World are
// unrelated classes with unrelated members, so their blobs start at different
// offsets; what the two sides agree on is the payload WORD INDEX, which is what
// makes one declaration serve two layouts.
//-----------------------------------------------------------------------------
int SourceSharp_WorldBlobOffset()
{
	return (int)ALIGN_VALUE( sizeof( C_World ), 16 );
}

//-----------------------------------------------------------------------------
// EntityMem_Alloc calloc's, so the blob starts zeroed and every value has the
// default its member had.
//
// nBytes is ignored deliberately: it is sizeof( C_World ), and the offset the
// props were resolved against is a constant computed from that same sizeof.
//-----------------------------------------------------------------------------
static void *AllocClientWorld( size_t nBytes )
{
	Assert( (int)nBytes <= SourceSharp_WorldBlobOffset() );

	int nBlobBytes = SourceSharp_WorldBlobBytes();

	if ( nBlobBytes < 0 )
		nBlobBytes = 0;

	return EntityMem_Alloc( SourceSharp_WorldBlobOffset() + nBlobBytes );
}

void *C_World::operator new( size_t stAllocateBlock )
{
	return AllocClientWorld( stAllocateBlock );
}

void *C_World::operator new( size_t stAllocateBlock, int nBlockUse,
                             const char *pFileName, int nLine )
{
	return AllocClientWorld( stAllocateBlock );
}

// C_BaseEntity's operator delete used to free this; the class no longer has one
// to inherit, so the pair the allocation needs is declared here. Both forms,
// because the placement `new` above has to have a matching `delete` for the
// compiler to call if the constructor throws.
void C_World::operator delete( void *pMem )
{
	EntityMem_Free( pMem );
}

void C_World::operator delete( void *pMem, int nBlockUse, const char *pFileName, int nLine )
{
	EntityMem_Free( pMem );
}

unsigned char *C_World::DataBlob()
{
	return (unsigned char *)this + SourceSharp_WorldBlobOffset();
}

int C_World::BlobBytes() const
{
	return SourceSharp_WorldBlobBytes();
}

//-----------------------------------------------------------------------------
// Asks C# where its ten fields are, once, and again after a managed reload has
// moved them. Every reader below answers a zero when it says no, rather than
// reading offset zero -- which is the vtable pointer.
//-----------------------------------------------------------------------------
bool C_World::ResolveFields()
{
	if ( m_Fields.IsCurrent() )
		return true;

	return SourceSharpWorldFields_Resolve( &m_Fields );
}

//-----------------------------------------------------------------------------
// The two client.so still reads natively (the bounds, for the precipitation and
// the WorldDims proxy; the detail sprite material, for the detail props), as
// reads of the blob through the SAME world_net_sync.cpp the server reads
// through. The other seven getters left 2026-10-25 with no caller: what the
// create does with the occlusion areas, the prop widths and startdark is
// WorldArrival's (SourceSharp.Client), which decodes the blob itself.
//-----------------------------------------------------------------------------
const char *C_World::GetDetailSpriteMaterial()
{
	ResolveFields();
	return SourceSharpWorld_ReadString( DataBlob(), BlobBytes(),
	                                    m_Fields.nDetailSpriteMaterial );
}

Vector C_World::GetWorldMins()
{
	ResolveFields();
	return SourceSharpWorld_ReadVector( DataBlob(), m_Fields.nWorldMins );
}

Vector C_World::GetWorldMaxs()
{
	ResolveFields();
	return SourceSharpWorld_ReadVector( DataBlob(), m_Fields.nWorldMaxs );
}

#if defined( _MSC_VER )
// 'this' in the initialiser list below is deliberate. The build disables C4355
// (/wd4355, as Valve's VPC does), but tier1/convar.h ends with
// `#pragma warning( default : 4355 )`, which turns it back on for the rest of
// any file that includes it.
#pragma warning( disable : 4355 )
#endif

//-----------------------------------------------------------------------------
// m_Collideable IS NAMED IN THE INITIALISER LIST, which is what makes its owner
// pointer valid before the body runs — the same reason the server's CWorld
// names its two members there.
//-----------------------------------------------------------------------------
C_World::C_World( void )
	: m_bReportedOnce( false )
	, m_DataChangeEventRef( -1 )
	, m_hThink( (ClientThinkHandle_t)(uintp)0xFFFF )	// the think list's invalid handle (C8: the list left)
	, m_hRender( INVALID_CLIENT_RENDER_HANDLE )
	, m_Collideable( this )
{
	V_memset( m_LastReported, 0, sizeof( m_LastReported ) );

	// An invalid handle until the level's create call binds one. `IsTheWorld`
	// reads this to tell "there is no world yet" from "you hit the world", and
	// the singleton exists for the whole process — so the handle, not the
	// pointer, is what says whether a level is loaded.
	m_RefEHandle.Term();
}

C_World::~C_World( void )
{
	// The binding is Release()'s to undo and it has already run by here in
	// every ordinary shutdown. This is the one that has not: a DLL shutdown
	// with a level still loaded, where nothing ever calls Release.
	if ( m_RefEHandle.IsValid() )
		SourceSharp_LegacyListUnbind( m_RefEHandle );

	// ~C_BaseEntity's line, kept in the shape the queue left behind: nothing
	// outlives this object holding a pointer to its member any more, so
	// forgetting the deferral is all there is to do.
	m_DataChangeEventRef = -1;
}

//-----------------------------------------------------------------------------
// THE WORLD JOINS THE OnDataChanged QUEUE.
//
// C_BaseEntity::PreDataUpdate opens with exactly these three lines and the
// world inherited them until step 5e. c_world.h says what their absence cost
// and how it was found; the short version is that OnDataChanged is the CLIENT
// DLL's call and not the engine's, so an entity that does not register here
// never gets one.
//
// EVERYTHING ELSE C_BaseEntity::PreDataUpdate DID IS GONE WITH ITS SUBJECT: the
// spawn-before-first-update, the interpolation history, the previous origin and
// angles for the network-change check. The world spawns nothing, interpolates
// nothing, and does not move.
//-----------------------------------------------------------------------------
void C_World::PreDataUpdate( DataUpdateType_t updateType )
{
	// THE QUEUE, INLINED AT ITS ONE PRODUCER -- Phase 1c. What
	// AddDataChangeEvent did, in the order it did it:
	//
	//   nothing recorded yet  -> record it, and answer TRUE so the caller runs
	//                            OnPreDataChanged exactly once per deferral
	//   already recorded      -> answer FALSE, and let DATA_UPDATE_CREATED
	//                            override a DATA_UPDATE_DATATABLE_CHANGED
	//
	// Both arms are here. The Assert the queue carried (`the stored index names
	// THIS entity`) cannot be violated by a member and is gone with it.
	if ( m_DataChangeEventRef < 0 )
	{
		m_DataChangeEventRef = (int)updateType;

		OnPreDataChanged( updateType );
	}
	else if ( updateType == DATA_UPDATE_CREATED )
	{
		m_DataChangeEventRef = (int)updateType;
	}

	// THE SPAWN THAT STOOD HERE LEFT 2026-10-25. C_BaseEntity::PreDataUpdate's
	// `Spawn()` on the create ran C_World::Precache, and nothing it did had a
	// subject left: the activity and event registries went in Phase 2d, the
	// precache-register walk in C8, the eight g_sModelIndex* sprite indexes
	// W_Precache took had no reader on the client link edge (nm -u: the temp
	// entities that named them are managed), and `haptics` is never set (the
	// SDK ships no haptics driver).
}

//-----------------------------------------------------------------------------
// THE DELIVERY, ONCE A FRAME. shell_clientdll.cpp's OnRenderStart calls this
// after the net update, which is the whole reason the call is deferred at all:
// OnDataChanged must see a COMPLETE update, and PreDataUpdate runs before the
// fields have arrived.
//
// WHAT THE QUEUE DID THAT THIS DOES NOT: drain a list. Phase 1c measured its
// producers over the client link line -- `nm -u` for AddDataChangeEvent over
// the 271 objects on the ninja link edge found exactly one, this file -- so
// the list could never hold more than one element, and FOR_EACH_LL over it was
// a loop that ran once or not at all.
//
// THE REF IS CLEARED BEFORE THE CALL, not after, because the queue cleared it
// before delivering too (`*pEvent->m_pStoredEvent = -1;` came before
// OnDataChanged). An OnDataChanged that caused another PreDataUpdate would
// otherwise have its second deferral thrown away.
//-----------------------------------------------------------------------------
void C_World::DeliverPendingDataChanged()
{

	if ( m_DataChangeEventRef < 0 )
		return;

	const DataUpdateType_t updateType = (DataUpdateType_t)m_DataChangeEventRef;

	m_DataChangeEventRef = -1;

	OnDataChanged( updateType );
}

//-----------------------------------------------------------------------------
// THE WORLD ENTERS THE LEVEL.
//
// Stock's body was `ActivityList_Init(); EventList_Init(); return
// BaseClass::Init( entnum, iSerialNum );` and C_BaseEntity::Init's own work was
// three things: the entity list registration, a partition handle, and the
// interpolation setup. Only the first of those has a subject here — this object
// is not in the partition (stock's C_World answered ENTITY_SHOULD_NOT_COLLIDE,
// so CollisionProp never made a handle for it) and it interpolates nothing.
//
// SourceSharp_LegacyListBind IS AddNetworkableEntity, and it calls
// SetRefEHandle on the way through — which is how m_RefEHandle comes to hold
// the handle rather than being assigned from the arguments here. Reading it
// back is the same discipline SourceSharp_ClientEntityBind keeps for the
// hierarchy's shells, and for the same reason: one place issues the handle.
//
// `m_flWaveHeight = 0.0f` STOOD IN STOCK'S BODY AND IS ALREADY TRUE. The blob
// is part of the allocation and EntityMem_Alloc calloc's. Writing it here would
// be a blob write before the field offsets have been asked for — and this runs
// on every Init, including the one that reuses the singleton for a second map,
// where an unasked-for zero would clobber a value that had already arrived.
//-----------------------------------------------------------------------------
bool C_World::Init( int entnum, int iSerialNum )
{
	// THE ACTIVITY AND EVENT REGISTRIES ARE GONE -- plan_shim.md Phase 2d.
	//
	// ActivityList_Init() and EventList_Init() opened two native name tables
	// that a per-level crossing then filled from managed code, so that
	// game/shared/animation.cpp could write an ORDINAL into mdl memory for a
	// later native reader to compare. Nothing compares ordinals now:
	// precache_model.cpp reads event NAMES and the sequence/activity lookups
	// are one managed rule over a ModelDescriptor. The tables, the registry
	// (shell_animlists.cpp), its header, animation.cpp and the 2,048-line
	// ActivityCatalog.cs that fed it all went together.
	//
	// The names are not lost: EventCatalog.cs keeps the fifty event names,
	// which is the half managed code reads itself.

	// A REFUSAL RATHER THAN A SECOND BINDING. The singleton is reused per map,
	// so an Init that arrives while the previous level's handle is still held
	// means Release did not run — and binding again would leave the stock list
	// holding two rows for one object, the second of which nothing would ever
	// remove.
	if ( m_RefEHandle.IsValid() )
	{
		Warning( "[SourceSharp] worldspawn: the client world is still bound to "
		         "entity %d (serial %d) and a create for entity %d (serial %d) "
		         "arrived. The previous level's Release did not run; REFUSED.\n",
		         m_RefEHandle.GetEntryIndex(), m_RefEHandle.GetSerialNumber(),
		         entnum, iSerialNum );
		return false;
	}

	SourceSharp_LegacyListBind( this, entnum, iSerialNum );

	// The box, from the model the index names — the same four lines CWorld's
	// Spawn runs on the server, and the only thing an ICollideable is obliged
	// to answer that the constructor could not know yet.
	if ( const model_t *pModel = m_Collideable.GetCollisionModel() )
	{
		Vector vecMins, vecMaxs;
		modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );
		m_Collideable.SetBounds( vecMins, vecMaxs );
	}

	return true;
}

//-----------------------------------------------------------------------------
// THE WORLD LEAVES, AND THE OBJECT DOES NOT.
//
// `delete this` is what CStandaloneEntity::Release and the server's
// CWorldNetworkable::Release both are, and it is the WRONG answer here: this
// object is a process-lifetime singleton that ClientWorldFactoryShutdown owns,
// and stock's C_World::Release was `ActivityList_Free(); Term();` for exactly
// that reason. What Term did that matters is the entity-list removal, and that
// is the line below.
//
// UNBINDING IS NOT OPTIONAL AND IT IS THE RELOAD BUG. The singleton survives
// the level change; the handle must not. A world that kept the first map's
// handle would be found at entry 0 by the first map's serial after the second
// map had bound its own — see world_release_test.cpp for the server's version
// of the same class of bug, which cost a SIGSEGV on dm_lockdown's 82nd decal.
//-----------------------------------------------------------------------------
void C_World::Release()
{
	// Nothing to free: the activity and event registries went in Phase 2d.
	// See the note where they used to be opened.

	// The deferral is delivered per frame from OnRenderStart, so one recorded
	// by the last update of the level that is leaving would otherwise be
	// delivered into the level that is arriving.
	m_DataChangeEventRef = -1;

	if ( m_RefEHandle.IsValid() )
	{
		SourceSharp_LegacyListUnbind( m_RefEHandle );
		m_RefEHandle.Term();
	}

	// The ten are gone with the level. Forgetting what was last printed makes
	// the next map's create print its values rather than comparing them against
	// a level that is no longer loaded.
	m_bReportedOnce = false;
}

//-----------------------------------------------------------------------------
// WHAT THE VALUES ARE FOR, ON THE CREATE -- managed's decision, carried out.
//
// Stock's C_World::OnDataChanged reset the client mode, faded a start-dark map
// in from black, and handed the renderer the level's occlusion areas and prop
// fade range, on the create. Which of that happens, and with what values, is
// SourceSharp.Client's WorldArrival now (it decodes the same blob, and does the
// fade itself through the view effects); this carries out the three engine
// calls its answer names. WHAT the map's keys MEAN is WorldNet.ApplyKey's.
//-----------------------------------------------------------------------------
void C_World::OnDataChanged( DataUpdateType_t updateType )
{
	// The plan's four floats, in WorldArrival's order: max occludee area, min
	// occluder area, min and max prop screen width.
	float flPlan[ 4 ] = {};
	IManagedRuntimeClient *pRuntime = ManagedRuntimeClient();
	const int nActions = pRuntime
		? pRuntime->WorldDataChanged( DataBlob(), BlobBytes(), (int32_t)updateType, flPlan, 4 )
		: 0;

	if ( nActions & kWorldArrival_NormalMode )
		modemanager->SwitchMode( false, true );

	if ( nActions & kWorldArrival_Occlusion )
	{
		OcclusionParams_t params;
		params.m_flMaxOccludeeArea = flPlan[ 0 ];
		params.m_flMinOccluderArea = flPlan[ 1 ];
		engine->SetOcclusionParameters( params );
	}

	if ( nActions & kWorldArrival_ScreenFadeRange )
		modelinfo->SetLevelScreenFadeRange( flPlan[ 2 ], flPlan[ 3 ] );

	// WHAT ARRIVED, WHENEVER IT CHANGES, AND IT IS THE ONLY EVIDENCE IT DID.
	//
	// IT CANNOT SPAM: ReportIfChanged keeps the bytes it last printed and
	// compares, and the world's ten values are written by the map parse and by
	// nothing else in an ordinary session. The server prints the same ten
	// through the same formatter, so the two lines are comparable by eye and by
	// grep.
	ReportIfChanged();
}

//-----------------------------------------------------------------------------
// The ten, printed when they are not what was printed last.
//
// A byte compare of the blob rather than a value compare: the blob IS the
// state, so this cannot miss a field the formatter happens not to print, and it
// costs one memcmp of 304 bytes per world update.
//-----------------------------------------------------------------------------
void C_World::ReportIfChanged()
{
	ResolveFields();

	const int nBytes = BlobBytes();

	if ( nBytes > 0 && nBytes <= (int)sizeof( m_LastReported ) )
	{
		if ( m_bReportedOnce && V_memcmp( m_LastReported, DataBlob(), nBytes ) == 0 )
			return;

		V_memcpy( m_LastReported, DataBlob(), nBytes );
		m_bReportedOnce = true;
	}

	SourceSharpWorld_Report( "Client", DataBlob(), BlobBytes(), m_Fields );
}

//=============================================================================
// THE PLACE, WHICH IS THE ORIGIN AND NEVER MOVES
//
// Five interfaces ask this object where it is — IClientEntity twice and
// IClientRenderable three times — and the level's frame IS the world frame, so
// all five answer the same three constants. They are file-scope so that
// returning a reference to one is not returning a reference to a temporary.
//=============================================================================
static const Vector      s_vecWorldOrigin( 0.0f, 0.0f, 0.0f );
static const QAngle      s_angWorldAngles( 0.0f, 0.0f, 0.0f );
static matrix3x4_t       s_matWorldTransform;
static bool              s_bWorldTransformBuilt = false;

const Vector &C_World::GetAbsOrigin( void ) const     { return s_vecWorldOrigin; }
const QAngle &C_World::GetAbsAngles( void ) const     { return s_angWorldAngles; }
const Vector &C_World::GetRenderOrigin( void )        { return s_vecWorldOrigin; }
const QAngle &C_World::GetRenderAngles( void )        { return s_angWorldAngles; }

const matrix3x4_t &C_World::RenderableToWorldTransform()
{
	if ( !s_bWorldTransformBuilt )
	{
		SetIdentityMatrix( s_matWorldTransform );
		s_bWorldTransformBuilt = true;
	}

	return s_matWorldTransform;
}

//-----------------------------------------------------------------------------
// The level's box, which is what the world's render bounds are — asked in world
// space and in local space, and they are the same box because the world's frame
// is the level's.
//-----------------------------------------------------------------------------
void C_World::GetRenderBounds( Vector &mins, Vector &maxs )
{
	mins = m_Collideable.OBBMins();
	maxs = m_Collideable.OBBMaxs();
}

void C_World::GetRenderBoundsWorldspace( Vector &mins, Vector &maxs )
{
	GetRenderBounds( mins, maxs );
}

void C_World::GetShadowRenderBounds( Vector &mins, Vector &maxs, ShadowType_t shadowType )
{
	GetRenderBounds( mins, maxs );
}

void C_World::GetColorModulation( float *color )
{
	Assert( color );
	color[ 0 ] = color[ 1 ] = color[ 2 ] = 1.0f;
}


#if defined( WIN32 )
// What stock's ConnectHaptics and DisconnectHaptics (called from
// shell_clientdll.cpp's Init and Shutdown) come to in this build: stock loads
// the Novint device library (haptics.dll), which the SDK's bin/x64 does not
// ship, and without it falls back to a stub that does nothing. haptics stays
// NULL, as it is on Linux.
void ConnectHaptics( CreateInterfaceFn )
{
}

void DisconnectHaptics()
{
}
#endif

C_World *GetClientWorldEntity()
{
	Assert( g_pClientWorld != NULL );
	return g_pClientWorld;
}

//-----------------------------------------------------------------------------
// THE WORLD'S LEVEL-SHUTDOWN RELEASE, AND IT HAS THE SAME OWNER PROBLEM THE
// SHELLS HAVE.
//
// C_World::Release() is not called by anything in this file. It is called by
// CClientEntityList::Release() walking the STOCK list, because C_World::Init
// puts the singleton in that list through SourceSharp_LegacyListBind -- the
// same seam, and the same removal-set row. So the world's per-map UNBINDING,
// which the banner above Release calls "not optional and ... the reload bug",
// is owned armed by nothing at all.
//
// THIS IS THE SERVER'S ARRANGEMENT, ARRIVING ON THIS SIDE. On the server the
// world's release was taken OUT of the entity-list walk for exactly this reason
// and became a line of SourceSharp_LevelClearEntityList
// (shell_level.cpp) -- "relocated rather than replaced, and to the same
// POSITION it had". This is that line, on the client.
//
// GUARDED ON THE BINDING AND NOT ON THE POINTER. The singleton exists for the
// whole process, so `g_pClientWorld != NULL` says nothing about whether a level
// is loaded; IsBoundToTheLevel() is the test every other reader in this file
// uses, and it is what makes a second call in one shutdown a no-op rather than
// a second ActivityList_Free.
//
// THROUGH Release() and not through the pieces. Release is the virtual the
// stock walk called, and calling it is what keeps ONE spelling of this
// destruction -- world_release_test.cpp is the file that exists because the
// server had two and one of them quietly became a no-op.
//-----------------------------------------------------------------------------
bool SourceSharp_ClientWorldLevelShutdown( void )
{
	if ( g_pClientWorld == NULL || !g_pClientWorld->IsBoundToTheLevel() )
		return false;

	g_pClientWorld->Release();

	return true;
}

//-----------------------------------------------------------------------------
// THE WORLD'S ARM OF THE CLIENT RESOLVE.
//
// shell_cliententitylist.h carries the whole argument; the short version is
// that worldspawn is the one entity §3.3's slot table does not hold, so the
// world answers for itself and these are how. The server's twin is
// SourceSharp_WorldFromHandle in world_adapter.cpp, and this is deliberately
// the same shape rather than a second one.
//
// NOT GetClientWorldEntity(), WHICH ASSERTS. These run on the engine's resolve
// path, which is reached before ClientWorldFactoryInit on nothing today and
// would be reached after ClientWorldFactoryShutdown by a late engine ask -- and
// an Assert firing on a question whose correct answer is "no" would turn a
// NULL into a debug break. The global is read directly and tested, which is
// what the file's own comment six lines below says the non-asserting readers
// do.
//
// THE HANDLE IS THE TEST AND NOT THE POINTER. The singleton exists for the
// whole process; only the handle says whether a level is loaded, and only the
// SERIAL inside it says WHICH level. C_World::Release Term()s it, so between
// maps every one of these answers "no world" -- which is the reload bug this
// arm would otherwise reintroduce: a world still answering at entry 0 with the
// previous map's serial.
//-----------------------------------------------------------------------------
CBaseHandle SourceSharp_ClientWorldHandle( void )
{
	// IsBoundToTheLevel() is the same test spelled on the object; it is asked
	// here rather than there because a NULL singleton has no object to ask.
	return ( g_pClientWorld && g_pClientWorld->IsBoundToTheLevel() )
	     ? g_pClientWorld->GetRefEHandle()
	     : CBaseHandle();
}

IClientUnknown *SourceSharp_ClientWorldFromIndex( int nEntry )
{
	const CBaseHandle hWorld = SourceSharp_ClientWorldHandle();

	return SourceSharp_ClientWorldAnswersForIndex( hWorld, nEntry )
	     ? g_pClientWorld->GetIClientUnknown()
	     : NULL;
}

IClientUnknown *SourceSharp_ClientWorldFromHandle( const CBaseHandle &hEnt )
{
	const CBaseHandle hWorld = SourceSharp_ClientWorldHandle();

	return SourceSharp_ClientWorldAnswersForHandle( hWorld, hEnt )
	     ? g_pClientWorld->GetIClientUnknown()
	     : NULL;
}

//-----------------------------------------------------------------------------
// "IS THIS THE WORLD", the client's answer to world_subject.h's question: the
// shared rule against the world this file binds, which is NULL between maps.
// Here since gametrace_client.cpp left client.so (C6): its callers ask this
// directly rather than a CGameTrace member, as the server's do.
//-----------------------------------------------------------------------------
bool SourceSharp_IsTheWorld( const IHandleEntity *pHandleEntity )
{
	return SourceSharp_IsTheWorldHandle(
		pHandleEntity, SourceSharp_ClientWorldFromHandle( SourceSharp_ClientWorldHandle() ) );
}

//-----------------------------------------------------------------------------
// THE DETAIL-SPRITE MATERIAL, as the one thing detailobjectsystem.cpp needs.
//
// shell_world_nettable_client.h has the argument. The three answers that mean
// "the map said nothing" -- no world, no declaration, an empty string -- are
// folded into one NULL here so the caller has one test and not three.
//
// NOT Assert'd on the world existing, unlike GetClientWorldEntity above: this
// is called from the detail props' LevelInitPostEntity (shell_detail_props.cpp), and a level init
// that reached it before the world's packet had been decoded is a fallback
// material rather than a fault.
//-----------------------------------------------------------------------------
const char *SourceSharp_WorldDetailSpriteMaterial()
{
	if ( g_pClientWorld == NULL )
		return NULL;

	const char *pszMaterial = g_pClientWorld->GetDetailSpriteMaterial();

	if ( pszMaterial == NULL || pszMaterial[ 0 ] == '\0' )
		return NULL;

	return pszMaterial;
}

//-----------------------------------------------------------------------------
// The world, as the thing a screen-space material binds its proxies to.
//
// viewpostprocess.cpp asked GetClientWorldEntity()->GetClientRenderable() in
// seven places. Seven copies of one decision in a file that is otherwise
// nothing to do with entities is seven copies of the same decision, so the
// decision is made once, here, where the world entity already lives.
//
// void * rather than IClientRenderable *, because that is what every consumer
// takes: IMaterialSystem::DrawScreenSpaceRectangle's parameter is
// `void *pClientRenderable = NULL` (imaterialsystem.h:1445) and so are
// viewpostprocess.cpp's own DrawPyroVignette and DrawPyroPost.
//
// STILL A REAL RENDERABLE AFTER THE UN-DERIVE, which is the part worth saying:
// C_World is its own IClientEntity now and IClientEntity is an
// IClientRenderable, so GetClientRenderable() answers `this` exactly as
// C_BaseEntity's did. The proxies that bind against it — WorldDimsProxy is the
// one in this build — see the same non-NULL token they always saw.
//-----------------------------------------------------------------------------
void *GetClientWorldRenderable()
{
	return GetClientWorldEntity()->GetClientRenderable();
}
