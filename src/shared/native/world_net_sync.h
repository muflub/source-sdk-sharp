//========= SourceSharp — worldspawn's networked state, in one place =========//
//
// The ten values DT_WORLD carries, declared in C# and reached from C++ by
// asking C# where they are (plan_gamedll_after.md Part B2).
//
// COMPILED INTO BOTH DLLS, and that is the point rather than a convenience. The
// server's shim is a CWorld and the client's is a C_World; they are unrelated
// classes with different blob bases, and the ONE thing they must agree about is
// what a field is called and where inside the blob it sits. That agreement is
// this file, and it is one body with two callers — the shape Phase 7 recorded
// as "two drivers is the design, two bodies would be the bug".
//
// EVERY OFFSET IS ASKED, NEVER COPIED. The layout exists once, in the language
// that computes it, and native gets at it through NetStateFieldOffset /
// NetStateFieldBits on the managed API — which is what the ABI comment on those
// two slots says they are for, and what standalone_player_sync.cpp already does
// for ss_player's twenty-nine. A field that does not resolve reads as nothing
// and says so once, rather than reading whatever happens to be at offset zero.
//
//=============================================================================//
#ifndef SOURCESHARP_WORLD_NET_SYNC_H
#define SOURCESHARP_WORLD_NET_SYNC_H
#ifdef _WIN32
#pragma once
#endif

#include "generic_payload.h"
#include "mathlib/vector.h"

#include <string.h>

//-----------------------------------------------------------------------------
// DELIBERATELY LIGHT. world.h includes this, and world.h is included by five
// translation units that have nothing to do with networking --
// baseentity.cpp, mapentities.cpp, physics.cpp, physics_impact_damage.cpp and
// (since step 4a moved gametrace_dll.cpp there) world_subject_dll.cpp. Pulling
// sourcesharp.h and standalone_player_sync.h in behind it would put the whole
// ABI header into each of them to reach one function pointer, so the ask is
// made in the .cpp and only the RESULT has a type here.
//-----------------------------------------------------------------------------
unsigned int SourceSharp_PlayerSyncGeneration();

//-----------------------------------------------------------------------------
// The classname the declaration is keyed by, spelt once.
//
// It is the map's classname, the factory's name and the [StandaloneNetState]
// key all at the same time, and a misspelling here is not a compile error — it
// is ten fields that silently do not resolve. So there is one spelling of it in
// the C++ and one in the C#, and the parity between them is a fact rather than
// a habit (world_shim_test.cpp).
//-----------------------------------------------------------------------------
#define SOURCESHARP_WORLD_NETCLASS "worldspawn"

//-----------------------------------------------------------------------------
// Where each of the ten sits INSIDE THE BLOB.
//
// Blob-relative, exactly as NetStateFieldOffset answers: the caller adds its own
// shim's base. The two sides' bases differ and always will, because a CWorld and
// a C_World are different objects; what the two sides agree on is the offset
// within the blob, which is the same split the payload props have always had.
//
// StartDark and ColdWorld are PACKED — the C# packer put both flags in one word,
// at shifts 0 and 1 — so they carry a placement rather than a plain offset. A
// caller that read a byte at StartDark's offset would get ColdWorld's bit with
// it, which is precisely the hazard NetStateFieldBits exists for.
//-----------------------------------------------------------------------------
struct SourceSharpWorldFields_t
{
	unsigned int nGeneration;   // 0 = never resolved

	int nWaveHeight;
	int nMaxOccludeeArea;
	int nMinOccluderArea;
	int nMaxPropScreenSpaceWidth;
	int nMinPropScreenSpaceWidth;
	int nWorldMins;
	int nWorldMaxs;
	int nDetailSpriteMaterial;

	SourceSharpPackedField_t StartDark;
	SourceSharpPackedField_t ColdWorld;

	SourceSharpWorldFields_t() { memset( this, 0, sizeof( *this ) ); nWaveHeight = -1;
	                             nMaxOccludeeArea = -1; nMinOccluderArea = -1;
	                             nMaxPropScreenSpaceWidth = -1; nMinPropScreenSpaceWidth = -1;
	                             nWorldMins = -1; nWorldMaxs = -1; nDetailSpriteMaterial = -1; }

	// THE SAME RESOLVE GENERATION THE PLAYER'S SYNC USES, deliberately and not
	// by accident of reuse. It counts one event — "the managed side republished
	// its net states" — and that event invalidates every native-held offset at
	// once, the world's along with the player's. A second counter bumped from
	// the same place would be two records of one fact, which is the shape this
	// tree keeps paying for.
	bool IsCurrent() const
	{
		return nGeneration != 0 && nGeneration == SourceSharp_PlayerSyncGeneration();
	}
};

//-----------------------------------------------------------------------------
// Asks the managed side for all ten placements.
//
// False — with one warning naming the class rather than ten naming fields —
// when worldspawn's declaration is not published on this side. The caller tries
// again on its next read rather than remembering the failure, because the
// managed side comes up after the shim does and a remembered "no" would outlive
// the reason for it.
//-----------------------------------------------------------------------------
bool SourceSharpWorldFields_Resolve( SourceSharpWorldFields_t *pFields );

//-----------------------------------------------------------------------------
// The readers. Every one is safe against an unresolved field and against a NULL
// blob, and answers the zero a freshly allocated world would have carried —
// which is what stock's members held too, because PvAllocEntPrivateData returns
// zeroed memory and CWorld's constructor set only m_bColdWorld, to false.
//-----------------------------------------------------------------------------
float  SourceSharpWorld_ReadFloat ( const unsigned char *pBlob, int nOffset );
Vector SourceSharpWorld_ReadVector( const unsigned char *pBlob, int nOffset );
bool   SourceSharpWorld_ReadFlag  ( const unsigned char *pBlob,
                                    const SourceSharpPackedField_t &field );

//-----------------------------------------------------------------------------
// The detail sprite material, as a pointer INTO the blob.
//
// A pointer rather than a copy because that is what its one consumer wants:
// shell_detail_props.cpp hands it to managed's DetailPropSystem. The bytes
// are the world object's and live as long as it does, which is the same
// lifetime the char[256] member had. Since step 5e that consumer asks
// SourceSharp_WorldDetailSpriteMaterial() rather than naming C_World — the
// renderer wants a string, not an entity type — and that function is one more
// hop down this same read.
//
// Answers "" — never NULL — for an unresolved field or a blob that cannot hold
// the string, so a caller may dereference the first byte without testing, which
// is exactly what the one above the hop does before it folds "" into NULL.
//
// TERMINATION IS CHECKED, not assumed. C#'s WriteString always terminates, but
// this reads memory the wire filled and a caller that got a run of bytes with no
// NUL in it would walk off the end of the allocation.
//-----------------------------------------------------------------------------
const char *SourceSharpWorld_ReadString( const unsigned char *pBlob, int nBlobBytes,
                                         int nOffset );

//-----------------------------------------------------------------------------
// The one writer native does. See CWorld::SetStartDark.
//-----------------------------------------------------------------------------
void SourceSharpWorld_WriteFlag( unsigned char *pBlob,
                                 const SourceSharpPackedField_t &field, bool bValue );

//-----------------------------------------------------------------------------
// ALL TEN VALUES, ON ONE LINE, FROM WHICHEVER SIDE ASKS.
//
// THIS IS THE ONLY LIVE EVIDENCE THE VALUES ARRIVE, and that is why it is a
// shipped line rather than a probe somebody added for one run. The eleven
// things worldspawn tells the client are read by the RENDERER -- the world
// bounds by the WorldDims material proxy and the client's own effects, the two
// occlusion areas by engine->SetOcclusionParameters, the two prop widths by
// modelinfo->SetLevelScreenFadeRange, the detail sprite material by
// the detail props (shell_detail_props.cpp), startdark by the screen fade -- and every one of them
// fails the same way: the value is a plausible zero, nothing crashes, nothing
// logs, and the symptom is a rendering difference nobody attributes. Phase A2
// reached the same conclusion about its three Steam/mdl methods and shipped
// three log lines for the same reason.
//
// ONE BODY, BOTH SIDES, which is the point of it: the server prints what it
// stored and the client prints what it decoded, through the same formatting of
// the same field list, so the two lines are comparable by eye and by grep. A
// per-side formatter would be two chances to print a different subset.
//
// Once per level load on each side. Not per frame: none of these values changes
// after the map is parsed.
//-----------------------------------------------------------------------------
void SourceSharpWorld_Report( const char *pszSide, const unsigned char *pBlob,
                              int nBlobBytes, const SourceSharpWorldFields_t &fields );

#endif // SOURCESHARP_WORLD_NET_SYNC_H
