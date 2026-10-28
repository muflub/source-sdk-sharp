//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The top of a move-parent chain, walked inline over GetMoveParent() on either side.
#pragma once

#ifndef NULL
#define NULL 0
#endif

// The top of pEntity's move-parent chain: pEntity itself when it has no parent, NULL
// only for NULL. Unlike stock the walk is bounded, so a parent cycle gives a wrong
// answer instead of a hang. Real chains are two or three deep.
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
