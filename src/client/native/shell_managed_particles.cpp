//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

//========= SourceSharp — the draw executor for the managed particle sims ====//
//
// plan_client_novalve C2 (lane c2-sims, the "our native files" row for the
// sims): a CParticleMgr simple-particle sim (the steam jet first) is C#'s
// (SourceSharp.Client.Effects), and this file is what stays native. One
// CManagedParticleEffect per (owner, channel), registered with the particle
// manager, whose three IParticleEffect calls each ask managed one question:
//
//   Update             IManagedParticlesClient::Emit: the frame's spawns, as
//                      ids, which become particles in the manager, and the
//                      bounds to hand SetBBox
//   SimulateParticles  IManagedParticlesClient::Simulate: which of the
//                      manager's particles (by id) are still alive
//   RenderParticles    IManagedParticlesClient::Draw: each particle's quad
//                      numbers, which particle_util.h's builders draw after
//                      the view-space transform and sort the manager owns
//
// It decides nothing: which material, which builder, what colour, how big and
// when a particle dies are all managed answers. The manager still decides
// when each call happens (culling, the first frame, drawn-last-frame), which
// is what keeps the managed sim's step identical to stock's.
//
// The particle the manager holds carries only the id managed gave it: the
// manager re-orders its list (bucket sort, incremental swaps), so the pairing
// is by id and never by position.
//
//=============================================================================//

#include "cbase.h"
#include "particle_util.h"
#include "materialsystem/imaterialvar.h"
#include "view.h"
#include "shell_particle_effect.h"
#include "shell_standalone_client.h"
#include "shell_clientsystem.h"
#include "shell_effect_ledger.h"
#include "particles_ez.h"	// DrawParticleSingletons, defined below now that particles_ez.cpp has left
#include "imanaged_areas_client.h"
#include "sourcesharp.h"
#include "shell_particle_mgr.h"	// the PMaterial table and the draw state (C7 particles phase 4)
#include "managed_convar_ref.h"
#include "shell_new_particles.h"	// C7 particles phase 3: the new-style effects' shell
#include "particle_follow_mask.h"
#include "shell_effect_registry.h"
#include "shell_particle_systems.h"
#include "tier1/utlobjectreference.h"
#include "shell_clientdll_refusals.h"
#include "viewrender.h"
#include "c_pixel_visibility.h"
#include "clienteffectprecachesystem.h"
#include "shell_cliententitylist.h"
#include "fx.h"
#include "view_scene.h"
#include "ss_bitblock.h"
#include "iclientmode.h"
#include "engine/ivdebugoverlay.h"
#include "fx_water.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// shell_client_orphans.cpp: the view model FOV squash CLocalSpaceEmitter applied (FLE_VIEWMODEL).
extern void FormatViewModelAttachment( Vector &vOrigin, bool bInverse );

using ss::BitBlockBits;
using ss::BitBlockSetBits;
using ss::kBitBlockHeaderBytes;

// Managed's declarations (C7 particles phase 4, ParticleConVars), read by
// name: DrawModel reads them.
static CManagedConVarRef r_DrawParticles( "r_drawparticles" );
static CManagedConVarRef cl_particle_show_bbox( "cl_particle_show_bbox" );
static CManagedConVarRef cl_particle_show_bbox_cost( "cl_particle_show_bbox_cost" );
static CManagedConVarRef cl_particleeffect_aabb_buffer( "cl_particleeffect_aabb_buffer" );

namespace
{

//-----------------------------------------------------------------------------
// The heatwave's quad: c_steamjet.cpp's RenderParticle_ColorSizePerturbNormal,
// with the normal managed drew (stock drew it here, per vertex, from rand()).
//-----------------------------------------------------------------------------
void RenderParticle_ColorSizeNormal( ParticleDraw *pDraw, const Vector &pos, const Vector &color,
                                     float alpha, float size, const Vector &vNorm )
{
	if ( alpha < 0.001f )
		return;

	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if ( !pBuilder )
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt( color.x * 254.9f );
	ubColor[1] = (unsigned char)RoundFloatToInt( color.y * 254.9f );
	ubColor[2] = (unsigned char)RoundFloatToInt( color.z * 254.9f );
	ubColor[3] = (unsigned char)RoundFloatToInt( alpha * 254.9f );

	pBuilder->Position3f( pos.x - size, pos.y - size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->Normal3fv( vNorm.Base() );
	pBuilder->TexCoord2f( 0, 0, 1.0f );
	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x - size, pos.y + size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->Normal3fv( vNorm.Base() );
	pBuilder->TexCoord2f( 0, 0, 0 );
	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + size, pos.y + size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->Normal3fv( vNorm.Base() );
	pBuilder->TexCoord2f( 0, 1.0f, 0 );
	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + size, pos.y - size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->Normal3fv( vNorm.Base() );
	pBuilder->TexCoord2f( 0, 1.0f, 1.0f );
	pBuilder->AdvanceVertex();
}

//-----------------------------------------------------------------------------
// Kind 4, a tracer's quad (lane c5-particles): four view-space corners managed
// computed (TracerGeometry, c_tracer.cpp's geometry), wound start-minus,
// start-plus, end-plus, end-minus with the texture run along the line, in one
// float colour.
//-----------------------------------------------------------------------------
void RenderTracerQuad( ParticleDraw *pDraw, const SourceSharpParticleQuad_t &q )
{
	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if ( !pBuilder )
		return;

	const float color[4] = { q.ColorXYZ[0], q.ColorXYZ[1], q.ColorXYZ[2], q.Alpha };
	const float *corners[4] = { q.PositionXYZ, q.NormalXYZ, q.Corner3XYZ, q.Corner2XYZ };
	const float u[4] = { 0.0f, 1.0f, 1.0f, 0.0f };
	const float v[4] = { 0.0f, 0.0f, 1.0f, 1.0f };
	for ( int i = 0; i < 4; i++ )
	{
		pBuilder->Position3f( corners[i][0], corners[i][1], corners[i][2] );
		pBuilder->TexCoord2f( 0, u[i], v[i] );
		pBuilder->Color4fv( color );
		pBuilder->AdvanceVertex();
	}
}

//-----------------------------------------------------------------------------
// Kind 5, a 3D particle's quad (lane c5-particles: Simple3DRules, stock's
// RenderParticle_ColorSizeAngles): managed hands the world-space corner, the
// face normal and the width and height edges (each scaled by the size); the
// corners are walked as stock walked them and taken into the view the effect
// draws in. Nothing is drawn below alpha 0.001.
//-----------------------------------------------------------------------------
void RenderAnglesQuad( ParticleDraw *pDraw, const SourceSharpParticleQuad_t &q, const VMatrix &mv )
{
	if ( q.Alpha < 0.001f )
		return;
	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if ( !pBuilder )
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt( q.ColorXYZ[0] * 254.9f );
	ubColor[1] = (unsigned char)RoundFloatToInt( q.ColorXYZ[1] * 254.9f );
	ubColor[2] = (unsigned char)RoundFloatToInt( q.ColorXYZ[2] * 254.9f );
	ubColor[3] = (unsigned char)RoundFloatToInt( q.Alpha * 254.9f );

	const Vector vWidth( q.Corner2XYZ[0], q.Corner2XYZ[1], q.Corner2XYZ[2] );
	const Vector vHeight( q.Corner3XYZ[0], q.Corner3XYZ[1], q.Corner3XYZ[2] );
	Vector vNorm;
	Vector3DMultiply( mv, Vector( q.NormalXYZ[0], q.NormalXYZ[1], q.NormalXYZ[2] ), vNorm );

	Vector corners[4];
	corners[0] = Vector( q.PositionXYZ[0], q.PositionXYZ[1], q.PositionXYZ[2] );
	corners[1] = corners[0] + vWidth;
	corners[2] = corners[1] + vHeight;
	corners[3] = corners[2] - vWidth;
	const CParticleSubTexture *pSub = pDraw->m_pSubTexture;
	const float u[4] = { pSub->m_tCoordMins[0], pSub->m_tCoordMins[0], pSub->m_tCoordMaxs[0], pSub->m_tCoordMaxs[0] };
	const float v[4] = { pSub->m_tCoordMaxs[1], pSub->m_tCoordMins[1], pSub->m_tCoordMins[1], pSub->m_tCoordMaxs[1] };
	for ( int i = 0; i < 4; i++ )
	{
		Vector vView;
		TransformParticle( mv, corners[i], vView );
		pBuilder->Position3f( vView.x, vView.y, vView.z );
		pBuilder->Color4ubv( ubColor );
		pBuilder->Normal3f( vNorm.x, vNorm.y, vNorm.z );
		pBuilder->TexCoord2f( 0, u[i], v[i] );
		pBuilder->AdvanceVertex();
	}
}

//-----------------------------------------------------------------------------
// Which view the particle manager is drawing, as a key: FNV-1a over its
// model-view matrix and CurrentViewID(). Managed re-reads the view
// (SourceSharp_ParticlesView) only when the key changes, so a frame costs one
// read per view drawn, not one per effect.
//-----------------------------------------------------------------------------
int32_t ParticlesViewKey()
{
	const VMatrix &mv = ShellParticleDraw_ModelView();
	const unsigned char *p = (const unsigned char *)&mv;
	uint32_t h = 2166136261u;
	for ( size_t i = 0; i < sizeof( VMatrix ); i++ )
		h = ( h ^ p[i] ) * 16777619u;
	h = ( h ^ (uint32_t)CurrentViewID() ) * 16777619u;
	return (int32_t)h;
}

} // namespace

class CManagedParticleEffect : public CShellParticleEffect
{
public:
	CManagedParticleEffect( uint32_t hOwner, int32_t nChannel )
		: m_hOwner( hOwner ), m_nChannel( nChannel ), m_hMaterial( INVALID_MATERIAL_HANDLE ),
		  m_bLit( false ), m_nSpaceAttachment( 0 ), m_nSpaceFlags( 0 ), m_flCullRadius( 0.0f ),
		  m_nFlags( kFlagsConstructed ), m_nActive( 0 )
	{
		SetIdentityMatrix( m_LocalSpace );
		SetIdentityMatrix( m_matRenderable );
		// CParticleEffectBinding's constructor's box, until managed commits one.
		m_Min.Init( -50, -50, -50 );
		m_Max.Init( 50, 50, 50 );
	}

	// The space managed put this executor in (ParticlesSetSpace): an entity
	// attachment's, as CLocalSpaceEmitter::Create took it, the view model's FOV
	// squash, and the draw before the view model. Which, and on what, is
	// managed's (PoolEmitters, for the temp entities' muzzle flashes).
	// 8 and 16 (lane c5-particles): a singleton, drawn not through the leaf system
	// but at DrawParticleSingletons' place, as particles_ez.cpp's singletons were;
	// 16 the 3D sky's call, 8 the world's.
	enum { kSpaceLocal = 1, kSpaceViewModel = 2, kSpaceBeforeViewModel = 4, kSpaceSingleton = 8, kSpaceSingletonSky = 16 };

	// CParticleEffectBinding's FLAGS_ this renderable reads from managed's
	// commit (particlemgr.h), and the seven its constructor sets.
	enum
	{
		kFlagCameraSpace = 1 << 7,
		kFlagDrawThruLeafSystem = 1 << 8,
		kFlagsConstructed = ( 1 << 3 ) | ( 1 << 11 ) | ( 1 << 2 ) | ( 1 << 4 ) | ( 1 << 7 ) | ( 1 << 8 ) | ( 1 << 10 ),
	};

	// Which DrawParticleSingletons call draws this executor, or -1 for none.
	int SingletonPass() const
	{
		return ( m_nSpaceFlags & kSpaceSingleton ) ? ( ( m_nSpaceFlags & kSpaceSingletonSky ) ? 1 : 0 ) : -1;
	}

	// particles_ez.cpp's DrawParticleSingletons body, for one executor.
	void DrawAsSingleton() { DrawModel( 1 ); }

	// The binding's half of the space is managed's (ClientParticleManager.SetSpace);
	// this keeps where it draws and answers the follow.
	void SetSpace( uint32_t hEntity, int32_t nAttachment, int32_t nFlags, SourceSharpParticleFollow_t *pFollow )
	{
		m_hSpace = CBaseHandle( (int)( hEntity & ENT_ENTRY_MASK ), (int)( hEntity >> NUM_SERIAL_NUM_SHIFT_BITS ) );
		m_nSpaceAttachment = nAttachment;
		m_nSpaceFlags = nFlags;
		if ( pFollow )
			memset( pFollow, 0, sizeof( *pFollow ) );
		if ( nFlags & kSpaceLocal )
			FollowSpace( pFollow );
	}

	// CLocalSpaceEmitter::SetupTransformMatrix, stock's: the attachment's
	// matrix is the particles' space, its origin the sort origin, and the
	// entity's bounding radius about it the world box. What it read goes to
	// managed, whose binding holds the box and the transform.
	void FollowSpace( SourceSharpParticleFollow_t *pOut )
	{
		IClientRenderable *pRenderable = SourceSharp_ClientRenderableFromHandle( m_hSpace );
		if ( !pRenderable )
			return;
		matrix3x4_t mat;
		SetIdentityMatrix( mat );
		pRenderable->GetAttachment( m_nSpaceAttachment, mat );
		Vector origin;
		MatrixGetColumn( mat, 3, origin );
		m_LocalSpace = mat;
		SetAbsOrigin( origin );
		if ( !pOut )
			return;
		pOut->Followed = 1;
		V_memcpy( pOut->Matrix, mat.Base(), sizeof( pOut->Matrix ) );
		VectorCopy( origin.Base(), pOut->OriginXYZ );
		IClientUnknown *pUnk = pRenderable->GetIClientUnknown();
		ICollideable *pCollide = pUnk ? pUnk->GetCollideable() : NULL;
		if ( pCollide )
		{
			Vector vecSize;
			VectorSubtract( pCollide->OBBMaxs(), pCollide->OBBMins(), vecSize );
			const float scale = vecSize.Length() * 0.5f;
			const Vector vScale( scale, scale, scale );
			pOut->HasBox = 1;
			VectorCopy( ( origin - vScale ).Base(), pOut->MinsXYZ );
			VectorCopy( ( origin + vScale ).Base(), pOut->MaxsXYZ );
		}
	}

	~CManagedParticleEffect()
	{
		if ( m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
			ClientLeafSystem()->RemoveRenderable( m_hRenderHandle );
	}

	// CManagedParticleEffect::Start: its material, its cull radius, and into the
	// leaf system (CParticleMgr::AddEffect's CreateRenderableHandle).
	bool Start( const char *pszMaterial, float flCullRadius )
	{
		m_hMaterial = ShellParticleMaterial_Find( pszMaterial );
		if ( !m_hMaterial )
			return false;
		m_flCullRadius = flCullRadius;
		ClientLeafSystem()->CreateRenderableHandle( this, false );
		return true;
	}

	// SetParticleCullRadius: the leaf system is told when it changed.
	void Configure( float flCullRadius )
	{
		if ( m_flCullRadius == flCullRadius )
			return;
		m_flCullRadius = flCullRadius;
		if ( m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
			ClientLeafSystem()->RenderableChanged( m_hRenderHandle );
	}

	// The lit sim's lights (the smoke stack's): base colour 0..1, then the
	// ambient and the directional light, each position, colour, intensity.
	// Managed lights each quad itself (SphereLighting, lane c5-particles); what
	// stays here is the one thing CParticleSphereRenderer did for the
	// renderer: hand the directional light to the particle manager before the
	// effect draws, for the materials that light on the GPU.
	void SetLights( const float *pLights )
	{
		m_bLit = true;
		m_Directional.m_vPos.Init( pLights[10], pLights[11], pLights[12] );
		m_Directional.m_vColor.Init( pLights[13], pLights[14], pLights[15] );
		m_Directional.m_flIntensity = pLights[16];
	}

	// The effect's material, for a managed question about one of its vars.
	IMaterial *Material() { return ShellParticleMaterial_Material( m_hMaterial ); }

	uint32_t Owner() const { return m_hOwner; }
	int32_t Channel() const { return m_nChannel; }
	int32_t SpaceFlags() const { return m_nSpaceFlags; }
	int32_t ActiveParticles() const { return m_nActive; }

	// This frame's read for managed's update (IManagedParticlesClient::Frame):
	// where it stands, its material's frame-buffer needs, and the follow, which
	// CManagedParticleEffect::Update ran inside the binding's update.
	void FillFrameIn( SourceSharpParticleFrameIn_t &in )
	{
		memset( &in, 0, sizeof( in ) );
		in.Owner = m_hOwner;
		in.Channel = m_nChannel;
		VectorCopy( GetAbsOrigin().Base(), in.OriginXYZ );
		in.AnglesPYR[0] = GetAbsAngles().x;
		in.AnglesPYR[1] = GetAbsAngles().y;
		in.AnglesPYR[2] = GetAbsAngles().z;
		IMaterial *pPage = PageMaterial();
		if ( pPage )
			in.MaterialFlags = ( pPage->NeedsPowerOfTwoFrameBufferTexture() ? 1 : 0 ) | ( pPage->NeedsFullFrameBufferTexture() ? 2 : 0 );
		if ( m_nSpaceFlags & kSpaceLocal )
		{
			SourceSharpParticleFollow_t follow;
			memset( &follow, 0, sizeof( follow ) );
			FollowSpace( &follow );
			in.Followed = follow.Followed;
			V_memcpy( in.Matrix, follow.Matrix, sizeof( in.Matrix ) );
			VectorCopy( follow.OriginXYZ, in.FollowOriginXYZ );
			in.HasBox = follow.HasBox;
			VectorCopy( follow.MinsXYZ, in.MinsXYZ );
			VectorCopy( follow.MaxsXYZ, in.MaxsXYZ );
		}
	}

	// Managed's binding after its update: what this renderable answers the
	// leaf system with, and whether the leaf system must re-place it.
	void Commit( const SourceSharpParticleCommit_t &c )
	{
		m_Min.Init( c.MinXYZ[0], c.MinXYZ[1], c.MinXYZ[2] );
		m_Max.Init( c.MaxXYZ[0], c.MaxXYZ[1], c.MaxXYZ[2] );
		m_nFlags = c.Flags;
		m_nActive = c.Active;
		if ( c.Changed && m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE )
			ClientLeafSystem()->RenderableChanged( m_hRenderHandle );
	}

// IClientRenderable, CParticleEffectBinding's answers.
public:
	virtual const Vector &GetRenderOrigin() OVERRIDE { return GetAbsOrigin(); }
	virtual const QAngle &GetRenderAngles() OVERRIDE { return vec3_angle; }

	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE
	{
		SetIdentityMatrix( m_matRenderable );
		PositionMatrix( GetRenderOrigin(), m_matRenderable );
		return m_matRenderable;
	}

	// The world box about the sort origin, grown by the cull radius.
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE
	{
		const Vector &vSortOrigin = GetAbsOrigin();
		mins = m_Min - vSortOrigin;
		mins.x -= m_flCullRadius; mins.y -= m_flCullRadius; mins.z -= m_flCullRadius;
		maxs = m_Max - vSortOrigin;
		maxs.x += m_flCullRadius; maxs.y += m_flCullRadius; maxs.z += m_flCullRadius;
	}

	virtual bool ShouldDraw() OVERRIDE { return ( m_nFlags & kFlagDrawThruLeafSystem ) != 0; }
	virtual bool IsTransparent() OVERRIDE { return true; }

	virtual int DrawModel( int flags ) OVERRIDE;

private:
	IMaterial *PageMaterial()
	{
		if ( !m_hMaterial )
			return NULL;
		ShellParticleMaterial_Material( m_hMaterial );	// RepairPMaterial, as AddParticle did
		return m_hMaterial->m_pGroup ? m_hMaterial->m_pGroup->m_pPageMaterial : NULL;
	}

	void RenderStart( VMatrix &tempModel, VMatrix &tempView );
	void RenderEnd( VMatrix &tempModel, VMatrix &tempView );
	void DrawQuads( IMaterial *pMaterial, float flFrameTime, const SourceSharpParticleQuad_t *pQuads, int nQuads, float *pKeys );

	uint32_t m_hOwner;
	int32_t m_nChannel;
	PMaterialHandle m_hMaterial;
	bool m_bLit;
	CParticleLightInfo m_Directional;
	CBaseHandle m_hSpace;
	int32_t m_nSpaceAttachment;
	int32_t m_nSpaceFlags;
	float m_flCullRadius;
	int32_t m_nFlags;
	int32_t m_nActive;
	Vector m_Min;
	Vector m_Max;
	matrix3x4_t m_LocalSpace;
	matrix3x4_t m_matRenderable;
	CUtlVector< uint8_t > m_DrawBuffer;
	CUtlVector< float > m_Keys;
	CUtlVector< uint8_t > m_KeyBuffer;
};

//-----------------------------------------------------------------------------
// CParticleEffectBinding::RenderStart: the particles draw in camera space, so
// the model and view go to identity for the draw (the view kept for the user
// clip planes), and the lit sim hands its light over (StartRender). The
// manager's model view, which the executor and ParticleSphereProxy read, was set
// before managed was asked (DrawModel).
//-----------------------------------------------------------------------------
void CManagedParticleEffect::RenderStart( VMatrix &tempModel, VMatrix &tempView )
{
	if ( m_nFlags & kFlagCameraSpace )
	{
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->GetMatrix( MATERIAL_VIEW, &tempView );
		pRenderContext->GetMatrix( MATERIAL_MODEL, &tempModel );
		pRenderContext->EnableUserClipTransformOverride( true );
		pRenderContext->UserClipTransform( tempView );
		pRenderContext->MatrixMode( MATERIAL_MODEL );
		pRenderContext->LoadIdentity();
		pRenderContext->MatrixMode( MATERIAL_VIEW );
		pRenderContext->LoadIdentity();
	}
	if ( m_bLit )
		ShellParticleDraw_SetLight( m_Directional );
}

void CManagedParticleEffect::RenderEnd( VMatrix &tempModel, VMatrix &tempView )
{
	if ( m_nFlags & kFlagCameraSpace )
	{
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->EnableUserClipTransformOverride( false );
		pRenderContext->MatrixMode( MATERIAL_MODEL );
		pRenderContext->LoadMatrix( tempModel );
		pRenderContext->MatrixMode( MATERIAL_VIEW );
		pRenderContext->LoadMatrix( tempView );
	}
}

//-----------------------------------------------------------------------------
// One material's draw (DrawMaterialParticles over the render iterator): the
// material bound with the particle manager as its proxy argument, the quads in
// the order managed listed them, a batch flushed every NUM_PARTICLES_PER_BATCH,
// and each quad's sort key written for managed's sort.
//-----------------------------------------------------------------------------
void CManagedParticleEffect::DrawQuads( IMaterial *pMaterial, float flFrameTime, const SourceSharpParticleQuad_t *pQuads,
                                        int nQuads, float *pKeys )
{
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->Bind( pMaterial, ShellParticleDraw_BindArg() );
	IMesh *pMesh = pRenderContext->GetDynamicMesh( true );
	CMeshBuilder builder;
	builder.Begin( pMesh, MATERIAL_QUADS, NUM_PARTICLES_PER_BATCH * 4 );
	ParticleDraw particleDraw;
	particleDraw.Init( &builder, pMaterial, flFrameTime );
	particleDraw.m_pSubTexture = m_hMaterial;
	ParticleDraw *pDraw = &particleDraw;
	const VMatrix &mv = ShellParticleDraw_ModelView();

	int nInBatch = 0;
	for ( int i = 0; i < nQuads; i++ )
	{
		const SourceSharpParticleQuad_t *pQuad = &pQuads[i];
		float flKey = 0.0f;
		if ( pQuad->Kind == 5 )
		{
			RenderAnglesQuad( pDraw, *pQuad, mv );
			flKey = pQuad->SortKey;
		}
		else if ( pQuad->Kind == 6 )
		{
			// The dust motes (DustVolumeSim, lane c5-particles): already in view
			// space, their alpha and size taken from its depth by managed;
			// stock's CDustEffect::RenderParticles drew them so.
			RenderParticle_Color255Size( pDraw, Vector( pQuad->PositionXYZ[0], pQuad->PositionXYZ[1], pQuad->PositionXYZ[2] ),
			                             Vector( pQuad->ColorXYZ[0], pQuad->ColorXYZ[1], pQuad->ColorXYZ[2] ),
			                             pQuad->Alpha, pQuad->Size );
			// Step 7a's gate: one per mote handed to the mesh. shell_effect_ledger.h.
			g_SourceSharpEffects.nDustMotes++;
			flKey = pQuad->SortKey;
		}
		else if ( pQuad->Kind == 4 )
		{
			if ( pQuad->Size > 0.0f )
				RenderTracerQuad( pDraw, *pQuad );
			flKey = pQuad->SortKey;
		}
		else if ( pQuad->Kind >= 0 )
		{
			Vector tPos;
			Vector vPos( pQuad->PositionXYZ[0], pQuad->PositionXYZ[1], pQuad->PositionXYZ[2] );
			if ( m_nSpaceFlags & kSpaceLocal )
			{
				// CLocalSpaceEmitter::RenderParticles: local to world, then the view model's squash.
				Vector vWorld;
				VectorTransform( vPos, m_LocalSpace, vWorld );
				if ( m_nSpaceFlags & kSpaceViewModel )
					FormatViewModelAttachment( vWorld, false );
				vPos = vWorld;
			}
			TransformParticle( mv, vPos, tPos );
			const Vector vColor( pQuad->ColorXYZ[0], pQuad->ColorXYZ[1], pQuad->ColorXYZ[2] );
			const float flAlpha = pQuad->FadeFar > 0.0f
				? pQuad->Alpha * GetAlphaDistanceFade( tPos, pQuad->FadeNear, pQuad->FadeFar )
				: pQuad->Alpha;

			if ( pQuad->Kind == 2 )
				RenderParticle_Color255SizeNormalAngle( pDraw, tPos, vColor, flAlpha, pQuad->Size, vec3_origin, pQuad->Roll );
			else if ( pQuad->Kind == 3 )
				RenderParticle_ColorSize( pDraw, tPos, vColor, flAlpha, pQuad->Size );
			else if ( pQuad->Kind == 1 )
				RenderParticle_ColorSizeNormal( pDraw, tPos, vColor, flAlpha, pQuad->Size,
				                                Vector( pQuad->NormalXYZ[0], pQuad->NormalXYZ[1], pQuad->NormalXYZ[2] ) );
			else
				RenderParticle_ColorSizeAngle( pDraw, tPos, vColor, flAlpha, pQuad->Size, pQuad->Roll );

			flKey = pQuad->FixedSort == 1 ? pQuad->SortKey : pQuad->FixedSort == 2 ? (float)(int)tPos.z : tPos.z;
		}
		pKeys[i] = flKey;

		// CParticleRenderIterator::TestFlushBatch, once per particle iterated.
		if ( ++nInBatch >= NUM_PARTICLES_PER_BATCH )
		{
			builder.End( false, true );
			builder.Begin( pMesh, MATERIAL_QUADS, NUM_PARTICLES_PER_BATCH * 4 );
			nInBatch = 0;
		}
	}
	builder.End( false, true );
}

//-----------------------------------------------------------------------------
// CParticleEffectBinding::DrawModel, carried out: managed decides whether it
// draws, its frame time, the frame-buffer updates, the wireframe pass and the
// debug box, and lists the quads in its binding's order; this draws them and
// hands back each one's sort key, from which managed sorts the list.
//-----------------------------------------------------------------------------
int CManagedParticleEffect::DrawModel( int flags )
{
	IManagedParticlesClient *pArea = ManagedParticlesClient();
	if ( !pArea )
		return 0;

	// The model view this draw's particles are taken into (RenderStart's):
	// the view in camera space, else the identity. Set before managed is asked,
	// because its view read (SourceSharp_ParticlesView) is keyed by it.
	if ( m_nFlags & kFlagCameraSpace )
	{
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->GetMatrix( MATERIAL_VIEW, &ShellParticleDraw_ModelView() );
	}
	else
	{
		ShellParticleDraw_ModelView().Identity();
	}

	SourceSharpParticleDrawIn_t in;
	memset( &in, 0, sizeof( in ) );
	in.Owner = m_hOwner;
	in.Channel = m_nChannel;
	in.Flags = flags;
	in.ClientModeDraws = g_pClientMode && g_pClientMode->ShouldDrawParticles() ? 1 : 0;
	in.DrawParticles = r_DrawParticles.GetInt() != 0 ? 1 : 0;
	in.Wireframe = ShouldDrawInWireFrameMode() ? 1 : 0;
	in.ShowBBox = cl_particle_show_bbox.GetBool() ? 1 : 0;
	in.ShowBBoxCost = cl_particle_show_bbox_cost.GetInt();
	in.ViewKey = ParticlesViewKey();
	in.FrameTime = gpGlobals->frametime;

	const int nHeader = kBitBlockHeaderBytes + (int)sizeof( SourceSharpParticleDrawOut_t );
	if ( m_DrawBuffer.Count() < nHeader + 64 * (int)sizeof( SourceSharpParticleQuad_t ) )
		m_DrawBuffer.SetCount( nHeader + 64 * (int)sizeof( SourceSharpParticleQuad_t ) );
	int nResult = pArea->DrawBegin( &in, m_DrawBuffer.Base(), m_DrawBuffer.Count() );
	if ( nResult > m_DrawBuffer.Count() )
	{
		// Managed needs more room: it answered the size before deciding
		// anything, so the second ask is the first draw decision.
		m_DrawBuffer.SetCount( nResult );
		nResult = pArea->DrawBegin( &in, m_DrawBuffer.Base(), m_DrawBuffer.Count() );
	}
	if ( nResult < nHeader || BitBlockBits( m_DrawBuffer.Base() ) / 8 + kBitBlockHeaderBytes > m_DrawBuffer.Count() )
		return 0;

	const SourceSharpParticleDrawOut_t *pOut = (const SourceSharpParticleDrawOut_t *)( m_DrawBuffer.Base() + kBitBlockHeaderBytes );
	if ( pOut->Result == 0 )
		return 0;
	if ( pOut->Result == 1 )
		return 1;

	const int nPayload = BitBlockBits( m_DrawBuffer.Base() ) / 8;
	const int nQuads = ( nPayload - (int)sizeof( SourceSharpParticleDrawOut_t ) ) / (int)sizeof( SourceSharpParticleQuad_t );
	const SourceSharpParticleQuad_t *pQuads = (const SourceSharpParticleQuad_t *)( pOut + 1 );

	VMatrix mTempModel, mTempView;
	RenderStart( mTempModel, mTempView );

	IMaterial *pPage = PageMaterial();
	if ( pOut->RefractUpdate )
		UpdateRefractTexture();
	if ( pOut->ScreenUpdate )
		UpdateScreenEffectTexture();

	m_Keys.SetCount( MAX( nQuads, 1 ) );
	m_KeyBuffer.SetCount( kBitBlockHeaderBytes + MAX( nQuads, 1 ) * (int)sizeof( float ) );
	const int nPasses = pOut->Wireframe ? 2 : 1;
	const float flFrameTime = pOut->FrameTime;
	const bool bBox = pOut->BBox != 0;
	const int r = pOut->BBoxR, g = pOut->BBoxG, b = pOut->BBoxB, nActive = pOut->Active;
	const Vector vMin( pOut->MinXYZ[0], pOut->MinXYZ[1], pOut->MinXYZ[2] );
	const Vector vMax( pOut->MaxXYZ[0], pOut->MaxXYZ[1], pOut->MaxXYZ[2] );
	for ( int nPass = 0; nPass < nPasses; nPass++ )
	{
		IMaterial *pMaterial = nPass == 0 ? pPage
			: materials->FindMaterial( "debug/debugparticlewireframe", TEXTURE_GROUP_OTHER );
		// The wireframe pass draws the same quads again (stock asked the sim to
		// render twice; the managed sims' draws are pure, docs/todo.md).
		DrawQuads( pMaterial, flFrameTime, pQuads, nQuads, m_Keys.Base() );

		// The keys back, one length-prefixed buffer: managed sorts from them.
		BitBlockSetBits( m_KeyBuffer.Base(), nQuads * (int)sizeof( float ) * 8 );
		V_memcpy( m_KeyBuffer.Base() + kBitBlockHeaderBytes, m_Keys.Base(), nQuads * sizeof( float ) );
		pArea->DrawEnd( m_hOwner, m_nChannel, m_KeyBuffer.Base(), m_KeyBuffer.Count() );
	}

	if ( bBox && debugoverlay )
	{
		const Vector center = ( vMin + vMax ) / 2;
		debugoverlay->AddBoxOverlay( center, vMin - center, vMax - center, QAngle( 0, 0, 0 ), r, g, b, 16, 0 );
		debugoverlay->AddTextOverlayRGB( center, 0, 0, r, g, b, 64, "managed-%d:(%d)", m_nChannel, nActive );
	}

	RenderEnd( mTempModel, mTempView );
	return 1;
}

//-----------------------------------------------------------------------------
// The live executors, one per (owner, channel), and the per-frame follow.
//-----------------------------------------------------------------------------
static CUtlVector< CManagedParticleEffect * > g_ManagedParticles;

static int ManagedParticles_Find( uint32_t hOwner, int32_t nChannel )
{
	for ( int i = 0; i < g_ManagedParticles.Count(); i++ )
	{
		if ( g_ManagedParticles[i]->Owner() == hOwner && g_ManagedParticles[i]->Channel() == nChannel )
			return i;
	}
	return -1;
}

// An executor released by any route (the follow sweep when its owner went, level
// shutdown, Destroy) is forgotten through the latch's callback.
static void ManagedParticles_Forget( CShellParticleEffect *pEffect )
{
	for ( int i = g_ManagedParticles.Count() - 1; i >= 0; i-- )
	{
		if ( g_ManagedParticles[i] == pEffect )
			g_ManagedParticles.Remove( i );
	}
}

int32_t SourceSharp_ParticlesCreate( uint32_t hOwner, int32_t nChannel, const char *pszMaterial,
                                     float flCullRadius, int32_t bAlwaysSimulate )
{
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );
	if ( !pOwner || !pszMaterial || ManagedParticles_Find( hOwner, nChannel ) >= 0 )
		return 0;

	CManagedParticleEffect *pEffect = new CManagedParticleEffect( hOwner, nChannel );
	pEffect->SetAbsOrigin( pOwner->GetAbsOrigin() );
	pEffect->SetAbsAngles( pOwner->GetAbsAngles() );
	if ( !pEffect->Start( pszMaterial, flCullRadius ) )
	{
		pEffect->Release();
		return 0;
	}

	pEffect->SetGoneCallback( ManagedParticles_Forget );
	g_ManagedParticles.AddToTail( pEffect );
	return 1;
}

// An executor for an effect no entity owns (lane c5-particles: the managed
// CParticleMgr's ownerless effects, ClientParticleEffects), placed once at a
// world point and keyed by the no-owner handle and the managed id. The follow
// sweep leaves it where it was put; managed releases it (ParticlesDestroy)
// when its effect is finished.
int32_t SourceSharp_ParticlesCreateAt( int32_t nId, const char *pszMaterial, const float *pOriginXYZ,
                                       float flCullRadius, int32_t bAlwaysSimulate )
{
	if ( !pszMaterial || !pOriginXYZ || ManagedParticles_Find( SOURCESHARP_PARTICLE_NO_OWNER, nId ) >= 0 )
		return 0;

	CManagedParticleEffect *pEffect = new CManagedParticleEffect( SOURCESHARP_PARTICLE_NO_OWNER, nId );
	pEffect->SetAbsOrigin( Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] ) );
	if ( !pEffect->Start( pszMaterial, flCullRadius ) )
	{
		pEffect->Release();
		return 0;
	}

	pEffect->SetGoneCallback( ManagedParticles_Forget );
	g_ManagedParticles.AddToTail( pEffect );
	return 1;
}

//-----------------------------------------------------------------------------
// particles_ez.cpp's DrawParticleSingletons, which viewrender.cpp calls at its
// place in the translucent passes (and the render plan's ParticleSingletons op,
// RENDER_OP_ParticleSingletons, at the same place). particles_ez.cpp's own
// singletons left with lane c5-particles: their one filler, FX_Dust, is C#'s
// DustPuff, whose executors managed put in the singleton space (8, and 16 for
// the 3D sky's call). Each is drawn here, as stock drew its singleton's
// binding; nothing else is.
//-----------------------------------------------------------------------------
void DrawParticleSingletons( bool bInSkybox )
{
	const int nPass = bInSkybox ? 1 : 0;
	for ( int i = 0; i < g_ManagedParticles.Count(); i++ )
	{
		if ( g_ManagedParticles[i]->SingletonPass() != nPass )
			continue;
		g_ManagedParticles[i]->DrawAsSingleton();
		g_SourceSharpEffects.nSingletonDraws++;
	}
}

// The executor's space, and the follow it read (an attachment's space), which
// managed's binding takes. 1 when the executor was found.
int32_t SourceSharp_ParticlesSetSpace( uint32_t hOwner, int32_t nChannel, uint32_t hEntity, int32_t nAttachment, int32_t nFlags,
                                       SourceSharpParticleFollow_t *pFollow )
{
	const int i = ManagedParticles_Find( hOwner, nChannel );
	if ( i < 0 )
		return 0;
	g_ManagedParticles[i]->SetSpace( hEntity, nAttachment, nFlags, pFollow );
	return 1;
}

//-----------------------------------------------------------------------------
// CParticleMgr::UpdateAllEffects' old-style half, which is managed's
// (ClientParticleManager.Frame): each live executor's read for the frame goes
// over in one length-prefixed buffer, and each binding's state comes back in
// another, which the executors answer the leaf system from.
//-----------------------------------------------------------------------------

void ShellParticles_Frame( float flFrameTime )
{
	IManagedParticlesClient *pArea = ManagedParticlesClient();
	if ( !pArea )
		return;

	// On the stack for the usual executor counts (a few dozen); the heap past it.
	CUtlVectorFixedGrowable< uint8_t, 16384 > in;
	CUtlVectorFixedGrowable< uint8_t, 4096 > out;
	const int nExecutors = g_ManagedParticles.Count();
	const int nIn = (int)sizeof( SourceSharpParticleFrameHeader_t ) + nExecutors * (int)sizeof( SourceSharpParticleFrameIn_t );
	in.SetCount( kBitBlockHeaderBytes + nIn );
	BitBlockSetBits( in.Base(), nIn * 8 );
	SourceSharpParticleFrameHeader_t *pHeader = (SourceSharpParticleFrameHeader_t *)( in.Base() + kBitBlockHeaderBytes );
	pHeader->Dt = flFrameTime;
	pHeader->CurTime = gpGlobals->curtime;
	pHeader->FrameTime = gpGlobals->frametime;
	pHeader->AabbBuffer = cl_particleeffect_aabb_buffer.GetFloat();
	VectorCopy( MainViewOrigin().Base(), pHeader->ViewOriginXYZ );
	SourceSharpParticleFrameIn_t *pIn = (SourceSharpParticleFrameIn_t *)( pHeader + 1 );
	for ( int i = 0; i < nExecutors; i++ )
		g_ManagedParticles[i]->FillFrameIn( pIn[i] );

	const int nOut = nExecutors * (int)sizeof( SourceSharpParticleCommit_t );
	out.SetCount( kBitBlockHeaderBytes + MAX( nOut, 4 ) );
	BitBlockSetBits( out.Base(), 0 );
	if ( pArea->Frame( in.Base(), in.Count(), out.Base(), out.Count() ) <= 0 )
		return;

	const int nBytes = MIN( BitBlockBits( out.Base() ) / 8, out.Count() - kBitBlockHeaderBytes );
	const SourceSharpParticleCommit_t *pCommits = (const SourceSharpParticleCommit_t *)( out.Base() + kBitBlockHeaderBytes );
	for ( int c = 0; c < nBytes / (int)sizeof( SourceSharpParticleCommit_t ); c++ )
	{
		const int i = ManagedParticles_Find( pCommits[c].Owner, pCommits[c].Channel );
		if ( i >= 0 )
			g_ManagedParticles[i]->Commit( pCommits[c] );
	}
}

// CParticleMgr::IncrementFrameCode and PostRender's old-style halves, managed's.
void ShellParticles_FrameCode()
{
	if ( IManagedParticlesClient *pArea = ManagedParticlesClient() )
		pArea->FrameCode();
}

void ShellParticles_PostRender()
{
	if ( IManagedParticlesClient *pArea = ManagedParticlesClient() )
		pArea->PostRender();
}

void SourceSharp_ParticlesConfigure( uint32_t hOwner, int32_t nChannel, float flCullRadius )
{
	const int i = ManagedParticles_Find( hOwner, nChannel );
	if ( i >= 0 )
		g_ManagedParticles[i]->Configure( flCullRadius );
}

void SourceSharp_ParticlesSetLights( uint32_t hOwner, int32_t nChannel, const float *pLights, int32_t nFloats )
{
	const int i = ManagedParticles_Find( hOwner, nChannel );
	if ( i >= 0 && pLights && nFloats >= 17 )
		g_ManagedParticles[i]->SetLights( pLights );
}

// One integer var of an executor's material, as CParticleSphereRenderer::Init
// read $USINGPIXELSHADER: 1 and the value when the var is there, else 0.
int32_t SourceSharp_ParticlesMaterialVarInt( uint32_t hOwner, int32_t nChannel, const char *pszVar, int32_t *pValue )
{
	const int i = ManagedParticles_Find( hOwner, nChannel );
	IMaterial *pMaterial = i >= 0 ? g_ManagedParticles[i]->Material() : NULL;
	if ( !pMaterial || !pszVar || !pValue )
		return 0;
	bool bFound = false;
	IMaterialVar *pVar = pMaterial->FindVar( pszVar, &bFound, false );
	if ( !bFound || !pVar )
		return 0;
	*pValue = pVar->GetIntValue();
	return 1;
}

// A world trace that answers the hit PLANE as well as the fraction: the smoke
// volume's six-wall fade reads plane.dist, which the endpoint (pulled back by
// DIST_EPSILON) does not give back. UTIL_TraceLine with no pass entity and
// COLLISION_GROUP_NONE, as c_func_smokevolume.cpp's WorldTraceLine. Out:
// fraction, normal xyz, dist. The count written, or 0.
int32_t SourceSharp_ParticlesTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask, float *pOut, int32_t nOut )
{
	if ( !pStartXYZ || !pEndXYZ || !pOut || nOut < 5 )
		return 0;
	trace_t tr;
	UTIL_TraceLine( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ), Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                (unsigned int)nMask, NULL, COLLISION_GROUP_NONE, &tr );
	pOut[0] = tr.fraction;
	pOut[1] = tr.plane.normal.x;
	pOut[2] = tr.plane.normal.y;
	pOut[3] = tr.plane.normal.z;
	pOut[4] = tr.plane.dist;
	return 5;
}

void SourceSharp_ParticlesDestroy( uint32_t hOwner, int32_t nChannel )
{
	const int i = ManagedParticles_Find( hOwner, nChannel );
	if ( i >= 0 )
		g_ManagedParticles[i]->Release();
}

// The effect sits where its owner is, every frame: stock's effects were the
// entity, and a physics_cannister drags a steam jet around as it tumbles.
static void ManagedParticles_Follow()
{
	for ( int i = g_ManagedParticles.Count() - 1; i >= 0; i-- )
	{
		CManagedParticleEffect *pEffect = g_ManagedParticles[i];

		// An ownerless effect stays where it was put.
		if ( pEffect->Owner() == SOURCESHARP_PARTICLE_NO_OWNER )
			continue;

		C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( pEffect->Owner() );
		if ( !pOwner )
		{
			pEffect->Release();
			continue;
		}
		pEffect->SetAbsOrigin( pOwner->GetAbsOrigin() );
		pEffect->SetAbsAngles( pOwner->GetAbsAngles() );
	}
}

//-----------------------------------------------------------------------------
// cs_cl_effect_kinds: every live executor (the managed trails, steam jets,
// smoke stacks and smoke volumes, one per channel), where it is drawn this
// frame and how many particles its binding is simulating. Instrumentation for
// the live gates (the follow and the render), moved here from
// shell_trail_render.cpp when the trails became executor clients (lane
// c2-sims); it goes with the executor.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_effect_kinds,
             "every live managed particle executor (trails, steam jets, smoke stacks, smoke volumes): "
             "where it is drawn this frame and how many particles its binding is simulating" )
{
	for ( int i = 0; i < g_ManagedParticles.Count(); i++ )
	{
		CManagedParticleEffect *pEffect = g_ManagedParticles[i];
		const Vector vOrigin = pEffect->GetAbsOrigin();
		Msg( "[SourceSharp] effect kind: managed-%d at (%.1f %.1f %.1f) particles %d owner %u space %d\n",
		     pEffect->Channel(), vOrigin.x, vOrigin.y, vOrigin.z,
		     pEffect->ActiveParticles(), pEffect->Owner(), pEffect->SpaceFlags() );
	}

	Msg( "[SourceSharp] effect kinds: %d executor(s)\n", g_ManagedParticles.Count() );
}

class CSourceSharpManagedParticlesSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpManagedParticlesSystem() : CClientSystemPerFrame( "CSourceSharpManagedParticlesSystem" ) {}

	virtual void PreRender() OVERRIDE { ManagedParticles_Follow(); }

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		while ( g_ManagedParticles.Count() )
		{
			const int nBefore = g_ManagedParticles.Count();
			g_ManagedParticles[0]->Release();
			if ( g_ManagedParticles.Count() >= nBefore )
				g_ManagedParticles.Remove( 0 );
		}
	}
};

static CSourceSharpManagedParticlesSystem g_ManagedParticlesSystem;

CClientSystem *ClientSystem_ManagedParticles() { return &g_ManagedParticlesSystem; }

//=============================================================================//
// THE NEW-STYLE PARTICLE SYSTEMS (lane c5-particles)
//
// particles.a and dmxloader.a stay, bound like vphysics (user ruling,
// 2026-10-04, plan_client_novalve §3.4). This half of the file is the client's
// side of that binding: the new-style effects (CShellNewParticleEffect) the
// client starts, whose collections the library owns, and what the dev API reads about them.
//
// TWO KINDS OF INSTANCE ARE STARTED HERE, and both are kept in one table:
//
//   owned     a named system a managed entity started (SourceSharp_Particle-
//             SystemStart: ss_firesmoke, ss_entityflame). Held by a CSmartPtr,
//             its control points kept at the owner by the follow sweep, and
//             stopped when the owner stops it or goes.
//   one-shot  a system an impact started at a world point (shell_impact_
//             effects.cpp's DispatchNewParticleImpact). Not held: the manager
//             removes it when it finishes, exactly as stock's local CSmartPtr
//             let it. This table only WATCHES it, through a CUtlReference that
//             the effect's destructor clears.
//
// Every instance is watched by a weak reference, so a stopped system burning
// out is still listed until the manager deletes it. That is what
// /client/{slot}/particles reports (plan_client_novalve §4.4).
//
// THE FLAME'S PARTICLE SYSTEM moved here from shell_sprite_render.cpp, where it
// sat behind a banner because a new .cpp once needed a line in a .vpc. Its
// comments came with it. It joined this file rather than a new one because a
// new TU would raise the census's incdir-forbidden-tus ratchet; both halves
// are the particles binding. The two hooks it needs from a client system,
// PreRender and LevelShutdownPostEntity, are still called from the sprite
// system's own, in the same place, so no system's order changed.
//=============================================================================//

//=============================================================================//
// THE FLAME'S PARTICLE SYSTEM.
//
// ss_firesmoke is a named particle system and nothing else. Stock's C_FireSmoke
// had already been worn down to that by the time this port reached it -- every
// one of its update methods is an empty body, and Start() calls
// ParticleProp()->Create() and returns -- so what moves to C# is the DECISION
// (which of the eight systems, and where) and what stays is the one call.
//
// WHY THE EFFECT IS OWNERLESS. Stock passes the entity, and CParticleProperty
// binds the system's control point to it so the flame follows. ParticleProp()
// is a CBaseEntity member; C_StandaloneEntity is an IClientEntity on the
// parallel hierarchy, has no such property, and cannot be given one without
// becoming a C_BaseEntity again -- which is the whole thing this port is
// undoing. So the system is created with a NULL owner and its control point is
// driven from the owner's origin by the sweep below, which is the same shape
// the sprite trails use and for a related reason.
//=============================================================================//

struct SourceSharpParticleSystem_t
{
	uint32_t                        hOwner;
	CSmartPtr< CShellNewParticleEffect > pEffect;

	// Which control points the sweep below keeps at the owner, as a bitmask.
	//
	// One is the DEFAULT and is not a placeholder: control point 0 alone is what
	// this file fed before the flame arrived, and it is right for all eight of
	// fire_01.pcf's env_fire_* systems, whose highest referenced control point is
	// 0. burning_character is the first system here that reads control point 1 --
	// its "Position on Model Random" initializer names it -- and an unfed control
	// point is the world ORIGIN, so a flame whose CP1 nobody wrote emits at the
	// centre of the map. See SourceSharp_ParticleSystemFollowPoints.
	int32_t                         nFollowMask;
};

// The third hand-rolled owner lookup; it is ShellEffects_Find too.
static CUtlVector< SourceSharpParticleSystem_t > g_ParticleSystems;

//-----------------------------------------------------------------------------
// Every instance started here, watched. Heap entries: a CUtlReference links
// itself into its object's list, so it must not be moved by a vector's growth.
//-----------------------------------------------------------------------------
struct WatchedParticleSystem_t
{
	CUtlReference< CShellNewParticleEffect > ref;
	uint32_t                            hOwner;
	int32_t                             nKind;
	float                               flStarted;
};

static CUtlVector< WatchedParticleSystem_t * > g_WatchedParticles;

// The entries whose effect the manager has deleted.
static void ReapWatchedParticleSystems()
{
	for ( int i = g_WatchedParticles.Count() - 1; i >= 0; i-- )
	{
		if ( g_WatchedParticles[i]->ref() == NULL )
		{
			delete g_WatchedParticles[i];
			g_WatchedParticles.Remove( i );
		}
	}
}

// Reaps first, so the table is bounded by the live effects even when nobody
// reads it.
static void WatchParticleSystem( CShellNewParticleEffect *pEffect, uint32_t hOwner, int32_t nKind )
{
	ReapWatchedParticleSystems();

	WatchedParticleSystem_t *pWatch = new WatchedParticleSystem_t;
	pWatch->ref.Set( pEffect );
	pWatch->hOwner = hOwner;
	pWatch->nKind = nKind;
	pWatch->flStarted = gpGlobals->curtime;
	g_WatchedParticles.AddToTail( pWatch );
}

// The library's Create, for an owned system, watched for the route. A refused
// name (no definition) is not watched: Start refuses it to managed.
static CSmartPtr< CShellNewParticleEffect > CreateWatched( uint32_t hOwner, const char *pszName )
{
	CSmartPtr< CShellNewParticleEffect > pEffect = CShellNewParticleEffect::Create( pszName, "SourceSharpFlame" );
	if ( pEffect.IsValid() && pEffect->IsValid() )
		WatchParticleSystem( pEffect.GetObject(), hOwner, SOURCESHARP_PARTICLE_SYSTEM_OWNED );
	return pEffect;
}

//-----------------------------------------------------------------------------
// Lets one entity's system finish, and forgets it.
//
// StopEmission rather than a release: the particles already in the air are a
// second or two of fire that should burn out rather than blink away, which is
// what stock's ~C_FireSmoke asks for with its own three arguments.
//-----------------------------------------------------------------------------
static void ReleaseParticleSystemAt( int nIndex )
{
	CShellNewParticleEffect *pEffect = g_ParticleSystems[ nIndex ].pEffect.GetObject();

	if ( pEffect )
		pEffect->StopEmission( false, false, true );

	g_ParticleSystems.Remove( nIndex );
}

//-----------------------------------------------------------------------------
// Starts a named particle system for a managed entity.
//
// One at a time per entity: asking again replaces whatever was running, which
// is what a flame that changed size would want and what stock's Start() gets
// for free by only ever being called once.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ParticleSystemStart( uint32_t hOwner, const char *pszName )
{
	if ( !pszName || !pszName[0] )
		return 0;

	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );

	if ( !pOwner )
		return 0;

	int nExisting = ShellEffects_Find( g_ParticleSystems, hOwner );

	if ( nExisting >= 0 )
		ReleaseParticleSystemAt( nExisting );

	CSmartPtr< CShellNewParticleEffect > pEffect = CreateWatched( hOwner, pszName );

	// A name no .pcf defines comes back as an effect with no definition behind
	// it, which draws nothing and says nothing. Refused here so the managed
	// side is told once, rather than one silent orphan accumulating per flame.
	if ( !pEffect.IsValid() || !pEffect->IsValid() )
		return 0;

	pEffect->SetControlPoint( 0, pOwner->GetAbsOrigin() );
	pEffect->SetControlPointOrientation( 0, Vector( 1, 0, 0 ), Vector( 0, 1, 0 ), Vector( 0, 0, 1 ) );
	pEffect->SetSortOrigin( pOwner->GetAbsOrigin() );

	SourceSharpParticleSystem_t entry;
	entry.hOwner      = hOwner;
	entry.pEffect     = pEffect;
	entry.nFollowMask = kSourceSharpParticleFollowDefault;
	g_ParticleSystems.AddToTail( entry );

	return 1;
}

//-----------------------------------------------------------------------------
// Which control points of a running system stay at its owner.
//
// A SEPARATE CALL rather than an argument to Start, because Start's signature is
// an ABI field six other lanes are appending beside and widening one in the
// middle of that struct moves everything after it. This appends instead, and
// the default it overrides is the behaviour every existing caller already has.
//
// WHY ANY OF IT IS NEEDED. A particle system's operators address the world
// through numbered control points, and an operator whose point was never
// written reads (0,0,0). Read out of the .pcf files rather than assumed:
//
//   fire_01.pcf   env_fire_{tiny,small,medium,large}[_smoke]  highest CP = 0
//   burning_fx.pcf  burning_character and its four children   highest CP = 1
//
// burning_character's "Position on Model Random" initializer names control
// point 1, and asks the particle query for points on the MODEL bound there.
// Nothing can be bound: SetControlPointEntity takes a CBaseEntity and every
// entity on this hierarchy is not one. CParticleSystemQuery::
// GetRandomPointsOnControllingObjectHitBox then takes its `if ( !bSucesss )`
// fallback and answers m_ControlPoints[1].m_Position for every point -- so the
// flame draws at the map origin unless that point is fed.
//
// Returns 1 if the entity had a system to configure. A 0 means the flame asked
// before it started one, which is a caller bug rather than a missing feature.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ParticleSystemFollowPoints( uint32_t hOwner, int32_t nPointMask )
{
	int nIndex = ShellEffects_Find( g_ParticleSystems, hOwner );

	if ( nIndex < 0 )
		return 0;

	// Point 0 is always fed. It is where the system itself is placed at
	// creation, and a mask that cleared it would leave the effect stranded at
	// wherever it was born -- which is the bug this call exists to prevent,
	// inverted.
	g_ParticleSystems[ nIndex ].nFollowMask =
		SourceSharpParticleFollow_Normalise( nPointMask );

	return 1;
}

//-----------------------------------------------------------------------------
// The flame goes out when its entity does.
//-----------------------------------------------------------------------------
void SourceSharp_ParticleSystemStop( uint32_t hOwner )
{
	int nIndex = ShellEffects_Find( g_ParticleSystems, hOwner );

	if ( nIndex >= 0 )
		ReleaseParticleSystemAt( nIndex );
}

//-----------------------------------------------------------------------------
// A one-shot an impact started: watched, not held.
//-----------------------------------------------------------------------------
void ShellParticles_WatchOneShot( CShellNewParticleEffect *pEffect )
{
	if ( pEffect )
		WatchParticleSystem( pEffect, SOURCESHARP_PARTICLE_NO_OWNER, SOURCESHARP_PARTICLE_SYSTEM_ONE_SHOT );
}

//-----------------------------------------------------------------------------
// Every managed particle system, put back where its entity is.
//
// The trails' sweep, for the trails' reason: an entity that only MOVED delivers
// no data change at all, because its origin is a real prop outside the blob. A
// system fed only on update would emit at the place its flame was created and
// stay there while the flame walked off.
//
// Backwards, so that removing an entry mid-walk cannot make the sweep skip its
// neighbour.
//-----------------------------------------------------------------------------
void ShellParticles_PreRender()
{
	for ( int i = g_ParticleSystems.Count() - 1; i >= 0; i-- )
	{
		CShellNewParticleEffect *pEffect = g_ParticleSystems[i].pEffect.GetObject();

		if ( !pEffect )
		{
			g_ParticleSystems.Remove( i );
			continue;
		}

		C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( g_ParticleSystems[i].hOwner );

		// The entity has gone without this side being told -- it left the PVS
		// for good, or the level is coming down. Let the system finish rather
		// than stranding it emitting at a dead entity's last position.
		if ( !pOwner )
		{
			ReleaseParticleSystemAt( i );
			continue;
		}

		// EVERY point the owner asked for, not only point 0. They all get the
		// same position, which is what stock's C_EntityFlame does too --
		// SetControlPointEntity( 0, pEntity ) and ( 1, pEntity ), one entity,
		// twice -- because the second point is there to say WHAT is burning
		// rather than where a second thing is.
		const Vector vecOrigin = pOwner->GetAbsOrigin();
		const int32_t nMask = g_ParticleSystems[i].nFollowMask;

		for ( int nPoint = 0; nPoint <= kSourceSharpParticleMaxFollowPoint; nPoint++ )
		{
			if ( SourceSharpParticleFollow_Wants( nMask, nPoint ) )
				pEffect->SetControlPoint( nPoint, vecOrigin );
		}
		pEffect->SetSortOrigin( vecOrigin );	// stock UpdateControlPoint's last line: the leaf system culls by it
	}
}

//-----------------------------------------------------------------------------
// The systems are stopped rather than merely forgotten. Unlike a renderer, a
// new-style effect is a live simulation the particle manager holds a
// reference to, and a level change is not a moment to let one burn out
// gracefully.
//-----------------------------------------------------------------------------
void ShellParticles_LevelShutdown()
{
	for ( int i = g_ParticleSystems.Count() - 1; i >= 0; i-- )
		if ( CShellNewParticleEffect *pEffect = g_ParticleSystems[i].pEffect.GetObject() )
			pEffect->StopEmission( false, true, true );

	g_ParticleSystems.RemoveAll();
}

//-----------------------------------------------------------------------------
// The dev API's read (/client/{slot}/particles): one watched instance by index,
// the total the return. An index past the end writes nothing.
//-----------------------------------------------------------------------------
static int CountParticles( CParticleCollection *pCollection )
{
	if ( !pCollection )
		return 0;

	int nTotal = pCollection->m_nActiveParticles;
	for ( CParticleCollection *pChild = pCollection->m_Children.m_pHead; pChild; pChild = pChild->m_pNext )
		nTotal += CountParticles( pChild );
	return nTotal;
}

int32_t SourceSharp_ParticleSystemsRead( int32_t nIndex, SourceSharpParticleSystemInfo_t *pOut, char *pszName, int32_t nNameMax )
{
	ReapWatchedParticleSystems();

	const int32_t nCount = g_WatchedParticles.Count();
	if ( nIndex < 0 || nIndex >= nCount || !pOut )
		return nCount;

	const WatchedParticleSystem_t *pWatch = g_WatchedParticles[ nIndex ];
	CShellNewParticleEffect *pEffect = pWatch->ref();

	memset( pOut, 0, sizeof( *pOut ) );
	pOut->Kind = pWatch->nKind;
	pOut->Owner = pWatch->hOwner;
	pOut->Age = gpGlobals->curtime - pWatch->flStarted;

	// Released: an owned system no row of the owned table holds any more.
	const int nOwned = ShellEffects_Find( g_ParticleSystems, pWatch->hOwner );
	const bool bHeld = nOwned >= 0 && g_ParticleSystems[ nOwned ].pEffect.GetObject() == pEffect;
	pOut->Stopped = ( pWatch->nKind == SOURCESHARP_PARTICLE_SYSTEM_OWNED && !bHeld ) ? 1 : 0;
	pOut->FollowMask = bHeld ? g_ParticleSystems[ nOwned ].nFollowMask : 0;

	if ( pEffect )
	{
		pOut->Particles = CountParticles( pEffect->Collection() );
		const Vector &cp0 = pEffect->Collection()->m_ControlPoints[0].m_Position;
		pOut->ControlPoint0XYZ[0] = cp0.x;
		pOut->ControlPoint0XYZ[1] = cp0.y;
		pOut->ControlPoint0XYZ[2] = cp0.z;
		const Vector &cp1 = pEffect->Collection()->m_ControlPoints[1].m_Position;
		pOut->ControlPoint1XYZ[0] = cp1.x;
		pOut->ControlPoint1XYZ[1] = cp1.y;
		pOut->ControlPoint1XYZ[2] = cp1.z;
		const Vector &vSort = pEffect->GetSortOrigin();
		pOut->SortOriginXYZ[0] = vSort.x;
		pOut->SortOriginXYZ[1] = vSort.y;
		pOut->SortOriginXYZ[2] = vSort.z;
	}

	if ( pszName && nNameMax > 0 )
		Q_strncpy( pszName, pEffect ? pEffect->GetEffectName() : "", nNameMax );

	return nCount;
}

//=============================================================================//
// THE PARTICLE QUERY (lane c5-particles, plan_client_novalve §3.4)
//
// IParticleSystemQuery is what particles.a calls back into the game for. It
// was particlesystemquery.cpp's CParticleSystemQuery; it is a thunk now, and
// what each answer IS comes from managed (SourceSharp.Client.Effects.
// ParticleSystemQuery, replayed against the stock recording
// Fixtures/particlequery-stock.tsv.gz). This carries the answers out.
//
// THE LIBRARY CALLS FIVE METHODS (C0 probe 1: every one of the 245 loaded
// definitions started in turn): TraceLine, GetLightingAtPoint, the two
// controlling-object hit-box methods and GetCollisionGroupFromName. Each asks
// managed through IManagedParticlesClient.
//
// THE OTHER SIX ARE REFUSED: never called by any definition, so each one
// warns and counts on the client's refusal tally (/client/{slot}/absences, a
// row there is a failing gate) and answers as the interface's own inline
// default does (GetPixelVisibility, which has none, answers stock's server
// arm, 0; SetUpLightingEnvironment does nothing).
//
// UNANSWERED: a call that finds no managed area, or a managed answer that
// failed. The collision-group names are read twice: once while the client's
// Init loads the definitions, which is before the managed runtime exists
// (14 calls at a9d8f9fd, measured by the recording probe's clock), and again
// at the first level's full read (ParticleReloadPolicy: the first level
// always re-reads), which replaces every definition before any effect can
// exist. The first read is answered with the interface's own default,
// COLLISION_GROUP_NONE, and counted here; the route reports the count.
//=============================================================================//

class CShellParticleSystemQuery : public CBaseAppSystem< IParticleSystemQuery >
{
public:
	enum { kCollisionGroup, kLighting, kTrace, kHitBoxPoints, kHitBoxInfo, kMethods };

	virtual void GetLightingAtPoint( const Vector &vecOrigin, Color &tint ) OVERRIDE
	{
		IManagedParticlesClient *pArea = ManagedParticlesClient();
		uint8_t rgba[4];
		// Stock's s_LightMutex: the library simulates its systems on several
		// threads at once (r_threaded_particles), and the engine's light
		// lookup the answer is made from is not safe to enter twice.
		AUTO_LOCK( m_LightMutex );
		if ( pArea && pArea->QueryLighting( vecOrigin.Base(), rgba ) == 1 )
			tint.SetColor( rgba[0], rgba[1], rgba[2], rgba[3] );
		else
			Unanswered_Add( kLighting );
	}

	virtual void TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
	                        const IHandleEntity *ignore, int collisionGroup, CBaseTrace *ptr ) OVERRIDE
	{
		IManagedParticlesClient *pArea = ManagedParticlesClient();
		const int32_t nAnswer = pArea ? pArea->QueryTrace( vecAbsStart.Base(), vecAbsEnd.Base(), mask,
		                                                   collisionGroup, ignore ? 1 : 0 ) : -1;
		if ( nAnswer == 1 )
		{
			trace_t tempTrace;
			UTIL_TraceLine( vecAbsStart, vecAbsEnd, mask, ignore, collisionGroup, &tempTrace );
			memcpy( ptr, &tempTrace, sizeof( CBaseTrace ) );
			return;
		}
		if ( nAnswer < 0 )
			Unanswered_Add( kTrace );
		ptr->startsolid = 0;
		ptr->fraction = 1.0;
	}

	virtual int GetCollisionGroupFromName( const char *pszCollisionGroupName ) OVERRIDE
	{
		IManagedParticlesClient *pArea = ManagedParticlesClient();
		const int32_t nGroup = ( pArea && pszCollisionGroupName ) ? pArea->QueryCollisionGroup( pszCollisionGroupName ) : -1;
		if ( nGroup >= 0 )
			return nGroup;
		Unanswered_Add( kCollisionGroup );
		return COLLISION_GROUP_NONE;
	}

	virtual void GetRandomPointsOnControllingObjectHitBox( CParticleCollection *pParticles, int nControlPointNumber,
		int nNumPtsOut, float flBBoxScale, int nNumTrysToGetAPointInsideTheModel, Vector *pPntsOut,
		Vector vecDirectionalBias, Vector *pHitBoxRelativeCoordOut, int *pHitBoxIndexOut ) OVERRIDE
	{
		const CParticleControlPoint &point = pParticles->m_ControlPoints[ nControlPointNumber ];
		IManagedParticlesClient *pArea = ManagedParticlesClient();
		const int32_t nAnswer = pArea
			? pArea->QueryHitBoxPoints( nControlPointNumber, point.m_Position.Base(), point.m_pObject ? 1 : 0, nNumPtsOut,
			                            pPntsOut ? pPntsOut->Base() : NULL, pHitBoxIndexOut,
			                            pHitBoxRelativeCoordOut ? pHitBoxRelativeCoordOut->Base() : NULL )
			: -1;
		if ( nAnswer == nNumPtsOut )
			return;

		// The stock function's own "fallback if anything goes wrong": every
		// point at the control point.
		Unanswered_Add( kHitBoxPoints );
		for ( int i = 0; i < nNumPtsOut; i++ )
		{
			pPntsOut[i] = point.m_Position;
			if ( pHitBoxIndexOut )
				pHitBoxIndexOut[i] = 0;
			if ( pHitBoxRelativeCoordOut )
				pHitBoxRelativeCoordOut[i].Init();
		}
	}

	virtual int GetControllingObjectHitBoxInfo( CParticleCollection *pParticles, int nControlPointNumber,
		int nBufSize, ModelHitBoxInfo_t *pHitBoxOutputBuffer ) OVERRIDE
	{
		IManagedParticlesClient *pArea = ManagedParticlesClient();
		const int32_t nAnswer = pArea
			? pArea->QueryHitBoxInfo( nControlPointNumber,
			                          pParticles->m_ControlPoints[ nControlPointNumber ].m_pObject ? 1 : 0, nBufSize )
			: -1;
		if ( nAnswer >= 0 && nAnswer <= nBufSize )
			return nAnswer;
		Unanswered_Add( kHitBoxInfo );
		return 0;
	}

	// -- the six refused ----------------------------------------------------
	virtual bool MovePointInsideControllingObject( CParticleCollection *, void *, Vector * ) OVERRIDE
	{
		ShellClientDll_RefuseExternal( "IParticleSystemQuery::MovePointInsideControllingObject" );
		return true;
	}

	virtual bool IsPointInControllingObjectHitBox( CParticleCollection *, int, Vector, bool ) OVERRIDE
	{
		ShellClientDll_RefuseExternal( "IParticleSystemQuery::IsPointInControllingObjectHitBox" );
		return true;
	}

	virtual Vector GetLocalPlayerPos() OVERRIDE
	{
		ShellClientDll_RefuseExternal( "IParticleSystemQuery::GetLocalPlayerPos" );
		return vec3_origin;
	}

	virtual void GetLocalPlayerEyeVectors( Vector *pForward, Vector *pRight, Vector *pUp ) OVERRIDE
	{
		ShellClientDll_RefuseExternal( "IParticleSystemQuery::GetLocalPlayerEyeVectors" );
		*pForward = vec3_origin;
		*pRight = vec3_origin;
		*pUp = vec3_origin;
	}

	virtual float GetPixelVisibility( int *, const Vector &, float ) OVERRIDE
	{
		ShellClientDll_RefuseExternal( "IParticleSystemQuery::GetPixelVisibility" );
		return 0.0f;
	}

	virtual void SetUpLightingEnvironment( const Vector & ) OVERRIDE
	{
		ShellClientDll_RefuseExternal( "IParticleSystemQuery::SetUpLightingEnvironment" );
	}

	int32_t Unanswered( int nMethod ) const
	{
		return ( nMethod >= 0 && nMethod < kMethods ) ? m_nUnanswered[ nMethod ] : 0;
	}

private:
	// Interlocked: the library's worker threads call these concurrently.
	void Unanswered_Add( int nMethod ) { ThreadInterlockedIncrement( &m_nUnanswered[ nMethod ] ); }

	CThreadFastMutex m_LightMutex;
	volatile int32 m_nUnanswered[ kMethods ];
};

static CShellParticleSystemQuery s_ShellParticleQuery;

// The global CParticleMgr::Init hands particles.a (particlemgr.cpp names it
// extern), and the interface tools may ask for by name: both were
// particlesystemquery.cpp's, which left the client with this thunk's arrival
// (lane c5-particles). No engine binary names VParticleSystemQuery001
// (strings over the 243750 install's bin/linux64); the registration is kept so
// the client's exported interface set (K1) does not change.
IParticleSystemQuery *g_pParticleSystemQuery = &s_ShellParticleQuery;
EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CShellParticleSystemQuery, IParticleSystemQuery, PARTICLE_SYSTEM_QUERY_INTERFACE_VERSION, s_ShellParticleQuery );

// The particles route's readout: the unanswered calls, per method, in
// CShellParticleSystemQuery's order (collision group, lighting, trace, hit-box
// points, hit-box info). The count written.
int32_t SourceSharp_ParticleQueryUnanswered( int32_t *pOut, int32_t nMax )
{
	int32_t n = 0;
	for ( ; pOut && n < nMax && n < CShellParticleSystemQuery::kMethods; n++ )
		pOut[n] = s_ShellParticleQuery.Unanswered( n );
	return n;
}

//-----------------------------------------------------------------------------
// The ParticleEffect client effect's carry-out (lane c5-particles): what
// stock's ParticleEffectCallback did with CNewParticleEffect for a dispatch at
// a world point. Which name, which points and the orientation are managed's
// (ParticleEffectHandler); this creates the system, watched as a one-shot.
//-----------------------------------------------------------------------------
const char *GetParticleSystemNameFromIndex( int nIndex );

int32_t SourceSharp_ParticleSystemNameFromIndex( int32_t nIndex, char *pszOut, int32_t nOutMax )
{
	const char *pszName = nIndex >= 0 ? GetParticleSystemNameFromIndex( nIndex ) : NULL;
	if ( !pszName || !pszOut || nOutMax <= 0 )
		return -1;
	Q_strncpy( pszOut, pszName, nOutMax );
	return Q_strlen( pszOut );
}

// pPoints: nPoints of (point, x, y, z), set in order. pOrientation0: control
// point 0's forward, right and up. 1 when a definition answered the name.
int32_t SourceSharp_ParticleSystemOneShot( const char *pszName, const float *pSortOriginXYZ, const float *pPoints,
                                           int32_t nPoints, const float *pOrientation0 )
{
	if ( !pszName || !pSortOriginXYZ || ( nPoints > 0 && !pPoints ) || !pOrientation0 )
		return 0;
	CSmartPtr< CShellNewParticleEffect > pEffect = CShellNewParticleEffect::Create( pszName );
	if ( !pEffect.IsValid() || !pEffect->IsValid() )
		return 0;
	pEffect->SetSortOrigin( Vector( pSortOriginXYZ[0], pSortOriginXYZ[1], pSortOriginXYZ[2] ) );
	for ( int i = 0; i < nPoints && i < 2; i++ )
		pEffect->SetControlPoint( (int)pPoints[ i * 4 ], Vector( pPoints[ i * 4 + 1 ], pPoints[ i * 4 + 2 ], pPoints[ i * 4 + 3 ] ) );
	pEffect->SetControlPointOrientation( 0, Vector( pOrientation0[0], pOrientation0[1], pOrientation0[2] ),
	                                     Vector( pOrientation0[3], pOrientation0[4], pOrientation0[5] ),
	                                     Vector( pOrientation0[6], pOrientation0[7], pOrientation0[8] ) );
	for ( int i = 2; i < nPoints; i++ )
		pEffect->SetControlPoint( (int)pPoints[ i * 4 ], Vector( pPoints[ i * 4 + 1 ], pPoints[ i * 4 + 2 ], pPoints[ i * 4 + 3 ] ) );
	ShellParticles_WatchOneShot( pEffect.GetObject() );
	return 1;
}

//-----------------------------------------------------------------------------
// The particle collision's world trace (lane c5-particles: ParticleCollision's
// IParticleWorldTrace): UTIL_TraceLine( start, end, MASK_SOLID_BRUSHONLY,
// NULL, COLLISION_GROUP_NONE ), as particle_collision.cpp made every one of
// its traces. Out: fraction, endpos xyz, normal xyz, dist, allsolid,
// startsolid. The count written.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ParticlesWorldTrace( const float *pStartXYZ, const float *pEndXYZ, float *pOut, int32_t nOut )
{
	if ( !pStartXYZ || !pEndXYZ || !pOut || nOut < 10 )
		return 0;
	trace_t tr;
	UTIL_TraceLine( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ), Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                MASK_SOLID_BRUSHONLY, NULL, COLLISION_GROUP_NONE, &tr );
	pOut[0] = tr.fraction;
	pOut[1] = tr.endpos.x;
	pOut[2] = tr.endpos.y;
	pOut[3] = tr.endpos.z;
	pOut[4] = tr.plane.normal.x;
	pOut[5] = tr.plane.normal.y;
	pOut[6] = tr.plane.normal.z;
	pOut[7] = tr.plane.dist;
	pOut[8] = tr.allsolid ? 1.0f : 0.0f;
	pOut[9] = tr.startsolid ? 1.0f : 0.0f;
	return 10;
}


//-----------------------------------------------------------------------------
// The view the particle manager is drawing (lane c5-particles): the model-view
// matrix's three rows, as CTrailParticles' render read them, and
// CurrentViewID(), which CSimpleGlowEmitter keys its visibility tests by, and
// the view's origin and forward, which CSimple3DEmitter sorts and shades by.
//-----------------------------------------------------------------------------
void SourceSharp_ParticlesView( float *pModelView12, int32_t *pViewId, float *pOriginForward6 )
{
	if ( pModelView12 )
	{
		const VMatrix &mv = ShellParticleDraw_ModelView();
		for ( int r = 0; r < 3; r++ )
			for ( int c = 0; c < 4; c++ )
				pModelView12[ r * 4 + c ] = mv[r][c];
	}
	if ( pViewId )
		*pViewId = (int32_t)CurrentViewID();
	if ( pOriginForward6 )
	{
		const Vector &vo = CurrentViewOrigin();
		const Vector &vf = CurrentViewForward();
		pOriginForward6[0] = vo.x;
		pOriginForward6[1] = vo.y;
		pOriginForward6[2] = vo.z;
		pOriginForward6[3] = vf.x;
		pOriginForward6[4] = vf.y;
		pOriginForward6[5] = vf.z;
	}
}

//-----------------------------------------------------------------------------
// PixelVisibility_FractionVisible at a point, through the caller's handle (a
// pixelvis_handle_t, which is an int; managed keeps it with the emitter that
// owns the query, as CSimpleGlowEmitter kept m_queryHandle).
//-----------------------------------------------------------------------------
float SourceSharp_ParticlesPixelVisible( const float *pOriginXYZ, int32_t *pQueryHandle )
{
	if ( !pOriginXYZ || !pQueryHandle )
		return 0.0f;
	pixelvis_queryparams_t params;
	params.Init( Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] ) );
	pixelvis_handle_t hQuery = *pQueryHandle;
	const float flVisible = PixelVisibility_FractionVisible( params, &hQuery );
	*pQueryHandle = hQuery;
	return flVisible;
}

//-----------------------------------------------------------------------------
// A line through the world with the contents mask managed names (lane
// c5-particles: WaterSplashes' MASK_WATER ripple, ExplosionEffects'
// CONTENTS_SOLID probes and MASK_WATER surface search):
// UTIL_TraceLine( start, end, mask, NULL, COLLISION_GROUP_NONE ). Out:
// fraction, endpos xyz, normal xyz, startsolid, fractionleftsolid. The count
// written.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ParticlesTraceMask( const float *pStartXYZ, const float *pEndXYZ, uint32_t nMask, float *pOut, int32_t nOut )
{
	if ( !pStartXYZ || !pEndXYZ || !pOut || nOut < 9 )
		return 0;
	trace_t tr;
	UTIL_TraceLine( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ), Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                nMask, NULL, COLLISION_GROUP_NONE, &tr );
	pOut[0] = tr.fraction;
	pOut[1] = tr.endpos.x;
	pOut[2] = tr.endpos.y;
	pOut[3] = tr.endpos.z;
	pOut[4] = tr.plane.normal.x;
	pOut[5] = tr.plane.normal.y;
	pOut[6] = tr.plane.normal.z;
	pOut[7] = tr.startsolid ? 1.0f : 0.0f;
	pOut[8] = tr.fractionleftsolid;
	return 9;
}

//-----------------------------------------------------------------------------
// fx_water.cpp's four entry points, which native code still calls (lane
// c5-particles): physics.cpp's PhysicsSplash (a prop hitting water) and the
// rain's ripple (shell_effects_sim.cpp). The routines are C#'s (WaterSplashes,
// WaterMath); each call here is one crossing with stock's arguments, and the
// splash or the tint is decided there.
//-----------------------------------------------------------------------------
static void ManagedWaterSplash( int nKind, const Vector &origin, const Vector *pNormal, float flScale, const Vector *pColor,
                                float flLifetime, float flAlpha )
{
	if ( IManagedParticlesClient *pArea = ManagedParticlesClient() )
		pArea->WaterSplash( nKind, origin.Base(), pNormal ? pNormal->Base() : NULL, flScale, pColor ? pColor->Base() : NULL,
		                    flLifetime, flAlpha );
}

void FX_GunshotSplash( const Vector &origin, const Vector &normal, float scale )
{
	ManagedWaterSplash( 0, origin, &normal, scale, NULL, 0.0f, 0.0f );
}

void FX_GunshotSlimeSplash( const Vector &origin, const Vector &normal, float scale )
{
	ManagedWaterSplash( 1, origin, &normal, scale, NULL, 0.0f, 0.0f );
}

void FX_WaterRipple( const Vector &origin, float scale, Vector *pColor, float flLifetime, float flAlpha )
{
	ManagedWaterSplash( 2, origin, NULL, scale, pColor, flLifetime, flAlpha );
}

// Unanswered (no managed side), the tint is black and the luminosity 0: what
// stock answers for a black light.
void UTIL_GetNormalizedColorTintAndLuminosity( const Vector &color, Vector *tint, float *luminosity )
{
	Vector vTint( 0, 0, 0 );
	float flLuminosity = 0.0f;
	if ( IManagedParticlesClient *pArea = ManagedParticlesClient() )
		pArea->SplashTint( color.Base(), vTint.Base(), &flLuminosity );
	if ( tint )
		*tint = vTint;
	if ( luminosity )
		*luminosity = flLuminosity;
}

//-----------------------------------------------------------------------------
// fx_blood.cpp's precache list, which left with it (lane c5-particles): the
// blood effects are C# (BloodEffects) and draw through the executors above,
// and the precache system is still native, so the four materials stock's
// fx_blood.cpp registered are registered here, unchanged.
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectBloodSpray )
CLIENTEFFECT_MATERIAL( "effects/blood_core" )
CLIENTEFFECT_MATERIAL( "effects/blood_gore" )
CLIENTEFFECT_MATERIAL( "effects/blood_drop" )
CLIENTEFFECT_MATERIAL( "effects/blood_puff" )
CLIENTEFFECT_REGISTER_END()

//-----------------------------------------------------------------------------
// fx_explosion.cpp's precache list, which left with it (lane c5-particles):
// the explosions are C# (ExplosionEffects) and draw through the executors
// above and the effect list; the precache system is still native, so the nine
// materials stock's fx_explosion.cpp registered are registered here, unchanged.
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectExplosion )
CLIENTEFFECT_MATERIAL( "effects/fire_cloud1" )
CLIENTEFFECT_MATERIAL( "effects/fire_cloud2" )
CLIENTEFFECT_MATERIAL( "effects/fire_embers1" )
CLIENTEFFECT_MATERIAL( "effects/fire_embers2" )
CLIENTEFFECT_MATERIAL( "effects/fire_embers3" )
CLIENTEFFECT_MATERIAL( "particle/particle_smokegrenade" )
CLIENTEFFECT_MATERIAL( "particle/particle_smokegrenade1" )
CLIENTEFFECT_MATERIAL( "effects/splash3" )
CLIENTEFFECT_MATERIAL( "effects/splashwake1" )
CLIENTEFFECT_REGISTER_END()

//-----------------------------------------------------------------------------
// fx.cpp's precache list, which left with it (lane c5-particles): the muzzle
// flashes are C# (FxEffects, MuzzleFlashes) and draw through the executors
// above and the pool's emitters; the precache system is still native, so the
// twelve materials stock's fx.cpp registered in this build (no TF_CLIENT_DLL,
// CSTRIKE_DLL or HL2MP) are registered here, unchanged.
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheMuzzleFlash )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash1" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash2" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash3" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash4" )
CLIENTEFFECT_MATERIAL( "effects/bluemuzzle" )
CLIENTEFFECT_MATERIAL( "effects/gunshipmuzzle" )
CLIENTEFFECT_MATERIAL( "effects/gunshiptracer" )
CLIENTEFFECT_MATERIAL( "effects/huntertracer" )
CLIENTEFFECT_MATERIAL( "sprites/physcannon_bluelight2" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle1" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle2" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle2_nocull" )
CLIENTEFFECT_REGISTER_END()

//-----------------------------------------------------------------------------
// fx_water.cpp's precache list, which left with it (lane c5-particles): the
// splashes are C# (WaterSplashes) and draw through the executors above, so the
// four materials stock's fx_water.cpp registered are registered here, unchanged.
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectSplash )
CLIENTEFFECT_MATERIAL( "effects/splash1" )
CLIENTEFFECT_MATERIAL( "effects/splash2" )
CLIENTEFFECT_MATERIAL( "effects/splash4" )
CLIENTEFFECT_MATERIAL( "effects/slime1" )
CLIENTEFFECT_REGISTER_END()

//-----------------------------------------------------------------------------
// fx_sparks.cpp's precache list, which left with it (lane c5-particles): the
// sparks are C# (SparkEffects) and draw through the executors above and the
// effect list, so the ten materials stock's fx_sparks.cpp registered are
// registered here (sorted by name: the order a precache list names its
// materials in decides nothing).
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectSparks )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle1_nocull" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle2_nocull" )
CLIENTEFFECT_MATERIAL( "effects/energyball" )
CLIENTEFFECT_MATERIAL( "effects/energysplash" )
CLIENTEFFECT_MATERIAL( "effects/spark" )
CLIENTEFFECT_MATERIAL( "effects/yellowflare" )
CLIENTEFFECT_MATERIAL( "effects/yellowflare_noz" )
CLIENTEFFECT_MATERIAL( "sprites/blueflare1" )
CLIENTEFFECT_MATERIAL( "sprites/rico1" )
CLIENTEFFECT_MATERIAL( "sprites/rico1_noz" )
CLIENTEFFECT_REGISTER_END()
