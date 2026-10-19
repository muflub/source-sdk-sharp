//========= SourceSharp — the client leaf system's forwarder, carried out =====//
//
// C7 cluster 12 L2 (docs/plans/c7_leaf_system.md). libss_fwd_client builds the
// IClientLeafSystem the engine and client.so's C++ call (fwd_leaf_system.cpp):
// each override hands a managed slot its arguments and carries out the
// operations the slot answers with, and the HAND batches below it ask the
// renderables and the engine what the managed registry needs. None of it may
// decide anything, and each piece of carrying-out must reach the object it
// names, in the order the answer gave.
//
// THE FAKES ARE VTABLE ARRAYS, as fwd_engine_test's: each recorder sits at the
// slot the compiler says its method occupies (VTableHook::MemberOf), the rest
// null, so a call to anything else faults rather than passes. The managed side
// is a table of recorders, as a native test of a managed-built object hands
// its own (imanaged_core.h).
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_fwd.h"
#include "fwd_leaf_system.h"
#include "vtable_hook.h"

#include "cdll_int.h"
#include "globalvars_base.h"
#include "engine/ivmodelinfo.h"
#include "ivrenderview.h"
#include "iclientrenderable.h"
#include "bsptreedata.h"
#include "clientleafsystem.h"
#include "ehandle.h"
class IGameEntity;
typedef CHandle< IGameEntity > EHANDLE;
#include "iclientshadowmgr.h"
#include "detailobjectsystem.h"
#include "imanaged_areas_client.h"

#include <stdarg.h>
#include <string.h>

#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// A fake object: word 0 is the vtable, recorders at the compiler's slots.
//-----------------------------------------------------------------------------
struct FakeTable
{
	void *slots[ 256 ];

	FakeTable() { memset( slots, 0, sizeof( slots ) ); }

	template < typename Fn >
	void Put( Fn pmf, void *pRecorder )
	{
		const int slot = VTableHook::MemberOf( pmf ).slot;
		ASSERT_GE( slot, 0 );
		ASSERT_LT( slot, 256 );
		slots[ slot ] = pRecorder;
	}
};

std::vector< std::string > g_calls;

void Call( const std::string &s ) { g_calls.push_back( s ); }

//-----------------------------------------------------------------------------
// Renderables.
//-----------------------------------------------------------------------------
struct FakeRenderable
{
	void **vtable;
	int id;
	ClientRenderHandle_t handle;
	int blend;
	bool transparent;
	bool twoPass;
	int receive;
	Vector mins, maxs, absMins, absMaxs, origin;
	const model_t *model;
};

FakeRenderable *R( void *self ) { return static_cast< FakeRenderable * >( self ); }

ClientRenderHandle_t &Rec_RenderHandle( void *self ) { return R( self )->handle; }
bool Rec_IsTransparent( void *self ) { Call( "IsTransparent " + std::to_string( R( self )->id ) ); return R( self )->transparent; }
bool Rec_IsTwoPass( void *self ) { Call( "IsTwoPass " + std::to_string( R( self )->id ) ); return R( self )->twoPass; }
const model_t *Rec_GetModel( const void *self ) { return static_cast< const FakeRenderable * >( self )->model; }
void Rec_ComputeFxBlend( void *self ) { Call( "ComputeFxBlend " + std::to_string( R( self )->id ) ); }
int Rec_GetFxBlend( void *self ) { Call( "GetFxBlend " + std::to_string( R( self )->id ) ); return R( self )->blend; }
void Rec_GetRenderBounds( void *self, Vector &mins, Vector &maxs ) { mins = R( self )->mins; maxs = R( self )->maxs; }
void Rec_GetRenderBoundsWorldspace( void *self, Vector &mins, Vector &maxs ) { mins = R( self )->absMins; maxs = R( self )->absMaxs; }
const Vector &Rec_GetRenderOrigin( void *self ) { return R( self )->origin; }
bool Rec_ShouldReceiveProjectedTextures( void *self, int flags ) { return ( R( self )->receive & flags ) != 0; }

struct RenderableTable : FakeTable
{
	RenderableTable()
	{
		Put( &IClientRenderable::RenderHandle, (void *)&Rec_RenderHandle );
		Put( &IClientRenderable::IsTransparent, (void *)&Rec_IsTransparent );
		Put( &IClientRenderable::IsTwoPass, (void *)&Rec_IsTwoPass );
		Put( &IClientRenderable::GetModel, (void *)&Rec_GetModel );
		Put( &IClientRenderable::ComputeFxBlend, (void *)&Rec_ComputeFxBlend );
		Put( &IClientRenderable::GetFxBlend, (void *)&Rec_GetFxBlend );
		Put( &IClientRenderable::GetRenderBounds, (void *)&Rec_GetRenderBounds );
		Put( &IClientRenderable::GetRenderBoundsWorldspace, (void *)&Rec_GetRenderBoundsWorldspace );
		Put( &IClientRenderable::GetRenderOrigin, (void *)&Rec_GetRenderOrigin );
		Put( &IClientRenderable::ShouldReceiveProjectedTextures, (void *)&Rec_ShouldReceiveProjectedTextures );
	}
};

RenderableTable &Renderables()
{
	static RenderableTable s_table;
	return s_table;
}

FakeRenderable Make( int id )
{
	FakeRenderable r;
	r.vtable = Renderables().slots;
	r.id = id;
	r.handle = INVALID_CLIENT_RENDER_HANDLE;
	r.blend = 255;
	r.transparent = false;
	r.twoPass = false;
	r.receive = 0;
	r.mins.Init( -1, -2, -3 );
	r.maxs.Init( 1, 2, 3 );
	r.absMins.Init( id, 0, 0 );
	r.absMaxs.Init( id + 10.0f, 5, 5 );
	r.origin.Init( id, 7, 8 );
	r.model = reinterpret_cast< const model_t * >( (intptr_t)( 0x1000 + id ) );
	return r;
}

IClientRenderable *AsRenderable( FakeRenderable &r ) { return reinterpret_cast< IClientRenderable * >( &r ); }

//-----------------------------------------------------------------------------
// The engine, model info, render view, shadow manager and detail system.
//-----------------------------------------------------------------------------
struct FakeObject
{
	void **vtable;
};

int g_nLeafCount = 0;
int Rec_LevelLeafCount( const void * ) { return g_nLeafCount; }

int Rec_GetModelType( const void *, const model_t *model ) { return (int)( (intptr_t)model & 0xF ); }

std::vector< int > g_pvsAsked;
bool Rec_AreAnyLeavesVisible( void *, int *pLeaves, int n )
{
	g_pvsAsked.assign( pLeaves, pLeaves + n );
	return n > 0 && pLeaves[ 0 ] == 4;
}

void Rec_AddShadowToReceiver( void *, ClientShadowHandle_t h, IClientRenderable *p, ShadowReceiver_t type )
{
	Call( "AddShadowToReceiver " + std::to_string( h ) + " " + std::to_string( reinterpret_cast< FakeRenderable * >( p )->id ) + " " + std::to_string( (int)type ) );
}

void Rec_RemoveAllShadowsFromReceiver( void *, IClientRenderable *p, ShadowReceiver_t type )
{
	Call( "RemoveAllShadowsFromReceiver " + std::to_string( reinterpret_cast< FakeRenderable * >( p )->id ) + " " + std::to_string( (int)type ) );
}

std::vector< FakeRenderable > g_details;
IClientRenderable *Rec_GetDetailModel( void *, int idx )
{
	return idx >= 0 && idx < (int)g_details.size() ? AsRenderable( g_details[ idx ] ) : nullptr;
}

void Rec_Con_NPrintf( void *, int pos, const char *fmt, ... )
{
	char sz[ 256 ];
	va_list ap;
	va_start( ap, fmt );
	vsnprintf( sz, sizeof( sz ), fmt, ap );
	va_end( ap );
	Call( "Con_NPrintf " + std::to_string( pos ) + " " + sz );
}

// The engine's culling questions answer from the box's low x: area (x < 100
// touches), cull (x >= 200 culls), occluded (x in 50..59).
bool Rec_DoesBoxTouchAreaFrustum( void *, const Vector &mins, const Vector &, int area )
{
	Call( "Area " + std::to_string( (int)mins.x ) + " " + std::to_string( area ) );
	return mins.x < 100;
}
bool Rec_CullBox( void *, const Vector &mins, const Vector & )
{
	Call( "Cull " + std::to_string( (int)mins.x ) );
	return mins.x >= 200;
}
bool Rec_IsOccluded( void *, const Vector &mins, const Vector & )
{
	Call( "Occluded " + std::to_string( (int)mins.x ) );
	return mins.x >= 50 && mins.x < 60;
}

int Rec_GetLeavesArea( void *, int *pLeaves, int n )
{
	int sum = 0;
	for ( int i = 0; i < n; i++ )
		sum += pLeaves[ i ];
	return n == 0 ? -1 : sum;
}

// The BSP query: a box touches the leaves from its low x to its high x, in
// whole tens.
bool Rec_EnumerateLeavesInBox( void *, Vector const &mins, Vector const &maxs, ISpatialLeafEnumerator *pEnum, intp context )
{
	for ( int leaf = (int)( mins.x / 10 ); leaf <= (int)( maxs.x / 10 ); leaf++ )
		if ( !pEnum->EnumerateLeaf( leaf, context ) )
			return false;
	return true;
}

struct QueryTable : FakeTable
{
	QueryTable() { Put( &ISpatialQuery::EnumerateLeavesInBox, (void *)&Rec_EnumerateLeavesInBox ); }
};
QueryTable g_queryTable;
FakeObject g_query = { g_queryTable.slots };
ISpatialQuery *Rec_GetBSPTreeQuery( void * ) { return reinterpret_cast< ISpatialQuery * >( &g_query ); }

struct EngineTable : FakeTable
{
	EngineTable()
	{
		Put( &IVEngineClient::LevelLeafCount, (void *)&Rec_LevelLeafCount );
		Put( &IVEngineClient::Con_NPrintf, (void *)&Rec_Con_NPrintf );
		Put( &IVEngineClient::DoesBoxTouchAreaFrustum, (void *)&Rec_DoesBoxTouchAreaFrustum );
		Put( &IVEngineClient::CullBox, (void *)&Rec_CullBox );
		Put( &IVEngineClient::IsOccluded, (void *)&Rec_IsOccluded );
		Put( &IVEngineClient::GetLeavesArea, (void *)&Rec_GetLeavesArea );
		Put( &IVEngineClient::GetBSPTreeQuery, (void *)&Rec_GetBSPTreeQuery );
	}
};

struct ModelInfoTable : FakeTable
{
	ModelInfoTable() { Put( &IVModelInfoClient::GetModelType, (void *)&Rec_GetModelType ); }
};

struct RenderTable : FakeTable
{
	RenderTable() { Put( &IVRenderView::AreAnyLeavesVisible, (void *)&Rec_AreAnyLeavesVisible ); }
};

struct ShadowTable : FakeTable
{
	ShadowTable()
	{
		Put( &IClientShadowMgr::AddShadowToReceiver, (void *)&Rec_AddShadowToReceiver );
		Put( &IClientShadowMgr::RemoveAllShadowsFromReceiver, (void *)&Rec_RemoveAllShadowsFromReceiver );
	}
};

struct DetailTable : FakeTable
{
	DetailTable() { Put( &IDetailObjectSystem::GetDetailModel, (void *)&Rec_GetDetailModel ); }
};

EngineTable g_engineTable;
ModelInfoTable g_modelInfoTable;
RenderTable g_renderTable;
ShadowTable g_shadowTable;
DetailTable g_detailTable;
FakeObject g_engineObject = { g_engineTable.slots };
FakeObject g_modelInfoObject = { g_modelInfoTable.slots };
FakeObject g_renderObject = { g_renderTable.slots };
FakeObject g_shadowObject = { g_shadowTable.slots };
FakeObject g_detailObject = { g_detailTable.slots };

IVEngineClient *g_pEngine = reinterpret_cast< IVEngineClient * >( &g_engineObject );
IVModelInfoClient *g_pModelInfo = reinterpret_cast< IVModelInfoClient * >( &g_modelInfoObject );
IVRenderView *g_pRender = reinterpret_cast< IVRenderView * >( &g_renderObject );
IClientShadowMgr *g_pShadowMgr = reinterpret_cast< IClientShadowMgr * >( &g_shadowObject );
CGlobalVarsBase g_globals( false );
CGlobalVarsBase *g_pGlobals = &g_globals;

IDetailObjectSystem *DetailSystem() { return reinterpret_cast< IDetailObjectSystem * >( &g_detailObject ); }

std::vector< std::pair< void *, int > > g_appended;
void Append( void *pVector, IClientRenderable *p ) { g_appended.push_back( { pVector, reinterpret_cast< FakeRenderable * >( p )->id } ); }

//-----------------------------------------------------------------------------
// The managed side: one recorder per slot the tests use; the operations a
// slot answers with come from g_answer, a chunk at a time.
//-----------------------------------------------------------------------------
std::vector< int64_t > g_answer;   // the operations still to hand over, four words each
int g_nTakeOps = 0;

int32_t HandOver( int64_t *pOps, int32_t nMaxOps )
{
	const int32_t n = std::min< int32_t >( nMaxOps, (int32_t)( g_answer.size() / 4 ) );
	memcpy( pOps, g_answer.data(), sizeof( int64_t ) * 4 * n );
	g_answer.erase( g_answer.begin(), g_answer.begin() + 4 * n );
	return n;
}

void Op( int64_t kind, int64_t a, int64_t b = 0, int64_t c = 0 )
{
	g_answer.push_back( kind );
	g_answer.push_back( a );
	g_answer.push_back( b );
	g_answer.push_back( c );
}

std::vector< int > g_leaves;
float g_originForward[ 6 ];
int g_frames[ 3 ];
void *g_pDetailAsked = nullptr;

void M_LevelInitPreEntity( IManagedLeafSystemClient *, int32_t n ) { Call( "M.LevelInitPreEntity " + std::to_string( n ) ); }
int32_t M_LevelShutdownPostEntity( IManagedLeafSystemClient *, int64_t *pOps, int32_t nMax ) { return HandOver( pOps, nMax ); }
std::string PendingLine( const char *pName, const int32_t *pEntries, int32_t n );
std::vector< std::string > g_pending;
// PreRender's queue, as managed receives it: a "PreRender ..." line in g_pending.
int32_t M_PreRender( IManagedLeafSystemClient *, const int32_t *pPending, int32_t nPending, int64_t *pOps, int32_t nMax )
{
	g_pending.push_back( PendingLine( "PreRender", pPending, nPending ) );
	return HandOver( pOps, nMax );
}
int32_t M_TakeOps( IManagedLeafSystemClient *, int64_t *pOps, int32_t nMax ) { ++g_nTakeOps; return HandOver( pOps, nMax ); }
int32_t M_CreateRenderableHandle( IManagedLeafSystemClient *, void *p, int32_t bStatic, int32_t type, int32_t bTransparent, int32_t bTwoPass )
{
	Call( "M.CreateRenderableHandle " + std::to_string( reinterpret_cast< FakeRenderable * >( p )->id ) + " " + std::to_string( bStatic ) + " " +
	      std::to_string( type ) + " " + std::to_string( bTransparent ) + " " + std::to_string( bTwoPass ) );
	return 42;
}
int32_t M_AddRenderable( IManagedLeafSystemClient *, void *p, int32_t group, int32_t type )
{
	Call( "M.AddRenderable " + std::to_string( reinterpret_cast< FakeRenderable * >( p )->id ) + " " + std::to_string( group ) + " " + std::to_string( type ) );
	return 7;
}
int32_t M_RemoveRenderable( IManagedLeafSystemClient *, int32_t h, int64_t *pOps, int32_t nMax )
{
	Call( "M.RemoveRenderable " + std::to_string( h ) );
	return HandOver( pOps, nMax );
}
int32_t M_AddRenderableToLeaves( IManagedLeafSystemClient *, int32_t h, const uint16_t *pLeaves, int32_t n, int64_t *pOps, int32_t nMax )
{
	g_leaves.assign( pLeaves, pLeaves + n );
	Call( "M.AddRenderableToLeaves " + std::to_string( h ) );
	return HandOver( pOps, nMax );
}
int32_t M_GetRenderableLeaves( IManagedLeafSystemClient *, int32_t h, int32_t *pLeaves )
{
	if ( h != 7 )
		return -1;
	pLeaves[ 0 ] = 4;
	pLeaves[ 1 ] = 9;
	return 2;
}
int32_t M_GetRenderableLeaf( IManagedLeafSystemClient *, int32_t h, int32_t bHas, int32_t it, int32_t *pOut )
{
	Call( "M.GetRenderableLeaf " + std::to_string( h ) + " " + std::to_string( bHas ) + " " + std::to_string( it ) );
	if ( bHas && it >= 3 )
		return 0;
	pOut[ 0 ] = 100 + ( bHas ? it : 0 );
	pOut[ 1 ] = bHas ? it + 1 : 1;
	return 1;
}
struct CTestSubData : public CClientLeafSubSystemData
{
	explicit CTestSubData( int tag ) : m_tag( tag ) {}
	~CTestSubData() { Call( "deleted " + std::to_string( m_tag ) ); }
	int m_tag;
};
void *M_SetSubSystemDataInLeaf( IManagedLeafSystemClient *, int32_t leaf, int32_t, void *pData )
{
	Call( "M.SetSubSystemDataInLeaf " + std::to_string( leaf ) + " " + std::to_string( static_cast< CTestSubData * >( pData )->m_tag ) );
	return leaf == 1 ? new CTestSubData( 99 ) : nullptr;
}
int32_t M_GetDetailObjectsInLeaf( IManagedLeafSystemClient *, int32_t leaf, int32_t *pOut )
{
	if ( leaf != 3 )
		return 0;
	pOut[ 0 ] = 11;
	pOut[ 1 ] = 4;
	return 1;
}
void M_DrawDetailObjectsInLeaf( IManagedLeafSystemClient *, int32_t leaf, int32_t frame, int32_t *pOut )
{
	pOut[ 0 ] = leaf;
	pOut[ 1 ] = frame;
}
void *g_pSystemHanded = nullptr;
void M_ComputeTranslucentRenderLeaf( IManagedLeafSystemClient *, const uint16_t *pLeaves, int32_t n, int32_t frame, int32_t view, int32_t global,
                                     void *pSystem, const int32_t *pPending, int32_t nPending )
{
	if ( nPending > 0 )
		g_pending.push_back( PendingLine( "Translucent", pPending, nPending ) );
	g_pSystemHanded = pSystem;
	g_leaves.assign( pLeaves, pLeaves + n );
	g_frames[ 0 ] = frame;
	g_frames[ 1 ] = view;
	g_frames[ 2 ] = global;
}
int32_t M_BuildRenderablesList( IManagedLeafSystemClient *, const uint16_t *pLeaves, int32_t n, const float *pOriginForward, int32_t frame,
                                int32_t detailFrame, int32_t flags, void *pDetail, int64_t *pOps, int32_t nMax,
                                const int32_t *pPending, int32_t nPending )
{
	if ( nPending > 0 )
		g_pending.push_back( PendingLine( "BuildList", pPending, nPending ) );
	g_leaves.assign( pLeaves, pLeaves + n );
	memcpy( g_originForward, pOriginForward, sizeof( g_originForward ) );
	g_frames[ 0 ] = frame;
	g_frames[ 1 ] = detailFrame;
	g_frames[ 2 ] = flags;
	g_pDetailAsked = pDetail;
	return HandOver( pOps, nMax );
}
int32_t M_CollateViewModelRenderables( IManagedLeafSystemClient *, int64_t *pOps, int32_t nMax, void *pSystem,
                                       const int32_t *pPending, int32_t nPending )
{
	if ( nPending > 0 )
		g_pending.push_back( PendingLine( "Collate", pPending, nPending ) );
	g_pSystemHanded = pSystem;
	return HandOver( pOps, nMax );
}
// The queued calls as managed receives them, one line a hand-over.
std::string PendingLine( const char *pName, const int32_t *pEntries, int32_t n )
{
	std::string line = pName;
	for ( int32_t i = 0; pEntries && i < n; i++ )
		line += " " + std::to_string( pEntries[ 3 * i ] ) + ":" + std::to_string( pEntries[ 3 * i + 1 ] ) + ":" + std::to_string( pEntries[ 3 * i + 2 ] );
	return line;
}
// The queued calls' own slots: a call that reached one crossed on its own.
void M_RenderableChanged( IManagedLeafSystemClient *, int32_t handle ) { Call( "M.RenderableChanged " + std::to_string( handle ) ); }
void M_ChangeRenderableRenderGroup( IManagedLeafSystemClient *, int32_t handle, int32_t group )
{
	Call( "M.ChangeRenderableRenderGroup " + std::to_string( handle ) + " " + std::to_string( group ) );
}
void M_SetRenderGroup( IManagedLeafSystemClient *, int32_t handle, int32_t group )
{
	Call( "M.SetRenderGroup " + std::to_string( handle ) + " " + std::to_string( group ) );
}
void M_Pending( IManagedLeafSystemClient *, const int32_t *pEntries, int32_t n )
{
	g_pending.push_back( PendingLine( "Pending", pEntries, n ) );
	Call( "M.Pending " + std::to_string( n ) );
}
int32_t M_AddShadow( IManagedLeafSystemClient *, int32_t user, int32_t flags ) { return user + flags; }
int32_t M_ProjectShadow( IManagedLeafSystemClient *, int32_t h, const int32_t *pLeaves, int32_t n, int64_t *pOps, int32_t nMax )
{
	g_leaves.assign( pLeaves, pLeaves + n );
	Call( "M.ProjectShadow " + std::to_string( h ) );
	return HandOver( pOps, nMax );
}
int32_t M_EnumerateShadowsInLeaves( IManagedLeafSystemClient *, const uint16_t *pLeaves, int32_t n, int64_t *pOps, int32_t nMax )
{
	g_leaves.assign( pLeaves, pLeaves + n );
	return HandOver( pOps, nMax );
}

SSManagedLeafSystemClientTable ManagedTable()
{
	SSManagedLeafSystemClientTable t;
	memset( &t, 0, sizeof( t ) );
	t.LevelInitPreEntity = M_LevelInitPreEntity;
	t.LevelShutdownPostEntity = M_LevelShutdownPostEntity;
	t.PreRender = M_PreRender;
	t.TakeOps = M_TakeOps;
	t.CreateRenderableHandle = M_CreateRenderableHandle;
	t.AddRenderable = M_AddRenderable;
	t.RemoveRenderable = M_RemoveRenderable;
	t.AddRenderableToLeaves = M_AddRenderableToLeaves;
	t.GetRenderableLeaves = M_GetRenderableLeaves;
	t.GetRenderableLeaf = M_GetRenderableLeaf;
	t.SetSubSystemDataInLeaf = M_SetSubSystemDataInLeaf;
	t.GetDetailObjectsInLeaf = M_GetDetailObjectsInLeaf;
	t.DrawDetailObjectsInLeaf = M_DrawDetailObjectsInLeaf;
	t.ComputeTranslucentRenderLeaf = M_ComputeTranslucentRenderLeaf;
	t.BuildRenderablesList = M_BuildRenderablesList;
	t.CollateViewModelRenderables = M_CollateViewModelRenderables;
	t.AddShadow = M_AddShadow;
	t.ProjectShadow = M_ProjectShadow;
	t.ProjectFlashlight = M_ProjectShadow;
	t.EnumerateShadowsInLeaves = M_EnumerateShadowsInLeaves;
	t.Pending = M_Pending;
	t.RenderableChanged = M_RenderableChanged;
	t.ChangeRenderableRenderGroup = M_ChangeRenderableRenderGroup;
	t.SetRenderGroup = M_SetRenderGroup;
	return t;
}

SSManagedLeafSystemClientTable g_managedTable = ManagedTable();
IManagedLeafSystemClient g_managed( &g_managedTable );
IManagedLeafSystemClient *g_pManaged = &g_managed;

//-----------------------------------------------------------------------------
// The forwarder, built through its table as client.so builds it.
//-----------------------------------------------------------------------------
enum LeafSystemSlot { kSizeOf, kConstruct, kComputeFxBlends, kFacts, kReceives, kEnumerateLeaves, kLeavesAreas, kBoxQueries, kDetailModels, kSlots };

const SSForwarderTable *Table() { return SSFwdClient_Table( "SSFwd_LeafSystem" ); }

template < typename Fn >
Fn Slot( int slot )
{
	const SSForwarderTable *pTable = Table();
	return pTable && slot < pTable->nSlots ? reinterpret_cast< Fn >( pTable->ppSlots[ slot ] ) : nullptr;
}

SSLeafSystemEnv Env( IManagedLeafSystemClient *const *ppTarget )
{
	SSLeafSystemEnv env;
	env.ppTarget = ppTarget;
	env.ppEngine = &g_pEngine;
	env.ppModelInfo = &g_pModelInfo;
	env.ppRender = &g_pRender;
	env.ppShadowMgr = &g_pShadowMgr;
	env.ppGlobals = &g_pGlobals;
	env.pfnDetailObjectSystem = DetailSystem;
	env.pfnAppendRenderable = Append;
	return env;
}

struct Built
{
	alignas( 16 ) unsigned char storage[ kSSLeafSystemBytes ];
	SSLeafSystemEnv env;
	IClientLeafSystem *pSystem = nullptr;

	explicit Built( IManagedLeafSystemClient *const *ppTarget = &g_pManaged ) : env( Env( ppTarget ) )
	{
		typedef void *( *Construct_t )( void *, const SSLeafSystemEnv * );
		Construct_t construct = Slot< Construct_t >( kConstruct );
		EXPECT_NE( nullptr, construct );
		if ( construct )
			pSystem = static_cast< IClientLeafSystem * >( construct( storage, &env ) );
		EXPECT_NE( nullptr, pSystem );
		g_calls.clear();
		g_answer.clear();
		g_nTakeOps = 0;
		g_appended.clear();
		g_pending.clear();
	}
};

} // namespace

TEST( FwdLeafSystem, TheLibraryServesTheLeafSystemsNineDistinctSlots )
{
	const SSForwarderTable *pTable = Table();
	ASSERT_NE( nullptr, pTable );
	ASSERT_EQ( (int)kSlots, pTable->nSlots );
	for ( int i = 0; i < pTable->nSlots; i++ )
	{
		EXPECT_NE( nullptr, pTable->ppSlots[ i ] ) << "slot " << i;
		for ( int j = 0; j < i; j++ )
			EXPECT_NE( pTable->ppSlots[ j ], pTable->ppSlots[ i ] ) << "slots " << j << " and " << i;
	}
	EXPECT_EQ( (int)kSSLeafSystemBytes, Slot< int ( * )() >( kSizeOf )() );
}

TEST( FwdLeafSystem, ConstructRefusesWithoutMemoryOrAnEnvironment )
{
	typedef void *( *Construct_t )( void *, const SSLeafSystemEnv * );
	Construct_t construct = Slot< Construct_t >( kConstruct );
	ASSERT_NE( nullptr, construct );
	alignas( 16 ) unsigned char storage[ kSSLeafSystemBytes ];
	SSLeafSystemEnv env = Env( &g_pManaged );
	EXPECT_EQ( nullptr, construct( nullptr, &env ) );
	EXPECT_EQ( nullptr, construct( storage, nullptr ) );
	env.ppShadowMgr = nullptr;
	EXPECT_EQ( nullptr, construct( storage, &env ) );
	env = Env( &g_pManaged );
	EXPECT_NE( nullptr, construct( storage, &env ) );
}

TEST( FwdLeafSystem, ItAnswersAsTheClientLeafSystemByName )
{
	Built b;
	EXPECT_STREQ( "CClientLeafSystem", b.pSystem->Name() );
	EXPECT_TRUE( b.pSystem->IsPerFrame() );
	// The engine's interface is the object's first base: the same address.
	EXPECT_EQ( (void *)b.pSystem, (void *)static_cast< IClientLeafSystemEngine * >( b.pSystem ) );
}

TEST( FwdLeafSystem, WithNoManagedAreaEveryCallDoesNothing )
{
	IManagedLeafSystemClient *pNone = nullptr;
	Built b( &pNone );
	FakeRenderable r = Make( 5 );
	b.pSystem->CreateRenderableHandle( AsRenderable( r ), true );
	b.pSystem->AddRenderable( AsRenderable( r ), RENDER_GROUP_OPAQUE_ENTITY );
	b.pSystem->RemoveRenderable( 3 );
	b.pSystem->PreRender();
	b.pSystem->LevelInitPreEntity();
	b.pSystem->LevelShutdownPostEntity();
	int leaves[ 128 ];
	EXPECT_EQ( -1, b.pSystem->GetRenderableLeaves( 3, leaves ) );
	EXPECT_FALSE( b.pSystem->IsRenderableInPVS( AsRenderable( r ) ) );
	EXPECT_FALSE( b.pSystem->ShouldDrawDetailObjectsInLeaf( 1, 1 ) );
	EXPECT_EQ( (ClientLeafShadowHandle_t)CLIENT_LEAF_SHADOW_INVALID_HANDLE, b.pSystem->AddShadow( 1, 2 ) );
	EXPECT_EQ( INVALID_CLIENT_RENDER_HANDLE, r.handle );
	EXPECT_TRUE( g_calls.empty() ) << g_calls.front();
}

TEST( FwdLeafSystem, CreateRenderableHandleHandsTheModelsKindOverAndWritesTheHandle )
{
	Built b;
	FakeRenderable r = Make( 3 );   // its model's kind is 3 (0x1003 & 0xF)
	r.transparent = true;
	r.twoPass = true;
	b.pSystem->CreateRenderableHandle( AsRenderable( r ), true );
	EXPECT_EQ( 42, r.handle );
	ASSERT_FALSE( g_calls.empty() );
	EXPECT_EQ( "M.CreateRenderableHandle 3 1 3 1 1", g_calls.back() );
}

TEST( FwdLeafSystem, AddRenderableHandsTheGroupAndKindOverAndWritesTheHandle )
{
	Built b;
	FakeRenderable r = Make( 1 );
	b.pSystem->AddRenderable( AsRenderable( r ), RENDER_GROUP_TWOPASS );
	EXPECT_EQ( 7, r.handle );
	ASSERT_EQ( 1u, g_calls.size() );
	EXPECT_EQ( "M.AddRenderable 1 9 1", g_calls[ 0 ] );
}

TEST( FwdLeafSystem, RemoveRenderableCarriesOutWhatManagedAnswersInOrder )
{
	Built b;
	FakeRenderable r = Make( 2 );
	r.handle = 5;
	Op( 3, (intptr_t)&r, INVALID_CLIENT_RENDER_HANDLE );   // SetRenderHandle
	Op( 2, (intptr_t)&r, 0, 2 );                          // RemoveAllShadowsFromReceiver, studio
	b.pSystem->RemoveRenderable( 5 );
	EXPECT_EQ( INVALID_CLIENT_RENDER_HANDLE, r.handle );
	const std::vector< std::string > expected = { "M.RemoveRenderable 5", "RemoveAllShadowsFromReceiver 2 2" };
	EXPECT_EQ( expected, g_calls );
	EXPECT_EQ( 0, g_nTakeOps ) << "a short answer is not drained further";
}

TEST( FwdLeafSystem, ALongAnswerIsDrainedThroughTakeOpsIntoTheList )
{
	Built b;
	std::vector< FakeRenderable > things;
	for ( int i = 0; i < 1030; i++ )
		things.push_back( Make( i ) );
	for ( int i = 0; i < 1030; i++ )
		Op( 5, (intptr_t)&things[ i ], i | ( (int64_t)( i % 7 ) << 16 ) | ( (int64_t)( i & 1 ) << 32 ), i < 1000 ? RENDER_GROUP_OPAQUE_ENTITY : RENDER_GROUP_TRANSLUCENT_ENTITY );
	Op( 5, 0, 0, RENDER_GROUP_COUNT );   // out of range: written nowhere

	CClientRenderablesList *pList = static_cast< CClientRenderablesList * >( malloc( sizeof( CClientRenderablesList ) ) );
	for ( int g = 0; g < RENDER_GROUP_COUNT; g++ )
		pList->m_RenderGroupCounts[ g ] = 0;
	pList->m_RenderGroupCounts[ RENDER_GROUP_TRANSLUCENT_ENTITY ] = 2;   // the list is appended to, as stock's was
	WorldListInfo_t world;
	LeafIndex_t leafList[ 3 ] = { 9, 4, 1 };
	world.m_LeafCount = 3;
	world.m_pLeafList = leafList;
	SetupRenderInfo_t info;
	info.m_pWorldListInfo = &world;
	info.m_pRenderList = pList;
	info.m_vecRenderOrigin.Init( 1, 2, 3 );
	info.m_vecRenderForward.Init( 0, 1, 0 );
	info.m_nRenderFrame = 77;
	info.m_nDetailBuildFrame = 66;
	info.m_bDrawDetailObjects = true;
	info.m_bDrawTranslucentObjects = false;
	b.pSystem->BuildRenderablesList( info );

	EXPECT_EQ( 16, g_nTakeOps ) << "64 in the answer, then 15 chunks of 64 and one of 7: 1,031 operations, a 2 KB buffer";
	EXPECT_EQ( 1000, pList->m_RenderGroupCounts[ RENDER_GROUP_OPAQUE_ENTITY ] );
	EXPECT_EQ( 32, pList->m_RenderGroupCounts[ RENDER_GROUP_TRANSLUCENT_ENTITY ] );
	for ( int i = 0; i < 1000; i++ )
	{
		const CClientRenderablesList::CEntry &e = pList->m_RenderGroups[ RENDER_GROUP_OPAQUE_ENTITY ][ i ];
		EXPECT_EQ( (void *)&things[ i ], (void *)e.m_pRenderable ) << i;
		EXPECT_EQ( i, e.m_RenderHandle ) << i;
		EXPECT_EQ( i % 7, e.m_iWorldListInfoLeaf ) << i;
		EXPECT_EQ( i & 1, e.m_TwoPass ) << i;
	}
	EXPECT_EQ( (void *)&things[ 1000 ], (void *)pList->m_RenderGroups[ RENDER_GROUP_TRANSLUCENT_ENTITY ][ 2 ].m_pRenderable );

	// The view as it was handed over.
	const std::vector< int > leaves = { 9, 4, 1 };
	EXPECT_EQ( leaves, g_leaves );
	EXPECT_EQ( 1.0f, g_originForward[ 0 ] );
	EXPECT_EQ( 3.0f, g_originForward[ 2 ] );
	EXPECT_EQ( 1.0f, g_originForward[ 4 ] );
	EXPECT_EQ( 77, g_frames[ 0 ] );
	EXPECT_EQ( 66, g_frames[ 1 ] );
#ifdef _DEBUG
	EXPECT_EQ( 1 | 4, g_frames[ 2 ] ) << "detail, no translucent, the debug build's cl_drawleaf filter";
#else
	EXPECT_EQ( 1, g_frames[ 2 ] ) << "detail, no translucent";
#endif
	EXPECT_EQ( (void *)DetailSystem(), g_pDetailAsked );
	free( pList );
}

TEST( FwdLeafSystem, AFullGroupStopsTheListAndAnOverflowWarnsPerGroup )
{
	Built b;
	FakeRenderable r = Make( 1 );
	CClientRenderablesList *pList = static_cast< CClientRenderablesList * >( malloc( sizeof( CClientRenderablesList ) ) );
	for ( int g = 0; g < RENDER_GROUP_COUNT; g++ )
		pList->m_RenderGroupCounts[ g ] = 0;
	pList->m_RenderGroupCounts[ RENDER_GROUP_OPAQUE_STATIC ] = CClientRenderablesList::MAX_GROUP_ENTITIES;
	Op( 5, (intptr_t)&r, 0, RENDER_GROUP_OPAQUE_STATIC );
	Op( 6, 0, ( 1LL << RENDER_GROUP_OPAQUE_STATIC ) | ( 1LL << RENDER_GROUP_OPAQUE_ENTITY ) );
	WorldListInfo_t world;
	LeafIndex_t leaf = 0;
	world.m_LeafCount = 1;
	world.m_pLeafList = &leaf;
	SetupRenderInfo_t info;
	info.m_pWorldListInfo = &world;
	info.m_pRenderList = pList;
	info.m_vecRenderOrigin.Init();
	info.m_vecRenderForward.Init();
	b.pSystem->BuildRenderablesList( info );
	EXPECT_EQ( CClientRenderablesList::MAX_GROUP_ENTITIES, pList->m_RenderGroupCounts[ RENDER_GROUP_OPAQUE_STATIC ] );
	const std::vector< std::string > expected = {
		"Con_NPrintf 10 Warning: overflowed CClientRenderablesList group 6",
		"Con_NPrintf 10 Warning: overflowed CClientRenderablesList group 7",
	};
	EXPECT_EQ( expected, g_calls );
	free( pList );
}

TEST( FwdLeafSystem, TheTranslucentLeafPassHandsTheGlobalFrameOver )
{
	Built b;
	g_globals.framecount = 1234;
	LeafIndex_t leaves[ 2 ] = { 3, 8 };
	b.pSystem->ComputeTranslucentRenderLeaf( 2, leaves, nullptr, 55, 2 );
	const std::vector< int > expected = { 3, 8 };
	EXPECT_EQ( expected, g_leaves );
	EXPECT_EQ( 55, g_frames[ 0 ] );
	EXPECT_EQ( 2, g_frames[ 1 ] );
	EXPECT_EQ( 1234, g_frames[ 2 ] );
	EXPECT_EQ( (void *)b.pSystem, g_pSystemHanded ) << "the blend batch's flush needs the leaf system";
}

TEST( FwdLeafSystem, EnumerateShadowsInLeavesCallsTheCallerBackPerShadow )
{
	struct CEnum : public IClientLeafShadowEnum
	{
		std::vector< int > seen;
		void EnumShadow( ClientShadowHandle_t h ) override { seen.push_back( h ); }
	} e;
	Built b;
	Op( 8, 0, 100 );
	Op( 8, 0, 101 );
	Op( 8, 0, 7 );
	LeafIndex_t leaves[ 2 ] = { 1, 2 };
	b.pSystem->EnumerateShadowsInLeaves( 2, leaves, &e );
	const std::vector< int > expected = { 100, 101, 7 };
	EXPECT_EQ( expected, e.seen );
}

TEST( FwdLeafSystem, TheViewModelsAreAppendedToTheVectorTheirSplitNames )
{
	Built b;
	FakeRenderable a = Make( 1 ), c = Make( 2 ), d = Make( 3 );
	Op( 7, (intptr_t)&a, 0 );
	Op( 7, (intptr_t)&c, 1 );
	Op( 7, (intptr_t)&d, 0 );
	// Two vectors' storage, never constructed: the forwarder only hands their
	// addresses to the append callback (a CUtlVector would need the heap).
	typedef CUtlVector< IClientRenderable * > Vector_t;
	alignas( Vector_t ) unsigned char opaqueBytes[ sizeof( Vector_t ) ], translucentBytes[ sizeof( Vector_t ) ];
	Vector_t &opaque = *reinterpret_cast< Vector_t * >( opaqueBytes );
	Vector_t &translucent = *reinterpret_cast< Vector_t * >( translucentBytes );
	b.pSystem->CollateViewModelRenderables( opaque, translucent );
	ASSERT_EQ( 3u, g_appended.size() );
	EXPECT_EQ( (void *)&opaque, g_appended[ 0 ].first );
	EXPECT_EQ( 1, g_appended[ 0 ].second );
	EXPECT_EQ( (void *)&translucent, g_appended[ 1 ].first );
	EXPECT_EQ( 2, g_appended[ 1 ].second );
	EXPECT_EQ( (void *)&opaque, g_appended[ 2 ].first );
	EXPECT_EQ( 3, g_appended[ 2 ].second );
}

TEST( FwdLeafSystem, SubSystemDataManagedReplacesIsDeletedAndAShutdownDeletesTheRest )
{
	Built b;
	b.pSystem->SetSubSystemDataInLeaf( 1, CLSUBSYSTEM_DETAILOBJECTS, new CTestSubData( 5 ) );
	b.pSystem->SetSubSystemDataInLeaf( 2, CLSUBSYSTEM_DETAILOBJECTS, new CTestSubData( 6 ) );
	Op( 4, (intptr_t) new CTestSubData( 31 ) );
	Op( 4, (intptr_t) new CTestSubData( 32 ) );
	b.pSystem->LevelShutdownPostEntity();
	const std::vector< std::string > expected = {
		"M.SetSubSystemDataInLeaf 1 5", "deleted 99", "M.SetSubSystemDataInLeaf 2 6", "deleted 31", "deleted 32",
	};
	EXPECT_EQ( expected, g_calls );
}

TEST( FwdLeafSystem, ShadowsAreProjectedAndTheirReceiversAddedAsAnswered )
{
	Built b;
	FakeRenderable r = Make( 4 ), s = Make( 9 );
	Op( 1, (intptr_t)&r, 102, 0 );
	Op( 1, (intptr_t)&s, 102, 1 );
	const int leaves[ 2 ] = { 6, 3 };
	b.pSystem->ProjectFlashlight( 12, 2, leaves );
	const std::vector< std::string > expected = { "M.ProjectShadow 12", "AddShadowToReceiver 102 4 0", "AddShadowToReceiver 102 9 1" };
	EXPECT_EQ( expected, g_calls );
	const std::vector< int > handed = { 6, 3 };
	EXPECT_EQ( handed, g_leaves );
	EXPECT_EQ( 105, b.pSystem->AddShadow( 100, 5 ) );
}

TEST( FwdLeafSystem, TheStaticPropsLeavesAreHandedOver )
{
	Built b;
	unsigned short leaves[ 3 ] = { 40, 41, 2 };
	b.pSystem->AddRenderableToLeaves( 6, 3, leaves );
	const std::vector< int > expected = { 40, 41, 2 };
	EXPECT_EQ( expected, g_leaves );
	ASSERT_FALSE( g_calls.empty() );
	EXPECT_EQ( "M.AddRenderableToLeaves 6", g_calls[ 0 ] );
}

TEST( FwdLeafSystem, ThePvsIsTheRenderViewsAnswerOverTheLeavesManagedGives )
{
	Built b;
	FakeRenderable r = Make( 1 );
	r.handle = 7;
	EXPECT_TRUE( b.pSystem->IsRenderableInPVS( AsRenderable( r ) ) );
	const std::vector< int > expected = { 4, 9 };
	EXPECT_EQ( expected, g_pvsAsked );
	r.handle = 8;   // in no leaf: the render view is not asked
	g_pvsAsked.clear();
	EXPECT_FALSE( b.pSystem->IsRenderableInPVS( AsRenderable( r ) ) );
	EXPECT_TRUE( g_pvsAsked.empty() );
}

TEST( FwdLeafSystem, ALeafWalkAndTheDetailRangesAreManagedsAnswers )
{
	Built b;
	int leaf = -1, it = -1;
	ASSERT_TRUE( b.pSystem->GetRenderableLeaf( 4, &leaf, nullptr, &it ) );
	EXPECT_EQ( 100, leaf );
	EXPECT_EQ( 1, it );
	const int in = 2;
	ASSERT_TRUE( b.pSystem->GetRenderableLeaf( 4, &leaf, &in, &it ) );
	EXPECT_EQ( 102, leaf );
	EXPECT_EQ( 3, it );
	const int end = 3;
	EXPECT_FALSE( b.pSystem->GetRenderableLeaf( 4, &leaf, &end, &it ) );
	EXPECT_FALSE( b.pSystem->GetRenderableLeaf( 4, nullptr, nullptr, nullptr ) );

	int first = -9, count = -9;
	b.pSystem->GetDetailObjectsInLeaf( 2, first, count );
	EXPECT_EQ( -9, first ) << "a leaf managed refuses leaves the caller's values";
	b.pSystem->GetDetailObjectsInLeaf( 3, first, count );
	EXPECT_EQ( 11, first );
	EXPECT_EQ( 4, count );
	b.pSystem->DrawDetailObjectsInLeaf( 8, 21, first, count );
	EXPECT_EQ( 8, first );
	EXPECT_EQ( 21, count );
}

TEST( FwdLeafSystem, TheLevelsLeafCountIsTheEngines )
{
	Built b;
	g_nLeafCount = 4321;
	b.pSystem->LevelInitPreEntity();
	ASSERT_EQ( 1u, g_calls.size() );
	EXPECT_EQ( "M.LevelInitPreEntity 4321", g_calls[ 0 ] );
}

//-----------------------------------------------------------------------------
// The batches.
//-----------------------------------------------------------------------------
TEST( FwdLeafSystem, FactsAsksEachRenderableOnlyWhatItsMaskSays )
{
	auto facts = Slot< void ( * )( void **, const int32_t *, int32_t, int32_t * ) >( kFacts );
	ASSERT_NE( nullptr, facts );
	FakeRenderable a = Make( 10 ), c = Make( 20 );
	a.blend = 128;
	c.transparent = true;
	void *pp[ 2 ] = { &a, &c };
	const int32_t what[ 2 ] = { 1 | 4, 2 | 8 | 16 };
	int32_t out[ 34 ];
	for ( int i = 0; i < 34; i++ )
		out[ i ] = -77;
	g_calls.clear();
	facts( pp, what, 2, out );
	auto f = []( int32_t w ) { float v; memcpy( &v, &w, sizeof( v ) ); return v; };
	EXPECT_EQ( 128, out[ 0 ] );
	EXPECT_EQ( -77, out[ 1 ] ) << "transparency not asked of the first";
	EXPECT_EQ( 10.0f, f( out[ 2 ] ) );
	EXPECT_EQ( 20.0f, f( out[ 5 ] ) );
	EXPECT_EQ( -77, out[ 8 ] ) << "render bounds not asked of the first";
	EXPECT_EQ( -77, out[ 17 + 0 ] ) << "blend not asked of the second";
	EXPECT_EQ( 1, out[ 17 + 1 ] );
	EXPECT_EQ( -77, out[ 17 + 2 ] ) << "world bounds not asked of the second";
	EXPECT_EQ( -1.0f, f( out[ 17 + 8 ] ) );
	EXPECT_EQ( 3.0f, f( out[ 17 + 13 ] ) );
	EXPECT_EQ( 20.0f, f( out[ 17 + 14 ] ) );
	EXPECT_EQ( 8.0f, f( out[ 17 + 16 ] ) );
	const std::vector< std::string > expected = { "GetFxBlend 10", "IsTransparent 20" };
	EXPECT_EQ( expected, g_calls );
}

TEST( FwdLeafSystem, ReceivesAndBlendsAreEachRenderablesOwn )
{
	auto receives = Slot< void ( * )( void **, const int32_t *, int32_t, int32_t * ) >( kReceives );
	auto blends = Slot< int32_t ( * )( void *, void **, int32_t, int32_t *, int32_t ) >( kComputeFxBlends );
	ASSERT_NE( nullptr, receives );
	ASSERT_NE( nullptr, blends );
	FakeRenderable a = Make( 1 ), c = Make( 2 );
	a.receive = 1;
	c.receive = 2 | 4;
	void *pp[ 3 ] = { &a, &c, &a };
	const int32_t flags[ 3 ] = { 2, 2 | 8, 1 };
	int32_t answers[ 3 ] = { -1, -1, -1 };
	receives( pp, flags, 3, answers );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_EQ( 1, answers[ 1 ] );
	EXPECT_EQ( 1, answers[ 2 ] );
	g_calls.clear();
	EXPECT_EQ( 0, blends( nullptr, pp, 3, nullptr, 0 ) );
	const std::vector< std::string > expected = { "ComputeFxBlend 1", "ComputeFxBlend 2", "ComputeFxBlend 1" };
	EXPECT_EQ( expected, g_calls );
}

TEST( FwdLeafSystem, ABoxsQuestionsStopAtTheFirstThatRejects )
{
	auto queries = Slot< void ( * )( void *, const float *, const int32_t *, int32_t, int32_t * ) >( kBoxQueries );
	ASSERT_NE( nullptr, queries );
	// Boxes by low x: 10 passes everything, 150 fails the area, 250 is culled,
	// 55 is occluded.
	const float boxes[] = { 10, 0, 0, 11, 1, 1, 150, 0, 0, 151, 1, 1, 250, 0, 0, 251, 1, 1, 55, 0, 0, 56, 1, 1, 150, 0, 0, 151, 1, 1 };
	const int32_t programs[] = { 1 | 4 | ( 3 << 8 ), 1 | 4 | ( 2 << 8 ), 2 | 4, 2 | 4, 2 };
	int32_t answers[ 5 ] = { -1, -1, -1, -1, -1 };
	g_calls.clear();
	queries( g_pEngine, boxes, programs, 5, answers );
	EXPECT_EQ( 0, answers[ 0 ] );
	EXPECT_EQ( 1, answers[ 1 ] );
	EXPECT_EQ( 2, answers[ 2 ] );
	EXPECT_EQ( 4, answers[ 3 ] );
	EXPECT_EQ( 0, answers[ 4 ] );
	const std::vector< std::string > expected = {
		"Area 10 3", "Occluded 10",   // passes both
		"Area 150 2",                 // rejected: occlusion not asked
		"Cull 250",                   // culled: occlusion not asked
		"Cull 55", "Occluded 55",
		"Cull 150",                   // the main frustum only
	};
	EXPECT_EQ( expected, g_calls );
}

TEST( FwdLeafSystem, LeavesAreEnumeratedPerBoxAndALongAnswerAsksForRoom )
{
	auto enumerate = Slot< int32_t ( * )( void *, const float *, int32_t, int32_t *, int32_t ) >( kEnumerateLeaves );
	ASSERT_NE( nullptr, enumerate );
	const float boxes[] = { 0, 0, 0, 25, 1, 1, 40, 0, 0, 41, 1, 1 };
	int32_t out[ 16 ];
	EXPECT_EQ( 6, enumerate( g_pEngine, boxes, 2, out, 16 ) );
	const int32_t expected[] = { 3, 0, 1, 2, 1, 4 };
	for ( int i = 0; i < 6; i++ )
		EXPECT_EQ( expected[ i ], out[ i ] ) << i;
	EXPECT_EQ( -6, enumerate( g_pEngine, boxes, 2, out, 4 ) ) << "the words needed, negated";
}

TEST( FwdLeafSystem, EachRunsAreaIsTheEnginesAndDetailModelsAreTheSystems )
{
	auto areas = Slot< void ( * )( void *, const int32_t *, int32_t, int32_t * ) >( kLeavesAreas );
	auto details = Slot< void ( * )( void *, const int32_t *, int32_t, void ** ) >( kDetailModels );
	ASSERT_NE( nullptr, areas );
	ASSERT_NE( nullptr, details );
	const int32_t runs[] = { 2, 3, 4, 1, 10, 0 };
	int32_t answers[ 3 ] = { 0, 0, 0 };
	areas( g_pEngine, runs, 3, answers );
	EXPECT_EQ( 7, answers[ 0 ] );
	EXPECT_EQ( 10, answers[ 1 ] );
	EXPECT_EQ( -1, answers[ 2 ] );

	g_details.clear();
	for ( int i = 0; i < 6; i++ )
		g_details.push_back( Make( 1000 + i ) );
	const int32_t ranges[] = { 1, 2, 4, 3 };
	void *models[ 5 ];
	details( DetailSystem(), ranges, 4, models );
	EXPECT_EQ( (void *)&g_details[ 1 ], models[ 0 ] );
	EXPECT_EQ( (void *)&g_details[ 2 ], models[ 1 ] );
	EXPECT_EQ( (void *)&g_details[ 4 ], models[ 2 ] );
	EXPECT_EQ( (void *)&g_details[ 5 ], models[ 3 ] );
	EXPECT_EQ( nullptr, models[ 4 ] ) << "past the system's models: a sprite's empty slot";
}

//-----------------------------------------------------------------------------
// L3's batching: the per-renderable calls are queued and handed over whole.
//-----------------------------------------------------------------------------
TEST( FwdLeafSystem, RenderableChangedAndGroupChangesRideInPreRendersArguments )
{
	Built b;
	b.pSystem->RenderableChanged( 4 );
	b.pSystem->ChangeRenderableRenderGroup( 9, RENDER_GROUP_TRANSLUCENT_ENTITY );
	b.pSystem->RenderableChanged( 2 );
	EXPECT_TRUE( g_calls.empty() ) << "a queued call crossed on its own";
	EXPECT_TRUE( g_pending.empty() );
	b.pSystem->PreRender();
	const std::vector< std::string > first = { "PreRender 1:4:0 2:9:8 1:2:0" };
	EXPECT_EQ( first, g_pending ) << "in order, in PreRender's own call";
	EXPECT_TRUE( g_calls.empty() ) << "no Pending crossing beside PreRender";
	// Nothing queued: the next PreRender carries nothing.
	b.pSystem->PreRender();
	ASSERT_EQ( 2u, g_pending.size() );
	EXPECT_EQ( "PreRender", g_pending[ 1 ] );
}

TEST( FwdLeafSystem, AnyOtherCallHandsTheQueueOverFirst )
{
	Built b;
	FakeRenderable r = Make( 6 );
	r.handle = 4;
	b.pSystem->RenderableChanged( 4 );
	b.pSystem->RemoveRenderable( 4 );
	const std::vector< std::string > expected = { "M.Pending 1", "M.RemoveRenderable 4" };
	EXPECT_EQ( expected, g_calls ) << "the dirty mark must reach managed before the removal";
}

TEST( FwdLeafSystem, AFullQueueIsHandedOverAsItFills )
{
	Built b;
	for ( int i = 0; i < 300; i++ )
		b.pSystem->RenderableChanged( (ClientRenderHandle_t)i );
	ASSERT_EQ( 1u, g_pending.size() ) << "256 fit, the 257th handed them over";
	EXPECT_EQ( 0u, g_pending[ 0 ].rfind( "Pending 1:0:0 1:1:0", 0 ) ) << g_pending[ 0 ].substr( 0, 60 );
	b.pSystem->PreRender();
	ASSERT_EQ( 2u, g_pending.size() );
	EXPECT_EQ( 0u, g_pending[ 1 ].rfind( "PreRender 1:256:0 1:257:0", 0 ) ) << g_pending[ 1 ].substr( 0, 60 );
}

TEST( FwdLeafSystem, ABlendBatchAnswersTheGroupChangesItsBlendsQueued )
{
	Built b;
	auto blends = Slot< int32_t ( * )( void *, void **, int32_t, int32_t *, int32_t ) >( kComputeFxBlends );
	ASSERT_NE( nullptr, blends );
	// A blend that queues a group change, as a fading static prop's does: the
	// recorder below queues on the leaf system under test.
	static IClientLeafSystem *s_pSystem;
	s_pSystem = b.pSystem;
	struct Fading
	{
		static void Blend( void * ) { s_pSystem->ChangeRenderableRenderGroup( 3, RENDER_GROUP_OPAQUE_STATIC ); }
	};
	FakeTable fading;
	fading.Put( &IClientRenderable::ComputeFxBlend, (void *)&Fading::Blend );
	FakeObject prop = { fading.slots };
	void *pp[ 1 ] = { &prop };
	int32_t out[ 3 * 4 ] = { -1, -1, -1 };
	ASSERT_EQ( 1, blends( b.pSystem, pp, 1, out, 4 ) );
	EXPECT_EQ( 2, out[ 0 ] );
	EXPECT_EQ( 3, out[ 1 ] );
	EXPECT_EQ( RENDER_GROUP_OPAQUE_STATIC, out[ 2 ] );
	EXPECT_TRUE( g_pending.empty() ) << "the answer, not a Pending crossing";
	b.pSystem->PreRender();
	ASSERT_EQ( 1u, g_pending.size() );
	EXPECT_EQ( "PreRender", g_pending[ 0 ] ) << "the answered change was taken off the queue";
}

TEST( FwdLeafSystem, ABlendBatchWithoutTheLeafSystemLeavesTheQueue )
{
	Built b;
	auto blends = Slot< int32_t ( * )( void *, void **, int32_t, int32_t *, int32_t ) >( kComputeFxBlends );
	ASSERT_NE( nullptr, blends );
	b.pSystem->ChangeRenderableRenderGroup( 3, RENDER_GROUP_OPAQUE_STATIC );
	FakeRenderable r = Make( 1 );
	void *pp[ 1 ] = { &r };
	int32_t out[ 3 ] = { -1, -1, -1 };
	EXPECT_EQ( 0, blends( nullptr, pp, 1, out, 1 ) );
	EXPECT_EQ( -1, out[ 0 ] );
	b.pSystem->PreRender();
	const std::vector< std::string > expected = { "PreRender 2:3:6" };
	EXPECT_EQ( expected, g_pending ) << "nothing dropped: PreRender took it";
}

TEST( FwdLeafSystem, AQueueLongerThanTheBlendAnswerIsHandedOverWhole )
{
	Built b;
	auto blends = Slot< int32_t ( * )( void *, void **, int32_t, int32_t *, int32_t ) >( kComputeFxBlends );
	ASSERT_NE( nullptr, blends );
	b.pSystem->RenderableChanged( 5 );
	b.pSystem->ChangeRenderableRenderGroup( 3, RENDER_GROUP_OPAQUE_STATIC );
	FakeRenderable r = Make( 1 );
	void *pp[ 1 ] = { &r };
	int32_t out[ 3 ] = { -1, -1, -1 };
	EXPECT_EQ( 0, blends( b.pSystem, pp, 1, out, 1 ) );
	EXPECT_EQ( -1, out[ 0 ] ) << "half a queue was answered";
	const std::vector< std::string > expected = { "Pending 1:5:0 2:3:6" };
	EXPECT_EQ( expected, g_pending );
}

TEST( FwdLeafSystem, ThePerViewCallsCarryWhatWasQueuedSincePreRender )
{
	Built b;
	b.pSystem->PreRender();
	g_pending.clear();
	// Moved after PreRender, before the view: a bone setup's RenderableChanged,
	// a fading prop's group change.
	b.pSystem->RenderableChanged( 7 );
	b.pSystem->ChangeRenderableRenderGroup( 3, RENDER_GROUP_OPAQUE_STATIC );
	LeafIndex_t leaves[ 2 ] = { 4, 5 };
	b.pSystem->ComputeTranslucentRenderLeaf( 2, leaves, nullptr, 10, 0 );
	b.pSystem->RenderableChanged( 8 );
	WorldListInfo_t world;
	world.m_LeafCount = 2;
	world.m_pLeafList = leaves;
	CClientRenderablesList *pList = static_cast< CClientRenderablesList * >( malloc( sizeof( CClientRenderablesList ) ) );
	for ( int g = 0; g < RENDER_GROUP_COUNT; g++ )
		pList->m_RenderGroupCounts[ g ] = 0;
	SetupRenderInfo_t info;
	info.m_pWorldListInfo = &world;
	info.m_pRenderList = pList;
	info.m_vecRenderOrigin.Init( 0, 0, 0 );
	info.m_vecRenderForward.Init( 1, 0, 0 );
	b.pSystem->BuildRenderablesList( info );
	b.pSystem->RenderableChanged( 9 );
	typedef CUtlVector< IClientRenderable * > Vector_t;   // storage only: no heap here
	alignas( Vector_t ) unsigned char opaqueBytes[ sizeof( Vector_t ) ], translucentBytes[ sizeof( Vector_t ) ];
	b.pSystem->CollateViewModelRenderables( *reinterpret_cast< Vector_t * >( opaqueBytes ), *reinterpret_cast< Vector_t * >( translucentBytes ) );
	free( pList );
	const std::vector< std::string > expected = { "Translucent 1:7:0 2:3:6", "BuildList 1:8:0", "Collate 1:9:0" };
	EXPECT_EQ( expected, g_pending ) << "each in the next per-view call's own arguments";
	for ( const std::string &call : g_calls )
		EXPECT_NE( 0u, call.rfind( "M.Pending", 0 ) ) << "no Pending crossing: " << call;
}

TEST( FwdLeafSystem, SetRenderGroupIsQueuedInOrderWithTheOthers )
{
	Built b;
	b.pSystem->ChangeRenderableRenderGroup( 3, RENDER_GROUP_OPAQUE_STATIC );
	b.pSystem->SetRenderGroup( 3, RENDER_GROUP_TRANSLUCENT_ENTITY );
	b.pSystem->RenderableChanged( 3 );
	EXPECT_TRUE( g_calls.empty() ) << "SetRenderGroup crossed on its own";
	b.pSystem->PreRender();
	const std::vector< std::string > expected = { "PreRender 2:3:6 3:3:8 1:3:0" };
	EXPECT_EQ( expected, g_pending );
}
