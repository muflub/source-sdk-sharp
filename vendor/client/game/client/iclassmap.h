//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class C_BaseEntity;
typedef C_BaseEntity* (*DISPATCHFUNCTION)( void );

abstract_class IClassMap
{
public:
	virtual					~IClassMap() {}

	virtual void			Add( const char *mapname, const char *classname, int size, DISPATCHFUNCTION factory = 0 ) = 0;
	virtual void unused_Lookup() = 0;
	virtual C_BaseEntity	*CreateEntity( const char *mapname ) = 0;
	virtual void unused_GetClassSize() = 0;
};

extern IClassMap& GetClassMap();
