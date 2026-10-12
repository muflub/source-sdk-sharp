//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets

//========= SourceSharp â one line off stock, reason (d) ====================//
//
// This file is the mdl LAYOUT -- CStudioHdr over studiohdr_t and
// virtualmodel_t -- and it was relocated under src/sourcesharp/ at 041ecd5ea
// for the "only sourcesharp/ compiles" rule, byte-identical to stock.
//
// IT IS NO LONGER BYTE-IDENTICAL, and this is the whole of the difference:
// mod_load_showstall, a load-hitch diagnostic read at three sites below, was a
// `static ConVar` here and is a managed declaration now
// (SourceSharp.Server/AnimationConVars.cs and SourceSharp.Client's copy),
// read through CManagedConVarRef. plan_shim.md Â§6: no ConVar's default or
// semantics is declared natively unless the engine resolves the name, and this
// one is a developer's hitch counter.
//
// Declared on BOTH sides because it already was: studio.cpp is a row in both
// _csharp.vpc file lists, so today's build constructs this ConVar once in
// server.so and once in client.so. Moving it to one side would have been a
// behaviour change dressed up as a port.
//
//=============================================================================//
#include "studio.h"
#include "datacache/idatacache.h"
#include "datacache/imdlcache.h"
#include "convar.h"
#include "managed_convar_ref.h"
#include "ss_clock.h"


// How large the stack is for flex rules
#define STUDIO_FLEX_STACK 32

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
mstudioanimdesc_t &studiohdr_t::pAnimdesc( int i ) const
{ 
	if (numincludemodels == 0)
	{
		return *pLocalAnimdesc( i );
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_anim[i].group ];
	const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();
	Assert( pStudioHdr );

	return *pStudioHdr->pLocalAnimdesc( pVModel->m_anim[i].index );
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

mstudioanim_t *mstudioanimdesc_t::pAnimBlock( int block, int index ) const
{
	if (block == -1)
	{
		return (mstudioanim_t *)NULL;
	}
	if (block == 0)
	{
		return (mstudioanim_t *)(((byte *)this) + index);
	}

	byte *pAnimBlock = pStudiohdr()->GetAnimBlock( block );
	if ( pAnimBlock )
	{
		return (mstudioanim_t *)(pAnimBlock + index);
	}

	return (mstudioanim_t *)NULL;
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

static CManagedConVarRef mod_load_showstall( "mod_load_showstall" );
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
mstudioanim_t *mstudioanimdesc_t::pAnim( int *piFrame ) const
{
	float flStall;
	return pAnim( piFrame, flStall );
}
#endif

mstudioanim_t *mstudioanimdesc_t::pAnim( int *piFrame, float &flStall ) const
{
	mstudioanim_t *panim = NULL;

	int block = animblock;
	int index = animindex;
	int section = 0;

	if (sectionframes != 0)
	{
		if (numframes > sectionframes && *piFrame == numframes - 1)
		{
			// last frame on long anims is stored separately
			*piFrame = 0;
			section = (numframes / sectionframes) + 1;
		}
		else
		{
			section = *piFrame / sectionframes;
			*piFrame -= section * sectionframes;
		}

		block = pSection( section )->animblock;
		index = pSection( section )->animindex;
	}

	if (block == -1)
	{
		// model needs to be recompiled
		return NULL;
	}

	panim = pAnimBlock( block, index );

	// force a preload on the next block
	if ( sectionframes != 0 )
	{
		int count = ( numframes / sectionframes) + 2;
		for ( int i = section + 1; i < count; i++ )
		{
			if ( pSection( i )->animblock != block )
			{
				pAnimBlock( pSection( i )->animblock, pSection( i )->animindex );
				break;
			}
		}
	}

	if (panim == NULL)
	{
		if (section > 0 && mod_load_showstall.GetInt() > 0)
		{
			Msg("[%8.3f] hitch on %s:%s:%d:%d\n", ss::SecondsSinceStart(), pStudiohdr()->pszName(), pszName(), section, block );
		}
		// back up until a previously loaded block is found
		while (--section >= 0)
		{
			block = pSection( section )->animblock;
			index = pSection( section )->animindex;
			panim = pAnimBlock( block, index );
			if (panim)
			{
				// set it to the last frame in the last valid section
				*piFrame = sectionframes - 1;
				break;
			}
		}
	}

	// try to guess a valid stall time interval (tuned for the X360)
	flStall = 0.0f;
	if (panim == NULL && section <= 0)
	{
		zeroframestalltime = ss::SecondsSinceStart();
		flStall = 1.0f;
	}
	else if (panim != NULL && zeroframestalltime != 0.0f)
	{
		float dt = ss::SecondsSinceStart() - zeroframestalltime;
		if (dt >= 0.0)
		{
			flStall = SimpleSpline( clamp( (0.200f - dt) * 5.0f, 0.0f, 1.0f ) );
		}

		if (flStall == 0.0f)
		{
			// disable stalltime
			zeroframestalltime = 0.0f;
		}
		else if (mod_load_showstall.GetInt() > 1)
		{
			Msg("[%8.3f] stall blend %.2f on %s:%s:%d:%d\n", ss::SecondsSinceStart(), flStall, pStudiohdr()->pszName(), pszName(), section, block );
		}
	}

	if (panim == NULL && mod_load_showstall.GetInt() > 1)
	{
		Msg("[%8.3f] stall on %s:%s:%d:%d\n", ss::SecondsSinceStart(), pStudiohdr()->pszName(), pszName(), section, block );
	}

	return panim;
}

mstudioikrule_t *mstudioanimdesc_t::pIKRule( int i ) const
{
	if (ikruleindex)
	{
		return (mstudioikrule_t *)(((byte *)this) + ikruleindex) + i;
	}
	else if (animblockikruleindex)
	{
		if (animblock == 0)
		{
			return  (mstudioikrule_t *)(((byte *)this) + animblockikruleindex) + i;
		}
		else
		{
			byte *pAnimBlocks = pStudiohdr()->GetAnimBlock( animblock );
			
			if ( pAnimBlocks )
			{
				return (mstudioikrule_t *)(pAnimBlocks + animblockikruleindex) + i;
			}
		}
	}

	return NULL;
}


mstudiolocalhierarchy_t *mstudioanimdesc_t::pHierarchy( int i ) const
{
	if (localhierarchyindex)
	{
		if (animblock == 0)
		{
			return  (mstudiolocalhierarchy_t *)(((byte *)this) + localhierarchyindex) + i;
		}
		else
		{
			byte *pAnimBlocks = pStudiohdr()->GetAnimBlock( animblock );
			
			if ( pAnimBlocks )
			{
				return (mstudiolocalhierarchy_t *)(pAnimBlocks + localhierarchyindex) + i;
			}
		}
	}

	return NULL;
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
bool studiohdr_t::SequencesAvailable() const
{
	if (numincludemodels == 0)
	{
		return true;
	}

	return ( GetVirtualModel() != NULL );
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int studiohdr_t::GetNumSeq( void ) const
{
	if (numincludemodels == 0)
	{
		return numlocalseq;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );
	return pVModel->m_seq.Count();
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

mstudioseqdesc_t &studiohdr_t::pSeqdesc( int i ) const
{
	if (numincludemodels == 0)
	{
		return *pLocalSeqdesc( i );
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	if ( !pVModel )
	{
		return *pLocalSeqdesc( i );
	}

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_seq[i].group ];
	const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();
	Assert( pStudioHdr );

	return *pStudioHdr->pLocalSeqdesc( pVModel->m_seq[i].index );
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::iRelativeAnim( int baseseq, int relanim ) const
{
	if (numincludemodels == 0)
	{
		return relanim;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_seq[baseseq].group ];

	return pGroup->masterAnim[ relanim ];
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::iRelativeSeq( int baseseq, int relseq ) const
{
	if (numincludemodels == 0)
	{
		return relseq;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_seq[baseseq].group ];

	return pGroup->masterSeq[ relseq ];
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::GetNumPoseParameters( void ) const
{
	if (numincludemodels == 0)
	{
		return numlocalposeparameters;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	return pVModel->m_pose.Count();
}
#endif



//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
const mstudioposeparamdesc_t &studiohdr_t::pPoseParameter( int i )
{
	if (numincludemodels == 0)
	{
		return *pLocalPoseParameter( i );
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	if ( pVModel->m_pose[i].group == 0)
		return *pLocalPoseParameter( pVModel->m_pose[i].index );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_pose[i].group ];

	const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();
	Assert( pStudioHdr );

	return *pStudioHdr->pLocalPoseParameter( pVModel->m_pose[i].index );
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::GetSharedPoseParameter( int iSequence, int iLocalPose ) const
{
	if (numincludemodels == 0)
	{
		return iLocalPose;
	}

	if (iLocalPose == -1)
		return iLocalPose;

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	int group = pVModel->m_seq[iSequence].group;
	virtualgroup_t* pGroup = pVModel->m_group.IsValidIndex( group ) ? &pVModel->m_group[group] : NULL;

	// Josh: This used to return iLocalPose when it was out of bounds
	// but that is not correct, this should return -1 because
	// otherwise it's just some random unrelated index. 
	if ( !pGroup )
		return -1;

	// Josh: Sometimes the Sniper can try to eat a gun and we hit this.
	// Model pose group data is just complete garbage out of studiomdl, woo!
	if ( !pGroup->masterPose.IsValidIndex( iLocalPose ) )
		return -1;

	return pGroup->masterPose[iLocalPose];
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::EntryNode( int iSequence )
{
	mstudioseqdesc_t &seqdesc = pSeqdesc( iSequence );

	if (numincludemodels == 0 || seqdesc.localentrynode == 0)
	{
		return seqdesc.localentrynode;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_seq[iSequence].group ];

	return pGroup->masterNode[seqdesc.localentrynode-1]+1;
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------


#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::ExitNode( int iSequence )
{
	mstudioseqdesc_t &seqdesc = pSeqdesc( iSequence );

	if (numincludemodels == 0 || seqdesc.localexitnode == 0)
	{
		return seqdesc.localexitnode;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_seq[iSequence].group ];

	return pGroup->masterNode[seqdesc.localexitnode-1]+1;
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::GetNumAttachments( void ) const
{
	if (numincludemodels == 0)
	{
		return numlocalattachments;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	return pVModel->m_attachment.Count();
}
#endif



//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
const mstudioattachment_t &studiohdr_t::pAttachment( int i ) const
{
	if (numincludemodels == 0)
	{
		return *pLocalAttachment( i );
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_attachment[i].group ];
	const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();
	Assert( pStudioHdr );

	return *pStudioHdr->pLocalAttachment( pVModel->m_attachment[i].index );
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::GetAttachmentBone( int i )
{
	const mstudioattachment_t &attachment = pAttachment( i );

	// remap bone
	virtualmodel_t *pVModel = GetVirtualModel();
	if (pVModel)
	{
		virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_attachment[i].group ];
		int iBone = pGroup->masterBone[attachment.localbone];
		if (iBone == -1)
			return 0;
		return iBone;
	}
	return attachment.localbone;
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void studiohdr_t::SetAttachmentBone( int iAttachment, int iBone )
{
	mstudioattachment_t &attachment = (mstudioattachment_t &)pAttachment( iAttachment );

	// remap bone
	virtualmodel_t *pVModel = GetVirtualModel();
	if (pVModel)
	{
		virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_attachment[iAttachment].group ];
		iBone = pGroup->boneMap[iBone];
	}
	attachment.localbone = iBone;
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
char *studiohdr_t::pszNodeName( int iNode )
{
	if (numincludemodels == 0)
	{
		return pszLocalNodeName( iNode );
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	if ( pVModel->m_node.Count() <= iNode-1 )
		return "Invalid node";

	return pVModel->m_group[ pVModel->m_node[iNode-1].group ].GetStudioHdr()->pszLocalNodeName( pVModel->m_node[iNode-1].index );
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::GetTransition( int iFrom, int iTo ) const
{
	if (numincludemodels == 0)
	{
		return *pLocalTransition( (iFrom-1)*numlocalnodes + (iTo - 1) );
	}

	return iTo;
	/*
	FIXME: not connected
	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	return pVModel->m_transition.Element( iFrom ).Element( iTo );
	*/
}
#endif


#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::GetActivityListVersion( void )
{
	if (numincludemodels == 0)
	{
		return activitylistversion;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	int ActVersion = activitylistversion;

	int i;
	for (i = 1; i < pVModel->m_group.Count(); i++)
	{
		virtualgroup_t *pGroup = &pVModel->m_group[ i ];
		const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();

		Assert( pStudioHdr );

		ActVersion = min( ActVersion, pStudioHdr->activitylistversion );
	}

	return ActVersion;
}
#endif

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void studiohdr_t::SetActivityListVersion( int ActVersion ) const
{
	activitylistversion = ActVersion;

	if (numincludemodels == 0)
	{
		return;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	int i;
	for (i = 1; i < pVModel->m_group.Count(); i++)
	{
		virtualgroup_t *pGroup = &pVModel->m_group[ i ];
		const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();

		Assert( pStudioHdr );

		pStudioHdr->SetActivityListVersion( ActVersion );
	}
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------


#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int studiohdr_t::GetNumIKAutoplayLocks( void ) const
{
	if (numincludemodels == 0)
	{
		return numlocalikautoplaylocks;
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	return pVModel->m_iklock.Count();
}
#endif

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
const mstudioiklock_t &studiohdr_t::pIKAutoplayLock( int i )
{
	if (numincludemodels == 0)
	{
		return *pLocalIKAutoplayLock( i );
	}

	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	virtualgroup_t *pGroup = &pVModel->m_group[ pVModel->m_iklock[i].group ];
	const studiohdr_t *pStudioHdr = pGroup->GetStudioHdr();
	Assert( pStudioHdr );

	return *pStudioHdr->pLocalIKAutoplayLock( pVModel->m_iklock[i].index );
}
#endif

int	studiohdr_t::CountAutoplaySequences() const
{
	int count = 0;
	for (int i = 0; i < GetNumSeq(); i++)
	{
		mstudioseqdesc_t &seqdesc = pSeqdesc( i );
		if (seqdesc.flags & STUDIO_AUTOPLAY)
		{
			count++;
		}
	}
	return count;
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::CopyAutoplaySequences( unsigned short *pOut, int outCount ) const
{
	int outIndex = 0;
	for (int i = 0; i < GetNumSeq() && outIndex < outCount; i++)
	{
		mstudioseqdesc_t &seqdesc = pSeqdesc( i );
		if (seqdesc.flags & STUDIO_AUTOPLAY)
		{
			pOut[outIndex] = i;
			outIndex++;
		}
	}
	return outIndex;
}
#endif

//-----------------------------------------------------------------------------
// Purpose:	maps local sequence bone to global bone
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::RemapSeqBone( int iSequence, int iLocalBone ) const	
{
	// remap bone
	virtualmodel_t *pVModel = GetVirtualModel();
	if (pVModel)
	{
		const virtualgroup_t *pSeqGroup = pVModel->pSeqGroup( iSequence );
		return pSeqGroup->masterBone[iLocalBone];
	}
	return iLocalBone;
}
#endif

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	studiohdr_t::RemapAnimBone( int iAnim, int iLocalBone ) const
{
	// remap bone
	virtualmodel_t *pVModel = GetVirtualModel();
	if (pVModel)
	{
		const virtualgroup_t *pAnimGroup = pVModel->pAnimGroup( iAnim );
		return pAnimGroup->masterBone[iLocalBone];
	}
	return iLocalBone;
}
#endif






//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

CStudioHdr::CStudioHdr( void ) 
{
	// set pointer to bogus value
	m_nFrameUnlockCounter = 0;
	m_pFrameUnlockCounter = &m_nFrameUnlockCounter;
	Init( NULL );
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
CStudioHdr::CStudioHdr( const studiohdr_t *pStudioHdr, IMDLCache *mdlcache ) 
{
	// preset pointer to bogus value (it may be overwritten with legitimate data later)
	m_nFrameUnlockCounter = 0;
	m_pFrameUnlockCounter = &m_nFrameUnlockCounter;
	Init( pStudioHdr, mdlcache );
}
#endif


// extern IDataCache *g_pDataCache;

void CStudioHdr::Init( const studiohdr_t *pStudioHdr, IMDLCache *mdlcache )
{
	m_pStudioHdr = pStudioHdr;

	m_pVModel = NULL;
	m_pStudioHdrCache.RemoveAll();

	if (m_pStudioHdr == NULL)
	{
		return;
	}

	if ( mdlcache )
	{
		m_pFrameUnlockCounter = mdlcache->GetFrameUnlockCounterPtr( MDLCACHE_STUDIOHDR );
		m_nFrameUnlockCounter = *m_pFrameUnlockCounter - 1;
	}

	if (m_pStudioHdr->numincludemodels == 0)
	{
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
#else
		m_ActivityToSequence.Initialize(this);
#endif
	}
	else
	{
		ResetVModel( m_pStudioHdr->GetVirtualModel() );
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
#else
		m_ActivityToSequence.Initialize(this);
#endif
	}

	m_boneFlags.EnsureCount( numbones() );
	m_boneParent.EnsureCount( numbones() );
	for (int i = 0; i < numbones(); i++)
	{
		m_boneFlags[i] = pBone( i )->flags;
		m_boneParent[i] = pBone( i )->parent;
	}
}

void CStudioHdr::Term()
{
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

bool CStudioHdr::SequencesAvailable() const
{
	if (m_pStudioHdr->numincludemodels == 0)
	{
		return true;
	}

	if (m_pVModel == NULL)
	{
		// repoll m_pVModel
		return (ResetVModel( m_pStudioHdr->GetVirtualModel() ) != NULL);
	}
	else
		return true;
}


const virtualmodel_t * CStudioHdr::ResetVModel( const virtualmodel_t *pVModel ) const
{
	if (pVModel != NULL)
	{
		m_pVModel = (virtualmodel_t *)pVModel;
		Assert( !pVModel->m_Lock.GetOwnerId() );
		m_pStudioHdrCache.SetCount( m_pVModel->m_group.Count() );

		int i;
		for (i = 0; i < m_pStudioHdrCache.Count(); i++)
		{
			m_pStudioHdrCache[ i ] = NULL;
		}
		
		return const_cast<virtualmodel_t *>(pVModel);
	}
	else
	{
		m_pVModel = NULL;
		return NULL;
	}
}

const studiohdr_t *CStudioHdr::GroupStudioHdr( int i )
{
	if ( !this )
	{
		ExecuteNTimes( 5, Warning( "Call to NULL CStudioHdr::GroupStudioHdr()\n" ) );
	}

	if ( m_nFrameUnlockCounter != *m_pFrameUnlockCounter )
	{
		m_FrameUnlockCounterMutex.Lock();
		if ( *m_pFrameUnlockCounter != m_nFrameUnlockCounter ) // i.e., this thread got the mutex
		{
			memset( m_pStudioHdrCache.Base(), 0, m_pStudioHdrCache.Count() * sizeof(studiohdr_t *) );
			m_nFrameUnlockCounter = *m_pFrameUnlockCounter;
		}
		m_FrameUnlockCounterMutex.Unlock();
	}

	if ( !m_pStudioHdrCache.IsValidIndex( i ) )
	{
		const char *pszName = ( m_pStudioHdr ) ? m_pStudioHdr->pszName() : "<<null>>";
		ExecuteNTimes( 5, Warning( "Invalid index passed to CStudioHdr(%s)::GroupStudioHdr(): %d, but max is %d\n", pszName, i, m_pStudioHdrCache.Count() ) );
		DebuggerBreakIfDebugging();
		return m_pStudioHdr; // return something known to probably exist, certainly things will be messed up, but hopefully not crash before the warning is noticed
	}

	const studiohdr_t *pStudioHdr = m_pStudioHdrCache[ i ];

	if (pStudioHdr == NULL)
	{
		Assert( !m_pVModel->m_Lock.GetOwnerId() );
		virtualgroup_t *pGroup = &m_pVModel->m_group[ i ];
		pStudioHdr = pGroup->GetStudioHdr();
		m_pStudioHdrCache[ i ] = pStudioHdr;
	}

	Assert( pStudioHdr );
	return pStudioHdr;
}


const studiohdr_t *CStudioHdr::pSeqStudioHdr( int sequence )
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr;
	}

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_seq[sequence].group );

	return pStudioHdr;
}


const studiohdr_t *CStudioHdr::pAnimStudioHdr( int animation )
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr;
	}

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_anim[animation].group );

	return pStudioHdr;
}



mstudioanimdesc_t &CStudioHdr::pAnimdesc( int i )
{ 
	if (m_pVModel == NULL)
	{
		return *m_pStudioHdr->pLocalAnimdesc( i );
	}

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_anim[i].group );

	return *pStudioHdr->pLocalAnimdesc( m_pVModel->m_anim[i].index );
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int CStudioHdr::GetNumSeq( void ) const
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr->numlocalseq;
	}

	return m_pVModel->m_seq.Count();
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

mstudioseqdesc_t &CStudioHdr::pSeqdesc( int i )
{
	Assert( ( i >= 0 && i < GetNumSeq() ) || ( i == 1 && GetNumSeq() <= 1 ) );
	if ( i < 0 || i >= GetNumSeq() )
	{
		if ( GetNumSeq() <= 0 )
		{
			// Return a zero'd out struct reference if we've got nothing.
			// C_BaseObject::StopAnimGeneratedSounds was crashing due to this function
			//	returning a reference to garbage. It should now see numevents is 0,
			//	and bail.
			static mstudioseqdesc_t s_nil_seq;
			return s_nil_seq;
		}

		// Avoid reading random memory.
		i = 0;
	}
	
	if (m_pVModel == NULL)
	{
		return *m_pStudioHdr->pLocalSeqdesc( i );
	}

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_seq[i].group );

	return *pStudioHdr->pLocalSeqdesc( m_pVModel->m_seq[i].index );
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int CStudioHdr::iRelativeAnim( int baseseq, int relanim ) const
{
	if (m_pVModel == NULL)
	{
		return relanim;
	}

	virtualgroup_t *pGroup = &m_pVModel->m_group[ m_pVModel->m_seq[baseseq].group ];

	return pGroup->masterAnim[ relanim ];
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int CStudioHdr::iRelativeSeq( int baseseq, int relseq ) const
{
	if (m_pVModel == NULL)
	{
		return relseq;
	}

	Assert( m_pVModel );

	virtualgroup_t *pGroup = &m_pVModel->m_group[ m_pVModel->m_seq[baseseq].group ];

	return pGroup->masterSeq[ relseq ];
}


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int	CStudioHdr::GetNumPoseParameters( void ) const
{
	if (m_pVModel == NULL)
	{
		if ( m_pStudioHdr )
			return m_pStudioHdr->numlocalposeparameters;
		else
			return 0;
	}

	Assert( m_pVModel );

	return m_pVModel->m_pose.Count();
}



//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

const mstudioposeparamdesc_t &CStudioHdr::pPoseParameter( int i )
{
	if (m_pVModel == NULL)
	{
		return *m_pStudioHdr->pLocalPoseParameter( i );
	}

	if ( m_pVModel->m_pose[i].group == 0)
		return *m_pStudioHdr->pLocalPoseParameter( m_pVModel->m_pose[i].index );

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_pose[i].group );

	return *pStudioHdr->pLocalPoseParameter( m_pVModel->m_pose[i].index );
}


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int CStudioHdr::GetSharedPoseParameter( int iSequence, int iLocalPose ) const
{
	if (m_pVModel == NULL)
	{
		return iLocalPose;
	}

	if (iLocalPose == -1)
		return iLocalPose;

	Assert( m_pVModel );

	int group = m_pVModel->m_seq[iSequence].group;
	virtualgroup_t *pGroup = m_pVModel->m_group.IsValidIndex( group ) ? &m_pVModel->m_group[ group ] : NULL;

	// Josh: This used to return iLocalPose when it was out of bounds
	// but that is not correct, this should return -1 because
	// otherwise it's just some random unrelated index. 
	if ( !pGroup )
		return -1;

	// Josh: Sometimes the Sniper can try to eat a gun and we hit this.
	// Model pose group data is just complete garbage out of studiomdl, woo!
	if ( !pGroup->masterPose.IsValidIndex( iLocalPose ) )
		return -1;

	return pGroup->masterPose[iLocalPose];

}


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int CStudioHdr::EntryNode( int iSequence )
{
	mstudioseqdesc_t &seqdesc = pSeqdesc( iSequence );

	if (m_pVModel == NULL || seqdesc.localentrynode == 0)
	{
		return seqdesc.localentrynode;
	}

	Assert( m_pVModel );

	virtualgroup_t *pGroup = &m_pVModel->m_group[ m_pVModel->m_seq[iSequence].group ];

	return pGroup->masterNode[seqdesc.localentrynode-1]+1;
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------


#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int CStudioHdr::ExitNode( int iSequence )
{
	mstudioseqdesc_t &seqdesc = pSeqdesc( iSequence );

	if (m_pVModel == NULL || seqdesc.localexitnode == 0)
	{
		return seqdesc.localexitnode;
	}

	Assert( m_pVModel );

	virtualgroup_t *pGroup = &m_pVModel->m_group[ m_pVModel->m_seq[iSequence].group ];

	return pGroup->masterNode[seqdesc.localexitnode-1]+1;
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int	CStudioHdr::GetNumAttachments( void ) const
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr->numlocalattachments;
	}

	Assert( m_pVModel );

	return m_pVModel->m_attachment.Count();
}



//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

const mstudioattachment_t &CStudioHdr::pAttachment( int i )
{
	if (m_pVModel == NULL)
	{
		return *m_pStudioHdr->pLocalAttachment( i );
	}

	Assert( m_pVModel );

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_attachment[i].group );

	return *pStudioHdr->pLocalAttachment( m_pVModel->m_attachment[i].index );
}

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

int	CStudioHdr::GetAttachmentBone( int i )
{
	if (m_pVModel == 0)
	{
		return m_pStudioHdr->pLocalAttachment( i )->localbone;
	}

	virtualgroup_t *pGroup = &m_pVModel->m_group[ m_pVModel->m_attachment[i].group ];
	const mstudioattachment_t &attachment = pAttachment( i );
	int iBone = pGroup->masterBone[attachment.localbone];
	if (iBone == -1)
		return 0;
	return iBone;
}


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void CStudioHdr::SetAttachmentBone( int iAttachment, int iBone )
{
	mstudioattachment_t &attachment = (mstudioattachment_t &)m_pStudioHdr->pAttachment( iAttachment );

	// remap bone
	if (m_pVModel)
	{
		virtualgroup_t *pGroup = &m_pVModel->m_group[ m_pVModel->m_attachment[iAttachment].group ];
		iBone = pGroup->boneMap[iBone];
	}
	attachment.localbone = iBone;
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
char *CStudioHdr::pszNodeName( int iNode )
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr->pszLocalNodeName( iNode );
	}

	if ( m_pVModel->m_node.Count() <= iNode-1 )
		return "Invalid node";

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_node[iNode-1].group );
	
	return pStudioHdr->pszLocalNodeName( m_pVModel->m_node[iNode-1].index );
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int CStudioHdr::GetTransition( int iFrom, int iTo ) const
{
	if (m_pVModel == NULL)
	{
		return *m_pStudioHdr->pLocalTransition( (iFrom-1)*m_pStudioHdr->numlocalnodes + (iTo - 1) );
	}

	return iTo;
	/*
	FIXME: not connected
	virtualmodel_t *pVModel = (virtualmodel_t *)GetVirtualModel();
	Assert( pVModel );

	return pVModel->m_transition.Element( iFrom ).Element( iTo );
	*/
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	CStudioHdr::GetActivityListVersion( void )
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr->activitylistversion;
	}

	int version = m_pStudioHdr->activitylistversion;

	int i;
	for (i = 1; i < m_pVModel->m_group.Count(); i++)
	{
		const studiohdr_t *pStudioHdr = GroupStudioHdr( i );
		Assert( pStudioHdr );
		version = min( version, pStudioHdr->activitylistversion );
	}

	return version;
}
#endif

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void CStudioHdr::SetActivityListVersion( int version )
{
	m_pStudioHdr->activitylistversion = version;

	if (m_pVModel == NULL)
	{
		return;
	}

	int i;
	for (i = 1; i < m_pVModel->m_group.Count(); i++)
	{
		const studiohdr_t *pStudioHdr = GroupStudioHdr( i );
		Assert( pStudioHdr );
		pStudioHdr->SetActivityListVersion( version );
	}
}
#endif


//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	CStudioHdr::GetEventListVersion( void )
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr->eventsindexed;
	}

	int version = m_pStudioHdr->eventsindexed;

	int i;
	for (i = 1; i < m_pVModel->m_group.Count(); i++)
	{
		const studiohdr_t *pStudioHdr = GroupStudioHdr( i );
		Assert( pStudioHdr );
		version = min( version, pStudioHdr->eventsindexed );
	}

	return version;
}
#endif

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void CStudioHdr::SetEventListVersion( int version )
{
	m_pStudioHdr->eventsindexed = version;

	if (m_pVModel == NULL)
	{
		return;
	}

	int i;
	for (i = 1; i < m_pVModel->m_group.Count(); i++)
	{
		const studiohdr_t *pStudioHdr = GroupStudioHdr( i );
		Assert( pStudioHdr );
		pStudioHdr->eventsindexed = version;
	}
}
#endif

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------


int CStudioHdr::GetNumIKAutoplayLocks( void ) const
{
	if (m_pVModel == NULL)
	{
		return m_pStudioHdr->numlocalikautoplaylocks;
	}

	return m_pVModel->m_iklock.Count();
}

const mstudioiklock_t &CStudioHdr::pIKAutoplayLock( int i )
{
	if (m_pVModel == NULL)
	{
		return *m_pStudioHdr->pLocalIKAutoplayLock( i );
	}

	const studiohdr_t *pStudioHdr = GroupStudioHdr( m_pVModel->m_iklock[i].group );
	Assert( pStudioHdr );
	return *pStudioHdr->pLocalIKAutoplayLock( m_pVModel->m_iklock[i].index );
}

#if 0
int	CStudioHdr::CountAutoplaySequences() const
{
	int count = 0;
	for (int i = 0; i < GetNumSeq(); i++)
	{
		mstudioseqdesc_t &seqdesc = pSeqdesc( i );
		if (seqdesc.flags & STUDIO_AUTOPLAY)
		{
			count++;
		}
	}
	return count;
}

int	CStudioHdr::CopyAutoplaySequences( unsigned short *pOut, int outCount ) const
{
	int outIndex = 0;
	for (int i = 0; i < GetNumSeq() && outIndex < outCount; i++)
	{
		mstudioseqdesc_t &seqdesc = pSeqdesc( i );
		if (seqdesc.flags & STUDIO_AUTOPLAY)
		{
			pOut[outIndex] = i;
			outIndex++;
		}
	}
	return outIndex;
}

#endif

//-----------------------------------------------------------------------------
// Purpose:	maps local sequence bone to global bone
//-----------------------------------------------------------------------------

int	CStudioHdr::RemapSeqBone( int iSequence, int iLocalBone ) const	
{
	// remap bone
	if (m_pVModel)
	{
		const virtualgroup_t *pSeqGroup = m_pVModel->pSeqGroup( iSequence );
		return pSeqGroup->masterBone[iLocalBone];
	}
	return iLocalBone;
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int	CStudioHdr::RemapAnimBone( int iAnim, int iLocalBone ) const
{
	// remap bone
	if (m_pVModel)
	{
		const virtualgroup_t *pAnimGroup = m_pVModel->pAnimGroup( iAnim );
		return pAnimGroup->masterBone[iLocalBone];
	}
	return iLocalBone;
}
#endif

//-----------------------------------------------------------------------------
// TWO BLOCKS ARE GONE FROM HERE -- plan_shim.md Phase 2d, and neither had a
// caller this build compiles.
//
// CStudioHdr::RunFlexRules (277 lines) interpreted a model's FAC expressions
// into flex weights. Its only caller in stock is c_baseflex.cpp, which is in
// neither _csharp.vpc and on no ninja graph:
//
//     tools/shim-census who-imports _ZN11CStudioHdr12RunFlexRulesEPKfPf
//     COUNT server 0 / COUNT client 0
//
// CStudioHdr::CActivityToSequenceMapping (243 lines) built a hash from
// activity ordinal to the sequences carrying it, so that SelectWeightedSequence
// could make a WEIGHTED RANDOM pick among them. It is lazily initialised
// (STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE, studio.h:42) and the only path
// that reached it was the client's ::LookupSequence fall-through -- which is
// the prediction defect Phase 2d closed: the server's label-only lookup
// answered -1 for the same name. Same census, same answer:
//
//     who-imports _ZN11CStudioHdr26CActivityToSequenceMapping10InitializeEPS_
//     COUNT server 0 / COUNT client 0
//
// IT WAS ALSO THE LAST READER OF seqdesc.activity, the ordinal that
// game/shared/animation.cpp wrote into mdl memory out of a per-level registry
// -- which is what lets that whole family go: animation.cpp,
// shell_animlists.cpp, anim_name_registry.h and ActivityCatalog.cs.
//
// THE DECLARATIONS STAY IN src/public/studio.h and are deliberately not
// touched. That is a STOCK public header shared with projects this tree does
// not build, and the member m_ActivityToSequence is part of CStudioHdr's
// layout -- which this file exists to be the definition of. What had no
// consumer is the code, and code is what was removed.
//-----------------------------------------------------------------------------

