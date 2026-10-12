//========= SourceSharp — loaded_symbol.h's lookup ============================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// Native for engine_heap.h (d), which must free through the allocator the
// engine's vectors were grown with, and for tier0_spew_port (x).
//
// Three spellings of "a symbol in a library the process already holds",
// none of which loads anything: Linux walks the loaded objects
// (dl_iterate_phdr) for the file name and reopens it RTLD_NOLOAD; macOS asks
// dyld for its image list; Windows asks for the module handle by name, which
// is a lookup, not a load.

#include "loaded_symbol.h"

#include <string.h>

#if defined( _WIN32 )
	#ifndef WIN32_LEAN_AND_MEAN
		#define WIN32_LEAN_AND_MEAN
	#endif
	#include <windows.h>
#elif defined( __APPLE__ )
	#include <dlfcn.h>
	#include <mach-o/dyld.h>
#else
	#include <dlfcn.h>
	#include <link.h>
#endif

namespace ss
{

#if !defined( _WIN32 )
namespace
{
const char *BaseName( const char *pszName )
{
	const char *pszBase = strrchr( pszName, '/' );
	return pszBase ? pszBase + 1 : pszName;
}

#if !defined( __APPLE__ )
struct FindFile_t
{
	const char *pszFile;
	const char *pszPath;
};

int FindLoadedPath( struct dl_phdr_info *pInfo, size_t, void *pv )
{
	FindFile_t *pFind = (FindFile_t *)pv;
	const char *pszName = pInfo->dlpi_name;
	if ( !pszName || !*pszName )
		return 0;

	if ( strcmp( BaseName( pszName ), pFind->pszFile ) != 0 )
		return 0;

	pFind->pszPath = pszName;
	return 1;
}
#endif

// The path dyld or the dynamic linker has for a loaded file of that base
// name, or NULL.
const char *LoadedPath( const char *pszFile )
{
#if defined( __APPLE__ )
	const uint32_t nImages = _dyld_image_count();
	for ( uint32_t i = 0; i < nImages; ++i )
	{
		const char *pszName = _dyld_get_image_name( i );
		if ( pszName && strcmp( BaseName( pszName ), pszFile ) == 0 )
			return pszName;
	}
	return NULL;
#else
	FindFile_t find = { pszFile, NULL };
	dl_iterate_phdr( FindLoadedPath, &find );
	return find.pszPath;
#endif
}
}
#endif

void *FindLoadedSymbol( const char *pszFile, const char *pszSymbol )
{
#if defined( _WIN32 )
	HMODULE hModule = ::GetModuleHandleA( pszFile );
	if ( !hModule )
		return NULL;
	return (void *)::GetProcAddress( hModule, pszSymbol );
#else
	const char *pszPath = LoadedPath( pszFile );
	if ( !pszPath )
		return NULL;

	void *hModule = dlopen( pszPath, RTLD_NOW | RTLD_NOLOAD );
	if ( !hModule )
		return NULL;

	void *pSymbol = dlsym( hModule, pszSymbol );

	// NOLOAD took a reference to a library something else keeps loaded.
	dlclose( hModule );
	return pSymbol;
#endif
}

} // namespace ss
