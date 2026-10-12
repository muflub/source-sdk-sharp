//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_tempents.cpp's state, for its debug file =====//
//
// Read-only accessors over what shell_tempents.cpp owns, for
// shell_tempents_debug.cpp's cs_sv_te_dump. The registry and the built tables
// stay file-static there; these are functions, not globals, so ownership does
// not move and nothing outside the two files is offered a way to write them.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_TEMPENTS_INTERNAL_H
#define SOURCESHARP_SHELL_TEMPENTS_INTERNAL_H
#ifdef _WIN32
#pragma once
#endif

class CSourceSharpTeRegistry;
class SendTable;
class ServerClass;

// Every description C# registered, in handle order.
const CSourceSharpTeRegistry &SourceSharpTe_Registry();

// The table and the ServerClass built for registry handle i. NULL when nothing
// was built, when the description asked for no ServerClass, or when i is out of
// range (0 .. SOURCESHARP_TE_MAX_CLASSES - 1).
SendTable   *SourceSharpTe_BuiltTable( int i );
ServerClass *SourceSharpTe_BuiltClass( int i );

#endif // SOURCESHARP_SHELL_TEMPENTS_INTERNAL_H
