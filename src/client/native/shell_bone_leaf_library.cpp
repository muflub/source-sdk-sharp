//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client bone leaf library's gate and counters ====//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_entity.md §6, the bone-leaf-library row, and §3.7. Lane `f-bone`.
// The argument for the move is in bone_leaf_library.h; this file is the bodies.
//
// EVERY BODY BELOW WAS MOVED OUT OF `game/client/c_baseanimating.cpp`, which the
// client flip deletes, AND NOT REWRITTEN. Where a line looks odd it is stock's
// line: the `(char const *)1` sentinel tag, the `g_BoneAcessBase` spelling and
// the `STAGING_ONLY_EXEC` reentrancy verifier are all carried across verbatim so
// that a reader diffing the move sees a move. `C_BaseAnimating`'s statics are now
// one-line forwarders to these, so there is exactly ONE copy of the state and
// the unarmed build behaves as it did.
//
// THIS FILE IS OPTED OUT OF THE cbase.h PRECOMPILED HEADER, for the reason
// `shell_acquire_reasons.cpp` gives: nothing that includes cbase.h can be
// compiled into a standalone gtest, and the gate's push/pop/allow behaviour is
// exactly the kind of claim a gtest should drive against the real function
// rather than a restatement of it. `sourcesharp/test/bone_leaf_library_test.cpp`
// compiles THIS FILE.
//
// It is also the strongest available proof of the §3.7 promise. A translation
// unit that does not include cbase.h CANNOT name an entity type, so "no entity
// type in the leaf library's interface" is enforced by the build rather than by
// review.
//
//===========================================================================//

#include "bone_leaf_library.h"

#include <string.h>

#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier0/threadtools.h"
#include "tier1/utlvector.h"

//-----------------------------------------------------------------------------
// THE BONE-ACCESS GATE.
//
// Stock's comment, kept: "Causes an assert to happen if bones or attachments are
// used while this is false."
//-----------------------------------------------------------------------------
struct BoneAccess
{
	BoneAccess()
	{
		bAllowBoneAccessForNormalModels = false;
		bAllowBoneAccessForViewModels = false;
		tag = NULL;
	}

	bool bAllowBoneAccessForNormalModels;
	bool bAllowBoneAccessForViewModels;
	char const *tag;
};

// Stock's comment, kept: "the modelcache critical section is insufficient for
// preventing us from getting into the bone cache at the same time. The bonecache
// itself is protected by a mutex, but the actual bone access stack needs to be
// protected separately."
static CThreadFastMutex g_BoneAccessMutex;
static CUtlVector< BoneAccess > g_BoneAccessStack;
// `g_BoneAcessBase` — stock's spelling, with the missing `c`. Kept so that a
// grep for the old name still lands on the state it named.
static BoneAccess g_BoneAcessBase;

void SourceSharp_BoneAccessPush( bool bAllowBones, bool bAllowAttachments, const char *pTag )
{
	AUTO_LOCK( g_BoneAccessMutex );
	STAGING_ONLY_EXEC( ReentrancyVerifier rv( &dbg_bonestack_reentrant_count, dbg_bonestack_perturb.GetInt() ) );

	BoneAccess save = g_BoneAcessBase;
	g_BoneAccessStack.AddToTail( save );

	Assert( g_BoneAccessStack.Count() < 32 ); // Most likely we are leaking "PushAllowBoneAccess" calls if PopBoneAccess is never called. Consider using AutoAllowBoneAccess.
	g_BoneAcessBase.bAllowBoneAccessForNormalModels = bAllowBones;
	g_BoneAcessBase.bAllowBoneAccessForViewModels = bAllowAttachments;
	g_BoneAcessBase.tag = pTag;
}

void SourceSharp_BoneAccessPop( const char *pTag )
{
	AUTO_LOCK( g_BoneAccessMutex );
	STAGING_ONLY_EXEC( ReentrancyVerifier rv( &dbg_bonestack_reentrant_count, dbg_bonestack_perturb.GetInt() ) );

	// Validate that pop matches the push
	Assert( ( g_BoneAcessBase.tag == pTag ) || ( g_BoneAcessBase.tag && g_BoneAcessBase.tag != ( char const * ) 1 && pTag && pTag != ( char const * ) 1 && !strcmp( g_BoneAcessBase.tag, pTag ) ) );
	int lastIndex = g_BoneAccessStack.Count() - 1;
	if ( lastIndex < 0 )
	{
		Assert( !"SourceSharp_BoneAccessPop:  Stack is empty!!!" );
		return;
	}
	g_BoneAcessBase = g_BoneAccessStack[ lastIndex ];
	g_BoneAccessStack.Remove( lastIndex );
}

//-----------------------------------------------------------------------------
// Stock's `C_BaseAnimating::IsBoneAccessAllowed()` was
//
//     if ( IsViewModel() ) return ...ForViewModels; else return ...ForNormalModels;
//
// and the ONLY thing the entity contributed was that one bool. So the bool is
// the argument, and this function is entity-free.
//-----------------------------------------------------------------------------
bool SourceSharp_BoneAccessAllowed( bool bViewModel )
{
	if ( bViewModel )
		return g_BoneAcessBase.bAllowBoneAccessForViewModels;
	else
		return g_BoneAcessBase.bAllowBoneAccessForNormalModels;
}

//-----------------------------------------------------------------------------
// THE MODEL-BONE GENERATION COUNTER — stock's `g_iModelBoneCounter`.
//-----------------------------------------------------------------------------
static unsigned long g_iModelBoneCounter = 0;

void SourceSharp_InvalidateBoneCaches()
{
	g_iModelBoneCounter++;
}

unsigned long SourceSharp_ModelBoneCounter()
{
	return g_iModelBoneCounter;
}

//-----------------------------------------------------------------------------
// THE BONE-SETUP THREAD POOL.
//
// Both bodies are empty in stock and both are kept. The pair is not dead code:
// the client DLL calls them at start-up and shut-down by name, and an empty body
// is the record that the pool the name refers to is the task system's, owned
// somewhere else entirely. Deleting them would turn a documented no-op into a
// missing call nobody could find again.
//-----------------------------------------------------------------------------
void SourceSharp_InitBoneSetupThreadPool()
{
}

void SourceSharp_ShutdownBoneSetupThreadPool()
{
}

//-----------------------------------------------------------------------------
// THE TWO PER-FRAME SWEEPS.
//
// A FIXED ARRAY AND NOT A CUtlVector, on purpose. Registration happens during
// STATIC INITIALISATION from whichever translation units own animating things,
// and a CUtlVector is dynamically initialised — so a registration running before
// the vector's constructor would be silently lost, and lost registrations are
// invisible from the outside. A zero-initialised POD array has no constructor to
// lose a race against. `SourceSharp_BoneSweepCount()` exists so a test can see
// what did register.
//
// The cap is deliberately small. Two registrants are expected during the
// transition — stock's `C_BaseAnimating` and the managed hierarchy's shell — and
// overflowing it is an Assert rather than a growth, because a third would mean
// something registered twice.
//-----------------------------------------------------------------------------
enum { kMaxBoneSweeps = 4 };

static SourceSharpBoneSweepFn g_BoneSweeps[ kBoneSweep_Count ][ kMaxBoneSweeps ];
static int g_nBoneSweeps[ kBoneSweep_Count ];

void SourceSharp_RegisterBoneSweep( SourceSharpBoneSweep_t which, SourceSharpBoneSweepFn pfn )
{
	Assert( which >= 0 && which < kBoneSweep_Count );
	Assert( pfn != NULL );
	if ( which < 0 || which >= kBoneSweep_Count || !pfn )
		return;

	Assert( g_nBoneSweeps[ which ] < kMaxBoneSweeps );
	if ( g_nBoneSweeps[ which ] >= kMaxBoneSweeps )
		return;

	// Registering the same sweep twice would run it twice, which for the bone
	// setup queue means a second parallel pass over an already-emptied queue and
	// for the animation list means a double cycle advance. Both are silent.
	for ( int i = 0; i < g_nBoneSweeps[ which ]; ++i )
	{
		if ( g_BoneSweeps[ which ][ i ] == pfn )
		{
			Assert( !"SourceSharp_RegisterBoneSweep: already registered" );
			return;
		}
	}

	g_BoneSweeps[ which ][ g_nBoneSweeps[ which ]++ ] = pfn;
}

int SourceSharp_BoneSweepCount( SourceSharpBoneSweep_t which )
{
	if ( which < 0 || which >= kBoneSweep_Count )
		return 0;

	return g_nBoneSweeps[ which ];
}

static void RunBoneSweeps( SourceSharpBoneSweep_t which )
{
	const int nCount = g_nBoneSweeps[ which ];
	for ( int i = 0; i < nCount; ++i )
	{
		g_BoneSweeps[ which ][ i ]();
	}
}

void SourceSharp_ThreadedBoneSetup()
{
	RunBoneSweeps( kBoneSweep_ThreadedBoneSetup );
}

void SourceSharp_UpdateClientSideAnimations()
{
	RunBoneSweeps( kBoneSweep_ClientSideAnimations );
}

// Lane `f-block`, client family 9. The header carries the ruling. Same shape as
// the two above: the definition is HERE, in a file that stays, and the body is
// registered from the file that leaves.
void SourceSharp_ClientBoneSetupLevelShutdown()
{
	RunBoneSweeps( kBoneSweep_LevelShutdown );
}
