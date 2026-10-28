//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the layout of CGlobalVars, stated once ==============//
//
// plan_gamedll.md Part 0a #1. `Globals` on the managed side is a READER over
// engine-owned memory: one pointer, and every property is a load from the
// live `CGlobalVars` the engine hands the shell. A snapshot would be one frame
// stale everywhere and nothing would report it.
//
// That moves the whole risk from lifetime to LAYOUT. There is nothing to pin —
// the engine allocated this before the CLR existed and it never moves — but a
// managed mirror whose fields sit at different offsets than the C++ class reads
// the neighbouring field's bytes and returns a plausible number. No crash, no
// log line, a wrong `curtime`.
//
// SO THE LAYOUT IS ASSERTED, NOT MAINTAINED. This file is the single table both
// sides answer to:
//
//   - NATIVE: every row below is turned into a `static_assert` against the real
//     `offsetof(CGlobalVars, ...)` and `sizeof`. The numbers in the table are
//     therefore the COMPILER's, not a person's — a field inserted into
//     edict.h or globalvars_base.h stops this header compiling.
//
//   - MANAGED: SourceSharp.Tests/GlobalVarsLayoutParityTests reads the rows out
//     of this file as text and compares them to `CGlobalVarsRaw`, the same way
//     AbiParityTests reads sourcesharp.h and TierParityTests reads
//     generic_nettable.h. The C# side never contributes a number here, which is
//     what stops the comparison from being a check that cannot fail.
//
// FOUR THINGS IN THIS STRUCT RETURN A WRONG NUMBER RATHER THAN FAILING:
//
//   1. THE BASE CLASS'S PRIVATE TAIL COUNTS. `m_bClient`,
//      `nTimestampNetworkingBase` and `nTimestampRandomizeWindow` are private
//      and are still part of the layout. A mirror written from the public
//      fields alone puts `mapname` sixteen bytes early. They are in the table
//      below, marked PRIVATE — `offsetof` cannot reach them from out here, so
//      they are pinned by the two anchors either side (`pSaveData` at 48,
//      `mapname` at 72, `sizeof(CGlobalVarsBase)` at 72) plus a probe struct
//      that makes the compiler lay out the same three declarations. See
//      SS_GV_ASSERT_PRIVATE.
//
//   2. `bool` IS A TRAP. `m_bClient` is a one-byte bool followed by an int, and
//      `CGlobalVars` then has four consecutive bools before `maxEntities`. C#
//      `bool` is one byte in the runtime layout and FOUR under the marshaller,
//      so a mirror declaring `bool` has two layouts that disagree and the
//      difference is invisible until a later field reads garbage. The mirror
//      declares `byte` and exposes `bool` properties; the parity test checks
//      the runtime layout AND the marshalled layout so a `bool` here is red.
//
//   3. `pSaveData` IS A POINTER, so every offset after it depends on the
//      pointer width. x64 is asserted below rather than assumed.
//
//   4. THE TWO string_t ARE NOT STRINGS, and — measured, not assumed — in this
//      tree they are not four-byte pooled handles either. public/string_t.h's
//      default "strong" form is `struct string_t { const char *pszValue; }` and
//      the client's NO_STRING_T form is a bare `const char *`: EIGHT bytes and
//      eight-aligned in both. That is load-bearing twice over. If they really
//      were four-byte handles, `mapname` would land at 68 rather than 72 —
//      `CGlobalVarsBase` is not standard-layout (it mixes public and private
//      members), so the Itanium ABI lets a four-aligned derived field sit in
//      the base's tail padding — and every offset from there on would shift.
//      `Globals` still does not expose them: the pointer is engine-owned with
//      an engine lifetime, and handing managed code something that looks like a
//      string is the bug. The map name reaches C# through MapLoad.
//
//=============================================================================//
#pragma once

#include <stddef.h>
#include "edict.h"

//-----------------------------------------------------------------------------
// THE TABLE. F( name, offset, size, reach ) in declaration order, base class
// first. `reach` is PUBLIC when offsetof can name the field and PRIVATE when it
// cannot; it selects which assertion the row gets, and the managed side ignores
// it beyond checking the row is there.
//
// Rows are the whole struct, not only what `Globals` exposes. A mirror missing
// a field it never reads still moves every field after it.
//-----------------------------------------------------------------------------
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

// The total, which per-field offsets do not cover: every offset can agree while
// a trailing field is missing from the mirror entirely.
#define SOURCESHARP_GLOBALVARS_SIZEOF       112
#define SOURCESHARP_GLOBALVARS_BASE_SIZEOF   72

//-----------------------------------------------------------------------------
// x64 only, asserted. Every offset from pSaveData on depends on this.
//-----------------------------------------------------------------------------
static_assert( sizeof( void * ) == 8,
    "globalvars_layout.h describes the x64 layout of CGlobalVars; on a 32-bit "
    "build every offset from pSaveData on is different and the table is wrong." );

// string_t is a pointer here, not a four-byte pooled handle. If this ever
// changes, mapname moves into CGlobalVarsBase's tail padding at 68 and the rest
// of the struct shifts with it -- silently, since nothing else would fail.
static_assert( sizeof( string_t ) == 8,
    "string_t is pointer-sized in this tree (strong string_t wraps a const char*, "
    "NO_STRING_T *is* a const char*). A four-byte string_t moves mapname to 68." );

//-----------------------------------------------------------------------------
// A row's assertion, chosen by reach.
//
// PUBLIC: straight offsetof and sizeof against the real class.
//
// PRIVATE: offsetof cannot name the field, so the row is pinned three ways
// instead -- the public anchors either side of the tail (below), the size of
// the base class as a whole, and SS_GV_PrivateTailProbe, which hands the
// compiler the same three declarations in the same order and asks where it puts
// them. The probe is the real compiler's real padding rule for
// `pointer, bool, int, int`, which is the only thing in question here.
//-----------------------------------------------------------------------------
#define SS_GV_ASSERT_PUBLIC( name, offset, size )                        \
    static_assert( offsetof( CGlobalVars, name ) == (offset),            \
        "CGlobalVars::" #name " is not at " #offset " -- the managed "   \
        "mirror in SourceSharp.Abi reads the wrong bytes." );            \
    static_assert( sizeof( ( (CGlobalVars *)0 )->name ) == (size),       \
        "CGlobalVars::" #name " is not " #size " bytes." );

#define SS_GV_ASSERT_PRIVATE( name, offset, size ) /* see the probe below */

#define SS_GV_ASSERT_ROW( name, offset, size, reach ) \
    SS_GV_ASSERT_##reach( name, offset, size )

// -Winvalid-offsetof, silenced HERE and nowhere else. CGlobalVarsBase mixes
// public and private members, so CGlobalVars is not standard-layout and
// offsetof on it is "conditionally-supported" rather than guaranteed by the
// standard. It is supported and correct on every compiler this port builds
// with -- and the alternative to asking offsetof is guessing, which is the
// failure this whole file exists to prevent. Scoped to the assertions so a
// genuine invalid offsetof elsewhere is still reported.
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

//-----------------------------------------------------------------------------
// The private tail, laid out by the compiler rather than by hand.
//-----------------------------------------------------------------------------
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

// And the anchors that place the probe inside the real class: the tail begins
// right after pSaveData and ends where CGlobalVars's own fields begin.
static_assert( offsetof( CGlobalVars, pSaveData ) + sizeof( void * ) == 56,
    "the private tail does not begin at 56." );
static_assert( offsetof( CGlobalVars, mapname ) == SOURCESHARP_GLOBALVARS_BASE_SIZEOF,
    "CGlobalVars's own fields do not begin at the end of the base class -- the "
    "derived class is reusing the base's tail padding, which happens as soon as "
    "the first derived field needs less than eight-byte alignment." );

#if defined( __GNUC__ ) || defined( __clang__ )
#pragma GCC diagnostic pop
#endif
