//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp - what the game asks of the game rules ===============//
//
// The rules object reached the rest of the build as a CGameRules * -- a stock
// CLASS: 500 lines of header and 1,247 of implementation, deriving from
// CAutoGameSystemPerFrame, carrying a datadesc, a network table and a proxy
// entity. Anything that wanted to ask it a question had to name that class, so
// the class WAS the interface -- and the only way to ANSWER the game was to
// inherit the whole of it.
//
// THIS IS THE INTERFACE, TAKEN FROM WHAT ACTUALLY CALLS IN -- not from the
// class. Every "g_pGameRules->" and "GameRules()->" call site in the
// translation units the server and the client really compile was collected,
// and the ones CGameRules declares are below: 50 of them.
//
// SIXTY-EIGHT NAMES ARE CALLED AND EIGHTEEN ARE NOT CGameRules'.
// AllowTrainingAchievements, IsMannVsMachineMode, GetGravityMultiplier,
// RoundHasBeenWon and the rest belong to TF's, Counter-Strike's and the SDK
// sample's rules classes. That they are unreachable is PROVED rather than
// assumed: the build compiles today with the global typed CGameRules *, so any
// of them on a live line would already be a compile error. They sit in dead
// preprocessor arms.
//
// THE FIRST SWEEP MISSED EIGHT by only reading .cpp files. shareddefs.h's
// VEC_VIEW / VEC_HULL_MIN / VEC_HULL_MAX are "g_pGameRules->GetViewVectors()"
// inside a MACRO, so the call site is a header and the compiler found it before
// this comment did. Headers are in the sweep now.
//
// THE CLIENT_DLL SPLIT IS COPIED FROM gamerules.h, NOT INVENTED. 30 of these
// are declared inside "#ifndef CLIENT_DLL" there, 2 inside "#ifdef CLIENT_DLL",
// and 18 on both sides. A flat interface was tried first and made the CLIENT's
// C_SourceSharpGameRules abstract on thirty-one methods that C_GameRules does
// not have -- because they are not compiled into the client at all. An
// interface that demands more than the class it abstracts is not an interface.
//
// Init is worth naming: it IS called (world_adapter.cpp:358) and it is
// IGameSystem's rather than CGameRules'. It is declared here so the interface
// is self-sufficient, and gamerules.h gives CGameRules an explicit forwarding
// override -- one signature arriving down two bases is an ambiguity in C++,
// not an override of either.
//
// THE SIGNATURES ARE COPIED, NOT DESIGNED. Every one is gamerules.h's exactly,
// including the arguments this port would rather not have -- CBasePlayer *,
// edict_t *, CItem *. Changing one here would be a silent behaviour change at
// 124 call sites, and narrowing them is a separate job from introducing the
// interface.
//
//=============================================================================//

#ifndef SOURCESHARP_IGAMERULES_H
#define SOURCESHARP_IGAMERULES_H
#ifdef _WIN32
#pragma once
#endif

// SELF-CONTAINED, and it has to be: gamerules.h includes THIS so that
// CGameRules can implement it, so including gamerules.h back would be a cycle.
//
// Everything the signatures name is a pointer or a reference except Vector and
// the CViewVectors pointer's pointee, so forward declarations carry all but the
// value types below.
#include "tier0/platform.h"
#include "mathlib/vector.h"
#include "edict.h"

// GUARDED, and alias_mechanics_test.cpp is what says so. Phase 13 makes
// CBaseEntity a typedef for IGameEntity, and a class forward declaration of a
// typedef is a hard error -- in a file whose author never heard of the alias.
// Every forward declaration of it in the tree lives inside this guard.
#include "igameentity.h"

class CBasePlayer;
class CItem;

// The damage info RadiusDamage / AdjustPlayerDamageTaken take, per side: the
// client's is takedamageinfo.h's CTakeDamageInfo, the server's is server.so's
// own ss::CTakeDamageInfo (ss_takedamageinfo.h).
#ifdef CLIENT_DLL
class CTakeDamageInfo;
typedef CTakeDamageInfo RulesDamageInfo_t;
#else
namespace ss { class CTakeDamageInfo; }
typedef ss::CTakeDamageInfo RulesDamageInfo_t;
#endif

// The hull and eye offsets GetViewVectors answers, per side (the
// MvLocalData_t pattern in imovementplayer.h): the client's is shareddefs.h's
// CViewVectors, the server's is server.so's own ss::CViewVectors, complete
// here because shareddefs.h's VEC_* macros dereference it.
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

//-----------------------------------------------------------------------------
// Every question the build asks the game rules, and nothing else.
//-----------------------------------------------------------------------------
abstract_class IGameRules
{
public:
	virtual ~IGameRules() {}

	// IGameSystem's, not CGameRules' -- see the header comment above.
	virtual bool Init() = 0;

	//-------------------------------------------------------------------------
	// Asked on BOTH sides.
	//-------------------------------------------------------------------------
	virtual bool AllowThirdPersonCamera( void ) = 0;
	virtual void ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) = 0;
	virtual void ClientSpawned( edict_t * pPlayer ) = 0;
	virtual int Damage_GetNoPhysicsForce( void ) = 0;
	virtual int Damage_GetTimeBased( void ) = 0;
	virtual const RulesViewVectors_t* GetViewVectors( void ) const = 0;
	virtual bool IsConnectedUserInfoChangeAllowed( CBasePlayer *pPlayer ) = 0;
	virtual bool IsMultiplayer( void ) = 0;
	virtual void OnFileReceived( const char * fileName, unsigned int transferID ) = 0;
	virtual bool ShouldCollide( int collisionGroup0, int collisionGroup1 ) = 0;
	virtual bool ShouldDrawHeadLabels( void ) = 0;

#ifndef CLIENT_DLL
	//-------------------------------------------------------------------------
	// Server only -- these are inside "#ifndef CLIENT_DLL" in gamerules.h, so
	// the client's C_GameRules does not have them and cannot be asked for them.
	//-------------------------------------------------------------------------
	virtual float AdjustPlayerDamageInflicted( float damage ) = 0;
	virtual void AdjustPlayerDamageTaken( RulesDamageInfo_t *pInfo ) = 0;
	virtual bool ClientConnected( edict_t *pEntity, const char *pszName, const char *pszAddress, char *reject, int maxrejectlen ) = 0;
	virtual void CreateCustomNetworkStringTables( void ) = 0;
	virtual void CreateStandardEntities( void ) = 0;
	virtual void EndGameFrame( void ) = 0;
	virtual void EndMultiplayerGame( void ) = 0;
	virtual void InitDefaultAIRelationships( void ) = 0;
	// LevelShutdown IS NOT HERE. This interface lists what CALLS INTO the rules,
	// and LevelShutdown had exactly one caller in this build -- CWorld's
	// destructor -- which reached an EMPTY body (CGameRules::LevelShutdown is
	// `{ return; }`) and has been removed. The level-shutdown event goes to C#
	// and is dispatched to every IGameLifecycle, which is where a rules class
	// that wants it should ask for it.
	virtual void Precache( void ) = 0;
	virtual void ResetMapCycleTimeStamp( void ) = 0;
	virtual void Status( void (*print) (const char *fmt, ...) ) = 0;
#else
	//-------------------------------------------------------------------------
	// Client only.
	//-------------------------------------------------------------------------
	virtual bool AllowMapVisionFilterShaders( void ) = 0;
	virtual const char* TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName ) = 0;
#endif // !CLIENT_DLL
};

// The rules object: NULL between levels, so test it before calling. gamerules.h
// declares it too, for the translation units that still include that; this
// declaration is the one a TU built without game/ headers reaches it through.
extern IGameRules *g_pGameRules;

// Builds the rules object for a new level (shell_server.cpp); gamerules.h
// declares it too.
void InstallGameRules();

#endif // SOURCESHARP_IGAMERULES_H
