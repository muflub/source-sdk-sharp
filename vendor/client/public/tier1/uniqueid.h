//========= Copyright Valve Corporation, All rights reserved. ============//
// Utilities for globally unique IDs

#pragma once

#include "tier1/utlvector.h"

// Forward declarations
struct UniqueId_t;
class CUtlBuffer;

// Defines a globally unique ID
struct UniqueId_t
{
	unsigned char m_Value[16];
};

void InvalidateUniqueId( UniqueId_t *pDest );
bool IsUniqueIdEqual( const UniqueId_t &id1, const UniqueId_t &id2 );
bool UniqueIdFromString( UniqueId_t *pDest, const char *pBuf, int nMaxLen = 0 );
void CopyUniqueId( const UniqueId_t &src, UniqueId_t *pDest );
