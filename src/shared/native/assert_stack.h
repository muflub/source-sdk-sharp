//========= SourceSharp — the arithmetic behind a stack for every assert =====//
//
// A failed assert in this tree says three things: a file, a line, and an
// expression. That is enough to find the CODE and never enough to find the
// BUG, because the interesting assert in a game is always in a leaf that
// hundreds of callers reach:
//
//   public/materialsystem/imesh.h (1665) : IsFinite(x) && IsFinite(y) && IsFinite(z)
//
// is CVertexBuilder::Position3f. Twelve files in this client can reach it, the
// number it rejected was computed somewhere else again, and the report as it
// stands cannot separate a particle from a tracer from a shadow quad. What is
// missing is the CALLER, and the caller is on the stack at the moment the
// assert fires — a moment that is over by the time anyone reads the log.
//
// So: capture the native stack inside tier0's assert-notify callback and print
// it. The parts that need the process (backtrace(), dladdr()) live in
// shell_diagnostics.cpp. The parts that are arithmetic live here, engine-free,
// so they can be tested without a running Source:
//
//   Key           what makes two reports "the same assert"
//   ShouldReport  the dedup, so a per-frame assert prints one stack, not 3600
//   FormatFrame   one line, in the form addr2line can be handed directly
//
// The dedup is the half that is easy to get quietly wrong. Dedup on the message
// alone and two different call sites that trip the SAME expression collapse
// into one report — which for this assert is precisely the distinction being
// hunted. Dedup on the whole stack and a caller reached through a slightly
// different tail prints again every frame. So the key is the file, the line,
// and the first few return addresses: the immediate callers, which name the
// call site, and not the deep tail, which does not.
//
//=============================================================================//

#pragma once

#include <stddef.h>
#include <stdio.h>
#include <string.h>

//-----------------------------------------------------------------------------
// How many return addresses take part in the identity of a report.
//
// Six: enough to separate call sites that share a leaf (the whole point), few
// enough that the same call site reached from a different depth of the frame
// loop still counts as the same report (the other whole point).
//-----------------------------------------------------------------------------
enum { kSourceSharpAssertStackKeyFrames = 6 };

//-----------------------------------------------------------------------------
// Distinct reports remembered before the dedup gives up.
//
// A hunt that has produced 128 DIFFERENT asserts is not a hunt that is helped
// by the 129th; past that the table says so once and goes quiet, rather than
// either flooding the log or silently forgetting the oldest.
//-----------------------------------------------------------------------------
enum { kSourceSharpAssertStackMaxSeen = 128 };

typedef unsigned long long SourceSharpAssertStackKey_t;

//-----------------------------------------------------------------------------
// FNV-1a, 64-bit. Restated rather than included because this header must
// compile with nothing but libc behind it.
//-----------------------------------------------------------------------------
inline SourceSharpAssertStackKey_t SourceSharpAssertStack_HashBytes(
	SourceSharpAssertStackKey_t ulSeed, const void *pBytes, size_t cBytes )
{
	const unsigned char *pWalk = (const unsigned char *)pBytes;
	SourceSharpAssertStackKey_t ulHash = ulSeed;

	for ( size_t i = 0; i < cBytes; ++i )
	{
		ulHash ^= (SourceSharpAssertStackKey_t)pWalk[ i ];
		ulHash *= 1099511628211ULL;
	}

	return ulHash;
}

//-----------------------------------------------------------------------------
// What makes two assert reports the same one.
//
// The file and line say WHICH assert; the leading return addresses say WHICH
// CALLER tripped it. Both halves are needed: without the line, two asserts in
// one function collapse; without the frames, twelve callers of one leaf do.
//
// A NULL file is tolerated and hashed as the empty string — tier0 always passes
// __TFILE__, but a notify callback is a public entry point and a report that
// crashes while explaining a crash is worse than useless.
//-----------------------------------------------------------------------------
inline SourceSharpAssertStackKey_t SourceSharpAssertStack_Key(
	const char *pchFile, int nLine, void *const *ppFrames, int nFrames )
{
	SourceSharpAssertStackKey_t ulHash = 14695981039346656037ULL;

	if ( pchFile )
		ulHash = SourceSharpAssertStack_HashBytes( ulHash, pchFile, strlen( pchFile ) );

	ulHash = SourceSharpAssertStack_HashBytes( ulHash, &nLine, sizeof( nLine ) );

	if ( ppFrames && nFrames > 0 )
	{
		const int nUse = ( nFrames < kSourceSharpAssertStackKeyFrames )
		               ? nFrames : (int)kSourceSharpAssertStackKeyFrames;

		for ( int i = 0; i < nUse; ++i )
		{
			// The pointer VALUE, not the pointee.
			const unsigned long long ulAddr = (unsigned long long)(size_t)ppFrames[ i ];
			ulHash = SourceSharpAssertStack_HashBytes( ulHash, &ulAddr, sizeof( ulAddr ) );
		}
	}

	return ulHash;
}

//-----------------------------------------------------------------------------
// The set of reports already printed.
//
// A plain array scanned linearly: 128 entries touched once per FAILED assert,
// which in a healthy run is never and in the run this exists for is a handful
// of times a second. Nothing here is worth a hash table.
//
// Zero-initialised is a valid empty set, so a file-scope instance needs no
// constructor and is usable from static init — which matters, because asserts
// fire during module load.
//-----------------------------------------------------------------------------
struct SourceSharpAssertStackSeen_t
{
	SourceSharpAssertStackKey_t m_Keys[ kSourceSharpAssertStackMaxSeen ];
	int  m_nCount;
	bool m_bSaidFull;
};

inline void SourceSharpAssertStack_Reset( SourceSharpAssertStackSeen_t *pSeen )
{
	if ( !pSeen )
		return;

	pSeen->m_nCount = 0;
	pSeen->m_bSaidFull = false;
}

//-----------------------------------------------------------------------------
// Whether this report is new, and remember it if so.
//
// Answers true exactly once per distinct key. When the table is full it answers
// false for anything unrecognised — the alternatives are to flood or to evict,
// and evicting means a repeating assert prints forever once the table churns.
// *pbFirstOverflow is set on the single call that discovers fullness, so the
// caller can say "no more stacks" once instead of never or every time.
//-----------------------------------------------------------------------------
inline bool SourceSharpAssertStack_ShouldReport(
	SourceSharpAssertStackSeen_t *pSeen, SourceSharpAssertStackKey_t ulKey,
	bool *pbFirstOverflow )
{
	if ( pbFirstOverflow )
		*pbFirstOverflow = false;

	if ( !pSeen )
		return true;

	for ( int i = 0; i < pSeen->m_nCount; ++i )
	{
		if ( pSeen->m_Keys[ i ] == ulKey )
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

	pSeen->m_Keys[ pSeen->m_nCount++ ] = ulKey;
	return true;
}

//-----------------------------------------------------------------------------
// One stack frame, as a line.
//
//   #03 /path/to/client.so+0x00123abc  CBaseEntity::DrawModel
//
// The module-plus-offset form is not decoration: every binary here is position
// independent, so the absolute address in a backtrace means nothing after the
// process ends, and `addr2line -e <module> -f -i -C <offset>` is the command
// that turns the line into a file, a function and — with -i — the INLINED
// frames, which is where an assert inside a FORCEINLINE mesh-builder call
// actually lives. The line is written to be pasted into that command.
//
// Two degradations, both of which happen in practice:
//   - no module (dladdr failed): the absolute address alone, marked ??
//   - no symbol (a static function, or one the linker's --gc-sections kept
//     but did not name): the offset is still exact, so the line is still
//     usable; only the human-readable half is missing.
//
// An address BELOW the module base is nonsense — it cannot be an offset into
// that module — and is reported as if there were no module rather than as a
// wrapped-around 64-bit offset that looks authoritative and is not.
//
// Returns the number of characters written, never more than cchOut-1, and
// always leaves pchOut NUL-terminated.
//-----------------------------------------------------------------------------
inline int SourceSharpAssertStack_FormatFrame(
	char *pchOut, size_t cchOut, int nIndex,
	const char *pchModule, unsigned long long ulModuleBase,
	unsigned long long ulAddr, const char *pchSymbol )
{
	if ( !pchOut || cchOut == 0 )
		return 0;

	pchOut[ 0 ] = '\0';

	const bool bHaveModule = ( pchModule != NULL && pchModule[ 0 ] != '\0'
	                           && ulAddr >= ulModuleBase );
	const char *pchSym = ( pchSymbol && pchSymbol[ 0 ] ) ? pchSymbol : "??";

	int nWritten;
	if ( bHaveModule )
	{
		nWritten = snprintf( pchOut, cchOut, "  #%-2d %s+0x%08llx  %s",
		                     nIndex, pchModule, ulAddr - ulModuleBase, pchSym );
	}
	else
	{
		// No parenthesis after the question marks. "??(" was a TRIGRAPH under
		// -std=c++11 (strict ISO), replaced with "[" before the string was ever
		// a string: the unknown-module marker silently became "#0  [0x...)".
		// C++17 removed trigraphs, but the format stays as it is -- tests and
		// readers already know it.
		nWritten = snprintf( pchOut, cchOut, "  #%-2d ?? 0x%016llx  %s",
		                     nIndex, ulAddr, pchSym );
	}

	// snprintf reports what it WOULD have written. Clamp to what it did.
	if ( nWritten < 0 )
	{
		pchOut[ 0 ] = '\0';
		return 0;
	}

	if ( (size_t)nWritten >= cchOut )
		return (int)( cchOut - 1 );

	return nWritten;
}

//-----------------------------------------------------------------------------
// The same frame, as a LINE: with the newline that ends it.
//
// Separate from FormatFrame, and here rather than at the one call site, because
// the newline is the character that used to go missing and a call site inside
// the engine-linked emitter is not somewhere a test can reach. That emitter
// formatted a frame into one 1024-byte buffer and then did
// snprintf( szLine, 1024, "%s\n", szFrame ) into a second one of the SAME
// size -- so a frame whose text filled its buffer produced a line with no line
// ending at all, and the next frame's text carried on from where it stopped.
// Not hypothetical at the sizes involved: dli_sname is the mangled name,
// undemangled, and _ZN symbols in this tree run to hundreds of characters on
// top of a module path.
//
// One byte of cchOut is reserved for the newline before the frame is formatted
// at all, so the frame text is what gets clamped and the newline is what cannot
// be. Returns the length of the whole line, newline included, and always leaves
// pchOut NUL-terminated. A buffer with no room for even "\n" and its terminator
// is refused, the way FormatFrame refuses a degenerate one.
//-----------------------------------------------------------------------------
inline int SourceSharpAssertStack_FormatFrameLine(
	char *pchOut, size_t cchOut, int nIndex,
	const char *pchModule, unsigned long long ulModuleBase,
	unsigned long long ulAddr, const char *pchSymbol )
{
	if ( !pchOut || cchOut < 2 )
	{
		if ( pchOut && cchOut > 0 )
			pchOut[ 0 ] = '\0';
		return 0;
	}

	const int nLen = SourceSharpAssertStack_FormatFrame( pchOut, cchOut - 1, nIndex,
		pchModule, ulModuleBase, ulAddr, pchSymbol );

	pchOut[ nLen ] = '\n';
	pchOut[ nLen + 1 ] = '\0';

	return nLen + 1;
}

//-----------------------------------------------------------------------------
// A counter published as text, read back.
//
// The hook and the self-test can be in DIFFERENT DLLs — the hook is claimed once
// per process while the console command is registered per side — so the counters
// travel through the environment, which is the channel the claim itself already
// uses and the only one both halves share.
//
// Deliberately total: a missing variable, an empty one, a negative one and a
// corrupt one all answer zero rather than a stale or a garbage number. Zero is
// the SAFE answer here because the verdict compares before against after, so a
// channel that has broken and answers zero reports "the hook did not fire" — a
// loud, correct failure — rather than a quiet pass.
//-----------------------------------------------------------------------------
inline long long SourceSharpAssertStack_ParseCounter( const char *pchValue )
{
	if ( !pchValue )
		return 0;

	const char *pWalk = pchValue;

	// Leading blanks only. No sign is accepted: these counters never go
	// backwards, and reading "-1" as a number would let a corrupt value show up
	// as movement.
	while ( *pWalk == ' ' || *pWalk == '\t' )
		++pWalk;

	if ( *pWalk < '0' || *pWalk > '9' )
		return 0;

	long long llValue = 0;
	for ( ; *pWalk >= '0' && *pWalk <= '9'; ++pWalk )
	{
		// A count of anything here fits in far less than this. Past it, the
		// value did not come from the hook.
		if ( llValue > ( 9223372036854775807LL - 9 ) / 10 )
			return 0;

		llValue = llValue * 10 + ( *pWalk - '0' );
	}

	// Trailing anything means this was not written by the hook.
	if ( *pWalk != '\0' )
		return 0;

	return llValue;
}

//-----------------------------------------------------------------------------
// WHETHER A FAILED ASSERT ENDS THE PROCESS.
//
// Pure, and in the header, for the same reason
// SourceSharp_PredictionFieldTolerated is (prediction_ring.h:309): the decision
// has four inputs and three of them are switches a person sets, so the way it
// goes wrong is a wrong ANSWER rather than a crash -- a self-test that kills
// the game, or a survey run that stops at the first assert. Neither shows up as
// a failure anywhere. Put where gtest can reach it, it is one table of cases.
//
//   bDeliberate  the assertstack self-test's own assert, which is not a defect
//   bDemote      -assertlog: log every assert and keep going, for a survey
//   bFatal       the default: log, flush, stop
//
// Precedence is deliberate and is the whole content of this function:
// DELIBERATE beats everything (a self-test must never end the process),
// then -assertlog (an explicit request to survive), then fatal.
//-----------------------------------------------------------------------------
inline bool SourceSharp_AssertShouldBeFatal( bool bIsAssert, bool bFatal,
                                             bool bDemote, bool bDeliberate )
{
	if ( !bIsAssert )
		return false;
	if ( bDeliberate )
		return false;
	if ( bDemote )
		return false;
	return bFatal;
}

//-----------------------------------------------------------------------------
// WHETHER A SPEW LINE REACHES THE TERMINAL, and the fourth input is why this
// function exists at all.
//
// THE FAILURE THAT PAID FOR IT. On 2026-09-07 the live gate's save/load step
// killed the game three times and every one of them read, on the terminal and
// in the kept log:
//
//     *** stack smashing detected ***: terminated
//
// which sent a lane to the managed/native boundary looking for an overflowing
// P/Invoke buffer. There was none. The process had taken a SIGSEGV from
// Sys_Error_Internal's own `*(int *)0 = 0xdeadbeef` -- the engine's deliberate
// crash -- and CoreCLR's PAL signal handler smashed its own frame on the way to
// reporting it. The actual message, recovered out of a core dump, was
//
//     IVP Failed at ../ivp_utility/ivu_vhash.cxx 157
//
// spewed by vphysics at SPEW_ERROR. It passed through THIS hook and was not
// echoed, for one reason: it does not start with "[SourceSharp".
//
// SPEW_ERROR IS THE LAST THING THE PROCESS SAYS. The engine's own spew handler
// answers it with Sys_Error(), and the block
// above this one in shell_diagnostics.cpp already records what that costs. A
// message on that path has no later chance to be read, so the prefix filter --
// which exists so the terminal is not a second copy of the engine console --
// must not apply to it.
//
//   bClaimedEcho   this DLL won the one-echo-per-process claim. client.so and
//                  server.so both install the hook and both see every message;
//                  without this it prints twice.
//   bStackWriting  an -assertstack report is mid-write and has already put this
//                  line on stderr itself.
//   bOurs          the message begins with "[SourceSharp".
//   bFatal         SPEW_ERROR: the engine will not return from it.
//
// Precedence: the two SUPPRESSORS first and unconditionally -- echoing from the
// DLL that did not claim it, or on top of a report already writing the same
// line, is a duplicate whether or not the line is fatal -- then ours-or-fatal.
//-----------------------------------------------------------------------------
inline bool SourceSharp_SpewShouldEcho( bool bClaimedEcho, bool bStackWriting,
                                        bool bOurs, bool bFatal )
{
	if ( !bClaimedEcho )
		return false;
	if ( bStackWriting )
		return false;
	return bOurs || bFatal;
}

//-----------------------------------------------------------------------------
// AND WHETHER TO PRINT A STACK UNDER IT — lane 8e-physics.
//
// The predicate above made the fatal line VISIBLE. It is still only a line:
// `IVP Failed at ../ivp_utility/ivu_vhash.cxx 157` names the assert and not one
// of its callers, and the callers are the whole question — the same argument
// this header's own opening block makes about asserts. Diagnosing the last
// occurrence cost a session with gdb and three core dumps, and produced a stack
// that had to be walked by hand from a saved rbp because the binaries it named
// no longer existed.
//
// A fatal spew is the ONE message for which a thirty-line backtrace is never
// noise: the process is about to be killed by the engine's own deliberate null
// write, so there is no later message for it to bury and no next frame for it
// to cost.
//
//   bFatal          SPEW_ERROR: the engine answers with Sys_Error and does not
//                   return.
//   bAlreadyWritten a stack has already been written for a fatal in this
//                   process. The spew chain reaches both DLLs' hooks and an
//                   engine fatal can be re-spewed on the way down; one report
//                   is a report and two are a doubled report with no more in
//                   it.
//   bStackWriting   an -assertstack report is mid-write. Re-entering the frame
//                   walk from inside it is the recursion the ordinary hook
//                   already guards against.
//
// Deliberately NOT gated on -assertstack. That switch exists because a
// backtrace per ASSERT is thirty lines on a frame walk that may fire every
// frame; a fatal fires once and ends the process, and requiring a flag to be
// set in advance is exactly why the last three occurrences had no stack.
//-----------------------------------------------------------------------------
inline bool SourceSharp_FatalSpewShouldWriteStack( bool bFatal, bool bAlreadyWritten,
                                                   bool bStackWriting )
{
	if ( !bFatal )
		return false;
	if ( bAlreadyWritten )
		return false;
	if ( bStackWriting )
		return false;
	return true;
}
