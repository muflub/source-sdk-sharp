//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//=============================================================================//

#ifndef OBJECT_HASH_H
#define OBJECT_HASH_H
#ifdef _WIN32
#pragma once
#endif

class IPhysicsObjectPairHash
{
public:
	virtual ~IPhysicsObjectPairHash() {}
	virtual void unused_AddObjectPair() = 0;
	virtual void unused_RemoveObjectPair() = 0;
	virtual void unused_IsObjectPairInHash() = 0;
	virtual void unused_RemoveAllPairsForObject() = 0;
	virtual void unused_IsObjectInHash() = 0;

	// Used to iterate over all pairs an object is part of
	virtual void unused_GetPairCountForObject() = 0;
	virtual void unused_GetPairListForObject() = 0;
};


#endif // OBJECT_HASH_H
