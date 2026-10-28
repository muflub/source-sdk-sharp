//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// The client's world: a native object because the engine names entity 0 by index, not by
// handle. Its networked values live in a blob past the object; what they mean and what
// the client does with them are managed's.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"
#include "igamerules.h"
#include "client_world.h"
#include "ivmodemanager.h"

#include "vphysics_interface.h"
#include "world_subject.h"
#include "shell_world_nettable_client.h"
#include "shell_cliententitylist.h"
#include "c_baseentity_statics.h"

#include "shell_managed_areas_client.h"

// What managed's answer to a world update asks this file to do.
enum
{
	kWorldArrival_NormalMode = 1,
	kWorldArrival_Occlusion = 2,
	kWorldArrival_ScreenFadeRange = 4,
};

#ifdef CWorld
#undef CWorld
#endif

// The client's single definition of the game rules global.
IGameRules *g_pGameRules = NULL;

static C_World *g_pClientWorld;

CWorldClientCollideable::CWorldClientCollideable( C_World *pOuter )
{
	m_pOuter = pOuter;

	m_vecMins = vec3_origin;
	m_vecMaxs = vec3_origin;

	// A debug engine asserts the world is unrotated on every world trace.
	m_vecOrigin = vec3_origin;
	m_angRotation = vec3_angle;
	SetIdentityMatrix( m_Transform );

	m_nSolidType = SOLID_BSP;
	m_nSolidFlags = 0;
}

// The owner, not this: the engine casts the answer to IClientUnknown for trace_t::m_pEnt.
IHandleEntity *CWorldClientCollideable::GetEntityHandle()
{
	return m_pOuter;
}

IClientUnknown *CWorldClientCollideable::GetIClientUnknown()
{
	return m_pOuter;
}

// Brush model 1, the world. A constant here: nothing on the client sets a model index.
int CWorldClientCollideable::GetCollisionModelIndex()
{
	return 1;
}

const model_t *CWorldClientCollideable::GetCollisionModel()
{
	return modelinfo ? modelinfo->GetModel( GetCollisionModelIndex() ) : NULL;
}

// One world per process, reused for every map and freed at DLL shutdown. Its handle must
// be unbound per map (Release), or the next map's lookups would find the old serial.
void ClientWorldFactoryInit()
{
	g_pClientWorld = new C_World;
}

void ClientWorldFactoryShutdown()
{
	delete g_pClientWorld;
	g_pClientWorld = NULL;
}

// The factory the ClientClass is built with. It returns the object itself: the pointer the
// engine keeps must be the one trace_t::m_pEnt holds.
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

// Built by shell_world_nettable_client.cpp from the shared declaration.
ClientClass *C_World::GetClientClass()
{
	return SourceSharp_WorldClientClass();
}

// Where the blob sits inside a C_World. Not the server's offset; the two sides agree on the
// payload word index instead.
int SourceSharp_WorldBlobOffset()
{
	return (int)ALIGN_VALUE( sizeof( C_World ), 16 );
}

// EntityMem_Alloc zeroes, so every value starts at its default. nBytes is sizeof( C_World ),
// which the blob offset was computed from.
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

void *C_World::operator new( size_t stAllocateBlock, int nBlockUse, const char *pFileName, int nLine )
{
	return AllocClientWorld( stAllocateBlock );
}

// Both forms: the placement new needs a matching delete if the constructor throws.
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

// Asks managed where its fields are, again after a reload moves them. Readers answer zero
// when this fails rather than read offset zero, the vtable pointer.
bool C_World::ResolveFields()
{
	if ( m_Fields.IsCurrent() )
		return true;

	return SourceSharpWorldFields_Resolve( &m_Fields );
}

// The two values client.so still reads natively, through world_net_sync.cpp as the server does.
const char *C_World::GetDetailSpriteMaterial()
{
	ResolveFields();
	return SourceSharpWorld_ReadString( DataBlob(), BlobBytes(), m_Fields.nDetailSpriteMaterial );
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
// tier1/convar.h re-enables C4355; 'this' in the initialiser list below is deliberate.
#pragma warning( disable : 4355 )
#endif

// m_Collideable is named in the initialiser list so its owner pointer is valid before the body.
C_World::C_World()
	: m_bReportedOnce( false )
	, m_DataChangeEventRef( -1 )
	, m_hThink( (ClientThinkHandle_t)(uintp)0xFFFF ) // the think list's invalid handle
	, m_hRender( INVALID_CLIENT_RENDER_HANDLE )
	, m_Collideable( this )
{
	V_memset( m_LastReported, 0, sizeof( m_LastReported ) );

	// Invalid until a level binds one. The singleton lives for the process, so the handle, not
	// the pointer, says whether a level is loaded.
	m_RefEHandle.Term();
}

C_World::~C_World()
{
	// Normally Release() has unbound already; not on a DLL shutdown with a level loaded.
	if ( m_RefEHandle.IsValid() )
		SourceSharp_LegacyListUnbind( m_RefEHandle );

	m_DataChangeEventRef = -1;
}

// The world joins the OnDataChanged deferral: OnDataChanged is the client DLL's call, so
// an entity that does not register here never gets one.
void C_World::PreDataUpdate( DataUpdateType_t updateType )
{
	// Record the first deferral and answer true so OnPreDataChanged runs once; later ones
	// answer false and let DATA_UPDATE_CREATED override DATA_UPDATE_DATATABLE_CHANGED.
	if ( m_DataChangeEventRef < 0 )
	{
		m_DataChangeEventRef = (int)updateType;

		OnPreDataChanged( updateType );
	}
	else if ( updateType == DATA_UPDATE_CREATED )
	{
		m_DataChangeEventRef = (int)updateType;
	}
}

// Called once a frame after the net update, so OnDataChanged sees a complete update. The
// ref is cleared before the call so a nested PreDataUpdate's deferral is kept.
void C_World::DeliverPendingDataChanged()
{
	if ( m_DataChangeEventRef < 0 )
		return;

	const DataUpdateType_t updateType = (DataUpdateType_t)m_DataChangeEventRef;

	m_DataChangeEventRef = -1;

	OnDataChanged( updateType );
}

// The world enters the level. SourceSharp_LegacyListBind registers it and sets
// m_RefEHandle. The blob is not written here: a reused singleton may already hold values.
bool C_World::Init( int entnum, int iSerialNum )
{
	// The singleton is reused per map, so a live handle means Release did not run; binding
	// again would leave two list rows for one object.
	if ( m_RefEHandle.IsValid() )
	{
		Warning( "[SourceSharp] worldspawn: the client world is still bound to "
				 "entity %d (serial %d) and a create for entity %d (serial %d) "
				 "arrived. The previous level's Release did not run; REFUSED.\n",
				 m_RefEHandle.GetEntryIndex(), m_RefEHandle.GetSerialNumber(), entnum, iSerialNum );
		return false;
	}

	SourceSharp_LegacyListBind( this, entnum, iSerialNum );

	// The box, from the model the index names.
	if ( const model_t *pModel = m_Collideable.GetCollisionModel() )
	{
		Vector vecMins, vecMaxs;
		modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );
		m_Collideable.SetBounds( vecMins, vecMaxs );
	}

	return true;
}

// The world leaves the level but the object stays: it is a process-lifetime singleton.
// Unbinding the handle here is required, or the next map finds this map's serial.
void C_World::Release()
{
	// Otherwise a deferral from the leaving level is delivered into the arriving one.
	m_DataChangeEventRef = -1;

	if ( m_RefEHandle.IsValid() )
	{
		SourceSharp_LegacyListUnbind( m_RefEHandle );
		m_RefEHandle.Term();
	}

	// The next map's create prints its values instead of comparing with this level's.
	m_bReportedOnce = false;
}

// Managed decides what the create does with the values; this makes the three engine calls
// its answer names.
void C_World::OnDataChanged( DataUpdateType_t updateType )
{
	// WorldArrival's order: max occludee area, min occluder area, min and max prop screen width.
	float flPlan[4] = {};
	IManagedRuntimeClient *pRuntime = ManagedRuntimeClient();
	const int nActions =
		pRuntime ? pRuntime->WorldDataChanged( DataBlob(), BlobBytes(), (int32_t)updateType, flPlan, 4 ) : 0;

	if ( nActions & kWorldArrival_NormalMode )
		modemanager->SwitchMode( false, true );

	if ( nActions & kWorldArrival_Occlusion )
	{
		OcclusionParams_t params;
		params.m_flMaxOccludeeArea = flPlan[0];
		params.m_flMinOccluderArea = flPlan[1];
		engine->SetOcclusionParameters( params );
	}

	if ( nActions & kWorldArrival_ScreenFadeRange )
		modelinfo->SetLevelScreenFadeRange( flPlan[2], flPlan[3] );

	// Prints only when the bytes change, in the server's format.
	ReportIfChanged();
}

// A byte compare of the blob, so no field can be missed.
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

// The world's place is the origin and never moves. File-scope so references to them are
// not references to temporaries.
static const Vector s_vecWorldOrigin( 0.0f, 0.0f, 0.0f );
static const QAngle s_angWorldAngles( 0.0f, 0.0f, 0.0f );
static matrix3x4_t s_matWorldTransform;
static bool s_bWorldTransformBuilt = false;

const Vector &C_World::GetAbsOrigin() const
{
	return s_vecWorldOrigin;
}

const QAngle &C_World::GetAbsAngles() const
{
	return s_angWorldAngles;
}

const Vector &C_World::GetRenderOrigin()
{
	return s_vecWorldOrigin;
}

const QAngle &C_World::GetRenderAngles()
{
	return s_angWorldAngles;
}

const matrix3x4_t &C_World::RenderableToWorldTransform()
{
	if ( !s_bWorldTransformBuilt )
	{
		SetIdentityMatrix( s_matWorldTransform );
		s_bWorldTransformBuilt = true;
	}

	return s_matWorldTransform;
}

// The level's box, in world and local space alike.
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
	color[0] = color[1] = color[2] = 1.0f;
}

#if defined( WIN32 )
// Stock falls back to a no-op without the Novint library, which the SDK does not ship;
// haptics stays NULL.
void ConnectHaptics( CreateInterfaceFn ) {}

void DisconnectHaptics() {}
#endif

C_World *GetClientWorldEntity()
{
	Assert( g_pClientWorld != NULL );
	return g_pClientWorld;
}

// The world's per-map unbinding at level shutdown, called from the client's level clear as
// on the server. Guarded on the binding, so a second call is a no-op.
bool SourceSharp_ClientWorldLevelShutdown()
{
	if ( g_pClientWorld == NULL || !g_pClientWorld->IsBoundToTheLevel() )
		return false;

	g_pClientWorld->Release();

	return true;
}

// The world's arm of the client resolve: worldspawn is not in the slot table. Reads the
// global directly rather than GetClientWorldEntity(), which asserts, and tests the handle,
// which is invalid between maps.
CBaseHandle SourceSharp_ClientWorldHandle()
{
	// A NULL singleton has no object to ask.
	return ( g_pClientWorld && g_pClientWorld->IsBoundToTheLevel() ) ? g_pClientWorld->GetRefEHandle() : CBaseHandle();
}

IClientUnknown *SourceSharp_ClientWorldFromIndex( int nEntry )
{
	const CBaseHandle hWorld = SourceSharp_ClientWorldHandle();

	return SourceSharp_ClientWorldAnswersForIndex( hWorld, nEntry ) ? g_pClientWorld->GetIClientUnknown() : NULL;
}

IClientUnknown *SourceSharp_ClientWorldFromHandle( const CBaseHandle &hEnt )
{
	const CBaseHandle hWorld = SourceSharp_ClientWorldHandle();

	return SourceSharp_ClientWorldAnswersForHandle( hWorld, hEnt ) ? g_pClientWorld->GetIClientUnknown() : NULL;
}

// The shared world_subject.h rule against the bound world, NULL between maps.
bool SourceSharp_IsTheWorld( const IHandleEntity *pHandleEntity )
{
	return SourceSharp_IsTheWorldHandle( pHandleEntity,
										 SourceSharp_ClientWorldFromHandle( SourceSharp_ClientWorldHandle() ) );
}

// NULL for no world, no declaration or an empty string. Not asserted: a level init may
// run before the world's packet is decoded.
const char *SourceSharp_WorldDetailSpriteMaterial()
{
	if ( g_pClientWorld == NULL )
		return NULL;

	const char *pszMaterial = g_pClientWorld->GetDetailSpriteMaterial();

	if ( pszMaterial == NULL || pszMaterial[0] == '\0' )
		return NULL;

	return pszMaterial;
}

// The world as the renderable screen-space materials bind their proxies to. void * because
// DrawScreenSpaceRectangle takes one.
void *GetClientWorldRenderable()
{
	return GetClientWorldEntity()->GetClientRenderable();
}
