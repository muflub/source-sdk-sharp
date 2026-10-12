//========= SourceSharp — the registry a de-entified effect needs ===========//
//
// WHAT THIS REPLACES, AND WHY IT IS NOT A CONVENIENCE.
//
// Every `_render` shell in the port keeps a list of "this managed entity owns
// that client-side effect", and every one of them used to hold a CHandle. A
// CHandle is a guarantee as much as a pointer: the moment the engine destroys
// the entity behind it, every holder's copy reads as null of its own accord, so
// a stale row announced itself and the sweep that found it just dropped it.
//
// De-entifying the effects (shell_particle_effect.h) took that guarantee away.
// A CShellParticleEffect is not in the entity list, has no handle, and a raw
// pointer to one never goes null by itself. What replaces it is the effect
// telling the registry from its own destructor — the one place that hears both
// destroy routes, Release() from the shell and NotifyRemove() from CParticleMgr
// — and the registry removing the row.
//
// That inverts a rule the old shells were written against, and getting the
// inversion wrong is silent both ways:
//
//   * a caller that Release()s AND THEN Remove()s the index deletes a
//     NEIGHBOUR'S row, because the release already shortened the list. The
//     neighbour's effect is then pointed at by nothing and freed by nobody.
//   * a caller that Release()s and does NOT rely on the callback leaves a row
//     holding a freed pointer, which the next sweep dereferences.
//
// So the three operations that have to agree live here, once, with a test that
// drives them, rather than four times over in four `_render` shells.
//
// Header-only and engine-free — templated on the CONTAINER, so the game passes
// a CUtlVector of its own row struct and shell_effect_registry_test.cpp passes
// a std::vector of its own. The only thing required of a row is a member
// `hOwner` and a member `pEffect`.
//
//=============================================================================//

#ifndef SOURCESHARP_SHELL_EFFECT_REGISTRY_H
#define SOURCESHARP_SHELL_EFFECT_REGISTRY_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// The row for an owner, or -1.
//
// FIRST match, deliberately. One managed entity owns at most one effect of a
// given family, so a second row for the same owner is a bug elsewhere; finding
// the first keeps the answer stable while that bug is being found, where a
// "last" or an "any" would make it intermittent.
//-----------------------------------------------------------------------------
template < typename TList >
inline int ShellEffects_Find( const TList &list, unsigned int hOwner )
{
	for ( int i = 0; i < list.Count(); i++ )
	{
		if ( list[i].hOwner == hOwner )
			return i;
	}

	return -1;
}

//-----------------------------------------------------------------------------
// An effect is being destroyed — drop the row that points at it.
//
// Called from the effect's DESTRUCTOR, through CShellParticleEffect's gone
// callback, so it hears every destroy route without having to know which one
// ran.
//
// Backwards, and every match rather than the first: a list that has somehow
// acquired two rows for one effect would otherwise be left holding a freed
// pointer in the second, which is the exact failure this whole mechanism exists
// to prevent. The walk is backwards so that removing a row cannot make the next
// step skip its neighbour.
//-----------------------------------------------------------------------------
template < typename TList, typename TEffect >
inline void ShellEffects_Forget( TList &list, const TEffect *pEffect )
{
	for ( int i = list.Count() - 1; i >= 0; i-- )
	{
		if ( list[i].pEffect == pEffect )
			list.Remove( i );
	}
}

//-----------------------------------------------------------------------------
// Release every effect in the list — a level change, where every managed owner
// is about to go and no other Release is coming.
//
// A `while ( Count() )` rather than a `for`, because Release() SHORTENS THE
// LIST ITSELF through the callback above and a loop cannot hold an index across
// a call that invalidates it.
//
// AND IT IS BOUNDED, which is the part worth having a test for. `while (
// Count() ) list[0].pEffect->Release()` is an infinite loop the day the gone
// callback is not hooked up — a one-line omission at the factory, in a file
// nowhere near this one, that turns a level change into a hang rather than into
// a leak. The guard notices that the list did not shrink and stops, so the
// symptom is a leak that a log line can describe instead of a frozen client.
//
// Answers how many it released, so the caller can tell the two outcomes apart.
// A NEGATIVE answer means it stopped early: -n where n is the number it did
// release before the list stopped shrinking.
//-----------------------------------------------------------------------------
template < typename TList >
inline int ShellEffects_ReleaseAll( TList &list )
{
	int nReleased = 0;

	while ( list.Count() )
	{
		const int nBefore = list.Count();

		list[0].pEffect->Release();

		if ( list.Count() >= nBefore )
			return -nReleased;

		nReleased++;
	}

	return nReleased;
}

#endif // SOURCESHARP_SHELL_EFFECT_REGISTRY_H
