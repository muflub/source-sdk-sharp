//========= SourceSharp — gamestringpool.cpp's server-side read access ========//
//
// What gamestringpool_debug.cpp's dumpgamestringtable needs from the pool, and
// nothing more. The pool itself (g_GameStringPool) stays file-static in
// gamestringpool.cpp; these two functions are the only way in from outside it.
//
// Server only: the debug file is compiled into server.so alone, and the client
// half of gamestringpool.cpp defines neither function.
//
//=============================================================================//

#ifndef SOURCESHARP_GAMESTRINGPOOL_INTERNAL_H
#define SOURCESHARP_GAMESTRINGPOOL_INTERNAL_H
#ifdef _WIN32
#pragma once
#endif

typedef void ( *GameStringPoolVisitFn )( const char *pszString, void *pContext );

// Calls pfnVisit once per pooled string, in the pool's (case-insensitive) order.
void GameStringPool_Visit( GameStringPoolVisitFn pfnVisit, void *pContext );

// How many strings the pool holds.
int GameStringPool_Count( void );

#endif // SOURCESHARP_GAMESTRINGPOOL_INTERNAL_H
