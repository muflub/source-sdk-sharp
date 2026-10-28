//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// What stands between a blast and its victim, and how much of the blast it
// keeps. The rule is stock's, out of CGameRules::RadiusDamage; this is where it
// is written down so it can be pinned by a test, and so BOTH hierarchies go
// through the same arithmetic rather than through two copies that can drift.
//
// The rule, checked against gamerules.cpp rather than remembered:
//
//   the trace to the victim stopped on something
//   that something is not the victim
//   nor is it owned by the victim          -> it is in the way
//
//   in the way, and physics                -> it keeps mass/350 of the damage,
//                                             and all of it at 350kg or more
//   in the way, and not physics            -> it keeps a flat quarter
//
// WHY IT IS A HEADER AND NOT THREE LINES WHERE IT WAS. The first clause used to
// be spelled `TraceEntity( tr ) != NULL`, which resolves an IHandleEntity to a
// CBaseEntity and answers NULL for everything else — so an entity on the
// parallel hierarchy standing squarely in the blast was not "in the way", and
// the whole rule was skipped for it. A managed crate shielded nothing at all,
// silently, while an SDK crate of the same mass shielded a quarter of its
// weight. Stating the rule over IDENTITIES rather than over one hierarchy's
// pointer type is the fix; putting it here is what stops the two answers being
// written twice.
//
// No engine headers, deliberately, for the reason collideable_center_math.h and
// collision_group_names.h are shaped the same way: the arithmetic is the part
// that can be silently wrong, and a test binary must be able to reach it
// without a running Source. IHandleEntity is forward declared and only ever
// compared, never dereferenced.
//
// gtest: sourcesharp/test/blast_blocking_test.cpp.
//
//=============================================================================//
#pragma once

class IHandleEntity;

//-----------------------------------------------------------------------------
// A blocker heavy enough to keep the whole blast to itself, in kilogrammes.
// Stock's MASS_ABSORB_ALL_DAMAGE, named here because the ratio below is the
// only thing that reads it.
//-----------------------------------------------------------------------------
const float BLAST_MASS_ABSORB_ALL = 350.0f;

//-----------------------------------------------------------------------------
// What something the solver does not move keeps. Stock's flat quarter: it has
// no mass to weigh, so there is no ratio to take.
//-----------------------------------------------------------------------------
const float BLAST_BLOCKED_NO_PHYSICS = 0.25f;

//-----------------------------------------------------------------------------
// Whether what the trace stopped on is in the way.
//
// pVictim      the entity the blast is trying to reach. Never NULL — a sweep
//              only asks this about something it already found.
// pBlocker     what the trace stopped on, as the handle the trace itself
//              carries. NULL when it stopped on the world, on a static prop, or
//              on anything that is not an entity: none of those reach this rule,
//              because the world is handled by the CONTENTS_SOLID retrace above
//              it and a static prop cannot be hurt or own anything.
// pBlockerOwner  who owns the blocker, or NULL. A thing owned by the victim does
//              not shield it — stock's "shouldn't let children block parents".
//
// Compared as handles throughout, which is the point: trace_t::m_pEnt is an
// IHandleEntity and so is the victim's identity, so the comparison is exact for
// an entity of either hierarchy. The version that resolved the blocker to a
// CBaseEntity first could only ever see one kind.
//-----------------------------------------------------------------------------
inline bool BlastBlockerCounts( const IHandleEntity *pVictim,
                                const IHandleEntity *pBlocker,
                                const IHandleEntity *pBlockerOwner )
{
	if ( !pBlocker )
		return false;

	if ( pBlocker == pVictim )
		return false;

	if ( pBlockerOwner && pBlockerOwner == pVictim )
		return false;

	return true;
}

//-----------------------------------------------------------------------------
// The share of the blast a blocker keeps for itself, as a fraction of what
// would otherwise have landed.
//
// 1.0 or more means it kept everything and the victim takes nothing. The caller
// tests for that rather than this clamping, because "absorbed it all" and
// "absorbed all but a rounding error" are different outcomes there: the first
// skips the victim entirely, forces and sounds included.
//
// bHasPhysics  whether the solver moves the blocker. A prop with motion
//              disabled still HAS an object and still answers its mass, which
//              is right: a pinned crate weighs what it weighs.
// flMass       that object's mass in kilogrammes; ignored without one.
//-----------------------------------------------------------------------------
inline float BlastBlockedFraction( bool bHasPhysics, float flMass )
{
	if ( !bHasPhysics )
		return BLAST_BLOCKED_NO_PHYSICS;

	return flMass / BLAST_MASS_ABSORB_ALL;
}
