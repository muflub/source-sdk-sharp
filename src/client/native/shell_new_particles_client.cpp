//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The new-style particle effects' shell and frame. Managed decides each step and draw;
// the particle library owns and runs the collections. Each frame reads every live effect
// into one length-prefixed buffer and carries out managed's answers.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"

#include "iclientmode.h"

#include "iviewrender.h"
#include "model_types.h"

#include "shell_new_particles.h"
#include "imanaged_areas_client.h"

#include "ss_bitblock.h"
#include "managed_convar_ref.h"

#include "shell_standalone_client.h"
#include "slot_table.h"

using ss::BitBlockBits;
using ss::BitBlockSetBits;
using ss::kBitBlockHeaderBytes;

// The convars the frame and the draw read, by name.
static CManagedConVarRef s_r_drawparticles( "r_drawparticles" );
static CManagedConVarRef s_cl_particleeffect_aabb_buffer( "cl_particleeffect_aabb_buffer" );
static CManagedConVarRef s_cl_particle_show_bbox( "cl_particle_show_bbox" );
static CManagedConVarRef s_cl_particle_show_bbox_cost( "cl_particle_show_bbox_cost" );
static CManagedConVarRef s_r_particle_timescale( "r_particle_timescale" );
static CManagedConVarRef s_cl_particle_retire_cost( "cl_particle_retire_cost" );
static CManagedConVarRef s_r_particle_sim_spike_threshold_ms( "r_particle_sim_spike_threshold_ms" );

namespace
{

// The protected members of the library's collection this shell reaches. No member is
// added, so the object is exactly the library's CParticleCollection.
struct ShellCollection : public CParticleCollection
{
	static CParticleCollection *Make( const char *pszName )
	{
		ShellCollection *p = new ShellCollection;
		p->Init( pszName );
		return p;
	}

	static void Free( CParticleCollection *p ) { delete static_cast<ShellCollection *>( p ); }

	static ShellCollection *Of( CParticleCollection *p ) { return static_cast<ShellCollection *>( p ); }

	static const ShellCollection *Of( const CParticleCollection *p )
	{
		return static_cast<const ShellCollection *>( p );
	}

	Vector &MinBounds() { return m_MinBounds; }

	Vector &MaxBounds() { return m_MaxBounds; }

	const Vector &MinBounds() const { return m_MinBounds; }

	const Vector &MaxBounds() const { return m_MaxBounds; }

	int HighestCP() const { return m_nHighestCP; }
};

// The live effects, newest first; ids rise with creation, so falling id order.
CUtlVector<CShellNewParticleEffect *> s_Effects;
uint32_t s_nNextId = 0;

void CopyVector( const Vector &v, float *pOut )
{
	pOut[0] = v.x;
	pOut[1] = v.y;
	pOut[2] = v.z;
}

} // namespace

// The render group a view-model effect takes is managed's, at its first frame.
CSmartPtr<CShellNewParticleEffect> CShellNewParticleEffect::Create( const char *pszName, const char *pszDebugName )
{
	CShellNewParticleEffect *pEffect =
		new CShellNewParticleEffect( ShellCollection::Make( pszName ), pszDebugName ? pszDebugName : pszName );
	return pEffect;
}

CShellNewParticleEffect::CShellNewParticleEffect( CParticleCollection *pCollection, const char *pszDebugName )
	: m_pCollection( pCollection )
	, m_pDebugName( pszDebugName )
	, m_nId( ++s_nNextId )
	, m_nRefCount( 0 )
	, m_bRemoved( false )
	, m_bReleasedSinceFrame( false )
	, m_bAddedSinceFrame( true )
{
	m_vSortOrigin.Init();
	SetIdentityMatrix( m_RenderTransform );

	// Construct()'s inside-out box, so the first frame's box is a change.
	ShellCollection::Of( m_pCollection )->MinBounds().Init( 1.0e6, 1.0e6, 1.0e6 );
	ShellCollection::Of( m_pCollection )->MaxBounds().Init( -1.0e6, -1.0e6, -1.0e6 );

	s_Effects.AddToHead( this );
	ClientLeafSystem()->CreateRenderableHandle( this );
}

CShellNewParticleEffect::~CShellNewParticleEffect()
{
	if ( !m_bRemoved )
	{
		if ( m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
			ClientLeafSystem()->RemoveRenderable( m_hRenderHandle );
		s_Effects.FindAndRemove( this );
	}
	ShellCollection::Free( m_pCollection );
	m_pCollection = NULL;
}

// Out of the leaf system and the list; deleted now if nothing holds it, else when the
// last holder lets go.
void CShellNewParticleEffect::Remove()
{
	if ( m_bRemoved )
		return;
	if ( m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
	{
		ClientLeafSystem()->RemoveRenderable( m_hRenderHandle );
		m_hRenderHandle = INVALID_CLIENT_RENDER_HANDLE;
	}
	s_Effects.FindAndRemove( this );
	m_bRemoved = true;
	if ( m_nRefCount == 0 )
		delete this;
}

void CShellNewParticleEffect::AddRef()
{
	++m_nRefCount;
}

// The finished-and-allocated removal test is managed's, at the next frame.
void CShellNewParticleEffect::Release()
{
	Assert( m_nRefCount > 0 );
	if ( --m_nRefCount > 0 )
		return;
	if ( m_bRemoved )
		delete this;
	else
		m_bReleasedSinceFrame = true;
}

bool CShellNewParticleEffect::DefinitionReadsControlPoint( int nPoint ) const
{
	return m_pCollection && m_pCollection->m_pDef && m_pCollection->m_pDef->ReadsControlPoint( nPoint );
}

void CShellNewParticleEffect::SetControlPoint( int nWhichPoint, const Vector &v )
{
	m_pCollection->SetControlPoint( nWhichPoint, v );
}

void CShellNewParticleEffect::SetControlPointOrientation( int nWhichPoint, const Vector &forward, const Vector &right,
														  const Vector &up )
{
	m_pCollection->SetControlPointOrientation( nWhichPoint, forward, right, up );
}

void CShellNewParticleEffect::SetControlPointEntity( int nWhichPoint, C_StandaloneEntity *pEntity )
{
	if ( pEntity )
	{
		m_pCollection->SetControlPointObject( nWhichPoint, &m_hControlPointOwners[nWhichPoint] );
		m_hControlPointOwners[nWhichPoint] = pEntity->GetRefEHandle();
	}
	else
	{
		m_pCollection->SetControlPointObject( nWhichPoint, NULL );
	}
}

C_StandaloneEntity *CShellNewParticleEffect::GetControlPointEntity( int nWhichPoint ) const
{
	return SourceSharp_ClientShellFromHandle( m_hControlPointOwners[nWhichPoint] );
}

void CShellNewParticleEffect::StopEmission( bool bInfiniteOnly, bool bRemoveAllParticles, bool bWakeOnStop )
{
	m_pCollection->StopEmission( bInfiniteOnly, bRemoveAllParticles, bWakeOnStop );
}

CShellNewParticleEffect *CShellNewParticleEffect::ReplaceWith( const char *pszName )
{
	StopEmission( false, true, true );
	if ( !pszName || !pszName[0] )
		return NULL;

	CSmartPtr<CShellNewParticleEffect> pNew = Create( pszName, pszName );
	if ( !pNew->IsValid() )
		return pNew.GetObject();

	CParticleCollection *pFrom = m_pCollection;
	CParticleCollection *pTo = pNew->m_pCollection;
	for ( int i = 0; i < MAX_PARTICLE_CONTROL_POINTS; ++i )
	{
		if ( !pFrom->ReadsControlPoint( i ) )
			continue;
		Vector vecForward, vecRight, vecUp;
		pTo->SetControlPoint( i, pFrom->GetControlPointAtCurrentTime( i ) );
		pFrom->GetControlPointOrientationAtCurrentTime( i, &vecForward, &vecRight, &vecUp );
		pTo->SetControlPointOrientation( i, vecForward, vecRight, vecUp );
		pTo->SetControlPointParent( i, pFrom->GetControlPointParent( i ) );
	}
	return pNew.GetObject();
}

const matrix3x4_t &CShellNewParticleEffect::RenderableToWorldTransform()
{
	SetIdentityMatrix( m_RenderTransform );
	PositionMatrix( GetRenderOrigin(), m_RenderTransform );
	return m_RenderTransform;
}

void CShellNewParticleEffect::GetRenderBounds( Vector &mins, Vector &maxs )
{
	const ShellCollection *p = ShellCollection::Of( m_pCollection );
	VectorSubtract( p->MinBounds(), GetRenderOrigin(), mins );
	VectorSubtract( p->MaxBounds(), GetRenderOrigin(), maxs );
}

bool CShellNewParticleEffect::IsTransparent()
{
	return m_pCollection->IsTranslucent();
}

bool CShellNewParticleEffect::IsTwoPass()
{
	return m_pCollection->IsTwoPass();
}

bool CShellNewParticleEffect::UsesPowerOfTwoFrameBufferTexture()
{
	return m_pCollection->UsesPowerOfTwoFrameBufferTexture( true );
}

bool CShellNewParticleEffect::UsesFullFrameBufferTexture()
{
	return m_pCollection->UsesFullFrameBufferTexture( true );
}

// The entities passed to managed are identity tokens: managed only compares them.
int CShellNewParticleEffect::DrawModel( int flags )
{
	IManagedParticlesClient *pArea = ManagedParticlesClient();
	if ( !pArea || !IsValid() )
		return 0;

	const ShellCollection *p = ShellCollection::Of( m_pCollection );
	const CParticleSystemDefinition *pDef = m_pCollection->m_pDef;
	CMatRenderContextPtr pRenderContext( materials );

	SourceSharpNewParticleDrawIn_t in;
	V_memset( &in, 0, sizeof( in ) );
	in.Id = m_nId;
	in.Flags = flags;
	in.DrawParticles = s_r_drawparticles.GetBool() ? 1 : 0;
	in.ClientModeDraws = g_pClientMode->ShouldDrawParticles() ? 1 : 0;
	// Nothing turns the manager's render switch off, so it is on.
	in.RenderSystems = 1;
	in.ShowBBox = s_cl_particle_show_bbox.GetBool() ? 1 : 0;
	in.ShowBBoxCost = s_cl_particle_show_bbox_cost.GetInt();
	in.Active = m_pCollection->m_nActiveParticles;
	Vector vecCamera;
	pRenderContext->GetWorldSpaceCameraPosition( &vecCamera );
	CopyVector( vecCamera, in.CameraXYZ );
	CopyVector( p->MinBounds(), in.MinXYZ );
	CopyVector( p->MaxBounds(), in.MaxXYZ );
	in.MaxDrawDistance = pDef->m_flMaxDrawDistance;
	in.NoDrawTimeToGoToSleep = pDef->m_flNoDrawTimeToGoToSleep;
	in.LastSimulationTime = g_pParticleSystemMgr->GetLastSimulationTime();
	in.NextSleepTime = m_pCollection->m_flNextSleepTime;
	in.SkipRenderControlPoint = pDef->m_nSkipRenderControlPoint;
	in.HighestControlPoint = p->HighestCP();
	in.ThirdPerson = input->CAM_IsThirdPerson() ? 1 : 0;

	// The camera object is NULL: on this side no view entity resolves to a C_BaseEntity.
	void *pCameraObject = NULL;
	if ( flags & STUDIO_TRANSPARENCY )
	{
		const int nSkip = pDef->m_nSkipRenderControlPoint;
		if ( nSkip >= 0 && nSkip < MAX_PARTICLE_CONTROL_POINTS )
			in.SkipObject = (uint64_t)(uintptr_t)GetControlPointEntity( nSkip );
		// The local player is never a C_BasePlayer, so its observer fields stay 0.
	}

	SourceSharpNewParticleDrawOut_t out;
	V_memset( &out, 0, sizeof( out ) );
	if ( pArea->NewEffectDraw( &in, &out ) <= 0 )
		return 0;

	if ( out.SetSleep )
		m_pCollection->m_flNextSleepTime = out.NextSleepTime;

	if ( out.Action == 1 )
	{
		pRenderContext->MatrixMode( MATERIAL_MODEL );
		pRenderContext->PushMatrix();
		pRenderContext->LoadIdentity();
		m_pCollection->Render( pRenderContext, IsTwoPass(), pCameraObject );
		pRenderContext->MatrixMode( MATERIAL_MODEL );
		pRenderContext->PopMatrix();
	}
	else if ( out.Action == 2 )
	{
		g_pParticleSystemMgr->AddToRenderCache( m_pCollection );
	}

	// DebugDrawBbox's overlay, in the colour managed chose.
	if ( out.BBox && debugoverlay )
	{
		const Vector center = GetRenderOrigin();
		const Vector mins = p->MinBounds() - center;
		const Vector maxs = p->MaxBounds() - center;
		debugoverlay->AddBoxOverlay( center, mins, maxs, QAngle( 0, 0, 0 ), out.BBoxR, out.BBoxG, out.BBoxB, 16, 0 );
		debugoverlay->AddTextOverlayRGB( center, 0, 0, out.BBoxR, out.BBoxG, out.BBoxB, 64, "%s:(%d)", GetEffectName(),
										 m_pCollection->m_nActiveParticles );
	}

	return out.Action != 0 ? 1 : 0;
}

int ShellNewParticles_Count()
{
	return s_Effects.Count();
}

CShellNewParticleEffect *ShellNewParticles_At( int nIndex )
{
	return ( nIndex >= 0 && nIndex < s_Effects.Count() ) ? s_Effects[nIndex] : NULL;
}

namespace
{

void FillEffectIn( CShellNewParticleEffect *pEffect, SourceSharpNewParticleIn_t &in )
{
	CParticleCollection *pCollection = pEffect->Collection();
	const ShellCollection *p = ShellCollection::Of( pCollection );
	const CParticleSystemDefinition *pDef = pCollection->m_pDef;
	const bool bReleased = pEffect->IsReleasedSinceFrame() && pEffect->RefCount() == 0;

	V_memset( &in, 0, sizeof( in ) );
	in.Id = pEffect->Id();
	in.Flags = SOURCESHARP_NEW_PARTICLE_ALLOCATED | ( bReleased ? SOURCESHARP_NEW_PARTICLE_RELEASED : 0 ) |
		( pEffect->m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE ? SOURCESHARP_NEW_PARTICLE_RENDER_HANDLE : 0 ) |
		( pEffect->IsAddedSinceFrame() ? SOURCESHARP_NEW_PARTICLE_ADDED : 0 );
	if ( pEffect->IsValid() && pDef->IsViewModelEffect() )
		in.Flags |= SOURCESHARP_NEW_PARTICLE_VIEW_MODEL;
	in.Collection = (uint64_t)(uintptr_t)pCollection;
	in.Definition = (uint64_t)(uintptr_t)pDef;
	in.NextSleepTime = pCollection->m_flNextSleepTime;
	in.Active = pCollection->m_nActiveParticles;
	in.HasMoved = pCollection->HasMoved() ? 1 : 0;
	in.Finished = ( bReleased && pCollection->IsFinished() ) ? 1 : 0;
	CopyVector( pEffect->GetSortOrigin(), in.SortOriginXYZ );
	CopyVector( p->MinBounds(), in.MinXYZ );
	CopyVector( p->MaxBounds(), in.MaxXYZ );
	if ( pDef )
	{
		const char *pszReplacement = pDef->GetCullReplacementDefinition();
		if ( pszReplacement && pszReplacement[0] )
			in.Flags |= SOURCESHARP_NEW_PARTICLE_HAS_REPLACEMENT;
		in.CullRadius = pDef->GetCullRadius();
		in.CullFillCost = pDef->GetCullFillCost();
		// Read from the array rather than GetControlPointAtCurrentTime, which asserts for a point
		// nothing set.
		const int nCull = pDef->GetCullControlPoint();
		if ( in.CullRadius != 0.0f && nCull >= 0 && nCull < MAX_PARTICLE_CONTROL_POINTS )
			CopyVector( pCollection->m_ControlPoints[nCull].m_Position, in.CullPointXYZ );
	}
}

// The live effect with this id, in a snapshot in falling id order.
int FindById( const uint32_t *ids, int nCount, uint32_t nId )
{
	int lo = 0, hi = nCount - 1;
	while ( lo <= hi )
	{
		const int mid = ( lo + hi ) / 2;
		if ( ids[mid] == nId )
			return mid;
		if ( ids[mid] > nId )
			lo = mid + 1;
		else
			hi = mid - 1;
	}
	return -1;
}

} // namespace

// One crossing: managed decides, the library steps during it, and managed's answers are
// carried out here in order.
void ShellNewParticles_Frame( float flFrameTime )
{
	IManagedParticlesClient *pArea = ManagedParticlesClient();
	if ( !pArea )
		return;

	// UpdateAllEffects' clamp.
	if ( flFrameTime > 0.1f )
		flFrameTime = 0.1f;

	// The partition's read-lock pair before any collection steps.
	if ( s_Effects.Count() > 0 )
		UpdateDirtySpatialPartitionEntities();

	const int nEffects = s_Effects.Count();
	CUtlVectorFixedGrowable<uint8_t, 16384> in;
	CUtlVectorFixedGrowable<uint8_t, 4096> out;
	const int nIn =
		(int)sizeof( SourceSharpNewParticleFrameHeader_t ) + nEffects * (int)sizeof( SourceSharpNewParticleIn_t );
	in.SetCount( kBitBlockHeaderBytes + nIn );
	BitBlockSetBits( in.Base(), nIn * 8 );

	SourceSharpNewParticleFrameHeader_t *pHeader =
		(SourceSharpNewParticleFrameHeader_t *)( in.Base() + kBitBlockHeaderBytes );
	V_memset( pHeader, 0, sizeof( *pHeader ) );
	pHeader->Dt = flFrameTime;
	pHeader->Timescale = s_r_particle_timescale.GetFloat();
	pHeader->CurTime = gpGlobals->curtime;
	pHeader->FrameCount = gpGlobals->framecount;
	pHeader->AabbBuffer = s_cl_particleeffect_aabb_buffer.GetFloat();
	pHeader->RetireCost = s_cl_particle_retire_cost.GetFloat();
	pHeader->SpikeThresholdMs = s_r_particle_sim_spike_threshold_ms.GetInt();
	if ( pHeader->RetireCost != 0.0f && nEffects > 0 )
	{
		const CViewSetup *pView = view->GetPlayerViewSetup();
		pHeader->ViewX = pView->x;
		pHeader->ViewY = pView->y;
		pHeader->ViewWidth = pView->width;
		pHeader->ViewHeight = pView->height;
		pHeader->ViewFov = pView->fov;
		CopyVector( pView->origin, pHeader->ViewOriginXYZ );
		VMatrix worldToView, viewToProjection, worldToProjection, worldToScreen;
		render->GetMatricesForView( *pView, &worldToView, &viewToProjection, &worldToProjection, &worldToScreen );
		V_memcpy( pHeader->WorldToScreen, worldToScreen.Base(), sizeof( pHeader->WorldToScreen ) );
	}

	CUtlVectorFixedGrowable<CShellNewParticleEffect *, 256> snapshot;
	CUtlVectorFixedGrowable<uint32_t, 256> ids;
	snapshot.SetCount( nEffects );
	ids.SetCount( nEffects );
	SourceSharpNewParticleIn_t *pIn = (SourceSharpNewParticleIn_t *)( pHeader + 1 );
	for ( int i = 0; i < nEffects; i++ )
	{
		snapshot[i] = s_Effects[i];
		ids[i] = s_Effects[i]->Id();
		FillEffectIn( s_Effects[i], pIn[i] );
		s_Effects[i]->ClearFrameFlags();
	}

	// At most four commits per effect: the view group, a replacement, a change and the removal.
	const int nOut = MAX( nEffects * 4 * (int)sizeof( SourceSharpNewParticleCommit_t ), 4 );
	out.SetCount( kBitBlockHeaderBytes + nOut );
	BitBlockSetBits( out.Base(), 0 );
	if ( pArea->NewEffectsFrame( in.Base(), in.Count(), out.Base(), out.Count() ) <= 0 )
		return;

	const int nBytes = MIN( BitBlockBits( out.Base() ) / 8, out.Count() - kBitBlockHeaderBytes );
	const SourceSharpNewParticleCommit_t *pCommits =
		(const SourceSharpNewParticleCommit_t *)( out.Base() + kBitBlockHeaderBytes );
	for ( int c = 0; c < nBytes / (int)sizeof( SourceSharpNewParticleCommit_t ); c++ )
	{
		const int i = FindById( ids.Base(), ids.Count(), pCommits[c].Id );
		if ( i < 0 || !snapshot[i] )
			continue;
		CShellNewParticleEffect *pEffect = snapshot[i];
		const int32_t nActions = pCommits[c].Actions;
		if ( ( nActions & SOURCESHARP_NEW_PARTICLE_VIEW_GROUP ) &&
			 pEffect->m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
			ClientLeafSystem()->SetRenderGroup( pEffect->m_hRenderHandle, RENDER_GROUP_VIEW_MODEL_TRANSLUCENT );
		if ( nActions & SOURCESHARP_NEW_PARTICLE_REPLACE )
			pEffect->ReplaceWith( pEffect->Collection()->m_pDef->GetCullReplacementDefinition() );
		if ( ( nActions & SOURCESHARP_NEW_PARTICLE_CHANGED ) &&
			 pEffect->m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
			ClientLeafSystem()->RenderableChanged( pEffect->m_hRenderHandle );
		if ( nActions & SOURCESHARP_NEW_PARTICLE_REMOVE )
		{
			snapshot[i] = NULL;
			pEffect->Remove();
		}
	}
}

void ShellNewParticles_RemoveAll()
{
	while ( s_Effects.Count() > 0 )
		s_Effects[0]->Remove();
}
