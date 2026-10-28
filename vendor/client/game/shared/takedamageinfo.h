//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "networkvar.h" // todo: change this when DECLARE_CLASS is moved into a better location.

// Used to initialize m_flBaseDamage to something that we know pretty much for sure
// hasn't been modified by a user. 
#define BASEDAMAGE_NOT_SPECIFIED	FLT_MAX

class CBaseEntity;

class CTakeDamageInfo
{
public:

	enum ECritType
	{
		CRIT_NONE = 0,
		CRIT_MINI,
		CRIT_FULL,
	};

	DECLARE_CLASS_NOBASE( CTakeDamageInfo );

					CTakeDamageInfo();
					CTakeDamageInfo( CBaseEntity *pInflictor, CBaseEntity *pAttacker, float flDamage, int bitsDamageType, int iKillType = 0 );
					CTakeDamageInfo( CBaseEntity *pInflictor, CBaseEntity *pAttacker, CBaseEntity *pWeapon, float flDamage, int bitsDamageType, int iKillType = 0 );
					CTakeDamageInfo( CBaseEntity *pInflictor, CBaseEntity *pAttacker, const Vector &damageForce, const Vector &damagePosition, float flDamage, int bitsDamageType, int iKillType = 0, Vector *reportedPosition = NULL );
					CTakeDamageInfo( CBaseEntity *pInflictor, CBaseEntity *pAttacker, CBaseEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, float flDamage, int bitsDamageType, int iKillType = 0, Vector *reportedPosition = NULL );
	
	void			Set( CBaseEntity *pInflictor, CBaseEntity *pAttacker, float flDamage, int bitsDamageType, int iKillType = 0 );
	void			Set( CBaseEntity *pInflictor, CBaseEntity *pAttacker, CBaseEntity *pWeapon, float flDamage, int bitsDamageType, int iKillType = 0 );
	void			Set( CBaseEntity *pInflictor, CBaseEntity *pAttacker, const Vector &damageForce, const Vector &damagePosition, float flDamage, int bitsDamageType, int iKillType = 0, Vector *reportedPosition = NULL );
	void			Set( CBaseEntity *pInflictor, CBaseEntity *pAttacker, CBaseEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, float flDamage, int bitsDamageType, int iKillType = 0, Vector *reportedPosition = NULL );

protected:
	void			Init( CBaseEntity *pInflictor, CBaseEntity *pAttacker, CBaseEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iKillType );

	Vector			m_vecDamageForce;
	Vector			m_vecDamagePosition;
	Vector			m_vecReportedPosition;	// Position players are told damage is coming from
	EHANDLE			m_hInflictor;
	EHANDLE			m_hAttacker;
	EHANDLE			m_hWeapon;
	float			m_flDamage;
	float			m_flMaxDamage;
	float			m_flBaseDamage;			// The damage amount before skill leve adjustments are made. Used to get uniform damage forces.
	int				m_bitsDamageType;
	int				m_iDamageCustom;
	int				m_iDamageStats;
	int				m_iAmmoType;			// AmmoType of the weapon used to cause this damage, if any
	int				m_iDamagedOtherPlayers;
	int				m_iPlayerPenetrationCount;
	float			m_flDamageBonus;		// Anything that increases damage (crit) - store the delta
	EHANDLE			m_hDamageBonusProvider;	// Who gave us the ability to do extra damage?
	bool			m_bForceFriendlyFire;	// Ideally this would be a dmg type, but we can't add more

	float			m_flDamageForForce;

	ECritType		m_eCritType;

	DECLARE_SIMPLE_DATADESC();
};

// Purpose: Multi damage. Used to collect multiple damages in the same frame (i.e. shotgun pellets)
class CMultiDamage : public CTakeDamageInfo
{
	DECLARE_CLASS( CMultiDamage, CTakeDamageInfo );
public:
	CMultiDamage();

	void			Init( CBaseEntity *pTarget, CBaseEntity *pInflictor, CBaseEntity *pAttacker, CBaseEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iKillType );

protected:
	EHANDLE			m_hTarget;

	DECLARE_SIMPLE_DATADESC();
};

// Inlines.
