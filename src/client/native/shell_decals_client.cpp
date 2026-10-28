//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

//========= SourceSharp — the client's decalsystem, a forward to C# ==========//
//
// plan_client_novalve cluster 18 (lane c2-sims). game/shared/decals.cpp's
// CDecalEmitterSystem left client.so; its tables, its weighted pick and its
// game-material translation are SourceSharp.Client's ClientDecals (over the
// DecalScript the server's DecalEmitter shares). What stays is the binding:
//
//   * IDecalEmitterSystem, the vtable the client's two callers reach through
//     `decalsystem` (shell_impact_effects.cpp, shell_te_backends_client.cpp),
//     unedited -- each method one forward to IManagedDecalsClient;
//   * the kClientSystems row (shell_clientsystems.cpp) whose Init, Shutdown
//     and LevelInitPreEntity are stock's three steps, each one forward.
//
// The two name answers are copied into members, as the server's shell_decals
// does: IDecalEmitterSystem returns `const char *`, and a pointer into managed
// memory is not one native may hold.
//
//=============================================================================//

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

// memdbgon must be the last include file in a .cpp file!!!


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

// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_DecalEmitter() { return &g_DecalSystem; }
IDecalEmitterSystem *decalsystem = &g_DecalSystem;
