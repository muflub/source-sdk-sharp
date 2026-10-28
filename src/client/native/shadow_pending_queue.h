//========= SourceSharp — the shadow manager's pending queue ==================//
//
// C7 cluster 12 S2 (docs/plans/c7_shadow_manager.md). What an entity's shell
// tells the managed shadow manager between its calls (shell_shadows_client.h's
// kinds), kept in order, three int32 an entry, so a moving entity costs no
// crossing of its own: the entries ride in the manager's next call.
//
// THE QUEUE'S RULES, each a fact in shadow_pending_queue_test:
//   - a dirty mark for an entity already marked ORs into that entry (the
//     manager's AddToDirtyShadowList refuses a renderable already dirty, so a
//     second mark before the hand-over says nothing new but the texture bit);
//   - a destroy drops the entity's entries before it (the renderable may be
//     freed before the hand-over, and nothing may ask it anything);
//   - a full queue is handed over (the sink) before the next entry goes in;
//   - what leaves (Take, the sink) leaves as a copy, so the call or sink may
//     queue again while it reads.
// No SDK header: the shell supplies the sink.
//=============================================================================//
#pragma once

#include <stdint.h>

class CShadowPendingQueue
{
public:
	enum
	{
		DIRTY = 1,
		REFRESH = 2,
		DESTROY = 3,
		WORDS = 3,
		// dm_lockdown queues 248 to 269 entries a frame (S3 at c7c7a17c8:
		// cs_cl_shadow_stats over 600 frames), and 256 handed a full queue
		// over once a frame at three players or RTT on.
		MAX = 512,
	};

	// Where a full queue, or one handed over before a call, goes.
	typedef void ( *Sink_t )( const int32_t *pEntries, int32_t nCount, void *pContext );

	CShadowPendingQueue() : m_nCount( 0 ), m_nHandedOver( 0 ), m_pfnSink( 0 ), m_pSinkContext( 0 ) {}

	void SetSink( Sink_t pfnSink, void *pContext )
	{
		m_pfnSink = pfnSink;
		m_pSinkContext = pContext;
	}

	void Dirty( uint32_t hEntity, int32_t bTextureDirty )
	{
		for ( int i = 0; i < m_nCount; ++i )
		{
			int32_t *p = m_Entries + WORDS * i;
			if ( p[ 0 ] == DIRTY && (uint32_t)p[ 1 ] == hEntity )
			{
				p[ 2 ] |= bTextureDirty ? 1 : 0;
				return;
			}
		}
		Append( DIRTY, hEntity, bTextureDirty ? 1 : 0 );
	}

	void Refresh( uint32_t hEntity, int32_t nTypeAndFlags ) { Append( REFRESH, hEntity, nTypeAndFlags ); }

	void Destroy( uint32_t hEntity )
	{
		int nKept = 0;
		for ( int i = 0; i < m_nCount; ++i )
		{
			const int32_t *p = m_Entries + WORDS * i;
			if ( (uint32_t)p[ 1 ] == hEntity )
				continue;
			int32_t *q = m_Entries + WORDS * nKept++;
			q[ 0 ] = p[ 0 ];
			q[ 1 ] = p[ 1 ];
			q[ 2 ] = p[ 2 ];
		}
		m_nCount = nKept;
		Append( DESTROY, hEntity, 0 );
	}

	// The entries, emptied, copied into pOut (WORDS * MAX words), for a call
	// that carries them in its arguments; their count. A copy, as the sink's
	// is: the call's work may queue again before it has read them all.
	int32_t Take( int32_t *pOut )
	{
		const int n = m_nCount;
		m_nCount = 0;
		for ( int i = 0; i < WORDS * n; ++i )
			pOut[ i ] = m_Entries[ i ];
		return n;
	}

	// The entries to the sink, emptied; nothing when there are none. The sink
	// reads a copy, so what it does may queue again.
	void HandOver()
	{
		const int n = m_nCount;
		m_nCount = 0;
		if ( n <= 0 || !m_pfnSink )
			return;
		int32_t copy[ WORDS * MAX ];
		for ( int i = 0; i < WORDS * n; ++i )
			copy[ i ] = m_Entries[ i ];
		++m_nHandedOver;
		m_pfnSink( copy, n, m_pSinkContext );
	}

	// The entries dropped (no manager to tell: outside a load).
	void Clear() { m_nCount = 0; }

	int Count() const { return m_nCount; }
	const int32_t *Entries() const { return m_Entries; }
	int HandedOver() const { return m_nHandedOver; }

private:
	void Append( int32_t kind, uint32_t hEntity, int32_t value )
	{
		if ( m_nCount == MAX )
			HandOver();
		if ( m_nCount == MAX )
			m_nCount = 0;  // no sink: the oldest go, as a call with no manager drops them
		int32_t *p = m_Entries + WORDS * m_nCount++;
		p[ 0 ] = kind;
		p[ 1 ] = (int32_t)hEntity;
		p[ 2 ] = value;
	}

	int m_nCount;
	int m_nHandedOver;
	Sink_t m_pfnSink;
	void *m_pSinkContext;
	int32_t m_Entries[ WORDS * MAX ];
};
