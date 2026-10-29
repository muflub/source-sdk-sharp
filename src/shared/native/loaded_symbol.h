// Finds a symbol in a library the process already loaded, without loading anything.
// libtier0.so has no DT_SONAME, so the match is on the loaded object's basename.

#pragma once

namespace ss
{

// The address of pszSymbol as the loaded object whose file is pszFile (e.g.
// "libtier0.so") resolves it, or NULL when no such object is loaded or it has
// no such symbol. Never loads a library.
void *FindLoadedSymbol( const char *pszFile, const char *pszSymbol );

// FindLoadedSymbol for a symbol the caller cannot work without: a missing object or
// symbol aborts, naming both. Defined in tier_bindings_client.cpp.
void *RequireLoadedSymbol( const char *pszFile, const char *pszSymbol );

} // namespace ss
