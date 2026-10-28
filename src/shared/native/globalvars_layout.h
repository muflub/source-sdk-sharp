//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CGlobalVars' layout, asserted against the compiler here and read as text by the managed
// mirror's parity test. The private base tail and the eight-byte string_t fields count,
// and a wrong offset silently reads a neighbouring field.
#pragma once

#include <stddef.h>
#include "edict.h"

// F( name, offset, size, reach ): the whole struct in declaration order, base first.
// reach is PRIVATE where offsetof cannot name the field.
#define SOURCESHARP_GLOBALVARS_LAYOUT( F )                       \
    /* CGlobalVarsBase, public */                                \
    F( realtime,                    0,  4, PUBLIC  )             \
    F( framecount,                  4,  4, PUBLIC  )             \
    F( absoluteframetime,           8,  4, PUBLIC  )             \
    F( curtime,                    12,  4, PUBLIC  )             \
    F( frametime,                  16,  4, PUBLIC  )             \
    F( maxClients,                 20,  4, PUBLIC  )             \
    F( tickcount,                  24,  4, PUBLIC  )             \
    F( interval_per_tick,          28,  4, PUBLIC  )             \
    F( interpolation_amount,       32,  4, PUBLIC  )             \
    F( simTicksThisFrame,          36,  4, PUBLIC  )             \
    F( network_protocol,           40,  4, PUBLIC  )             \
    /* four bytes of padding: the pointer is eight-aligned */    \
    F( pSaveData,                  48,  8, PUBLIC  )             \
    /* CGlobalVarsBase, private — part of the layout all the same */ \
    F( m_bClient,                  56,  1, PRIVATE )             \
    F( nTimestampNetworkingBase,   60,  4, PRIVATE )             \
    F( nTimestampRandomizeWindow,  64,  4, PRIVATE )             \
    /* CGlobalVars's own fields start after the base's 72 bytes */ \
    F( mapname,                    72,  8, PUBLIC  )             \
    F( mapversion,                 80,  4, PUBLIC  )             \
    F( startspot,                  88,  8, PUBLIC  )             \
    F( eLoadType,                  96,  4, PUBLIC  )             \
    F( bMapLoadFailed,            100,  1, PUBLIC  )             \
    F( deathmatch,                101,  1, PUBLIC  )             \
    F( coop,                      102,  1, PUBLIC  )             \
    F( teamplay,                  103,  1, PUBLIC  )             \
    F( maxEntities,               104,  4, PUBLIC  )             \
    F( serverCount,               108,  4, PUBLIC  )

// The total catches a trailing field missing from the mirror.
#define SOURCESHARP_GLOBALVARS_SIZEOF       112
#define SOURCESHARP_GLOBALVARS_BASE_SIZEOF   72

// x64 only: every offset from pSaveData on depends on it.
static_assert( sizeof( void * ) == 8,
    "globalvars_layout.h describes the x64 layout of CGlobalVars; on a 32-bit "
    "build every offset from pSaveData on is different and the table is wrong." );

// A four-byte string_t would move mapname into the base's tail padding at 68.
static_assert( sizeof( string_t ) == 8,
    "string_t is pointer-sized in this tree (strong string_t wraps a const char*, "
    "NO_STRING_T *is* a const char*). A four-byte string_t moves mapname to 68." );

// PRIVATE rows are pinned by the probe struct and the anchors below instead.
#define SS_GV_ASSERT_PUBLIC( name, offset, size )                        \
    static_assert( offsetof( CGlobalVars, name ) == (offset),            \
        "CGlobalVars::" #name " is not at " #offset " -- the managed "   \
        "mirror in SourceSharp.Abi reads the wrong bytes." );            \
    static_assert( sizeof( ( (CGlobalVars *)0 )->name ) == (size),       \
        "CGlobalVars::" #name " is not " #size " bytes." );

#define SS_GV_ASSERT_PRIVATE( name, offset, size ) /* see the probe below */

#define SS_GV_ASSERT_ROW( name, offset, size, reach ) \
    SS_GV_ASSERT_##reach( name, offset, size )

// CGlobalVars is not standard-layout, so offsetof on it warns; silenced for these asserts only.
#if defined( __GNUC__ ) || defined( __clang__ )
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Winvalid-offsetof"
#endif

SOURCESHARP_GLOBALVARS_LAYOUT( SS_GV_ASSERT_ROW )

#undef SS_GV_ASSERT_ROW
#undef SS_GV_ASSERT_PRIVATE
#undef SS_GV_ASSERT_PUBLIC

static_assert( sizeof( CGlobalVars ) == SOURCESHARP_GLOBALVARS_SIZEOF,
    "sizeof(CGlobalVars) changed. Per-field offsets can all still agree while a "
    "trailing field is missing from the managed mirror." );

static_assert( sizeof( CGlobalVarsBase ) == SOURCESHARP_GLOBALVARS_BASE_SIZEOF,
    "sizeof(CGlobalVarsBase) changed, so the private tail is a different size "
    "and CGlobalVars's own fields no longer start at 72." );

// The private tail, laid out by the compiler.
struct SS_GV_PrivateTailProbe
{
    CSaveRestoreData *pSaveData;            // the last public base field
    bool              m_bClient;
    int               nTimestampNetworkingBase;
    int               nTimestampRandomizeWindow;
};

static_assert( offsetof( SS_GV_PrivateTailProbe, pSaveData ) == 48 - 48, "" );
static_assert( offsetof( SS_GV_PrivateTailProbe, m_bClient ) == 56 - 48,
    "the bool after the pointer is not where the table says (three bytes of "
    "padding follow it, which is the trap C# bool walks into)." );
static_assert( offsetof( SS_GV_PrivateTailProbe, nTimestampNetworkingBase ) == 60 - 48, "" );
static_assert( offsetof( SS_GV_PrivateTailProbe, nTimestampRandomizeWindow ) == 64 - 48, "" );

// Anchors placing the probe inside the real class.
static_assert( offsetof( CGlobalVars, pSaveData ) + sizeof( void * ) == 56,
    "the private tail does not begin at 56." );
static_assert( offsetof( CGlobalVars, mapname ) == SOURCESHARP_GLOBALVARS_BASE_SIZEOF,
    "CGlobalVars's own fields do not begin at the end of the base class -- the "
    "derived class is reusing the base's tail padding, which happens as soon as "
    "the first derived field needs less than eight-byte alignment." );

#if defined( __GNUC__ ) || defined( __clang__ )
#pragma GCC diagnostic pop
#endif
