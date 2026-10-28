//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's pooled-string functions, defined in gamestringpool.cpp, declared
// on public/ headers alone.

#pragma once

#include "string_t.h"

class CServerSystem;

// The level-lifetime pooled copy of pszValue (NULL_STRING for NULL or "").
string_t AllocPooledString( const char *pszValue );

// The pool's server system: it frees every pooled string at level shutdown.
CServerSystem *GameStringSystem();
