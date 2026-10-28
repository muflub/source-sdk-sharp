//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The game rules class. Decisions route into C#.
#pragma once

#ifdef CLIENT_DLL
#include "igamerules.h"
#else
#include "igamerules.h"
#include "networkvar.h"
#include "server_class.h"
#endif

#ifdef CLIENT_DLL
	#define CSourceSharpGameRules      C_SourceSharpGameRules
#endif

class CSourceSharpGameRules;

// Implements IGameRules directly; g_pGameRules is set and cleared by the constructor
// and destructor.
class CSourceSharpGameRules : public IGameRules
{
public:
	DECLARE_CLASS_NOBASE( CSourceSharpGameRules );
#ifdef CLIENT_DLL
	DECLARE_NETWORKCLASS_NOBASE();
#endif

	CSourceSharpGameRules();
	virtual ~CSourceSharpGameRules();

	// Overridden only to drop the base's !IsMultiplayer() assert.
	virtual bool IsConnectedUserInfoChangeAllowed( CBasePlayer *pPlayer ) OVERRIDE { return true; }

	// The damage-type masks are C#'s policy, pushed once. Only these two are reached
	// through the rules pointer.
	virtual int  Damage_GetTimeBased( void ) OVERRIDE;
	virtual int  Damage_GetNoPhysicsForce( void ) OVERRIDE;

	virtual bool IsMultiplayer( void ) OVERRIDE;

	// Stock CGameRules' answers.
	virtual bool AllowThirdPersonCamera( void ) OVERRIDE { return false; }
	virtual void ClientSpawned( edict_t *pPlayer ) OVERRIDE { return; }
	virtual void OnFileReceived( const char *fileName, unsigned int transferID ) OVERRIDE { return; }
	virtual bool ShouldDrawHeadLabels( void ) OVERRIDE { return true; }

	// Declared in IGameRules' shared arm, so both sides need them.
	virtual bool Init( void ) OVERRIDE { return true; }
	virtual void ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) OVERRIDE {}

	virtual const RulesViewVectors_t *GetViewVectors( void ) const OVERRIDE;

	// Not an IGameRules member: nothing calls it through the rules pointer.
	virtual bool ShouldUseRobustRadiusDamage( IGameEntity *pEntity ) { return false; }
	virtual bool  ShouldCollide( int collisionGroup0, int collisionGroup1 ) OVERRIDE;

#ifndef CLIENT_DLL
	virtual bool  ClientConnected( edict_t *pEntity, const char *pszName,
	                               const char *pszAddress, char *reject, int maxrejectlen ) OVERRIDE;

	// Empty: player_manager is a managed entity, created by the server module at level
	// init. The base would create it through CBaseEntity::Create, which fails for one.
	virtual void CreateStandardEntities( void ) OVERRIDE {}

	// The last hook in a frame, after both the dev API queue and the entity I/O queue,
	// so the dirty-word drain runs again to announce writes made by either.
	virtual void EndGameFrame( void ) OVERRIDE;

	// Required by IGameRules and unreachable in this build; they answer stock's values.
	virtual float AdjustPlayerDamageInflicted( float damage ) OVERRIDE { return damage; }
	virtual void  AdjustPlayerDamageTaken( RulesDamageInfo_t *pInfo ) OVERRIDE {}
	virtual void  CreateCustomNetworkStringTables( void ) OVERRIDE {}
	virtual void  InitDefaultAIRelationships( void ) OVERRIDE { return; }
	virtual void  Precache( void ) OVERRIDE { return; }
	virtual void  ResetMapCycleTimeStamp( void ) OVERRIDE { return; }
	virtual void  Status( void (*print) (const char *fmt, ...) ) OVERRIDE {}
#else
	// The two the client half of IGameRules declares.
	virtual bool AllowMapVisionFilterShaders( void ) OVERRIDE { return false; }
	virtual const char *TranslateEffectForVisionFilter( const char *pchEffectType,
	                                                    const char *pchEffectName ) OVERRIDE
	{
		return pchEffectName;
	}
#endif

};

// Makes the one rules object and tells the client to make its own.
#ifndef CLIENT_DLL
void SourceSharp_CreateGameRules();
#endif

inline CSourceSharpGameRules *SourceSharpGameRules()
{
	return static_cast< CSourceSharpGameRules * >( g_pGameRules );
}
