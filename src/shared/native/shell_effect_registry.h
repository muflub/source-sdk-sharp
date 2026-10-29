// Rows of "this managed entity owns that client-side effect". An effect's destructor
// removes its own row through the gone callback, so a caller that Release()s must not
// also remove the row. Templated on the container; a row needs hOwner and pEffect.

#pragma once

// The first row for an owner, or -1. An owner has at most one effect per family.
template <typename TList>
inline int ShellEffects_Find( const TList &list, unsigned int hOwner )
{
	for ( int i = 0; i < list.Count(); i++ )
	{
		if ( list[i].hOwner == hOwner )
			return i;
	}

	return -1;
}

// Drops every row pointing at a destroyed effect. Called from the effect's destructor;
// walks backwards so a removal cannot skip a neighbour.
template <typename TList, typename TEffect>
inline void ShellEffects_Forget( TList &list, const TEffect *pEffect )
{
	for ( int i = list.Count() - 1; i >= 0; i-- )
	{
		if ( list[i].pEffect == pEffect )
			list.Remove( i );
	}
}

// Releases every effect, at a level change. Release() shortens the list through the
// gone callback, so the loop stops if the list fails to shrink rather than hang.
// Returns the number released, negated when it stopped early.
template <typename TList>
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
