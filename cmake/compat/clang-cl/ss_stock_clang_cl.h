//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// Forced into libss_particles, tier1 and mathlib under clang-cl only: the two
// stock headers those targets read that clang-cl rejects and MSVC accepts,
// settled before anything else includes them. They build from the pristine
// clone (cmake/SourceSharp.cmake's SS_STOCK), which is never edited.
//
//  * tier1/bitbuf.h declares members without RESTRICT and defines some with it
//    (__restrict under _MSC_VER): two types to clang, one to MSVC. The header
//    is read once here with RESTRICT empty for its body alone; elsewhere it
//    stays __restrict, which the prebuilt archives' symbols carry
//    (CMemoryStack::CommitTo). tier1/bitbuf.cpp, whose definitions say
//    RESTRICT too, is left out of tier1 under clang-cl (cmake/sdk/tier1.cmake).
//  * mathlib/ssemath.h's SubFloat and SubInt read an __m128's m128_f32 and
//    m128_u32 off POSIX, and clang's __m128 has no members. The header's POSIX
//    arm reads the same lanes through a pointer, so the header is read once
//    here with POSIX defined for its body alone; its guard makes every later
//    include a no-op.
//
#pragma once

#include "tier0/platform.h"
#pragma push_macro("RESTRICT")
#undef RESTRICT
#define RESTRICT
#include "tier1/bitbuf.h"
#pragma pop_macro("RESTRICT")

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"
#pragma push_macro("POSIX")
#define POSIX 1
#include "mathlib/ssemath.h"
#pragma pop_macro("POSIX")
