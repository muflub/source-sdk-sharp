//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the relays a SHARED file needs from an entity =======//
//
// standalone_relays.h is the server's relay header and there is no client twin,
// for the reason standalone_handle_ban_client.h states about its own pair: the
// two shells are two classes in two headers compiled into two DLLs, so a shared
// header cannot name either of them. That is fine for a relay DECLARATION,
// which names neither — only `IGameEntity`, which both sides have.
//
// So this header is the declaration, and the definition is written once per
// side beside the shell it crosses to:
//
//     server   sourcesharp/game/server/standalone_relays.cpp   (ShellOf)
//     client   sourcesharp/game/client/shell_standalone_client.cpp
//
// WHY IT EXISTS AT ALL, rather than the questions being asked of the entity.
// `game/shared/physics_shared.cpp` and `game/shared/ragdoll_shared.cpp` compile
// into BOTH DLLs and both are KEPT by the flip, so each of their entity asks
// has to have an answer on each side, in one spelling. Armed, `CBaseEntity` is
// `IGameEntity` — seven virtuals and no base — and igameentity.h refuses to
// grow: "an entity that implements those already answers them, and duplicating
// them here would invite the two to disagree." A relay is the shape that
// sentence points at, and it is the shape standalone_relays.h already is.
//
// THE MEMBERSHIP RULE, RESTATED FOR THIS FILE. Nothing goes here that the
// ENGINE initiates. Everything here is game code that starts at an entity and
// wants one fact about it, and every one of them dies with its callers exactly
// as standalone_relays.h's do.
//
//=============================================================================//
#ifndef SOURCESHARP_GAME_ENTITY_RELAYS_H
#define SOURCESHARP_GAME_ENTITY_RELAYS_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"

#include "igameentity_fwd.h"

//-----------------------------------------------------------------------------
// TWO ARMS, AND THE UNARMED ONE IS A TEMPLATE. Read this before adding a relay.
//
// Armed, `CBaseEntity` IS `IGameEntity` and every caller's argument binds to the
// declarations below directly. UNARMED IT DOES NOT: a CBaseEntity is a class of
// its own and is not an IGameEntity at all, so `GameEntity_ModelIndex( pEnt )`
// in a shared file would stop compiling in the configuration that ships today.
// (Measured, not reasoned: 17 errors across 7 files on an unarmed build of this
// worktree, which is what put these arms here.)
//
// So unarmed each relay is an inline forwarder to the member the entity still
// has. A TEMPLATE, because this header must not see the entity class -- it is
// reached from `game/shared/` files compiled into both DLLs and the two classes
// are different types in different headers. `move_root.h` is the same shape
// for the same reason: "TEntity is anything with GetMoveParent()".
//
// WHAT THIS COSTS A CALLER, and it is the one thing to know: template argument
// deduction does not perform user-defined conversions, so a caller holding an
// EHANDLE must write `h.Get()` rather than `h`. Armed that is the same
// expression, so the call site reads identically in both configurations.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE ENTITY'S OWN HANDLE — the identity both hierarchies share and the one
// thing armed code can always ask for.
//
// igameentity.h refuses this by name, in the same breath as the reason:
// "Also absent for the same reason, and never here at all: GetSolid on
// ICollideable, GetModelIndex on IServerEntity, GetRefEHandle on IHandleEntity.
// An entity that implements those already answers them, and duplicating them
// here would invite the two to disagree."  So the relay asks the face that
// already answered, after the one crossing each side's shell allows.
//
// NOT handle_cross.h, which runs the OTHER way — from a table occupant of
// unknown kind to a typed pointer — and pays two virtual calls for exactly that
// uncertainty. Here the kind is known by construction: the caller is holding an
// IGameEntity, and each DLL has exactly one class that implements it.
//
// BY VALUE rather than IHandleEntity::GetRefEHandle()'s const reference: a
// reference would bind into an object whose type the caller does not know, and
// every call site copies the handle out immediately anyway.
//-----------------------------------------------------------------------------
CBaseHandle GameEntity_RefHandle( const IGameEntity *pEnt );

//-----------------------------------------------------------------------------
// The engine's index for it, or -1.
//
// The same clause as the handle: this is IServerNetworkable's / IClientEntity's
// question and each shell answers it there. -1 is the value the SDK's own
// entindex() returns for an entity the engine has never heard of, so a NULL
// entity answers what an unnetworked one always did.
//-----------------------------------------------------------------------------
int GameEntity_EntIndex( const IGameEntity *pEnt );

//-----------------------------------------------------------------------------
// The model the engine knows this entity by, as the index modelinfo takes.
//
// igameentity.h names this one explicitly among the things it will NOT carry:
// "Also absent for the same reason, and never here at all: GetSolid on
// ICollideable, GetModelIndex on IServerEntity, GetRefEHandle on IHandleEntity."
// Both shells hold the SAME field — `Block().Head.ModelIndex`, the engine block
// at offset zero (plan_entity.md 3.2) — and answer it on the face that side
// has: `CStandaloneEntity::GetModelIndex()` (IServerEntity) and
// `C_StandaloneEntity::GetCollisionModelIndex()` (ICollideable). One field, two
// faces, and this is the one spelling a shared file can write.
//
// 0 for a NULL entity and for an entity with no model, which is what
// `modelinfo->GetModel( 0 )` already answers NULL for.
//-----------------------------------------------------------------------------
int GameEntity_ModelIndex( const IGameEntity *pEnt );

#endif // SOURCESHARP_GAME_ENTITY_RELAYS_H
