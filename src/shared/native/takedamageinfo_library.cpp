//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON e  a synchronous mid-step answer
// The damage-info value type and the damage-force calculators. Declarations stay in
// takedamageinfo.h; compiled into both DLLs.

#ifdef CLIENT_DLL
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
#endif
#ifdef CLIENT_DLL
#include "igamerules.h"
#else
#include "igamerules.h"
#endif
#ifdef CLIENT_DLL
#include "sharedInterface.h"
#include <vstdlib/random.h>
#else
// The engine's random stream, named random_valve under POSIX so stdlib's random()
// does not collide; defined in shell_engineboundary.cpp.

extern IUniformRandomStream *random_valve;
#define random random_valve
// The damage bits this file names, under their stock spellings.

#define DMG_GENERIC ss::kDmgGeneric
#define DMG_BULLET ss::kDmgBullet
#define DMG_BLAST ss::kDmgBlast
#define DMG_CRUSH ss::kDmgCrush
#endif
#ifdef CLIENT_DLL
#include "takedamageinfo.h"
#else
// The server's damage info is its own ss:: type.
#include "ss_takedamageinfo.h"
#endif

#include "managed_convar_ref.h"

// Declared in managed code; CManagedConVarRef retries until it registers.
static CManagedConVarRef phys_pushscale( "phys_pushscale" );

#ifndef CLIENT_DLL
namespace ss
{
#endif

// Client only: nothing on the server accumulates multi-damage.
#ifdef CLIENT_DLL
BEGIN_SIMPLE_DATADESC( CTakeDamageInfo )
DEFINE_FIELD( m_vecDamageForce, FIELD_VECTOR ), DEFINE_FIELD( m_vecDamagePosition, FIELD_POSITION_VECTOR ),
	DEFINE_FIELD( m_vecReportedPosition, FIELD_POSITION_VECTOR ), DEFINE_FIELD( m_hInflictor, FIELD_EHANDLE ),
	DEFINE_FIELD( m_hAttacker, FIELD_EHANDLE ), DEFINE_FIELD( m_hWeapon, FIELD_EHANDLE ),
	DEFINE_FIELD( m_flDamage, FIELD_FLOAT ), DEFINE_FIELD( m_flMaxDamage, FIELD_FLOAT ),
	DEFINE_FIELD( m_flBaseDamage, FIELD_FLOAT ), DEFINE_FIELD( m_bitsDamageType, FIELD_INTEGER ),
	DEFINE_FIELD( m_iDamageCustom, FIELD_INTEGER ), DEFINE_FIELD( m_iDamageStats, FIELD_INTEGER ),
	DEFINE_FIELD( m_iAmmoType, FIELD_INTEGER ), DEFINE_FIELD( m_iDamagedOtherPlayers, FIELD_INTEGER ),
	END_DATADESC()
#endif // CLIENT_DLL

		void CTakeDamageInfo::Init( IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon,
									const Vector &damageForce, const Vector &damagePosition,
									const Vector &reportedPosition, float flDamage, int bitsDamageType,
									int iCustomDamage )
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

CTakeDamageInfo::CTakeDamageInfo( IGameEntity *pInflictor, IGameEntity *pAttacker, float flDamage, int bitsDamageType,
								  int iKillType )
{
	Set( pInflictor, pAttacker, flDamage, bitsDamageType, iKillType );
}

void CTakeDamageInfo::Set( IGameEntity *pInflictor, IGameEntity *pAttacker, float flDamage, int bitsDamageType,
						   int iKillType )
{
	Init( pInflictor, pAttacker, NULL, vec3_origin, vec3_origin, vec3_origin, flDamage, bitsDamageType, iKillType );
}

#ifdef CLIENT_DLL
// MultiDamage: collects multiple small damages into a single damage.
BEGIN_SIMPLE_DATADESC_( CMultiDamage, CTakeDamageInfo )
DEFINE_FIELD( m_hTarget, FIELD_EHANDLE ),
	END_DATADESC()

		CMultiDamage g_MultiDamage;

CMultiDamage::CMultiDamage()
{
	m_hTarget = NULL;
}

void CMultiDamage::Init( IGameEntity *pTarget, IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon,
						 const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition,
						 float flDamage, int bitsDamageType, int iKillType )
{
	m_hTarget = pTarget;
	BaseClass::Init( pInflictor, pAttacker, pWeapon, damageForce, damagePosition, reportedPosition, flDamage,
					 bitsDamageType, iKillType );
}

// Resets the global multi damage accumulator.
void ClearMultiDamage()
{
	g_MultiDamage.Init( NULL, NULL, NULL, NULL, vec3_origin, vec3_origin, vec3_origin, 0, 0, 0 );
}

// Inflicts the contents of the global multi damage register on its target.
void ApplyMultiDamage()
{
	if ( !g_MultiDamage.GetTarget() )
		return;

	// Damage is done, clear it out
	ClearMultiDamage();
}
#endif // CLIENT_DLL

// An impulse scale required to push an object. flTargetMass in kg, flDesiredSpeed in
// inches/sec.
float ImpulseScale( float flTargetMass, float flDesiredSpeed )
{
	return ( flTargetMass * flDesiredSpeed );
}

// Fills out a takedamageinfo with a damage force for an explosive.
void CalculateExplosiveDamageForce( CTakeDamageInfo *info, const Vector &vecDir, const Vector &vecForceOrigin,
									float flScale )
{
	info->SetDamagePosition( vecForceOrigin );

	float flClampForce = ImpulseScale( 75, 400 );

	// Calculate an impulse large enough to push a 75kg man 4 in/sec per point of damage
	float flForceScale = info->GetBaseDamage() * ImpulseScale( 75, 4 );

	if ( flForceScale > flClampForce )
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

#ifndef CLIENT_DLL
} // namespace ss
#endif
