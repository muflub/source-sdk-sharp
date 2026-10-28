//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

// The client's IDecalEmitterSystem (`decalsystem`), each method one forward to
// IManagedDecalsClient, and its client-system row. Name answers are copied into
// members: native may not hold a pointer into managed memory.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "decals.h"
#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"

class CShellDecalSystemClient : public IDecalEmitterSystem, public CClientSystem
{
public:
	CShellDecalSystemClient() : CClientSystem( "CDecalEmitterSystem" )
	{
		m_szName[ 0 ] = '\0';
		m_szTranslated[ 0 ] = '\0';
	}

	virtual bool Init()
	{
		if ( IManagedDecalsClient *pArea = ManagedDecalsClient() )
			pArea->Init();
		return true;
	}

	virtual void Shutdown()
	{
		if ( IManagedDecalsClient *pArea = ManagedDecalsClient() )
			pArea->Shutdown();
	}

	virtual void LevelInitPreEntity()
	{
		if ( IManagedDecalsClient *pArea = ManagedDecalsClient() )
			pArea->LevelInitPreEntity();
	}

	virtual int GetDecalIndexForName( char const *decalname )
	{
		IManagedDecalsClient *pArea = ManagedDecalsClient();
		if ( !pArea || !decalname || !decalname[ 0 ] )
			return -1;
		return pArea->IndexForName( decalname );
	}

	// "" for an index no entry carries, as stock answers.
	virtual const char *GetDecalNameForIndex( int nIndex )
	{
		m_szName[ 0 ] = '\0';
		IManagedDecalsClient *pArea = ManagedDecalsClient();
		if ( pArea && pArea->NameForIndex( nIndex, m_szName, sizeof( m_szName ) ) < 0 )
			m_szName[ 0 ] = '\0';
		return m_szName;
	}

	// The caller's own pointer when managed has no other answer, as stock
	// returns `decalName`; "" is a real answer (the material is not decalled).
	virtual char const *TranslateDecalForGameMaterial( char const *decalName, unsigned char gamematerial )
	{
		IManagedDecalsClient *pArea = ManagedDecalsClient();
		if ( !pArea || !decalName )
			return decalName;
		if ( pArea->TranslateForGameMaterial( decalName, (int32_t)gamematerial, m_szTranslated, sizeof( m_szTranslated ) ) < 0 )
			return decalName;
		return m_szTranslated;
	}

private:
	char m_szName[ 260 ];
	char m_szTranslated[ 260 ];
};

static CShellDecalSystemClient g_DecalSystem;

CClientSystem *ClientSystem_DecalEmitter() { return &g_DecalSystem; }
IDecalEmitterSystem *decalsystem = &g_DecalSystem;
