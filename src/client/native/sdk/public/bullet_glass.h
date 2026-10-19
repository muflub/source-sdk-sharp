//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — when a bullet carries on through glass =============//
//
// One rule, asked of whatever a bullet hit, on whichever hierarchy holds it.
//
// This is stock's rule and nothing else: `CBaseEntity::FireBullets` lets a round
// continue past a `func_breakable` whose struck surface is glass, unless the
// mapper set SF_BREAK_NO_BULLET_PENETRATION. It is out here rather than inline
// because there are now TWO kinds of thing a trace can return — a CBaseEntity
// and an IGameEntity — and a rule written twice is a rule that will be answered
// two different ways the first time either copy is touched.
//
// The other reason is that the inline version was a crash. It guarded on
// `tr.m_pEnt`, which the engine fills for anything it can trace against, and
// then dereferenced `TraceEntity( tr )`, which is NULL for everything that is
// not a CBaseEntity. Those are two different questions and the second answers
// NULL exactly when the first says yes. Shooting the skylight on `dm_lockdown`
// segfaulted the server, every time, from normal play. Taking a CLASSNAME here
// rather than an entity is what makes that shape impossible: a caller with
// nothing to name what it hit has to pass NULL, and NULL has an answer.
//
// In public/ for the same reason igameentity.h is: stock game code includes it,
// in every configuration, client and server alike.
//
// No engine headers, so it is a gtest away from a running Source — see
// sourcesharp/test/bullet_glass_test.cpp.
//
//=============================================================================//
#ifndef BULLET_GLASS_H
#define BULLET_GLASS_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Case-insensitive ASCII compare, equality only.
//
// Not Q_stricmp: that is tier1, and dragging tier1 into a unit test to compare
// two classnames is how a test stops being a test of one thing. Not shared with
// StandaloneNameEquals in game/server/standalone_names.h either, deliberately —
// this header is on the STOCK include path and reaching from there into the
// shell's private headers would invert the dependency for eight lines.
//
// Case matters because stock's own comparison is case-insensitive:
// ClassMatches goes to NamesMatch, and a map that spells a classname with a
// capital in it has always still matched.
//-----------------------------------------------------------------------------
inline bool BulletGlassNameEquals( const char *pA, const char *pB )
{
	if ( !pA || !pB )
		return false;

	for ( ; *pA && *pB; pA++, pB++ )
	{
		char a = *pA;
		char b = *pB;

		if ( a >= 'A' && a <= 'Z' ) a = (char)( a - 'A' + 'a' );
		if ( b >= 'A' && b <= 'Z' ) b = (char)( b - 'A' + 'a' );

		if ( a != b )
			return false;
	}

	return *pA == *pB;
}

//-----------------------------------------------------------------------------
// Whether a round that just landed should be re-fired from behind what it hit.
//
//   bGlassSurface     the struck surface's material is CHAR_TEX_GLASS
//   pszHitClass       what was hit, or NULL when NOTHING could name it — a
//                     static prop, or an entity on a hierarchy that has no way
//                     to answer. NULL is not an error and not a crash; it is
//                     "not a func_breakable", which is what such a thing looked
//                     like before it could be hit at all.
//   nHitSpawnFlags    its spawnflags, ignored unless the classname matched
//   nNoPenetrationBit SF_BREAK_NO_BULLET_PENETRATION, passed in so this header
//                     needs no game header to name it
//
// EXACT on the classname, because stock's literal is exact: "func_breakable"
// carries no wildcard, so `func_breakable_surf` has never been penetrable and
// is not made so here.
//-----------------------------------------------------------------------------
inline bool BulletPassesThroughGlass( bool bGlassSurface, const char *pszHitClass,
                                      int nHitSpawnFlags, int nNoPenetrationBit )
{
	if ( !bGlassSurface )
		return false;

	if ( !BulletGlassNameEquals( pszHitClass, "func_breakable" ) )
		return false;

	return ( nHitSpawnFlags & nNoPenetrationBit ) == 0;
}

#endif // BULLET_GLASS_H
