//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE LEAF SYSTEM'S VIEW, GATHERED FOR MANAGED (C7 cluster 12, L1 of
// docs/plans/c7_leaf_system.md).
//
// Which renderables a view draws, the leaf each translucent one renders in,
// the back-to-front order and the view models' opaque/translucent split are
// managed's decisions (SourceSharp.Client's RenderableListBuilder). The
// registry they are made over is still stock's CClientLeafSystem until L2
// moves it. This header is the native half of the crossing: it READS the
// registry for one view into one length-prefixed buffer, asks the engine the
// questions managed's answer needs in the same pass, and then WRITES
// managed's answer into the CClientRenderablesList and the registry. It
// decides nothing: every `if` below is a bound, a validity guard or the
// once-per-frame-and-view ComputeFxBlend upkeep that stock's
// ComputeTranslucentRenderLeaf did before its decisions.
//
// THE REGISTRY IS READ THROUGH A LAYOUT MIRROR. CClientLeafSystem is declared
// in clientleafsystem.cpp alone, so nothing outside that file can name its
// members, and that file is the fork's. CLeafRegistryMirror below declares
// the same bases and the same data members, in the same order and of the same
// types, so the compiler lays it out as it lays the real class out. Two
// checks hold that: leafsystem_oracle_test's MirrorMatchesTheRealClass
// compares every member's offset against the pristine class (compiled into
// that test), and its FarmClassIsThePristineClass holds the farm's class text
// to the pristine's. At run time Gather() refuses a renderable whose
// RenderHandle() does not name the slot it was read from, and the executor
// falls back to stock's own BuildRenderablesList. L2 deletes the mirror with
// the stock registry.
//
// The same header is compiled in two places: into client.so by
// shell_view_executor_client.cpp, against the engine, and into
// leafsystem_oracle_test.cpp, against the oracle's stand-ins. Both include
// it after clientleafsystem.h and detailobjectsystem.h and the declarations
// of `engine` and `gpGlobals`.
//
// THE BUFFER (int32 words; floats are their IEEE bits). The managed reader is
// SourceSharp.Client.Render.LeafSystem.LeafViewInput; the two must agree, and
// the golden src/test/client/Fixtures/leafsystem-stock.tsv carries buffers
// this code wrote, which the managed facts decode.
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
// THE ANSWER (int32 words):
//
//   [0]  total words written
//   [1]  overflowed groups, a bit per group
//   then R rows of 3: render frame, render leaf, render frame 2, written back
//   then N, the entry count, and N entries of 4: group, source (a renderable
//        row; -1 the leaf's empty marker; -2-d detail model d), the world
//        list's leaf index, two-pass
//
// VIEW MODELS: in, a count V and V words of each view model's group after its
// ComputeFxBlend, in stock's order (m_ViewModels from the end); out, V words,
// 0 opaque and 1 translucent.
//
//=============================================================================

#ifndef SS_LEAF_VIEW_GATHER_H
#define SS_LEAF_VIEW_GATHER_H

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
	LEAFVIEW_STATE_WORDS      = 3,
	LEAFVIEW_ENTRY_WORDS      = 4,
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
	CUtlVector< int32_t >              m_Answer;      // sized for the worst answer

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

		// The answer's worst case: every row's state, two markers a leaf, each
		// renderable at most twice (translucent and its two-pass opaque copy)
		// and each detail model once.
		m_Answer.SetCount( 3 + LEAFVIEW_STATE_WORDS * m_Handles.Count()
		                   + LEAFVIEW_ENTRY_WORDS * ( 2 * world.m_LeafCount + 2 * m_Handles.Count() + m_Details.Count() ) );
		return true;
	}

	// Managed's answer, carried out: the registry's per-renderable state and
	// the list. False, with nothing written, when the answer is malformed (a
	// group, a source or a count out of range).
	bool Apply( IClientLeafSystem *pSystem, CClientRenderablesList &list, int &nOverflowGroups )
	{
		CLeafRegistryMirror &reg = *CLeafRegistryMirror::Of( pSystem );
		const int32_t *pOut = m_Answer.Base();
		const int nOut = pOut[0];
		const int nRows = m_Handles.Count();
		const int nEntriesAt = 2 + LEAFVIEW_STATE_WORDS * nRows;
		if ( nOut < nEntriesAt + 1 || nOut > m_Answer.Count() )
			return false;
		const int nEntries = pOut[nEntriesAt];
		if ( nEntries < 0 || nEntriesAt + 1 + LEAFVIEW_ENTRY_WORDS * nEntries != nOut )
			return false;

		int counts[ RENDER_GROUP_COUNT ] = {};
		for ( int e = 0, at = nEntriesAt + 1; e < nEntries; ++e, at += LEAFVIEW_ENTRY_WORDS )
		{
			const int group = pOut[at], source = pOut[at + 1];
			if ( group < 0 || group >= RENDER_GROUP_COUNT || ++counts[group] > CClientRenderablesList::MAX_GROUP_ENTITIES )
				return false;
			if ( source >= nRows || ( source <= -2 && -2 - source >= m_Details.Count() ) )
				return false;
		}

		nOverflowGroups = pOut[1];
		for ( int r = 0; r < nRows; ++r )
		{
			CLeafRegistryMirror::RenderableInfo_t &info = reg.m_Renderables[ m_Handles[r] ];
			const int32_t *pState = pOut + 2 + LEAFVIEW_STATE_WORDS * r;
			info.m_RenderFrame = pState[0];
			info.m_RenderLeaf = (unsigned int)pState[1];
			info.m_RenderFrame2 = pState[2];
		}

		for ( int g = 0; g < RENDER_GROUP_COUNT; ++g )
			list.m_RenderGroupCounts[g] = 0;
		for ( int e = 0, at = nEntriesAt + 1; e < nEntries; ++e, at += LEAFVIEW_ENTRY_WORDS )
		{
			const int group = pOut[at], source = pOut[at + 1];
			IClientRenderable *pRenderable = NULL;
			ClientRenderHandle_t handle = 0;
			if ( source >= 0 )
			{
				pRenderable = reg.m_Renderables[ m_Handles[source] ].m_pRenderable;
				handle = m_Handles[source];
			}
			else if ( source <= -2 )
			{
				pRenderable = m_Details[ -2 - source ];
				handle = DETAIL_PROP_RENDER_HANDLE;
			}
			CClientRenderablesList::CEntry &entry = list.m_RenderGroups[group][ list.m_RenderGroupCounts[group]++ ];
			entry.m_pRenderable = pRenderable;
			entry.m_iWorldListInfoLeaf = (unsigned short)pOut[at + 2];
			entry.m_TwoPass = (unsigned short)pOut[at + 3];
			entry.m_RenderHandle = handle;
		}
		return true;
	}

	// CollateViewModelRenderables' upkeep: each view model, from the end of the
	// list, computes its blend (which may take it off the list, as stock's
	// comment says), and its group after that is what managed splits on.
	CUtlVector< IClientRenderable * > m_ViewModels;
	CUtlVector< int32_t >             m_ViewModelWords;

	void GatherViewModels( IClientLeafSystem *pSystem )
	{
		CLeafRegistryMirror &reg = *CLeafRegistryMirror::Of( pSystem );
		m_ViewModels.RemoveAll();
		m_ViewModelWords.RemoveAll();
		m_ViewModelWords.AddToTail( 0 );
		for ( int i = reg.m_ViewModels.Count() - 1; i >= 0; --i )
		{
			CLeafRegistryMirror::RenderableInfo_t &renderable = reg.m_Renderables[ reg.m_ViewModels[i] ];
			renderable.m_pRenderable->ComputeFxBlend();
			m_ViewModels.AddToTail( renderable.m_pRenderable );
			m_ViewModelWords.AddToTail( renderable.m_RenderGroup );
		}
		m_ViewModelWords[0] = m_ViewModels.Count();
	}

	// out[i]: 0 opaque, 1 translucent, for m_ViewModels[i].
	void ApplyViewModels( const int32_t *pOut, CUtlVector< IClientRenderable * > &opaque, CUtlVector< IClientRenderable * > &translucent )
	{
		for ( int i = 0; i < m_ViewModels.Count(); ++i )
			( pOut[i] ? translucent : opaque ).AddToTail( m_ViewModels[i] );
	}

private:
	typedef CLeafRegistryMirror::ClientLeaf_t ClientLeaf_tRef_t;
};

#endif // SS_LEAF_VIEW_GATHER_H
