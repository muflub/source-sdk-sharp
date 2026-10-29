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

// Where the world's blob sits inside a CWorld. Defined in world_adapter.cpp, which can
// see sizeof( CWorld ). Its length is the world's net table's (CWorldNetTable).
int SourceSharp_WorldBlobOffset();
