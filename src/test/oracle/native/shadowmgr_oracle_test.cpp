//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE SHADOW MANAGER ORACLE: stock's client shadow manager --
// CClientShadowMgr's shadows and their kinds (CreateShadow,
// CreateProjectedTexture, GetActualShadowCastType, the retirement of a shadow
// whose renderable is gone, the level's shutdown), its modes (Init's low-end
// and depth-texture checks, PreRender's render-to-texture and depth-texture
// switches, the shadow colour, direction, distance and blobby cut-off, the
// restore, the five console commands), its scheduling (the dirty set walked
// in handle order, the transparent shadows kept dirty, the moved test, the
// shadow-parent hierarchy), its projection math (the blob and
// render-to-texture bases, the clip planes, the hierarchical bounds, the
// per-renderable distance and direction), the flashlight (its matrices, the
// light-world and models switches, the version2 arm), the receivers
// (CullReceiver's frustum, sphere and separating plane, each kind's engine
// call), the render-to-texture pass (the visible list's filters and order,
// the r_shadows gates, the r_shadowmaxrendered budget and its LOD fallback,
// the redraw rule, CTextureAllocator's LRU atlas), the flashlight depth
// textures (their allocation, the active list, the lock), the debug overlays
// and the two material proxies -- run offline from the PRISTINE
// game/client/clientshadowmgr.cpp over a scripted world, and written down as
// the golden src/test/client/Fixtures/shadowmgr-stock.tsv
// (README-shadowmgr-stock.md says how to read it).
//
// WHY OFFLINE. A live map with r_shadowrendertotexture 0 reaches the blob
// path and nothing else; the atlas's overflow, the LOD fallback, the
// separating plane's four sides, a flashlight with light-world off or a
// depth texture that cannot be locked are never met. Here each is placed on
// purpose.
//
// THE SUBJECT IS INCLUDED, NOT LINKED: its class is file-local, so the harness
// includes the file into this translation unit. shadowmgr_stub/
// shadowmgr_harness.h says what is stock and what is stand-in.
//
// WHAT MANAGED REPLAYS. The managed shadow manager (SourceSharp.Client's
// Render/Shadows, S1 of docs/plans/c7_shadow_manager.md) owns the shadows and
// every decision. The golden carries its own stimulus -- every thing is
// described (T) before it is used, every setting (CV, ENV) before it is read,
// every call the scenario makes (C) before it is made and its answer (R)
// after -- so ShadowManagerOracleTests rebuilds the same world from the golden
// alone and requires stock's effects (E): every call into the engine, the
// leaf system, the renderables, the materials and the render context, in
// order, with floats as bits. The questions the subject asked (Q) are written
// with their answers; the replay answers from the same world and does not
// compare their order.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; ShadowManagerOracleTests asserts the managed port does.
// Regenerating the golden to make this test green deletes the check: the
// golden changes only when the scenario or a row's format does, and then on
// purpose (touch shadowmgr_golden.regen beside this file, run, delete the
// marker, and say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <algorithm>
#include <fstream>
#include <functional>
#include <map>
#include <memory>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"
#include "valve_sdk.h"

#include "shadowmgr_harness.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;
static size_t g_nLogLines = 0;
static int g_nQuiet = 0;   // > 0: nothing is written (the oracle's probes)
static int g_nQuietRows = 0;

void ShadowLog( const char *pFmt, ... )
{
	if ( g_nQuiet > 0 )
	{
		++g_nQuietRows;
		return;
	}
	va_list ap, copy;
	va_start( ap, pFmt );
	va_copy( copy, ap );
	const int n = vsnprintf( NULL, 0, pFmt, copy );
	va_end( copy );
	std::string line( n > 0 ? n : 0, '\0' );
	if ( n > 0 )
		vsnprintf( &line[ 0 ], n + 1, pFmt, ap );
	va_end( ap );
	g_Log += line;
	g_Log += '\n';
	++g_nLogLines;
}

unsigned int ShadowBits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

namespace
{

// The spellings every row uses.
std::string Hex( float f )
{
	char sz[ 16 ];
	snprintf( sz, sizeof( sz ), "%08x", ShadowBits( f ) );
	return sz;
}

std::string Vec( const Vector &v )
{
	return Hex( v.x ) + "," + Hex( v.y ) + "," + Hex( v.z );
}

std::string Ang( const QAngle &a )
{
	return Hex( a.x ) + "," + Hex( a.y ) + "," + Hex( a.z );
}

std::string Mat( const VMatrix &m )
{
	std::string s;
	for ( int i = 0; i < 4; i++ )
		for ( int j = 0; j < 4; j++ )
			s += ( i || j ? "," : "" ) + Hex( m.m[ i ][ j ] );
	return s;
}

std::string Mat34( const matrix3x4_t &m )
{
	std::string s;
	for ( int i = 0; i < 3; i++ )
		for ( int j = 0; j < 4; j++ )
			s += ( i || j ? "," : "" ) + Hex( m[ i ][ j ] );
	return s;
}

std::string Leaves( int n, const int *p )
{
	std::string s = std::to_string( n ) + ":";
	for ( int i = 0; i < n; i++ )
		s += ( i ? "," : "" ) + std::to_string( p[ i ] );
	return s;
}

} // namespace

//-----------------------------------------------------------------------------
// The console. ConVar::SetValue tells g_pCVar's global change callbacks: a
// change the SUBJECT makes is an E row (the scenario's own are CV rows, said
// before they are made, so this is quiet for them).
//-----------------------------------------------------------------------------
#include "icvar.h"

static bool s_bScenarioSetsConVar = false;

class CShadowCvar final : public ICvar
{
public:
	virtual bool Connect( CreateInterfaceFn ) { return true; }
	virtual void Disconnect() {}
	virtual void *QueryInterface( const char * ) { return NULL; }
	virtual InitReturnVal_t Init() { return INIT_OK; }
	virtual void Shutdown() {}
	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 1; }
	virtual void RegisterConCommand( ConCommandBase * ) {}
	virtual void UnregisterConCommand( ConCommandBase * ) {}
	virtual void UnregisterConCommands( CVarDLLIdentifier_t ) {}
	virtual const char *GetCommandLineValue( const char * ) { return NULL; }
	virtual ConCommandBase *FindCommandBase( const char * ) { return NULL; }
	virtual const ConCommandBase *FindCommandBase( const char * ) const { return NULL; }
	virtual ConVar *FindVar( const char * ) { return NULL; }
	virtual const ConVar *FindVar( const char * ) const { return NULL; }
	virtual ConCommand *FindCommand( const char * ) { return NULL; }
	virtual const ConCommand *FindCommand( const char * ) const { return NULL; }
	virtual ConCommandBase *GetCommands( void ) { return NULL; }
	virtual const ConCommandBase *GetCommands( void ) const { return NULL; }
	virtual void InstallGlobalChangeCallback( FnChangeCallback_t ) {}
	virtual void RemoveGlobalChangeCallback( FnChangeCallback_t ) {}
	virtual void CallGlobalChangeCallbacks( ConVar *var, const char *, float )
	{
		if ( !s_bScenarioSetsConVar )
			ShadowLog( "E\tcvar.Set\t%s\t%d", var->GetName(), var->GetInt() );
	}
	virtual void InstallConsoleDisplayFunc( IConsoleDisplayFunc * ) {}
	virtual void RemoveConsoleDisplayFunc( IConsoleDisplayFunc * ) {}
	virtual void ConsoleColorPrintf( const Color &, const char *, ... ) const {}
	virtual void ConsolePrintf( const char *, ... ) const {}
	virtual void ConsoleDPrintf( const char *, ... ) const {}
	virtual void RevertFlaggedConVars( int ) {}
	virtual void InstallCVarQuery( ICvarQuery * ) {}
	virtual void PublishToVXConsole() {}
	virtual bool IsMaterialThreadSetAllowed() const { return true; }
	virtual void QueueMaterialThreadSetValue( ConVar *, const char * ) {}
	virtual void QueueMaterialThreadSetValue( ConVar *, int ) {}
	virtual void QueueMaterialThreadSetValue( ConVar *, float ) {}
	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }
	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }
	virtual ICVarIteratorInternal *FactoryInternalIterator( void ) { return NULL; }
};
static CShadowCvar g_ShadowCvar;

// The client's two ConVars the subject names extern (viewrender's).
ConVar cl_drawshadowtexture( "cl_drawshadowtexture", "0" );
ConVar cl_shadowtextureoverlaysize( "cl_shadowtextureoverlaysize", "256" );

//-----------------------------------------------------------------------------
// The spew: Msg, Warning and DevMsg are M rows (the console's text, which
// managed prints too); an assert is an A row (stock's own consistency checks,
// which managed does not carry and the replay does not compare).
//-----------------------------------------------------------------------------
static int s_nLibraryAsserts = 0;

static SpewRetval_t ShadowSpew( SpewType_t type, const tchar *pMsg )
{
	std::string s( pMsg );
	while ( !s.empty() && ( s.back() == '\n' || s.back() == '\r' ) )
		s.pop_back();
	for ( char &c : s )
	{
		if ( c == '\n' || c == '\t' )
			c = ' ';
	}
	if ( type == SPEW_ASSERT )
	{
		// The subject's own checks are written; the library's (vector2d.h's
		// IsValid on a shadow's uninitialised members as CUtlLinkedList
		// copies them) are counted, and the count closes the log.
		if ( s.find( "clientshadowmgr.cpp" ) != std::string::npos || s.find( "utlrbtree.h" ) != std::string::npos )
			ShadowLog( "A\t%s", s.c_str() );
		else
			++s_nLibraryAsserts;
	}
	else
		ShadowLog( "M\t%d\t%s", (int)type, s.c_str() );
	return SPEW_CONTINUE;
}

//-----------------------------------------------------------------------------
// The scripted world.
//-----------------------------------------------------------------------------
int g_nBaseEntityTouches = 0;
static int s_nBaseEntityAsks = 0;   // GetBaseEntity / GetBaseEntityFromHandle asked (and NULL answered)

static ShadowGlobals_t s_Globals;
ShadowGlobals_t *gpGlobals = &s_Globals;

namespace
{

struct ShadowBox_t
{
	Vector mins, maxs;
};

bool Overlaps( const Vector &mins, const Vector &maxs, const ShadowBox_t &b )
{
	return mins.x < b.maxs.x && maxs.x > b.mins.x && mins.y < b.maxs.y && maxs.y > b.mins.y && mins.z < b.maxs.z &&
	       maxs.z > b.mins.z;
}

// The level: leaves of 100 units along x, every leaf reaching over all y and z.
int s_nLeafCount = 0;
float s_flLeafWidth = 100.0f;

// What the engine answers.
ShadowBox_t s_ViewBox;               // CullBox culls a box that does not overlap it
Vector s_vecAmbient;                 // GetAmbientLightColor
float s_flPixelsPerUnit = 1.0f;      // ComputePixelDiameterOfSphere = radius * this
int s_nDXLevel = 90;
bool s_bSupportsDepth = true;
int s_nMaxRenderTarget = 2048;       // a render target's actual width is clamped to this
MaterialFogMode_t s_FogMode = MATERIAL_FOG_LINEAR;
MaterialHeightClipMode_t s_HeightClip = MATERIAL_HEIGHTCLIPMODE_RENDER_ABOVE_HEIGHT;
MaterialBufferRestoreFunc_t s_pfnRestore = NULL;

std::vector< std::unique_ptr< CShadowThing > > s_Things;

CShadowThing *Find( int nId )
{
	for ( auto &p : s_Things )
	{
		if ( p->m_nId == nId )
			return p.get();
	}
	return NULL;
}

int IdOf( IClientRenderable *p )
{
	if ( !p )
		return -1;
	for ( auto &t : s_Things )
	{
		if ( static_cast< IClientRenderable * >( t.get() ) == p )
			return t->m_nId;
	}
	return -2;   // not a thing: the oracle's facts fail on it
}

std::string R( IClientRenderable *p )
{
	const int n = IdOf( p );
	return n < 0 ? std::string( n == -1 ? "-" : "?" ) : "r" + std::to_string( n );
}

std::string R( const CShadowThing *p )
{
	return p ? "r" + std::to_string( p->m_nId ) : std::string( "-" );
}

// A model pointer names its thing and kind: 0x1000 + id * 16 + model type.
const model_t *ModelOf( const CShadowThing *p )
{
	return p->m_bHasModel ? (const model_t *)(intp)( 0x1000 + p->m_nId * 16 + p->m_nModelType ) : NULL;
}

std::string M( const model_t *p )
{
	if ( !p )
		return "-";
	const intp v = (intp)p - 0x1000;
	return "m" + std::to_string( (int)( v / 16 ) );
}

} // namespace

ClientEntityHandle_t ShadowEntityOf( int nId )
{
	return CBaseHandle( nId, 1 );
}

//-----------------------------------------------------------------------------
// The renderables.
//-----------------------------------------------------------------------------
CShadowThing::CShadowThing( int nId ) : m_nId( nId )
{
	m_vecOrigin.Init(); m_vecMins.Init(); m_vecMaxs.Init(); m_vecRttMins.Init(); m_vecRttMaxs.Init();
	m_vecAbsMins.Init(); m_vecAbsMaxs.Init(); m_vecCastDirection.Init();
	m_angAngles.Init();
	SetIdentityMatrix( m_Transform );
	m_RefEHandle = ShadowEntityOf( nId );
}

C_BaseEntity *CShadowThing::GetBaseEntity()
{
	++s_nBaseEntityAsks;
	ShadowLog( "Q\tr.baseentity\t%s\t-", R( this ).c_str() );
	return NULL;
}

static int s_nDirtyOnOriginDepth = 0;

const Vector &CShadowThing::GetRenderOrigin( void )
{
	ShadowLog( "Q\tr.origin\t%s\t%s", R( this ).c_str(), Vec( m_vecOrigin ).c_str() );
	if ( m_bDirtyOnOrigin && s_nDirtyOnOriginDepth == 0 )
	{
		// As an aiment's render origin can: asked for it, it recomputes, and
		// marks its shadow dirty.
		++s_nDirtyOnOriginDepth;
		ShadowLog( "E\tr.redirty\t%s", R( this ).c_str() );
		g_pClientShadowMgr->AddToDirtyShadowList( this, false );
		--s_nDirtyOnOriginDepth;
	}
	return m_vecOrigin;
}

const QAngle &CShadowThing::GetRenderAngles( void )
{
	ShadowLog( "Q\tr.angles\t%s\t%s", R( this ).c_str(), Ang( m_angAngles ).c_str() );
	return m_angAngles;
}

const matrix3x4_t &CShadowThing::RenderableToWorldTransform()
{
	ShadowLog( "Q\tr.transform\t%s\t%s", R( this ).c_str(), Mat34( m_Transform ).c_str() );
	return m_Transform;
}

ClientShadowHandle_t CShadowThing::GetShadowHandle() const
{
	ShadowLog( "Q\tr.shadowhandle\t%s\t%d", R( this ).c_str(), m_hShadow );
	return m_hShadow;
}

const model_t *CShadowThing::GetModel() const
{
	ShadowLog( "Q\tr.model\t%s\t%s", R( this ).c_str(), M( ModelOf( this ) ).c_str() );
	return ModelOf( this );
}

int CShadowThing::GetBody()
{
	ShadowLog( "Q\tr.body\t%s\t%d", R( this ).c_str(), m_nBody );
	return m_nBody;
}

int CShadowThing::GetSkin()
{
	ShadowLog( "Q\tr.skin\t%s\t%d", R( this ).c_str(), m_nSkin );
	return m_nSkin;
}

void CShadowThing::GetRenderBoundsWorldspace( Vector &mins, Vector &maxs )
{
	mins = m_vecAbsMins;
	maxs = m_vecAbsMaxs;
	ShadowLog( "Q\tr.worldbounds\t%s\t%s\t%s", R( this ).c_str(), Vec( mins ).c_str(), Vec( maxs ).c_str() );
}

void CShadowThing::GetShadowRenderBounds( Vector &mins, Vector &maxs, ShadowType_t shadowType )
{
	// Render-to-texture shadows are bounded by the RTT box (a studio model's
	// hitboxes in stock), every other kind by the render box.
	const bool bRtt = shadowType == SHADOWS_RENDER_TO_TEXTURE || shadowType == SHADOWS_RENDER_TO_TEXTURE_DYNAMIC;
	mins = bRtt ? m_vecRttMins : m_vecMins;
	maxs = bRtt ? m_vecRttMaxs : m_vecMaxs;
	ShadowLog( "Q\tr.shadowbounds\t%s\t%d\t%s\t%s", R( this ).c_str(), (int)shadowType, Vec( mins ).c_str(), Vec( maxs ).c_str() );
}

bool CShadowThing::ShouldReceiveProjectedTextures( int flags )
{
	const bool b = ( m_nReceiveFlags & flags ) != 0;
	ShadowLog( "Q\tr.receives\t%s\t%d\t%d", R( this ).c_str(), flags, b ? 1 : 0 );
	return b;
}

bool CShadowThing::GetShadowCastDistance( float *pDist, ShadowType_t shadowType ) const
{
	const float flIn = *pDist;
	if ( m_bCastDistance )
		*pDist = m_flCastDistance;
	ShadowLog( "Q\tr.castdist\t%s\t%d\t%s\t%s", R( this ).c_str(), (int)shadowType, Hex( flIn ).c_str(), Hex( *pDist ).c_str() );
	return m_bCastDistance;
}

bool CShadowThing::GetShadowCastDirection( Vector *pDirection, ShadowType_t shadowType ) const
{
	const Vector vIn = *pDirection;
	if ( m_bCastDirection )
		*pDirection = m_vecCastDirection;
	ShadowLog( "Q\tr.castdir\t%s\t%d\t%s\t%s", R( this ).c_str(), (int)shadowType, Vec( vIn ).c_str(), Vec( *pDirection ).c_str() );
	return m_bCastDirection;
}

bool CShadowThing::IsShadowDirty()
{
	ShadowLog( "Q\tr.dirty\t%s\t%d", R( this ).c_str(), m_bShadowDirty ? 1 : 0 );
	return m_bShadowDirty;
}

void CShadowThing::MarkShadowDirty( bool bDirty )
{
	ShadowLog( "E\tr.MarkShadowDirty\t%s\t%d", R( this ).c_str(), bDirty ? 1 : 0 );
	m_bShadowDirty = bDirty;
}

IClientRenderable *CShadowThing::GetShadowParent()
{
	CShadowThing *p = m_nParent >= 0 ? Find( m_nParent ) : NULL;
	ShadowLog( "Q\tr.parent\t%s\t%s", R( this ).c_str(), R( p ).c_str() );
	return p;
}

IClientRenderable *CShadowThing::FirstShadowChild()
{
	CShadowThing *p = m_nFirstChild >= 0 ? Find( m_nFirstChild ) : NULL;
	ShadowLog( "Q\tr.firstchild\t%s\t%s", R( this ).c_str(), R( p ).c_str() );
	return p;
}

IClientRenderable *CShadowThing::NextShadowPeer()
{
	CShadowThing *p = m_nNextPeer >= 0 ? Find( m_nNextPeer ) : NULL;
	ShadowLog( "Q\tr.nextpeer\t%s\t%s", R( this ).c_str(), R( p ).c_str() );
	return p;
}

void CShadowThing::CreateModelInstance()
{
	ShadowLog( "E\tr.CreateModelInstance\t%s", R( this ).c_str() );
	if ( m_hInstance == MODEL_INSTANCE_INVALID )
		m_hInstance = (ModelInstanceHandle_t)( 100 + m_nId );
}

ModelInstanceHandle_t CShadowThing::GetModelInstance()
{
	ShadowLog( "Q\tr.instance\t%s\t%d", R( this ).c_str(), m_hInstance );
	return m_hInstance;
}

// CDefaultClientRenderable's default for GetRenderBoundsWorldspace (never
// reached: every renderable here answers its own).
void DefaultRenderBoundsWorldspace( IClientRenderable *pRenderable, Vector &absMins, Vector &absMaxs )
{
	absMins.Init();
	absMaxs.Init();
}

//-----------------------------------------------------------------------------
// The entity list.
//-----------------------------------------------------------------------------
static CShadowEntityList s_EntityList;
CShadowEntityList &ClientEntityList() { return s_EntityList; }

IClientRenderable *CShadowEntityList::GetClientRenderableFromHandle( ClientEntityHandle_t h )
{
	CShadowThing *p = NULL;
	if ( h.IsValid() )
	{
		p = Find( h.GetEntryIndex() );
		if ( p && p->m_bGone )
			p = NULL;
	}
	ShadowLog( "Q\tel.renderable\t%d\t%s", h.IsValid() ? h.GetEntryIndex() : -1, R( p ).c_str() );
	return p;
}

C_BaseEntity *CShadowEntityList::GetBaseEntityFromHandle( ClientEntityHandle_t h )
{
	++s_nBaseEntityAsks;
	ShadowLog( "Q\tel.baseentity\t%d\t-", h.IsValid() ? h.GetEntryIndex() : -1 );
	return NULL;
}

//-----------------------------------------------------------------------------
// The client leaf system: it keeps each shadow's flags and leaves, and the
// receivers the scenario registered in each leaf. Projecting a shadow or a
// flashlight offers it to each leaf's receivers in turn, as stock's leaf
// system does (each receiver once per projection); enumerating offers each
// shadow projected into the leaves once, in the order the leaves are given,
// and each leaf's shadows oldest first.
//-----------------------------------------------------------------------------
namespace
{

struct LeafShadow_t
{
	ClientShadowHandle_t m_UserId;
	int m_nFlags;
	std::vector< int > m_Leaves;
	bool m_bLive;
};

struct Receiver_t
{
	int m_nLeaf;
	int m_nThing;
	ShadowReceiver_t m_Kind;
};

std::vector< LeafShadow_t > s_LeafShadows;
std::vector< Receiver_t > s_Receivers;

} // namespace

// The oracle's own reading of a receiver offer, made with the subject's own
// functions before the subject decides (defined after the subject).
static void ProbeReceiver( ClientShadowHandle_t handle, IClientRenderable *pRenderable, ShadowReceiver_t type );

namespace
{

void OfferToReceivers( ClientLeafShadowHandle_t h, int nLeafCount, const int *pLeafList )
{
	std::set< int > offered;
	for ( int i = 0; i < nLeafCount; i++ )
	{
		for ( const Receiver_t &r : s_Receivers )
		{
			if ( r.m_nLeaf != pLeafList[ i ] || offered.count( r.m_nThing ) )
				continue;
			offered.insert( r.m_nThing );
			CShadowThing *p = Find( r.m_nThing );
			if ( !p )
				continue;
			ShadowLog( "E\tleaf.receiver\t%d\t%s\t%d", s_LeafShadows[ h ].m_UserId, R( p ).c_str(), (int)r.m_Kind );
			ProbeReceiver( s_LeafShadows[ h ].m_UserId, p, r.m_Kind );
			g_pClientShadowMgr->AddShadowToReceiver( s_LeafShadows[ h ].m_UserId, p, r.m_Kind );
		}
	}
}

} // namespace

static CShadowLeafSystem s_LeafSystem;
CShadowLeafSystem *ClientLeafSystem() { return &s_LeafSystem; }

ClientLeafShadowHandle_t CShadowLeafSystem::AddShadow( ClientShadowHandle_t userId, unsigned short flags )
{
	s_LeafShadows.push_back( { userId, flags, {}, true } );
	const ClientLeafShadowHandle_t h = (ClientLeafShadowHandle_t)( s_LeafShadows.size() - 1 );
	ShadowLog( "E\tleaf.AddShadow\t%d\t%d\t=>%d", userId, flags, h );
	return h;
}

void CShadowLeafSystem::RemoveShadow( ClientLeafShadowHandle_t h )
{
	ShadowLog( "E\tleaf.RemoveShadow\t%d", h );
	s_LeafShadows[ h ].m_bLive = false;
	s_LeafShadows[ h ].m_Leaves.clear();
}

void CShadowLeafSystem::ProjectShadow( ClientLeafShadowHandle_t h, int nLeafCount, const int *pLeafList )
{
	ShadowLog( "E\tleaf.ProjectShadow\t%d\t%s", h, Leaves( nLeafCount, pLeafList ).c_str() );
	s_LeafShadows[ h ].m_Leaves.assign( pLeafList, pLeafList + nLeafCount );
	OfferToReceivers( h, nLeafCount, pLeafList );
}

void CShadowLeafSystem::ProjectFlashlight( ClientLeafShadowHandle_t h, int nLeafCount, const int *pLeafList )
{
	ShadowLog( "E\tleaf.ProjectFlashlight\t%d\t%s", h, Leaves( nLeafCount, pLeafList ).c_str() );
	s_LeafShadows[ h ].m_Leaves.assign( pLeafList, pLeafList + nLeafCount );
	OfferToReceivers( h, nLeafCount, pLeafList );
}

void CShadowLeafSystem::EnumerateShadowsInLeaves( int leafCount, LeafIndex_t *pLeaves, IClientLeafShadowEnum *pEnum )
{
	std::vector< int > leaves( pLeaves, pLeaves + leafCount );
	ShadowLog( "E\tleaf.EnumerateShadowsInLeaves\t%s", Leaves( leafCount, leaves.data() ).c_str() );
	std::set< size_t > offered;
	for ( int i = 0; i < leafCount; i++ )
	{
		for ( size_t h = 0; h < s_LeafShadows.size(); h++ )
		{
			const LeafShadow_t &s = s_LeafShadows[ h ];
			if ( !s.m_bLive || offered.count( h ) )
				continue;
			if ( std::find( s.m_Leaves.begin(), s.m_Leaves.end(), (int)pLeaves[ i ] ) == s.m_Leaves.end() )
				continue;
			offered.insert( h );
			ShadowLog( "E\tleaf.enum\t%d", s.m_UserId );
			pEnum->EnumShadow( s.m_UserId );
		}
	}
}

//-----------------------------------------------------------------------------
// Render targets and materials.
//-----------------------------------------------------------------------------
namespace
{
std::vector< std::unique_ptr< ShadowTexture_t > > s_Textures;
std::vector< std::unique_ptr< ShadowMaterial_t > > s_Materials;
}

const char *ShadowTextureName( const ITexture *pTexture )
{
	return pTexture ? ( (const ShadowTexture_t *)pTexture )->m_szName : "-";
}

const char *ShadowMaterialName( const IMaterial *pMaterial )
{
	return pMaterial ? ( (const ShadowMaterial_t *)pMaterial )->m_szName : "-";
}

void ShadowMaterial_t::ColorModulate( float r, float g, float b )
{
	ShadowLog( "E\tmat.ColorModulate\t%s\t%s\t%s\t%s", m_szName, Hex( r ).c_str(), Hex( g ).c_str(), Hex( b ).c_str() );
}

void CShadowTextureReference::InitRenderTarget( int w, int h, RenderTargetSizeMode_t sizeMode, ImageFormat fmt,
                                                MaterialRenderTargetDepth_t depth, bool bHDR, const char *pStrOptionalName )
{
	s_Textures.emplace_back( new ShadowTexture_t );
	m_pTexture = s_Textures.back().get();
	V_strncpy( m_pTexture->m_szName, pStrOptionalName ? pStrOptionalName : "-", sizeof( m_pTexture->m_szName ) );
	m_pTexture->m_nWidth = std::min( w, s_nMaxRenderTarget );
	m_pTexture->m_nHeight = std::min( h, s_nMaxRenderTarget );
	ShadowLog( "E\ttex.InitRenderTarget\t%s\t%d\t%d\t%d\t%d\t%d\t%d\t=>%d", m_pTexture->m_szName, w, h, (int)sizeMode, (int)fmt,
	           (int)depth, bHDR ? 1 : 0, m_pTexture->m_nWidth );
}

void CShadowTextureReference::Shutdown( bool bDeleteIfUnReferenced )
{
	ShadowLog( "E\ttex.Shutdown\t%s", ShadowTextureName( (ITexture *)m_pTexture ) );
	m_pTexture = NULL;
}

void CShadowMaterialReference::Init( const char *pMaterialName, const char *pTextureGroupName, bool bComplain )
{
	s_Materials.emplace_back( new ShadowMaterial_t );
	m_pMaterial = s_Materials.back().get();
	V_strncpy( m_pMaterial->m_szName, pMaterialName, sizeof( m_pMaterial->m_szName ) );
	ShadowLog( "E\tmat.Init\t%s\t%s", pMaterialName, pTextureGroupName );
}

void CShadowMaterialReference::Shutdown()
{
	ShadowLog( "E\tmat.Shutdown\t%s", ShadowMaterialName( (IMaterial *)m_pMaterial ) );
	m_pMaterial = NULL;
}

//-----------------------------------------------------------------------------
// The material system, its render context and the dynamic mesh.
//-----------------------------------------------------------------------------
static CShadowMaterials s_Materials0;
CShadowMaterials *materials = &s_Materials0;
static CShadowHardwareConfig s_HardwareConfig;
CShadowHardwareConfig *g_pMaterialSystemHardwareConfig = &s_HardwareConfig;
static CShadowRenderContext s_RenderContext;
static CShadowMesh s_Mesh;

bool CShadowMaterials::SupportsShadowDepthTextures()
{
	ShadowLog( "Q\tmat.SupportsShadowDepthTextures\t%d", s_bSupportsDepth ? 1 : 0 );
	return s_bSupportsDepth;
}

void CShadowMaterials::AddRestoreFunc( MaterialBufferRestoreFunc_t func )
{
	ShadowLog( "E\tmat.AddRestoreFunc" );
	s_pfnRestore = func;
}

void CShadowMaterials::RemoveRestoreFunc( MaterialBufferRestoreFunc_t func )
{
	ShadowLog( "E\tmat.RemoveRestoreFunc\t%d", func == s_pfnRestore ? 1 : 0 );
	s_pfnRestore = NULL;
}

ImageFormat CShadowMaterials::GetShadowDepthTextureFormat()
{
	ShadowLog( "Q\tmat.GetShadowDepthTextureFormat\t%d", (int)IMAGE_FORMAT_NV_DST24 );
	return IMAGE_FORMAT_NV_DST24;
}

ImageFormat CShadowMaterials::GetNullTextureFormat()
{
	ShadowLog( "Q\tmat.GetNullTextureFormat\t%d", (int)IMAGE_FORMAT_NV_NULL );
	return IMAGE_FORMAT_NV_NULL;
}

void CShadowMaterials::BeginRenderTargetAllocation()
{
	ShadowLog( "E\tmat.BeginRenderTargetAllocation" );
}

void CShadowMaterials::EndRenderTargetAllocation()
{
	ShadowLog( "E\tmat.EndRenderTargetAllocation" );
}

void CShadowMaterials::UncacheUnusedMaterials( bool )
{
	ShadowLog( "E\tmat.UncacheUnusedMaterials" );
}

int CShadowHardwareConfig::GetDXSupportLevel()
{
	ShadowLog( "Q\thw.GetDXSupportLevel\t%d", s_nDXLevel );
	return s_nDXLevel;
}

CShadowRenderContextPtr::CShadowRenderContextPtr( CShadowMaterials * ) {}
CShadowRenderContext *CShadowRenderContextPtr::operator->() const { return &s_RenderContext; }

float CShadowRenderContext::ComputePixelDiameterOfSphere( const Vector &vecAbsOrigin, float flRadius )
{
	const float f = flRadius * s_flPixelsPerUnit;
	ShadowLog( "Q\tctx.ComputePixelDiameterOfSphere\t%s\t%s\t%s", Vec( vecAbsOrigin ).c_str(), Hex( flRadius ).c_str(), Hex( f ).c_str() );
	return f;
}

MaterialFogMode_t CShadowRenderContext::GetFogMode()
{
	ShadowLog( "Q\tctx.GetFogMode\t%d", (int)s_FogMode );
	return s_FogMode;
}

void CShadowRenderContext::FogMode( MaterialFogMode_t fogMode )
{
	ShadowLog( "E\tctx.FogMode\t%d", (int)fogMode );
	s_FogMode = fogMode;
}

void CShadowRenderContext::Bind( IMaterial *pMaterial, void *proxyData )
{
	ShadowLog( "E\tctx.Bind\t%s", ShadowMaterialName( pMaterial ) );
}

CShadowMesh *CShadowRenderContext::GetDynamicMesh( bool buffered, void *, void *, IMaterial * )
{
	ShadowLog( "E\tctx.GetDynamicMesh\t%d", buffered ? 1 : 0 );
	return &s_Mesh;
}

void CShadowRenderContext::ClearColor4ub( unsigned char r, unsigned char g, unsigned char b, unsigned char a )
{
	ShadowLog( "E\tctx.ClearColor4ub\t%d\t%d\t%d\t%d", r, g, b, a );
}

void CShadowRenderContext::ClearColor3ub( unsigned char r, unsigned char g, unsigned char b )
{
	ShadowLog( "E\tctx.ClearColor3ub\t%d\t%d\t%d", r, g, b );
}

MaterialHeightClipMode_t CShadowRenderContext::GetHeightClipMode()
{
	ShadowLog( "Q\tctx.GetHeightClipMode\t%d", (int)s_HeightClip );
	return s_HeightClip;
}

void CShadowRenderContext::SetHeightClipMode( MaterialHeightClipMode_t nHeightClipMode )
{
	ShadowLog( "E\tctx.SetHeightClipMode\t%d", (int)nHeightClipMode );
	s_HeightClip = nHeightClipMode;
}

void CShadowRenderContext::MatrixMode( MaterialMatrixMode_t matrixMode )
{
	ShadowLog( "E\tctx.MatrixMode\t%d", (int)matrixMode );
}

void CShadowRenderContext::PushMatrix() { ShadowLog( "E\tctx.PushMatrix" ); }
void CShadowRenderContext::PopMatrix() { ShadowLog( "E\tctx.PopMatrix" ); }
void CShadowRenderContext::LoadIdentity() { ShadowLog( "E\tctx.LoadIdentity" ); }

void CShadowRenderContext::LoadMatrix( const VMatrix &matrix )
{
	ShadowLog( "E\tctx.LoadMatrix\t%s", Mat( matrix ).c_str() );
}

void CShadowRenderContext::Scale( float x, float y, float z )
{
	ShadowLog( "E\tctx.Scale\t%s\t%s\t%s", Hex( x ).c_str(), Hex( y ).c_str(), Hex( z ).c_str() );
}

void CShadowRenderContext::Ortho( double left, double top, double right, double bottom, double zNear, double zFar )
{
	ShadowLog( "E\tctx.Ortho\t%.17g\t%.17g\t%.17g\t%.17g\t%.17g\t%.17g", left, top, right, bottom, zNear, zFar );
}

void CShadowRenderContext::PushRenderTargetAndViewport( ITexture *pTexture )
{
	ShadowLog( "E\tctx.PushRenderTargetAndViewport\t%s", ShadowTextureName( pTexture ) );
}

void CShadowRenderContext::PopRenderTargetAndViewport() { ShadowLog( "E\tctx.PopRenderTargetAndViewport" ); }

void CShadowRenderContext::ClearBuffers( bool bClearColor, bool bClearDepth, bool bClearStencil )
{
	ShadowLog( "E\tctx.ClearBuffers\t%d\t%d\t%d", bClearColor ? 1 : 0, bClearDepth ? 1 : 0, bClearStencil ? 1 : 0 );
}

void CShadowRenderContext::Viewport( int x, int y, int width, int height )
{
	ShadowLog( "E\tctx.Viewport\t%d\t%d\t%d\t%d", x, y, width, height );
}

void CShadowRenderContext::CopyRenderTargetToTextureEx( ITexture *pTexture, int, Rect_t *, Rect_t * )
{
	ShadowLog( "E\tctx.CopyRenderTargetToTextureEx\t%s", ShadowTextureName( pTexture ) );
}

void CShadowRenderContext::SetShadowDepthBiasFactors( float fSlopeScaleDepthBias, float fDepthBias )
{
	ShadowLog( "E\tctx.SetShadowDepthBiasFactors\t%s\t%s", Hex( fSlopeScaleDepthBias ).c_str(), Hex( fDepthBias ).c_str() );
}

void CShadowMesh::Draw( int firstIndex, int numIndices )
{
	ShadowLog( "E\tmesh.Draw\t%d\t%d", firstIndex, numIndices );
}

void CShadowMeshBuilder::Begin( CShadowMesh *, MaterialPrimitiveType_t type, int numPrimitives )
{
	ShadowLog( "E\tmesh.Begin\t%d\t%d", (int)type, numPrimitives );
}

void CShadowMeshBuilder::Position3f( float x, float y, float z )
{
	ShadowLog( "E\tmesh.Position3f\t%s\t%s\t%s", Hex( x ).c_str(), Hex( y ).c_str(), Hex( z ).c_str() );
}

void CShadowMeshBuilder::TexCoord2f( int stage, float s, float t )
{
	ShadowLog( "E\tmesh.TexCoord2f\t%d\t%s\t%s", stage, Hex( s ).c_str(), Hex( t ).c_str() );
}

void CShadowMeshBuilder::Color4ub( unsigned char r, unsigned char g, unsigned char b, unsigned char a )
{
	ShadowLog( "E\tmesh.Color4ub\t%d\t%d\t%d\t%d", r, g, b, a );
}

void CShadowMeshBuilder::AdvanceVertex() { ShadowLog( "E\tmesh.AdvanceVertex" ); }
void CShadowMeshBuilder::End() { ShadowLog( "E\tmesh.End" ); }

//-----------------------------------------------------------------------------
// The engine and its BSP query: leaf i is [i * width, (i + 1) * width) along
// x. A swept box touches the leaves its swept bounds overlap, a box the
// leaves it overlaps, in ascending order.
//-----------------------------------------------------------------------------
namespace
{

void LeavesOverX( float x0, float x1, ISpatialLeafEnumerator *pEnum, intp context, std::string *pOut )
{
	for ( int i = 0; i < s_nLeafCount; i++ )
	{
		const float l0 = i * s_flLeafWidth, l1 = ( i + 1 ) * s_flLeafWidth;
		if ( x0 < l1 && x1 > l0 )
		{
			*pOut += ( pOut->empty() ? "" : "," ) + std::to_string( i );
			if ( !pEnum->EnumerateLeaf( i, context ) )
				return;
		}
	}
}

} // namespace

class CShadowBSPQuery final : public ISpatialQuery
{
public:
	virtual int LeafCount() const { return s_nLeafCount; }
	virtual bool EnumerateLeavesAtPoint( Vector const &, ISpatialLeafEnumerator *, intp ) { return true; }
	virtual bool EnumerateLeavesInBox( Vector const &mins, Vector const &maxs, ISpatialLeafEnumerator *pEnum, intp context )
	{
		std::string s;
		LeavesOverX( mins.x, maxs.x, pEnum, context, &s );
		ShadowLog( "Q\tbsp.EnumerateLeavesInBox\t%s\t%s\t%s", Vec( mins ).c_str(), Vec( maxs ).c_str(), s.c_str() );
		return true;
	}
	virtual bool EnumerateLeavesInSphere( Vector const &, float, ISpatialLeafEnumerator *, intp ) { return true; }
	virtual bool EnumerateLeavesAlongRay( Ray_t const &ray, ISpatialLeafEnumerator *pEnum, intp context )
	{
		const float a = ray.m_Start.x, b = ray.m_Start.x + ray.m_Delta.x;
		const float x0 = std::min( a, b ) - ray.m_Extents.x, x1 = std::max( a, b ) + ray.m_Extents.x;
		std::string s;
		LeavesOverX( x0, x1, pEnum, context, &s );
		ShadowLog( "Q\tbsp.EnumerateLeavesAlongRay\t%s\t%s\t%s\t%s\t%d\t%d\t%s", Vec( ray.m_Start ).c_str(), Vec( ray.m_Delta ).c_str(),
		           Vec( ray.m_StartOffset ).c_str(), Vec( ray.m_Extents ).c_str(), ray.m_IsRay ? 1 : 0, ray.m_IsSwept ? 1 : 0,
		           s.c_str() );
		return true;
	}
};
static CShadowBSPQuery s_BSPQuery;

static CShadowEngine s_Engine;
CShadowEngine *engine = &s_Engine;

bool CShadowEngine::CullBox( const Vector &mins, const Vector &maxs )
{
	const bool b = !Overlaps( mins, maxs, s_ViewBox );
	ShadowLog( "Q\teng.CullBox\t%s\t%s\t%d", Vec( mins ).c_str(), Vec( maxs ).c_str(), b ? 1 : 0 );
	return b;
}

void CShadowEngine::GetAmbientLightColor( Vector &ambientColor )
{
	ambientColor = s_vecAmbient;
	ShadowLog( "Q\teng.GetAmbientLightColor\t%s", Vec( ambientColor ).c_str() );
}

ISpatialQuery *CShadowEngine::GetBSPTreeQuery()
{
	return &s_BSPQuery;
}

static CShadowModelInfo s_ModelInfo;
CShadowModelInfo *modelinfo = &s_ModelInfo;

int CShadowModelInfo::GetModelType( const model_t *pModel ) const
{
	const int n = pModel ? (int)( ( (intp)pModel - 0x1000 ) % 16 ) : mod_bad;
	ShadowLog( "Q\tmi.GetModelType\t%s\t%d", M( pModel ).c_str(), n );
	return n;
}

static CShadowModelRender s_ModelRender;
CShadowModelRender *modelrender = &s_ModelRender;
static matrix3x4_t s_BoneToWorld[ 1 ];

matrix3x4_t *CShadowModelRender::DrawModelShadowSetup( IClientRenderable *pRenderable, int body, int skin, DrawModelInfo_t *pInfo )
{
	CShadowThing *p = Find( IdOf( pRenderable ) );
	const bool bOk = p && p->m_bBonesOk;
	ShadowLog( "E\tmr.DrawModelShadowSetup\t%s\t%d\t%d\t%d", R( pRenderable ).c_str(), body, skin, bOk ? 1 : 0 );
	return bOk ? s_BoneToWorld : NULL;
}

void CShadowModelRender::DrawModelShadow( IClientRenderable *pRenderable, const DrawModelInfo_t &, matrix3x4_t * )
{
	ShadowLog( "E\tmr.DrawModelShadow\t%s", R( pRenderable ).c_str() );
}

static CShadowRenderView s_RenderView;
CShadowRenderView *render = &s_RenderView;

void CShadowRenderView::DrawBrushModelShadow( IClientRenderable *pRenderable )
{
	ShadowLog( "E\trv.DrawBrushModelShadow\t%s", R( pRenderable ).c_str() );
}

static CShadowStaticPropMgr s_StaticPropMgr;
CShadowStaticPropMgr *staticpropmgr = &s_StaticPropMgr;

void CShadowStaticPropMgr::AddShadowToStaticProp( ShadowHandle_t shadowHandle, IClientRenderable *pRenderable )
{
	ShadowLog( "E\tsp.AddShadowToStaticProp\t%d\t%s", shadowHandle, R( pRenderable ).c_str() );
}

void CShadowStaticPropMgr::RemoveAllShadowsFromStaticProp( IClientRenderable *pRenderable )
{
	ShadowLog( "E\tsp.RemoveAllShadowsFromStaticProp\t%s", R( pRenderable ).c_str() );
}

static CShadowView s_View;
CShadowView *view = &s_View;

void CShadowView::UpdateShadowDepthTexture( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &v )
{
	ShadowLog( "E\tview.UpdateShadowDepthTexture\t%s\t%s\txywh=%d,%d,%d,%d\tfov=%s,%s\torigin=%s\tangles=%s\tz=%s,%s\tzvm=%s,%s\t"
	           "aspect=%s\tortho=%d\tbloom=%d",
	           ShadowTextureName( pRenderTarget ), ShadowTextureName( pDepthTexture ), v.x, v.y, v.width, v.height,
	           Hex( v.fov ).c_str(), Hex( v.fovViewmodel ).c_str(), Vec( v.origin ).c_str(), Ang( v.angles ).c_str(),
	           Hex( v.zNear ).c_str(), Hex( v.zFar ).c_str(), Hex( v.zNearViewmodel ).c_str(), Hex( v.zFarViewmodel ).c_str(),
	           Hex( v.m_flAspectRatio ).c_str(), v.m_bOrtho ? 1 : 0, v.m_bDoBloomAndToneMapping ? 1 : 0 );
}

static CShadowDebugOverlay s_DebugOverlay;
CShadowDebugOverlay *debugoverlay = &s_DebugOverlay;

void CShadowDebugOverlay::AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b, bool noDepthTest, float duration )
{
	ShadowLog( "E\tdbg.AddLineOverlay\t%s\t%s\t%d\t%d\t%d\t%d\t%s", Vec( origin ).c_str(), Vec( dest ).c_str(), r, g, b, noDepthTest ? 1 : 0,
	           Hex( duration ).c_str() );
}

void CShadowDebugOverlay::AddTextOverlay( const Vector &origin, int line_offset, const char *format, ... )
{
	// The subject prints a renderable's address with "%X" when it has no
	// entity; the log names the renderable instead, so two runs compare.
	std::string text;
	va_list ap;
	va_start( ap, format );
	if ( !V_strcmp( format, "%X" ) )
	{
		const size_t p = va_arg( ap, size_t );
		text = "renderable " + R( (IClientRenderable *)p );
	}
	else
	{
		char sz[ 256 ];
		vsnprintf( sz, sizeof( sz ), format, ap );
		text = sz;
	}
	va_end( ap );
	ShadowLog( "E\tdbg.AddTextOverlay\t%s\t%d\t%s", Vec( origin ).c_str(), line_offset, text.c_str() );
}

// The tool framework's recorder: the subject calls it only when ToolsEnabled(),
// which is false here.
void ToolFramework_RecordMaterialParams( IMaterial * )
{
	ADD_FAILURE() << "the subject recorded material params with the tools off";
}

static CShadowThreadPool s_ThreadPool;
CShadowThreadPool *g_pThreadPool = &s_ThreadPool;

//-----------------------------------------------------------------------------
// The engine's shadow manager: a whole IShadowMgr that keeps what it was told
// (each shadow's materials, proxy data, flags, ShadowInfo_t and enabled bit,
// each flashlight's state and frustum) and answers from it. Its handles come
// as CUtlLinkedList's do (freed ones first, newest freed first).
//
// THE ANSWERS' RULES (the C# replay keeps the same ones):
//   * CreateShadowEx: a ShadowInfo_t of zeroes, its tex size 1,1, bias 0.
//   * ProjectShadow: m_WorldToShadow = worldToShadow, m_MaxDist = maxHeight,
//     m_FalloffOffset = falloffOffset, m_FalloffAmount = falloffAmount.
//   * ProjectFlashlight: m_WorldToShadow = worldToShadow; the frustum is the
//     six axis planes (inward, X Y Z min then X Y Z max) of
//     CalculateAABBFromProjectionMatrix( worldToShadow ).
//   * SetShadowTexCoord: m_TexOrigin = x,y; m_TexSize = w,h.
//   * SetFalloffBias: m_FalloffBias.
//   * UpdateFlashlightState: the state, as GetFlashlightState answers it.
//-----------------------------------------------------------------------------
namespace
{

struct EngineShadow_t
{
	bool m_bLive = false;
	ShadowInfo_t m_Info;
	Frustum_t m_Frustum;
	FlashlightState_t m_State;
	int m_nNextFree = -1;
};

std::vector< EngineShadow_t > s_EngineShadows;
int s_nEngineFirstFree = -1;

std::string InfoText( const ShadowInfo_t &i )
{
	return Mat( i.m_WorldToShadow ) + "\t" + Hex( i.m_FalloffOffset ) + "\t" + Hex( i.m_MaxDist ) + "\t" + Hex( i.m_FalloffAmount ) +
	       "\t" + Hex( i.m_TexOrigin.x ) + "," + Hex( i.m_TexOrigin.y ) + "\t" + Hex( i.m_TexSize.x ) + "," + Hex( i.m_TexSize.y ) +
	       "\t" + std::to_string( i.m_FalloffBias );
}

std::string StateText( const FlashlightState_t &s )
{
	char sz[ 64 ];
	snprintf( sz, sizeof( sz ), "%d\t%d", s.m_bEnableShadows ? 1 : 0, s.m_nSpotlightTextureFrame );
	return Vec( s.m_vecLightOrigin ) + "\t" + Hex( s.m_quatOrientation.x ) + "," + Hex( s.m_quatOrientation.y ) + "," +
	       Hex( s.m_quatOrientation.z ) + "," + Hex( s.m_quatOrientation.w ) + "\t" + Hex( s.m_NearZ ) + "\t" + Hex( s.m_FarZ ) + "\t" +
	       Hex( s.m_fHorizontalFOVDegrees ) + "\t" + Hex( s.m_fVerticalFOVDegrees ) + "\t" + Hex( s.m_fQuadraticAtten ) + "," +
	       Hex( s.m_fLinearAtten ) + "," + Hex( s.m_fConstantAtten ) + "\t" + Hex( s.m_Color[ 0 ] ) + "," + Hex( s.m_Color[ 1 ] ) + "," +
	       Hex( s.m_Color[ 2 ] ) + "," + Hex( s.m_Color[ 3 ] ) + "\t" + ShadowTextureName( s.m_pSpotlightTexture ) + "\t" + sz + "\t" +
	       Hex( s.m_flShadowMapResolution ) + "\t" + Hex( s.m_flShadowAtten ) + "\t" + Hex( s.m_flShadowSlopeScaleDepthBias ) + "\t" +
	       Hex( s.m_flShadowDepthBias );
}

std::string FrustumText( const Frustum_t &f )
{
	std::string s;
	for ( int i = 0; i < FRUSTUM_NUMPLANES; i++ )
	{
		const cplane_t *p = f.GetPlane( i );
		s += ( i ? ";" : "" ) + Vec( p->normal ) + "," + Hex( p->dist ) + "," + std::to_string( (int)p->type ) + "," +
		     std::to_string( (int)p->signbits );
	}
	return s;
}

} // namespace

class CShadowEngineShadowMgr final : public IShadowMgr
{
public:
	virtual ShadowHandle_t CreateShadow( IMaterial *pMaterial, IMaterial *pModelMaterial, void *pBindProxy, int creationFlags )
	{
		ADD_FAILURE() << "the subject called CreateShadow, not CreateShadowEx";
		return CreateShadowEx( pMaterial, pModelMaterial, pBindProxy, creationFlags );
	}
	virtual void DestroyShadow( ShadowHandle_t h )
	{
		ShadowLog( "E\tsm.DestroyShadow\t%d", h );
		s_EngineShadows[ h ].m_bLive = false;
		s_EngineShadows[ h ].m_nNextFree = s_nEngineFirstFree;
		s_nEngineFirstFree = h;
	}
	virtual void SetShadowMaterial( ShadowHandle_t h, IMaterial *pMaterial, IMaterial *pModelMaterial, void *pBindProxy )
	{
		ShadowLog( "E\tsm.SetShadowMaterial\t%d\t%s\t%s\t%d", h, ShadowMaterialName( pMaterial ), ShadowMaterialName( pModelMaterial ),
		           (int)(intp)pBindProxy );
	}
	virtual void ProjectShadow( ShadowHandle_t h, const Vector &origin, const Vector &projectionDir, const VMatrix &worldToShadow,
	                            const Vector2D &size, int nLeafCount, const int *pLeafList, float maxHeight, float falloffOffset,
	                            float falloffAmount, const Vector &vecCasterOrigin )
	{
		ShadowLog( "E\tsm.ProjectShadow\t%d\t%s\t%s\t%s\t%s,%s\t%s\t%s\t%s\t%s\t%s", h, Vec( origin ).c_str(), Vec( projectionDir ).c_str(),
		           Mat( worldToShadow ).c_str(), Hex( size.x ).c_str(), Hex( size.y ).c_str(), Leaves( nLeafCount, pLeafList ).c_str(),
		           Hex( maxHeight ).c_str(), Hex( falloffOffset ).c_str(), Hex( falloffAmount ).c_str(), Vec( vecCasterOrigin ).c_str() );
		ShadowInfo_t &i = s_EngineShadows[ h ].m_Info;
		i.m_WorldToShadow = worldToShadow;
		i.m_MaxDist = maxHeight;
		i.m_FalloffOffset = falloffOffset;
		i.m_FalloffAmount = falloffAmount;
	}
	virtual void ProjectFlashlight( ShadowHandle_t h, const VMatrix &worldToShadow, int nLeafCount, const int *pLeafList )
	{
		ShadowLog( "E\tsm.ProjectFlashlight\t%d\t%s\t%s", h, Mat( worldToShadow ).c_str(), Leaves( nLeafCount, pLeafList ).c_str() );
		EngineShadow_t &s = s_EngineShadows[ h ];
		s.m_Info.m_WorldToShadow = worldToShadow;
		Vector mins, maxs;
		CalculateAABBFromProjectionMatrix( worldToShadow, &mins, &maxs );
		s.m_Frustum.SetPlane( 0, PLANE_X, Vector( 1, 0, 0 ), mins.x );
		s.m_Frustum.SetPlane( 1, PLANE_Y, Vector( 0, 1, 0 ), mins.y );
		s.m_Frustum.SetPlane( 2, PLANE_Z, Vector( 0, 0, 1 ), mins.z );
		s.m_Frustum.SetPlane( 3, PLANE_X, Vector( -1, 0, 0 ), -maxs.x );
		s.m_Frustum.SetPlane( 4, PLANE_Y, Vector( 0, -1, 0 ), -maxs.y );
		s.m_Frustum.SetPlane( 5, PLANE_Z, Vector( 0, 0, -1 ), -maxs.z );
	}
	virtual const ShadowInfo_t &GetInfo( ShadowHandle_t h )
	{
		ShadowLog( "Q\tsm.GetInfo\t%d\t%s", h, InfoText( s_EngineShadows[ h ].m_Info ).c_str() );
		return s_EngineShadows[ h ].m_Info;
	}
	virtual const Frustum_t &GetFlashlightFrustum( ShadowHandle_t h )
	{
		ShadowLog( "Q\tsm.GetFlashlightFrustum\t%d\t%s", h, FrustumText( s_EngineShadows[ h ].m_Frustum ).c_str() );
		return s_EngineShadows[ h ].m_Frustum;
	}
	virtual void AddShadowToBrushModel( ShadowHandle_t h, model_t *pModel, const Vector &origin, const QAngle &angles )
	{
		ShadowLog( "E\tsm.AddShadowToBrushModel\t%d\t%s\t%s\t%s", h, M( pModel ).c_str(), Vec( origin ).c_str(), Ang( angles ).c_str() );
	}
	virtual void RemoveAllShadowsFromBrushModel( model_t *pModel )
	{
		ShadowLog( "E\tsm.RemoveAllShadowsFromBrushModel\t%s", M( pModel ).c_str() );
	}
	virtual void SetShadowTexCoord( ShadowHandle_t h, float x, float y, float w, float hh )
	{
		ShadowLog( "E\tsm.SetShadowTexCoord\t%d\t%s\t%s\t%s\t%s", h, Hex( x ).c_str(), Hex( y ).c_str(), Hex( w ).c_str(), Hex( hh ).c_str() );
		ShadowInfo_t &i = s_EngineShadows[ h ].m_Info;
		i.m_TexOrigin.Init( x, y );
		i.m_TexSize.Init( w, hh );
	}
	virtual void AddShadowToModel( ShadowHandle_t h, ModelInstanceHandle_t instance )
	{
		ShadowLog( "E\tsm.AddShadowToModel\t%d\t%d", h, instance );
	}
	virtual void RemoveAllShadowsFromModel( ModelInstanceHandle_t instance )
	{
		ShadowLog( "E\tsm.RemoveAllShadowsFromModel\t%d", instance );
	}
	virtual void ClearExtraClipPlanes( ShadowHandle_t h ) { ShadowLog( "E\tsm.ClearExtraClipPlanes\t%d", h ); }
	virtual void AddExtraClipPlane( ShadowHandle_t h, const Vector &normal, float dist )
	{
		ShadowLog( "E\tsm.AddExtraClipPlane\t%d\t%s\t%s", h, Vec( normal ).c_str(), Hex( dist ).c_str() );
	}
	virtual void EnableShadow( ShadowHandle_t h, bool bEnable ) { ShadowLog( "E\tsm.EnableShadow\t%d\t%d", h, bEnable ? 1 : 0 ); }
	virtual void SetFalloffBias( ShadowHandle_t h, unsigned char ucBias )
	{
		ShadowLog( "E\tsm.SetFalloffBias\t%d\t%d", h, ucBias );
		s_EngineShadows[ h ].m_Info.m_FalloffBias = ucBias;
	}
	virtual void UpdateFlashlightState( ShadowHandle_t h, const FlashlightState_t &lightState )
	{
		ShadowLog( "E\tsm.UpdateFlashlightState\t%d\t%s", h, StateText( lightState ).c_str() );
		s_EngineShadows[ h ].m_State = lightState;
	}
	virtual void DrawFlashlightDepthTexture() { ADD_FAILURE() << "the subject called DrawFlashlightDepthTexture"; }
	virtual void AddFlashlightRenderable( ShadowHandle_t h, IClientRenderable *pRenderable )
	{
		ShadowLog( "E\tsm.AddFlashlightRenderable\t%d\t%s", h, R( pRenderable ).c_str() );
	}
	virtual ShadowHandle_t CreateShadowEx( IMaterial *pMaterial, IMaterial *pModelMaterial, void *pBindProxy, int creationFlags )
	{
		int h;
		if ( s_nEngineFirstFree >= 0 )
		{
			h = s_nEngineFirstFree;
			s_nEngineFirstFree = s_EngineShadows[ h ].m_nNextFree;
		}
		else
		{
			h = (int)s_EngineShadows.size();
			s_EngineShadows.emplace_back();
		}
		EngineShadow_t &s = s_EngineShadows[ h ];
		s = EngineShadow_t();
		s.m_bLive = true;
		memset( (void *)&s.m_Info, 0, sizeof( s.m_Info ) );
		s.m_Info.m_TexSize.Init( 1.0f, 1.0f );
		ShadowLog( "E\tsm.CreateShadowEx\t%s\t%s\t%d\t%d\t=>%d", ShadowMaterialName( pMaterial ), ShadowMaterialName( pModelMaterial ),
		           (int)(intp)pBindProxy, creationFlags, h );
		return (ShadowHandle_t)h;
	}
	virtual void SetFlashlightDepthTexture( ShadowHandle_t h, ITexture *pFlashlightDepthTexture, unsigned char ucShadowStencilBit )
	{
		ShadowLog( "E\tsm.SetFlashlightDepthTexture\t%d\t%s\t%d", h, ShadowTextureName( pFlashlightDepthTexture ), ucShadowStencilBit );
	}
	virtual const FlashlightState_t &GetFlashlightState( ShadowHandle_t h )
	{
		ShadowLog( "Q\tsm.GetFlashlightState\t%d\t%s", h, StateText( s_EngineShadows[ h ].m_State ).c_str() );
		return s_EngineShadows[ h ].m_State;
	}
	virtual void SetFlashlightRenderState( ShadowHandle_t h ) { ShadowLog( "E\tsm.SetFlashlightRenderState\t%d", h ); }
};
static CShadowEngineShadowMgr s_EngineShadowMgr;
IShadowMgr *shadowmgr = &s_EngineShadowMgr;

//-----------------------------------------------------------------------------
// The material the two proxies are initialised on, and its vars. A var's
// setters are E rows naming the var.
//-----------------------------------------------------------------------------
namespace
{

class CProxyVar final : public IMaterialVar
{
public:
	CProxyVar( const char *pszName, IMaterial *pOwner ) : m_strName( pszName ), m_pOwner( pOwner ) {}
	virtual ITexture *GetTextureValue( void ) { return NULL; }
	virtual char const *GetName( void ) const { return m_strName.c_str(); }
	virtual MaterialVarSym_t GetNameAsSymbol() const { return 0; }
	virtual void SetFloatValue( float val ) { ShadowLog( "E\tvar.SetFloatValue\t%s\t%s", m_strName.c_str(), Hex( val ).c_str() ); }
	virtual void SetIntValue( int val ) { ShadowLog( "E\tvar.SetIntValue\t%s\t%d", m_strName.c_str(), val ); }
	virtual void SetStringValue( char const *val ) { ShadowLog( "E\tvar.SetStringValue\t%s\t%s", m_strName.c_str(), val ); }
	virtual char const *GetStringValue( void ) const { return ""; }
	virtual void SetFourCCValue( FourCC, void * ) {}
	virtual void GetFourCCValue( FourCC *, void ** ) {}
	virtual void SetVecValue( float const *val, int numcomps )
	{
		std::string s;
		for ( int i = 0; i < numcomps; i++ )
			s += ( i ? "," : "" ) + Hex( val[ i ] );
		ShadowLog( "E\tvar.SetVecValue\t%s\t%d\t%s", m_strName.c_str(), numcomps, s.c_str() );
	}
	virtual void SetVecValue( float x, float y ) { ShadowLog( "E\tvar.SetVecValue2\t%s", m_strName.c_str() ); }
	virtual void SetVecValue( float x, float y, float z ) { ShadowLog( "E\tvar.SetVecValue3\t%s", m_strName.c_str() ); }
	virtual void SetVecValue( float x, float y, float z, float w ) { ShadowLog( "E\tvar.SetVecValue4\t%s", m_strName.c_str() ); }
	virtual void GetLinearVecValue( float *, int ) const {}
	virtual void SetTextureValue( ITexture *pTexture )
	{
		ShadowLog( "E\tvar.SetTextureValue\t%s\t%s", m_strName.c_str(), ShadowTextureName( pTexture ) );
	}
	virtual IMaterial *GetMaterialValue( void ) { return NULL; }
	virtual void SetMaterialValue( IMaterial * ) {}
	virtual bool IsDefined() const { return true; }
	virtual void SetUndefined() {}
	virtual void SetMatrixValue( VMatrix const &matrix ) { ShadowLog( "E\tvar.SetMatrixValue\t%s\t%s", m_strName.c_str(), Mat( matrix ).c_str() ); }
	virtual const VMatrix &GetMatrixValue() { return m_Matrix; }
	virtual bool MatrixIsIdentity() const { return true; }
	virtual void CopyFrom( IMaterialVar * ) {}
	virtual void SetValueAutodetectType( char const * ) {}
	virtual IMaterial *GetOwningMaterial() { return m_pOwner; }
	virtual void SetVecComponentValue( float, int ) {}
	virtual int GetIntValueInternal( void ) const { return 0; }
	virtual float GetFloatValueInternal( void ) const { return 0.0f; }
	virtual float const *GetVecValueInternal() const { return m_Vec; }
	virtual void GetVecValueInternal( float *, int ) const {}
	virtual int VectorSizeInternal() const { return 0; }

	std::string m_strName;
	IMaterial *m_pOwner;
	VMatrix m_Matrix;
	float m_Vec[ 4 ] = {};
};

// A material that has the vars named in its list; FindVar logs each lookup.
class CProxyMaterial final : public IMaterial
{
public:
	CProxyMaterial( const char *pszName, std::vector< std::string > vars ) : m_strName( pszName )
	{
		for ( const std::string &v : vars )
			m_Vars.emplace_back( new CProxyVar( v.c_str(), this ) );
	}
	virtual IMaterialVar *FindVar( const char *varName, bool *found, bool complain )
	{
		for ( auto &v : m_Vars )
		{
			if ( v->m_strName == varName )
			{
				*found = true;
				ShadowLog( "Q\tmat.FindVar\t%s\t%s\t%d\t1", m_strName.c_str(), varName, complain ? 1 : 0 );
				return v.get();
			}
		}
		*found = false;
		ShadowLog( "Q\tmat.FindVar\t%s\t%s\t%d\t0", m_strName.c_str(), varName, complain ? 1 : 0 );
		return &m_Undefined;
	}
	virtual const char *GetName() const { return m_strName.c_str(); }
	virtual const char *GetTextureGroupName() const { return ""; }
	virtual PreviewImageRetVal_t GetPreviewImageProperties( int *, int *, ImageFormat *, bool * ) const { return MATERIAL_NO_PREVIEW_IMAGE; }
	virtual PreviewImageRetVal_t GetPreviewImage( unsigned char *, int, int, ImageFormat ) const { return MATERIAL_NO_PREVIEW_IMAGE; }
	virtual int GetMappingWidth() { return 0; }
	virtual int GetMappingHeight() { return 0; }
	virtual int GetNumAnimationFrames() { return 0; }
	virtual bool InMaterialPage( void ) { return false; }
	virtual void GetMaterialOffset( float * ) {}
	virtual void GetMaterialScale( float * ) {}
	virtual IMaterial *GetMaterialPage( void ) { return NULL; }
	virtual void IncrementReferenceCount( void ) {}
	virtual void DecrementReferenceCount( void ) {}
	virtual int GetEnumerationID( void ) const { return 0; }
	virtual void GetLowResColorSample( float, float, float * ) const {}
	virtual void RecomputeStateSnapshots() {}
	virtual bool IsTranslucent() { return false; }
	virtual bool IsAlphaTested() { return false; }
	virtual bool IsVertexLit() { return false; }
	virtual VertexFormat_t GetVertexFormat() const { return 0; }
	virtual bool HasProxy( void ) const { return true; }
	virtual bool UsesEnvCubemap( void ) { return false; }
	virtual bool NeedsTangentSpace( void ) { return false; }
	virtual bool NeedsPowerOfTwoFrameBufferTexture( bool ) { return false; }
	virtual bool NeedsFullFrameBufferTexture( bool ) { return false; }
	virtual bool NeedsSoftwareSkinning( void ) { return false; }
	virtual void AlphaModulate( float ) {}
	virtual void ColorModulate( float, float, float ) {}
	virtual void SetMaterialVarFlag( MaterialVarFlags_t, bool ) {}
	virtual bool GetMaterialVarFlag( MaterialVarFlags_t ) const { return false; }
	virtual void GetReflectivity( Vector & ) {}
	virtual bool GetPropertyFlag( MaterialPropertyTypes_t ) { return false; }
	virtual bool IsTwoSided() { return false; }
	virtual void SetShader( const char * ) {}
	virtual int GetNumPasses( void ) { return 0; }
	virtual int GetTextureMemoryBytes( void ) { return 0; }
	virtual void Refresh() {}
	virtual bool NeedsLightmapBlendAlpha( void ) { return false; }
	virtual bool NeedsSoftwareLighting( void ) { return false; }
	virtual int ShaderParamCount() const { return 0; }
	virtual IMaterialVar **GetShaderParams( void ) { return NULL; }
	virtual bool IsErrorMaterial() const { return false; }
	virtual void SetUseFixedFunctionBakedLighting( bool ) {}
	virtual float GetAlphaModulation() { return 1.0f; }
	virtual void GetColorModulation( float *, float *, float * ) {}
	virtual MorphFormat_t GetMorphFormat() const { return 0; }
	virtual IMaterialVar *FindVarFast( char const *, unsigned int * ) { return NULL; }
	virtual void SetShaderAndParams( KeyValues * ) {}
	virtual const char *GetShaderName() const { return ""; }
	virtual void DeleteIfUnreferenced() {}
	virtual bool IsSpriteCard() { return false; }
	virtual void CallBindProxy( void * ) {}
	virtual IMaterial *CheckProxyReplacement( void * ) { return this; }
	virtual void RefreshPreservingMaterialVars() {}
	virtual bool WasReloadedFromWhitelist() { return false; }
	virtual bool IsPrecached() const { return true; }

	std::string m_strName;
	std::vector< std::unique_ptr< CProxyVar > > m_Vars;
	CProxyVar m_Undefined{ "-", this };
};

} // namespace

// What the subject's header and its interface registrations link against:
// igamesystem.cpp's two destructors, and tier1's InterfaceReg, which records
// the names the material system would ask for and nothing more.
IGameSystem::~IGameSystem() {}
IGameSystemPerFrame::~IGameSystemPerFrame() {}

static std::vector< std::string > s_ExposedAs;

InterfaceReg::InterfaceReg( InstantiateInterfaceFn fn, const char *pName ) : m_pName( pName )
{
	m_CreateFn = fn;
	m_pNext = NULL;
	s_ExposedAs.push_back( pName );
}

//-----------------------------------------------------------------------------
// THE SUBJECT. Its members are opened to the oracle's probes (which read the
// manager's own state to name the arms it is about to take; they never write
// it): `private` is spelled `public` for the subject's text alone, which
// changes no layout and no behaviour.
//-----------------------------------------------------------------------------
#define private public
#include "clientshadowmgr.cpp"
#undef private
#include "tier0/memdbgoff.h"

//-----------------------------------------------------------------------------
// THE PROBES: what the oracle reads of the subject's state at a moment, to
// name an arm (TheScenarioReachesEveryArm). Each is taken quietly (no row is
// written while it runs) and is tied to the log line it was taken at, so the
// fact can hold it to the rows stock wrote next.
//-----------------------------------------------------------------------------
struct Probe_t
{
	size_t m_nLine;        // rows written before the probe
	std::string m_strArm;  // the arm, or arms joined by '|'
	int m_nTag;
};
static std::vector< Probe_t > s_Probes;

static size_t LogLines()
{
	return g_nLogLines;
}

static void Probe( const std::string &arm, int nTag = 0 )
{
	s_Probes.push_back( { LogLines(), arm, nTag } );
}

// stock's AddShadowToReceiver and CullReceiver, read step by step with the
// subject's own helpers before the subject runs them.
static void ProbeReceiver( ClientShadowHandle_t handle, IClientRenderable *pRenderable, ShadowReceiver_t type )
{
	CClientShadowMgr &m = s_ClientShadowMgr;
	++g_nQuiet;
	const CClientShadowMgr::ClientShadow_t &shadow = m.m_Shadows[ handle ];
	IClientRenderable *pSource = ClientEntityList().GetClientRenderableFromHandle( shadow.m_Entity );
	std::string arm;
	const bool bFlash = ( shadow.m_Flags & SHADOW_FLAGS_FLASHLIGHT ) != 0;
	if ( pSource == pRenderable )
		arm = "d6.self";
	else if ( !pRenderable->ShouldReceiveProjectedTextures( SHADOW_FLAGS_PROJECTED_TEXTURE_TYPE_MASK ) )
		arm = "d6.notreceiving";
	else
	{
		bool bCulled = false;
		if ( bFlash )
		{
			const Frustum_t &frustum = shadowmgr->GetFlashlightFrustum( shadow.m_ShadowHandle );
			Vector mins, maxs;
			pRenderable->GetRenderBoundsWorldspace( mins, maxs );
			bCulled = R_CullBox( mins, maxs, frustum );
			arm = bCulled ? "d6.flash.culled" : "d6.flash.kept";
		}
		else
		{
			Vector origin;
			float radius;
			m.ComputeBoundingSphere( pRenderable, origin, radius );
			Vector localOrigin;
			const ShadowInfo_t &info = shadowmgr->GetInfo( shadow.m_ShadowHandle );
			Vector3DMultiplyPosition( shadow.m_WorldToShadow, origin, localOrigin );
			Vector shadowMin, shadowMax;
			shadowMin.Init( -shadow.m_WorldSize.x * 0.5f, -shadow.m_WorldSize.y * 0.5f, 0 );
			shadowMax.Init( shadow.m_WorldSize.x * 0.5f, shadow.m_WorldSize.y * 0.5f, info.m_MaxDist );
			if ( !IsBoxIntersectingSphere( shadowMin, shadowMax, localOrigin, radius ) )
			{
				bCulled = true;
				arm = "d6.shadow.boxmiss";
			}
			else
			{
				Vector originSource;
				float radiusSource;
				m.ComputeBoundingSphere( pSource, originSource, radiusSource );
				bool bFound = false;
				cplane_t plane;
				if ( !IsSphereIntersectingSphere( originSource, radiusSource, origin, radius ) )
				{
					bFound = true;
					VectorSubtract( origin, originSource, plane.normal );
					arm = "d6.plane.centers";
				}
				else
				{
					bFound = m.ComputeSeparatingPlane( pRenderable, pSource, &plane );
					arm = bFound ? "d6.plane.separating" : "d6.plane.none";
				}
				if ( bFound )
				{
					const Vector vecShadowDir = m.GetShadowDirection( pSource );
					const float shadowDot = DotProduct( vecShadowDir, plane.normal );
					const float receiverDot = DotProduct( plane.normal, origin );
					const float sourceDot = DotProduct( plane.normal, originSource );
					if ( shadowDot > 0.0f )
					{
						bCulled = receiverDot <= sourceDot;
						arm += bCulled ? "|d6.side.pos.culled" : "|d6.side.pos.kept";
					}
					else
					{
						bCulled = receiverDot >= sourceDot;
						arm += bCulled ? "|d6.side.neg.culled" : "|d6.side.neg.kept";
					}
				}
			}
		}
		if ( bCulled )
			arm += "|d6.culled";
		else
		{
			const char *pszKind = type == SHADOW_RECEIVER_BRUSH_MODEL ? "brush" : type == SHADOW_RECEIVER_STATIC_PROP ? "static" : "studio";
			const char *pszCast = bFlash ? "flash" : m.GetActualShadowCastType( handle ) == SHADOWS_RENDER_TO_TEXTURE ? "rtt" : "simple";
			arm += std::string( "|d6.kept." ) + pszKind + "." + pszCast;
		}
	}
	--g_nQuiet;
	Probe( arm );
}

// Each render-to-texture shadow's atlas fragment: its power, or -1.
static std::map< ClientShadowHandle_t, std::pair< int, int > > AtlasState()
{
	CClientShadowMgr &m = s_ClientShadowMgr;
	std::map< ClientShadowHandle_t, std::pair< int, int > > state;   // shadow -> (fragment, power)
	if ( !m.m_RenderToTextureActive )
		return state;
	for ( ClientShadowHandle_t h = m.m_Shadows.Head(); h != m.m_Shadows.InvalidIndex(); h = m.m_Shadows.Next( h ) )
	{
		const CClientShadowMgr::ClientShadow_t &shadow = m.m_Shadows[ h ];
		if ( !( shadow.m_Flags & SHADOW_FLAGS_USE_RENDER_TO_TEXTURE ) )
			continue;
		const int f = m.m_ShadowAllocator.m_Textures[ shadow.m_ShadowTexture ].m_Fragment;
		const bool bValid = f != (int)(unsigned short)~0;
		state[ h ] = { bValid ? f : -1, bValid ? m.m_ShadowAllocator.GetFragmentPower( (unsigned short)f ) : -1 };
	}
	return state;
}

// The power stock's UseTexture asks for, from the visible list's area.
static int DesiredPower( ClientShadowHandle_t h, float flArea )
{
	CClientShadowMgr &m = s_ClientShadowMgr;
	const int nPower = m.m_ShadowAllocator.m_Textures[ m.m_Shadows[ h ].m_ShadowTexture ].m_Power;
	int nDesiredPower = 4, nDesiredWidth = 16;
	while ( ( nDesiredWidth * nDesiredWidth ) < flArea )
	{
		if ( nDesiredPower >= nPower )
		{
			nDesiredPower = nPower;
			break;
		}
		++nDesiredPower;
		nDesiredWidth <<= 1;
	}
	return nDesiredPower;
}

// The dirty set, in the order the walk visits it.
static std::vector< ClientShadowHandle_t > DirtyInOrder()
{
	CClientShadowMgr &m = s_ClientShadowMgr;
	std::vector< ClientShadowHandle_t > v;
	for ( unsigned short i = m.m_DirtyShadows.FirstInorder(); i != m.m_DirtyShadows.InvalidIndex(); i = m.m_DirtyShadows.NextInorder( i ) )
		v.push_back( m.m_DirtyShadows[ i ] );
	return v;
}

// The subject as its callers reach it: through IClientShadowMgr.
static IClientShadowMgr &Subject()
{
	return *g_pClientShadowMgr;
}

namespace
{

//-----------------------------------------------------------------------------
// The scenario's stimulus: each thing as a T row, each setting as a CV or ENV
// row, each call as a C row before it is made and its answer as an R row.
//-----------------------------------------------------------------------------
void Describe( const CShadowThing *p )
{
	ShadowLog( "T\t%d\t%d\t%d\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%d\t%s\t%d\t%s\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d", p->m_nId,
	           p->m_bHasModel ? 1 : 0, p->m_nModelType, Vec( p->m_vecOrigin ).c_str(), Ang( p->m_angAngles ).c_str(),
	           Vec( p->m_vecMins ).c_str(), Vec( p->m_vecMaxs ).c_str(), Vec( p->m_vecRttMins ).c_str(), Vec( p->m_vecRttMaxs ).c_str(),
	           Vec( p->m_vecAbsMins ).c_str(), Vec( p->m_vecAbsMaxs ).c_str(), Mat34( p->m_Transform ).c_str(), p->m_bCastDistance ? 1 : 0,
	           Hex( p->m_flCastDistance ).c_str(), p->m_bCastDirection ? 1 : 0, Vec( p->m_vecCastDirection ).c_str(), p->m_nParent,
	           p->m_nFirstChild, p->m_nNextPeer, p->m_nReceiveFlags, p->m_hShadow, p->m_bShadowDirty ? 1 : 0, p->m_hInstance, p->m_nBody,
	           p->m_nSkin, p->m_bBonesOk ? 1 : 0, p->m_bDirtyOnOrigin ? 1 : 0, p->m_bGone ? 1 : 0 );
}

// The box the renderable occupies in the world, and its frame, from its
// origin, angles and render box.
void Place( CShadowThing *p )
{
	AngleMatrix( p->m_angAngles, p->m_vecOrigin, p->m_Transform );
	TransformAABB( p->m_Transform, p->m_vecMins, p->m_vecMaxs, p->m_vecAbsMins, p->m_vecAbsMaxs );
}

// A thing at an origin with a render box of half extents (hx, hy) and height
// hz above its origin (a model stands on its origin); its RTT box is the
// same unless the scenario sets it.
CShadowThing *Thing( int nId, int nModelType, float x, float y, float z, float hx, float hy, float hz )
{
	s_Things.emplace_back( new CShadowThing( nId ) );
	CShadowThing *p = s_Things.back().get();
	p->m_nModelType = nModelType;
	p->m_vecOrigin.Init( x, y, z );
	p->m_vecMins.Init( -hx, -hy, 0.0f );
	p->m_vecMaxs.Init( hx, hy, hz );
	p->m_vecRttMins = p->m_vecMins;
	p->m_vecRttMaxs = p->m_vecMaxs;
	Place( p );
	Describe( p );
	return p;
}

// A centred box (receivers, the crowd).
CShadowThing *Box( int nId, int nModelType, float x, float y, float z, float h )
{
	s_Things.emplace_back( new CShadowThing( nId ) );
	CShadowThing *p = s_Things.back().get();
	p->m_nModelType = nModelType;
	p->m_vecOrigin.Init( x, y, z );
	p->m_vecMins.Init( -h, -h, -h );
	p->m_vecMaxs.Init( h, h, h );
	p->m_vecRttMins = p->m_vecMins;
	p->m_vecRttMaxs = p->m_vecMaxs;
	Place( p );
	Describe( p );
	return p;
}

void Redescribe( CShadowThing *p )
{
	Place( p );
	Describe( p );
}

void Env( const char *pszKey, const std::string &value )
{
	ShadowLog( "ENV\t%s\t%s", pszKey, value.c_str() );
}

void SetLevel( int nLeaves, float flWidth )
{
	s_nLeafCount = nLeaves;
	s_flLeafWidth = flWidth;
	Env( "leaves", std::to_string( nLeaves ) + "\t" + Hex( flWidth ) );
}

void SetViewBox( float x0, float y0, float z0, float x1, float y1, float z1 )
{
	s_ViewBox.mins.Init( x0, y0, z0 );
	s_ViewBox.maxs.Init( x1, y1, z1 );
	Env( "viewbox", Vec( s_ViewBox.mins ) + "\t" + Vec( s_ViewBox.maxs ) );
}

void SetAmbient( float r, float g, float b )
{
	s_vecAmbient.Init( r, g, b );
	Env( "ambient", Vec( s_vecAmbient ) );
}

void SetPixelsPerUnit( float f )
{
	s_flPixelsPerUnit = f;
	Env( "ppu", Hex( f ) );
}

void SetDXLevel( int n )
{
	s_nDXLevel = n;
	Env( "dxlevel", std::to_string( n ) );
}

void SetSupportsDepth( bool b )
{
	s_bSupportsDepth = b;
	Env( "supportsdepth", b ? "1" : "0" );
}

void SetMaxRenderTarget( int n )
{
	s_nMaxRenderTarget = n;
	Env( "maxrendertarget", std::to_string( n ) );
}

void SetTools( bool b )
{
	CommandLine()->CreateCmdLine( b ? "shadowmgr_oracle_test -tools" : "shadowmgr_oracle_test" );
	Env( "tools", b ? "1" : "0" );
}

void SetFog( MaterialFogMode_t m )
{
	s_FogMode = m;
	Env( "fog", std::to_string( (int)m ) );
}

void SetHeightClip( MaterialHeightClipMode_t m )
{
	s_HeightClip = m;
	Env( "heightclip", std::to_string( (int)m ) );
}

void CV( ConVar &var, const char *pszValue )
{
	ShadowLog( "CV\t%s\t%s", var.GetName(), pszValue );
	s_bScenarioSetsConVar = true;
	var.SetValue( pszValue );
	s_bScenarioSetsConVar = false;
}

void Frame( int n )
{
	s_Globals.framecount = n;
	s_Globals.curtime = n * 0.015f;
	ShadowLog( "F\t%d\t%s", n, Hex( s_Globals.curtime ).c_str() );
}

void Call( const std::string &what )
{
	ShadowLog( "C\t%s", what.c_str() );
}

void Init()
{
	Call( "Init" );
	ShadowLog( "R\t%d", Subject().Init() ? 1 : 0 );
}

void Shutdown()
{
	Call( "Shutdown" );
	Subject().Shutdown();
}

void LevelInit()
{
	Call( "LevelInitPreEntity" );
	Subject().LevelInitPreEntity();
}

void LevelShutdown()
{
	Call( "LevelShutdownPostEntity" );
	Subject().LevelShutdownPostEntity();
}

void PreRender()
{
	Call( "PreRender" );
	// The dirty set as the walk will visit it.
	std::string order;
	for ( ClientShadowHandle_t h : DirtyInOrder() )
		order += ( order.empty() ? "" : "," ) + std::to_string( h );
	Probe( "d3.walk:" + order );
	Subject().PreRender();
}

void AdvanceFrame()
{
	Call( "AdvanceFrame" );
	Subject().AdvanceFrame();
}

ClientShadowHandle_t CreateShadow( CShadowThing *p, int flags )
{
	Call( "CreateShadow\t" + std::to_string( p->m_nId ) + "\t" + std::to_string( flags ) );
	const ClientShadowHandle_t h = Subject().CreateShadow( ShadowEntityOf( p->m_nId ), flags );
	ShadowLog( "R\t%d", h );
	// The entity keeps the handle, as C_BaseEntity::CreateShadow does.
	p->m_hShadow = h;
	Describe( p );
	return h;
}

void DestroyShadow( CShadowThing *p )
{
	Call( "DestroyShadow\t" + std::to_string( p->m_hShadow ) );
	Subject().DestroyShadow( p->m_hShadow );
	p->m_hShadow = CLIENTSHADOW_INVALID_HANDLE;
	Describe( p );
}

std::string StateArgs( const FlashlightState_t &s )
{
	return StateText( s );
}

ShadowTexture_t s_SpotlightTexture = { "effects/flashlight001", 128, 128 };

FlashlightState_t Light( float x, float y, float z, const QAngle &angles, bool bShadows, float flFar = 300.0f )
{
	FlashlightState_t s;
	s.m_vecLightOrigin.Init( x, y, z );
	AngleQuaternion( RadianEuler( DEG2RAD( angles.z ), DEG2RAD( angles.x ), DEG2RAD( angles.y ) ), s.m_quatOrientation );
	s.m_NearZ = 4.0f;
	s.m_FarZ = flFar;
	s.m_fHorizontalFOVDegrees = 60.0f;
	s.m_fVerticalFOVDegrees = 50.0f;
	s.m_fQuadraticAtten = 0.0f;
	s.m_fLinearAtten = 100.0f;
	s.m_fConstantAtten = 0.0f;
	s.m_Color[ 0 ] = 1.0f; s.m_Color[ 1 ] = 0.9f; s.m_Color[ 2 ] = 0.8f; s.m_Color[ 3 ] = 0.0f;
	s.m_pSpotlightTexture = (ITexture *)&s_SpotlightTexture;
	s.m_nSpotlightTextureFrame = 0;
	s.m_bEnableShadows = bShadows;
	s.m_flShadowMapResolution = 1024.0f;
	s.m_flShadowAtten = 0.35f;
	s.m_flShadowSlopeScaleDepthBias = 16.0f;
	s.m_flShadowDepthBias = 0.0005f;
	return s;
}

ClientShadowHandle_t CreateFlashlight( const FlashlightState_t &s )
{
	Call( "CreateFlashlight\t" + StateArgs( s ) );
	const ClientShadowHandle_t h = Subject().CreateFlashlight( s );
	ShadowLog( "R\t%d", h );
	return h;
}

void UpdateFlashlightState( ClientShadowHandle_t h, const FlashlightState_t &s )
{
	Call( "UpdateFlashlightState\t" + std::to_string( h ) + "\t" + StateArgs( s ) );
	Subject().UpdateFlashlightState( h, s );
}

void DestroyFlashlight( ClientShadowHandle_t h )
{
	Call( "DestroyFlashlight\t" + std::to_string( h ) );
	Subject().DestroyFlashlight( h );
}

void UpdateProjectedTexture( ClientShadowHandle_t h, bool bForce )
{
	Call( "UpdateProjectedTexture\t" + std::to_string( h ) + "\t" + ( bForce ? "1" : "0" ) );
	Subject().UpdateProjectedTexture( h, bForce );
}

void DirtyHandle( ClientShadowHandle_t h, bool bForce )
{
	Call( "AddToDirtyShadowListHandle\t" + std::to_string( h ) + "\t" + ( bForce ? "1" : "0" ) );
	Subject().AddToDirtyShadowList( h, bForce );
}

void Dirty( CShadowThing *p, bool bForce )
{
	Call( "AddToDirtyShadowList\t" + std::to_string( p->m_nId ) + "\t" + ( bForce ? "1" : "0" ) );
	Subject().AddToDirtyShadowList( p, bForce );
}

void MarkTextureDirty( ClientShadowHandle_t h )
{
	Call( "MarkRenderToTextureShadowDirty\t" + std::to_string( h ) );
	Subject().MarkRenderToTextureShadowDirty( h );
}

void Receiver( int nLeaf, CShadowThing *p, ShadowReceiver_t kind )
{
	s_Receivers.push_back( { nLeaf, p->m_nId, kind } );
	ShadowLog( "RCV\t%d\t%d\t%d", nLeaf, p->m_nId, (int)kind );
}

void AddToReceiver( ClientShadowHandle_t h, CShadowThing *p, ShadowReceiver_t kind )
{
	Call( "AddShadowToReceiver\t" + std::to_string( h ) + "\t" + std::to_string( p->m_nId ) + "\t" + std::to_string( (int)kind ) );
	ProbeReceiver( h, p, kind );
	Subject().AddShadowToReceiver( h, p, kind );
}

void RemoveFromReceiver( CShadowThing *p, ShadowReceiver_t kind )
{
	Call( "RemoveAllShadowsFromReceiver\t" + std::to_string( p->m_nId ) + "\t" + std::to_string( (int)kind ) );
	Subject().RemoveAllShadowsFromReceiver( p, kind );
}

CViewSetup View( float x, float y, float z, const QAngle &angles )
{
	CViewSetup v;
	v.origin.Init( x, y, z );
	v.angles = angles;
	v.zNear = 7.0f;
	v.zFar = 10000.0f;
	v.fov = 90.0f;
	v.m_flAspectRatio = 1.0f;
	return v;
}

std::string ViewArgs( const CViewSetup &v )
{
	return Vec( v.origin ) + "\t" + Ang( v.angles ) + "\t" + Hex( v.zNear ) + "\t" + Hex( v.zFar ) + "\t" + Hex( v.fov ) + "\t" +
	       Hex( v.m_flAspectRatio );
}

// The atlas arms of one pass: for each shadow the pass asked the allocator
// about, its fragment before and after against the power it asked for.
void ProbeAtlas( const std::map< ClientShadowHandle_t, std::pair< int, int > > &before, size_t nFirstByte )
{
	CClientShadowMgr &m = s_ClientShadowMgr;
	const std::map< ClientShadowHandle_t, std::pair< int, int > > after = AtlasState();
	// Which shadows the pass drew: each draw loads the engine's matrix for it.
	std::set< int > drewEngine;
	{
		std::istringstream in( g_Log.substr( nFirstByte ) );
		std::string line, prev;
		while ( std::getline( in, line ) )
		{
			if ( line.rfind( "Q\tsm.GetInfo\t", 0 ) == 0 && prev == "E\tctx.MatrixMode\t0" )
				drewEngine.insert( atoi( line.c_str() + 13 ) );
			if ( line.rfind( "Q\t", 0 ) != 0 )
				prev = line;
		}
	}
	const int nMax = r_shadowmaxrendered.GetInt();
	int nRendered = 0;
	std::set< int > fragmentsBefore;
	for ( auto &kv : before )
		if ( kv.second.first >= 0 )
			fragmentsBefore.insert( kv.second.first );
	for ( int i = 0; i < s_VisibleShadowList.GetVisibleShadowCount(); i++ )
	{
		const VisibleShadowInfo_t &info = s_VisibleShadowList.GetVisibleShadow( i );
		const ClientShadowHandle_t h = info.m_hShadow;
		if ( nRendered >= nMax )
		{
			Probe( "d7.budget.lod", h );
			continue;
		}
		const bool bDrew = drewEngine.count( m.m_Shadows[ h ].m_ShadowHandle ) != 0;
		if ( bDrew )
			++nRendered;
		const int nDesired = DesiredPower( h, info.m_flArea );
		const auto b = before.count( h ) ? before.at( h ) : std::make_pair( -1, -1 );
		const auto a = after.count( h ) ? after.at( h ) : std::make_pair( -1, -1 );
		std::string arm;
		if ( b.first >= 0 && a.first == b.first )
		{
			if ( nDesired == b.second )
				arm = "d7.alloc.same";
			else if ( nDesired == b.second + 1 && nDesired < 8 )
				arm = "d7.alloc.keepnear";
			else
				arm = "d7.alloc.keepold";
		}
		else if ( b.first >= 0 && a.first >= 0 )
			arm = a.second > b.second ? "d7.alloc.upgrade" : "d7.alloc.other";
		else if ( a.first >= 0 )
			arm = a.second == nDesired ? "d7.alloc.fresh" : a.second < nDesired ? "d7.alloc.lower" : "d7.alloc.other";
		else
			arm = b.first >= 0 ? "d7.alloc.lost" : "d7.alloc.none";
		if ( a.first >= 0 && fragmentsBefore.count( a.first ) && !( b.first == a.first ) )
			arm += "|d7.alloc.steal";
		Probe( arm + ( bDrew ? "|d7.drew" : "" ), h );
	}
	// A texture that lost its fragment to another in this pass.
	for ( auto &kv : before )
	{
		if ( kv.second.first >= 0 && after.count( kv.first ) && after.at( kv.first ).first < 0 )
			Probe( "d7.alloc.stolenfrom", kv.first );
	}
}

void ComputeShadowTextures( const CViewSetup &v, std::vector< int > leaves )
{
	Call( "ComputeShadowTextures\t" + ViewArgs( v ) + "\t" + Leaves( (int)leaves.size(), leaves.data() ) );
	std::vector< LeafIndex_t > l( leaves.begin(), leaves.end() );
	const auto before = AtlasState();
	const size_t nFirst = g_Log.size();
	s_VisibleShadowList.m_ShadowsInView.RemoveAll();
	Subject().ComputeShadowTextures( v, (int)l.size(), l.data() );
	ProbeAtlas( before, nFirst );
}

void ComputeShadowDepthTextures( const CViewSetup &v )
{
	Call( "ComputeShadowDepthTextures\t" + ViewArgs( v ) );
	Subject().ComputeShadowDepthTextures( v );
}

void UnlockAllShadowDepthTextures()
{
	Call( "UnlockAllShadowDepthTextures" );
	Subject().UnlockAllShadowDepthTextures();
}

void RenderShadowTexture( int w, int h )
{
	Call( "RenderShadowTexture\t" + std::to_string( w ) + "\t" + std::to_string( h ) );
	Subject().RenderShadowTexture( w, h );
}

void SetShadowDirection( float x, float y, float z )
{
	const Vector dir( x, y, z );
	Call( "SetShadowDirection\t" + Vec( dir ) );
	Subject().SetShadowDirection( dir );
}

void GetShadowDirection()
{
	Call( "GetShadowDirection" );
	ShadowLog( "R\t%s", Vec( Subject().GetShadowDirection() ).c_str() );
}

void SetShadowColor( int r, int g, int b )
{
	Call( "SetShadowColor\t" + std::to_string( r ) + "\t" + std::to_string( g ) + "\t" + std::to_string( b ) );
	Subject().SetShadowColor( (unsigned char)r, (unsigned char)g, (unsigned char)b );
}

void SetShadowDistance( float f )
{
	Call( "SetShadowDistance\t" + Hex( f ) );
	Subject().SetShadowDistance( f );
}

void SetBlobbyCutoff( float f )
{
	Call( "SetShadowBlobbyCutoffArea\t" + Hex( f ) );
	Subject().SetShadowBlobbyCutoffArea( f );
}

void SetFalloffBias( ClientShadowHandle_t h, int nBias )
{
	Call( "SetFalloffBias\t" + std::to_string( h ) + "\t" + std::to_string( nBias ) );
	Subject().SetFalloffBias( h, (unsigned char)nBias );
}

void SetFlashlightLightWorld( ClientShadowHandle_t h, bool b )
{
	Call( "SetFlashlightLightWorld\t" + std::to_string( h ) + "\t" + ( b ? "1" : "0" ) );
	Subject().SetFlashlightLightWorld( h, b );
}

void SetFlashlightTarget( ClientShadowHandle_t h )
{
	// The target is always NULL: the fork deleted the target's tails, and no
	// caller sets one (§3 of the plan).
	Call( "SetFlashlightTarget\t" + std::to_string( h ) );
	Subject().SetFlashlightTarget( h, EHANDLE() );
}

void SetShadowsDisabled( bool b )
{
	Call( std::string( "SetShadowsDisabled\t" ) + ( b ? "1" : "0" ) );
	Subject().SetShadowsDisabled( b );
}

void Restore()
{
	Call( "Restore" );
	ASSERT_TRUE( s_pfnRestore != NULL ) << "the subject registered no restore function";
	s_pfnRestore( 0 );
}

void Command( void ( *pfn )( const CCommand & ), const char *pszLine )
{
	Call( std::string( "Command\t" ) + pszLine );
	CCommand args;
	args.Tokenize( pszLine );
	pfn( args );
}

// The two proxies, as the material system makes them: Init on a material
// with the vars it has, then OnBind with the client shadow handle.
void ShadowProxy( const std::vector< std::string > &vars, const std::vector< int > &binds )
{
	std::string list;
	for ( const std::string &v : vars )
		list += ( list.empty() ? "" : "," ) + v;
	Call( "ProxyInit\tShadow\t" + list );
	CProxyMaterial material( "decals/rendershadow", vars );
	CShadowProxy proxy;
	const bool bOk = proxy.Init( &material, NULL );
	ShadowLog( "R\t%d", bOk ? 1 : 0 );
	if ( !bOk )
		return;
	for ( int h : binds )
	{
		Call( "ProxyBind\tShadow\t" + std::to_string( h ) );
		proxy.OnBind( (void *)(intp)h );
	}
}

void ShadowModelProxy( const std::vector< std::string > &vars, const std::vector< int > &binds )
{
	std::string list;
	for ( const std::string &v : vars )
		list += ( list.empty() ? "" : "," ) + v;
	Call( "ProxyInit\tShadowModel\t" + list );
	CProxyMaterial material( "decals/rendermodelshadow", vars );
	CShadowModelProxy proxy;
	const bool bOk = proxy.Init( &material, NULL );
	ShadowLog( "R\t%d", bOk ? 1 : 0 );
	if ( !bOk )
		return;
	for ( int h : binds )
	{
		Call( "ProxyBind\tShadowModel\t" + std::to_string( h ) );
		proxy.OnBind( (void *)(intp)h );
	}
}

void Gone( CShadowThing *p )
{
	p->m_bGone = true;
	Describe( p );
}

void Move( CShadowThing *p, float x, float y, float z )
{
	p->m_vecOrigin.Init( x, y, z );
	Redescribe( p );
}

void Turn( CShadowThing *p, const QAngle &angles )
{
	p->m_angAngles = angles;
	Redescribe( p );
}

std::vector< int > Range( int a, int b )
{
	std::vector< int > v;
	for ( int i = a; i <= b; i++ )
		v.push_back( i );
	return v;
}

const int kShadow = SHADOW_FLAGS_SHADOW;
const int kRtt = SHADOW_FLAGS_USE_RENDER_TO_TEXTURE;
const int kAnim = SHADOW_FLAGS_ANIMATING_SOURCE;
const int kAllReceive = SHADOW_FLAGS_PROJECTED_TEXTURE_TYPE_MASK;

} // namespace

//-----------------------------------------------------------------------------
// The scenario. Run once per process (the subject's function-local statics,
// its warn-once flags, would make a second run differ), its log cached.
//-----------------------------------------------------------------------------
static std::string RunScenarioOnce()
{
	EXPECT_TRUE( ThreadInMainThread() );
	g_pCVar = &g_ShadowCvar;
	SpewOutputFunc( ShadowSpew );
	g_Log.clear();
	g_nLogLines = 0;
	s_Things.clear();
	s_Receivers.clear();
	s_LeafShadows.clear();
	ShadowLog( "# shadowmgr-stock.tsv: written by src/test/oracle/native/shadowmgr_oracle_test.cpp" );
	ShadowLog( "# from the pristine game/client/clientshadowmgr.cpp; README-shadowmgr-stock.md says how to read it." );

	// The interfaces the material system asks for by name.
	for ( const std::string &s : s_ExposedAs )
		ShadowLog( "X\t%s", s.c_str() );

	// The layouts of the engine's answers the managed side copies (its
	// ShadowsAbi.cs holds each to these): size, then each field's offset.
	{
		ShadowInfo_t info;
		const char *b = (const char *)&info;
		ShadowLog( "L\tShadowInfo_t\t%d\t%d,%d,%d,%d,%d,%d,%d", (int)sizeof( info ), (int)( (const char *)&info.m_WorldToShadow - b ),
		           (int)( (const char *)&info.m_FalloffOffset - b ), (int)( (const char *)&info.m_MaxDist - b ),
		           (int)( (const char *)&info.m_FalloffAmount - b ), (int)( (const char *)&info.m_TexOrigin - b ),
		           (int)( (const char *)&info.m_TexSize - b ), (int)( (const char *)&info.m_FalloffBias - b ) );
		cplane_t plane;
		const char *p = (const char *)&plane;
		ShadowLog( "L\tcplane_t\t%d\t%d,%d,%d,%d", (int)sizeof( plane ), (int)( (const char *)&plane.normal - p ),
		           (int)( (const char *)&plane.dist - p ), (int)( (const char *)&plane.type - p ), (int)( (const char *)&plane.signbits - p ) );
		Frustum_t frustum;
		const char *f = (const char *)&frustum;
		ShadowLog( "L\tFrustum_t\t%d\t%d,%d", (int)sizeof( frustum ), (int)( (const char *)frustum.GetPlane( 0 ) - f ),
		           (int)( (const char *)&frustum.GetAbsNormal( 0 ) - f ) );
	}

	// The engine's answers.
	SetLevel( 40, 100.0f );
	SetViewBox( -100, -1000, -500, 3000, 1000, 500 );
	SetAmbient( 0.1f, 0.2f, 0.5f );
	SetPixelsPerUnit( 4.0f );
	SetMaxRenderTarget( 2048 );
	SetFog( MATERIAL_FOG_LINEAR );
	SetHeightClip( MATERIAL_HEIGHTCLIPMODE_RENDER_ABOVE_HEIGHT );
	Frame( 1 );

	// ---- CYCLE 1: a low-end card that cannot do depth textures. Init leaves
	// render-to-texture off whatever the ConVar says, and turns the depth
	// texture ConVar off.
	SetDXLevel( 70 );
	SetSupportsDepth( false );
	SetTools( false );
	CV( r_shadowrendertotexture, "1" );
	CV( r_flashlightdepthtexture, "1" );
	Init();
	Shutdown();

	// ---- CYCLE 2: -tools, with depth textures: four of them.
	SetDXLevel( 90 );
	SetSupportsDepth( true );
	SetTools( true );
	CV( r_shadowrendertotexture, "1" );
	CV( r_flashlightdepthtexture, "1" );
	Init();
	Shutdown();
	SetTools( false );
	CV( r_shadowrendertotexture, "0" );

	// ---- CYCLE 3, the game: blobs, one depth texture.
	Init();
	LevelInit();
	GetShadowDirection();

	// THE BLOBS. Three orientations for the three bases (the shadow axis is
	// the object's z, x and y), a brush model, a renderable that overrides
	// its distance and direction, one with no model, one made transparent,
	// one whose entity goes, a sprite, and one whose render origin re-dirties
	// its shadow (as an aiment's does).
	CShadowThing *t1 = Thing( 1, mod_studio, 150, 0, 40, 10, 10, 40 );
	CShadowThing *t2 = Thing( 2, mod_studio, 250, 0, 40, 10, 12, 30 );
	Turn( t2, QAngle( 90, 0, 0 ) );
	CShadowThing *t3 = Thing( 3, mod_studio, 350, 0, 40, 8, 10, 30 );
	Turn( t3, QAngle( 0, 30, 90 ) );
	CShadowThing *t4 = Thing( 4, mod_brush, 450, 0, 20, 20, 20, 20 );
	CShadowThing *t5 = Thing( 5, mod_studio, 550, 0, 40, 10, 10, 40 );
	t5->m_bCastDistance = true;
	t5->m_flCastDistance = 120.0f;
	t5->m_bCastDirection = true;
	t5->m_vecCastDirection.Init( 0.6f, 0.0f, -0.8f );
	Describe( t5 );
	CShadowThing *t6 = Thing( 6, mod_studio, 650, 0, 40, 10, 10, 40 );
	t6->m_bHasModel = false;
	Describe( t6 );
	CShadowThing *t7 = Thing( 7, mod_studio, 750, 0, 40, 10, 10, 40 );
	CShadowThing *t8 = Thing( 8, mod_studio, 850, 0, 40, 10, 10, 40 );
	CShadowThing *t9 = Thing( 9, mod_sprite, 950, 0, 40, 10, 10, 40 );
	CShadowThing *t10 = Thing( 10, mod_studio, 1050, 0, 40, 10, 10, 40 );
	// A renderable with no entity behind its handle: CreateShadow answers no shadow.
	CShadowThing *t11 = Thing( 11, mod_studio, 1150, 0, 40, 10, 10, 40 );
	t11->m_bGone = true;
	Describe( t11 );

	// THE RECEIVERS, registered in the leaves they sit in.
	CShadowThing *r40 = Box( 40, mod_brush, 150, 0, -10, 30 );
	r40->m_nReceiveFlags = kAllReceive;
	Describe( r40 );
	Receiver( 1, r40, SHADOW_RECEIVER_BRUSH_MODEL );
	CShadowThing *r41 = Box( 41, mod_studio, 250, 0, -10, 30 );
	r41->m_nReceiveFlags = kAllReceive;
	Describe( r41 );
	Receiver( 2, r41, SHADOW_RECEIVER_STATIC_PROP );
	CShadowThing *r42 = Box( 42, mod_studio, 350, 0, -10, 30 );
	r42->m_nReceiveFlags = kAllReceive;
	Describe( r42 );
	Receiver( 3, r42, SHADOW_RECEIVER_STUDIO_MODEL );
	CShadowThing *r43 = Box( 43, mod_studio, 160, 20, -10, 5 );
	Receiver( 1, r43, SHADOW_RECEIVER_STUDIO_MODEL );
	// The caster itself, as its own leaf would hold it.
	Receiver( 1, t1, SHADOW_RECEIVER_STUDIO_MODEL );
	// A ring of small brush receivers around the brush caster and the blob,
	// above, beside and below, so the receiver's cull meets every case.
	int nRing = 300;
	for ( int ix = -2; ix <= 2; ix++ )
	{
		for ( int iz = -2; iz <= 2; iz++ )
		{
			CShadowThing *p = Box( nRing++, mod_brush, 450 + ix * 18.0f, 0, 20 + iz * 18.0f, 6 );
			p->m_nReceiveFlags = kAllReceive;
			Describe( p );
			Receiver( 4, p, SHADOW_RECEIVER_BRUSH_MODEL );
		}
	}
	for ( int ix = -2; ix <= 2; ix++ )
	{
		for ( int iy = -1; iy <= 1; iy++ )
		{
			CShadowThing *p = Box( nRing++, mod_brush, 150 + ix * 14.0f, iy * 20.0f, 30 - ix * 6.0f, 4 );
			p->m_nReceiveFlags = kAllReceive;
			Describe( p );
			Receiver( 1, p, SHADOW_RECEIVER_BRUSH_MODEL );
		}
	}

	Frame( 2 );
	ClientShadowHandle_t h1 = CreateShadow( t1, kShadow );
	ClientShadowHandle_t h2 = CreateShadow( t2, kShadow | kRtt );
	ClientShadowHandle_t h3 = CreateShadow( t3, kShadow );
	ClientShadowHandle_t h4 = CreateShadow( t4, kShadow );
	ClientShadowHandle_t h5 = CreateShadow( t5, kShadow );
	ClientShadowHandle_t h6 = CreateShadow( t6, kShadow );
	ClientShadowHandle_t h7 = CreateShadow( t7, kShadow );
	ClientShadowHandle_t h8 = CreateShadow( t8, kShadow );
	ClientShadowHandle_t h9 = CreateShadow( t9, kShadow );
	ClientShadowHandle_t h10 = CreateShadow( t10, kShadow );
	CreateShadow( t11, kShadow );
	SetFalloffBias( h7, 255 );
	PreRender();
	AdvanceFrame();

	// FRAME 3: nothing moved; shadows re-dirtied out of handle order (the
	// walk is in handle order), one forced; the transparent shadow is still
	// dirty; the aiment re-dirties while the walk runs (ignored).
	Frame( 3 );
	t10->m_bDirtyOnOrigin = true;
	Describe( t10 );
	Move( t5, 560, 0, 40 );
	Dirty( t5, false );
	Dirty( t3, false );
	Dirty( t3, false );   // already dirty
	Dirty( t1, true );
	Dirty( t10, false );
	DirtyHandle( CLIENTSHADOW_INVALID_HANDLE, false );
	PreRender();
	t10->m_bDirtyOnOrigin = false;
	Describe( t10 );
	AdvanceFrame();

	// FRAME 4: the transparent shadow made visible again; an entity gone
	// while its shadow is dirty retires it, and the walk stops there: the
	// shadows after it in handle order leave the dirty set unprojected.
	Frame( 4 );
	SetFalloffBias( h7, 0 );
	Move( t9, 960, 0, 40 );
	Dirty( t9, false );
	Gone( t8 );
	DirtyHandle( h8, true );
	Move( t4, 455, 0, 20 );
	Dirty( t4, false );
	PreRender();
	AdvanceFrame();

	// The receivers directly, as the leaf system's moves call them.
	Frame( 5 );
	AddToReceiver( h1, r40, SHADOW_RECEIVER_BRUSH_MODEL );
	AddToReceiver( h2, r41, SHADOW_RECEIVER_STATIC_PROP );
	AddToReceiver( h3, r42, SHADOW_RECEIVER_STUDIO_MODEL );
	RemoveFromReceiver( r40, SHADOW_RECEIVER_BRUSH_MODEL );
	RemoveFromReceiver( r41, SHADOW_RECEIVER_STATIC_PROP );
	RemoveFromReceiver( r42, SHADOW_RECEIVER_STUDIO_MODEL );
	RemoveFromReceiver( r43, SHADOW_RECEIVER_STUDIO_MODEL );
	r42->m_hInstance = 142;
	Describe( r42 );
	RemoveFromReceiver( r42, SHADOW_RECEIVER_STUDIO_MODEL );

	// The settings and the console commands, with and without arguments.
	SetShadowColor( 40, 50, 60 );
	SetShadowDistance( 60.0f );
	SetBlobbyCutoff( 0.01f );
	SetShadowDirection( 0.2f, 0.1f, -1.0f );
	GetShadowDirection();
	Command( r_shadowdir, "r_shadowdir" );
	Command( r_shadowdir, "r_shadowdir 0.1 0.1 -1" );
	Command( r_shadowdir, "r_shadowdir 1 2" );
	Command( r_shadowangles, "r_shadowangles" );
	Command( r_shadowangles, "r_shadowangles 80 30 0" );
	Command( r_shadowcolor, "r_shadowcolor" );
	Command( r_shadowcolor, "r_shadowcolor 64 64 64" );
	Command( r_shadowdist, "r_shadowdist" );
	Command( r_shadowdist, "r_shadowdist 50" );
	Command( r_shadowblobbycutoff, "r_shadowblobbycutoff" );
	Command( r_shadowblobbycutoff, "r_shadowblobbycutoff 0.005" );
	Call( "Command\tr_shadowbreak" );
	ShadowBreak_f();
	SetShadowsDisabled( true );
	SetShadowsDisabled( false );
	PreRender();
	AdvanceFrame();

	// Destroyed, and its handle reused.
	Frame( 6 );
	Dirty( r40, false );   // no shadow of its own
	// The dirty set keeps a handle twice (CUtlRBTree::Insert does not refuse
	// one), and the walk visits each copy.
	DirtyHandle( h1, false );
	DirtyHandle( h1, false );
	DestroyShadow( t3 );
	CShadowThing *t12 = Thing( 12, mod_studio, 1250, 0, 40, 10, 10, 40 );
	CreateShadow( t12, kShadow );
	PreRender();
	Dirty( t12, false );
	DestroyShadow( t12 );   // dirty when it goes: the renderable's dirty bit is cleared
	CShadowThing *t14 = Thing( 14, mod_studio, 1350, 0, 40, 10, 10, 40 );
	CreateShadow( t14, kShadow );
	PreRender();
	AdvanceFrame();

	(void)h4; (void)h5; (void)h6; (void)h9; (void)h10;

	// ---- RENDER TO TEXTURE. A shadow hierarchy (a parent; a child with its
	// own shadow; a child with none whose own child has one; a brush child; a
	// blob child), seven big casters (the atlas holds six fragments of 256),
	// a medium one, an animating one, a transparent one, one out of the view,
	// receivers under the parent and a ring of them around t2, all made while
	// render-to-texture is still off.
	Frame( 7 );
	CShadowThing *pP = Thing( 20, mod_studio, 1500, 0, 40, 30, 30, 60 );
	CShadowThing *pC = Thing( 21, mod_studio, 1530, 0, 40, 10, 10, 30 );
	CShadowThing *pN = Thing( 22, mod_studio, 1470, 0, 40, 10, 10, 30 );
	CShadowThing *pG = Thing( 23, mod_studio, 1460, 0, 30, 5, 5, 20 );
	CShadowThing *pB = Thing( 24, mod_brush, 1500, 30, 10, 10, 10, 10 );
	CShadowThing *pS = Thing( 25, mod_studio, 1500, -30, 40, 10, 10, 20 );
	pP->m_nFirstChild = 21;
	pC->m_nParent = 20; pC->m_nNextPeer = 22;
	pN->m_nParent = 20; pN->m_nNextPeer = 24; pN->m_nFirstChild = 23;
	pG->m_nParent = 22;
	pB->m_nParent = 20; pB->m_nNextPeer = 25;
	pS->m_nParent = 20;
	pC->m_bBonesOk = false;
	for ( CShadowThing *p : { pP, pC, pN, pG, pB, pS } )
		Describe( p );
	const ClientShadowHandle_t hP = CreateShadow( pP, kShadow | kRtt );
	const ClientShadowHandle_t hC = CreateShadow( pC, kShadow | kRtt );
	const ClientShadowHandle_t hG = CreateShadow( pG, kShadow | kRtt );
	const ClientShadowHandle_t hB = CreateShadow( pB, kShadow | kRtt );
	CreateShadow( pS, kShadow );
	std::vector< CShadowThing * > big;
	std::vector< ClientShadowHandle_t > hBig;
	for ( int i = 0; i < 7; i++ )
	{
		big.push_back( Box( 60 + i, mod_studio, 1700 + i * 100.0f, 0, 40, 40 ) );
		hBig.push_back( CreateShadow( big.back(), kShadow | kRtt ) );
	}
	// Bigger than the largest fragment: its texture is clamped to 256.
	CShadowThing *pHuge = Box( 67, mod_studio, 2300, 300, 70, 70 );
	CreateShadow( pHuge, kShadow | kRtt );
	CShadowThing *pMedium = Box( 70, mod_studio, 2400, 0, 20, 10 );
	const ClientShadowHandle_t hMedium = CreateShadow( pMedium, kShadow | kRtt );
	CShadowThing *pAnim = Box( 71, mod_studio, 2450, 100, 20, 10 );
	const ClientShadowHandle_t hAnim = CreateShadow( pAnim, kShadow | kRtt | kAnim );
	CShadowThing *pClear = Box( 72, mod_studio, 2500, 0, 20, 10 );
	const ClientShadowHandle_t hClear = CreateShadow( pClear, kShadow | kRtt );
	CShadowThing *pOut = Box( 73, mod_studio, 3500, 0, 20, 10 );
	CreateShadow( pOut, kShadow | kRtt );
	CShadowThing *r46 = Box( 46, mod_studio, 1500, 0, -10, 40 );
	r46->m_nReceiveFlags = kAllReceive;
	Describe( r46 );
	Receiver( 15, r46, SHADOW_RECEIVER_STUDIO_MODEL );
	CShadowThing *r47 = Box( 47, mod_brush, 1520, 0, -10, 40 );
	r47->m_nReceiveFlags = kAllReceive;
	Describe( r47 );
	Receiver( 15, r47, SHADOW_RECEIVER_BRUSH_MODEL );
	for ( int ix = -2; ix <= 2; ix++ )
	{
		for ( int iz = -2; iz <= 2; iz++ )
		{
			CShadowThing *p = Box( nRing++, mod_brush, 250 + ix * 14.0f, ix * 7.0f, 30 + iz * 16.0f, 5 );
			p->m_nReceiveFlags = kAllReceive;
			Describe( p );
			Receiver( 2, p, SHADOW_RECEIVER_BRUSH_MODEL );
		}
	}
	CV( r_shadowrendertotexture, "1" );
	PreRender();
	AdvanceFrame();

	const CViewSetup vMain = View( 0, 0, 100, QAngle( 0, 0, 0 ) );
	const std::vector< int > all = Range( 0, 39 );

	// FRAME 8: every shadow re-projected with the render-to-texture bases;
	// the pass draws them all (the seventh big caster overflows the 256s),
	// and a second view in the same frame finds nothing left to draw.
	Frame( 8 );
	// The child C is projected on its own first (unparented for this
	// PreRender), so its leaves hold it when the pass finds it a child again;
	// the transparent caster too, made transparent after it was projected.
	pC->m_nParent = -1;
	Describe( pC );
	PreRender();
	pC->m_nParent = 20;
	Describe( pC );
	SetFalloffBias( hClear, 255 );
	ComputeShadowTextures( vMain, all );
	ComputeShadowTextures( vMain, all );
	RenderShadowTexture( 256, 256 );
	AdvanceFrame();

	// FRAME 9: a budget of two. The dirty ones draw first; the rest fall back
	// to their blobs.
	Frame( 9 );
	CV( r_shadowmaxrendered, "2" );
	MarkTextureDirty( hP );
	MarkTextureDirty( hBig[ 0 ] );
	MarkTextureDirty( hBig[ 1 ] );
	MarkTextureDirty( hBig[ 2 ] );
	MarkTextureDirty( hG );
	MarkTextureDirty( CLIENTSHADOW_INVALID_HANDLE );
	DirtyHandle( hG, false );
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();

	// FRAME 10: still two; those already on their blobs stay there.
	Frame( 10 );
	MarkTextureDirty( hBig[ 3 ] );
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();

	// FRAME 11: the budget back; the ones that fell back return to their
	// textures, redrawn or not.
	Frame( 11 );
	CV( r_shadowmaxrendered, "32" );
	PreRender();
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();

	// FRAME 12: all seven big casters dirty again: the one on a 128 keeps it.
	Frame( 12 );
	for ( ClientShadowHandle_t h : hBig )
		MarkTextureDirty( h );
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();

	// FRAMES 13-15: smaller on the screen; a new big caster takes a 64, keeps
	// it while it grows by one size, and moves up to a 256 (taking it from
	// the caster that used it least lately) when it grows by two.
	Frame( 13 );
	SetPixelsPerUnit( 0.5f );
	CShadowThing *pGrow = Box( 74, mod_studio, 2600, 0, 40, 40 );
	CreateShadow( pGrow, kShadow | kRtt );
	PreRender();
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();
	Frame( 14 );
	SetPixelsPerUnit( 1.0f );
	// One big caster leaves the view: its 256 goes unused, to the LRU's head.
	Move( big[ 0 ], 5000, 0, 40 );
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();
	Frame( 15 );
	SetPixelsPerUnit( 2.0f );
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();

	// FRAME 16: a crowd of 257 small casters, the 16-texel fragments are 256:
	// the last one has none to take and falls back to its blob. (Their
	// questions are counted, not written.)
	Frame( 16 );
	CV( r_shadowmaxrendered, "400" );
	std::vector< CShadowThing * > crowd;
	++g_nQuiet;
	--g_nQuiet;
	for ( int i = 0; i < 257; i++ )
	{
		crowd.push_back( Box( 1000 + i, mod_studio, 2510 + ( i % 40 ) * 10.0f, -600 + ( i / 40 ) * 100.0f, 0, 1 ) );
		CreateShadow( crowd.back(), kShadow | kRtt );
	}
	PreRender();
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();
	Frame( 17 );
	for ( CShadowThing *p : crowd )
		DestroyShadow( p );
	CV( r_shadowmaxrendered, "32" );

	// The direction, with render-to-texture on, re-dirties every shadow; a
	// restore marks every texture dirty; the gates.
	SetShadowDirection( 0.1f, 0.2f, -1.0f );
	GetShadowDirection();
	SetShadowColor( 70, 60, 50 );
	Restore();
	PreRender();
	ComputeShadowTextures( vMain, all );
	AdvanceFrame();
	Frame( 18 );
	SetShadowsDisabled( true );
	ComputeShadowTextures( vMain, all );
	SetShadowsDisabled( false );
	CV( r_shadows, "0" );
	ComputeShadowTextures( vMain, all );
	CV( r_shadows, "1" );
	ComputeShadowTextures( vMain, { 39 } );
	AdvanceFrame();

	// FRAME 19: cl_drawshadowtexture boxes each re-projected RTT shadow.
	Frame( 19 );
	CV( cl_drawshadowtexture, "1" );
	Move( pMedium, 2405, 0, 20 );
	Dirty( pMedium, false );
	Move( pAnim, 2455, 100, 20 );
	Dirty( pAnim, false );
	PreRender();
	CV( cl_drawshadowtexture, "0" );
	AdvanceFrame();

	// The two proxies, bound to render-to-texture shadows.
	ShadowProxy( { "$basetexture" }, { hP, hBig[ 0 ], hMedium } );
	ShadowProxy( { "$color" }, {} );
	ShadowModelProxy( { "$basetexture", "$basetextureoffset", "$basetexturescale", "$basetexturetransform", "$falloffoffset",
	                    "$falloffdistance", "$falloffamount" },
	                  { hP, hBig[ 6 ], hAnim } );
	ShadowModelProxy( { "$basetexture", "$basetextureoffset", "$basetexturescale", "$basetexturetransform", "$falloffoffset",
	                    "$falloffdistance" },
	                  {} );
	ShadowModelProxy( { "$basetextureoffset" }, {} );

	// FRAME 20: render-to-texture off again: every shadow back to its blob.
	Frame( 20 );
	CV( r_shadowrendertotexture, "0" );
	PreRender();
	ComputeShadowTextures( vMain, all );
	RenderShadowTexture( 256, 256 );
	PreRender();
	AdvanceFrame();

	// ---- THE FLASHLIGHTS, over receivers of each kind.
	Frame( 21 );
	CShadowThing *r50 = Box( 50, mod_brush, 2000, 0, 0, 10 );
	CShadowThing *r51 = Box( 51, mod_studio, 2050, 0, 0, 10 );
	CShadowThing *r52 = Box( 52, mod_studio, 1950, 0, 0, 10 );
	CShadowThing *r53 = Box( 53, mod_studio, 2100, 900, 0, 10 );
	CShadowThing *r54 = Box( 54, mod_studio, 2010, 20, 0, 10 );
	for ( CShadowThing *p : { r50, r51, r52, r53 } )
	{
		p->m_nReceiveFlags = kAllReceive;
		Describe( p );
	}
	Receiver( 20, r50, SHADOW_RECEIVER_BRUSH_MODEL );
	Receiver( 20, r51, SHADOW_RECEIVER_STATIC_PROP );
	Receiver( 19, r52, SHADOW_RECEIVER_STUDIO_MODEL );
	Receiver( 21, r53, SHADOW_RECEIVER_STUDIO_MODEL );
	Receiver( 20, r54, SHADOW_RECEIVER_STUDIO_MODEL );
	const QAngle down( 90, 0, 0 );
	FlashlightState_t s1 = Light( 2000, 0, 100, down, true );
	const ClientShadowHandle_t f1 = CreateFlashlight( s1 );
	const ClientShadowHandle_t f2 = CreateFlashlight( Light( 2100, 0, 100, down, true ) );
	const ClientShadowHandle_t f3 = CreateFlashlight( Light( 2500, 3000, 100, down, true ) );
	const ClientShadowHandle_t f4 = CreateFlashlight( Light( 1900, 0, 100, QAngle( 80, 10, 0 ), false ) );
	PreRender();
	s1.m_vecLightOrigin.Init( 2005, 0, 100 );
	UpdateFlashlightState( f1, s1 );
	UpdateProjectedTexture( f1, true );
	CV( r_flashlightdrawfrustum, "1" );
	ComputeShadowDepthTextures( vMain );
	CV( r_flashlightdrawfrustum, "0" );
	UnlockAllShadowDepthTextures();
	AdvanceFrame();

	// FRAME 22: a flashlight that stops wanting shadows; the version2 arm.
	Frame( 22 );
	s1.m_bEnableShadows = false;
	UpdateFlashlightState( f1, s1 );
	UpdateProjectedTexture( f1, true );
	CV( r_flashlight_version2, "1" );
	UpdateProjectedTexture( f2, true );
	ComputeShadowDepthTextures( vMain );
	UnlockAllShadowDepthTextures();
	CV( r_flashlight_version2, "0" );
	AdvanceFrame();

	// FRAME 23: light-world off, then models off; the setters on a shadow
	// that is no flashlight; UpdateProjectedTexture on a shadow and on nothing.
	Frame( 23 );
	SetFlashlightLightWorld( f2, false );
	UpdateProjectedTexture( f2, true );
	SetFlashlightLightWorld( f2, true );
	CV( r_flashlightmodels, "0" );
	UpdateProjectedTexture( f2, true );
	CV( r_flashlightmodels, "1" );
	SetFlashlightLightWorld( h1, false );
	SetFlashlightTarget( h1 );
	SetFlashlightTarget( f2 );
	UpdateProjectedTexture( h1, true );
	UpdateProjectedTexture( CLIENTSHADOW_INVALID_HANDLE, true );
	AddToReceiver( f2, r52, SHADOW_RECEIVER_STUDIO_MODEL );
	AddToReceiver( f2, r50, SHADOW_RECEIVER_BRUSH_MODEL );
	AdvanceFrame();

	// FRAMES 24-26: the depth textures' resolution raised past what the card
	// allows (the actual width is written back), turned off, on, and turned
	// off by a card that stops supporting them.
	Frame( 24 );
	CV( r_flashlightdepthres, "4096" );
	PreRender();
	CV( r_flashlightdepthtexture, "0" );
	PreRender();
	CV( r_flashlightdepthtexture, "1" );
	PreRender();
	SetSupportsDepth( false );
	PreRender();
	SetSupportsDepth( true );
	CV( r_flashlightdepthtexture, "1" );
	PreRender();
	AdvanceFrame();

	// FRAME 27: more flashlights with depth textures in one view than the
	// active list holds (1024).
	Frame( 27 );
	CV( r_flashlightmodels, "0" );
	CV( r_flashlight_version2, "1" );
	std::vector< ClientShadowHandle_t > lights;
	for ( int i = 0; i < 1025; i++ )
		lights.push_back( CreateFlashlight( Light( 1000 + ( i % 32 ) * 20.0f, -300 + ( i / 32 ) * 20.0f, 100, down, true, 50.0f ) ) );
	ComputeShadowDepthTextures( vMain );
	UnlockAllShadowDepthTextures();
	for ( ClientShadowHandle_t h : lights )
		DestroyFlashlight( h );
	CV( r_flashlight_version2, "0" );
	CV( r_flashlightmodels, "1" );
	DestroyFlashlight( f1 );
	DestroyFlashlight( f2 );
	DestroyFlashlight( f3 );
	DestroyFlashlight( f4 );
	AdvanceFrame();

	// ---- THE LEVEL ENDS with shadows left and render-to-texture on; the
	// next level starts with it on (the atlas is reset), then the game ends.
	Frame( 28 );
	CV( r_shadowrendertotexture, "1" );
	PreRender();
	PreRender();
	ComputeShadowTextures( vMain, all );
	(void)hC; (void)hB;
	LevelShutdown();
	SetAmbient( 0.5f, 0.1f, 0.0f );
	LevelInit();
	CShadowThing *t13 = Thing( 13, mod_studio, 150, 0, 40, 10, 10, 40 );
	CreateShadow( t13, kShadow | kRtt );
	PreRender();
	ComputeShadowTextures( vMain, all );
	Shutdown();
	ShadowLog( "AL\t%d", s_nLibraryAsserts );
	ShadowLog( "END" );
	SpewOutputFunc( NULL );
	return g_Log;
}

static std::string RunScenario()
{
	static std::string s_Run = RunScenarioOnce();
	return s_Run;
}

static std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

static std::string GoldenPath()
{
	return RepoFile( "src/test/client/Fixtures/shadowmgr-stock.tsv" );
}

static std::string MarkerPath()
{
	return RepoFile( "src/test/oracle/native/shadowmgr_golden.regen" );
}

static std::vector< std::string > Lines( const std::string &s )
{
	std::vector< std::string > v;
	std::istringstream in( s );
	std::string line;
	while ( std::getline( in, line ) )
		v.push_back( line );
	return v;
}

static std::vector< std::string > Fields( const std::string &line )
{
	std::vector< std::string > v;
	size_t at = 0;
	while ( true )
	{
		const size_t tab = line.find( '\t', at );
		v.push_back( line.substr( at, tab == std::string::npos ? std::string::npos : tab - at ) );
		if ( tab == std::string::npos )
			break;
		at = tab + 1;
	}
	return v;
}

static std::vector< std::string > Split( const std::string &s, char c )
{
	std::vector< std::string > v;
	size_t at = 0;
	while ( true )
	{
		const size_t i = s.find( c, at );
		v.push_back( s.substr( at, i == std::string::npos ? std::string::npos : i - at ) );
		if ( i == std::string::npos )
			break;
		at = i + 1;
	}
	return v;
}

TEST( ShadowMgrOracle, StockProducesTheGolden )
{
	SS_SKIP_ON_CI( "a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit" );
	const std::string run = RunScenario();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	std::ifstream in( GoldenPath() );
	ASSERT_TRUE( in.good() ) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();

	ASSERT_GT( golden.size(), 1000000u ) << "the golden is empty or truncated";

	const std::vector< std::string > a = Lines( run ), b = Lines( golden );
	for ( size_t n = 0; n < a.size() || n < b.size(); n++ )
	{
		ASSERT_TRUE( n < a.size() && n < b.size() && a[ n ] == b[ n ] )
			<< "line " << n + 1 << ": stock now writes\n  " << ( n < a.size() ? a[ n ].substr( 0, 300 ) : "<end>" )
			<< "\nand the golden has\n  " << ( n < b.size() ? b[ n ].substr( 0, 300 ) : "<end>" )
			<< "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

//-----------------------------------------------------------------------------
// THE SCENARIO REACHES EVERY ARM of the eight decision families (the plan's
// D1-D8), each a non-zero number of times, and each tied to the row stock
// wrote because of it: an arm is named either from the rows themselves (a
// call's effect, or the question whose answer chose the branch) or from a
// probe of the subject's own state taken just before it decided (the
// receivers' cull, the atlas, the dirty walk), and then the rows stock wrote
// next are held to what the arm says. The arms stock cannot reach are listed
// with the reason (UNREACHABLE below) and are not counted.
//-----------------------------------------------------------------------------
namespace
{

struct RunRow
{
	std::vector< std::string > f;
	int call;   // the C row this row follows (-1 before the first)
};

struct RunCall
{
	std::vector< std::string > f;   // the C row's fields
	size_t first, last;              // its rows: (first, last]
	int frame;
	std::map< std::string, std::string > cv;   // the ConVars as the scenario had set them
};

struct RunModel
{
	std::vector< RunRow > rows;
	std::vector< RunCall > calls;
	std::map< int, int > flagsOfShadow;      // client handle -> CreateShadow / CreateFlashlight flags (from leaf.AddShadow)
	std::map< int, int > engineOfShadow;     // client handle -> engine handle (from CreateShadowEx)
	std::map< int, int > thingOfShadow;      // client handle -> thing id (-1 for a flashlight)
	std::set< int > flashlights;             // client handles
};

RunModel Model( const std::string &run )
{
	RunModel m;
	std::map< std::string, std::string > cv;
	int frame = 0;
	for ( const std::string &line : Lines( run ) )
	{
		RunRow r;
		r.f = Fields( line );
		if ( r.f[ 0 ] == "CV" )
			cv[ r.f[ 1 ] ] = r.f[ 2 ];
		if ( r.f[ 0 ] == "F" )
			frame = atoi( r.f[ 1 ].c_str() );
		if ( r.f[ 0 ] == "C" )
		{
			if ( !m.calls.empty() )
				m.calls.back().last = m.rows.size() - 1;
			RunCall c;
			c.f = r.f;
			c.first = m.rows.size();
			c.last = m.rows.size();
			c.frame = frame;
			c.cv = cv;
			m.calls.push_back( c );
		}
		r.call = (int)m.calls.size() - 1;
		m.rows.push_back( r );
	}
	if ( !m.calls.empty() )
		m.calls.back().last = m.rows.size() - 1;
	// The shadows: each create call's leaf.AddShadow (flags), CreateShadowEx
	// (the engine handle) and answer.
	for ( const RunCall &c : m.calls )
	{
		const bool bShadow = c.f[ 1 ] == "CreateShadow", bLight = c.f[ 1 ] == "CreateFlashlight";
		if ( !bShadow && !bLight )
			continue;
		int flags = -1, eh = -1, h = -1;
		for ( size_t i = c.first + 1; i <= c.last && i < m.rows.size(); i++ )
		{
			const std::vector< std::string > &f = m.rows[ i ].f;
			if ( f[ 0 ] == "E" && f[ 1 ] == "leaf.AddShadow" && flags < 0 )
				flags = atoi( f[ 3 ].c_str() );
			if ( f[ 0 ] == "E" && f[ 1 ] == "sm.CreateShadowEx" && eh < 0 )
				eh = atoi( f[ 6 ].c_str() + 2 );
			if ( f[ 0 ] == "R" )
				h = atoi( f[ 1 ].c_str() );
		}
		if ( h < 0 || h == 0xFFFF )
			continue;
		m.flagsOfShadow[ h ] = flags;
		m.engineOfShadow[ h ] = eh;
		m.thingOfShadow[ h ] = bShadow ? atoi( c.f[ 2 ].c_str() ) : -1;
		if ( bLight )
			m.flashlights.insert( h );
		else
			m.flashlights.erase( h );
	}
	return m;
}

bool Is( const RunRow &r, const char *pszKind, const char *pszWhat = NULL )
{
	return r.f[ 0 ] == pszKind && ( !pszWhat || ( r.f.size() > 1 && r.f[ 1 ] == pszWhat ) );
}

bool CallIs( const RunModel &m, int call, const char *pszName )
{
	return call >= 0 && m.calls[ call ].f[ 1 ] == pszName;
}

// The rows of a call, each E row's (kind, what).
std::vector< const RunRow * > RowsOf( const RunModel &m, const RunCall &c, const char *pszKind = NULL )
{
	std::vector< const RunRow * > v;
	for ( size_t i = c.first + 1; i <= c.last && i < m.rows.size(); i++ )
	{
		if ( !pszKind || m.rows[ i ].f[ 0 ] == pszKind )
			v.push_back( &m.rows[ i ] );
	}
	return v;
}

// A call that wrote nothing of its own: no effect, question, message or answer.
bool Silent( const RunModel &m, const RunCall &c )
{
	for ( const RunRow *r : RowsOf( m, c ) )
	{
		const std::string &k = r->f[ 0 ];
		if ( k == "E" || k == "Q" || k == "M" || k == "R" || k == "A" )
			return false;
	}
	return true;
}

bool CallHas( const RunModel &m, const RunCall &c, const char *pszKind, const char *pszWhat, const char *pszField2 = NULL )
{
	for ( const RunRow *r : RowsOf( m, c, pszKind ) )
	{
		if ( r->f.size() > 1 && r->f[ 1 ] == pszWhat && ( !pszField2 || ( r->f.size() > 2 && r->f[ 2 ] == pszField2 ) ) )
			return true;
	}
	return false;
}

const std::string &CV( const RunCall &c, const char *pszName )
{
	static const std::string s_Empty;
	auto it = c.cv.find( pszName );
	return it == c.cv.end() ? s_Empty : it->second;
}

} // namespace

TEST( ShadowMgrOracle, TheScenarioReachesEveryArm )
{
	const std::string run = RunScenario();
	const RunModel m = Model( run );
	ASSERT_GT( m.calls.size(), 1000u );

	std::map< std::string, int > arms;
	std::vector< std::string > failures;
	auto Count = [ & ]( const std::string &arm, int n = 1 ) { arms[ arm ] += n; };
	auto Fail = [ & ]( const std::string &why ) { if ( failures.size() < 40 ) failures.push_back( why ); };

	// ---- D1: which shadows exist, and their kind.
	for ( const RunCall &c : m.calls )
	{
		const std::string &name = c.f[ 1 ];
		if ( name == "CreateShadow" )
		{
			for ( const RunRow *r : RowsOf( m, c, "E" ) )
			{
				if ( r->f[ 1 ] == "sm.CreateShadowEx" )
				{
					if ( r->f[ 2 ] == "decals/simpleshadow" && r->f[ 4 ] == "65535" && r->f[ 5 ] == "1" )
						Count( "d1.create.blob" );
					if ( r->f[ 2 ] == "decals/rendershadow" && r->f[ 3 ] == "decals/rendermodelshadow" && r->f[ 4 ] != "65535" )
						Count( "d1.create.rtt" );
				}
				if ( r->f[ 1 ] == "leaf.AddShadow" && ( atoi( r->f[ 3 ].c_str() ) & ( CLIENT_SHADOW_FLAGS_LAST_FLAG << 2 ) ) )
					Count( "d1.create.brushflag" );
			}
			for ( const RunRow *r : RowsOf( m, c, "R" ) )
			{
				if ( r->f[ 1 ] == "65535" )
				{
					// The renderable did not resolve: no engine shadow was made.
					if ( CallHas( m, c, "E", "sm.CreateShadowEx" ) )
						Fail( "a CreateShadow that answered no shadow made one" );
					Count( "d1.create.norenderable" );
				}
			}
		}
		if ( name == "CreateFlashlight" )
		{
			for ( const RunRow *r : RowsOf( m, c, "E" ) )
			{
				if ( r->f[ 1 ] == "sm.CreateShadowEx" && r->f[ 5 ] == "2" )
					Count( r->f[ 4 ] != "65535" ? "d1.flashlight.depth" : "d1.flashlight.nodepth" );
			}
		}
		if ( name == "DestroyShadow" && CallHas( m, c, "E", "sm.DestroyShadow" ) && CallHas( m, c, "E", "leaf.RemoveShadow" ) )
			Count( "d1.destroy" );
		if ( name == "DestroyFlashlight" && CallHas( m, c, "E", "sm.DestroyShadow" ) )
			Count( "d1.destroyflashlight" );
		if ( name == "LevelShutdownPostEntity" && CallHas( m, c, "E", "sm.DestroyShadow" ) )
			Count( "d1.levelshutdown.destroys" );
		if ( name == "PreRender" && CallHas( m, c, "E", "sm.DestroyShadow" ) )
			Count( "d1.retire" );
		// A render-to-texture shadow projected as a blob while the mode is off
		// (GetActualShadowCastType's collapse): no clip planes follow it.
		if ( name == "PreRender" && CV( c, "r_shadowrendertotexture" ) != "1" )
		{
			const std::vector< const RunRow * > e = RowsOf( m, c, "E" );
			for ( size_t i = 0; i < e.size(); i++ )
			{
				if ( e[ i ]->f[ 1 ] != "sm.ProjectShadow" )
					continue;
				const int eh = atoi( e[ i ]->f[ 2 ].c_str() );
				for ( auto &kv : m.engineOfShadow )
				{
					if ( kv.second == eh && ( m.flagsOfShadow.at( kv.first ) & SHADOW_FLAGS_USE_RENDER_TO_TEXTURE ) &&
					     !( i + 2 < e.size() && e[ i + 2 ]->f[ 1 ] == "sm.ClearExtraClipPlanes" ) )
						Count( "d1.type.rttcollapsed" );
				}
			}
		}
	}

	// ---- D2: the modes.
	for ( const RunCall &c : m.calls )
	{
		const std::string &name = c.f[ 1 ];
		if ( name == "Init" )
		{
			bool bLowEnd = false;
			for ( const RunRow *r : RowsOf( m, c, "Q" ) )
				if ( r->f[ 1 ] == "hw.GetDXSupportLevel" && atoi( r->f[ 2 ].c_str() ) < 80 )
					bLowEnd = true;
			const bool bRtt = CallHas( m, c, "E", "mat.Init", "decals/rendershadow" );
			if ( bLowEnd && CV( c, "r_shadowrendertotexture" ) == "1" && !bRtt )
				Count( "d2.init.lowend" );
			if ( !bLowEnd && bRtt )
				Count( "d2.init.rtt" );
			if ( CallHas( m, c, "E", "cvar.Set", "r_flashlightdepthtexture" ) )
				Count( "d2.init.depthunsupported" );
			if ( CallHas( m, c, "E", "tex.InitRenderTarget", "_rt_ShadowDepthTexture_0" ) )
				Count( "d2.init.depth" );
			if ( CallHas( m, c, "E", "tex.InitRenderTarget", "_rt_ShadowDepthTexture_3" ) )
				Count( "d8.alloc.tools4" );
			if ( CallHas( m, c, "E", "mat.AddRestoreFunc" ) )
				Count( "d2.init.restorefunc" );
		}
		if ( name == "PreRender" )
		{
			const bool bDown = CallHas( m, c, "E", "tex.Shutdown", "_rt_ShadowDepthTexture_0" );
			const bool bUp = CallHas( m, c, "E", "tex.InitRenderTarget", "_rt_ShadowDepthTexture_0" );
			if ( CallHas( m, c, "E", "cvar.Set", "r_flashlightdepthtexture" ) )
				Count( "d2.prerender.depthunsupported" );
			else if ( bDown && bUp )
				Count( "d2.prerender.depthresize" );
			else if ( bDown )
				Count( "d2.prerender.depthoff" );
			else if ( bUp )
				Count( "d2.prerender.depthon" );
			if ( CallHas( m, c, "E", "mat.Init", "decals/rendershadow" ) )
			{
				Count( "d2.prerender.rtton" );
				if ( !CallHas( m, c, "E", "sm.ProjectShadow" ) )
					Count( "d2.prerender.toggle.returns" );
				if ( CallHas( m, c, "E", "sm.SetShadowMaterial", NULL ) )
					Count( "d2.rtton.convertsshadows" );
			}
			if ( CallHas( m, c, "E", "mat.Shutdown", "decals/rendershadow" ) )
			{
				Count( "d2.prerender.rttoff" );
				if ( CallHas( m, c, "E", "mat.UncacheUnusedMaterials" ) )
					Count( "d2.rttoff.uncache" );
			}
		}
		if ( name == "LevelInitPreEntity" )
		{
			for ( const RunRow *r : RowsOf( m, c, "E" ) )
			{
				if ( r->f[ 1 ] != "mat.ColorModulate" || r->f[ 2 ] != "decals/simpleshadow" )
					continue;
				for ( int k = 3; k <= 5; k++ )
					Count( r->f[ k ] == "3f800000" ? "d2.levelinit.clamped" : "d2.levelinit.scaled" );
			}
			if ( CallHas( m, c, "E", "mat.ColorModulate", "decals/rendershadow" ) )
				Count( "d2.levelinit.rttactive" );
		}
		if ( name == "SetShadowColor" )
			Count( CallHas( m, c, "E", "mat.ColorModulate", "decals/rendershadow" ) ? "d2.color.rtt" : "d2.color.blob" );
		if ( name == "Restore" && CallHas( m, c, "E", "mat.ColorModulate" ) )
			Count( "d2.restore" );
		if ( name == "SetShadowDirection" && CallHas( m, c, "E", "r.MarkShadowDirty" ) )
			Count( "d2.direction.rtt.updateall" );
		if ( name == "SetShadowDirection" && !CallHas( m, c, "E", "r.MarkShadowDirty" ) )
			Count( "d2.direction.blob.nothing" );
		if ( name == "GetShadowDirection" )
		{
			for ( const RunRow *r : RowsOf( m, c, "R" ) )
				Count( r->f[ 1 ] == "00000000,00000000,bf800000" ? "d2.direction.blobdown" : "d2.direction.rttset" );
		}
		if ( name == "SetShadowDistance" && CallHas( m, c, "E", "r.MarkShadowDirty" ) )
			Count( "d2.distance.updateall" );
		if ( name == "SetShadowsDisabled" && CallHas( m, c, "E", "cvar.Set", "r_shadows_gamecontrol" ) )
			Count( "d2.gamecontrol" );
		if ( name == "SetFalloffBias" && CallHas( m, c, "E", "sm.SetFalloffBias" ) )
			Count( "d2.falloffbias" );
		if ( name == "Command" )
		{
			const std::vector< std::string > words = Split( c.f[ 2 ], ' ' );
			const bool bPrinted = !RowsOf( m, c, "M" ).empty();
			if ( words.size() == 1 && bPrinted )
				Count( "d2.cmd." + words[ 0 ] + ".print" );
			if ( words.size() > 1 && !bPrinted )
				Count( "d2.cmd." + words[ 0 ] + ".set" );
		}
		if ( name == "Shutdown" && CallHas( m, c, "E", "mat.RemoveRestoreFunc" ) )
			Count( "d2.shutdown" );
	}

	// ---- D3: scheduling.
	for ( size_t ci = 0; ci < m.calls.size(); ci++ )
	{
		const RunCall &c = m.calls[ ci ];
		const std::string &name = c.f[ 1 ];
		const std::vector< const RunRow * > all = RowsOf( m, c );
		if ( name == "AddToDirtyShadowListHandle" && atoi( c.f[ 2 ].c_str() ) == 0xFFFF && Silent( m, c ) )
			Count( "d3.dirty.invalidhandle" );
		if ( name == "AddToDirtyShadowList" )
		{
			bool bAlready = false, bNoHandle = false, bMarked = false;
			for ( const RunRow *r : all )
			{
				if ( Is( *r, "Q", "r.dirty" ) && r->f[ 3 ] == "1" )
					bAlready = true;
				if ( Is( *r, "Q", "r.shadowhandle" ) && r->f[ 3 ] == "65535" && r->f[ 2 ] == "r" + c.f[ 2 ] )
					bNoHandle = true;
				if ( Is( *r, "E", "r.MarkShadowDirty" ) && r->f[ 3 ] == "1" )
					bMarked = true;
			}
			if ( bAlready && !bMarked )
				Count( "d3.dirty.already" );
			if ( bNoHandle && !bMarked )
				Count( "d3.dirty.noshadowhandle" );
			if ( bMarked )
				Count( c.f[ 3 ] == "1" ? "d3.dirty.marked.force" : "d3.dirty.marked" );
		}
		// A child's shadow dirtied marks the parent it renders into.
		if ( name == "AddToDirtyShadowListHandle" )
		{
			for ( const RunRow *r : all )
				if ( Is( *r, "E", "r.MarkShadowDirty" ) && r->f[ 3 ] == "1" )
					Count( "d3.dirty.parentpropagates" );
		}
		if ( name == "MarkRenderToTextureShadowDirty" )
		{
			if ( atoi( c.f[ 2 ].c_str() ) == 0xFFFF && Silent( m, c ) )
				Count( "d3.texturedirty.invalid" );
			for ( size_t i = 0; i + 1 < all.size(); i++ )
				if ( Is( *all[ i ], "Q", "r.parent" ) && all[ i ]->f[ 3 ] != "-" && Is( *all[ i + 1 ], "Q", "r.shadowhandle" ) )
					Count( "d3.texturedirty.parent" );
		}
		if ( name == "PreRender" )
		{
			// Each shadow's update: from its EnableShadow to the next one.
			for ( size_t i = 0; i < all.size(); i++ )
			{
				const RunRow &r = *all[ i ];
				if ( Is( r, "E", "r.redirty" ) && ( i + 1 >= all.size() || !Is( *all[ i + 1 ], "Q", "r.dirty" ) ) )
					Count( "d3.dirty.ignoredwhileupdating" );
				if ( Is( r, "Q", "sm.GetInfo" ) && Split( r.f[ 3 ], ',' ).size() == 16 && r.f.size() > 9 && r.f[ 9 ] == "255" )
				{
					if ( i + 1 < all.size() && Is( *all[ i + 1 ], "E", "sm.EnableShadow" ) && all[ i + 1 ]->f[ 3 ] == "0" )
						Count( "d3.transparent.disabled" );
				}
				if ( Is( r, "E", "sm.EnableShadow" ) && r.f[ 3 ] == "0" && i + 1 < all.size() && Is( *all[ i + 1 ], "E", "r.MarkShadowDirty" ) )
					Count( "d3.child.disabled" );
				if ( Is( r, "E", "sm.EnableShadow" ) && r.f[ 3 ] == "1" )
				{
					// moved or not: the next effect is the fog push (a projection) or the clean mark
					for ( size_t j = i + 1; j < all.size(); j++ )
					{
						if ( all[ j ]->f[ 0 ] != "E" )
							continue;
						if ( all[ j ]->f[ 1 ] == "ctx.FogMode" )
						{
							bool bProjected = false;
							for ( size_t k = j + 1; k < all.size() && !Is( *all[ k ], "E", "ctx.FogMode" ); k++ )
								bProjected = bProjected || Is( *all[ k ], "E", "sm.ProjectShadow" ) || Is( *all[ k ], "E", "sm.ProjectFlashlight" ) ||
								             Is( *all[ k ], "E", "sm.EnableShadow" );
							Count( bProjected ? "d3.update.moved" : "d3.update.unknownmodeltype" );
						}
						else if ( all[ j ]->f[ 1 ] == "r.MarkShadowDirty" )
							Count( "d3.update.notmoved" );
						break;
					}
				}
				if ( Is( r, "Q", "r.model" ) && r.f[ 3 ] == "-" && i + 1 < all.size() && Is( *all[ i + 1 ], "E", "r.MarkShadowDirty" ) )
					Count( "d3.update.nomodel" );
				if ( Is( r, "Q", "r.parent" ) && r.f[ 3 ] != "-" && i + 2 < all.size() && Is( *all[ i + 1 ], "Q", "r.shadowhandle" ) &&
				     all[ i + 1 ]->f[ 3 ] == "65535" )
					Count( CV( c, "r_shadowrendertotexture" ) == "1" ? "d3.useparent.recursesnone" : "d3.willparentblob.recursesnone" );
			}
			// The walk: the probe's in-order set against the order stock updated in.
			for ( const Probe_t &p : s_Probes )
			{
				if ( p.m_nLine != c.first + 1 || p.m_strArm.rfind( "d3.walk:", 0 ) != 0 )
					continue;
				const std::string list = p.m_strArm.substr( 8 );
				const std::vector< std::string > order = list.empty() ? std::vector< std::string >() : Split( list, ',' );
				// The handles dirtied by calls since the last PreRender, in the order they were dirtied.
				std::vector< int > dirtied;
				for ( size_t k = ci; k-- > 0 && m.calls[ k ].f[ 1 ] != "PreRender"; )
				{
					if ( m.calls[ k ].f[ 1 ] == "AddToDirtyShadowList" )
						dirtied.insert( dirtied.begin(), atoi( m.calls[ k ].f[ 2 ].c_str() ) );
				}
				if ( dirtied.size() > 1 && !std::is_sorted( dirtied.begin(), dirtied.end() ) && order.size() > 1 )
					Count( "d3.walk.handleorder" );
				if ( CallHas( m, c, "E", "sm.DestroyShadow" ) )
				{
					// Retired mid-walk: the shadows after it in handle order were not updated.
					int nUpdated = 0;
					for ( const RunRow *r : all )
						if ( Is( *r, "E", "sm.EnableShadow" ) )
							++nUpdated;
					if ( nUpdated + 1 < (int)order.size() )
						Count( "d3.walk.stopsatretire" );
				}
			}
		}
		if ( name == "UpdateProjectedTexture" )
		{
			if ( atoi( c.f[ 2 ].c_str() ) == 0xFFFF && Silent( m, c ) )
				Count( "d3.updateprojected.invalid" );
			if ( !RowsOf( m, c, "M" ).empty() )
				Count( "d3.updateprojected.notflashlight" );
			if ( CallHas( m, c, "E", "sm.ProjectFlashlight" ) )
				Count( "d3.updateprojected.flashlight" );
		}
		if ( name == "DestroyShadow" && CallHas( m, c, "E", "r.MarkShadowDirty", NULL ) )
			Count( "d3.removefromdirty.clearsbit" );
	}
	// The transparent shadow stays dirty: disabled in consecutive PreRenders without being dirtied again.
	{
		int nRun = 0, nBest = 0;
		for ( const RunCall &c : m.calls )
		{
			if ( c.f[ 1 ] != "PreRender" )
				continue;
			bool bTransparent = false;
			const std::vector< const RunRow * > all = RowsOf( m, c );
			for ( size_t i = 0; i + 1 < all.size(); i++ )
				if ( Is( *all[ i ], "Q", "sm.GetInfo" ) && all[ i ]->f.size() > 9 && all[ i ]->f[ 9 ] == "255" && Is( *all[ i + 1 ], "E", "sm.EnableShadow" ) )
					bTransparent = true;
			nRun = bTransparent ? nRun + 1 : 0;
			nBest = std::max( nBest, nRun );
		}
		if ( nBest >= 2 )
			Count( "d3.transparent.keptdirty" );
	}

	// ---- D4: the projection math, from each projection's inputs.
	for ( const RunCall &c : m.calls )
	{
		const std::vector< const RunRow * > all = RowsOf( m, c );
		for ( size_t i = 0; i < all.size(); i++ )
		{
			const RunRow &r = *all[ i ];
			if ( Is( r, "Q", "r.castdist" ) && r.f[ 4 ] != r.f[ 5 ] )
				Count( "d4.distance.override" );
			if ( Is( r, "Q", "r.castdir" ) && r.f[ 4 ] != r.f[ 5 ] )
				Count( "d4.direction.override" );
			if ( !Is( r, "E", "sm.ProjectShadow" ) )
				continue;
			const bool bRtt = i + 1 < all.size() && [ & ] {
				for ( size_t j = i + 1; j < all.size(); j++ )
				{
					if ( all[ j ]->f[ 0 ] != "E" )
						continue;
					return all[ j ]->f[ 1 ] == "sm.ClearExtraClipPlanes";
				}
				return false;
			}();
			if ( !bRtt )
			{
				Count( "d4.ortho" );
			}
			else
			{
				Count( "d4.rtt" );
				// Clip planes: each axis's sign picks the corner's side.
				const std::vector< std::string > dir = Split( r.f[ 4 ], ',' );
				bool bPos = false, bNeg = false;
				for ( const std::string &d : dir )
				{
					const unsigned int u = (unsigned int)strtoul( d.c_str(), NULL, 16 );
					( u & 0x80000000u ) && u != 0x80000000u ? bNeg = true : bPos = true;
				}
				if ( bPos && bNeg )
					Count( "d4.rtt.clipplanes.bothsides" );
			}
		}
		// The ortho basis: which object axis the shadow runs along, from the
		// caster's angles in its last T row and the direction it was cast in.
	}
	for ( const RunCall &c : m.calls )
	{
		if ( c.f[ 1 ] != "PreRender" )
			continue;
		const std::vector< const RunRow * > all = RowsOf( m, c );
		for ( size_t i = 0; i < all.size(); i++ )
		{
			const RunRow &r = *all[ i ];
			if ( !Is( r, "Q", "r.firstchild" ) )
				continue;
			// ComputeHierarchicalBounds asked a parent for its children.
			bool bTransformed = false;
			for ( size_t j = i + 1; j < all.size() && !Is( *all[ j ], "E", "sm.ProjectShadow" ); j++ )
				bTransformed = bTransformed || Is( *all[ j ], "Q", "r.transform" );
			if ( r.f[ 3 ] != "-" )
				Count( bTransformed ? "d4.bounds.childrenadded" : "d4.bounds.blobparent.norecursion" );
		}
	}
	{
		// SortAbsVectorComponents' three cases: the casters t1 (axis z), t2
		// (pitched 90: axis x) and t3 (rolled 90: axis y) each projected as a
		// blob, named by the caster whose angles the projection asked for.
		const char *ppszAxis[ 4 ] = { NULL, "d4.ortho.axis2", "d4.ortho.axis0", "d4.ortho.axis1" };
		for ( const RunCall &c : m.calls )
		{
			if ( c.f[ 1 ] != "PreRender" || CV( c, "r_shadowrendertotexture" ) == "1" )
				continue;
			std::string lastAngles;
			for ( const RunRow *r : RowsOf( m, c ) )
			{
				if ( Is( *r, "Q", "r.angles" ) )
					lastAngles = r->f[ 2 ];
				if ( Is( *r, "E", "sm.ProjectShadow" ) )
				{
					for ( int t = 1; t <= 3; t++ )
						if ( lastAngles == "r" + std::to_string( t ) )
							Count( ppszAxis[ t ] );
				}
			}
		}
	}

	// ---- D5: the flashlight.
	for ( const RunCall &c : m.calls )
	{
		const std::string &name = c.f[ 1 ];
		if ( name != "UpdateProjectedTexture" && name != "CreateFlashlight" )
			continue;
		const std::vector< const RunRow * > e = RowsOf( m, c, "E" );
		for ( size_t i = 0; i < e.size(); i++ )
		{
			if ( e[ i ]->f[ 1 ] == "sm.ProjectFlashlight" )
			{
				const bool bNoLeaves = e[ i ]->f[ 4 ] == "0:";
				if ( CV( c, "r_flashlight_version2" ) == "1" && bNoLeaves )
					Count( "d5.version2" );
				else
					Count( "d5.lightworld" );
			}
			if ( e[ i ]->f[ 1 ] == "sm.EnableShadow" && e[ i ]->f[ 3 ] == "0" && i + 1 < e.size() && e[ i + 1 ]->f[ 1 ] == "sm.EnableShadow" &&
			     e[ i + 1 ]->f[ 3 ] == "1" )
				Count( "d5.lightworld.off" );
			if ( e[ i ]->f[ 1 ] == "leaf.ProjectFlashlight" )
				Count( "d5.models" );
		}
		if ( CV( c, "r_flashlightmodels" ) == "0" && CallHas( m, c, "E", "sm.ProjectFlashlight" ) && !CallHas( m, c, "E", "leaf.ProjectFlashlight" ) &&
		     CV( c, "r_flashlight_version2" ) != "1" )
			Count( "d5.models.off" );
	}
	for ( const RunCall &c : m.calls )
	{
		const std::string &name = c.f[ 1 ];
		if ( ( name == "SetFlashlightLightWorld" || name == "SetFlashlightTarget" ) && RowsOf( m, c, "E" ).empty() )
			Count( m.flashlights.count( atoi( c.f[ 2 ].c_str() ) ) ? "d5.setter.flashlight" : "d5.setter.notflashlight" );
		if ( name == "UpdateFlashlightState" && CallHas( m, c, "E", "sm.UpdateFlashlightState" ) )
			Count( "d5.state" );
	}

	// ---- D6: the receivers, from the probes, each held to what stock did next.
	for ( size_t pi = 0; pi < s_Probes.size(); pi++ )
	{
		const Probe_t &p = s_Probes[ pi ];
		if ( p.m_strArm.rfind( "d6.", 0 ) != 0 )
			continue;
		for ( const std::string &arm : Split( p.m_strArm, '|' ) )
			Count( arm );
		// What stock did: the effects after the probe, up to the next offer, call or end.
		std::vector< std::string > effects;
		bool bAskedEntity = false;
		for ( size_t i = p.m_nLine; i < m.rows.size(); i++ )
		{
			const RunRow &r = m.rows[ i ];
			if ( i > p.m_nLine && ( Is( r, "E", "leaf.receiver" ) || r.f[ 0 ] == "C" || Is( r, "E", "leaf.ProjectShadow" ) ||
			                        Is( r, "E", "leaf.ProjectFlashlight" ) || Is( r, "E", "ctx.FogMode" ) || Is( r, "E", "r.MarkShadowDirty" ) ||
			                        Is( r, "E", "sm.EnableShadow" ) ) )
				break;
			if ( r.f[ 0 ] == "E" && !Is( r, "E", "leaf.receiver" ) )
				effects.push_back( r.f[ 1 ] );
			if ( Is( r, "Q", "r.baseentity" ) )
				bAskedEntity = true;
		}
		const std::vector< std::string > parts = Split( p.m_strArm, '|' );
		const std::string &last = parts.back();
		std::vector< std::string > want;
		bool bWantEntity = false;
		if ( last == "d6.kept.brush.flash" )
			want = { "sm.AddShadowToBrushModel", "sm.AddFlashlightRenderable" };
		else if ( last == "d6.kept.brush.rtt" || last == "d6.kept.brush.simple" )
			want = { "sm.AddShadowToBrushModel" };
		else if ( last == "d6.kept.static.flash" )
			want = { "sp.AddShadowToStaticProp", "sm.AddFlashlightRenderable" };
		else if ( last == "d6.kept.static.rtt" )
			bWantEntity = true;   // the NPC/player test asks for an entity, and is told none
		else if ( last == "d6.kept.studio.flash" )
			want = { "r.CreateModelInstance", "sm.AddShadowToModel", "sm.AddFlashlightRenderable" };
		if ( effects != want || bAskedEntity != bWantEntity )
			Fail( "after the probe at row " + std::to_string( p.m_nLine ) + " (" + p.m_strArm + ") stock did not do what the arm says" );
	}
	for ( const RunCall &c : m.calls )
	{
		if ( c.f[ 1 ] != "RemoveAllShadowsFromReceiver" )
			continue;
		const std::vector< const RunRow * > e = RowsOf( m, c, "E" );
		bool bReceives = false, bInvalidInstance = false;
		for ( const RunRow *r : RowsOf( m, c, "Q" ) )
		{
			if ( r->f[ 1 ] == "r.receives" && r->f[ 4 ] == "1" )
				bReceives = true;
			if ( r->f[ 1 ] == "r.instance" && r->f[ 3 ] == "65535" )
				bInvalidInstance = true;
		}
		if ( !bReceives && e.empty() )
			Count( "d6.removeall.notreceiving" );
		else if ( !e.empty() )
			Count( "d6.removeall." + e[ 0 ]->f[ 1 ] );
		else if ( bInvalidInstance )
			Count( "d6.removeall.studio.noinstance" );
	}

	// ---- D7: the render-to-texture pass.
	for ( const RunCall &c : m.calls )
	{
		if ( c.f[ 1 ] != "ComputeShadowTextures" )
			continue;
		const std::vector< const RunRow * > all = RowsOf( m, c );
		if ( Silent( m, c ) )
		{
			if ( CV( c, "r_shadowrendertotexture" ) != "1" )
				Count( "d7.gate.rttoff" );
			else if ( CV( c, "r_shadows" ) == "0" )
				Count( "d7.gate.r_shadows" );
			else
				Count( "d7.gate.gamecontrol" );
			continue;
		}
		if ( !CallHas( m, c, "E", "ctx.PushRenderTargetAndViewport" ) )
			Count( "d7.novisible" );
		std::set< std::string > accepted;
		for ( size_t i = 0; i < all.size(); i++ )
		{
			const RunRow &r = *all[ i ];
			if ( Is( r, "E", "leaf.enum" ) )
			{
				// What the enumeration asked of the shadow, up to the next.
				std::vector< const RunRow * > q;
				for ( size_t j = i + 1; j < all.size() && !Is( *all[ j ], "E", "leaf.enum" ) && !Is( *all[ j ], "E", "ctx.ClearColor4ub" ); j++ )
					q.push_back( all[ j ] );
				const int h = atoi( r.f[ 2 ].c_str() );
				const bool bRttFlag = m.flagsOfShadow.count( h ) && ( m.flagsOfShadow.at( h ) & SHADOW_FLAGS_USE_RENDER_TO_TEXTURE );
				if ( q.empty() )
					Count( accepted.count( r.f[ 2 ] ) || bRttFlag ? "d7.enum.renderedthisframe" : "d7.enum.notrtt" );
				else if ( Is( *q[ 0 ], "Q", "sm.GetInfo" ) && q[ 0 ]->f.size() > 9 && q[ 0 ]->f[ 9 ] == "255" && q.size() == 1 )
					Count( "d7.enum.transparent" );
				else
				{
					bool bCull = false, bArea = false;
					for ( const RunRow *x : q )
					{
						if ( Is( *x, "Q", "eng.CullBox" ) )
							bCull = x->f[ 4 ] == "1";
						if ( Is( *x, "Q", "ctx.ComputePixelDiameterOfSphere" ) )
							bArea = true;
					}
					if ( bArea )
					{
						Count( "d7.enum.accepted" );
						accepted.insert( r.f[ 2 ] );
					}
					else if ( bCull )
						Count( "d7.enum.culled" );
					else
						Count( "d7.enum.child" );
				}
			}
			if ( Is( r, "E", "ctx.PushRenderTargetAndViewport" ) )
			{
				const RunRow *next = NULL;
				for ( size_t j = i + 1; j < all.size(); j++ )
					if ( all[ j ]->f[ 0 ] == "E" )
					{
						next = all[ j ];
						break;
					}
				Count( next && next->f[ 1 ] == "ctx.ClearBuffers" ? "d7.target.cleared" : "d7.target.notcleared" );
			}
			if ( Is( r, "E", "sm.SetShadowMaterial" ) )
			{
				const bool bBlob = r.f[ 3 ] == "decals/simpleshadow";
				if ( bBlob && i + 2 < all.size() && Is( *all[ i + 1 ], "E", "sm.SetShadowTexCoord" ) && Is( *all[ i + 2 ], "E", "sm.ClearExtraClipPlanes" ) )
					Count( "d7.lod.blob" );
				if ( !bBlob )
				{
					Count( "d7.lod.back" );
					// Back from the blob with the texture it kept: only the texcoords, no draw.
					for ( size_t j = i + 1; j < all.size(); j++ )
					{
						if ( all[ j ]->f[ 0 ] != "E" )
							continue;
						if ( all[ j ]->f[ 1 ] == "sm.SetShadowTexCoord" )
							Count( "d7.lod.back.texcoordsonly" );
						break;
					}
				}
			}
			if ( Is( r, "E", "ctx.Viewport" ) )
			{
				Count( "d7.draw" );
				Count( "d7.draw.size" + r.f[ 4 ] );
			}
			if ( Is( r, "E", "mr.DrawModelShadowSetup" ) )
				Count( r.f[ 5 ] == "1" ? "d7.draw.model" : "d7.draw.model.nobones" );
			if ( Is( r, "E", "rv.DrawBrushModelShadow" ) )
				Count( "d7.draw.brush" );
		}
		// Drawn out of the order the shadows were enumerated (the area sort).
		std::vector< std::string > drawn;
		for ( size_t i = 0; i + 2 < all.size(); i++ )
			if ( Is( *all[ i ], "E", "ctx.MatrixMode" ) && all[ i ]->f[ 2 ] == "0" && Is( *all[ i + 1 ], "Q", "sm.GetInfo" ) )
				drawn.push_back( all[ i + 1 ]->f[ 2 ] );
		std::vector< std::string > enumerated;
		for ( const RunRow *r : all )
			if ( Is( *r, "E", "leaf.enum" ) )
				enumerated.push_back( r->f[ 2 ] );
		if ( drawn.size() > 1 )
			Count( "d7.sorted" );
	}
	for ( const Probe_t &p : s_Probes )
	{
		if ( p.m_strArm.rfind( "d7.", 0 ) == 0 )
			for ( const std::string &arm : Split( p.m_strArm, '|' ) )
				Count( arm );
	}
	// The animating source is redrawn each pass; a still one is not.
	{
		std::map< std::string, int > drawsByThing;
		for ( const RunCall &c : m.calls )
		{
			if ( c.f[ 1 ] != "ComputeShadowTextures" )
				continue;
			for ( const RunRow *r : RowsOf( m, c, "E" ) )
				if ( r->f[ 1 ] == "mr.DrawModelShadowSetup" )
					drawsByThing[ r->f[ 2 ] ]++;
		}
		int nPasses = 0;
		for ( const RunCall &c : m.calls )
			if ( c.f[ 1 ] == "ComputeShadowTextures" && CallHas( m, c, "E", "ctx.PushRenderTargetAndViewport" ) )
				++nPasses;
		for ( auto &kv : drawsByThing )
		{
			if ( kv.first == "r70" && kv.second < nPasses / 2 )
				Count( "d7.redraw.stillcleared" );
		}
		bool bLast = false;
		for ( const RunCall &c : m.calls )
		{
			if ( c.f[ 1 ] == "MarkRenderToTextureShadowDirty" || c.f[ 1 ] == "Restore" )
				bLast = false;
			if ( c.f[ 1 ] != "ComputeShadowTextures" || !CallHas( m, c, "E", "ctx.PushRenderTargetAndViewport" ) )
				continue;
			const bool bDrew = CallHas( m, c, "E", "mr.DrawModelShadowSetup", "r71" );
			if ( bDrew && bLast )
				Count( "d7.redraw.animating" );
			bLast = bDrew;
		}
	}

	// ---- D8: the flashlight depth textures.
	for ( const RunCall &c : m.calls )
	{
		const std::string &name = c.f[ 1 ];
		if ( name == "PreRender" )
			for ( const RunRow *r : RowsOf( m, c, "E" ) )
				if ( r->f[ 1 ] == "cvar.Set" && r->f[ 2 ] == "r_flashlightdepthres" )
					Count( "d8.alloc.clampwrittenback" );
		if ( name == "UnlockAllShadowDepthTextures" )
		{
			Count( "d8.unlock" );
			if ( CallHas( m, c, "E", "sm.SetFlashlightRenderState", "65535" ) )
				Count( "d8.viewstate.v2.none" );
		}
		if ( name != "ComputeShadowDepthTextures" )
			continue;
		const std::vector< const RunRow * > all = RowsOf( m, c );
		std::set< std::string > asked;
		for ( size_t i = 0; i < all.size(); i++ )
		{
			const RunRow &r = *all[ i ];
			if ( Is( r, "Q", "sm.GetFlashlightState" ) )
			{
				asked.insert( r.f[ 2 ] );
				if ( r.f.size() > 12 && r.f[ 12 ] == "0" && ( i + 1 >= all.size() || !Is( *all[ i + 1 ], "E", "sm.SetFlashlightDepthTexture" ) ) )
					Count( "d8.active.noshadows" );
				if ( i + 1 < all.size() && Is( *all[ i + 1 ], "E", "sm.SetFlashlightDepthTexture" ) && all[ i + 1 ]->f[ 3 ] == "-" )
					Count( "d8.active.culled" );
			}
			if ( Is( r, "M" ) && r.f[ 2 ].find( "Too many depth textures" ) == 0 )
				Count( "d8.active.overflow" );
			if ( Is( r, "M" ) && r.f[ 2 ].find( "Too many shadow maps" ) == 0 )
				Count( "d8.lock.failed" );
			if ( Is( r, "E", "view.UpdateShadowDepthTexture" ) )
				Count( "d8.render" );
			if ( Is( r, "E", "dbg.AddLineOverlay" ) )
				Count( "d9.frustum.lines" );
			if ( Is( r, "E", "sm.SetFlashlightRenderState" ) && r.f[ 2 ] != "65535" )
				Count( "d8.viewstate.v2.one" );
		}
		// A flashlight without the depth flag is never asked about.
		for ( int h : m.flashlights )
			if ( !( m.flagsOfShadow.at( h ) & SHADOW_FLAGS_USE_DEPTH_TEXTURE ) && !asked.count( std::to_string( m.engineOfShadow.at( h ) ) ) )
			{
				Count( "d8.active.nodepthflag" );
				break;
			}
		if ( CV( c, "r_flashlight_version2" ) != "1" && !CallHas( m, c, "E", "sm.SetFlashlightRenderState" ) )
			Count( "d8.viewstate.v1" );
	}

	// ---- The proxies (Q1) and the debug overlays (D9).
	for ( const RunCall &c : m.calls )
	{
		const std::string &name = c.f[ 1 ];
		if ( name == "ProxyInit" )
			for ( const RunRow *r : RowsOf( m, c, "R" ) )
				Count( "proxy." + c.f[ 2 ] + ( r->f[ 1 ] == "1" ? ".init" : ".refused" ) );
		if ( name == "ProxyBind" )
		{
			int nSets = 0;
			for ( const RunRow *r : RowsOf( m, c, "E" ) )
				if ( r->f[ 1 ].rfind( "var.", 0 ) == 0 )
					++nSets;
			Count( "proxy." + c.f[ 2 ] + ".bind.sets" + std::to_string( nSets ) );
		}
		if ( name == "RenderShadowTexture" )
			Count( CallHas( m, c, "E", "mesh.Draw" ) ? "d9.rendertexture.quad" : "d9.rendertexture.off" );
		if ( name == "PreRender" && CallHas( m, c, "E", "dbg.AddTextOverlay" ) )
			Count( "d9.rttdebug.boxes" );
	}

	// THE ARMS. Each must fire.
	const char *ppszArms[] = {
		// D1
		"d1.create.blob", "d1.create.rtt", "d1.create.brushflag", "d1.create.norenderable", "d1.flashlight.depth", "d1.flashlight.nodepth",
		"d1.destroy", "d1.destroyflashlight", "d1.levelshutdown.destroys", "d1.retire", "d1.type.rttcollapsed",
		// D2
		"d2.init.lowend", "d2.init.rtt", "d2.init.depthunsupported", "d2.init.depth", "d2.init.restorefunc", "d2.prerender.depthunsupported",
		"d2.prerender.depthresize", "d2.prerender.depthoff", "d2.prerender.depthon", "d2.prerender.rtton", "d2.prerender.toggle.returns",
		"d2.rtton.convertsshadows", "d2.prerender.rttoff", "d2.rttoff.uncache", "d2.levelinit.clamped", "d2.levelinit.scaled",
		"d2.levelinit.rttactive", "d2.color.rtt", "d2.color.blob", "d2.restore", "d2.direction.rtt.updateall", "d2.direction.blob.nothing",
		"d2.direction.blobdown", "d2.direction.rttset", "d2.distance.updateall", "d2.gamecontrol", "d2.falloffbias", "d2.shutdown",
		"d2.cmd.r_shadowdir.print", "d2.cmd.r_shadowdir.set", "d2.cmd.r_shadowangles.print", "d2.cmd.r_shadowangles.set",
		"d2.cmd.r_shadowcolor.print", "d2.cmd.r_shadowcolor.set", "d2.cmd.r_shadowdist.print", "d2.cmd.r_shadowdist.set",
		"d2.cmd.r_shadowblobbycutoff.print", "d2.cmd.r_shadowblobbycutoff.set",
		// D3
		"d3.dirty.invalidhandle", "d3.dirty.already", "d3.dirty.noshadowhandle", "d3.dirty.marked", "d3.dirty.marked.force",
		"d3.dirty.parentpropagates", "d3.dirty.ignoredwhileupdating", "d3.texturedirty.invalid", "d3.texturedirty.parent",
		"d3.transparent.disabled", "d3.transparent.keptdirty", "d3.child.disabled", "d3.update.moved", "d3.update.notmoved",
		"d3.update.unknownmodeltype", "d3.update.nomodel", "d3.useparent.recursesnone", "d3.willparentblob.recursesnone",
		"d3.walk.handleorder", "d3.walk.stopsatretire", "d3.updateprojected.invalid", "d3.updateprojected.notflashlight",
		"d3.updateprojected.flashlight", "d3.removefromdirty.clearsbit",
		// D4
		"d4.distance.override", "d4.direction.override", "d4.ortho", "d4.ortho.axis0", "d4.ortho.axis1", "d4.ortho.axis2",
		"d4.rtt", "d4.rtt.clipplanes.bothsides", "d4.bounds.childrenadded", "d4.bounds.blobparent.norecursion",
		// D5
		"d5.version2", "d5.lightworld", "d5.lightworld.off", "d5.models", "d5.models.off", "d5.setter.flashlight", "d5.setter.notflashlight",
		"d5.state",
		// D6
		"d6.self", "d6.notreceiving", "d6.flash.culled", "d6.flash.kept", "d6.shadow.boxmiss", "d6.plane.centers", "d6.plane.separating",
		"d6.plane.none", "d6.side.pos.culled", "d6.side.pos.kept", "d6.side.neg.culled", "d6.side.neg.kept", "d6.culled",
		"d6.kept.brush.flash", "d6.kept.brush.rtt", "d6.kept.brush.simple", "d6.kept.static.flash", "d6.kept.static.rtt",
		"d6.kept.static.simple", "d6.kept.studio.flash", "d6.kept.studio.rtt", "d6.kept.studio.simple", "d6.removeall.notreceiving",
		"d6.removeall.sm.RemoveAllShadowsFromBrushModel", "d6.removeall.sp.RemoveAllShadowsFromStaticProp",
		"d6.removeall.sm.RemoveAllShadowsFromModel", "d6.removeall.studio.noinstance",
		// D7
		"d7.gate.rttoff", "d7.gate.r_shadows", "d7.gate.gamecontrol", "d7.novisible", "d7.enum.renderedthisframe", "d7.enum.notrtt",
		"d7.enum.transparent", "d7.enum.child", "d7.enum.culled", "d7.enum.accepted", "d7.sorted", "d7.target.cleared",
		"d7.target.notcleared", "d7.budget.lod", "d7.lod.blob", "d7.lod.back", "d7.lod.back.texcoordsonly", "d7.draw", "d7.draw.size16",
		"d7.draw.size64", "d7.draw.size128", "d7.draw.size256", "d7.draw.model", "d7.draw.model.nobones", "d7.draw.brush",
		"d7.redraw.animating", "d7.redraw.stillcleared", "d7.alloc.same", "d7.alloc.keepnear", "d7.alloc.keepold", "d7.alloc.upgrade",
		"d7.alloc.fresh", "d7.alloc.lower", "d7.alloc.none", "d7.alloc.steal", "d7.alloc.stolenfrom", "d7.drew",
		// D8
		"d8.alloc.tools4", "d8.alloc.clampwrittenback", "d8.active.nodepthflag", "d8.active.noshadows", "d8.active.culled",
		"d8.active.overflow", "d8.lock.failed", "d8.render", "d8.viewstate.v1", "d8.viewstate.v2.one", "d8.viewstate.v2.none", "d8.unlock",
		// D9 and the proxies
		"d9.frustum.lines", "d9.rendertexture.quad", "d9.rendertexture.off", "d9.rttdebug.boxes", "proxy.Shadow.init", "proxy.Shadow.refused",
		"proxy.ShadowModel.init", "proxy.ShadowModel.refused", "proxy.Shadow.bind.sets1", "proxy.ShadowModel.bind.sets7",
	};
	std::string table;
	int nMissing = 0;
	for ( const char *pszArm : ppszArms )
	{
		table += std::string( pszArm ) + " " + std::to_string( arms[ pszArm ] ) + "\n";
		if ( arms[ pszArm ] == 0 )
		{
			++nMissing;
			ADD_FAILURE() << "the scenario never reaches " << pszArm;
		}
	}
	for ( const std::string &f : failures )
		ADD_FAILURE() << f;
	EXPECT_EQ( nMissing, 0 ) << table;
	// The arm table, for the plan's reach counts.
	if ( getenv( "SS_SHADOW_REACH_TABLE" ) )
	{
		std::ofstream out( getenv( "SS_SHADOW_REACH_TABLE" ) );
		out << table;
		for ( auto &kv : arms )
			out << "# all " << kv.first << " " << kv.second << "\n";
	}
}

// THE PRISTINE C_BaseEntity ARMS FIRE 0 TIMES: every place the subject asks
// for an entity was asked (and told NULL), and nothing of C_BaseEntity was
// touched, so the golden is also the fork's behaviour, whose deletions made
// those arms answer NULL.
TEST( ShadowMgrOracle, TheBaseEntityArmsFireZeroTimes )
{
	RunScenario();
	EXPECT_GT( s_nBaseEntityAsks, 0 ) << "the scenario never reached an arm that asks for a C_BaseEntity";
	EXPECT_EQ( g_nBaseEntityTouches, 0 ) << "a C_BaseEntity arm ran";
}

// THE FARM'S TU DIFFERS FROM THE PRISTINE ONLY BY THE PLAN'S EDIT FAMILIES
// (§1 of docs/plans/c7_shadow_manager.md), so the golden stock wrote is the
// fork's behaviour too. Each hunk of the line diff is named by a family; a
// hunk no family names fails.
namespace
{

struct Hunk_t
{
	std::vector< std::string > removed, added;
};

// Myers' O(ND) line diff, as hunks.
std::vector< Hunk_t > Diff( const std::vector< std::string > &a, const std::vector< std::string > &b )
{
	const int n = (int)a.size(), m = (int)b.size(), max = n + m;
	std::vector< std::vector< int > > trace;
	std::vector< int > v( 2 * max + 2, 0 );
	int d = 0;
	for ( ; d <= max; d++ )
	{
		trace.push_back( v );
		bool bDone = false;
		for ( int k = -d; k <= d; k += 2 )
		{
			int x = ( k == -d || ( k != d && v[ max + k - 1 ] < v[ max + k + 1 ] ) ) ? v[ max + k + 1 ] : v[ max + k - 1 ] + 1;
			int y = x - k;
			while ( x < n && y < m && a[ x ] == b[ y ] )
			{
				x++;
				y++;
			}
			v[ max + k ] = x;
			if ( x >= n && y >= m )
			{
				bDone = true;
				break;
			}
		}
		if ( bDone )
			break;
	}
	// Walk back to the edit script.
	std::vector< std::pair< char, int > > edits;   // '-' a index, '+' b index, '=' a common line
	int x = n, y = m;
	for ( int dd = d; dd > 0; dd-- )
	{
		const std::vector< int > &vv = trace[ dd ];
		const int k = x - y;
		const int prevK = ( k == -dd || ( k != dd && vv[ max + k - 1 ] < vv[ max + k + 1 ] ) ) ? k + 1 : k - 1;
		const int prevX = vv[ max + prevK ];
		const int prevY = prevX - prevK;
		while ( x > prevX && y > prevY )
		{
			x--;
			y--;
			edits.push_back( { '=', x } );
		}
		if ( x == prevX )
			edits.push_back( { '+', --y } );
		else
			edits.push_back( { '-', --x } );
	}
	while ( x > 0 && y > 0 )
	{
		x--;
		y--;
		edits.push_back( { '=', x } );
	}
	std::reverse( edits.begin(), edits.end() );
	std::vector< Hunk_t > hunks;
	bool bOpen = false;
	for ( auto &e : edits )
	{
		if ( e.first == '=' )
		{
			bOpen = false;
			continue;
		}
		if ( !bOpen )
			hunks.emplace_back();
		bOpen = true;
		if ( e.first == '-' )
			hunks.back().removed.push_back( a[ e.second ] );
		else
			hunks.back().added.push_back( b[ e.second ] );
	}
	return hunks;
}

std::string Trim( const std::string &s )
{
	size_t i = 0, j = s.size();
	while ( i < j && isspace( (unsigned char)s[ i ] ) )
		i++;
	while ( j > i && isspace( (unsigned char)s[ j - 1 ] ) )
		j--;
	return s.substr( i, j - i );
}

bool IsComment( const std::string &s )
{
	const std::string t = Trim( s );
	return t.empty() || t.rfind( "//", 0 ) == 0;
}

bool AnyHas( const std::vector< std::string > &v, const char *psz )
{
	for ( const std::string &s : v )
		if ( s.find( psz ) != std::string::npos )
			return true;
	return false;
}

// The family a hunk belongs to, or "" when none names it.
std::string Family( const Hunk_t &h )
{
	std::vector< std::string > code;
	for ( const std::string &s : h.added )
		if ( !IsComment( s ) )
			code.push_back( s );
	// 1. the resolve: ClientEntityList()'s renderable lookup, respelled.
	if ( !h.removed.empty() && h.removed.size() == code.size() )
	{
		bool bAll = true;
		for ( size_t i = 0; i < code.size(); i++ )
		{
			std::string r = h.removed[ i ];
			const std::string from = "ClientEntityList().GetClientRenderableFromHandle(";
			const size_t at = r.find( from );
			if ( at == std::string::npos )
			{
				bAll = false;
				break;
			}
			r.replace( at, from.size(), "SourceSharp_RenderableFromHandle(" );
			bAll = bAll && r == code[ i ];
		}
		if ( bAll )
			return "resolve";
	}
	// 2. the bone-setup queue, and its include.
	if ( ( AnyHas( h.removed, "ShadowBoneSetups" ) || AnyHas( h.removed, "SetupBonesOnBaseAnimating" ) ) && code.empty() )
		return "bonequeue";
	if ( h.removed.empty() && code.size() == 1 && Trim( code[ 0 ] ) == "#include \"c_baseanimating.h\"" )
		return "bonequeue";
	if ( h.removed.empty() && code.empty() )
		return "comment";
	// 3. the fourth clip plane.
	if ( AnyHas( h.removed, "m_bEnableRenderingClipPlane" ) && code.empty() )
		return "clipplane";
	// 4. the static prop's NPC/player arm.
	if ( AnyHas( h.removed, "FL_NPC | FL_CLIENT" ) && code.empty() )
		return "staticpropnpc";
	// 5. the flashlight target's tails.
	if ( AnyHas( h.removed, "m_hTargetEntity" ) && code.empty() )
		return "flashlighttarget";
	if ( AnyHas( h.removed, "pChild = pChild->NextMovePeer();" ) && code.empty() )
		return "flashlighttarget";
	{
		// IsFlashlightTarget no longer reads its shadow (its body went with the tails).
		bool bOnlyShadowRead = !h.removed.empty() && code.empty();
		for ( const std::string &r : h.removed )
			bOnlyShadowRead = bOnlyShadowRead && ( Trim( r ).empty() || Trim( r ) == "ClientShadow_t &shadow = m_Shadows[ shadowHandle ];" );
		if ( bOnlyShadowRead )
			return "flashlighttarget";
	}
	// 6. cs_cl_shadow_report: its include, counter, declaration and body.
	if ( h.removed.empty() && ( AnyHas( code, "renderable_facts.h" ) || AnyHas( code, "s_nRttShadowTexturesDrawn" ) ||
	                            AnyHas( code, "PrintShadowReport" ) ) )
		return "shadowreport";
	// 7. the debug overlay's entity index, from the networkable (not in §1's
	// list: it moves only cl_drawshadowtexture's text, see the plan's S0/S1 as built).
	if ( AnyHas( h.removed, "GetBaseEntity()" ) && AnyHas( code, "GetClientNetworkable()" ) )
		return "debugentindex";
	if ( AnyHas( h.removed, "pEnt->entindex()" ) && AnyHas( code, "pNet->entindex()" ) )
		return "debugentindex";
	return "";
}

std::map< std::string, int > Families( const std::string &pristine, const std::string &farm, std::vector< std::string > *pUnnamed )
{
	std::map< std::string, int > families;
	for ( const Hunk_t &h : Diff( Lines( pristine ), Lines( farm ) ) )
	{
		const std::string f = Family( h );
		if ( f.empty() )
			pUnnamed->push_back( ( h.removed.empty() ? std::string( "+" ) + ( h.added.empty() ? "" : h.added[ 0 ] ) : "-" + h.removed[ 0 ] ) );
		else
			families[ f ]++;
	}
	return families;
}

} // namespace

TEST( ShadowMgrOracle, TheFarmDiffersFromThePristineOnlyByTheEditFamilies )
{
	const std::string pristine = ReadValve( "src/game/client/clientshadowmgr.cpp" );
	std::ifstream in( "game/client/clientshadowmgr.cpp" );
	ASSERT_TRUE( in.good() ) << "the farm has no game/client/clientshadowmgr.cpp (the test runs in farm/<config>/src)";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string farm = ss.str();
	ASSERT_GT( pristine.size(), 100000u );
	ASSERT_GT( farm.size(), 100000u );

	std::vector< std::string > unnamed;
	const std::map< std::string, int > families = Families( pristine, farm, &unnamed );
	for ( const std::string &u : unnamed )
		ADD_FAILURE() << "a fork edit no family names: " << u.substr( 0, 200 );
	for ( const char *psz : { "resolve", "bonequeue", "clipplane", "staticpropnpc", "flashlighttarget", "shadowreport", "debugentindex" } )
		EXPECT_GT( families.count( psz ) ? families.at( psz ) : 0, 0 ) << "no hunk of the family " << psz << ": the fork changed";
	EXPECT_EQ( families.count( "resolve" ) ? families.at( "resolve" ) : 0, 12 ) << "the pristine file resolves a renderable through ClientEntityList() at 12 sites, each respelled";

	// The classifier can fail: an edit outside the families is named as such.
	std::string planted = farm;
	const size_t at = planted.find( "SetShadowBlobbyCutoffArea( 0.005 );" );
	ASSERT_NE( at, std::string::npos );
	planted.replace( at, strlen( "SetShadowBlobbyCutoffArea( 0.005 );" ), "SetShadowBlobbyCutoffArea( 0.006 );" );
	std::vector< std::string > plantedUnnamed;
	Families( pristine, planted, &plantedUnnamed );
	EXPECT_EQ( plantedUnnamed.size(), unnamed.size() + 1 ) << "a planted edit outside the families was not caught";
}
