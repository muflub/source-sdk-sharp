//========= SourceSharp — the one way this mod stops the process ==============//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//=============================================================================//

#include "fatal_report.h"

#include <execinfo.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#if defined( SS_COVERAGE )
extern "C" void __gcov_dump( void );
#endif

// Deep enough for the frame loop plus a render path plus the leaf.
enum { kFatalStackDepth = 48 };

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
// backtrace_symbols_fd and the file is written with open/write.
//-----------------------------------------------------------------------------
static void SourceSharp_WriteReport( int fd, const char *pKind, const char *pMsg, bool bStopLine )
{
	dprintf( fd, "[SourceSharp] FATAL %s: %s", pKind, pMsg ? pMsg : "(no message)\n" );
	if ( pMsg && pMsg[0] && pMsg[ strlen( pMsg ) - 1 ] != '\n' )
		dprintf( fd, "\n" );

	if ( bStopLine )
	{
		dprintf( fd, "[SourceSharp] the process is stopping here.%s\n",
		         strcmp( pKind, "ASSERT" ) == 0
		             ? " Pass -assertlog to log assertions and keep going." : "" );
	}

	void *pFrames[ kFatalStackDepth ];
	const int nFrames = backtrace( pFrames, kFatalStackDepth );
	dprintf( fd, "[SourceSharp] stack (%d frame(s)):\n", nFrames );
	backtrace_symbols_fd( pFrames, nFrames, fd );
}

// The exit watch's record of a fatal stop (server/native/exit_watch.cpp). WEAK:
// server.so defines it, client.so does not, and an undefined weak reference is
// null -- so the client's fatal path is exactly what it was.
extern "C" void SourceSharp_FatalWitness( const char *pKind, const char *pMsg ) __attribute__(( weak ));

void SourceSharp_FatalReport( const char *pKind, const char *pMsg )
{
	fflush( stderr );
	SourceSharp_WriteReport( fileno( stderr ), pKind, pMsg, true );

	if ( SourceSharp_FatalWitness )
		SourceSharp_FatalWitness( pKind, pMsg );

	const char *pszDir = getenv( "SOURCESHARP_ASSERT_DIR" );
	if ( pszDir && pszDir[0] )
	{
		char szPath[ 1024 ];
		snprintf( szPath, sizeof( szPath ), "%s/assert-%d.txt", pszDir, (int)getpid() );
		const int fd = open( szPath, O_WRONLY | O_CREAT | O_TRUNC, 0644 );
		if ( fd >= 0 )
		{
			SourceSharp_WriteReport( fd, pKind, pMsg, false );
			close( fd );
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

