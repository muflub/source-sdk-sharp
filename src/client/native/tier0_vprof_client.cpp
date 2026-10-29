//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The client's own vprof profile, never enabled: client.so links no tier0, and
// the VPROF macros in fork TUs name g_VProfCurrentProfile and three CVProfNode
// members, so every such macro does nothing. The client's nodes therefore do
// not appear in the engine's vprof report.
//
// The profile is constant-initialised bytes in CVProfile's layout under
// tier0's name, so no constructor runs: m_enabled is 0, m_fAtRoot is true (so
// EnterScope/ExitScope do nothing) and m_TargetThreadId is 0, which no thread's
// id is. The CVProfNode members exist only for the link; reaching one stops
// the process.

#define g_VProfCurrentProfile ss_VProfDeclaredProfile
#include "tier0/vprof.h"

#undef g_VProfCurrentProfile

#include "ss_platform.h"

namespace
{
// CVProfile's protected fields, by offset (CVProfile is standard-layout).
struct ProfileLayout : CVProfile
{
	static constexpr size_t AtRoot() { return offsetof( ProfileLayout, m_fAtRoot ); }
};

[[noreturn]] void NeverEntered( const char *pszMember )
{
	fprintf( stderr,
			 "[SourceSharp] CVProfNode::%s called in the client, whose vprof profile is "
			 "never enabled (user ruling 2026-10-18); stopping.\n",
			 pszMember );
	fflush( stderr );
	abort();
}
} // namespace

namespace ss
{
struct alignas( CVProfile ) ClientVProfBytes
{
	unsigned char m_Bytes[sizeof( CVProfile )];
};

constexpr ClientVProfBytes NeverEnabledProfile()
{
	ClientVProfBytes bytes{};
	bytes.m_Bytes[ProfileLayout::AtRoot()] = 1; // m_fAtRoot = true
	return bytes;
}
} // namespace ss

// tier0's name for the profile, over those bytes.
extern "C"
{
ss::ClientVProfBytes g_VProfCurrentProfile = ss::NeverEnabledProfile();
}
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

#if defined( _WIN32 )
// CVProfile's EnterScope and ExitScope are inline in vprof.h; a fork object that
// did not inline one imports it from tier0.dll. Emitted here so the linker binds
// each import to them (LNK4217). Nothing calls this function.
#pragma optimize( "", off )

void SS_EmitVProfInlines( CVProfile *pProfile )
{
	pProfile->EnterScope( "", 0, "", false, 0 );
	pProfile->ExitScope();
}

#pragma optimize( "", on )
#endif
