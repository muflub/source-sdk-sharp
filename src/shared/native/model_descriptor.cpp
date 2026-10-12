//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — ONE MODEL, ONE CROSSING ============================//
// SS_REASON f  a bit-level wire codec
//
// model_descriptor.h holds the format and the argument. This is the walk.
//
// IT WRITES TWICE ON PURPOSE. The size is not knowable without visiting every
// string, so the pack runs in two passes over the same code path — once with a
// NULL sink that only counts, once with the caller's buffer — rather than
// building a temporary and copying it. Two walks of a studio header cost less
// than a heap allocation the caller then has to be told the size of anyway, and
// one code path cannot disagree with itself about the layout.
//
//=============================================================================//
#ifdef CLIENT_DLL
#include "cbase.h"
#else
// The server half is on the R5 include set (plan_server_novalve.md P5): the
// two engine interfaces it reads, connected in shell_engineboundary.cpp and
// with the tiers. MDLCACHE_CRITICAL_SECTION below locks mdlcache by name.
class IVModelInfo;
extern IVModelInfo *modelinfo;
class IMDLCache;
extern IMDLCache *mdlcache;
#endif

#include "model_descriptor.h"

#include "studio.h"
#include "anim_event_names.h"
#include "engine/ivmodelinfo.h"
#include "datacache/imdlcache.h"


//-----------------------------------------------------------------------------
// The sink. `Bytes()` is the running total either way; `m_pOut` is NULL on the
// counting pass, and the two passes differ in nothing else.
//-----------------------------------------------------------------------------
class CDescriptorSink
{
public:
	CDescriptorSink( char *pOut, int32_t nBytes )
		: m_pOut( pOut ), m_nCapacity( nBytes ), m_nWritten( 0 ) {}

	int32_t Bytes() const { return m_nWritten; }

	void Int( int32_t v ) { Raw( &v, sizeof( v ) ); }

	void Float( float v ) { Raw( &v, sizeof( v ) ); }

	// A NULL or empty string is a zero-length string, not an absent one. The
	// distinction matters for the activity name: "this sequence has no
	// activity" is an empty name, and the decoder must not have to guess.
	void String( const char *psz )
	{
		const int32_t nLen = psz ? (int32_t)(int)strlen( psz ) : 0;

		Int( nLen );

		if ( nLen > 0 )
			Raw( psz, nLen );
	}

private:
	void Raw( const void *pSrc, int32_t nLen )
	{
		if ( m_pOut && m_nWritten + nLen <= m_nCapacity )
			memcpy( m_pOut + m_nWritten, pSrc, nLen );

		m_nWritten += nLen;
	}

	char    *m_pOut;
	int32_t  m_nCapacity;
	int32_t  m_nWritten;
};

//-----------------------------------------------------------------------------
// One pass. Called twice with the same header, which is what makes the size
// answer and the bytes written describe the same model.
//-----------------------------------------------------------------------------
//
// ONE SINK CALL PER FIELD, IN A STRAIGHT LINE, and that is a requirement
// rather than a style. model_descriptor_format_test.cpp extracts the ORDER AND
// KIND of every sink call here and requires it to equal the order and kind of
// every read in ModelDescriptor.cs's Decode -- the two being implementations
// of one format in two languages with no shared declaration. A defensive arm
// that wrote its own copy of the five event fields would add five entries to
// this side's sequence and none to the other's, and the check would be reading
// a control-flow accident rather than the format. So every value that can fail
// to be there is decided into a local FIRST and written once.
//
static void ModelDescriptor_Walk( CStudioHdr *pHdr, CDescriptorSink &sink )
{
	sink.Int( kModelDescriptorVersion );
	sink.Int( pHdr->numbones() );

	const int nAttachments = pHdr->GetNumAttachments();
	sink.Int( nAttachments );

	for ( int i = 0; i < nAttachments; i++ )
	{
		const mstudioattachment_t &attachment = pHdr->pAttachment( i );
		sink.String( attachment.pszName() );
		sink.Float( attachment.local[0][3] );
		sink.Float( attachment.local[1][3] );
		sink.Float( attachment.local[2][3] );
	}

	// No duration: each sequence's default-pose duration is managed's
	// (StudioSequenceTiming over the side's StudioHdr, format v3).

	// A model with no sequence data has NO sequences rather than a missing
	// count: the count is always written, and the loop below simply does not
	// run. "Zero sequences" and "this field is absent" must not be the same
	// bytes.
	const int nSeq = pHdr->SequencesAvailable() ? pHdr->GetNumSeq() : 0;
	sink.Int( nSeq );

	for ( int i = 0; i < nSeq; i++ )
	{
		mstudioseqdesc_t &seqdesc = pHdr->pSeqdesc( i );

		sink.String( seqdesc.pszLabel() );
		sink.String( seqdesc.pszActivityName() );
		sink.Int( seqdesc.flags );

		const int nEvents = (int)seqdesc.numevents;
		sink.Int( nEvents );

		for ( int j = 0; j < nEvents; j++ )
		{
			const mstudioevent_t *pEvent = seqdesc.pEvent( j );

			// Decided first, written once. pEvent is NULL only when numevents
			// disagrees with the table it counts, which is a corrupt .mdl --
			// and a corrupt .mdl must still produce a blob of the shape the
			// count promised, or the decoder reads the next sequence's label
			// as this event's options.
			const float flCycle  = pEvent ? pEvent->cycle : 0.0f;
			const int   nEventId = pEvent ? pEvent->event : 0;
			const int   nType    = pEvent ? pEvent->type  : 0;

			// THE NAME IS ONLY READ FOR A NEW-SYSTEM EVENT, and
			// sequence_events.h spelled out why: szeventindex is meaningless
			// on an old-system event -- the RPG's fire carries a bare 5001 --
			// so following it would walk off into whatever the .mdl happens to
			// hold at that offset.
			const char *pszName = ( pEvent && ( pEvent->type & kAnimEventTypeNewEventSystem ) )
			                          ? pEvent->pszEventName() : NULL;

			const char *pszOptions = pEvent ? pEvent->pszOptions() : NULL;

			sink.Float( flCycle );
			sink.Int( nEventId );
			sink.Int( nType );
			sink.String( pszName );
			sink.String( pszOptions );
		}
	}
}

int32_t SourceSharp_ModelDescriptor( int32_t nModelIndex, char *pBuffer, int32_t nBytes )
{
	if ( !modelinfo || nModelIndex <= 0 )
		return 0;

	const model_t *pModel = modelinfo->GetModel( nModelIndex );

	if ( !pModel )
		return 0;

	MDLCACHE_CRITICAL_SECTION();

	studiohdr_t *pStudio = modelinfo->GetStudiomodel( pModel );

	if ( !pStudio )
		return 0;

	CStudioHdr hdr;
	hdr.Init( pStudio, mdlcache );

	if ( !hdr.IsValid() )
		return 0;

	CDescriptorSink measure( NULL, 0 );
	ModelDescriptor_Walk( &hdr, measure );

	const int32_t nNeeded = measure.Bytes();

	if ( !pBuffer || nBytes < nNeeded )
		return nNeeded;

	CDescriptorSink write( pBuffer, nBytes );
	ModelDescriptor_Walk( &hdr, write );

	Assert( write.Bytes() == nNeeded );
	return nNeeded;
}
