// SS_REASON h  the CoreCLR host and bootstrap dispatch

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
extern "C" void __gcov_dump();
#endif

// Deep enough for the frame loop plus a render path plus the leaf.
enum
{
	kFatalStackDepth = 48
};

// POSIX file calls under the CRT's names. Nothing here uses stdio or allocates, so a
// report from an assert inside the allocator still comes out.
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
	char szLine[1024];
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

// Writes the report and the stack: backtrace_symbols_fd on glibc, module+offset lines
// elsewhere.
static void SourceSharp_WriteReport( int fd, const char *pKind, const char *pMsg, bool bStopLine )
{
	SourceSharp_Printf( fd, "[SourceSharp] FATAL %s: %s", pKind, pMsg ? pMsg : "(no message)\n" );
	if ( pMsg && pMsg[0] && pMsg[strlen( pMsg ) - 1] != '\n' )
		SourceSharp_Printf( fd, "\n" );

	if ( bStopLine )
	{
		SourceSharp_Printf( fd, "[SourceSharp] the process is stopping here.%s\n",
							strcmp( pKind, "ASSERT" ) == 0 ? " Pass -assertlog to log assertions and keep going."
														   : "" );
	}

	void *pFrames[kFatalStackDepth];
	const int nFrames = SS_Backtrace( pFrames, kFatalStackDepth );
	SourceSharp_Printf( fd, "[SourceSharp] stack (%d frame(s)):\n", nFrames );
#if defined( __linux__ )
	backtrace_symbols_fd( pFrames, nFrames, fd );
#else
	for ( int i = 0; i < nFrames; ++i )
	{
		SSModuleInfo info;
		if ( SS_ModuleOfAddress( pFrames[i], &info ) )
		{
			SourceSharp_Printf( fd, "%s(+0x%llx) [%p]\n", info.szPath,
								(unsigned long long)( (const char *)pFrames[i] - (const char *)info.pBase ),
								pFrames[i] );
		}
		else
		{
			SourceSharp_Printf( fd, "[%p]\n", pFrames[i] );
		}
	}
#endif
}

// Defined by server.so's exit watch only; weak, so client.so's reference is null.
// MSVC has no weak references, so /alternatename binds a no-op fallback instead.
#if defined( _MSC_VER )
extern "C" void SourceSharp_FatalWitness( const char *pKind, const char *pMsg );

extern "C" void SourceSharp_FatalWitness_NoWitness( const char *, const char * ) {}

#pragma comment( linker, "/alternatename:SourceSharp_FatalWitness=SourceSharp_FatalWitness_NoWitness" )
#else
extern "C" void SourceSharp_FatalWitness( const char *pKind, const char *pMsg ) __attribute__( ( weak ) );
#endif

void SourceSharp_FatalReport( const char *pKind, const char *pMsg )
{
	fflush( stderr );
	SourceSharp_WriteReport( ss_fileno( stderr ), pKind, pMsg, true );

#if defined( _MSC_VER )
	// The alternate name always binds, so there is no null to test.
	SourceSharp_FatalWitness( pKind, pMsg );
#else
	if ( SourceSharp_FatalWitness )
		SourceSharp_FatalWitness( pKind, pMsg );
#endif

	const char *pszDir = getenv( "SOURCESHARP_ASSERT_DIR" );
	if ( pszDir && pszDir[0] )
	{
		char szPath[1024];
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
