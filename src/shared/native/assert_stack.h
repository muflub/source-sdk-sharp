// Engine-free helpers for printing a native stack on a failed assert: the dedup
// key, the dedup set and frame formatting. The key is file, line and the first
// few return addresses, so different callers of one leaf report separately.

#pragma once

#include <stddef.h>
#include <stdio.h>

// Return addresses that take part in a report's identity.
enum
{
	kSourceSharpAssertStackKeyFrames = 6
};

enum
{
	kSourceSharpAssertStackMaxSeen = 128
};

typedef unsigned long long SourceSharpAssertStackKey_t;

// FNV-1a, 64-bit. Restated so this header needs only libc.
inline SourceSharpAssertStackKey_t SourceSharpAssertStack_HashBytes( SourceSharpAssertStackKey_t ulSeed,
																	 const void *pBytes, size_t cBytes )
{
	const unsigned char *pWalk = (const unsigned char *)pBytes;
	SourceSharpAssertStackKey_t ulHash = ulSeed;

	for ( size_t i = 0; i < cBytes; ++i )
	{
		ulHash ^= (SourceSharpAssertStackKey_t)pWalk[i];
		ulHash *= 1099511628211ULL;
	}

	return ulHash;
}

// The key that makes two assert reports the same one. A NULL file hashes as empty.
inline SourceSharpAssertStackKey_t SourceSharpAssertStack_Key( const char *pchFile, int nLine, void *const *ppFrames,
															   int nFrames )
{
	SourceSharpAssertStackKey_t ulHash = 14695981039346656037ULL;

	if ( pchFile )
		ulHash = SourceSharpAssertStack_HashBytes( ulHash, pchFile, strlen( pchFile ) );

	ulHash = SourceSharpAssertStack_HashBytes( ulHash, &nLine, sizeof( nLine ) );

	if ( ppFrames && nFrames > 0 )
	{
		const int nUse =
			( nFrames < kSourceSharpAssertStackKeyFrames ) ? nFrames : (int)kSourceSharpAssertStackKeyFrames;

		for ( int i = 0; i < nUse; ++i )
		{
			// The pointer VALUE, not the pointee.
			const unsigned long long ulAddr = (unsigned long long)(size_t)ppFrames[i];
			ulHash = SourceSharpAssertStack_HashBytes( ulHash, &ulAddr, sizeof( ulAddr ) );
		}
	}

	return ulHash;
}

// Reports already printed. Zero-initialised is a valid empty set, usable during
// static init, where asserts can fire.
struct SourceSharpAssertStackSeen_t
{
	SourceSharpAssertStackKey_t m_Keys[kSourceSharpAssertStackMaxSeen];
	int m_nCount;
	bool m_bSaidFull;
};

inline void SourceSharpAssertStack_Reset( SourceSharpAssertStackSeen_t *pSeen )
{
	if ( !pSeen )
		return;

	pSeen->m_nCount = 0;
	pSeen->m_bSaidFull = false;
}

// True exactly once per distinct key. When full, answers false for anything new
// rather than evicting; *pbFirstOverflow is set on the call that finds it full.
inline bool SourceSharpAssertStack_ShouldReport( SourceSharpAssertStackSeen_t *pSeen, SourceSharpAssertStackKey_t ulKey,
												 bool *pbFirstOverflow )
{
	if ( pbFirstOverflow )
		*pbFirstOverflow = false;

	if ( !pSeen )
		return true;

	for ( int i = 0; i < pSeen->m_nCount; ++i )
	{
		if ( pSeen->m_Keys[i] == ulKey )
			return false;
	}

	if ( pSeen->m_nCount >= kSourceSharpAssertStackMaxSeen )
	{
		if ( !pSeen->m_bSaidFull )
		{
			pSeen->m_bSaidFull = true;
			if ( pbFirstOverflow )
				*pbFirstOverflow = true;
		}
		return false;
	}

	pSeen->m_Keys[pSeen->m_nCount++] = ulKey;
	return true;
}

// One stack frame as `#03 module+0xoffset  symbol`, ready for addr2line. With no
// module, or an address below the module base, prints the absolute address.
// Returns the characters written; pchOut is always NUL-terminated.
inline int SourceSharpAssertStack_FormatFrame( char *pchOut, size_t cchOut, int nIndex, const char *pchModule,
											   unsigned long long ulModuleBase, unsigned long long ulAddr,
											   const char *pchSymbol )
{
	if ( !pchOut || cchOut == 0 )
		return 0;

	pchOut[0] = '\0';

	const bool bHaveModule = ( pchModule != NULL && pchModule[0] != '\0' && ulAddr >= ulModuleBase );
	const char *pchSym = ( pchSymbol && pchSymbol[0] ) ? pchSymbol : "??";

	int nWritten;
	if ( bHaveModule )
	{
		nWritten =
			snprintf( pchOut, cchOut, "  #%-2d %s+0x%08llx  %s", nIndex, pchModule, ulAddr - ulModuleBase, pchSym );
	}
	else
	{
		// No parenthesis after "??": "??(" was a trigraph before C++17.
		nWritten = snprintf( pchOut, cchOut, "  #%-2d ?? 0x%016llx  %s", nIndex, ulAddr, pchSym );
	}

	if ( nWritten < 0 )
	{
		pchOut[0] = '\0';
		return 0;
	}

	if ( (size_t)nWritten >= cchOut )
		return (int)( cchOut - 1 );

	return nWritten;
}

// FormatFrame with a trailing newline. The newline's byte is reserved first, so a
// long frame is clamped rather than losing its line ending.
inline int SourceSharpAssertStack_FormatFrameLine( char *pchOut, size_t cchOut, int nIndex, const char *pchModule,
												   unsigned long long ulModuleBase, unsigned long long ulAddr,
												   const char *pchSymbol )
{
	if ( !pchOut || cchOut < 2 )
	{
		if ( pchOut && cchOut > 0 )
			pchOut[0] = '\0';
		return 0;
	}

	const int nLen =
		SourceSharpAssertStack_FormatFrame( pchOut, cchOut - 1, nIndex, pchModule, ulModuleBase, ulAddr, pchSymbol );

	pchOut[nLen] = '\n';
	pchOut[nLen + 1] = '\0';

	return nLen + 1;
}

// Parses a counter published through the environment (the hook and the self-test
// may be in different DLLs). Anything malformed answers zero.
inline long long SourceSharpAssertStack_ParseCounter( const char *pchValue )
{
	if ( !pchValue )
		return 0;

	const char *pWalk = pchValue;

	// Leading blanks only; a sign is not accepted.
	while ( *pWalk == ' ' || *pWalk == '\t' )
		++pWalk;

	if ( *pWalk < '0' || *pWalk > '9' )
		return 0;

	long long llValue = 0;
	for ( ; *pWalk >= '0' && *pWalk <= '9'; ++pWalk )
	{
		// Larger than any real count: not written by the hook.
		if ( llValue > ( 9223372036854775807LL - 9 ) / 10 )
			return 0;

		llValue = llValue * 10 + ( *pWalk - '0' );
	}

	if ( *pWalk != '\0' )
		return 0;

	return llValue;
}

// Whether a failed assert ends the process. A deliberate self-test assert never
// does, then -assertlog keeps going, then the fatal default applies.
inline bool SourceSharp_AssertShouldBeFatal( bool bIsAssert, bool bFatal, bool bDemote, bool bDeliberate )
{
	if ( !bIsAssert )
		return false;
	if ( bDeliberate )
		return false;
	if ( bDemote )
		return false;
	return bFatal;
}

// Whether a spew line is echoed to the terminal. Only the DLL that claimed the
// echo prints, never on top of an -assertstack report, and SPEW_ERROR lines are
// echoed even without the "[SourceSharp" prefix: they are the last thing the
// process says before Sys_Error.
inline bool SourceSharp_SpewShouldEcho( bool bClaimedEcho, bool bStackWriting, bool bOurs, bool bFatal )
{
	if ( !bClaimedEcho )
		return false;
	if ( bStackWriting )
		return false;
	return bOurs || bFatal;
}

// Whether to print a stack under a fatal spew: once per process, not while an
// -assertstack report is writing, and regardless of -assertstack.
inline bool SourceSharp_FatalSpewShouldWriteStack( bool bFatal, bool bAlreadyWritten, bool bStackWriting )
{
	if ( !bFatal )
		return false;
	if ( bAlreadyWritten )
		return false;
	if ( bStackWriting )
		return false;
	return true;
}
