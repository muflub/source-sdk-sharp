//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The helper bases the SDK's point, logical and server-only entities derive from. No
// includes: the one includer supplies IGameEntity, and the tests include this inside a
// namespace.
#pragma once

class CPointEntity : public IGameEntity
{
public:
	DECLARE_CLASS( CPointEntity, IGameEntity );

	void	Spawn();

	// 0: CBaseEntity::ObjectCaps can only be non-zero for a parented entity, and this
	// hierarchy has no parent to forward from.
	virtual int	ObjectCaps() { return 0; }
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
private:
};

// Has a position + size
class CServerOnlyEntity : public IGameEntity
{
	DECLARE_CLASS( CServerOnlyEntity, IGameEntity );
public:
	// CBaseEntity's bServerOnly only set EFL_SERVER_ONLY, which this hierarchy has no use for.
	CServerOnlyEntity() {}

	virtual int ObjectCaps() { return 0; }
};

// Has only a position, no size
class CServerOnlyPointEntity : public CServerOnlyEntity
{
	DECLARE_CLASS( CServerOnlyPointEntity, CServerOnlyEntity );

public:
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
};

// Has no position or size
class CLogicalEntity : public CServerOnlyEntity
{
	DECLARE_CLASS( CLogicalEntity, CServerOnlyEntity );

public:
	virtual bool KeyValue( const char *szKeyName, const char *szValue );
};

// None of these four declares a datadesc, so a derived BEGIN_DATADESC would otherwise
// pick the template DataMapAccess and name a CBaseEntity::m_DataMap that does not exist.
// They contribute no save fields, so the chain ends here with NULL. datamap_t is only
// forward-declared, matching public/datamap.h.

struct datamap_t;

inline void DataMapAccess( CPointEntity *, datamap_t **p )			{ *p = NULL; }
inline void DataMapAccess( CServerOnlyEntity *, datamap_t **p )		{ *p = NULL; }
inline void DataMapAccess( CServerOnlyPointEntity *, datamap_t **p ){ *p = NULL; }
inline void DataMapAccess( CLogicalEntity *, datamap_t **p )		{ *p = NULL; }
