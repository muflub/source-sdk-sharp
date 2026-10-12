//========= SourceSharp — loaded_symbol.h's lookup ============================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// Native for engine_heap.h (d), which must free through the allocator the
// engine's vectors were grown with, and for tier0_spew_port (x).

#include "loaded_symbol.h"

#include <dlfcn.h>
#include <link.h>
#include <string.h>

namespace ss
{

namespace
{
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

	const char *pszBase = strrchr( pszName, '/' );
	pszBase = pszBase ? pszBase + 1 : pszName;
	if ( strcmp( pszBase, pFind->pszFile ) != 0 )
		return 0;

	pFind->pszPath = pszName;
	return 1;
}
}

void *FindLoadedSymbol( const char *pszFile, const char *pszSymbol )
{
	FindFile_t find = { pszFile, NULL };
	dl_iterate_phdr( FindLoadedPath, &find );
	if ( !find.pszPath )
		return NULL;

	void *hModule = dlopen( find.pszPath, RTLD_NOW | RTLD_NOLOAD );
	if ( !hModule )
		return NULL;

	void *pSymbol = dlsym( hModule, pszSymbol );

	// NOLOAD took a reference to a library something else keeps loaded.
	dlclose( hModule );
	return pSymbol;
}

} // namespace ss
