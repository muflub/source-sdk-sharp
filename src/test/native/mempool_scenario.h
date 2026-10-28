// The memory pool scenario both halves of the mempool oracle run: pristine
// tier1/mempool.cpp (mempool_stock_oracle_test, which records it as
// src/test/client/Fixtures/mempool-stock.tsv) and client.so's members
// (tier1_client.cpp; mempool_client_test, which replays it). Seeded
// allocs and frees over each grow mode, block size and alignment the client
// uses and around them. Addresses differ between runs, so what is written
// is what the pool decides: NULL or not, a freed block handed back (and
// which), a fresh block next to the last fresh one or in a new blob, the
// alignment, the zeroing, and Count/PeakCount.

#pragma once

#include "tier1/mempool.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace mempool_scenario
{
struct Rng
{
	uint32_t s = 0x1B873593u;

	uint32_t Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Pick( int lo, int hi ) { return lo + (int)( Next() % (uint32_t)( hi - lo + 1 ) ); }
};

inline std::string Run()
{
	std::string out;
	char b[256];
	Rng r;
	const int kBlockSizes[] = { 1, 4, 8, 13, 24, 64, 100 };
	const int kElements[] = { 1, 2, 3, 16 };
	const int kModes[] = { UTLMEMORYPOOL_GROW_NONE, UTLMEMORYPOOL_GROW_FAST, UTLMEMORYPOOL_GROW_SLOW };
	const int kAligns[] = { 0, 1, 4, 16 };
	int nCase = 0;
	for ( int nBlock : kBlockSizes )
		for ( int nElem : kElements )
			for ( int nMode : kModes )
				for ( int nAlign : kAligns )
				{
					CUtlMemoryPool pool( nBlock, nElem, nMode, "scenario", nAlign );
					std::vector<void *> live, freed;
					void *pLastFresh = NULL;
					// The pool's block size: at least a pointer, rounded up to the alignment.
					const int nAlignTo = nAlign > 1 ? nAlign : 1;
					const int nMin = nBlock < (int)sizeof( void * ) ? (int)sizeof( void * ) : nBlock;
					const int nStride = ( nMin + nAlignTo - 1 ) / nAlignTo * nAlignTo;
					snprintf( b, sizeof( b ), "case %d block %d elements %d mode %d align %d\n", nCase++, nBlock, nElem,
							  nMode, nAlign );
					out += b;
					for ( int step = 0; step < 40; step++ )
					{
						const int op = r.Pick( 0, 9 );
						if ( op < 6 || live.empty() )
						{
							const bool bZero = op == 1;
							const size_t nAsk = ( op == 2 ) ? (size_t)nBlock + 1 : ( op == 3 ? 1 : (size_t)nBlock );
							void *p = bZero ? pool.AllocZero() : pool.Alloc( nAsk );
							out += bZero ? "zero " : "alloc ";
							snprintf( b, sizeof( b ), "%d ", (int)nAsk );
							out += b;
							if ( !p )
							{
								out += "NULL";
							}
							else
							{
								int reused = -1;
								for ( int i = (int)freed.size() - 1; i >= 0; i-- )
									if ( freed[i] == p )
									{
										reused = i;
										freed.erase( freed.begin() + i );
										break;
									}
								if ( reused >= 0 )
								{
									snprintf( b, sizeof( b ), "reuse %d", reused );
									out += b;
								}
								else
								{
									const intptr_t d = pLastFresh ? (char *)p - (char *)pLastFresh : 0;
									// Blocks of one blob are exactly a stride apart; a new blob's first
									// is past its own header, so it never lands exactly there.
									out += !pLastFresh ? "first" : ( d == nStride ? "next" : "blob" );
									pLastFresh = p;
								}
								snprintf( b, sizeof( b ), " mod %d",
										  (int)( (uintptr_t)p % 16 ) % ( nAlign > 1 ? nAlign : 1 ) );
								out += b;
								if ( bZero )
								{
									bool bAllZero = true;
									for ( int i = 0; i < nBlock; i++ )
										bAllZero &= ( (unsigned char *)p )[i] == 0;
									out += bAllZero ? " zeroed" : " NOT-zeroed";
								}
								memset( p, 0xA5, nBlock ); // dirty it for the next zeroing check
								live.push_back( p );
							}
						}
						else
						{
							const int i = r.Pick( 0, (int)live.size() - 1 );
							pool.Free( live[i] );
							freed.push_back( live[i] );
							live.erase( live.begin() + i );
							out += "free";
						}
						snprintf( b, sizeof( b ), " count %d peak %d\n", pool.Count(), pool.PeakCount() );
						out += b;
					}
					pool.Free( NULL );
					for ( void *p : live )
						pool.Free( p );
					snprintf( b, sizeof( b ), "freed all count %d\n", pool.Count() );
					out += b;
					pool.Clear();
					void *p = pool.Alloc();
					snprintf( b, sizeof( b ), "after Clear: %s count %d\n", p ? "a block" : "NULL", pool.Count() );
					out += b;
					if ( p )
						pool.Free( p );
				}
	return out;
}
} // namespace mempool_scenario
