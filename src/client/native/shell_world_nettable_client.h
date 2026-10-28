//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// worldspawn's ClientClass, declared from C#. The two sides share the
// declaration (SourceSharp.Shared.WorldNetState) and agree on word indices,
// not offsets: each resolves the props against its own class's sizeof.

#pragma once

class ClientClass;
class IClientNetworkable;

// Where the world's blob sits inside a C_World, and how long it is. The offset
// is defined beside the class in world_adapter_client.cpp; the length is C#'s
// and zero until WorldNetDeclare has run.
int SourceSharp_WorldBlobOffset();
int SourceSharp_WorldBlobBytes();

// C# declares worldspawn's blob size; this builds the RecvTable and ClientClass
// around it. Runs before the C_World singleton is allocated, so its operator new
// knows the blob length. Returns the payload word count, or -1 on refusal.
int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes );

// The class C_World::GetClientClass() hands the engine. NULL until C# has
// declared, which drops the world's updates.
ClientClass *SourceSharp_WorldClientClass();

// The map's detail-sprite material, read from the blob. NULL when there is no
// world, no declaration, or the map left `detailmaterial` empty; the caller
// then uses its own default.
const char *SourceSharp_WorldDetailSpriteMaterial();

// world_adapter_client.cpp's factory, named here because the table is built here.
IClientNetworkable *SourceSharp_ClientWorldFactory( int entnum, int serialNum );
