//========= SourceSharp — .NET runtime host for Source SDK 2013 ================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// Owns the single per-process CoreCLR instance. See plan.md Part 1.
//
// THIS FILE IS libmanaged_host.so, ITS OWN SHARED OBJECT, and it has to be.
// It used to be a static lib linked into client.so AND server.so, so "the
// single per-process instance" above was two instances: g_State is file-static
// and emits `_ZL7g_State` with LOCAL binding, which no loader can share or
// interpose. Both modules therefore ran Acquire's cold path and both logged
// refcount 1. See sourcesharp/sourcesharp.vpc for the measurement, and
// test/managed_host_single_instance_test.cpp for the gate that keeps it one.
//
//=============================================================================//

#include "sourcesharp.h"

#include "include/hostfxr.h"
#include "include/coreclr_delegates.h"

#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <stdarg.h>

#ifdef _WIN32
	#include <windows.h>
	#define SS_PATHSEP "\\"
	typedef HMODULE ss_module_t;
	static ss_module_t ss_dlopen( const char *p ) { return ::LoadLibraryA( p ); }
	static void *ss_dlsym( ss_module_t m, const char *s ) { return (void *)::GetProcAddress( m, s ); }
	static void ss_setenv( const char *k, const char *v ) { ::SetEnvironmentVariableA( k, v ); }
#else
	#include <dlfcn.h>
	#include <dirent.h>
	#include <unistd.h>
	#if defined( __linux__ )
		#include <sys/prctl.h>
	#endif
	#define SS_PATHSEP "/"
	typedef void *ss_module_t;
	static ss_module_t ss_dlopen( const char *p ) { return ::dlopen( p, RTLD_LAZY | RTLD_LOCAL ); }
	static void *ss_dlsym( ss_module_t m, const char *s ) { return ::dlsym( m, s ); }
	static void ss_setenv( const char *k, const char *v ) { ::setenv( k, v, 1 ); }
#endif

//-----------------------------------------------------------------------------
// Managed bootstrap signatures.
//-----------------------------------------------------------------------------
// plan_side_entries SE1: one set per side (ServerEntry, ClientEntry), so no
// side travels as an argument.
typedef int32_t (CORECLR_DELEGATE_CALLTYPE *bootstrap_load_fn)(
	void *pShimRoot, int32_t nShimRootEntries, void **ppManagedRoot );
typedef int32_t (CORECLR_DELEGATE_CALLTYPE *bootstrap_unload_fn)( void );
typedef int32_t (CORECLR_DELEGATE_CALLTYPE *bootstrap_reload_check_fn)( char *pWhy, int32_t nWhy );

// A side's three Bootstrap entries, resolved once at Acquire.
struct SourceSharpSideEntries
{
	const char					*pName;   // "server" / "client", for the log
	const char					*pSuffix; // "Server" / "Client": LoadServer, UnloadClient, ...
	const char					*pType;   // the Bootstrap type that serves the side
	bootstrap_load_fn			pfnLoad;
	bootstrap_unload_fn			pfnUnload;
	bootstrap_reload_check_fn	pfnReloadCheck;
};

//-----------------------------------------------------------------------------
// WHICH MODULE IS CALLING, AND WHY EVERY REFCOUNT LINE HAS TO SAY SO.
//
// This file used to be a static lib linked into both client.so and server.so,
// which gave each of them its own g_State and so its own refcount. On a listen
// server BOTH modules therefore ran the cold path in SourceSharp_Acquire and
// both logged "refcount 1", while hostfxr told the second one
// Success_HostAlreadyInitialized. Nothing in the log said which module a line
// came from, so two cold acquires read exactly like one -- and that is why the
// split survived thirteen green phases. It is now libmanaged_host.so, mapped
// once per process, but the log lines stay tagged: the tag is the evidence that
// would have caught it, and it is the evidence that catches a regression.
//
// The tag is the CALLER's module, not this one -- dladdr on the return address.
// dladdr on a function in this file would answer "libmanaged_host.so" every
// time, which is true and useless.
//-----------------------------------------------------------------------------
struct SourceSharpCaller
{
	const char	*pName;		// basename of the calling module, for logging
	const void	*pBase;		// its load address, for identity comparisons
};

static SourceSharpCaller SS_DescribeCaller( const void *pReturnAddress )
{
	SourceSharpCaller caller = { "?", NULL };

#ifdef _WIN32
	HMODULE hModule = NULL;
	if ( ::GetModuleHandleExA(
			GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
			(LPCSTR)pReturnAddress, &hModule ) && hModule )
	{
		caller.pBase = (const void *)hModule;
		caller.pName = "module";
	}
#else
	Dl_info info;
	if ( ::dladdr( pReturnAddress, &info ) )
	{
		caller.pBase = info.dli_fbase;
		if ( info.dli_fname )
		{
			const char *pSlash = strrchr( info.dli_fname, '/' );
			caller.pName = pSlash ? pSlash + 1 : info.dli_fname;
		}
	}
#endif

	return caller;
}

#if defined( _MSC_VER )
	#include <intrin.h>
	#pragma intrinsic( _ReturnAddress )
	#define SS_RETURN_ADDRESS()	_ReturnAddress()
#else
	#define SS_RETURN_ADDRESS()	__builtin_return_address( 0 )
#endif

#define SS_CALLER()	SS_DescribeCaller( SS_RETURN_ADDRESS() )

// hostfxr's strings are char_t: char on Linux and macOS, wchar_t on Windows,
// where the runtime takes UTF-16 paths and names. This host builds them as
// UTF-8; SS_HostString converts at the call, a temporary that lives to the
// end of the full expression the call is in. Longer than the buffer is
// empty, which hostfxr then refuses with its own error code.
#ifdef _WIN32
struct SS_HostString
{
	wchar_t sz[1024];
	explicit SS_HostString( const char *pUtf8 )
	{
		if ( !MultiByteToWideChar( CP_UTF8, 0, pUtf8, -1, sz, (int)( sizeof( sz ) / sizeof( sz[0] ) ) ) )
			sz[0] = L'\0';
	}
	const char_t *c_str() const { return sz; }
};
#else
struct SS_HostString
{
	const char *p;
	explicit SS_HostString( const char *pUtf8 ) : p( pUtf8 ) {}
	const char_t *c_str() const { return p; }
};
#endif

//-----------------------------------------------------------------------------
// THE LOGGER IS A TWO-DEEP STACK, AND IT HAS TO BE.
//
// Both game DLLs install a logger, each a function in their OWN module. When
// this state was per-module that was two independent variables and could not go
// wrong. It is now one variable shared by both, so "last writer wins" would
// leave the SERVER's host output going through a function pointer into
// client.so -- and client.so is a module that is, at shutdown, on its way out.
// Calling through it after it is gone is a jump into unmapped memory, which is
// the exact failure shape this whole change exists to remove; introducing a new
// one while fixing the old would be a poor trade.
//
// So each installer is remembered against its module's load address, the newest
// live one is used, and SourceSharp_Release drops the releasing module's entry.
// After the client goes, host output falls back to the server's logger rather
// than to stdout -- which is strictly better than either "dangle" or "go quiet".
// Two slots is the whole population: there are two sides.
//-----------------------------------------------------------------------------
enum { kSourceSharpMaxLogSlots = 2 };

struct SourceSharpLogSlot
{
	const void			*pModuleBase;
	SourceSharpLogFn	pfnLog;
};

//-----------------------------------------------------------------------------
// State. ONE CoreCLR per process, refcounted across client.so + server.so, and
// since libmanaged_host.so is mapped once per process there is now exactly one
// of these to do the counting.
//-----------------------------------------------------------------------------
struct SourceSharpState
{
	int							nRefCount;
	ss_module_t					hHostFxr;
	hostfxr_handle				hContext;

	hostfxr_initialize_for_runtime_config_fn	pfnInit;
	hostfxr_get_runtime_delegate_fn				pfnGetDelegate;
	hostfxr_close_fn							pfnClose;

	load_assembly_and_get_function_pointer_fn	pfnLoadAssemblyAndGetFn;

	SourceSharpSideEntries		Server;
	SourceSharpSideEntries		Client;

	SourceSharpLogSlot			LogSlots[kSourceSharpMaxLogSlots];
	int							nLogSlots;
	int							nVerbosity;

	char						szModBinDir[1024];
	char						szLastError[1024];
};

// THE OWNER'S SANCTIONED EXCEPTION TO "no file-scope statics or globals in
// SourceSharp's C++", recorded here and not only in a table, because the
// question with the dullest answer is the one whose answer never gets written
// down. The ruling: *g_State is an explicit exception, because there can only
// ever be one instance of the managed runtime.* A process-wide singleton is the
// point of this file; it lives in the shared object whose whole purpose is to
// make that singleton genuinely single. docs/statics-audit/baseline.tsv carries
// the same reason where the checker will send the next person.
//
// It must NOT be zero-initialised by anything but its definition: Acquire's
// cold path memsets it, and doing that while another module holds a reference
// would silently drop that module's count. The refcount guard above the memset
// is what stops it.
static SourceSharpState g_State = { 0, 0, 0, 0, 0, 0, 0, { 0, 0, 0, 0, 0, 0 }, { 0, 0, 0, 0, 0, 0 }, { { 0, 0 }, { 0, 0 } }, 0, 1, "", "" };

void SourceSharp_SetLogger( SourceSharpLogFn pfnLog )
{
	const SourceSharpCaller caller = SS_CALLER();

	// Same module installing again replaces its own entry rather than growing
	// the stack -- a side that re-initialises must not consume the other's slot.
	for ( int i = 0; i < g_State.nLogSlots; i++ )
	{
		if ( g_State.LogSlots[i].pModuleBase == caller.pBase )
		{
			g_State.LogSlots[i].pfnLog = pfnLog;
			return;
		}
	}

	if ( g_State.nLogSlots < kSourceSharpMaxLogSlots )
	{
		g_State.LogSlots[g_State.nLogSlots].pModuleBase = caller.pBase;
		g_State.LogSlots[g_State.nLogSlots].pfnLog = pfnLog;
		g_State.nLogSlots++;
		return;
	}

	// A third side would be new architecture, not a new logger. Overwrite the
	// newest rather than dropping the line on the floor.
	g_State.LogSlots[kSourceSharpMaxLogSlots - 1].pModuleBase = caller.pBase;
	g_State.LogSlots[kSourceSharpMaxLogSlots - 1].pfnLog = pfnLog;
}

//-----------------------------------------------------------------------------
// Forgets the logger a module installed, so nothing calls into it once that
// module is going away. Silent: it runs from inside Release's logging path.
//-----------------------------------------------------------------------------
static void SS_ForgetLogger( const void *pModuleBase )
{
	for ( int i = 0; i < g_State.nLogSlots; i++ )
	{
		if ( g_State.LogSlots[i].pModuleBase != pModuleBase )
			continue;

		for ( int j = i; j + 1 < g_State.nLogSlots; j++ )
			g_State.LogSlots[j] = g_State.LogSlots[j + 1];

		g_State.nLogSlots--;
		g_State.LogSlots[g_State.nLogSlots].pModuleBase = NULL;
		g_State.LogSlots[g_State.nLogSlots].pfnLog = NULL;
		return;
	}
}

void SourceSharp_SetVerbosity( int nLevel )
{
	g_State.nVerbosity = nLevel;
}

//-----------------------------------------------------------------------------
// Routes through the newest installed logger (Source console) or stdout as a
// fallback. nLevel 0 always prints; higher levels are gated on verbosity.
//-----------------------------------------------------------------------------
static void SS_LogAt( int nLevel, const char *pFmt, ... )
{
	if ( nLevel > g_State.nVerbosity )
		return;

	char szBody[1024];
	va_list args;
	va_start( args, pFmt );
	vsnprintf( szBody, sizeof( szBody ), pFmt, args );
	va_end( args );

	char szLine[1100];
	snprintf( szLine, sizeof( szLine ), "[SourceSharp] %s\n", szBody );

	if ( g_State.nLogSlots > 0 && g_State.LogSlots[g_State.nLogSlots - 1].pfnLog )
		g_State.LogSlots[g_State.nLogSlots - 1].pfnLog( szLine );
	else
		fputs( szLine, stdout );
}

#define SS_LogErr( ... )  SS_LogAt( 0, __VA_ARGS__ )
#define SS_Log( ... )     SS_LogAt( 1, __VA_ARGS__ )
#define SS_LogV( ... )    SS_LogAt( 2, __VA_ARGS__ )

//-----------------------------------------------------------------------------
// Decodes the hostfxr status codes that matter for diagnosis.
//-----------------------------------------------------------------------------
static const char *HostFxrResultName( int rc )
{
	switch ( rc )
	{
	case 0:          return "Success";
	case 1:          return "Success_HostAlreadyInitialized";
	case 2:          return "Success_DifferentRuntimeProperties";
	case -2147450750: return "CoreHostIncompatibleConfig";
	case -2147450748: return "HostApiUnsupportedScenario";
	default:         return "unknown";
	}
}

static void SetError( const char *pFmt, ... )
{
	va_list args;
	va_start( args, pFmt );
	vsnprintf( g_State.szLastError, sizeof( g_State.szLastError ), pFmt, args );
	va_end( args );

	SS_LogErr( "ERROR: %s", g_State.szLastError );
}

const char *SourceSharp_LastError( void )
{
	return g_State.szLastError[0] ? g_State.szLastError : "no error";
}

int SourceSharp_RefCount( void )
{
	return g_State.nRefCount;
}

// NOTE: SourceSharp_LastError and SourceSharp_RefCount are deliberately silent —
// they are called FROM the logging paths, so tracing them would recurse.

//-----------------------------------------------------------------------------
// Finds the bundled runtime's libhostfxr by explicit path rather than using
// nethost's global discovery — players must not need a system .NET install.
//-----------------------------------------------------------------------------
static bool FindBundledHostFxr( const char *pModBinDir, char *pOut, size_t nOutSize )
{
	SS_Log( "  FindBundledHostFxr( %s )", pModBinDir ? pModBinDir : "(null)" );

#ifdef _WIN32
	const char *pLibName = "hostfxr.dll";
#else
	const char *pLibName = "libhostfxr.so";
#endif

	char szFxrDir[1024];
	snprintf( szFxrDir, sizeof( szFxrDir ), "%s" SS_PATHSEP "dotnet" SS_PATHSEP "host" SS_PATHSEP "fxr", pModBinDir );

	// A path that did not fit is not a path.
	//
	// The joins below are szFxrDir (up to 1023 bytes) plus a directory name
	// (up to NAME_MAX, 255) plus the library name, into pOut -- so they CAN
	// overrun, and snprintf's answer to that is to hand back a shortened
	// string that still looks like a filename. -Wformat-truncation said so:
	//
	//   sourcesharp.cpp:194: warning: '%s' directive output may be truncated
	//   writing up to 255 bytes into a region of size between 0 and 1023
	//
	// Returning a truncated path means the caller reports "dlopen failed:
	// <some prefix of a real path>", which reads as a broken or missing
	// runtime rather than as a checkout nested too deeply -- so the failure
	// names the wrong thing, which is the expensive kind. Both loops below
	// check what snprintf says it WOULD have written and skip the entry when
	// it does not fit, so pOut is only ever read on the true return, and the
	// caller's existing "no bundled libhostfxr under <dir>" is the message.

	// The version dir varies (10.0.11, ...). Take the first entry found rather
	// than hardcoding, so runtime servicing updates don't break the host.
#ifdef _WIN32
	char szSearch[1024];
	snprintf( szSearch, sizeof( szSearch ), "%s\\*", szFxrDir );
	WIN32_FIND_DATAA fd;
	HANDLE hFind = ::FindFirstFileA( szSearch, &fd );
	if ( hFind == INVALID_HANDLE_VALUE )
		return false;
	do
	{
		if ( fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY && fd.cFileName[0] != '.' )
		{
			const int nLen = snprintf( pOut, nOutSize, "%s\\%s\\%s", szFxrDir, fd.cFileName, pLibName );
			if ( nLen < 0 || (size_t)nLen >= nOutSize )
				continue;
			::FindClose( hFind );
			return true;
		}
	} while ( ::FindNextFileA( hFind, &fd ) );
	::FindClose( hFind );
	return false;
#else
	DIR *pDir = ::opendir( szFxrDir );
	if ( !pDir )
		return false;

	struct dirent *pEnt;
	while ( ( pEnt = ::readdir( pDir ) ) != NULL )
	{
		if ( pEnt->d_name[0] == '.' )
			continue;

		const int nLen = snprintf( pOut, nOutSize, "%s/%s/%s", szFxrDir, pEnt->d_name, pLibName );
		if ( nLen < 0 || (size_t)nLen >= nOutSize )
			continue;

		::closedir( pDir );
		return true;
	}
	::closedir( pDir );
	return false;
#endif
}

//-----------------------------------------------------------------------------
// Lets a debugger attach to this process, when asked.
//
// Linux refuses ptrace from anything but an ancestor when
// kernel.yama.ptrace_scope is 1, which it is by default on Ubuntu — so a
// debugger started from an editor cannot attach to a game started from a
// terminal. The alternative is relaxing that setting for every process on the
// machine, with sudo, until the next reboot.
//
// This asks for the narrowest version of the same thing: this process, this run,
// and only when the environment says a debugger is expected. A normal run never
// calls it.
//-----------------------------------------------------------------------------
static void AllowDebuggerAttach( void )
{
	const char *pWanted = getenv( "SOURCESHARP_DEBUG" );
	if ( !pWanted || !pWanted[0] || pWanted[0] == '0' )
		return;

#if defined( __linux__ )
	if ( prctl( PR_SET_PTRACER, PR_SET_PTRACER_ANY, 0, 0, 0 ) == 0 )
		SS_Log( "  SOURCESHARP_DEBUG: any debugger may now attach to pid %d", (int)getpid() );
	else
		SS_Log( "  SOURCESHARP_DEBUG: could not allow debugger attach (prctl failed)" );
#else
	SS_Log( "  SOURCESHARP_DEBUG: nothing to do on this platform" );
#endif
}

//-----------------------------------------------------------------------------
int SourceSharp_Acquire( const char *pModBinDir )
{
	const SourceSharpCaller caller = SS_CALLER();

	if ( g_State.nRefCount > 0 )
	{
		// Runtime already up — this is the listen-server case where the second
		// of client.so/server.so is loading. Exactly one CLR, two references.
		//
		// THIS BRANCH IS THE FIX'S OWN WITNESS. While the host was a static lib
		// in both modules it never ran, because the second module read its own
		// zeroed g_State. A run whose log shows two "runtime ready, refcount 1"
		// lines and no "refcount now 2" is the bug, back again.
		g_State.nRefCount++;
		SS_Log( "Acquire[%s]: runtime already running, refcount now %d (listen server)",
			caller.pName, g_State.nRefCount );
		return SOURCESHARP_OK;
	}

	SS_Log( "==== SourceSharp starting up ====" );
	SS_Log( "Acquire[%s]: mod bin dir = %s", caller.pName, pModBinDir ? pModBinDir : "(null)" );

	// Only ever reached at refcount 0, so no other module's reference is being
	// zeroed here. It does clear the log slots, so the caller's logger is put
	// back immediately below rather than being lost for the rest of this call.
	{
		const SourceSharpLogSlot savedSlots[kSourceSharpMaxLogSlots] =
			{ g_State.LogSlots[0], g_State.LogSlots[1] };
		const int nSavedSlots = g_State.nLogSlots;
		const int nSavedVerbosity = g_State.nVerbosity;

		memset( &g_State, 0, sizeof( g_State ) );

		g_State.LogSlots[0] = savedSlots[0];
		g_State.LogSlots[1] = savedSlots[1];
		g_State.nLogSlots = nSavedSlots;
		g_State.nVerbosity = nSavedVerbosity;
	}
	snprintf( g_State.szModBinDir, sizeof( g_State.szModBinDir ), "%s", pModBinDir );

	char szHostFxrPath[1024];
	if ( !FindBundledHostFxr( pModBinDir, szHostFxrPath, sizeof( szHostFxrPath ) ) )
	{
		SetError( "no bundled libhostfxr under %s/dotnet/host/fxr", pModBinDir );
		return SOURCESHARP_ERR_HOSTFXR_LOAD;
	}
	SS_Log( "  found hostfxr: %s", szHostFxrPath );

	AllowDebuggerAttach();

	// Point the framework resolver at the bundled runtime.
	char szDotnetRoot[1024];
	snprintf( szDotnetRoot, sizeof( szDotnetRoot ), "%s" SS_PATHSEP "dotnet", pModBinDir );
	ss_setenv( "DOTNET_ROOT", szDotnetRoot );
	SS_Log( "  DOTNET_ROOT = %s", szDotnetRoot );

	g_State.hHostFxr = ss_dlopen( szHostFxrPath );
	if ( !g_State.hHostFxr )
	{
		SetError( "dlopen failed: %s", szHostFxrPath );
		return SOURCESHARP_ERR_HOSTFXR_LOAD;
	}
	SS_Log( "  hostfxr loaded" );

	g_State.pfnInit = (hostfxr_initialize_for_runtime_config_fn)
		ss_dlsym( g_State.hHostFxr, "hostfxr_initialize_for_runtime_config" );
	g_State.pfnGetDelegate = (hostfxr_get_runtime_delegate_fn)
		ss_dlsym( g_State.hHostFxr, "hostfxr_get_runtime_delegate" );
	g_State.pfnClose = (hostfxr_close_fn)
		ss_dlsym( g_State.hHostFxr, "hostfxr_close" );

	if ( !g_State.pfnInit || !g_State.pfnGetDelegate || !g_State.pfnClose )
	{
		SetError( "libhostfxr missing required exports" );
		return SOURCESHARP_ERR_HOSTFXR_SYM;
	}
	SS_Log( "  hostfxr exports resolved" );

	char szRuntimeConfig[1024];
	snprintf( szRuntimeConfig, sizeof( szRuntimeConfig ),
		"%s" SS_PATHSEP "managed" SS_PATHSEP "SourceSharp.Bootstrap.runtimeconfig.json", pModBinDir );

	SS_Log( "  runtimeconfig: %s", szRuntimeConfig );

	int rc = g_State.pfnInit( SS_HostString( szRuntimeConfig ).c_str(), NULL, &g_State.hContext );
	SS_Log( "  hostfxr_initialize_for_runtime_config -> 0x%x (%s)", rc, HostFxrResultName( rc ) );

	// Success(0), Success_HostAlreadyInitialized(1) and
	// Success_DifferentRuntimeProperties(2) are all usable.
	if ( rc > 2 || rc < 0 || g_State.hContext == NULL )
	{
		SetError( "hostfxr_initialize_for_runtime_config failed (0x%x) for %s", rc, szRuntimeConfig );
		return SOURCESHARP_ERR_RUNTIME_INIT;
	}

	rc = g_State.pfnGetDelegate( g_State.hContext,
		hdt_load_assembly_and_get_function_pointer,
		(void **)&g_State.pfnLoadAssemblyAndGetFn );

	if ( rc != 0 || !g_State.pfnLoadAssemblyAndGetFn )
	{
		SetError( "hostfxr_get_runtime_delegate failed (0x%x)", rc );
		return SOURCESHARP_ERR_DELEGATE;
	}
	SS_Log( "  CLR up; load_assembly_and_get_function_pointer obtained" );

	// Resolve the managed bootstrap ONCE. Everything after this goes through
	// raw function pointers — never a hostfxr lookup on a hot path.
	char szBootstrapDll[1024];
	snprintf( szBootstrapDll, sizeof( szBootstrapDll ),
		"%s" SS_PATHSEP "managed" SS_PATHSEP "SourceSharp.Bootstrap.dll", pModBinDir );

	SS_Log( "  bootstrap: %s", szBootstrapDll );

	g_State.Server.pName = "server";
	g_State.Server.pSuffix = "Server";
	g_State.Server.pType = "SourceSharp.Bootstrap.ServerEntry, SourceSharp.Bootstrap";
	g_State.Client.pName = "client";
	g_State.Client.pSuffix = "Client";
	g_State.Client.pType = "SourceSharp.Bootstrap.ClientEntry, SourceSharp.Bootstrap";

	SourceSharpSideEntries *pSides[] = { &g_State.Server, &g_State.Client };
	for ( SourceSharpSideEntries *pSide : pSides )
	{
		struct { const char *pVerb; void **ppFn; } entries[] = {
			{ "Load",        (void **)&pSide->pfnLoad },
			{ "Unload",      (void **)&pSide->pfnUnload },
			// plan_managed_vtable_reload.md L5: the reload's hash check, run
			// before the running build is touched.
			{ "ReloadCheck", (void **)&pSide->pfnReloadCheck },
		};
		for ( const auto &entry : entries )
		{
			char szMethod[64];
			snprintf( szMethod, sizeof( szMethod ), "%s%s", entry.pVerb, pSide->pSuffix );
			rc = g_State.pfnLoadAssemblyAndGetFn( SS_HostString( szBootstrapDll ).c_str(),
			                                      SS_HostString( pSide->pType ).c_str(),
			                                      SS_HostString( szMethod ).c_str(),
			                                      UNMANAGEDCALLERSONLY_METHOD, NULL, entry.ppFn );
			if ( rc != 0 || !*entry.ppFn )
			{
				SetError( "failed to resolve %s.%s (0x%x) in %s", pSide->pType, szMethod, rc, szBootstrapDll );
				return SOURCESHARP_ERR_BOOTSTRAP;
			}
		}
		SS_Log( "  resolved the %s's Load, Unload and ReloadCheck", pSide->pName );
	}

	g_State.nRefCount = 1;
	SS_Log( "Acquire[%s]: runtime ready, refcount 1", caller.pName );
	return SOURCESHARP_OK;
}

//-----------------------------------------------------------------------------
// LAST SIDE OUT CLOSES THE RUNTIME, AND ONLY THE LAST -- IN EITHER ORDERING.
//
// This is a plain refcount over one shared counter, so it is symmetric: it does
// not matter which of client.so and server.so releases first, only that the
// count reaches zero once. The engine picks the shutdown order, not us, and
// with a single g_State both orders behave identically.
//
// A dedicated server is one side. It acquires once and releases once, so it
// reaches zero on its only release and closes the context. Nothing leaks and
// nothing closes early.
//
// This is NOT the ALC lifetime. cs_sv_reload tears the managed load context
// down and rebuilds it through SourceSharp_UnloadSide/LoadSide and never comes
// through here; conflating the two would make a reload close the process's CLR.
//-----------------------------------------------------------------------------
void SourceSharp_Release( void )
{
	const SourceSharpCaller caller = SS_CALLER();

	if ( g_State.nRefCount <= 0 )
	{
		SS_Log( "Release[%s]: nothing to release (refcount already 0)", caller.pName );
		return;
	}

	g_State.nRefCount--;
	SS_Log( "Release[%s]: refcount now %d", caller.pName, g_State.nRefCount );

	if ( g_State.nRefCount > 0 )
	{
		// Said its last words; now stop anything calling back into a module
		// that is going away. Host output falls back to the other side's
		// logger, which is still live.
		SS_ForgetLogger( caller.pBase );
		return;
	}

	SS_Log( "Release[%s]: last reference dropped, closing hostfxr context", caller.pName );

	if ( g_State.hContext && g_State.pfnClose )
		g_State.pfnClose( g_State.hContext );

	g_State.hContext = NULL;
	memset( &g_State.Server, 0, sizeof( g_State.Server ) );
	memset( &g_State.Client, 0, sizeof( g_State.Client ) );
	g_State.pfnLoadAssemblyAndGetFn = NULL;

	SS_ForgetLogger( caller.pBase );
}

//-----------------------------------------------------------------------------
// plan_engineapi_vtables.md step A3. LoadSide with the root beside the struct.
// There is no struct (the server's since E-last, the client's since
// plan_client_vtables CE-last): every call into managed code is a managed-built
// area acquired through the managed root this hands back. The shim root's
// slot count is checked on the managed side.
//-----------------------------------------------------------------------------
static int LoadSide( const SourceSharpSideEntries &side,
                     void *pShimRoot, int32_t nShimRootEntries, void **ppManagedRoot )
{
	if ( g_State.nRefCount <= 0 || !side.pfnLoad )
	{
		SetError( "SourceSharp_Load%s before Acquire", side.pName );
		return SOURCESHARP_ERR_NOT_INITIALIZED;
	}

	if ( !pShimRoot || nShimRootEntries <= 0 )
	{
		SetError( "SourceSharp_Load %s without a shim root (%p, %d)", side.pName, pShimRoot, nShimRootEntries );
		return SOURCESHARP_ERR_BOOTSTRAP;
	}

	SS_Log( "LoadSideV2(%s): calling managed bootstrap, root %p (%d slots)",
		side.pName, pShimRoot, nShimRootEntries );

	if ( ppManagedRoot )
		*ppManagedRoot = NULL;

	int rc = side.pfnLoad( pShimRoot, nShimRootEntries, ppManagedRoot );
	if ( rc != 0 )
	{
		SetError( "managed %s load returned %d", side.pName, rc );
		return SOURCESHARP_ERR_BOOTSTRAP;
	}

	SS_Log( "LoadSideV2(%s): ok", side.pName );
	return SOURCESHARP_OK;
}

static int ReloadCheck( const SourceSharpSideEntries &side, char *pWhy, int32_t nWhy )
{
	if ( g_State.nRefCount <= 0 || !side.pfnReloadCheck )
	{
		snprintf( pWhy, nWhy, "the managed runtime is not up" );
		return 1;
	}

	return side.pfnReloadCheck( pWhy, nWhy );
}

static void UnloadSide( const SourceSharpSideEntries &side )
{
	if ( g_State.nRefCount <= 0 || !side.pfnUnload )
		return;

	SS_Log( "UnloadSide(%s): unloading managed ALC", side.pName );
	side.pfnUnload();
}

int SourceSharp_LoadServer( void *pShimRoot, int32_t nShimRootEntries, void **ppManagedRoot )
{ return LoadSide( g_State.Server, pShimRoot, nShimRootEntries, ppManagedRoot ); }
int SourceSharp_LoadClient( void *pShimRoot, int32_t nShimRootEntries, void **ppManagedRoot )
{ return LoadSide( g_State.Client, pShimRoot, nShimRootEntries, ppManagedRoot ); }

int SourceSharp_ReloadCheckServer( char *pWhy, int32_t nWhy ) { return ReloadCheck( g_State.Server, pWhy, nWhy ); }
int SourceSharp_ReloadCheckClient( char *pWhy, int32_t nWhy ) { return ReloadCheck( g_State.Client, pWhy, nWhy ); }

void SourceSharp_UnloadServer( void ) { UnloadSide( g_State.Server ); }
void SourceSharp_UnloadClient( void ) { UnloadSide( g_State.Client ); }
