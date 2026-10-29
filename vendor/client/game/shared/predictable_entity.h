//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// For introspection

#include "predictioncopy.h"
#include "shared_classnames.h"

// CLIENT DLL includes

class SendTable;

// Game DLL includes

#define DECLARE_NETWORKCLASS() DECLARE_CLIENTCLASS()

#define DECLARE_NETWORKCLASS_NOBASE() DECLARE_CLIENTCLASS_NOBASE()

#define DECLARE_PREDICTABLE_IMPL(MAYBE_OVERRIDE)        \
public:                                                 \
	static typedescription_t m_PredDesc[];              \
	static datamap_t m_PredMap;                         \
	virtual datamap_t* GetPredDescMap() MAYBE_OVERRIDE; \
	template <typename T>                               \
	friend datamap_t* PredMapInit(T*)
#define DECLARE_PREDICTABLE() DECLARE_PREDICTABLE_IMPL(/* override */;)
// With warnings set to inconsistent-override, marking this properly as override would create warnings in all old files.
// Instead, as files are converted to use override, just upgrade to this

/* so you can define "empty" tables */

// On the client .dll this creates a mapping between a classname and
//  a client side class.  Probably could be templatized at some point.

#define LINK_ENTITY_TO_CLASS(localName, className)                                                                    \
	static C_BaseEntity* C##className##Factory()                                                                      \
	{                                                                                                                 \
		return static_cast<C_BaseEntity*>(new className);                                                             \
	};                                                                                                                \
	class C##localName##Foo                                                                                           \
	{                                                                                                                 \
	public:                                                                                                           \
		C##localName##Foo() { GetClassMap().Add(#localName, #className, sizeof(className), &C##className##Factory); } \
	};                                                                                                                \
	static C##localName##Foo g_C##localName##Foo;

#define BEGIN_NETWORK_TABLE_NOBASE(className, tableName) BEGIN_RECV_TABLE_NOBASE(className, tableName)

#define END_NETWORK_TABLE END_RECV_TABLE
