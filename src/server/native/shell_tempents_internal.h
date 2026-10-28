//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Read-only accessors over shell_tempents.cpp's built tables, for its debug file.
#pragma once

class SendTable;
class ServerClass;

// How many tables were built, and whether the registration window has shut.
int  SourceSharpTe_BuiltCount();
bool SourceSharpTe_RegistrationClosed();

// The table and the ServerClass built for handle i. NULL when nothing was
// built, when the plan asked for no ServerClass, or when i is out of range.
SendTable   *SourceSharpTe_BuiltTable( int i );
ServerClass *SourceSharpTe_BuiltClass( int i );
