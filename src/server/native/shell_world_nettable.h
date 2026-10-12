//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================//
//
// worldspawn's ServerClass, DECLARED FROM C# (plan_gamedll_after.md Part B2).
//
// THE OWNER'S RULING THIS FILE EXISTS FOR:
//
//     "CWorld needs a native shim class, but all the logic can be managed. It
//      should NOT DECLARE_SERVERCLASS -- that should all come from the managed
//      side. Network vars live in managed code, with C++ calling into managed
//      for get/set."
//
// So CWorld carries no DECLARE_SERVERCLASS and no IMPLEMENT_SERVERCLASS_ST, and
// the ServerClass the engine gets for it is built HERE, at DLL init, out of a
// declaration that lives in C# -- SourceSharp.Shared.WorldNetState, the same
// [StandaloneNetState] mechanism that already networks fifty classes.
//
//=============================================================================//
//
// THE QUESTION THIS PORT TURNS ON, ANSWERED WITH THE CODE THAT ANSWERS IT.
//
// The brief for this lane says worldspawn "is created by the engine during map
// load, not through the managed spawn path". MEASURED, THAT IS NOT WHAT
// HAPPENS. worldspawn is created by THIS GAME DLL, on the same route every
// managed class takes:
//
//     mapentities.cpp:612    MapEntity_ParseEntity( pEntity, ... )
//     mapentities.cpp:1199   pEntity = CreateEntityByName( className )
//                            -> EntityFactoryDictionary()->Create( "worldspawn" )
//                            -> CEntityFactory<CWorld>::Create      (util.h:134)
//                            -> _CreateEntityTemplate               (util.h:66)
//                               `newEnt = new T;`
//                            -> CBaseEntity::operator new
//                               -> engine->PvAllocEntPrivateData( sizeof )
//
// mapentities.cpp is in server_csharp.vpc, so that whole chain is this DLL's.
// What the ENGINE assumes about worldspawn is its INDEX and nothing else:
// CWorld::RequiredEdictIndex() answers 0 and the constructor does
// NetworkProp()->AttachEdict( INDEXENT( 0 ) ).
//
// So "how does managed net state attach to an entity managed code did not
// create" is the wrong question, because managed code's route and the world's
// route are the same route. The real difference is the HIERARCHY:
//
//     CStandaloneEntity::operator new( size_t, int nBlobBytes )
//         -> PvAllocEntPrivateData( SourceSharp_StandaloneBlobOffset() + n )
//
// over-allocates so the blob the props address sits past the object.
// CBaseEntity::operator new does not. Attaching managed net state to CWorld is
// therefore an ALLOCATION and a BASE OFFSET, not a creation path -- and both are
// things a shim can have. CWorld::operator new over-allocates by
// SourceSharp_WorldBlobBytes() past SourceSharp_WorldBlobOffset(), which is the
// same two numbers CStandaloneEntity has, computed the same way, for a class on
// the SDK's hierarchy instead of the parallel one.
//
//=============================================================================//

#ifndef SHELL_WORLD_NETTABLE_H
#define SHELL_WORLD_NETTABLE_H
#ifdef _WIN32
#pragma once
#endif

#include "basetypes.h"

class ServerClass;

//-----------------------------------------------------------------------------
// Where the world's blob sits inside a CWorld, and how long it is.
//
// The OFFSET is defined in world_adapter.cpp for the reason
// SourceSharp_StandaloneBlobOffset is defined beside the concrete shell rather
// than beside the table: sizeof() of a class is a fact of the translation unit
// that has its definition, and this header deliberately does not include
// world.h -- which drags in cbase.h and could never be compiled into a gtest.
//
// The LENGTH is C#'s. It arrives through SourceSharp_WorldNetDeclare below and
// is zero until it has: a build whose managed side never declared has no world
// table, which the shim reports rather than papers over.
//-----------------------------------------------------------------------------
int SourceSharp_WorldBlobOffset();
int SourceSharp_WorldBlobBytes();

//-----------------------------------------------------------------------------
// ABI (engine api): C# declares worldspawn's blob size, and this builds the
// ServerClass around it.
//
// Called from StandaloneNetStates' publish pass, INSIDE THE DLL-INIT WINDOW,
// for the same reason the standalone tiers are: a ServerClass links itself into
// g_pServerClassHead when it is constructed and the engine walks that list
// exactly once, through CServerGameDLL::GetAllServerClasses.
//
// Returns the payload word count the table carries, or -1 for a size no payload
// can name and for a second declaration that disagrees with one a table has
// already been built against.
//-----------------------------------------------------------------------------
int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes );

//-----------------------------------------------------------------------------
// The class the shim hands the engine from CWorld::GetServerClass().
//
// NULL until C# has declared, which is the one case CWorld has to have an
// answer for: it falls back to its base's, warns once, and says what to do
// about it. A NULL here would be a null dereference inside
// CServerNetworkProperty::GetServerClass on the first entity the engine packs.
//-----------------------------------------------------------------------------
ServerClass *SourceSharp_WorldServerClass();

//-----------------------------------------------------------------------------
// THE METER'S THIRD ANSWERER (plan_gamedll.md Part 5).
//
// SourceSharp_IsManagedServerClass answers by IDENTITY against the places
// SourceSharp CONSTRUCTS a ServerClass, and this is now one of them. Phase A5
// recorded that CWorld was counted as stock because world_adapter.cpp built it
// with the SDK macro and that "what must not happen is somebody moving the
// meter by adding world_adapter.cpp to that identity list: it would read as an
// eviction and evict nothing."
//
// THAT IS STILL THE RULE AND THIS IS NOT THAT EDIT. The macro is gone; the
// object below is constructed here, from a managed declaration, with managed
// props. The class MOVED, so counting it as managed is a report of the move
// rather than a relabelling of a registration that did not happen -- and the
// fact that refused the relabelling is kept, strengthened, and now asserts both
// halves at once.
//-----------------------------------------------------------------------------
bool SourceSharp_IsWorldServerClass( const ServerClass *pClass );

#endif // SHELL_WORLD_NETTABLE_H
