//========= SourceSharp — the platform calls our native code makes ===========//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// Our own native code (the shims, the host, the instruments) asked the
// platform for a handful of things that have no one spelling across Linux,
// Windows and macOS: which module holds an address, a stack of return
// addresses, the protection of a page and a way to change it, a page's size,
// the process id, the process's own command line. Every one of those is
// spelled here, once, with the Linux spelling unchanged from what each caller
// used to inline, so the Linux build does exactly what it did and the other
// two do the same thing their own way.
//
// System headers only, on purpose: vtable_slot_patch.h includes this and is
// tested on its own, free of Source headers (test/vtable_slot_patch_test.cpp).
//=============================================================================//
#pragma once

#include <stddef.h>
#include <stdlib.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#ifdef _WIN32
	#ifndef WIN32_LEAN_AND_MEAN
		#define WIN32_LEAN_AND_MEAN
	#endif
	#ifndef NOMINMAX
		#define NOMINMAX
	#endif
	#include <windows.h>
	#include <intrin.h>
	#include <process.h>
	#include <direct.h>
	// <windows.h> turns these into macros for their ...A/...W functions, and
	// the engine's interfaces and ours have methods of the same names
	// (IServerNetworkable::GetClassName, SendTable::GetProp,
	// IGameEventManager2::CreateEvent, ...): left defined, every later
	// declaration and call of one is renamed and stops matching. This header
	// is included by headers, so it must not leak them. Win32 code here and
	// in the .cpp files names the explicit ...A functions it calls.
	#undef GetClassName
	#undef GetProp
	#undef SetProp
	#undef GetObject
	#undef CreateEvent
	#undef GetCurrentTime
	#undef PostMessage
	#undef SendMessage
	#undef CreateFont
	#undef DrawText
	#undef GetMessage
	#undef CopyFile
	#undef DeleteFile
#else
	#include <dlfcn.h>
	#include <execinfo.h>
	#include <sys/mman.h>
	#include <unistd.h>
	#if defined( __APPLE__ )
		#include <crt_externs.h>
		#include <mach/mach.h>
		#include <mach/mach_vm.h>
	#endif
#endif

//-----------------------------------------------------------------------------
// The caller's return address, as gcc spells it; MSVC's intrinsic is
// _ReturnAddress. Level 0 only on either (the level-1 form is gcc's and
// unreliable even there).
//-----------------------------------------------------------------------------
#ifdef _MSC_VER
	#define SS_RETURN_ADDRESS() _ReturnAddress()
#else
	#define SS_RETURN_ADDRESS() __builtin_return_address( 0 )
#endif

//-----------------------------------------------------------------------------
// strcasecmp is POSIX; the CRT calls it _stricmp.
//-----------------------------------------------------------------------------
#ifdef _WIN32
	#define ss_strcasecmp _stricmp
#else
	#include <strings.h>
	#define ss_strcasecmp strcasecmp
#endif

//-----------------------------------------------------------------------------
// The module that holds an address: its load base, its file name (the path
// the loader has, or "" when the platform cannot say) and the nearest
// exported symbol at or before the address (dladdr's dli_sname, a pointer
// into the loader's own string; NULL when unknown, and always on Windows,
// which has no such lookup without the debug help library). False when no
// loaded module holds the address. dladdr on the GNU platforms;
// GetModuleHandleEx(FROM_ADDRESS) and GetModuleFileName on Windows.
//-----------------------------------------------------------------------------
struct SSModuleInfo
{
	const void *pBase;
	const char *pszSymbol;
	char szPath[ 1024 ];
};

inline bool SS_ModuleOfAddress( const void *pAddress, SSModuleInfo *pInfo )
{
	if ( !pInfo )
		return false;
	pInfo->pBase = NULL;
	pInfo->pszSymbol = NULL;
	pInfo->szPath[ 0 ] = '\0';
	if ( !pAddress )
		return false;
#ifdef _WIN32
	HMODULE hModule = NULL;
	if ( !::GetModuleHandleExA( GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
	                            (LPCSTR)pAddress, &hModule ) || !hModule )
		return false;
	pInfo->pBase = (const void *)hModule;
	if ( ::GetModuleFileNameA( hModule, pInfo->szPath, (DWORD)sizeof( pInfo->szPath ) ) == 0 )
		pInfo->szPath[ 0 ] = '\0';
	return true;
#else
	Dl_info info;
	if ( !dladdr( pAddress, &info ) || !info.dli_fbase )
		return false;
	pInfo->pBase = info.dli_fbase;
	pInfo->pszSymbol = info.dli_sname;
	if ( info.dli_fname )
	{
		strncpy( pInfo->szPath, info.dli_fname, sizeof( pInfo->szPath ) - 1 );
		pInfo->szPath[ sizeof( pInfo->szPath ) - 1 ] = '\0';
	}
	return true;
#endif
}

// The file name part of a module path: after the last separator (either kind
// on Windows, where the loader may hand back both).
inline const char *SS_ModuleBaseName( const char *pszPath )
{
	const char *pszName = strrchr( pszPath, '/' );
#ifdef _WIN32
	const char *pszBack = strrchr( pszPath, '\\' );
	if ( pszBack && ( !pszName || pszBack > pszName ) )
		pszName = pszBack;
#endif
	return pszName ? pszName + 1 : pszPath;
}

//-----------------------------------------------------------------------------
// Up to nMax return addresses of the calling thread's stack, innermost first,
// into pFrames; returns how many. glibc's and libSystem's backtrace(); on
// Windows, CaptureStackBackTrace.
//-----------------------------------------------------------------------------
inline int SS_Backtrace( void **pFrames, int nMax )
{
	if ( !pFrames || nMax <= 0 )
		return 0;
#ifdef _WIN32
	return (int)::CaptureStackBackTrace( 0, (DWORD)nMax, pFrames, NULL );
#else
	return backtrace( pFrames, nMax );
#endif
}

//-----------------------------------------------------------------------------
// Pages. The protection bits are mprotect's (read 1, write 2, execute 4) on
// every platform, so a value read back here can be passed back in unchanged;
// Windows' PAGE_* constants are translated at the edge.
//-----------------------------------------------------------------------------
enum
{
	SS_PAGE_READ = 1,
	SS_PAGE_WRITE = 2,
	SS_PAGE_EXEC = 4,
};

inline size_t SS_PageSize()
{
#ifdef _WIN32
	SYSTEM_INFO si;
	::GetSystemInfo( &si );
	return (size_t)si.dwPageSize;
#else
	const long n = sysconf( _SC_PAGESIZE );
	return n > 0 ? (size_t)n : 4096;
#endif
}

#ifdef _WIN32
inline int SS_PageBitsFromWindows( DWORD dwProtect )
{
	switch ( dwProtect & 0xFF )
	{
	case PAGE_READONLY: return SS_PAGE_READ;
	case PAGE_READWRITE: case PAGE_WRITECOPY: return SS_PAGE_READ | SS_PAGE_WRITE;
	case PAGE_EXECUTE: return SS_PAGE_EXEC;
	case PAGE_EXECUTE_READ: return SS_PAGE_READ | SS_PAGE_EXEC;
	case PAGE_EXECUTE_READWRITE: case PAGE_EXECUTE_WRITECOPY: return SS_PAGE_READ | SS_PAGE_WRITE | SS_PAGE_EXEC;
	default: return 0;
	}
}

inline DWORD SS_PageBitsToWindows( int nBits )
{
	const bool bExec = ( nBits & SS_PAGE_EXEC ) != 0;
	if ( nBits & SS_PAGE_WRITE )
		return bExec ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE;
	if ( nBits & SS_PAGE_READ )
		return bExec ? PAGE_EXECUTE_READ : PAGE_READONLY;
	return bExec ? PAGE_EXECUTE : PAGE_NOACCESS;
}
#endif

// The protection of the mapping that holds pAddress, or -1 when no mapping
// does. Linux reads /proc/self/maps (the kernel's own answer, and what this
// code always read); macOS asks Mach for the region; Windows, VirtualQuery.
inline int SS_PageProtection( const void *pAddress )
{
#if defined( _WIN32 )
	MEMORY_BASIC_INFORMATION mbi;
	if ( ::VirtualQuery( pAddress, &mbi, sizeof( mbi ) ) == 0 || mbi.State != MEM_COMMIT )
		return -1;
	return SS_PageBitsFromWindows( mbi.Protect );
#elif defined( __APPLE__ )
	mach_vm_address_t address = (mach_vm_address_t)(uintptr_t)pAddress;
	mach_vm_size_t size = 0;
	vm_region_basic_info_data_64_t info;
	mach_msg_type_number_t count = VM_REGION_BASIC_INFO_COUNT_64;
	mach_port_t object = MACH_PORT_NULL;
	if ( mach_vm_region( mach_task_self(), &address, &size, VM_REGION_BASIC_INFO_64,
	                     (vm_region_info_t)&info, &count, &object ) != KERN_SUCCESS )
		return -1;
	// The region found starts at or after the address; after means nothing
	// is mapped at the address itself.
	if ( address > (mach_vm_address_t)(uintptr_t)pAddress )
		return -1;
	int nProtection = 0;
	if ( info.protection & VM_PROT_READ ) nProtection |= SS_PAGE_READ;
	if ( info.protection & VM_PROT_WRITE ) nProtection |= SS_PAGE_WRITE;
	if ( info.protection & VM_PROT_EXECUTE ) nProtection |= SS_PAGE_EXEC;
	return nProtection;
#else
	FILE *pMaps = fopen( "/proc/self/maps", "r" );
	if ( !pMaps )
		return -1;
	const uintptr_t address = (uintptr_t)pAddress;
	int nProtection = -1;
	char szLine[ 512 ];
	while ( fgets( szLine, sizeof( szLine ), pMaps ) )
	{
		unsigned long lo = 0, hi = 0;
		char szPerms[ 8 ] = { 0 };
		if ( sscanf( szLine, "%lx-%lx %7s", &lo, &hi, szPerms ) != 3 )
			continue;
		if ( address < lo || address >= hi )
			continue;
		nProtection = 0;
		if ( szPerms[0] == 'r' ) nProtection |= SS_PAGE_READ;
		if ( szPerms[1] == 'w' ) nProtection |= SS_PAGE_WRITE;
		if ( szPerms[2] == 'x' ) nProtection |= SS_PAGE_EXEC;
		break;
	}
	fclose( pMaps );
	return nProtection;
#endif
}

// Sets the protection of the page holding pAddress. True on success.
inline bool SS_PageProtect( const void *pAddress, int nProtection )
{
	const size_t nPage = SS_PageSize();
	const uintptr_t page = (uintptr_t)pAddress & ~(uintptr_t)( nPage - 1 );
#ifdef _WIN32
	DWORD dwOld = 0;
	return ::VirtualProtect( (LPVOID)page, nPage, SS_PageBitsToWindows( nProtection ), &dwOld ) != 0;
#else
	int nProt = 0;
	if ( nProtection & SS_PAGE_READ ) nProt |= PROT_READ;
	if ( nProtection & SS_PAGE_WRITE ) nProt |= PROT_WRITE;
	if ( nProtection & SS_PAGE_EXEC ) nProt |= PROT_EXEC;
	return mprotect( (void *)page, nPage, nProt ) == 0;
#endif
}

//-----------------------------------------------------------------------------
// The process id, and the working directory (into a buffer; NULL when it
// does not fit).
//-----------------------------------------------------------------------------
inline int SS_GetPid()
{
#ifdef _WIN32
	return (int)::GetCurrentProcessId();
#else
	return (int)getpid();
#endif
}

// setenv(name, value, overwrite) and unsetenv. The CRT's environment on
// Windows (_putenv_s, which getenv reads), where a variable set to the
// empty string is removed: that is how unsetenv is spelled there.
inline int SS_SetEnv( const char *pszName, const char *pszValue, int bOverwrite )
{
#ifdef _WIN32
	if ( !bOverwrite && getenv( pszName ) )
		return 0;
	return _putenv_s( pszName, pszValue ) == 0 ? 0 : -1;
#else
	return setenv( pszName, pszValue, bOverwrite );
#endif
}

inline int SS_UnsetEnv( const char *pszName )
{
#ifdef _WIN32
	return _putenv_s( pszName, "" ) == 0 ? 0 : -1;
#else
	return unsetenv( pszName );
#endif
}

// SS_BIND_IMPORT(name): this module defines a function the engine's own DLL
// headers declare extern "C" __declspec(dllimport) (tier0's spew and thread
// entry points, vstdlib's random stream), and every call of it in this
// module, which the compiler made through the import pointer __imp_<name>,
// is to reach that definition. On Linux the definitions are hidden and the
// module's calls bind to them by themselves. On Windows the import pointer is
// defined here, so the linker never takes it from tier0.lib or vstdlib.lib.
// After the definition, at file scope, in a file that also disables C4273
// (a definition of a dllimport-declared function). C linkage only: an
// overload with C++ linkage has a mangled import name and binds the way the
// linker binds it (to the DLL's export, or to the local definition with
// LNK4217 when nothing imports it).
#ifdef _WIN32
	#define SS_BIND_IMPORT( name ) extern "C" const void *const __imp_##name = (const void *)&name;
	// The same for an overloaded name: Type is the C-linkage overload's
	// function-pointer type, which picks it.
	#define SS_BIND_IMPORT_AS( name, Type ) extern "C" const void *const __imp_##name = (const void *)static_cast< Type >( &name );
#else
	#define SS_BIND_IMPORT( name )
	#define SS_BIND_IMPORT_AS( name, Type )
#endif

inline char *SS_GetCwd( char *pOut, size_t nOut )
{
#ifdef _WIN32
	return _getcwd( pOut, (int)nOut );
#else
	return getcwd( pOut, nOut );
#endif
}
