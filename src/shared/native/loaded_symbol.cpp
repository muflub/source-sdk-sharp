// SS_REASON d  a layout the engine reads at fixed offsets

#include "loaded_symbol.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined( _WIN32 )
	#ifndef WIN32_LEAN_AND_MEAN
		#define WIN32_LEAN_AND_MEAN
	#endif
	#include <windows.h>
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

// The path the dynamic linker has for a loaded file of that base name, or NULL.
const char *LoadedPath( const char *pszFile )
{
	FindFile_t find = { pszFile, NULL };
	dl_iterate_phdr( FindLoadedPath, &find );
	return find.pszPath;
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
