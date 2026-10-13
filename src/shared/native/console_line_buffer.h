// SourceSharp: one side's engine console lines, buffered for the managed side
// to drain without a crossing (console-batch, 2026-10-11; one crossing per line
// took the gate's map-change stage over budget). nSeq is the side's ONE line
// counter, native and managed prints alike; compare numbers by (int32_t)(a - b).
// The lock is held only for one line's copy or the consumer's swap, so a print
// never waits on managed work. A line that does not fit is counted in
// nOverflow, its number spent; native decides nothing. The layout is an ABI:
// NativeConsoleBuffer.cs reads it, both sides' tests pin it.

#ifndef SOURCESHARP_CONSOLE_LINE_BUFFER_H
#define SOURCESHARP_CONSOLE_LINE_BUFFER_H

#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <atomic>
#include <thread>
#include <type_traits>

/// Bytes in each of the two halves: 2 x 512 KiB = 1 MiB per side.
static const uint32_t kConsoleLineHalfBytes = 512u * 1024u;

/// At offset 0 of the buffer; the halves follow it at offset 32.
struct ConsoleLineBufferHeader
{
	std::atomic< uint32_t > nSeq{ 0 };  // the last number taken; the next line takes nSeq + 1
	uint32_t nOverflow = 0;              // lines that did not fit since the last swap
	std::atomic< int32_t > nLock{ 0 };   // 0 free, 1 held: a copy or a swap, nothing longer
	int32_t  nActive = 0;                // the half producers append to (0 or 1)
	uint32_t nUsed[ 2 ] = { 0, 0 };      // bytes written into each half
	uint32_t nHalfBytes = kConsoleLineHalfBytes;  // for the reader
	uint32_t nReserved = 0;
};

// THE TWO ATOMIC WORDS ARE SHARED WITH MANAGED, which takes the lock with
// Interlocked.CompareExchange on the same four bytes (NativeConsoleBuffer.cs)
// and reads the counter at offset 0. So each must be exactly the plain word it
// wraps -- lock-free (and so address-free, which is what lets another
// language's atomic operate on it), four bytes, at the offsets the managed
// layout names -- and the header must stay standard-layout for offsetof.
static_assert( std::atomic< uint32_t >::is_always_lock_free && sizeof( std::atomic< uint32_t > ) == 4,
	"the line counter is read by managed as a plain uint32" );
static_assert( std::atomic< int32_t >::is_always_lock_free && sizeof( std::atomic< int32_t > ) == 4,
	"the lock word is taken by managed with Interlocked on a plain int32" );
static_assert( std::is_standard_layout< ConsoleLineBufferHeader >::value,
	"the header is an ABI: managed reads it at fixed offsets" );

/// One line in a half: this, then nLength bytes of UTF-8 (no NUL), then
/// padding to the next multiple of 4.
struct ConsoleLineRecord
{
	uint32_t nSeq;
	uint32_t nLength;
	uint32_t nWarning;    // 1: the decorated (warning) channel
};

inline uint32_t ConsoleLineRecordBytes( uint32_t nLength )
{
	return ( (uint32_t)sizeof( ConsoleLineRecord ) + nLength + 3u ) & ~3u;
}

class CConsoleLineBuffer
{
public:
	CConsoleLineBuffer() = default;
	CConsoleLineBuffer( const CConsoleLineBuffer & ) = delete;
	CConsoleLineBuffer &operator=( const CConsoleLineBuffer & ) = delete;

	/// The line in, numbered. False when it did not fit (counted in nOverflow).
	bool Append( const char *pText, int nWarning )
	{
		const size_t nLen = strlen( pText );
		Lock();
		// Under the lock, so the increment need order nothing by itself.
		const uint32_t nSeq = m_Header.nSeq.fetch_add( 1, std::memory_order_relaxed ) + 1;
		const int nHalf = m_Header.nActive;
		const uint32_t nUsed = m_Header.nUsed[ nHalf ];
		const uint64_t nNeed = nLen > 0xFFFFFFF0u ? UINT64_MAX : ConsoleLineRecordBytes( (uint32_t)nLen );
		if ( nNeed > (uint64_t)( m_Header.nHalfBytes - nUsed ) )
		{
			++m_Header.nOverflow;
			Unlock();
			return false;
		}
		uint8_t *pAt = Half( nHalf ) + nUsed;
		ConsoleLineRecord rec = { nSeq, (uint32_t)nLen, nWarning ? 1u : 0u };
		memcpy( pAt, &rec, sizeof( rec ) );
		memcpy( pAt + sizeof( rec ), pText, nLen );
		m_Header.nUsed[ nHalf ] = nUsed + (uint32_t)nNeed;
		Unlock();
		return true;
	}

	void Lock()
	{
		for ( int nSpins = 0; m_Header.nLock.exchange( 1, std::memory_order_acquire ) != 0; )
		{
			while ( m_Header.nLock.load( std::memory_order_relaxed ) != 0 )
			{
				if ( ++nSpins > 64 )
					std::this_thread::yield();
			}
		}
	}

	void Unlock() { m_Header.nLock.store( 0, std::memory_order_release ); }

	uint8_t *Half( int nHalf ) { return m_Halves + (size_t)nHalf * kConsoleLineHalfBytes; }

	ConsoleLineBufferHeader m_Header;
	uint8_t m_Halves[ 2 * kConsoleLineHalfBytes ];
};

#endif // SOURCESHARP_CONSOLE_LINE_BUFFER_H
