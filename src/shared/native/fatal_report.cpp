//========= SourceSharp — the one way this mod stops the process ==============//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//=============================================================================//

#include "fatal_report.h"
#include "ss_platform.h"

#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined( _WIN32 )
	#include <io.h>
	#include <sys/stat.h>
#else
	#include <unistd.h>
#endif

#if defined( SS_COVERAGE )
extern "C" void __gcov_dump( void );
#endif

// Deep enough for the frame loop plus a render path plus the leaf.
enum { kFatalStackDepth = 48 };

//-----------------------------------------------------------------------------
// The CRT's names for the POSIX file calls, where they differ. Nothing here
// goes through stdio: an assert that fired inside the allocator is exactly
// the case where the report must still come out, so the file is written
// with open/write and the lines are formatted into a fixed buffer.
//-----------------------------------------------------------------------------
#if defined( _WIN32 )
	#define ss_open( path ) _open( path, _O_WRONLY | _O_CREAT | _O_TRUNC, _S_IREAD | _S_IWRITE )
	#define ss_write _write
	#define ss_close _close
	#define ss_fileno _fileno
#else
	#define ss_open( path ) open( path, O_WRONLY | O_CREAT | O_TRUNC, 0644 )
	#define ss_write write
	#define ss_close close
	#define ss_fileno fileno
#endif

static void SourceSharp_Printf( int fd, const char *pFormat, ... )
{
	char szLine[ 1024 ];
	va_list args;
	va_start( args, pFormat );
	int n = vsnprintf( szLine, sizeof( szLine ), pFormat, args );
	va_end( args );
	if ( n < 0 )
		return;
	if ( (size_t)n >= sizeof( szLine ) )
		n = (int)sizeof( szLine ) - 1;
	const int nWritten = (int)ss_write( fd, szLine, (unsigned)n );
	(void)nWritten;
}

//-----------------------------------------------------------------------------
// THE ONE WAY THIS MOD STOPS THE PROCESS ON PURPOSE -- a failed assert, or
// Error(). The user's design (2026-09-29): the report and the stack go to
// stderr, where every scripted check looks; the same report goes to
// $SOURCESHARP_ASSERT_DIR/assert-<pid>.txt when the launcher named a directory,
// so it can show the report in a message box or leave it on disk; and the
// process ends with a negative code, kSourceSharpFatalExitCode, which is how
// the launcher tells this from any other exit.
//
// Nothing here allocates: an assert that fired inside the allocator is exactly
// the case where the report must still come out. The stack is printed with
// backtrace_symbols_fd where glibc offers it, and as module+offset lines (the
// form addr2line and a debugger take) elsewhere; the file is written with
// open/write.
//-----------------------------------------------------------------------------
static void SourceSharp_WriteReport( int fd, const char *pKind, const char *pMsg, bool bStopLine )
{
	SourceSharp_Printf( fd, "[SourceSharp] FATAL %s: %s", pKind, pMsg ? pMsg : "(no message)\n" );
	if ( pMsg && pMsg[0] && pMsg[ strlen( pMsg ) - 1 ] != '\n' )
		SourceSharp_Printf( fd, "\n" );

	if ( bStopLine )
	{
		SourceSharp_Printf( fd, "[SourceSharp] the process is stopping here.%s\n",
		         strcmp( pKind, "ASSERT" ) == 0
		             ? " Pass -assertlog to log assertions and keep going." : "" );
	}

	void *pFrames[ kFatalStackDepth ];
	const int nFrames = SS_Backtrace( pFrames, kFatalStackDepth );
	SourceSharp_Printf( fd, "[SourceSharp] stack (%d frame(s)):\n", nFrames );
#if defined( __linux__ )
	backtrace_symbols_fd( pFrames, nFrames, fd );
#else
	for ( int i = 0; i < nFrames; ++i )
	{
		SSModuleInfo info;
		if ( SS_ModuleOfAddress( pFrames[ i ], &info ) )
		{
			SourceSharp_Printf( fd, "%s(+0x%llx) [%p]\n", info.szPath,
			                    (unsigned long long)( (const char *)pFrames[ i ] - (const char *)info.pBase ),
			                    pFrames[ i ] );
		}
		else
		{
			SourceSharp_Printf( fd, "[%p]\n", pFrames[ i ] );
		}
	}
#endif
}

// The exit watch's record of a fatal stop (server/native/exit_watch.cpp). WEAK:
// server.so defines it, client.so does not, and an undefined weak reference is
// null -- so the client's fatal path is exactly what it was. MSVC has no weak
// references; /alternatename gives the name a fallback definition that the
// linker takes only when nothing else defines it, which is the same thing
// spelled for link.exe (the fallback leaves the pointer comparison below
// true, and the fallback does nothing).
#if defined( _MSC_VER )
extern "C" void SourceSharp_FatalWitness( const char *pKind, const char *pMsg );
extern "C" void SourceSharp_FatalWitness_NoWitness( const char *, const char * ) {}
#pragma comment( linker, "/alternatename:SourceSharp_FatalWitness=SourceSharp_FatalWitness_NoWitness" )
#else
extern "C" void SourceSharp_FatalWitness( const char *pKind, const char *pMsg ) __attribute__(( weak ));
#endif

void SourceSharp_FatalReport( const char *pKind, const char *pMsg )
{
	fflush( stderr );
	SourceSharp_WriteReport( ss_fileno( stderr ), pKind, pMsg, true );

#if defined( _MSC_VER )
	// The alternate name above always binds (to the no-op when nothing else
	// defines the witness), so there is no null to test.
	SourceSharp_FatalWitness( pKind, pMsg );
#else
	if ( SourceSharp_FatalWitness )
		SourceSharp_FatalWitness( pKind, pMsg );
#endif

	const char *pszDir = getenv( "SOURCESHARP_ASSERT_DIR" );
	if ( pszDir && pszDir[0] )
	{
		char szPath[ 1024 ];
		snprintf( szPath, sizeof( szPath ), "%s/assert-%d.txt", pszDir, SS_GetPid() );
		const int fd = ss_open( szPath );
		if ( fd >= 0 )
		{
			SourceSharp_WriteReport( fd, pKind, pMsg, false );
			ss_close( fd );
		}
		else
		{
			fprintf( stderr, "[SourceSharp] could not write the report to %s\n", szPath );
		}
	}

	fflush( NULL );
#if defined( SS_COVERAGE )
	{
		fprintf( stderr, "[SourceSharp] coverage build: dumping .gcda before _exit\n" );
		__gcov_dump();
		fflush( NULL );
	}
#endif
	_exit( kSourceSharpFatalExitCode );
}
