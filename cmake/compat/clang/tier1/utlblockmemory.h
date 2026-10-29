//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// clang 22 rejects stock public/tier1/utlblockmemory.h at the template's
// definition: CUtlBlockMemory::Swap passes the bit-fields m_nIndexMask and
// m_nIndexShift to V_swap, which takes T&, and a non-const reference cannot
// bind to a bit-field. g++ accepts the header (it diagnoses only on
// instantiation, and nothing instantiates Swap), which is why the sniper
// build compiles it unpatched.
//
// This wrapper is ahead of public/ on clang's include path only
// (plan_server_native_v2 D2). It includes the stock header's own
// dependencies first, so that their bodies are read with the real V_swap;
// spells V_swap as a swap through a temporary, which a bit-field can take,
// for the stock header's body alone; and includes that header unchanged.
//
#pragma once

#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "mathlib/mathlib.h"
#include "tier0/memalloc.h"

#pragma push_macro( "V_swap" )
#undef V_swap
#define V_swap( a, b )             \
	do                             \
	{                              \
		auto ssCompatSwap = ( a ); \
		( a ) = ( b );             \
		( b ) = ssCompatSwap;      \
	} while ( 0 )
#include_next "tier1/utlblockmemory.h"
#pragma pop_macro( "V_swap" )
