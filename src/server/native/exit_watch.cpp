// SS_REASON a  a vtable the engine calls through
// Everything here runs where managed code cannot: a signal handler (no
// allocation, no locks) and SDL's event watch.

#include "exit_watch.h"

#include "ss_platform.h"

#include <atomic>
#include <fcntl.h>

#include <time.h>

#if defined( _WIN32 )
#include <io.h>
#include <sys/stat.h>
#else
#include <signal.h>
#endif

namespace
{

// Requests to end. Faults belong to the runtime and the launcher. POSIX only:
// Windows has no SIGHUP or sigaction, so there the signal half arms nothing.
#if !defined( _WIN32 )
const int kWatched[] = { SIGTERM, SIGINT, SIGHUP };

enum
{
	kWatchedCount = sizeof( kWatched ) / sizeof( kWatched[0] )
};
#else
enum
{
	kWatchedCount = 3
};
#endif

// The SDL2 the launcher already loaded; found, never loaded.
#if defined( _WIN32 )
#define SS_SDL_LIBRARY "SDL2.dll"
#else
#define SS_SDL_LIBRARY "libSDL2-2.0.so.0"
#endif

// SDL2's event numbers and SDL_WindowEvent's sub-event offset, spelled out
// because server.so builds against no SDL headers.
enum : uint32_t
{
	kSdlQuit = 0x100,
	kSdlWindowEvent = 0x200
};

enum : uint8_t
{
	kSdlWindowEventClose = 14
};

enum
{
	kSdlWindowSubEventOffset = 12
};

typedef int ( *SdlEventFilter_t )( void *pUserData, void *pEvent );
typedef void ( *SdlAddEventWatch_t )( SdlEventFilter_t pFilter, void *pUserData );
typedef void ( *SdlDelEventWatch_t )( SdlEventFilter_t pFilter, void *pUserData );

// Zero-initialised, so constant-initialised and safe to touch from a signal handler.
struct ExitWatchState_t
{
	std::atomic<int32_t> nArmed;
	std::atomic<int64_t> nArmedAtNs;

	std::atomic<int32_t> nSignals;
	std::atomic<int32_t> nFirstSigno, nFirstPid, nFirstUid, nFirstAtMs;
	std::atomic<int32_t> nLastSigno, nLastPid, nLastUid, nLastAtMs;

	std::atomic<int32_t> nSdlQuits, nSdlQuitAtMs;
	std::atomic<int32_t> nWindowCloses, nWindowCloseAtMs;

	int32_t nPrevKind[kWatchedCount];
#if !defined( _WIN32 )
	struct sigaction prev[kWatchedCount];
#endif

	// Where a fatal stop writes its record. Set by the arm, on the game thread.
	char szRecordPath[512];
};

ExitWatchState_t g_ExitWatch;

int64_t MonotonicNs()
{
#if defined( _WIN32 )
	LARGE_INTEGER freq, now;
	::QueryPerformanceFrequency( &freq );
	::QueryPerformanceCounter( &now );
	return int64_t( ( (double)now.QuadPart / (double)freq.QuadPart ) * 1e9 );
#else
	struct timespec ts;
	clock_gettime( CLOCK_MONOTONIC, &ts );
	return int64_t( ts.tv_sec ) * 1000000000 + ts.tv_nsec;
#endif
}

int32_t MsSinceArm()
{
	const int64_t nAt = g_ExitWatch.nArmedAtNs.load( std::memory_order_relaxed );
	return nAt == 0 ? 0 : int32_t( ( MonotonicNs() - nAt ) / 1000000 );
}

#if !defined( _WIN32 )
int IndexOf( int nSigno )
{
	for ( int i = 0; i < kWatchedCount; ++i )
	{
		if ( kWatched[i] == nSigno )
			return i;
	}
	return -1;
}
#endif

void Record( int nSigno, int nPid, int nUid )
{
	const int32_t nAt = MsSinceArm();
	if ( g_ExitWatch.nSignals.fetch_add( 1 ) == 0 )
	{
		g_ExitWatch.nFirstSigno = nSigno;
		g_ExitWatch.nFirstPid = nPid;
		g_ExitWatch.nFirstUid = nUid;
		g_ExitWatch.nFirstAtMs = nAt;
	}

	g_ExitWatch.nLastSigno = nSigno;
	g_ExitWatch.nLastPid = nPid;
	g_ExitWatch.nLastUid = nUid;
	g_ExitWatch.nLastAtMs = nAt;
}

#if !defined( _WIN32 )
// The handler that was there before, run as it would have been. The default
// action is re-delivered: the signal is blocked while this handler runs, so the
// raise is taken the moment it returns, with the default disposition back.
void Chain( int nSigno, siginfo_t *pInfo, void *pContext )
{
	const int i = IndexOf( nSigno );
	if ( i < 0 )
		return;

	const struct sigaction &prev = g_ExitWatch.prev[i];

	if ( prev.sa_flags & SA_SIGINFO )
	{
		if ( prev.sa_sigaction )
			prev.sa_sigaction( nSigno, pInfo, pContext );
		return;
	}

	if ( prev.sa_handler == SIG_IGN )
		return;

	if ( prev.sa_handler == SIG_DFL )
	{
		struct sigaction dfl;
		memset( &dfl, 0, sizeof( dfl ) );
		dfl.sa_handler = SIG_DFL;
		sigemptyset( &dfl.sa_mask );
		sigaction( nSigno, &dfl, NULL );
		raise( nSigno );
		return;
	}

	prev.sa_handler( nSigno );
}

void OnSignal( int nSigno, siginfo_t *pInfo, void *pContext )
{
	Record( nSigno, pInfo ? int( pInfo->si_pid ) : 0, pInfo ? int( pInfo->si_uid ) : 0 );
	Chain( nSigno, pInfo, pContext );
}
#endif

int OnSdlEvent( void *, void *pEvent )
{
	if ( !pEvent )
		return 1;

	uint32_t nType;
	memcpy( &nType, pEvent, sizeof( nType ) );

	if ( nType == kSdlQuit )
	{
		if ( g_ExitWatch.nSdlQuits.fetch_add( 1 ) == 0 )
			g_ExitWatch.nSdlQuitAtMs = MsSinceArm();
	}
	else if ( nType == kSdlWindowEvent &&
			  static_cast<const uint8_t *>( pEvent )[kSdlWindowSubEventOffset] == kSdlWindowEventClose )
	{
		if ( g_ExitWatch.nWindowCloses.fetch_add( 1 ) == 0 )
			g_ExitWatch.nWindowCloseAtMs = MsSinceArm();
	}

	return 1;
}

#if !defined( _WIN32 )
int32_t KindOf( const struct sigaction &prev )
{
	if ( prev.sa_flags & SA_SIGINFO )
		return 3;
	if ( prev.sa_handler == SIG_IGN )
		return 1;
	if ( prev.sa_handler == SIG_DFL )
		return 0;
	return 2;
}

int32_t ArmSignals()
{
	for ( int i = 0; i < kWatchedCount; ++i )
	{
		struct sigaction mine;
		memset( &mine, 0, sizeof( mine ) );
		mine.sa_sigaction = OnSignal;
		mine.sa_flags = SA_SIGINFO | SA_RESTART | SA_ONSTACK;
		sigemptyset( &mine.sa_mask );

		if ( sigaction( kWatched[i], &mine, &g_ExitWatch.prev[i] ) != 0 )
			return 0;

		g_ExitWatch.nPrevKind[i] = KindOf( g_ExitWatch.prev[i] );
	}

	return kExitWatchSignals;
}

void RestoreSignals()
{
	for ( int i = 0; i < kWatchedCount; ++i )
		sigaction( kWatched[i], &g_ExitWatch.prev[i], NULL );
}
#else
int32_t ArmSignals()
{
	return 0;
}

void RestoreSignals() {}
#endif

// The SDL already in the process, or NULL (RTLD_NOLOAD). Release with SdlRelease.
#if defined( _WIN32 )
typedef HMODULE SdlHandle_t;

SdlHandle_t SdlFind()
{
	return ::GetModuleHandleA( SS_SDL_LIBRARY );
}

void *SdlSym( SdlHandle_t h, const char *pName )
{
	return (void *)::GetProcAddress( h, pName );
}

void SdlRelease( SdlHandle_t ) {}
#else
typedef void *SdlHandle_t;

SdlHandle_t SdlFind()
{
	return dlopen( SS_SDL_LIBRARY, RTLD_NOW | RTLD_NOLOAD );
}

void *SdlSym( SdlHandle_t h, const char *pName )
{
	return dlsym( h, pName );
}

void SdlRelease( SdlHandle_t h )
{
	dlclose( h );
}
#endif

int32_t ArmSdl()
{
	SdlHandle_t pSdl = SdlFind();
	if ( !pSdl )
		return 0;

	SdlAddEventWatch_t pAdd = reinterpret_cast<SdlAddEventWatch_t>( SdlSym( pSdl, "SDL_AddEventWatch" ) );
	if ( pAdd )
		pAdd( OnSdlEvent, NULL );

	SdlRelease( pSdl );
	return pAdd ? kExitWatchSdl : 0;
}

// JSON string body, escaped, into a fixed buffer: no allocation, because the
// fatal path must work from inside a failing allocator.
size_t AppendEscaped( char *pOut, size_t nAt, size_t nCap, const char *pText )
{
	for ( const char *p = pText ? pText : ""; *p && nAt + 7 < nCap; ++p )
	{
		const unsigned char c = static_cast<unsigned char>( *p );
		if ( c == '"' || c == '\\' )
		{
			pOut[nAt++] = '\\';
			pOut[nAt++] = char( c );
		}
		else if ( c < 0x20 )
		{
			nAt += size_t( snprintf( pOut + nAt, nCap - nAt, "\\u%04x", c ) );
		}
		else
		{
			pOut[nAt++] = char( c );
		}
	}
	return nAt;
}

} // namespace

int32_t ExitWatch_Arm( const char *pRecordPath )
{
	if ( pRecordPath )
	{
		strncpy( g_ExitWatch.szRecordPath, pRecordPath, sizeof( g_ExitWatch.szRecordPath ) - 1 );
		g_ExitWatch.szRecordPath[sizeof( g_ExitWatch.szRecordPath ) - 1] = '\0';
	}

	// Once per process: a second recorder would chain to the first and count
	// every signal twice.
	if ( g_ExitWatch.nArmed.load() != 0 )
		return g_ExitWatch.nArmed.load() & ~0x100;

	g_ExitWatch.nArmedAtNs = MonotonicNs();
	const int32_t nArmed = ArmSignals() | ArmSdl() | 0x100; // 0x100: armed at all
	g_ExitWatch.nArmed = nArmed;
	return nArmed & ~0x100;
}

// Restores the signal handlers and removes SDL's event watch before this module
// is unmapped (SDL would otherwise call into unmapped code on the next pump).
// Recorded facts are kept: managed writes its exit record after disarming.
int32_t ExitWatch_Disarm()
{
	const int32_t nWasArmed = g_ExitWatch.nArmed.load() & ~0x100;
	if ( nWasArmed == 0 )
		return 0;

	if ( nWasArmed & kExitWatchSignals )
		RestoreSignals();

	if ( nWasArmed & kExitWatchSdl )
	{
		SdlHandle_t pSdl = SdlFind();
		if ( pSdl )
		{
			SdlDelEventWatch_t pDel = reinterpret_cast<SdlDelEventWatch_t>( SdlSym( pSdl, "SDL_DelEventWatch" ) );
			if ( pDel )
				pDel( OnSdlEvent, NULL );
			SdlRelease( pSdl );
		}
	}

	// Cleared so a later Arm installs again.
	g_ExitWatch.nArmed = 0;
	return nWasArmed;
}

int32_t ExitWatch_Read( int32_t *pOut, int32_t nMax )
{
	if ( !pOut || nMax <= 0 )
		return 0;

	const int32_t facts[kExitFactCount] = {
		1,
		g_ExitWatch.nArmed.load() & ~0x100,
		MsSinceArm(),
		g_ExitWatch.nSignals.load(),
		g_ExitWatch.nFirstSigno.load(),
		g_ExitWatch.nFirstPid.load(),
		g_ExitWatch.nFirstUid.load(),
		g_ExitWatch.nFirstAtMs.load(),
		g_ExitWatch.nLastSigno.load(),
		g_ExitWatch.nLastPid.load(),
		g_ExitWatch.nLastUid.load(),
		g_ExitWatch.nLastAtMs.load(),
		g_ExitWatch.nSdlQuits.load(),
		g_ExitWatch.nSdlQuitAtMs.load(),
		g_ExitWatch.nWindowCloses.load(),
		g_ExitWatch.nWindowCloseAtMs.load(),
		g_ExitWatch.nPrevKind[0],
		g_ExitWatch.nPrevKind[1],
		g_ExitWatch.nPrevKind[2],
	};

	const int32_t n = nMax < kExitFactCount ? nMax : kExitFactCount;
	memcpy( pOut, facts, sizeof( int32_t ) * size_t( n ) );
	return n;
}

void ExitWatch_OnSignalForTest( int nSigno, int nSenderPid, int nSenderUid )
{
	Record( nSigno, nSenderPid, nSenderUid );
}

void ExitWatch_OnSdlEventForTest( const void *pEvent )
{
	OnSdlEvent( NULL, const_cast<void *>( pEvent ) );
}

void ExitWatch_ResetForTest()
{
	if ( g_ExitWatch.nArmed.load() & kExitWatchSignals )
		RestoreSignals();

	// The SDL watch is not removed: a test process has no SDL to have added it to.
	g_ExitWatch.nArmed = 0;
	g_ExitWatch.nArmedAtNs = 0;
	g_ExitWatch.nSignals = 0;
	g_ExitWatch.nFirstSigno = g_ExitWatch.nFirstPid = g_ExitWatch.nFirstUid = g_ExitWatch.nFirstAtMs = 0;
	g_ExitWatch.nLastSigno = g_ExitWatch.nLastPid = g_ExitWatch.nLastUid = g_ExitWatch.nLastAtMs = 0;
	g_ExitWatch.nSdlQuits = g_ExitWatch.nSdlQuitAtMs = 0;
	g_ExitWatch.nWindowCloses = g_ExitWatch.nWindowCloseAtMs = 0;
	memset( g_ExitWatch.nPrevKind, 0, sizeof( g_ExitWatch.nPrevKind ) );
	g_ExitWatch.szRecordPath[0] = '\0';
}

// A fatal stop's record, written to the path managed named, since managed code
// never runs again. Weakly referenced by SourceSharp_FatalReport.
extern "C" void SourceSharp_FatalWitness( const char *pKind, const char *pMsg )
{
	if ( g_ExitWatch.szRecordPath[0] == '\0' )
		return;

	char szJson[4096];
	size_t n = size_t( snprintf( szJson, sizeof( szJson ),
								 "{\"pid\":%d,\"reason\":\"fatal\",\"writtenBy\":\"native fatal report\",\"kind\":\"",
								 SS_GetPid() ) );
	n = AppendEscaped( szJson, n, sizeof( szJson ), pKind );
	n += size_t( snprintf( szJson + n, sizeof( szJson ) - n, "\",\"message\":\"" ) );
	n = AppendEscaped( szJson, n, sizeof( szJson ), pMsg );
	n += size_t( snprintf( szJson + n, sizeof( szJson ) - n, "\",\"signals\":%d,\"msSinceArm\":%d}\n",
						   g_ExitWatch.nSignals.load(), MsSinceArm() ) );
	if ( n > sizeof( szJson ) )
		n = sizeof( szJson );

#if defined( _WIN32 )
	const int fd = _open( g_ExitWatch.szRecordPath, _O_WRONLY | _O_CREAT | _O_TRUNC, _S_IREAD | _S_IWRITE );
	if ( fd < 0 )
		return;
	const int nWritten = _write( fd, szJson, (unsigned)n );
	(void)nWritten;
	_close( fd );
#else
	const int fd = open( g_ExitWatch.szRecordPath, O_WRONLY | O_CREAT | O_TRUNC, 0644 );
	if ( fd < 0 )
		return;

	ssize_t nWritten = write( fd, szJson, n );
	(void)nWritten;
	close( fd );
#endif
}

// The ABI slots (IShimDiagnostics).
int32_t SourceSharp_ExitWatchArm( const char *pRecordPath )
{
	return ExitWatch_Arm( pRecordPath );
}

int32_t SourceSharp_ExitWatchRead( int32_t *pOut, int32_t nMax )
{
	return ExitWatch_Read( pOut, nMax );
}

int32_t SourceSharp_ExitWatchDisarm()
{
	return ExitWatch_Disarm();
}
