//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_tempents.cpp's state, for its debug file =====//
//
// Read-only accessors over what shell_tempents.cpp owns, for
// shell_tempents_debug.cpp's cs_sv_te_dump. The built tables stay file-static
// there; these are functions, not globals, so ownership does
// not move and nothing outside the two files is offered a way to write them.
//
//=============================================================================//
#pragma once

class SendTable;
class ServerClass;

// How many tables were built, and whether the registration window has shut.
int  SourceSharpTe_BuiltCount( void );
bool SourceSharpTe_RegistrationClosed( void );

// The table and the ServerClass built for handle i. NULL when nothing was
// built, when the plan asked for no ServerClass, or when i is out of range.
SendTable   *SourceSharpTe_BuiltTable( int i );
ServerClass *SourceSharpTe_BuiltClass( int i );
