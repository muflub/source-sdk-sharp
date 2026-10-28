//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's damage-info value type, in namespace ss. The entity members are
// CBaseHandles read and written as the stock typed handle was. Members stay in stock's
// order because the datadesc names them by offset.

#pragma once

#include <float.h>
#include "mathlib/vector.h"
#include "basehandle.h"
#include "datamap.h"
#include "networkvar.h"
#include "igameentity.h"
#include "iserverentity.h"
#include "handle_cross.h"
#include "game_entity_relays.h"

class IHandleEntity;

// ehandle.h's resolver, redeclared: that header pulls cbase.h.
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle );

namespace ss
{

// A CBaseHandle assigned and read as an IGameEntity *.
class DamageInfoHandle : public CBaseHandle
{
public:
	// networkvar.h's SourceSharp_EntityHandle refuses every live IGameEntity, hence this.
	DamageInfoHandle &operator=( const IGameEntity *pEntity )
	{
		*static_cast< CBaseHandle * >( this ) = pEntity ? GameEntity_RefHandle( pEntity ) : CBaseHandle();
		return *this;
	}

	// The handle resolved and crossed to the game-entity face by the object itself.
	operator IGameEntity *() const
	{
		return SourceSharp_CrossHandleToGameEntity< IGameEntity, IServerEntity >( SourceSharp_EntityFromHandle( *this ) );
	}
};

// "No base damage was given": GetBaseDamage then answers the damage.
static const float kBaseDamageNotSpecified = FLT_MAX;

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
	CTakeDamageInfo( IGameEntity *pInflictor, IGameEntity *pAttacker, float flDamage, int bitsDamageType, int iKillType = 0 );

	void Set( IGameEntity *pInflictor, IGameEntity *pAttacker, float flDamage, int bitsDamageType, int iKillType = 0 );

	// Who.
	IGameEntity   *GetInflictor() const        { return m_hInflictor; }
	IHandleEntity *GetInflictorHandle() const  { return SourceSharp_EntityFromHandle( m_hInflictor ); }
	IGameEntity   *GetAttacker() const         { return m_hAttacker; }
	IGameEntity   *GetWeapon() const           { return m_hWeapon; }

	// How much. The base damage is the amount before any adjustment, if one
	// was recorded.
	float GetDamage() const                    { return m_flDamage; }
	void  SetDamage( float flDamage )          { m_flDamage = flDamage; }
	float GetMaxDamage() const                 { return m_flMaxDamage; }
	void  SetMaxDamage( float flMaxDamage )    { m_flMaxDamage = flMaxDamage; }
	bool  BaseDamageIsValid() const            { return m_flBaseDamage != kBaseDamageNotSpecified; }
	float GetBaseDamage() const
	{
		if ( BaseDamageIsValid() )
			return m_flBaseDamage;
		return m_flDamage;
	}

	// Where, and how hard it pushes.
	Vector GetDamageForce() const                         { return m_vecDamageForce; }
	void   SetDamageForce( const Vector &vecForce )        { m_vecDamageForce = vecForce; }
	Vector GetDamagePosition() const                      { return m_vecDamagePosition; }
	void   SetDamagePosition( const Vector &vecPosition )  { m_vecDamagePosition = vecPosition; }
	Vector GetReportedPosition() const                    { return m_vecReportedPosition; }
	void   SetReportedPosition( const Vector &vecReported ) { m_vecReportedPosition = vecReported; }

	// What kind.
	int  GetDamageType() const                 { return m_bitsDamageType; }
	void AddDamageType( int bitsDamageType )   { m_bitsDamageType |= bitsDamageType; }
	int  GetDamageCustom() const               { return m_iDamageCustom; }
	int  GetAmmoType() const                   { return m_iAmmoType; }
	void SetAmmoType( int iAmmoType )          { m_iAmmoType = iAmmoType; }
	int  GetPlayerPenetrationCount() const     { return m_iPlayerPenetrationCount; }
	void SetPlayerPenetrationCount( int nCount ) { m_iPlayerPenetrationCount = nCount; }
	ECritType GetCritType() const              { return m_eCritType; }

protected:
	void Init( IGameEntity *pInflictor, IGameEntity *pAttacker, IGameEntity *pWeapon, const Vector &damageForce, const Vector &damagePosition, const Vector &reportedPosition, float flDamage, int bitsDamageType, int iKillType );

	Vector      m_vecDamageForce;
	Vector      m_vecDamagePosition;
	Vector      m_vecReportedPosition;   // where the victim is told the damage came from
	DamageInfoHandle m_hInflictor;
	DamageInfoHandle m_hAttacker;
	DamageInfoHandle m_hWeapon;
	float       m_flDamage;
	float       m_flMaxDamage;
	float       m_flBaseDamage;          // kBaseDamageNotSpecified unless recorded
	int         m_bitsDamageType;
	int         m_iDamageCustom;
	int         m_iDamageStats;
	int         m_iAmmoType;             // -1 unless a weapon's ammo caused it
	int         m_iDamagedOtherPlayers;
	int         m_iPlayerPenetrationCount;
	float       m_flDamageBonus;
	DamageInfoHandle m_hDamageBonusProvider;
	bool        m_bForceFriendlyFire;
	float       m_flDamageForForce;
	ECritType   m_eCritType;
};

// The damage-force helpers: an impulse to push a mass at a speed, and the
// force a blast imparts.
float ImpulseScale( float flTargetMass, float flDesiredSpeed );
void CalculateExplosiveDamageForce( CTakeDamageInfo *info, const Vector &vecDir, const Vector &vecForceOrigin, float flScale = 1.0 );

} // namespace ss
