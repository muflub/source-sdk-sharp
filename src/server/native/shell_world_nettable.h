//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// worldspawn's ServerClass, built at DLL init from the C# declaration
// (SourceSharp.Shared.WorldNetState). CWorld over-allocates for the blob the way
// CStandaloneEntity does.

#pragma once



class ServerClass;

// Where the world's blob sits inside a CWorld, and its length. The offset is
// defined in world_adapter.cpp, which can see sizeof( CWorld ). The length comes
// from C# and is zero until it has been declared.
int SourceSharp_WorldBlobOffset();
int SourceSharp_WorldBlobBytes();

// C# declares worldspawn's blob size and this builds the ServerClass around it.
// Must run inside DLL init, since the engine walks the server class list once.
// Returns the payload word count, or -1 for a size no payload can name or a
// second declaration that disagrees with the first.
int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes );

// What CWorld::GetServerClass() answers. NULL until C# has declared, and CWorld
// then falls back to its base's.
ServerClass *SourceSharp_WorldServerClass();

// Whether pClass is the world's managed-declared ServerClass, by identity.
bool SourceSharp_IsWorldServerClass( const ServerClass *pClass );
