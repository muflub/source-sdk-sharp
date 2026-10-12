//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// The three entity member-function pointer types, and the three datadesc macros
// that store one -- lifted out of baseentity.h ahead of that header's deletion.
//
// BASEPTR, ENTITYFUNCPTR and USEPTR name the shapes of a Think, a Touch/Blocked
// and a Use handler. DEFINE_THINKFUNC, DEFINE_ENTITYFUNC and DEFINE_USEFUNC are
// the FTYPEDESC_FUNCTIONTABLE half of the datadesc, and they are the half that
// compiles silently: DEFINE_INPUTFUNC static_casts and becomes a compile error
// the day CBaseEntity changes, while these three go through
// DEFINE_FUNCTION_RAW, which used to be one C-style cast doing both the class
// conversion and the signature pun at once. entity_funcslot.h is where that was
// taken apart; this file is only about the three of them outliving the header
// they were declared in.
//
// Measured, at f219cf23, by preprocessing each translation unit with ninja's
// own command line rather than grepping the tree -- grep counts sites inside
// `#if !defined( CLIENT_DLL )` that no client TU is ever handed:
//
//   server: 142 inputfunc_t constructions across 19 objects
//   client:   0
//
// So this is a server header, and game/client/c_baseentity.h's own BASEPTR and
// ENTITYFUNCPTR are left alone: their only users are the client entity code
// that dies with them.
//
// WHY THIS FILE HAS NO INCLUDES
//
// Same reason as entity_bases.h. It is included from
// game/shared/baseentity_shared.h's server branch, on the line BEFORE
// `#include "baseentity.h"` -- before rather than after, because baseentity.h
// declares m_pfnThink as a BASEPTR and would not compile otherwise. What it
// needs from its includer is a declaration of CBaseEntity (public/datamap.h
// supplies one at prelude line 353) and USE_TYPE (game/shared/shareddefs.h, at
// prelude line 389), both of which the prelude has opened long before it
// reaches baseentity_shared.h at line 477. Measured with `g++ -H` on the
// precompiled prelude itself.
//
// Reach after the move, by docs/typedef-dryrun/reach_pch.py: 197 of 197 server
// objects, 0 of 325 client -- the same reach it had inside baseentity.h.
//
// It declares neither of those itself. A `class CBaseEntity;` here would be a
// seventy-second forward declaration for step 4's alias to have to defuse.
//
//=============================================================================//
#ifndef ENTITY_FUNCS_H
#define ENTITY_FUNCS_H
#ifdef _WIN32
#pragma once
#endif

typedef void (IGameEntity::*BASEPTR)(void);
typedef void (IGameEntity::*ENTITYFUNCPTR)(IGameEntity *pOther );
typedef void (IGameEntity::*USEPTR)( IGameEntity *pActivator, IGameEntity *pCaller, USE_TYPE useType, float value );

#define DEFINE_THINKFUNC( function ) DEFINE_FUNCTION_RAW( function, BASEPTR )
#define DEFINE_ENTITYFUNC( function ) DEFINE_FUNCTION_RAW( function, ENTITYFUNCPTR )
#define DEFINE_USEFUNC( function ) DEFINE_FUNCTION_RAW( function, USEPTR )

#endif // ENTITY_FUNCS_H
