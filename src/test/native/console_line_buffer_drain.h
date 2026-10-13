// SourceSharp: the consumer's half of console_line_buffer.h, for the native
// tests: the swap NativeConsoleBuffer.Drain makes over the same memory, the
// half it took, and the parse it does after releasing the lock.

#ifndef SOURCESHARP_TEST_CONSOLE_LINE_BUFFER_DRAIN_H
#define SOURCESHARP_TEST_CONSOLE_LINE_BUFFER_DRAIN_H

#include "console_line_buffer.h"

#include <string>
#include <vector>

struct ConsoleLineTaken
{
	const uint8_t *pData;
	uint32_t nBytes;
	uint32_t nOverflow;
};

struct ConsoleLineDrained
{
	uint32_t seq;
	std::string text;
	uint32_t warning;
};

/// The swap, under the lock and nothing else: the half producers wrote, the
/// overflow count, and the other half made active and empty.
inline ConsoleLineTaken ConsoleLineSwap( CConsoleLineBuffer &buffer )
{
	buffer.Lock();
	ConsoleLineBufferHeader &h = buffer.m_Header;
	const int nHalf = h.nActive;
	ConsoleLineTaken taken = { buffer.Half( nHalf ), h.nUsed[ nHalf ], h.nOverflow };
	h.nActive = nHalf ^ 1;
	h.nUsed[ nHalf ^ 1 ] = 0;
	h.nOverflow = 0;
	buffer.Unlock();
	return taken;
}

/// The parse, outside the lock.
inline std::vector<ConsoleLineDrained> ConsoleLineParse( const ConsoleLineTaken &taken )
{
	std::vector<ConsoleLineDrained> lines;
	for ( uint32_t at = 0; at < taken.nBytes; )
	{
		ConsoleLineRecord rec;
		memcpy( &rec, taken.pData + at, sizeof( rec ) );
		lines.push_back( ConsoleLineDrained{ rec.nSeq, std::string( (const char *)taken.pData + at + sizeof( rec ), rec.nLength ), rec.nWarning } );
		at += ConsoleLineRecordBytes( rec.nLength );
	}
	return lines;
}

// An acquire load: gcc's builtin on the GNU compilers; under MSVC, which has
// no __atomic, a volatile read (an acquire on x64) and a compiler barrier,
// as console_line_buffer.h's lock helpers do.
template < typename T >
inline T ConsoleLineLoadAcquire( const T *p )
{
#ifdef _MSC_VER
	const T v = *(const volatile T *)p;
	_ReadWriteBarrier();
	return v;
#else
	return __atomic_load_n( p, __ATOMIC_ACQUIRE );
#endif
}

inline uint32_t ConsoleLineSequence( const CConsoleLineBuffer &buffer )
{
	return ConsoleLineLoadAcquire( &buffer.m_Header.nSeq );
}

inline bool ConsoleLineLocked( const CConsoleLineBuffer &buffer )
{
	return ConsoleLineLoadAcquire( &buffer.m_Header.nLock ) != 0;
}

#endif // SOURCESHARP_TEST_CONSOLE_LINE_BUFFER_DRAIN_H
