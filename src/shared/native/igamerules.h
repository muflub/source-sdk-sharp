//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// What the game asks of the game rules: the CGameRules methods that are actually
// called, with gamerules.h's exact signatures and its CLIENT_DLL split.

#pragma once

// Self-contained: gamerules.h includes this header.
#include "tier0/platform.h"
#include "mathlib/vector.h"
#include "edict.h"

// Holds the guarded CBaseEntity forward declaration.
#include "igameentity.h"

class CBasePlayer;
class CItem;

// The damage info type, per side.
#ifdef CLIENT_DLL
class CTakeDamageInfo;
typedef CTakeDamageInfo RulesDamageInfo_t;
#else
namespace ss { class CTakeDamageInfo; }
typedef ss::CTakeDamageInfo RulesDamageInfo_t;
#endif

// The view vectors type, per side; the server's is complete because shareddefs.h's
// VEC_* macros dereference it.
#ifdef CLIENT_DLL
class CViewVectors;
typedef CViewVectors RulesViewVectors_t;
#else
#include "ss_viewvectors.h"
typedef ss::CViewVectors RulesViewVectors_t;
#endif
class CCommand;
class KeyValues;
class ConVarRef;
class IHandleEntity;
class CGameRules;
class CBaseCombatWeapon;
class CBaseCombatCharacter;
class CAmmoDef;

abstract_class IGameRules
{
public:
	virtual ~IGameRules() {}

	// IGameSystem's, not CGameRules'; gamerules.h forwards it explicitly.
	virtual bool Init() = 0;

	// Both sides.
	virtual bool AllowThirdPersonCamera() = 0;
	virtual void ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) = 0;
	virtual void ClientSpawned( edict_t * pPlayer ) = 0;
	virtual int Damage_GetNoPhysicsForce() = 0;
	virtual int Damage_GetTimeBased() = 0;
	virtual const RulesViewVectors_t* GetViewVectors() const = 0;
	virtual bool IsConnectedUserInfoChangeAllowed( CBasePlayer *pPlayer ) = 0;
	virtual bool IsMultiplayer() = 0;
	virtual void OnFileReceived( const char * fileName, unsigned int transferID ) = 0;
	virtual bool ShouldCollide( int collisionGroup0, int collisionGroup1 ) = 0;
	virtual bool ShouldDrawHeadLabels() = 0;

#ifndef CLIENT_DLL
	// Server only.
	virtual float AdjustPlayerDamageInflicted( float damage ) = 0;
	virtual void AdjustPlayerDamageTaken( RulesDamageInfo_t *pInfo ) = 0;
	virtual bool ClientConnected( edict_t *pEntity, const char *pszName, const char *pszAddress, char *reject, int maxrejectlen ) = 0;
	virtual void CreateCustomNetworkStringTables() = 0;
	virtual void CreateStandardEntities() = 0;
	virtual void EndGameFrame() = 0;
	virtual void InitDefaultAIRelationships() = 0;
	virtual void Precache() = 0;
	virtual void ResetMapCycleTimeStamp() = 0;
	virtual void Status( void (*print) (const char *fmt, ...) ) = 0;
#else
	// Client only.
	virtual bool AllowMapVisionFilterShaders() = 0;
	virtual const char* TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName ) = 0;
#endif // !CLIENT_DLL
};

// NULL between levels, so test it before calling.
extern IGameRules *g_pGameRules;

inline IGameRules *GameRules() { return g_pGameRules; }

// Builds the rules object for a new level.
void InstallGameRules();
