//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the detail props' executor ==========================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve.md C7 cluster 10. Everything the detail props DECIDE is
// in C# (SourceSharp.Client.Detail's DetailPropSystem, held to stock by
// DetailPropsOracleTests): the detail lump's parse, the fades, the screen
// alignment, the back-to-front sorts, every sprite quad, the batches, the
// model props' colour and the material's name. What is left here is what
// managed code cannot hold, and each piece carries out an answer:
//
//   * the model props' renderables. The leaf system hands them to the
//     renderer (GetDetailModel), which calls their IClientRenderable
//     virtuals; their alpha, angles and colour are managed's (BuildLists'
//     states and the sorts' alphas).
//   * the leaf system's tables: SetDetailObjectsInLeaf and a marker in
//     CLSUBSYSTEM_DETAILOBJECTS for a leaf with fast sprites, from the level's
//     runs; and each view's DrawDetailObjectsInLeaf marks, made by the
//     engine's BSP query over the sphere managed names.
//   * the materials (CMaterialReference), the render context and the mesh:
//     a draw is managed's Prepare (the material, or no draw), then the render
//     context up to GetMaxToRender, then managed's Emit carried out vertex by
//     vertex (detail_props_wire.h reads every answer).
//
// IManagedDetailPropsClient is the managed half; with no managed load (before
// the first, inside a cs_cl_reload) the system draws and builds nothing, as a
// level without detail props would.
//
// game/client/detailobjectsystem.cpp, which this replaces, left client.so in
// the same commit (docs/client_evictions.md).
//=============================================================================

#include "cbase.h"
#include "detailobjectsystem.h"
#include "clientleafsystem.h"
#include "view.h"
#include "iviewrender.h"
#include "iclientmode.h"
#include "bsptreedata.h"
#include "engine/ivmodelinfo.h"
#include "engine/ivmodelrender.h"
#include "model_types.h"
#include "materialsystem/imesh.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "imanaged_areas_client.h"
#include "shell_world_nettable_client.h"
#include "detail_props_wire.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// viewrender's, kept native with the render plan's other inputs
// (shell_viewrender_client.cpp).
extern ConVar r_DrawDetailProps;

namespace
{

const char *const kSpriteMaterial = "detail/detailsprites";
const char *const kWireframeMaterial = "debug/debugspritewireframe";

//-----------------------------------------------------------------------------
// A model prop, as the renderer reads it. Its state is managed's.
//-----------------------------------------------------------------------------
class CShellDetailModel : public IClientUnknown, public IClientRenderable
{
public:
	CShellDetailModel( const model_t *pModel, const Vector &origin, const QAngle &angles, int nOrdinal )
		: m_pModel( pModel ), m_Origin( origin ), m_Angles( angles ), m_nAlpha( 255 ), m_nOrdinal( nOrdinal )
	{
		m_Color[ 0 ] = m_Color[ 1 ] = m_Color[ 2 ] = 1.0f;
	}

	// A view's state, for a prop whose leaf is in the view's sphere.
	void SetState( unsigned char alpha, bool bSetAngles, const float *pAngles )
	{
		m_nAlpha = alpha;
		if ( bSetAngles )
			m_Angles.Init( pAngles[ 0 ], pAngles[ 1 ], pAngles[ 2 ] );
	}

	void SetColor( const float *pColor )
	{
		m_Color[ 0 ] = pColor[ 0 ];
		m_Color[ 1 ] = pColor[ 1 ];
		m_Color[ 2 ] = pColor[ 2 ];
	}

	void SetAlpha( unsigned char alpha ) { m_nAlpha = alpha; }

	int Ordinal() const { return m_nOrdinal; }

	// IHandleEntity: a detail prop has no handle.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE { Assert( false ); }
	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { Assert( false ); return *( (CBaseHandle *)0 ); }

	// IClientUnknown
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return this; }
	virtual ICollideable *GetCollideable() OVERRIDE { return 0; }
	virtual IClientNetworkable *GetClientNetworkable() OVERRIDE { return 0; }
	virtual IClientRenderable *GetClientRenderable() OVERRIDE { return this; }
	virtual IClientEntity *GetIClientEntity() OVERRIDE { return 0; }
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return 0; }
	virtual IClientThinkable *GetClientThinkable() OVERRIDE { return 0; }

	// IClientRenderable
	virtual const Vector &GetRenderOrigin() OVERRIDE { return m_Origin; }
	virtual const QAngle &GetRenderAngles() OVERRIDE { return m_Angles; }
	virtual bool ShouldDraw() OVERRIDE { return g_pClientMode->ShouldDrawDetailObjects(); }
	virtual bool IsTransparent() OVERRIDE { return m_nAlpha < 255 || modelinfo->IsTranslucent( m_pModel ); }
	virtual bool UsesPowerOfTwoFrameBufferTexture() OVERRIDE { return false; }
	virtual bool UsesFullFrameBufferTexture() OVERRIDE { return false; }
	virtual ClientShadowHandle_t GetShadowHandle() const OVERRIDE { return CLIENTSHADOW_INVALID_HANDLE; }
	virtual ClientRenderHandle_t &RenderHandle() OVERRIDE
	{
		AssertMsg( 0, "a detail prop has no render handle" );
		return *( (ClientRenderHandle_t *)NULL );
	}
	virtual const model_t *GetModel() const OVERRIDE { return m_pModel; }
	virtual int DrawModel( int flags ) OVERRIDE
	{
		if ( m_nAlpha == 0 || !m_pModel )
			return 0;
		return modelrender->DrawModel( flags, this, MODEL_INSTANCE_INVALID, -1, m_pModel, m_Origin, m_Angles, 0, 0, 0 );
	}
	virtual int GetBody() OVERRIDE { return 0; }
	virtual void ComputeFxBlend() OVERRIDE {}
	virtual int GetFxBlend() OVERRIDE { return m_nAlpha; }
	virtual void GetColorModulation( float *color ) OVERRIDE
	{
		color[ 0 ] = m_Color[ 0 ];
		color[ 1 ] = m_Color[ 1 ];
		color[ 2 ] = m_Color[ 2 ];
	}
	virtual bool LODTest() OVERRIDE { return true; }
	virtual bool SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask, float currentTime ) OVERRIDE
	{
		if ( !m_pModel )
			return false;
		// Every bone at the prop's own transform.
		matrix3x4_t transform;
		AngleMatrix( m_Angles, transform );
		transform[ 0 ][ 3 ] = m_Origin.x;
		transform[ 1 ][ 3 ] = m_Origin.y;
		transform[ 2 ][ 3 ] = m_Origin.z;
		studiohdr_t *pStudioHdr = modelinfo->GetStudiomodel( m_pModel );
		for ( int i = 0; i < pStudioHdr->numbones; i++ )
			MatrixCopy( transform, pBoneToWorldOut[ i ] );
		return true;
	}
	virtual void SetupWeights( const matrix3x4_t *pBoneToWorld, int nFlexWeightCount, float *pFlexWeights, float *pFlexDelayedWeights ) OVERRIDE {}
	virtual void DoAnimationEvents() OVERRIDE {}
	virtual IPVSNotify *GetPVSNotifyInterface() OVERRIDE { return NULL; }
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE
	{
		const int nType = modelinfo->GetModelType( m_pModel );
		if ( nType == mod_studio || nType == mod_brush )
		{
			modelinfo->GetModelRenderBounds( m_pModel, mins, maxs );
			return;
		}
		mins.Init( 0, 0, 0 );
		maxs.Init( 0, 0, 0 );
	}
	virtual void GetRenderBoundsWorldspace( Vector &mins, Vector &maxs ) OVERRIDE { DefaultRenderBoundsWorldspace( this, mins, maxs ); }
	virtual void GetShadowRenderBounds( Vector &mins, Vector &maxs, ShadowType_t shadowType ) OVERRIDE { GetRenderBounds( mins, maxs ); }
	virtual bool ShouldReceiveProjectedTextures( int flags ) OVERRIDE { return false; }
	virtual bool GetShadowCastDistance( float *pDist, ShadowType_t shadowType ) const OVERRIDE { return false; }
	virtual bool GetShadowCastDirection( Vector *pDirection, ShadowType_t shadowType ) const OVERRIDE { return false; }
	virtual bool IsShadowDirty() OVERRIDE { return false; }
	virtual void MarkShadowDirty( bool bDirty ) OVERRIDE {}
	virtual IClientRenderable *GetShadowParent() OVERRIDE { return NULL; }
	virtual IClientRenderable *FirstShadowChild() OVERRIDE { return NULL; }
	virtual IClientRenderable *NextShadowPeer() OVERRIDE { return NULL; }
	virtual ShadowType_t ShadowCastType() OVERRIDE { return SHADOWS_NONE; }
	virtual void CreateModelInstance() OVERRIDE {}
	virtual ModelInstanceHandle_t GetModelInstance() OVERRIDE { return MODEL_INSTANCE_INVALID; }
	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE
	{
		AngleMatrix( m_Angles, m_Origin, m_Transform );
		return m_Transform;
	}
	virtual int LookupAttachment( const char *pAttachmentName ) OVERRIDE { return -1; }
	virtual bool GetAttachment( int number, Vector &origin, QAngle &angles ) OVERRIDE
	{
		origin = m_Origin;
		angles = m_Angles;
		return true;
	}
	virtual bool GetAttachment( int number, matrix3x4_t &matrix ) OVERRIDE
	{
		MatrixCopy( RenderableToWorldTransform(), matrix );
		return true;
	}
	virtual float *GetRenderClipPlane() OVERRIDE { return NULL; }
	virtual int GetSkin() OVERRIDE { return 0; }
	virtual bool IsTwoPass() OVERRIDE { return false; }
	virtual void OnThreadedDrawSetup() OVERRIDE {}
	virtual bool UsesFlexDelayedWeights() OVERRIDE { return false; }
	virtual void RecordToolMessage() OVERRIDE {}
	virtual bool IgnoresZBuffer() const OVERRIDE { return false; }

private:
	const model_t *m_pModel;
	Vector m_Origin;
	QAngle m_Angles;
	unsigned char m_nAlpha;
	int m_nOrdinal;
	float m_Color[ 3 ];
	matrix3x4_t m_Transform;
};

// What the leaf system holds for a leaf with fast sprites: a marker, which
// ShouldDrawDetailObjectsInLeaf tests and the leaf system deletes at its
// shutdown. The sprites are managed's.
class CShellDetailLeafMarker : public CClientLeafSubSystemData
{
};

//-----------------------------------------------------------------------------
// The client system, which the leaf system, the view executor and
// kClientSystems[] reach as DetailObjectSystem().
//-----------------------------------------------------------------------------
class CShellDetailProps : public IDetailObjectSystem, public ISpatialLeafEnumerator
{
public:
	virtual char const *Name() OVERRIDE { return "DetailObjectSystem"; }

	virtual bool Init() OVERRIDE
	{
		m_matFullbright.Init( "mat_fullbright", true );
		if ( IManagedDetailPropsClient *pArea = ManagedDetailPropsClient() )
			pArea->DetailInit();
		return true;
	}

	virtual void LevelInitPreEntity() OVERRIDE
	{
		m_SpriteMaterial.Init( kSpriteMaterial, TEXTURE_GROUP_OTHER );
		m_WireframeMaterial.Init( kWireframeMaterial, TEXTURE_GROUP_OTHER );

		IManagedDetailPropsClient *pArea = ManagedDetailPropsClient();
		if ( !pArea )
			return;

		IMaterial *pMaterial = m_SpriteMaterial;
		const int nWidth = pMaterial ? pMaterial->GetMappingWidth() : 1;
		const int nHeight = pMaterial ? pMaterial->GetMappingHeight() : 1;
		const bool bHdr = g_pMaterialSystemHardwareConfig->GetHDRType() != HDR_TYPE_NONE;
		const void *pLevel = pArea->DetailLevelInitPreEntity( nWidth, nHeight, bHdr ? 1 : 0 );
		if ( !ss_detail::ReadLevel( pLevel, *this ) )
			Warning( "[SourceSharp/Detail] the level's detail props answer was malformed; the rest is dropped\n" );
		if ( m_bPrecache )
			PrecacheMaterial( kSpriteMaterial );
	}

	virtual void LevelInitPostEntity() OVERRIDE
	{
		char szMaterial[ MAX_PATH ];
		IManagedDetailPropsClient *pArea = ManagedDetailPropsClient();
		if ( !pArea || pArea->DetailLevelInitPostEntity( SourceSharp_WorldDetailSpriteMaterial(), szMaterial, sizeof( szMaterial ) ) <= 0 )
			V_strncpy( szMaterial, kSpriteMaterial, sizeof( szMaterial ) );
		m_SpriteMaterial.Init( szMaterial, TEXTURE_GROUP_OTHER );
	}

	virtual void LevelShutdownPreEntity() OVERRIDE
	{
		if ( IManagedDetailPropsClient *pArea = ManagedDetailPropsClient() )
			pArea->DetailLevelShutdownPreEntity();
		m_Objects.PurgeAndDeleteElements();
		m_Models.Purge();
		m_bPrecache = false;
		m_SpriteMaterial.Shutdown();
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		m_WireframeMaterial.Shutdown();
	}

	virtual IClientRenderable *GetDetailModel( int idx ) OVERRIDE
	{
		return m_Objects.IsValidIndex( idx ) ? m_Objects[ idx ] : NULL;
	}

	virtual void BuildDetailObjectRenderLists( const Vector &vViewOrigin ) OVERRIDE
	{
		IManagedDetailPropsClient *pArea = ManagedDetailPropsClient();
		if ( !pArea )
			return;
		const Vector vecCurrent = CurrentViewOrigin();
		const void *pView = pArea->DetailBuildLists( vViewOrigin.Base(), vecCurrent.Base(),
		                                             g_pClientMode->ShouldDrawDetailObjects() ? 1 : 0,
		                                             r_DrawDetailProps.GetInt(), m_matFullbright.GetInt() );
		m_States.RemoveAll();
		float flRadius = 0.0f;
		if ( !ss_detail::ReadView( pView, *this, &flRadius ) )
			return;

		m_nBuildWorldListNumber = view->BuildWorldListsNumber();
		engine->GetBSPTreeQuery()->EnumerateLeavesInSphere( vecCurrent, flRadius, this, 0 );
	}

	virtual void RenderOpaqueDetailObjects( int nLeafCount, LeafIndex_t *pLeafList ) OVERRIDE
	{
		// Stock's is empty: the model props draw through the renderables list.
	}

	virtual void BeginTranslucentDetailRendering() OVERRIDE
	{
		if ( IManagedDetailPropsClient *pArea = ManagedDetailPropsClient() )
			pArea->DetailBeginTranslucent();
	}

	virtual void RenderTranslucentDetailObjects( const Vector &viewOrigin, const Vector &viewForward, const Vector &viewRight,
	                                             const Vector &viewUp, int nLeafCount, LeafIndex_t *pLeafList ) OVERRIDE
	{
		CUtlVector< int32_t > leaves;
		leaves.EnsureCount( MAX( nLeafCount, 0 ) );
		for ( int i = 0; i < nLeafCount; ++i )
			leaves[ i ] = pLeafList[ i ];
		// The fast sprites (stock's RenderFastSprites), then the sorted ones.
		Draw( kFastList, viewOrigin, viewForward, viewRight, viewUp, leaves.Base(), leaves.Count(), NULL );
		Draw( kOldList, viewOrigin, viewForward, viewRight, viewUp, leaves.Base(), leaves.Count(), NULL );
	}

	virtual void RenderTranslucentDetailObjectsInLeaf( const Vector &viewOrigin, const Vector &viewForward, const Vector &viewRight,
	                                                   const Vector &viewUp, int nLeaf, const Vector *pVecClosestPoint ) OVERRIDE
	{
		int32_t leaf = nLeaf;
		Draw( kFastInLeaf, viewOrigin, viewForward, viewRight, viewUp, &leaf, 1, pVecClosestPoint );
		Draw( kOldInLeaf, viewOrigin, viewForward, viewRight, viewUp, &leaf, 1, pVecClosestPoint );
	}

	// ISpatialLeafEnumerator: a leaf in the view's sphere, marked, and the
	// managed states of its model props applied.
	virtual bool EnumerateLeaf( int leaf, intp context ) OVERRIDE
	{
		int nFirst, nCount;
		ClientLeafSystem()->DrawDetailObjectsInLeaf( leaf, m_nBuildWorldListNumber, nFirst, nCount );
		for ( int i = 0; i < nCount; ++i )
		{
			const int idx = nFirst + i;
			if ( !m_Objects.IsValidIndex( idx ) || !m_Objects[ idx ] )
				continue;
			const int nOrdinal = m_Objects[ idx ]->Ordinal();
			if ( m_States.IsValidIndex( nOrdinal ) )
			{
				const State_t &s = m_States[ nOrdinal ];
				m_Objects[ idx ]->SetState( s.alpha, s.bSetAngles, s.angles );
			}
		}
		return true;
	}

	//-------------------------------------------------------------------------
	// detail_props_wire.h's handlers
	//-------------------------------------------------------------------------
	void Precache( bool b ) { m_bPrecache = b; }

	void Object( int i, void *pModel, const float *pOrigin, const float *pAngles )
	{
		CShellDetailModel *pObject = NULL;
		if ( pModel )
		{
			pObject = new CShellDetailModel( (const model_t *)pModel, Vector( pOrigin[ 0 ], pOrigin[ 1 ], pOrigin[ 2 ] ),
			                                 QAngle( pAngles[ 0 ], pAngles[ 1 ], pAngles[ 2 ] ), m_Models.Count() );
			m_Models.AddToTail( pObject );
		}
		m_Objects.AddToTail( pObject );
	}

	void Run( int leaf, int first, int count, bool bFast )
	{
		if ( bFast )
			ClientLeafSystem()->SetSubSystemDataInLeaf( leaf, CLSUBSYSTEM_DETAILOBJECTS, new CShellDetailLeafMarker );
		ClientLeafSystem()->SetDetailObjectsInLeaf( leaf, first, count );
	}

	void Proxy( void *pModel ) { modelinfo->RecomputeTranslucency( (const model_t *)pModel, 0, 0, NULL ); }

	void Model( int i, unsigned char alpha, bool bSetAngles, const float *pAngles, const float *pColor )
	{
		State_t s;
		s.alpha = alpha;
		s.bSetAngles = bSetAngles;
		V_memcpy( s.angles, pAngles, sizeof( s.angles ) );
		m_States.AddToTail( s );
		// The colour is no view's: every model prop takes it now.
		if ( m_Models.IsValidIndex( i ) )
			m_Models[ i ]->SetColor( pColor );
	}

	void ModelAlpha( int object, unsigned char alpha )
	{
		if ( m_Objects.IsValidIndex( object ) && m_Objects[ object ] )
			m_Objects[ object ]->SetAlpha( alpha );
	}

	void Begin( int quads ) { m_MeshBuilder.Begin( m_pMesh, MATERIAL_QUADS, quads ); }

	void Vertex( const float *pXYZ, const unsigned char *pRGBA, const float *pST )
	{
		m_MeshBuilder.Position3fv( pXYZ );
		m_MeshBuilder.Color4ubv( pRGBA );
		m_MeshBuilder.TexCoord2fv( 0, pST );
		m_MeshBuilder.AdvanceVertex();
	}

	void EndDraw()
	{
		m_MeshBuilder.End();
		m_pMesh->Draw();
	}

private:
	enum { kFastList = 0, kOldList = 1, kFastInLeaf = 2, kOldInLeaf = 3 };

	// One of stock's four translucent draws: managed's Prepare, the render
	// context to GetMaxToRender, managed's Emit carried out. A mesh with no
	// room ends the draw with the matrix still pushed, as stock's return did.
	void Draw( int nKind, const Vector &viewOrigin, const Vector &viewForward, const Vector &viewRight, const Vector &viewUp,
	           const int32_t *pLeaves, int nLeaves, const Vector *pClosest )
	{
		IManagedDetailPropsClient *pArea = ManagedDetailPropsClient();
		if ( !pArea )
			return;

		const Vector &current = CurrentViewOrigin();
		const float view[ 15 ] = { viewOrigin.x, viewOrigin.y, viewOrigin.z, viewForward.x, viewForward.y, viewForward.z,
		                           viewRight.x, viewRight.y, viewRight.z, viewUp.x, viewUp.y, viewUp.z,
		                           current.x, current.y, current.z };
		const void *pPrepared = pArea->DetailPrepare( nKind, view, pLeaves, nLeaves, pClosest ? pClosest->Base() : NULL,
		                                              r_DrawDetailProps.GetInt(), ShouldDrawInWireFrameMode() ? 1 : 0,
		                                              m_matFullbright.GetInt() );
		const int nMaterial = ss_detail::ReadPrepare( pPrepared, *this );
		if ( nMaterial == ss_detail::kMaterialNone )
			return;

		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->MatrixMode( MATERIAL_MODEL );
		pRenderContext->PushMatrix();
		pRenderContext->LoadIdentity();

		IMaterial *pMaterial = nMaterial == ss_detail::kMaterialWireframe ? (IMaterial *)m_WireframeMaterial : (IMaterial *)m_SpriteMaterial;
		m_pMesh = pRenderContext->GetDynamicMesh( true, NULL, NULL, pMaterial );

		int nMaxVerts, nMaxIndices;
		pRenderContext->GetMaxToRender( m_pMesh, false, &nMaxVerts, &nMaxIndices );
		int nMaxQuads = nMaxIndices / 6;
		if ( nMaxQuads > nMaxVerts / 4 )
			nMaxQuads = nMaxVerts / 4;
		if ( nMaxQuads == 0 )
			return;

		if ( !ss_detail::RunEmit( pArea->DetailEmit( nMaxQuads ), *this ) )
			Warning( "[SourceSharp/Detail] a draw's answer was malformed; the rest is dropped\n" );

		pRenderContext->PopMatrix();
		m_pMesh = NULL;
	}

	struct State_t
	{
		unsigned char alpha;
		bool bSetAngles;
		float angles[ 3 ];
	};

	CUtlVector< CShellDetailModel * > m_Objects;	// by object index; NULL for a sprite
	CUtlVector< CShellDetailModel * > m_Models;		// the model props, by ordinal
	CUtlVector< State_t > m_States;					// this view's, by ordinal
	CMaterialReference m_SpriteMaterial;
	CMaterialReference m_WireframeMaterial;
	ConVarRef m_matFullbright{ "mat_fullbright", true };
	CMeshBuilder m_MeshBuilder;
	IMesh *m_pMesh = NULL;
	int m_nBuildWorldListNumber = 0;
	bool m_bPrecache = false;
};

CShellDetailProps g_ShellDetailProps;

} // namespace

IDetailObjectSystem *DetailObjectSystem()
{
	return &g_ShellDetailProps;
}
