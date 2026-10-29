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
#define CSourceSharpGameRules C_SourceSharpGameRules
#endif

class CSourceSharpGameRules;
#ifndef CLIENT_DLL
struct CManagedAreas;
#endif

// Implements IGameRules directly. On the client g_pGameRules is set and cleared by the
// constructor and destructor; the server's world owns its rules object.
class CSourceSharpGameRules : public IGameRules
{
public:
	DECLARE_CLASS_NOBASE( CSourceSharpGameRules );
#ifdef CLIENT_DLL
	DECLARE_NETWORKCLASS_NOBASE();
#endif

#ifndef CLIENT_DLL
	// managed is the load's areas, read when a client connects.
	explicit CSourceSharpGameRules( const CManagedAreas &managed );
#else
	CSourceSharpGameRules();
#endif
	virtual ~CSourceSharpGameRules();

	// Overridden only to drop the base's !IsMultiplayer() assert.
	virtual bool IsConnectedUserInfoChangeAllowed( CBasePlayer *pPlayer ) OVERRIDE { return true; }

	// The damage-type masks are C#'s policy, pushed once. Only these two are reached
	// through the rules pointer.
	virtual int Damage_GetTimeBased() OVERRIDE;
	virtual int Damage_GetNoPhysicsForce() OVERRIDE;

	virtual bool IsMultiplayer() OVERRIDE;

	// Stock CGameRules' answers.
	virtual bool AllowThirdPersonCamera() OVERRIDE { return false; }

	virtual void ClientSpawned( edict_t *pPlayer ) OVERRIDE { return; }

	virtual void OnFileReceived( const char *fileName, unsigned int transferID ) OVERRIDE { return; }

	virtual bool ShouldDrawHeadLabels() OVERRIDE { return true; }

	// Declared in IGameRules' shared arm, so both sides need them.
	virtual bool Init() OVERRIDE { return true; }

	virtual void ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) OVERRIDE {}

	virtual const RulesViewVectors_t *GetViewVectors() const OVERRIDE;

	// Not an IGameRules member: nothing calls it through the rules pointer.
	virtual bool ShouldUseRobustRadiusDamage( IGameEntity *pEntity ) { return false; }

#ifndef CLIENT_DLL
	virtual bool ClientConnected( edict_t *pEntity, const char *pszName, const char *pszAddress, char *reject,
								  int maxrejectlen ) OVERRIDE;

	// Empty: player_manager is a managed entity, created by the server module at level
	// init. The base would create it through CBaseEntity::Create, which fails for one.
	virtual void CreateStandardEntities() OVERRIDE {}

	// The last hook in a frame, after both the dev API queue and the entity I/O queue,
	// so the dirty-word drain runs again to announce writes made by either.
	virtual void EndGameFrame() OVERRIDE;

	// Required by IGameRules and unreachable in this build; they answer stock's values.
	virtual float AdjustPlayerDamageInflicted( float damage ) OVERRIDE { return damage; }

	virtual void AdjustPlayerDamageTaken( RulesDamageInfo_t *pInfo ) OVERRIDE {}

	virtual void CreateCustomNetworkStringTables() OVERRIDE {}

	virtual void InitDefaultAIRelationships() OVERRIDE { return; }

	virtual void Precache() OVERRIDE { return; }

	virtual void ResetMapCycleTimeStamp() OVERRIDE { return; }

	virtual void Status( void ( *print )( const char *fmt, ... ) ) OVERRIDE {}
#else
	// The two the client half of IGameRules declares.
	virtual bool AllowMapVisionFilterShaders() OVERRIDE { return false; }

	virtual const char *TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName ) OVERRIDE
	{
		return pchEffectName;
	}
#endif

#ifndef CLIENT_DLL
private:
	const CManagedAreas &m_Managed;
#endif
};

// Makes the level's rules object, which the world owns, and tells the client through the
// rules' string table to make its own.
#ifndef CLIENT_DLL
class INetworkStringTable;
class INetworkStringTableContainer;
IGameRules *SourceSharp_CreateGameRules( INetworkStringTable *pRulesTable, const CManagedAreas &managed );
INetworkStringTable *CreateNetworkStringTables_GameRules( INetworkStringTableContainer *pTables );
#endif
