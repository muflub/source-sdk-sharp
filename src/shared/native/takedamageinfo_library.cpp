//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON e  a synchronous mid-step answer
// SOURCESHARP: the damage-info value type, moved WHOLE out of
// game/shared/takedamageinfo.cpp.
// WHY THE WHOLE FILE AND NOT A FAMILY. Step 7f measured `ClearMultiDamage()`
// on its own and recorded it as a CONVERSION: its body is `g_MultiDamage.Init(
// ... )`, `g_MultiDamage`'s type is `CMultiDamage`, and `CMultiDamage::Init`
// reaches `CTakeDamageInfo::Init` and `CTakeDamageInfo::m_DataMap`, both
// out-of-line in the stock object. That measurement is right about the family
// and is exactly why the unit here is the TRANSLATION UNIT: taking the value
// type, its datadesc, the multi-damage accumulator and the four damage-force
// calculators together leaves nothing behind for a converted edge to land on.
// The cost was measured before the move rather than after. The stock server
// object left undefined exactly TWO symbols that SCC 11 defines --
// `g_pEntityList` and `CBaseEntity::TakeDamage(CTakeDamageInfo const&)`, both
// from `ApplyMultiDamage`'s one entity call -- and both were ALREADY distinct
// shim->stock cross-edges before this file existed, so neither is a new one.
// The stock CLIENT object left undefined NOTHING that any stock TU defines.
// TWO COMMENTS WERE REWORDED IN THE MOVE, and nothing else was. The Phase 13
// step 5 notes below named the typed-handle template and its Get() in prose,
// while explaining why the port cannot use it -- and this banner cannot repeat
// that spelling either, which is the rule working rather than a nuisance. Inside the stock TU that was invisible
// to TypedHandleSweep, which walks sourcesharp/game/{server,shared} off the
// FILESYSTEM and requires that no source there spell a typed handle AT ALL --
// no other swept file spells one in code or in prose, so the rule as enforced
// is "not even in prose". They now say "the typed handle's own Get()", which is
// the same sentence. The CODE is untouched: this TU never spelled a typed
// handle, because the members are declared in takedamageinfo.h.
// Every declaration stays in game/shared/takedamageinfo.h, so not one caller on
// either side changed. This TU is in BOTH vpcs, because the stock one was: a
// one-sided row would give one side two definitions of `g_MultiDamage` and the
// other none, and the side with two would silently keep stock's.

#ifdef CLIENT_DLL
#include "cbase.h"
#endif
#if !defined( CLIENT_DLL )
// te.h and its `te` singleton went with plan_tmp.md Part 7. What ApplyMultiDamage
// wanted from `te` was never an effect -- it was the temp entity subsystem's
// IPredictionSystem, so that damage-triggered effects are not culled by a
// prediction filter left set from somewhere else. That object outlived the
// dispatch table: SourceSharpTe_PredictionSystem(), in shell_tempents.cpp.
#include "shell_tempents.h"
#include "ss_te_prediction.h"
// SOURCESHARP: the dual value type for "an entity of either kind, found by
// handle". Server only, deliberately — anyentity.h says why: the client half of
// the parallel hierarchy does not join the spatial partition, so there is
// nothing over there for one of these to find.
#include "anyentity.h"
// SOURCESHARP: the union resolve -- plan_entity.md §3.3, lane entlist-server.
// The two entity-list LookupEntity calls in this file's server arms asked
// the entity list because it was the only thing that answered for BOTH
// hierarchies; SourceSharp_EntityFromHandle is the three named arms that
// replaced it. Server only, with anyentity.h and for the same reason.
#include "slot_table.h"
#endif
#ifdef CLIENT_DLL
#include "gamerules.h"
#else
#include "igamerules.h"	// g_pGameRules; stock's CGameRules is not reached on the server
#endif
#ifdef CLIENT_DLL
#include "sharedInterface.h"
#include <vstdlib/random.h>
#else
// sharedInterface.h's two lines the server half uses: the engine's random stream,
// which is named random_valve under POSIX (stdlib.h's random() would collide),
// defined in shell_engineboundary.cpp.
#include <vstdlib/random.h>
extern IUniformRandomStream *random_valve;
#define random random_valve
// The damage bits this file names, from ss_shareddefs.h (oracle-checked against
// shareddefs.h), under their stock spellings as shell_gamerules.cpp does.
#include "ss_shareddefs.h"
#define DMG_GENERIC ss::kDmgGeneric
#define DMG_BULLET  ss::kDmgBullet
#define DMG_BLAST   ss::kDmgBlast
#define DMG_CRUSH   ss::kDmgCrush
#endif
#ifdef CLIENT_DLL
#include "takedamageinfo.h"
#else
// The server's damage info is its own ss:: type (ss_takedamageinfo.h); every
// definition below lands in namespace ss on that side.
#include "ss_takedamageinfo.h"
#endif

// memdbgon must be the last include file in a .cpp file!!!
#include "managed_convar_ref.h"


// phys_pushscale MOVED TO MANAGED CODE, plan_shim.md Phase 5 (lane 5d):
// SourceSharp.Server/MoveVars.cs and SourceSharp.Client/MoveVars.cs declare it,
// both halves in one commit because it is FCVAR_REPLICATED. Nothing on either
// link line read the declaration; the three reads below are in this file and
// resolve the same engine ConVar by name.
//
// CManagedConVarRef rather than ConVarRef: a ConVarRef built at DLL load caches
// its miss forever, and a managed declaration does not exist until
// ConVarRegistry.RegisterAll runs. See managed_convar_ref.h.
static CManagedConVarRef phys_pushscale( "phys_pushscale" );

#ifndef CLIENT_DLL
namespace ss
{
#endif

BEGIN_SIMPLE_DATADESC( CTakeDamageInfo )
	DEFINE_FIELD( m_vecDamageForce, FIELD_VECTOR ),
	DEFINE_FIELD( m_vecDamagePosition, FIELD_POSITION_VECTOR),
	DEFINE_FIELD( m_vecReportedPosition, FIELD_POSITION_VECTOR),
	DEFINE_FIELD( m_hInflictor, FIELD_EHANDLE),
	DEFINE_FIELD( m_hAttacker, FIELD_EHANDLE),
	DEFINE_FIELD( m_hWeapon, FIELD_EHANDLE),
	DEFINE_FIELD( m_flDamage, FIELD_FLOAT),
	DEFINE_FIELD( m_flMaxDamage, FIELD_FLOAT),
	DEFINE_FIELD( m_flBaseDamage, FIELD_FLOAT ),
	DEFINE_FIELD( m_bitsDamageType, FIELD_INTEGER),
	DEFINE_FIELD( m_iDamageCustom, FIELD_INTEGER),
	DEFINE_FIELD( m_iDamageStats, FIELD_INTEGER),
	DEFINE_FIELD( m_iAmmoType, FIELD_INTEGER),
	DEFINE_FIELD( m_iDamagedOtherPlayers, FIELD_INTEGER),
END_DATADESC()

void CTakeDamageInfo::Init( IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iCustomDamage )
{
	m_hInflictor = pInflictor;
	if ( pAttacker )
	{
		m_hAttacker = pAttacker;
	}
	else
	{
		m_hAttacker = pInflictor;
	}

	m_hWeapon = pWeapon;

	m_flDamage = flDamage;

#ifdef CLIENT_DLL
	m_flBaseDamage = BASEDAMAGE_NOT_SPECIFIED;
#else
	m_flBaseDamage = kBaseDamageNotSpecified;
#endif

	m_bitsDamageType = bitsDamageType;
	m_iDamageCustom = iCustomDamage;

	m_flMaxDamage = flDamage;
	m_vecDamageForce = damageForce;
	m_vecDamagePosition = damagePosition;
	m_vecReportedPosition = reportedPosition;
	m_iAmmoType = -1;
	m_iDamagedOtherPlayers = 0;
	m_iPlayerPenetrationCount = 0;
	m_flDamageBonus = 0.f;
	m_bForceFriendlyFire = false;
	m_flDamageForForce = 0.f;
	m_eCritType = CRIT_NONE;
}

CTakeDamageInfo::CTakeDamageInfo()
{
	Init( NULL, NULL, NULL, vec3_origin, vec3_origin, vec3_origin, 0, 0, 0 );
}

CTakeDamageInfo::CTakeDamageInfo( IGameEntity *pInflictor, IGameEntity *pAttacker, float flDamage, int bitsDamageType, int iKillType )
{
	Set( pInflictor, pAttacker, flDamage, bitsDamageType, iKillType );
}

void CTakeDamageInfo::Set( IGameEntity *pInflictor, IGameEntity *pAttacker, float flDamage, int bitsDamageType, int iKillType )
{
	Init( pInflictor, pAttacker, NULL, vec3_origin, vec3_origin, vec3_origin, flDamage, bitsDamageType, iKillType );
}

//-----------------------------------------------------------------------------
// THREE MEMBERS ARE GONE -- plan_shim.md §5 3f, lane 3f.
//
// AdjustPlayerDamageInflictedForSkillLevel, AdjustPlayerDamageTakenForSkillLevel
// and GetAmmoName had no importer on either link line and no caller inside this
// translation unit either. takedamageinfo.h still DECLARES all three, which is
// how the unported stock files that call them (hl2_player.cpp, gamerules.cpp,
// the tf tree) keep compiling if a lane brings one back.
//
// GetAmmoName CARRIED AN ARGUMENT WORTH MORE THAN THE FUNCTION, and it is kept
// in docs/shim-3f-retired-arguments.md rather than in git history alone,
// because the next person to ask "how do I get an entity's classname on this
// side?" needs it and will not think to look in a deleted body: the SERVER can
// answer from the handle (GetClassname() is STRING(m_iClassname), which is
// IServerNetworkable::GetClassName(), the same string) and the CLIENT CANNOT.
// C_BaseEntity::GetClassname() reads the PREDICTION datamap and falls back to
// typeid, both SDK-internal; and IClientNetworkable::GetClientClass()->GetName()
// is NOT the substitute it looks like -- it is the DT class name
// ("C_PhysicsProp"), never a map classname, so a test on it would go from
// sometimes-true to never-true while appearing to have been answered.
//-----------------------------------------------------------------------------

// -------------------------------------------------------------------------------------------------- //
// MultiDamage
// Collects multiple small damages into a single damage
// -------------------------------------------------------------------------------------------------- //
#ifdef CLIENT_DLL
BEGIN_SIMPLE_DATADESC_( CMultiDamage, CTakeDamageInfo )
#else
// BEGIN_SIMPLE_DATADESC_ spelled out: its GetBaseMap calls DataMapAccess
// unqualified, and inside namespace ss that finds both datamap.h's template and
// the class's friend of the same name. The body is DataMapAccess's.
datamap_t CMultiDamage::m_DataMap = { 0, 0, "CMultiDamage", NULL };
datamap_t *CMultiDamage::GetBaseMap() { return &CTakeDamageInfo::m_DataMap; }
BEGIN_DATADESC_GUTS( CMultiDamage )
#endif
	DEFINE_FIELD( m_hTarget, FIELD_EHANDLE),
END_DATADESC()

CMultiDamage g_MultiDamage;

CMultiDamage::CMultiDamage()
{
	m_hTarget = NULL;
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void CMultiDamage::Init( IGameEntity *pTarget, IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iKillType )
{
	m_hTarget = pTarget;
	BaseClass::Init( pInflictor, pAttacker, pWeapon, damageForce, damagePosition, reportedPosition, flDamage, bitsDamageType, iKillType );
}

//-----------------------------------------------------------------------------
// Purpose: Resets the global multi damage accumulator
//-----------------------------------------------------------------------------
void ClearMultiDamage( void )
{
	g_MultiDamage.Init( NULL, NULL, NULL, NULL, vec3_origin, vec3_origin, vec3_origin, 0, 0, 0 );
}

//-----------------------------------------------------------------------------
// Purpose: inflicts contents of global multi damage register on gMultiDamage.pEntity
//-----------------------------------------------------------------------------
void ApplyMultiDamage( void )
{
	if ( !g_MultiDamage.GetTarget() )
		return;

#ifndef CLIENT_DLL
	ss::TePrediction *pTe = SourceSharpTe_PredictionSystem();

	const IGameEntity *host = pTe->GetSuppressHost();
	pTe->SetSuppressHost( NULL );

	// SOURCESHARP, Phase 13 step 5: the accumulated damage is delivered through
	// AnyEntity_t, which is the dual answer for "hurt this, whichever hierarchy
	// it is on" — pBase->TakeDamage for an SDK entity, StandaloneEntity_TakeDamage
	// for one outside it. Written `GetTarget()->TakeDamage()` this asked
	// CBaseEntity for a member the boundary does not carry, and answered a
	// managed victim with a silent nothing: the typed handle's Get() reports
	// NULL for a standalone entity, so IsClear() read as "no target" and the
	// whole accumulation was dropped. Resolving from the HANDLE finds both kinds.
	// SourceSharp_EntityFromHandle, for the reason above the inflictor lookup.
	AnyEntity_t target =
		AnyEntityOf( SourceSharp_EntityFromHandle( g_MultiDamage.GetTargetHandle() ) );
	if ( target.IsValid() )
	{
		target.TakeDamage( g_MultiDamage );
	}

	pTe->SetSuppressHost( const_cast< IGameEntity * >( host ) );
#endif

	// Damage is done, clear it out
	ClearMultiDamage();
}

//============================================================================================================
// Utility functions for physics damage force calculation 
//============================================================================================================
//-----------------------------------------------------------------------------
// Purpose: Returns an impulse scale required to push an object.
// Input  : flTargetMass - Mass of the target object, in kg
//			flDesiredSpeed - Desired speed of the target, in inches/sec.
//-----------------------------------------------------------------------------
float ImpulseScale( float flTargetMass, float flDesiredSpeed )
{
	return (flTargetMass * flDesiredSpeed);
}

//-----------------------------------------------------------------------------
// Purpose: Fill out a takedamageinfo with a damage force for an explosive
//-----------------------------------------------------------------------------
void CalculateExplosiveDamageForce( CTakeDamageInfo *info, const Vector &vecDir, const Vector &vecForceOrigin, float flScale )
{
	info->SetDamagePosition( vecForceOrigin );

	float flClampForce = ImpulseScale( 75, 400 );

	// Calculate an impulse large enough to push a 75kg man 4 in/sec per point of damage
	float flForceScale = info->GetBaseDamage() * ImpulseScale( 75, 4 );

	if( flForceScale > flClampForce )
		flForceScale = flClampForce;

	// Fudge blast forces a little bit, so that each
	// victim gets a slightly different trajectory. 
	// This simulates features that usually vary from
	// person-to-person variables such as bodyweight,
	// which are all indentical for characters using the same model.
	flForceScale *= random->RandomFloat( 0.85, 1.15 );

	// Calculate the vector and stuff it into the takedamageinfo
	Vector vecForce = vecDir;
	VectorNormalize( vecForce );
	vecForce *= flForceScale;
	vecForce *= phys_pushscale.GetFloat();
	vecForce *= flScale;
	info->SetDamageForce( vecForce );
}

//-----------------------------------------------------------------------------
// DebugGetDamageTypeString AND ITS 31-ENTRY STRING TABLE ARE GONE -- lane 3f.
//
// A diagnostic that spells a DMG_ bitfield out in words. No importer on either
// link line, no caller in this TU, and nothing reaches it by name: it is a
// member function, not a ConCommand, so there is no console route to it either.
// The table was file-static and this was its only reader, so the two go
// together or the one that stays is a warning.
//
// The DMG_ bit NAMES are not this file's to own and are not lost with it --
// they are in game/shared/shareddefs.h, which is where the table was copied
// from and where a managed or native replacement would read them.
//-----------------------------------------------------------------------------

#ifndef CLIENT_DLL
} // namespace ss
#endif
