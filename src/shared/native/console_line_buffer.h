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
#ifdef _MSC_VER
	#include <intrin.h>
	extern "C" __declspec( dllimport ) int __stdcall SwitchToThread( void );
#else
	#include <sched.h>
#endif

// The lock word's operations: gcc's __atomic builtins on the GNU compilers,
// the Interlocked intrinsics under MSVC, which has no __atomic. clang-cl
// takes both, and gets the MSVC spelling (keyed on _MSC_VER), so a clang-cl
// build compiles the Windows path. On x64 an MSVC volatile store is a
// release and an Interlocked exchange a full barrier, at least what the
// lock needs.
static inline int32_t ConsoleLockExchangeAcquire( int32_t *pWord, int32_t nValue )
{
#ifdef _MSC_VER
	return (int32_t)_InterlockedExchange( (volatile long *)pWord, (long)nValue );
#else
	return __atomic_exchange_n( pWord, nValue, __ATOMIC_ACQUIRE );
#endif
}

static inline int32_t ConsoleLockLoadRelaxed( const int32_t *pWord )
{
#ifdef _MSC_VER
	return *(const volatile int32_t *)pWord;
#else
	return __atomic_load_n( pWord, __ATOMIC_RELAXED );
#endif
}

static inline void ConsoleLockStoreRelease( int32_t *pWord, int32_t nValue )
{
#ifdef _MSC_VER
	_ReadWriteBarrier();
	*(volatile int32_t *)pWord = nValue;
#else
	__atomic_store_n( pWord, nValue, __ATOMIC_RELEASE );
#endif
}

static inline void ConsoleLockYield()
{
#ifdef _MSC_VER
	SwitchToThread();
#else
	sched_yield();
#endif
}

/// Bytes in each of the two halves: 2 x 512 KiB = 1 MiB per side.
static const uint32_t kConsoleLineHalfBytes = 512u * 1024u;

/// At offset 0 of the buffer; the halves follow it at offset 32.
struct ConsoleLineBufferHeader
{
	uint32_t nSeq;        // the last number taken; the next line takes nSeq + 1
	uint32_t nOverflow;   // lines that did not fit since the last swap
	int32_t  nLock;       // 0 free, 1 held: a copy or a swap, nothing longer
	int32_t  nActive;     // the half producers append to (0 or 1)
	uint32_t nUsed[ 2 ];  // bytes written into each half
	uint32_t nHalfBytes;  // kConsoleLineHalfBytes, for the reader
	uint32_t nReserved;
};

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
	CConsoleLineBuffer()
	{
		memset( &m_Header, 0, sizeof( m_Header ) );
		m_Header.nHalfBytes = kConsoleLineHalfBytes;
	}
	CConsoleLineBuffer( const CConsoleLineBuffer & ) = delete;
	CConsoleLineBuffer &operator=( const CConsoleLineBuffer & ) = delete;

	/// The line in, numbered. False when it did not fit (counted in nOverflow).
	bool Append( const char *pText, int nWarning )
	{
		const size_t nLen = strlen( pText );
		Lock();
		const uint32_t nSeq = ++m_Header.nSeq;
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
		for ( int nSpins = 0; ConsoleLockExchangeAcquire( &m_Header.nLock, 1 ) != 0; )
		{
			while ( ConsoleLockLoadRelaxed( &m_Header.nLock ) != 0 )
			{
				if ( ++nSpins > 64 )
					ConsoleLockYield();
			}
		}
	}

	void Unlock() { ConsoleLockStoreRelease( &m_Header.nLock, 0 ); }

	uint8_t *Half( int nHalf ) { return m_Halves + (size_t)nHalf * kConsoleLineHalfBytes; }

	ConsoleLineBufferHeader m_Header;
	uint8_t m_Halves[ 2 * kConsoleLineHalfBytes ];
};

#endif // SOURCESHARP_CONSOLE_LINE_BUFFER_H
