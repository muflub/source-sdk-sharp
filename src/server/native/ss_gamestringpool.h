//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's pooled-string accessors ===============//
//
// plan_server_novalve.md P5. gamestringpool.cpp (src/shared/native) defines
// two functions on the server: AllocPooledString and GameStringSystem. This
// header declares exactly those two, on public/ headers alone, for the
// translation units built without game/shared/gamestringpool.h. The names stay
// global because the TUs still on that header call the same definitions.
//
// game/shared/gamestringpool.h also declares FindPooledString,
// RemovePooledString, AllocPooledString_StaticConstantStringPointer and
// PurgeDeferredPooledStrings; server.so defines none of them, so they are not
// here.
//=============================================================================//

#pragma once

#include "string_t.h"

class CServerSystem;

// The level-lifetime pooled copy of pszValue (NULL_STRING for NULL or "").
string_t AllocPooledString( const char *pszValue );

// The pool's server system: it frees every pooled string at level shutdown.
CServerSystem *GameStringSystem();
