//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// NOTE: If you add a tex type, be sure to modify the s_pImpactEffect
// array in fx_impact.cpp to get an effect when that surface is shot.

 ///< the egg sacs in the tunnels in ep2.

//#define CHAR_TEX_UNUSED		'J'
//#define CHAR_TEX_UNUSED		'K'

//#define CHAR_TEX_UNUSED		'Q'
//#define CHAR_TEX_UNUSED		'R'

//#define CHAR_TEX_UNUSED		'U'

//#define CHAR_TEX_UNUSED		'X'

 ///< wierd-looking jello effect for advisor shield.

abstract_class IDecalEmitterSystem
{
public:
	virtual int	GetDecalIndexForName( char const *decalname ) = 0;
	virtual const char *GetDecalNameForIndex( int nIndex ) = 0;
	virtual char const *TranslateDecalForGameMaterial( char const *decalName, unsigned char gamematerial ) = 0;
};

extern IDecalEmitterSystem *decalsystem;
