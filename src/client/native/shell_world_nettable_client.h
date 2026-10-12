//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================//
//
// worldspawn's ClientClass, DECLARED FROM C# (plan_gamedll_after.md Part B2).
//
// The client's half of shell_world_nettable.h, and it is deliberately the same
// three questions: where the blob sits inside the shim, how long C# said it is,
// and which class the engine gets for it.
//
// WHAT THE TWO SIDES SHARE IS THE DECLARATION AND NOTHING ELSE.
// SourceSharp.Shared.WorldNetState is one type both DLLs compile against and
// neither owns; world_net_sync.cpp is one body both DLLs compile; and the field
// offsets both sides use come from that one declaration through
// NetStateFieldOffset. What the two sides do NOT share is the blob BASE: a
// CWorld and a C_World are unrelated classes with unrelated layouts, so each
// resolves its props against its own sizeof. That is the same split the payload
// props have always had — what the two sides agree on is the word index, never
// the offset — and it is why SourceSharp_WorldBlobOffset is defined twice, once
// per side, beside the class it measures.
//
//=============================================================================//

#ifndef SHELL_WORLD_NETTABLE_CLIENT_H
#define SHELL_WORLD_NETTABLE_CLIENT_H
#ifdef _WIN32
#pragma once
#endif

#include "basetypes.h"

class ClientClass;
class IClientNetworkable;

//-----------------------------------------------------------------------------
// Where the world's blob sits inside a C_World, and how long it is.
//
// The OFFSET is defined in world_adapter_client.cpp, beside the class whose
// sizeof it is.
// The LENGTH is C#'s and is zero until WorldNetDeclare has carried it across.
//-----------------------------------------------------------------------------
int SourceSharp_WorldBlobOffset();
int SourceSharp_WorldBlobBytes();

//-----------------------------------------------------------------------------
// ABI (engine api): C# declares worldspawn's blob size and this builds the
// RecvTable and the ClientClass around it.
//
// ORDERING, MEASURED RATHER THAN ASSUMED: SourceSharp_ClientEarlyInit runs at
// shell_clientdll.cpp:1073 and ClientWorldFactoryInit just after it, so the managed
// module — and this declaration with it — is up before the one C_World the
// factory hands out is allocated. That ordering is what lets the singleton's
// operator new know how long its blob has to be.
//
// Returns the payload word count, or -1 on refusal. See the server's twin.
//-----------------------------------------------------------------------------
int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes );

//-----------------------------------------------------------------------------
// The class the shim hands the engine from C_World::GetClientClass().
//
// NULL until C# has declared. Unlike the server's, a NULL here is survivable —
// the engine looks each SERVER class up in the client's list by name, so a
// client class that is absent simply never pairs — but it means the world's
// update is dropped, so the shim says so.
//-----------------------------------------------------------------------------
ClientClass *SourceSharp_WorldClientClass();

//-----------------------------------------------------------------------------
// THE DETAIL-SPRITE MATERIAL, READ OUT OF THE BLOB AND NOT OFF AN ENTITY.
//
// §3.11's clause "detailobjectsystem.cpp reads its material from the block",
// spelled as the one thing that file needs rather than as a class it has to
// name. It had `C_World *pWorld = GetClientWorldEntity();` and three calls
// through it; a stock renderer naming the world entity's TYPE to get at one
// string is a cast census entry and an include of c_world.h for nothing.
//
// NULL when there is no world yet, when C# has not declared, or when the map
// left `detailmaterial` unset -- and the caller's own fallback
// (DETAIL_SPRITE_MATERIAL) is what all three mean. An EMPTY string is
// deliberately answered as NULL rather than as "": the caller tested
// `... && *(...)` for exactly that case and folding it in here means one place
// decides what "the map said nothing" is.
//-----------------------------------------------------------------------------
const char *SourceSharp_WorldDetailSpriteMaterial();

//-----------------------------------------------------------------------------
// world_adapter_client.cpp's factory, by name, because the table is built here
// and the singleton lives there.
//
// It was `static IClientNetworkable *ClientWorldFactory( int, int )` and the
// only thing that changed is that a second translation unit can now name it:
// IMPLEMENT_CLIENTCLASS_FACTORY took its address from inside that file, and the
// runtime build takes it from here.
//-----------------------------------------------------------------------------
IClientNetworkable *SourceSharp_ClientWorldFactory( int entnum, int serialNum );

#endif // SHELL_WORLD_NETTABLE_CLIENT_H
