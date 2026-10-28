// Read access to gamestringpool.cpp's pool for dumpgamestringtable. Server only.

#pragma once

typedef void ( *GameStringPoolVisitFn )( const char *pszString, void *pContext );

// Calls pfnVisit once per pooled string, in the pool's (case-insensitive) order.
void GameStringPool_Visit( GameStringPoolVisitFn pfnVisit, void *pContext );

// How many strings the pool holds.
int GameStringPool_Count( void );
