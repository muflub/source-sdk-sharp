//========= SourceSharp — a symbol from a library the process already loaded ==//
//
// server.so links no tier0 (plan_server_novalve.md P4), but the engine loaded
// libtier0.so before it, and a few things only that copy can do: free a block
// its allocator made (engine_heap.h), write a line where console.log will see
// it (tier0_spew_port.cpp). FindLoadedSymbol finds such a symbol in the loaded
// object whose FILE is pszFile, without loading anything.
//
// The game's libtier0.so has no DT_SONAME, so the loader knows it only by the
// name it was opened under -- a bare name through engine.so's DT_NEEDED, or a
// path. The loaded-object list names the file whichever way it came in, so the
// match is on the basename. dlsym on a handle searches that object and then its
// dependencies: the scope the module's own references were bound in.
//
//=============================================================================//

#ifndef SOURCESHARP_LOADED_SYMBOL_H
#define SOURCESHARP_LOADED_SYMBOL_H
#ifdef _WIN32
#pragma once
#endif

namespace ss
{

// The address of pszSymbol as the loaded object whose file is pszFile (e.g.
// "libtier0.so") resolves it, or NULL when no such object is loaded or it has
// no such symbol. Never loads a library.
void *FindLoadedSymbol( const char *pszFile, const char *pszSymbol );

// FindLoadedSymbol for a symbol the caller cannot work without (client.so's
// tier0 and vstdlib bindings, tier_bindings_client.cpp, which defines it): no such object, or no
// such symbol in it, is a broken process, and this stops it, loudly, naming
// both, rather than hand back NULL for a later call to jump through.
void *RequireLoadedSymbol( const char *pszFile, const char *pszSymbol );

} // namespace ss

#endif // SOURCESHARP_LOADED_SYMBOL_H
