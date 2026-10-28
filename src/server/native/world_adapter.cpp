//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// CWorld: worldspawn, a native object because the engine assumes it at edict 0
// and brush model 0. Every key it is given is offered to C#.

#include "ss_util.h"
#include "ss_shareddefs.h"
#include "ss_gamestringpool.h"
#include "engine/ivmodelinfo.h"
#include "shell_tempents.h"
#include "ss_soundent.h"
#include "ss_decals.h"
#include "igamerules.h"
#include "isaverestore.h"
#include "ss_world.h"
#include "mempool.h"
#include "engine/IEngineSound.h"
#include "engine/IStaticPropMgr.h"

// Defined beside the other engine-boundary globals.
extern IVModelInfo  *modelinfo;
extern CGlobalVars  *gpGlobals;

void PrecacheParticleSystem( const char *pParticleSystemName );
void SENTENCEG_Init();

#include "precache_model.h"
#include "shell_server.h"

#include "slot_table.h"

#include "ss_effect_dispatch.h"

#include "shell_resolve.h"
#include "managed_runtime_server.h"

#include "entity_shared_statics.h"

#include "native_command_body.h"
#include "ss_recipient_filter.h"
#include "ss_entityfactory.h"

// The global sprite indexes, precached by the world.
short		g_sModelIndexSmoke;			// holds the index for the smoke cloud

// Only the smoke index has a native reader; the rest are precached but not stored.
static void W_Precache(void)
{
	g_sModelIndexSmoke = Precache_Model("sprites/steam1.vmt");// smoke
	Precache_Model("sprites/bubble.vmt");//bubbles
	Precache_Model("sprites/bloodspray.vmt"); // initial blood
	Precache_Model("sprites/blood.vmt"); // splattered blood
}

// Registered with its own factory rather than LINK_ENTITY_TO_CLASS, whose template
// needs CBaseEntity's PostConstructor and NetworkProp. Destroy() is Release(),
// which does nothing here: the world is freed by the entity purge.
namespace
{

class CWorldFactory : public ss::IEntityFactory
{
public:
	CWorldFactory()
	{
		ss::EntityFactoryDictionary()->InstallFactory( this, "worldspawn" );
	}

	virtual IServerNetworkable *Create( const char * ) OVERRIDE
	{
		// One world: a second construction would take edict 0 and the singleton from the
		// live world. CreateEntityByName( "worldspawn" ) would reach this, then drop the
		// result without freeing it.
		if ( GetWorldEntity() != NULL )
		{
			Warning( "[SourceSharp] worldspawn: a second one was asked for while "
			         "one exists. REFUSED -- it would take edict 0 from the world "
			         "that is already in the level.\n" );
			return NULL;
		}

		// Returns the networkable member; its Release deletes the world.
		CWorld *pWorld = new CWorld;

		return pWorld->GetNetworkable();
	}

	virtual void Destroy( IServerNetworkable *pNetworkable ) OVERRIDE
	{
		if ( pNetworkable )
			pNetworkable->Release();
	}

	virtual size_t GetEntitySize() OVERRIDE { return sizeof( CWorld ); }
};

CWorldFactory g_WorldFactory;

} // namespace

// Where the blob sits inside a CWorld, 16-aligned, defined here because this
// translation unit can see sizeof( CWorld ).
int SourceSharp_WorldBlobOffset()
{
	return (int)ALIGN_VALUE( sizeof( CWorld ), 16 );
}

// Allocated the way the engine allocates an entity, plus the blob. The memory is
// zeroed, which gives the networked values their stock defaults. The blob length
// is C#'s and may be zero.
static void *AllocWorld( size_t nBytes )
{
	Assert( (int)nBytes <= SourceSharp_WorldBlobOffset() );

	int nBlobBytes = SourceSharp_WorldBlobBytes();

	if ( nBlobBytes < 0 )
		nBlobBytes = 0;

	return engine->PvAllocEntPrivateData( SourceSharp_WorldBlobOffset() + nBlobBytes );
}

void *CWorld::operator new( size_t stAllocateBlock )
{
	return AllocWorld( stAllocateBlock );
}

// CWorld has no base with an operator delete, so without this the engine's block
// would reach the global delete.
void CWorld::operator delete( void *pMem )
{
	engine->FreeEntPrivateData( pMem );
}

unsigned char *CWorld::DataBlob()
{
	return (unsigned char *)this + SourceSharp_WorldBlobOffset();
}

int CWorld::BlobBytes() const
{
	return SourceSharp_WorldBlobBytes();
}

// CServerNetworkProperty caches this after the first ask. There is no base table
// to fall back to: DT_BaseEntity's props would read this object at CBaseEntity
// offsets, so a missing declaration is fatal.
ServerClass *CWorld::GetServerClass()
{
	if ( ServerClass *pClass = SourceSharp_WorldServerClass() )
		return pClass;

	Error( "[SourceSharp] worldspawn has no ServerClass: the managed side never "
	       "declared SourceSharp.Shared.WorldNetState. There is no base table to "
	       "fall back to -- CWorld is not a CBaseEntity, so DT_BaseEntity's props "
	       "would read this object at offsets that belong to a different class.\n" );

	return NULL;
}

// Asks C# where the world's fields are, once and again after a managed reload.
// False when the declaration is not published; readers then answer zero rather
// than read offset zero (the vtable pointer).
bool CWorld::ResolveFields()
{
	if ( m_Fields.IsCurrent() )
		return true;

	return SourceSharpWorldFields_Resolve( &m_Fields );
}

bool CWorld::KeyValue( const char *szKeyName, const char *szValue )
{
	// Every key goes to C#; the shim keeps no list of the keys C# answers.
	IManagedRuntimeServer *pRuntime = ManagedRuntimeServer();

	if ( pRuntime != NULL
	  && pRuntime->OnWorldKeyValue(
	         SourceSharp_HandleFromAnyEntity( static_cast< IServerEntity * >( this ) ),
	         szKeyName, szValue ) != 0 )
	{
		return true;
	}

	// Not mine: the engine ignores an unhandled worldspawn key.
	return false;
}

static CWorld *g_WorldEntity = NULL;

CWorld* GetWorldEntity()
{
	return g_WorldEntity;
}

// Subobjects, constructed before the CWorld body runs, so m_pOuter is never null.
CWorldCollideable::CWorldCollideable( CWorld *pOuter )
{
	m_pOuter = pOuter;

	// The level's own shape.
	m_nSolidType  = SOLID_BSP;
	m_nSolidFlags = 0;

	m_vecOrigin   = vec3_origin;
	m_angRotation = vec3_angle;
	SetIdentityMatrix( m_Transform );

	// Replaced at CWorld::Spawn by the model's real box.
	m_vecMins = vec3_origin;
	m_vecMaxs = vec3_origin;
}

CWorldNetworkable::CWorldNetworkable( CWorld *pOuter )
{
	m_pOuter = pOuter;

	// Zeroed so AreaNum answers a number before the engine has computed anything.
	memset( &m_PVSInfo, 0, sizeof( m_PVSInfo ) );
}

#if defined( _MSC_VER )
// 'this' in the initialiser list below is deliberate. tier1/convar.h re-enables
// C4355 for the rest of any file that includes it.
#pragma warning( disable : 4355 )
#endif

// The members are constructed first, then the body hands &m_Networkable to edict 0.
CWorld::CWorld( )
	: m_Collideable( this )
	, m_Networkable( this )
{
	// Claimed at construction, not in Precache as stock did: the keys arrive before
	// Precache, and the world's blob is found by identity against this singleton.
	g_WorldEntity = this;

	m_pEdict       = NULL;
	m_nModelIndex  = 0;
	m_iszModelName = NULL_STRING;

	AttachToEdictZero();
}

CWorld::~CWorld( )
{
	// The world owns the game rules object.
	if ( g_pGameRules )
	{
		delete g_pGameRules;
		g_pGameRules = NULL;
	}

	// Undoes AttachToEdictZero, last, so everything above can still name this entity.

	// The world is in neither the slot table nor the entity list, so terminating its
	// handle is the whole removal.
	m_RefEHandle.Term();

	// ~CServerNetworkProperty makes this call too; the world is FL_EDICT_ALWAYS.
	engine->CleanUpEntityClusterList( m_Networkable.PVSInfo() );

	if ( m_pEdict )
	{
		// Edict 0 is not returned to the engine, or the next CreateEdict could hand out
		// the world's index. SetEdict( NULL ) comes before the networkable is cleared.
		m_pEdict->SetEdict( NULL, false );
		m_pEdict->m_pNetworkable = NULL;
		m_pEdict = NULL;
	}

	g_WorldEntity = NULL;
}

void CWorld::DecalTrace( trace_t *pTrace, char const *decalName)
{
	int index = ss::DecalIndexForName( decalName );
	if ( index < 0 )
		return;

	ss::CBroadcastRecipientFilter filter;

	if ( pTrace->hitbox != 0 )
	{
		TE_Decal( filter, 0.0f, &pTrace->endpos, &pTrace->startpos, 0, pTrace->hitbox, index );
	}
	else
	{
		TE_WorldDecal( filter, 0.0, &pTrace->endpos, index );
	}
}

// The world's handle serial. Not reset between maps, so a client cannot resolve
// the previous map's world with the new handle.
static int SourceSharp_WorldIssueSerial()
{
	static unsigned short s_nWorldSerial = 0;

	s_nWorldSerial = SourceSharp_NextWorldSerial( s_nWorldSerial );

	return (int)s_nWorldSerial;
}

void CWorld::AttachToEdictZero()
{
	m_pEdict = ss::INDEXENT( RequiredEdictIndex() );

	if ( !m_pEdict )
	{
		Warning( "[SourceSharp] worldspawn: edict %d does not exist. The world "
		         "will not be sent and nothing will resolve a handle to it.\n",
			RequiredEdictIndex() );
		return;
	}

	m_pEdict->SetEdict( static_cast< IServerUnknown * >( this ), true );
	m_pEdict->m_EdictIndex   = RequiredEdictIndex();

	// CBaseEdict::AreaNum and GetClassName read this unguarded; ~CWorld clears it.
	m_pEdict->m_pNetworkable = &m_Networkable;

	m_pEdict->m_fStateFlags &= ~( FL_EDICT_DONTSEND | FL_EDICT_PVSCHECK | FL_EDICT_FULLCHECK );
	m_pEdict->m_fStateFlags |= FL_EDICT_ALWAYS;

	// The world issues its own handle; it is not in the slot table, which binds shells only.
	SetRefEHandle( CBaseHandle( RequiredEdictIndex(), SourceSharp_WorldIssueSerial() ) );
}

const model_t *CWorldCollideable::GetCollisionModel()
{
	return modelinfo->GetModel( GetCollisionModelIndex() );
}

void CWorld::Spawn( void )
{
	// NOTE:  SHOULD NEVER BE ANYTHING OTHER THAN 1!!!
	SetModelIndex( 1 );

	// world model
	m_iszModelName = AllocPooledString(
		modelinfo->GetModelName( m_Collideable.GetCollisionModel() ) );

	// The box, from the model the index names.
	if ( const model_t *pModel = m_Collideable.GetCollisionModel() )
	{
		Vector vecMins, vecMaxs;
		modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );
		m_Collideable.SetBounds( vecMins, vecMaxs );
	}

	Precache( );

	// After the keys have been parsed, so it reports what the map set.
	ReportNetState();
}

// The ten networked values, through the formatter both sides share.
void CWorld::ReportNetState()
{
	ResolveFields();
	SourceSharpWorld_Report( "Server", DataBlob(), BlobBytes(), m_Fields );
}

void CWorld::Precache( void )
{
	// Set up game rules.
	Assert( !g_pGameRules );
	if (g_pGameRules)
	{
		delete g_pGameRules;
	}

	InstallGameRules();
	Assert( g_pGameRules );

	ss::SoundEnt_Init();

	// The rest of stock's world precache is managed's.
	SourceSharp_ManagedLevelInitPreEntity( STRING( gpGlobals->mapname ) );

	// init sentence group playback stuff from sentences.txt.
	// ok to call this multiple times, calls after first are ignored.
	SENTENCEG_Init();

	W_Precache();
}
