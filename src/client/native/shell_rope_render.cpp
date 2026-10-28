//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the rope executor =================================//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_client_novalve.md §2 cluster 6. Everything a rope DECIDES is in C#
// now: the verlet ring, wind, gusts, shakes, the resting test, the endpoints,
// the Catmull-Rom subdivision, the lighting and the FakeAA widths
// (src/client/managed/Ropes). What is left here is what managed code cannot
// hold, which is three handles and a mesh:
//
//   * the materials. A rope names its material; IMaterial pointers are the
//     material system's, so this file finds them, keeps them referenced and
//     answers a small integer for each (RopeMaterial).
//   * the per-view render cache. viewrender asks IRopeManager to reset it
//     before a view's opaque entities draw and to draw it after them; a rope's
//     DrawModel (the managed owner's custom draw) hands its built strip in
//     between (RopeDraw).
//   * the mesh. Each strip arrives as the camera-facing vertices managed
//     code's BeamSegDraw made of it (two a segment, beam_strip_vertex.h's
//     layout), written into one triangle list per material. The FakeAA back
//     pass, the solid pass and the shadow-depth pass are a kind switch on what
//     managed code handed over.
//
// The material system may run its render calls on another thread. Then the
// cache is copied into one of two frame buffers (OnRenderStart switches them)
// and the draw is queued, which is what the stock CRopeManager did with its
// memory stack.
//
// The stock C_RopeKeyframe, CRopeManager and their simulation left with this
// file's rewrite; c_rope.cpp goes from the build in its own commit.
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "c_rope.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imesh.h"
#include "tier1/callqueue.h"
#include "tier1/KeyValues.h"
#include "view.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "engine/IEngineTrace.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "particles/particles.h"
#include "tier0/platform.h"
#include "tier1/refcount.h"
#include "tier1/strtools.h"
#include "tier1/utlvector.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "cdll_util.h"
#include "gametrace.h"
#include "particledraw.h"
#include "texture_group_names.h"
#include "trace.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "beam_strip_vertex.h"
#include "shell_effect_ledger.h"
#include "shell_clientsystem.h"	// plan_cworld_trace.md T4

// memdbgon must be the last include file in a .cpp file!!!


// RopeDraw's flags. Kinds, not decisions: managed code chose them.
enum
{
	ROPE_DRAW_FAKEAA = 0x1,   // the strip was built for the back pass; draw it with the back material
	ROPE_DRAW_SOLID  = 0x2,   // draw the solid pass (rope_rendersolid)
};

//-----------------------------------------------------------------------------
// The materials, by handle.
//-----------------------------------------------------------------------------
struct RopeMaterial_t
{
	char       szName[128];
	IMaterial *pSolid;
	IMaterial *pBack;
	int        nTextureHeight;
};

static CUtlVector< RopeMaterial_t > g_RopeMaterials;

int32_t SourceSharp_RopeMaterial( const char *pszName, int32_t *pOutTextureHeight, int32_t *pOutHasBack )
{
	if ( !pszName || !pszName[0] || !materials )
		return -1;

	int iFound = -1;
	for ( int i = 0; i < g_RopeMaterials.Count(); i++ )
	{
		if ( !V_stricmp( g_RopeMaterials[i].szName, pszName ) )
		{
			iFound = i;
			break;
		}
	}

	if ( iFound < 0 )
	{
		RopeMaterial_t entry;
		V_strncpy( entry.szName, pszName, sizeof( entry.szName ) );

		entry.pSolid = materials->FindMaterial( pszName, TEXTURE_GROUP_OTHER );
		entry.nTextureHeight = entry.pSolid ? entry.pSolid->GetMappingHeight() : 1;
		if ( entry.pSolid )
			entry.pSolid->IncrementReferenceCount();

		char szBack[512];
		V_snprintf( szBack, sizeof( szBack ), "%s_back", pszName );
		entry.pBack = materials->FindMaterial( szBack, TEXTURE_GROUP_OTHER, false );
		if ( IsErrorMaterial( entry.pBack ) )
			entry.pBack = NULL;
		if ( entry.pBack )
			entry.pBack->IncrementReferenceCount();

		iFound = g_RopeMaterials.AddToTail( entry );
	}

	if ( pOutTextureHeight )
		*pOutTextureHeight = g_RopeMaterials[iFound].nTextureHeight;
	if ( pOutHasBack )
		*pOutHasBack = g_RopeMaterials[iFound].pBack ? 1 : 0;

	return iFound;
}

static void RopeMaterials_Release()
{
	for ( int i = 0; i < g_RopeMaterials.Count(); i++ )
	{
		if ( g_RopeMaterials[i].pSolid )
			g_RopeMaterials[i].pSolid->DecrementReferenceCount();
		if ( g_RopeMaterials[i].pBack )
			g_RopeMaterials[i].pBack->DecrementReferenceCount();
	}

	g_RopeMaterials.RemoveAll();
}

//-----------------------------------------------------------------------------
// One view's strips: where each sits in the segment pool, and how to draw it.
//-----------------------------------------------------------------------------
struct RopeStrip_t
{
	int nMaterial;
	int iFirst;			// vertices, not floats
	int nVerts;
	int iFirstSolid;
	int nSolid;
	int nFlags;
};

struct RopeCache_t
{
	CUtlVector< RopeStrip_t > strips;
	CUtlVector< float >       verts;	// BEAM_STRIP_VERTEX_FLOATS a vertex

	void Clear()
	{
		strips.RemoveAll();
		verts.RemoveAll();
	}
};

class CSourceSharpRopeExecutor : public IRopeManager
{
public:
	CSourceSharpRopeExecutor() : m_iQueued( 0 ), m_pDepthWrite( NULL ) {}

	void Add( int nMaterial, const float *pVerts, int nVerts, const float *pSolid, int nSolid, int nFlags )
	{
		RopeStrip_t strip;
		strip.nMaterial   = nMaterial;
		strip.iFirst      = m_Cache.verts.Count() / BEAM_STRIP_VERTEX_FLOATS;
		strip.nVerts      = nVerts;
		m_Cache.verts.AddMultipleToTail( nVerts * BEAM_STRIP_VERTEX_FLOATS, pVerts );
		strip.iFirstSolid = m_Cache.verts.Count() / BEAM_STRIP_VERTEX_FLOATS;
		strip.nSolid      = pSolid ? nSolid : 0;
		if ( strip.nSolid > 0 )
			m_Cache.verts.AddMultipleToTail( strip.nSolid * BEAM_STRIP_VERTEX_FLOATS, pSolid );
		strip.nFlags      = nFlags;
		m_Cache.strips.AddToTail( strip );
	}

	// IRopeManager.
	virtual void ResetRenderCache( void ) OVERRIDE { m_Cache.Clear(); }

	// Nothing calls this any more: a rope reaches the cache through RopeDraw.
	virtual void AddToRenderCache( C_RopeKeyframe * ) OVERRIDE {}

	virtual void DrawRenderCache( bool bShadowDepth ) OVERRIDE
	{
		if ( m_Cache.strips.Count() == 0 )
			return;

		ICallQueue *pCallQueue = materials->GetRenderContext()->GetCallQueue();

		if ( !pCallQueue )
		{
			Draw( bShadowDepth, &m_Cache );
			return;
		}

		// Copied into this frame's buffer, which outlives the queued call: the
		// other buffer is the one OnRenderStart clears.
		CUtlVector< RopeCache_t * > &frame = m_Queued[ m_iQueued ];
		RopeCache_t *pCopy = new RopeCache_t;
		pCopy->strips.CopyArray( m_Cache.strips.Base(), m_Cache.strips.Count() );
		pCopy->verts.CopyArray( m_Cache.verts.Base(), m_Cache.verts.Count() );
		frame.AddToTail( pCopy );

		pCallQueue->QueueCall( this, &CSourceSharpRopeExecutor::Draw, bShadowDepth, pCopy );
	}

	virtual void OnRenderStart( void ) OVERRIDE
	{
		m_iQueued = 1 - m_iQueued;
		m_Queued[ m_iQueued ].PurgeAndDeleteElements();
	}

	// The holiday lights are TF's (USES_ECON_ITEMS), compiled out of stock here too.
	virtual void SetHolidayLightMode( bool ) OVERRIDE {}
	virtual bool IsHolidayLightMode( void ) OVERRIDE { return false; }
	virtual int  GetHolidayLightStyle( void ) OVERRIDE { return 0; }

	void LevelShutdown()
	{
		m_Cache.Clear();
		m_Queued[0].PurgeAndDeleteElements();
		m_Queued[1].PurgeAndDeleteElements();
	}

private:
	void Draw( bool bShadowDepth, RopeCache_t *pCache )
	{
		if ( bShadowDepth && !m_pDepthWrite && g_pMaterialSystem )
		{
			KeyValues *pVMT = new KeyValues( "DepthWrite" );
			pVMT->SetInt( "$no_fullbright", 1 );
			pVMT->SetInt( "$alphatest", 0 );
			pVMT->SetInt( "$nocull", 1 );
			m_pDepthWrite = g_pMaterialSystem->FindProceduralMaterial( "__DepthWrite01", TEXTURE_GROUP_OTHER, pVMT );
			m_pDepthWrite->IncrementReferenceCount();
		}

		CMatRenderContextPtr pRenderContext( materials );

		// One batch per material, in handle order.
		for ( int iMat = 0; iMat < g_RopeMaterials.Count(); iMat++ )
		{
			int nVerts = 0, nIndices = 0, nStrips = 0;
			int nFlags = 0;

			for ( int i = 0; i < pCache->strips.Count(); i++ )
			{
				const RopeStrip_t &s = pCache->strips[i];
				if ( s.nMaterial != iMat )
					continue;
				nVerts   += s.nVerts;
				nIndices += ( s.nVerts / 2 - 1 ) * 6;
				nFlags   |= s.nFlags;
				nStrips++;
			}

			if ( !nStrips )
				continue;

			const RopeMaterial_t &mat = g_RopeMaterials[iMat];
			const bool bBack = !bShadowDepth && ( nFlags & ROPE_DRAW_FAKEAA ) && mat.pBack;

			if ( bBack )
				DrawPass( pRenderContext, pCache, iMat, mat.pBack, nVerts, nIndices, false, true );

			if ( nFlags & ROPE_DRAW_SOLID )
			{
				IMaterial *pMaterial = bShadowDepth ? m_pDepthWrite : mat.pSolid;
				if ( pMaterial )
					DrawPass( pRenderContext, pCache, iMat, pMaterial, nVerts, nIndices, bBack, false );
			}
		}
	}

	// One mesh of every strip that uses the material: the built strip, or (on
	// the solid pass after a back pass) the solid strip managed code remapped.
	static void DrawPass( IMatRenderContext *pRenderContext, RopeCache_t *pCache, int iMat, IMaterial *pMaterial,
	                      int nVerts, int nIndices, bool bSolidStrips, bool bCount )
	{
		CMeshBuilder meshBuilder;
		IMesh *pMesh = pRenderContext->GetDynamicMesh( true, NULL, NULL, pMaterial );
		meshBuilder.Begin( pMesh, MATERIAL_TRIANGLES, nVerts, nIndices );

		int nVertsSoFar = 0;

		for ( int i = 0; i < pCache->strips.Count(); i++ )
		{
			const RopeStrip_t &s = pCache->strips[i];
			if ( s.nMaterial != iMat )
				continue;

			const int iFirst = bSolidStrips ? s.iFirstSolid : s.iFirst;
			const int nStripVerts = bSolidStrips ? s.nSolid : s.nVerts;

			if ( nStripVerts >= 4 )
			{
				ss::beamstrip::WriteIndexed( meshBuilder, &pCache->verts[ iFirst * BEAM_STRIP_VERTEX_FLOATS ], nStripVerts,
				                             nVertsSoFar );

				if ( bCount )
					g_SourceSharpEffects.nRopeDraws++;

				nVertsSoFar += nStripVerts;
			}
		}

		meshBuilder.End();
		pMesh->Draw();
	}

	RopeCache_t                  m_Cache;
	CUtlVector< RopeCache_t * >  m_Queued[2];
	int                          m_iQueued;
	IMaterial                   *m_pDepthWrite;
};

static CSourceSharpRopeExecutor g_RopeExecutor;

IRopeManager *RopeManager()
{
	return &g_RopeExecutor;
}

//-----------------------------------------------------------------------------
// One rope's strip for the view being drawn, as the vertices managed code's
// BeamSegDraw made of it (and the solid pass's, the same count or none: the
// pass's mesh is sized by the strip's). Called from inside the managed owner's
// DrawModel, between viewrender's ResetRenderCache and DrawRenderCache.
//-----------------------------------------------------------------------------
void SourceSharp_RopeDraw( int32_t nMaterial, const float *pVerts, int32_t nVerts,
                           const float *pSolid, int32_t nSolid, int32_t nFlags )
{
	if ( !pVerts || nVerts < 4 || ( nVerts & 1 ) || nMaterial < 0 || nMaterial >= g_RopeMaterials.Count() )
		return;
	if ( nSolid != 0 && ( nSolid != nVerts || !pSolid ) )
		nSolid = 0;

	g_RopeExecutor.Add( nMaterial, pVerts, nVerts, pSolid, nSolid, nFlags );
}

//-----------------------------------------------------------------------------
// The level's materials and caches go with it.
//-----------------------------------------------------------------------------
class CSourceSharpRopeSystem : public CClientSystem
{
public:
	CSourceSharpRopeSystem() : CClientSystem( "CSourceSharpRopeSystem" ) {}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		g_RopeExecutor.LevelShutdown();
		RopeMaterials_Release();
	}
};

static CSourceSharpRopeSystem g_SourceSharpRopeSystem;

// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_Rope() { return &g_SourceSharpRopeSystem; }

//-----------------------------------------------------------------------------
// THE READS A MANAGED ROPE MAKES, each a pass-through onto the engine. Named
// for what they read rather than for ropes: a beam asks the same view.
//-----------------------------------------------------------------------------
// The view being drawn (CurrentViewForward/Origin, and the render context's
// world-space camera the strip faces: only inside a view, which is what
// bCurrentView says the caller is), the main view's origin, the screen width,
// and the two hardware facts FakeAA rope smoothing reads.
void SourceSharp_ClientViewState( int32_t bCurrentView, float *pOutForwardXYZ, float *pOutOriginXYZ,
                                  float *pOutMainOriginXYZ, float *pOutCameraXYZ, int32_t *pOutScreenWidth,
                                  int32_t *pOutDxLevel, int32_t *pOutAAEnabled )
{
	const Vector &vMain = MainViewOrigin();

	if ( bCurrentView )
	{
		if ( pOutForwardXYZ )
			VectorCopy( CurrentViewForward(), *reinterpret_cast< Vector * >( pOutForwardXYZ ) );
		if ( pOutOriginXYZ )
			VectorCopy( CurrentViewOrigin(), *reinterpret_cast< Vector * >( pOutOriginXYZ ) );
		if ( pOutCameraXYZ )
		{
			CMatRenderContextPtr pRenderContext( materials );
			pRenderContext->GetWorldSpaceCameraPosition( reinterpret_cast< Vector * >( pOutCameraXYZ ) );
		}
	}
	if ( pOutMainOriginXYZ )
		VectorCopy( vMain, *reinterpret_cast< Vector * >( pOutMainOriginXYZ ) );
	if ( pOutScreenWidth )
		*pOutScreenWidth = ScreenWidth();
	if ( pOutDxLevel )
		*pOutDxLevel = engine->GetDXSupportLevel();
	if ( pOutAAEnabled )
		*pOutAAEnabled = g_pMaterialSystemHardwareConfig && g_pMaterialSystemHardwareConfig->IsAAEnabled() ? 1 : 0;
}

// engine->ComputeLighting at a point: the averaged colour and the six box colours.
void SourceSharp_ClientComputeLighting( const float *pPosXYZ, float *pOutColorRGB, float *pOutBoxRGB18 )
{
	if ( !pPosXYZ )
		return;

	Vector vColor;
	Vector boxColors[6];

	engine->ComputeLighting( Vector( pPosXYZ[0], pPosXYZ[1], pPosXYZ[2] ), NULL, true, vColor, boxColors );

	if ( pOutColorRGB )
		VectorCopy( vColor, *reinterpret_cast< Vector * >( pOutColorRGB ) );
	if ( pOutBoxRGB18 )
	{
		for ( int i = 0; i < 6; i++ )
			VectorCopy( boxColors[i], *reinterpret_cast< Vector * >( pOutBoxRGB18 + i * 3 ) );
	}
}

// A hull trace against the world only (CTraceFilterWorldOnly). Returns the
// fraction; out: allsolid (bit 0) and startsolid (bit 1), the plane normal and
// distance.
float SourceSharp_ClientTraceHullWorld( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ,
                                        const float *pMaxsXYZ, int32_t nMask, int32_t *pOutSolidFlags,
                                        float *pOutPlaneXYZD )
{
	if ( !pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ )
		return 1.0f;

	CTraceFilterWorldOnly filter;
	trace_t tr;

	UTIL_TraceHull( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ), Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ), Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ),
	                (unsigned int)nMask, &filter, &tr );

	if ( pOutSolidFlags )
		*pOutSolidFlags = ( tr.allsolid ? 1 : 0 ) | ( tr.startsolid ? 2 : 0 );
	if ( pOutPlaneXYZD )
	{
		pOutPlaneXYZD[0] = tr.plane.normal.x;
		pOutPlaneXYZD[1] = tr.plane.normal.y;
		pOutPlaneXYZD[2] = tr.plane.normal.z;
		pOutPlaneXYZD[3] = tr.plane.dist;
	}

	return tr.fraction;
}
