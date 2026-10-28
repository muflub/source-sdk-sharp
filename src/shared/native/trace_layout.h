//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Ray_t and CGameTrace offsets as managed mirrors them (x64 only).
// Columns: field, offset, size. The managed EngineRay and EngineGameTrace read the same rows.
#pragma once



#include "gametrace.h"

// Ray_t is 16-aligned for the engine's SIMD loads; the fourth float of each vector is unused.
#define SOURCESHARP_RAY_LAYOUT( F )      \
    F( m_Start,        0, 16 )           \
    F( m_Delta,       16, 16 )           \
    F( m_StartOffset, 32, 16 )           \
    F( m_Extents,     48, 16 )           \
    F( m_IsRay,       64,  1 )           \
    F( m_IsSwept,     65,  1 )

#define SOURCESHARP_RAY_SIZEOF   80
#define SOURCESHARP_RAY_ALIGNOF  16

// CGameTrace, CBaseTrace's fields first. surface is eight-aligned for its name pointer.
#define SOURCESHARP_GAMETRACE_LAYOUT( F )  \
    F( startpos,            0, 12 )        \
    F( endpos,             12, 12 )        \
    F( plane,              24, 20 )        \
    F( fraction,           44,  4 )        \
    F( contents,           48,  4 )        \
    F( dispFlags,          52,  2 )        \
    F( allsolid,           54,  1 )        \
    F( startsolid,         55,  1 )        \
    F( fractionleftsolid,  56,  4 )        \
    F( surface,            64, 16 )        \
    F( hitgroup,           80,  4 )        \
    F( physicsbone,        84,  2 )        \
    F( m_pEnt,             88,  8 )        \
    F( hitbox,             96,  4 )

#define SOURCESHARP_GAMETRACE_SIZEOF  104

static_assert( sizeof( void * ) == 8, "trace_layout.h describes the x64 layouts" );

#define SS_RAY_ASSERT( name, offset, size )                                          \
    static_assert( offsetof( Ray_t, name ) == (offset),                               \
        "Ray_t::" #name " is not at " #offset ": EngineRay builds the wrong bytes" );  \
    static_assert( sizeof( ( (Ray_t *)0 )->name ) == (size), "Ray_t::" #name " is not " #size " bytes" );

#define SS_GAMETRACE_ASSERT( name, offset, size )                                          \
    static_assert( offsetof( CGameTrace, name ) == (offset),                                \
        "CGameTrace::" #name " is not at " #offset ": EngineGameTrace reads the wrong bytes" ); \
    static_assert( sizeof( ( (CGameTrace *)0 )->name ) == (size), "CGameTrace::" #name " is not " #size " bytes" );

#if defined( __GNUC__ ) || defined( __clang__ )
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Winvalid-offsetof"
#endif
SOURCESHARP_RAY_LAYOUT( SS_RAY_ASSERT )
SOURCESHARP_GAMETRACE_LAYOUT( SS_GAMETRACE_ASSERT )

static_assert( offsetof( cplane_t, normal ) == 0 && offsetof( cplane_t, dist ) == 12
               && offsetof( cplane_t, type ) == 16 && offsetof( cplane_t, signbits ) == 17,
               "cplane_t is not normal, dist, type, signbits" );
static_assert( offsetof( csurface_t, name ) == 0 && offsetof( csurface_t, surfaceProps ) == 8
               && offsetof( csurface_t, flags ) == 10,
               "csurface_t is not name, surfaceProps, flags" );
#if defined( __GNUC__ ) || defined( __clang__ )
#pragma GCC diagnostic pop
#endif

static_assert( sizeof( Ray_t ) == SOURCESHARP_RAY_SIZEOF, "Ray_t is not 80 bytes" );
static_assert( alignof( Ray_t ) == SOURCESHARP_RAY_ALIGNOF, "Ray_t is not 16-aligned" );
static_assert( sizeof( CGameTrace ) == SOURCESHARP_GAMETRACE_SIZEOF, "CGameTrace is not 104 bytes" );

#undef SS_RAY_ASSERT
#undef SS_GAMETRACE_ASSERT
