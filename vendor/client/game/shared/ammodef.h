//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose:		Holds defintion for game ammo types

#pragma once

class ConVar;

struct Ammo_t 
{
	char 				*pName;
	int					nDamageType;
	int					eTracerType;
	float				physicsForceImpulse;
	int					nMinSplashSize;
	int					nMaxSplashSize;

	int					nFlags;

	// Values for player/NPC damage and carrying capability
	// If the integers are set, they override the CVars
	int					pPlrDmg;		// CVar for player damage amount
	int					pNPCDmg;		// CVar for NPC damage amount
	int					pMaxCarry;		// CVar for maximum number can carry
	const ConVar*		pPlrDmgCVar;	// CVar for player damage amount
	const ConVar*		pNPCDmgCVar;	// CVar for NPC damage amount
	const ConVar*		pMaxCarryCVar;	// CVar for maximum number can carry
};

// Used to tell AmmoDef to use the cvars, not the integers

// Ammo is infinite

#include "shareddefs.h"

//	>> CAmmoDef
class CAmmoDef
{

public:
	int					m_nAmmoIndex;

	Ammo_t				m_AmmoType[MAX_AMMO_TYPES];

	int					Index(const char *psz);

	CAmmoDef(void);
	virtual ~CAmmoDef( void );

private:
};
