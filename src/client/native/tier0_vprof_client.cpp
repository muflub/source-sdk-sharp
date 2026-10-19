//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client's own vprof profile, never enabled ======//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// plan_client_novalve C9: the client links no tier0. The VPROF macros in the
// fork TUs still on its link (clientleafsystem.cpp's VPROF_BUDGET scopes) name
// tier0's g_VProfCurrentProfile and three CVProfNode members. User ruling
// 2026-10-18: drop vprof from the client rather than bind to the engine's
// profile. This is the client's own profile, which is never enabled, so every
// one of those macros does nothing. Divergence, accepted by that ruling: the
// client's nodes no longer appear in the engine's `vprof` report or budget
// panel; the engine's own nodes are untouched.
//
// The profile is constant-initialised bytes in CVProfile's layout under
// tier0's name, so no constructor runs and none is needed: m_enabled is 0
// (IsEnabled() is false, so a CVProfScope never enters) and m_fAtRoot is true
// (so CVProfile::EnterScope/ExitScope, called directly, do nothing either).
// m_TargetThreadId is 0, which no thread's id is, a second guard on both.
// vprof.h's own declaration of the name is renamed out of the way here, so
// the bytes can carry it; on Windows SS_BIND_IMPORT binds the fork TUs'
// dllimport of it, and the linker binds the members' (LNK4217), as for
// server.dll's ports (Windows: CI-verified only).
//
// The three CVProfNode members exist for the link alone: a profile that is
// never enabled has no current node to call them on. Reaching one means that
// invariant broke, and it stops the process saying so.
//=============================================================================//

#define g_VProfCurrentProfile ss_VProfDeclaredProfile
#include "tier0/vprof.h"
#undef g_VProfCurrentProfile

#include "ss_platform.h"	// SS_BIND_IMPORT

#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>

namespace
{
// CVProfile's protected fields, by offset (CVProfile is standard-layout).
struct ProfileLayout : CVProfile
{
	static constexpr size_t AtRoot() { return offsetof( ProfileLayout, m_fAtRoot ); }
};

[[noreturn]] void NeverEntered( const char *pszMember )
{
	fprintf( stderr, "[SourceSharp] CVProfNode::%s called in the client, whose vprof profile is "
	                 "never enabled (user ruling 2026-10-18); stopping.\n", pszMember );
	fflush( stderr );
	abort();
}
} // namespace

namespace ss
{
struct alignas( CVProfile ) ClientVProfBytes
{
	unsigned char m_Bytes[ sizeof( CVProfile ) ];
};

constexpr ClientVProfBytes NeverEnabledProfile()
{
	ClientVProfBytes bytes{};
	bytes.m_Bytes[ ProfileLayout::AtRoot() ] = 1;	// m_fAtRoot = true
	return bytes;
}
} // namespace ss

// tier0's name for the profile, over those bytes.
extern "C" ss::ClientVProfBytes g_VProfCurrentProfile = ss::NeverEnabledProfile();
SS_BIND_IMPORT( g_VProfCurrentProfile )

CVProfNode *CVProfNode::GetSubNode( const tchar *, int, const tchar *, int )
{
	NeverEntered( "GetSubNode" );
}

void CVProfNode::EnterScope()
{
	NeverEntered( "EnterScope" );
}

bool CVProfNode::ExitScope()
{
	NeverEntered( "ExitScope" );
}
