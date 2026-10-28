// The thunks behind CBoundCallback (bound_callback.h): their bytes, and the pages that hold
// them.

#include "bound_callback.h"

#if defined( _WIN32 )
#include <windows.h>
#else
#include <sys/mman.h>
#include <unistd.h>
#endif

namespace
{

// x86-64 register numbers.
enum
{
	kRax = 0,
	kRcx = 1,
	kRdx = 2,
	kRsi = 6,
	kRdi = 7,
	kR8 = 8,
	kR9 = 9
};

class CEmitter
{
public:
	CEmitter( unsigned char *pOut, size_t nOut )
		: m_pOut( pOut )
		, m_nOut( nOut )
	{
	}

	void Byte( unsigned char b )
	{
		if ( m_nAt < m_nOut )
			m_pOut[m_nAt] = b;
		m_nAt++;
	}

	// mov dst, src (64-bit general registers).
	void MovReg( int nDst, int nSrc )
	{
		Byte( (unsigned char)( 0x48 | ( nSrc >= 8 ? 0x04 : 0 ) | ( nDst >= 8 ? 0x01 : 0 ) ) );
		Byte( 0x89 );
		Byte( (unsigned char)( 0xC0 | ( ( nSrc & 7 ) << 3 ) | ( nDst & 7 ) ) );
	}

	// movaps xmm_dst, xmm_src (xmm0..xmm7).
	void MovXmm( int nDst, int nSrc )
	{
		Byte( 0x0F );
		Byte( 0x28 );
		Byte( (unsigned char)( 0xC0 | ( nDst << 3 ) | nSrc ) );
	}

	// mov reg, imm64.
	void MovImm( int nReg, uint64_t uValue )
	{
		Byte( (unsigned char)( 0x48 | ( nReg >= 8 ? 0x01 : 0 ) ) );
		Byte( (unsigned char)( 0xB8 + ( nReg & 7 ) ) );
		for ( int i = 0; i < 8; i++ )
			Byte( (unsigned char)( uValue >> ( 8 * i ) ) );
	}

	// jmp rax.
	void JmpRax()
	{
		Byte( 0xFF );
		Byte( 0xE0 );
	}

	size_t Size() const { return m_nAt <= m_nOut ? m_nAt : 0; }

private:
	unsigned char *m_pOut;
	size_t m_nOut;
	size_t m_nAt = 0;
};

} // namespace

size_t bound_callback::Assemble( const ArgClass *pArgs, int nArgs, bool bWindows, uint64_t uInstance, uint64_t uTarget,
								 unsigned char *pOut, size_t nOut )
{
	CEmitter emit( pOut, nOut );

	if ( bWindows )
	{
		// Win64: an argument's position picks its register, rcx/rdx/r8/r9 or xmm0-3, so
		// every argument moves one position along, the last first.
		static const int kInt[] = { kRcx, kRdx, kR8, kR9 };
		if ( nArgs + 1 > 4 )
			return 0;

		for ( int k = nArgs - 1; k >= 0; k-- )
		{
			if ( pArgs[k] == ArgClass::Float )
				emit.MovXmm( k + 1, k );
			else
				emit.MovReg( kInt[k + 1], kInt[k] );
		}

		emit.MovImm( kRcx, uInstance );
	}
	else
	{
		// System V: integers take rdi/rsi/rdx/rcx/r8/r9 in order and floats their own
		// xmm registers, so only the integers move.
		static const int kInt[] = { kRdi, kRsi, kRdx, kRcx, kR8, kR9 };
		int nInts = 0;
		for ( int k = 0; k < nArgs; k++ )
			nInts += pArgs[k] == ArgClass::Integer ? 1 : 0;

		if ( nInts + 1 > 6 )
			return 0;

		for ( int k = nInts - 1; k >= 0; k-- )
			emit.MovReg( kInt[k + 1], kInt[k] );

		emit.MovImm( kRdi, uInstance );
	}

	emit.MovImm( kRax, uTarget );
	emit.JmpRax();
	return emit.Size();
}

void *BoundCallback_MapCode( const unsigned char *pCode, size_t nBytes )
{
#if defined( _WIN32 )
	void *pPage = VirtualAlloc( NULL, nBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE );
	if ( !pPage )
		return NULL;

	memcpy( pPage, pCode, nBytes );

	DWORD nOld = 0;
	if ( !VirtualProtect( pPage, nBytes, PAGE_EXECUTE_READ, &nOld ) )
	{
		VirtualFree( pPage, 0, MEM_RELEASE );
		return NULL;
	}

	FlushInstructionCache( GetCurrentProcess(), pPage, nBytes );
	return pPage;
#else
	const size_t nPage = (size_t)sysconf( _SC_PAGESIZE );
	void *pPage = mmap( NULL, nPage, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0 );
	if ( pPage == MAP_FAILED )
		return NULL;

	memcpy( pPage, pCode, nBytes );

	if ( mprotect( pPage, nPage, PROT_READ | PROT_EXEC ) != 0 )
	{
		munmap( pPage, nPage );
		return NULL;
	}

	return pPage;
#endif
}

void BoundCallback_Unmap( void *pPage )
{
	if ( !pPage )
		return;

#if defined( _WIN32 )
	VirtualFree( pPage, 0, MEM_RELEASE );
#else
	munmap( pPage, (size_t)sysconf( _SC_PAGESIZE ) );
#endif
}
