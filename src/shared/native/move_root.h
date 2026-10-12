//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the top of a move-parent chain, walked inline =======//
//
// `CBaseEntity::GetRootMoveParent()` / `C_BaseEntity::GetRootMoveParent()` are
// out-of-line members defined in `baseentity_shared.cpp` and
// `c_baseentity.cpp`, both of which LEAVE both vpcs in the client flip. Three
// files that STAY called one of them:
//
//     game/shared/util_shared.cpp     the trace filter's sibling exclusion
//     game/shared/collisionproperty.cpp
//     game/client/physics.cpp
//
// The BODY does not need the class. It is a walk over `GetMoveParent()`, which
// is INLINE on both sides (`baseentity.h:2328`, `c_baseentity.h:2206`) and so
// emits no symbol of its own. So the walk comes to a header, and the three
// callers keep the answer they had.
//
// ONE DELIBERATE DIFFERENCE FROM STOCK: THE WALK IS BOUNDED.
//
//     stock:  while ( pParent ) { pEntity = pParent; pParent = pEntity->GetMoveParent(); }
//
// A move-parent CYCLE hangs that loop, inside a trace filter, inside the
// physics callback — the process stops with no output. Stock is not wrong to
// omit the bound (its hierarchy code refuses to make a cycle), but this build
// has TWO hierarchies and only one of them is stock's; a bound costs an
// integer compare per step and turns a hang into a wrong answer, and a wrong
// answer can be seen.
//
// `nMaxDepth` is generous on purpose: real move chains in Source are two or
// three deep (a weapon on a hand on a player), and the bound exists for the
// cycle, not to police depth.
//
// See `sourcesharp/test/move_root_test.cpp`.
//
//=============================================================================//
#ifndef SOURCESHARP_MOVE_ROOT_H
#define SOURCESHARP_MOVE_ROOT_H

#ifndef NULL
#define NULL 0
#endif

//-----------------------------------------------------------------------------
// The top of pEntity's move-parent chain. Answers pEntity itself when it has no
// parent -- which is stock's answer and is what makes "same root" a usable
// sibling test -- and NULL only for a NULL argument.
//
// `TEntity` is anything with `GetMoveParent()`: CBaseEntity / C_BaseEntity in
// the build, a stub chain in the test.
//-----------------------------------------------------------------------------
template < typename TEntity >
inline TEntity *SourceSharpRootMoveParent( TEntity *pEntity, int nMaxDepth = 64 )
{
	if ( !pEntity )
		return NULL;

	TEntity *pRoot = pEntity;
	TEntity *pParent = pEntity->GetMoveParent();
	int nDepth = 0;

	while ( pParent && nDepth < nMaxDepth )
	{
		pRoot = pParent;
		pParent = pRoot->GetMoveParent();
		nDepth++;
	}

	return pRoot;
}

#endif // SOURCESHARP_MOVE_ROOT_H
