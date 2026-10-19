//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE ORACLE'S READING OF STOCK'S REGISTRY, one view at a time (C7 cluster
// 12; L1 of docs/plans/c7_leaf_system.md built it for client.so, and L2,
// which moved the registry to managed, left it to the oracle alone).
//
// leafsystem_oracle_test.cpp compiles the pristine CClientLeafSystem and
// writes, for each view, the VB row: stock's registry for that view and the
// engine's answers about every renderable in it, which
// TheScenarioReachesEveryArm names each arm of decisions 1, 2 and 5 from.
// CClientLeafSystem is declared in clientleafsystem.cpp alone, so nothing
// outside that file can name its members; CLeafRegistryMirror below declares
// the same bases and data members, in the same order and of the same types,
// so the compiler lays it out as it lays the real class out, and the oracle's
// MirrorMatchesTheRealClass holds every member's offset to the pristine
// class's. Nothing here is compiled into a game DLL.
//
// THE BUFFER (int32 words; floats are their IEEE bits), the golden's VB row
// (src/test/client/Fixtures/leafsystem-stock.tsv):
//
//   [0]  total words, this one included
//   [1]  render frame (SetupRenderInfo_t::m_nRenderFrame)
//   [2]  flags: 1 draw translucent, 2 draw detail, 4 the registry's
//        DrawStaticProps, 8 r_PortalTestEnts, 16 r_portalsopenall, 32 the
//        translucent-leaf pass runs (stock's ComputeTranslucentRenderLeaf)
//   [3]  cl_drawleaf (-1 when the build has no such filter)
//   [4]  render origin x y z, [7] render forward x y z
//   [10] leaves L, [11] renderables R, [12] occurrences E, [13] detail models D
//   then L leaves of 6: BSP leaf, first occurrence, occurrence count,
//        ShouldDrawDetailObjectsInLeaf, first detail model, detail model count
//   then E occurrences of 1: the renderable's row (stock's bucket order)
//   then R renderables of 24: handle, group, flags, render frame, render
//        leaf, render frame 2, area, GetFxBlend, engine answers (1 CullBox
//        culls, 2 DoesBoxTouchAreaFrustum, 4 IsOccluded), world mins, world
//        maxs, render mins, render maxs, render origin
//   then D detail models of 12: present, IsTransparent, GetFxBlend, render
//        mins, render maxs, render origin
//
//=============================================================================

#ifndef SS_LEAFSYSTEM_GATHER_H
#define SS_LEAFSYSTEM_GATHER_H

#include <stdint.h>
#include <string.h>

#include "tier1/utlvector.h"
#include "tier1/utllinkedlist.h"
#include "utlbidirectionalset.h"
#include "tier0/tslist.h"
#include "bsptreedata.h"

//-----------------------------------------------------------------------------
// CClientLeafSystem's data, as clientleafsystem.cpp declares it. Never made;
// a CClientLeafSystem is read as one. Change nothing here without the same
// change in the class, and the oracle's offsets will say whether they agree.
//-----------------------------------------------------------------------------
class CLeafRegistryMirror : public IClientLeafSystem, public ISpatialLeafEnumerator
{
public:
	// The render flags, as the class numbers them.
	enum { FLAG_TWOPASS = 0x01, FLAG_STATIC_PROP = 0x02, FLAG_BRUSH_MODEL = 0x04, FLAG_STUDIO_MODEL = 0x08,
	       FLAG_HASCHANGED = 0x10, FLAG_ALTERNATE_SORTING = 0x20 };

	// A renderable's slot: the renderable, its frame stamps, its leaf lists,
	// its flags and group, its first shadow, its area and the view its blend
	// was last computed for.
	struct RenderableInfo_t
	{
		IClientRenderable *m_pRenderable;
		int m_RenderFrame, m_RenderFrame2, m_EnumCount, m_TranslucencyCalculated;
		unsigned int m_LeafList, m_RenderLeaf;
		unsigned char m_Flags, m_RenderGroup;
		unsigned short m_FirstShadow;
		short m_Area;
		signed char m_TranslucencyCalculatedView;
	};

	// A leaf: its first renderable and shadow, its detail props and the frame
	// they were last drawn in, and the sub-systems' data.
	struct ClientLeaf_t
	{
		unsigned int m_FirstElement;
		unsigned short m_FirstShadow, m_FirstDetailProp, m_DetailPropCount;
		int m_DetailPropRenderFrame;
		CClientLeafSubSystemData *m_pSubSystemData[ N_CLSUBSYSTEMS ];
	};

	struct ShadowInfo_t
	{
		unsigned short m_FirstLeaf, m_FirstRenderable;
		int m_EnumCount;
		ClientShadowHandle_t m_Shadow;
		unsigned short m_Flags;
	};

	struct EnumResult_t { int leaf; EnumResult_t *pNext; };
	struct EnumResultList_t { EnumResult_t *pHead; ClientRenderHandle_t handle; };

	typedef CUtlLinkedList< RenderableInfo_t, ClientRenderHandle_t, false, unsigned int > RenderableTable_t;
	typedef CUtlLinkedList< ShadowInfo_t, ClientLeafShadowHandle_t, false, unsigned int > ShadowTable_t;
	typedef CBidirectionalSet< int, ClientRenderHandle_t, unsigned int, unsigned int > LeafRenderables_t;
	typedef CBidirectionalSet< int, ClientLeafShadowHandle_t, unsigned short, unsigned int > LeafShadows_t;
	typedef CBidirectionalSet< ClientRenderHandle_t, ClientLeafShadowHandle_t, unsigned short, unsigned int > RenderableShadows_t;

	CUtlVector< ClientLeaf_t >          m_Leaf;
	RenderableTable_t                   m_Renderables;
	ShadowTable_t                       m_Shadows;
	LeafRenderables_t                   m_RenderablesInLeaf;
	LeafShadows_t                       m_ShadowsInLeaf;
	RenderableShadows_t                 m_ShadowsOnRenderable;
	CUtlVector< ClientRenderHandle_t >  m_DirtyRenderables, m_ViewModels;
	bool                                m_DrawStaticProps, m_DrawSmallObjects;
	int                                 m_ShadowEnum;
	CTSList< EnumResultList_t >         m_DeferredInserts;

	static CLeafRegistryMirror *Of( IClientLeafSystem *pSystem ) { return static_cast< CLeafRegistryMirror * >( pSystem ); }
};

//-----------------------------------------------------------------------------
// One view's request, from the caller: what stock's SetupRenderInfo_t and
// ComputeTranslucentRenderLeaf's arguments carried, and the ConVars the
// decision reads.
//-----------------------------------------------------------------------------
struct LeafViewRequest_t
{
	const WorldListInfo_t *m_pWorldListInfo = NULL;
	int    m_nRenderFrame = 0;
	int    m_nDetailBuildFrame = 0;
	int    m_nGlobalFrame = 0;   // gpGlobals->framecount, the once-per-frame ComputeFxBlend stamp
	int    m_nViewID = 0;
	bool   m_bTranslucentLeafPass = false;
	bool   m_bDrawTranslucent = true;
	bool   m_bDrawDetail = true;
	bool   m_bPortalTestEnts = true;
	bool   m_bPortalsOpenAll = false;
	int    m_nDrawLeaf = -1;
	Vector m_vecOrigin;
	Vector m_vecForward;
};

enum
{
	LEAFVIEW_HEADER_WORDS     = 14,
	LEAFVIEW_LEAF_WORDS       = 6,
	LEAFVIEW_RENDERABLE_WORDS = 24,
	LEAFVIEW_DETAIL_WORDS     = 12,
	LEAFVIEW_ANSWER_CULLED    = 1,
	LEAFVIEW_ANSWER_AREA      = 2,
	LEAFVIEW_ANSWER_OCCLUDED  = 4,
};

//-----------------------------------------------------------------------------
// The gather. One per caller; its arrays are reused view to view.
//-----------------------------------------------------------------------------
class CLeafViewGather
{
public:
	// The buffer, and the native objects its rows stand for.
	CUtlVector< int32_t >              m_Words;
	CUtlVector< ClientRenderHandle_t > m_Handles;     // renderable row -> handle
	CUtlVector< IClientRenderable * >  m_Details;     // detail row -> model

	// The rows the view's occurrences name, by handle (cleared per view).
	CUtlVector< int32_t >              m_RowOfHandle;

	static int32_t Bits( float f ) { int32_t i; memcpy( &i, &f, sizeof( i ) ); return i; }

	void PutVector( const Vector &v ) { m_Words.AddToTail( Bits( v.x ) ); m_Words.AddToTail( Bits( v.y ) ); m_Words.AddToTail( Bits( v.z ) ); }

	// Stock ComputeTranslucentRenderLeaf's first loop, the upkeep its decisions
	// stand on: every renderable in the view's leaves computes its blend once
	// per frame and view. Its group may change here (a temp entity's does).
	void BlendPass( IClientLeafSystem *pSystem, const LeafViewRequest_t &req )
	{
		CLeafRegistryMirror &reg = *CLeafRegistryMirror::Of( pSystem );
		const WorldListInfo_t &world = *req.m_pWorldListInfo;
		for ( int i = 0; i < world.m_LeafCount; ++i )
		{
			const int leaf = world.m_pLeafList[i];
			for ( unsigned int idx = reg.m_RenderablesInLeaf.FirstElement( leaf ); idx != reg.m_RenderablesInLeaf.InvalidIndex();
			      idx = reg.m_RenderablesInLeaf.NextElement( idx ) )
			{
				CLeafRegistryMirror::RenderableInfo_t &info = reg.m_Renderables[ reg.m_RenderablesInLeaf.Element( idx ) ];
				if ( info.m_TranslucencyCalculated != req.m_nGlobalFrame || info.m_TranslucencyCalculatedView != req.m_nViewID )
				{
					info.m_pRenderable->ComputeFxBlend();
					info.m_TranslucencyCalculated = req.m_nGlobalFrame;
					info.m_TranslucencyCalculatedView = (signed char)req.m_nViewID;
				}
			}
		}
	}

	// The view into m_Words. False when a renderable's handle does not name the
	// slot it was read from: the mirror and the class disagree, and the caller
	// must not use the buffer.
	bool Gather( IClientLeafSystem *pSystem, const LeafViewRequest_t &req )
	{
		CLeafRegistryMirror &reg = *CLeafRegistryMirror::Of( pSystem );
		const WorldListInfo_t &world = *req.m_pWorldListInfo;

		if ( req.m_bTranslucentLeafPass )
			BlendPass( pSystem, req );

		m_Words.RemoveAll();
		m_Handles.RemoveAll();
		m_Details.RemoveAll();
		m_RowOfHandle.SetCount( reg.m_Renderables.MaxElementIndex() + 1 );
		for ( int i = 0; i < m_RowOfHandle.Count(); ++i )
			m_RowOfHandle[i] = -1;

		// The occurrences first, in stock's order, numbering each renderable
		// the first time it is met; the leaves' detail ranges beside them.
		CUtlVector< int32_t > leaves, occurrences, detailIndex;
		for ( int i = 0; i < world.m_LeafCount; ++i )
		{
			const int leaf = world.m_pLeafList[i];
			leaves.AddToTail( leaf );
			leaves.AddToTail( occurrences.Count() );
			int nInLeaf = 0;
			for ( unsigned int idx = reg.m_RenderablesInLeaf.FirstElement( leaf ); idx != reg.m_RenderablesInLeaf.InvalidIndex();
			      idx = reg.m_RenderablesInLeaf.NextElement( idx ) )
			{
				const ClientRenderHandle_t h = reg.m_RenderablesInLeaf.Element( idx );
				if ( m_RowOfHandle[h] < 0 )
				{
					m_RowOfHandle[h] = m_Handles.Count();
					m_Handles.AddToTail( h );
				}
				occurrences.AddToTail( m_RowOfHandle[h] );
				++nInLeaf;
			}
			leaves.AddToTail( nInLeaf );

			const bool bDetail = pSystem->ShouldDrawDetailObjectsInLeaf( leaf, req.m_nDetailBuildFrame );
			leaves.AddToTail( bDetail ? 1 : 0 );
			leaves.AddToTail( m_Details.Count() );
			int nDetail = 0;
			if ( bDetail )
			{
				const ClientLeaf_tRef_t &leafInfo = reg.m_Leaf[leaf];
				for ( int k = 0; k < leafInfo.m_DetailPropCount; ++k )
				{
					m_Details.AddToTail( DetailObjectSystem()->GetDetailModel( leafInfo.m_FirstDetailProp + k ) );
					++nDetail;
				}
			}
			leaves.AddToTail( nDetail );
		}

		int nFlags = 0;
		nFlags |= req.m_bDrawTranslucent ? 1 : 0;
		nFlags |= req.m_bDrawDetail ? 2 : 0;
		nFlags |= reg.m_DrawStaticProps ? 4 : 0;
		nFlags |= req.m_bPortalTestEnts ? 8 : 0;
		nFlags |= req.m_bPortalsOpenAll ? 16 : 0;
		nFlags |= req.m_bTranslucentLeafPass ? 32 : 0;

		m_Words.AddToTail( 0 );
		m_Words.AddToTail( req.m_nRenderFrame );
		m_Words.AddToTail( nFlags );
		m_Words.AddToTail( req.m_nDrawLeaf );
		PutVector( req.m_vecOrigin );
		PutVector( req.m_vecForward );
		m_Words.AddToTail( world.m_LeafCount );
		m_Words.AddToTail( m_Handles.Count() );
		m_Words.AddToTail( occurrences.Count() );
		m_Words.AddToTail( m_Details.Count() );
		m_Words.AddMultipleToTail( leaves.Count(), leaves.Base() );
		m_Words.AddMultipleToTail( occurrences.Count(), occurrences.Base() );

		for ( int r = 0; r < m_Handles.Count(); ++r )
		{
			const ClientRenderHandle_t h = m_Handles[r];
			const CLeafRegistryMirror::RenderableInfo_t &info = reg.m_Renderables[h];
			IClientRenderable *pRenderable = info.m_pRenderable;
			if ( !pRenderable || pRenderable->RenderHandle() != h )
				return false;

			Vector absMins, absMaxs, mins, maxs;
			pRenderable->GetRenderBoundsWorldspace( absMins, absMaxs );
			pRenderable->GetRenderBounds( mins, maxs );
			const Vector origin = pRenderable->GetRenderOrigin();

			int nAnswers = 0;
			nAnswers |= engine->CullBox( absMins, absMaxs ) ? LEAFVIEW_ANSWER_CULLED : 0;
			if ( info.m_Area != -1 )
				nAnswers |= engine->DoesBoxTouchAreaFrustum( absMins, absMaxs, info.m_Area ) ? LEAFVIEW_ANSWER_AREA : 0;
			nAnswers |= engine->IsOccluded( absMins, absMaxs ) ? LEAFVIEW_ANSWER_OCCLUDED : 0;

			m_Words.AddToTail( h );
			m_Words.AddToTail( info.m_RenderGroup );
			m_Words.AddToTail( info.m_Flags );
			m_Words.AddToTail( info.m_RenderFrame );
			m_Words.AddToTail( (int32_t)info.m_RenderLeaf );
			m_Words.AddToTail( info.m_RenderFrame2 );
			m_Words.AddToTail( info.m_Area );
			m_Words.AddToTail( pRenderable->GetFxBlend() );
			m_Words.AddToTail( nAnswers );
			PutVector( absMins );
			PutVector( absMaxs );
			PutVector( mins );
			PutVector( maxs );
			PutVector( origin );
		}

		for ( int d = 0; d < m_Details.Count(); ++d )
		{
			IClientRenderable *pDetail = m_Details[d];
			Vector mins( 0, 0, 0 ), maxs( 0, 0, 0 ), origin( 0, 0, 0 );
			m_Words.AddToTail( pDetail ? 1 : 0 );
			m_Words.AddToTail( ( pDetail && pDetail->IsTransparent() ) ? 1 : 0 );
			m_Words.AddToTail( pDetail ? pDetail->GetFxBlend() : 0 );
			if ( pDetail )
			{
				pDetail->GetRenderBounds( mins, maxs );
				origin = pDetail->GetRenderOrigin();
			}
			PutVector( mins );
			PutVector( maxs );
			PutVector( origin );
		}

		m_Words[0] = m_Words.Count();
		return true;
	}

private:
	typedef CLeafRegistryMirror::ClientLeaf_t ClientLeaf_tRef_t;
};

#endif // SS_LEAFSYSTEM_GATHER_H
