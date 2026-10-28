//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include <soundflags.h>
#include "mathlib/vector.h"
#include <shareddefs.h>

#include "shake.h"
#include "bitmap/imageformat.h"
#include "ispatialpartition.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "inputsystem/InputEnums.h"

class Vector;
class QAngle;
class IMaterial;
class ITexture;
class IClientEntity;
class CHudTexture;
class CGameTrace;
class C_BaseEntity;

struct Ray_t;
struct client_textmessage_t;
typedef CGameTrace trace_t;

namespace vgui
{
	typedef uint32 HFont;
};

// ScreenHeight returns the height of the screen, in pixels
int		ScreenHeight( void );
// ScreenWidth returns the width of the screen, in pixels
int		ScreenWidth( void );

void	UTIL_Smoke( const Vector &origin, const float scale, const float framerate );

client_textmessage_t	*TextMessageGet( const char *pName );

char	*VarArgs( PRINTF_FORMAT_STRING const char *format, ... );
	
// Base light indices to avoid index collision

enum
{
	LIGHT_INDEX_TE_DYNAMIC = 0x10000000,
	LIGHT_INDEX_PLAYER_BRIGHT = 0x20000000,
	LIGHT_INDEX_MUZZLEFLASH = 0x40000000,
};

// prints messages through the HUD (stub in client .dll right now )
class C_BasePlayer;

// make this a fixed size so it just sits on the stack
#define MAX_SPHERE_QUERY	256

C_BaseEntity *CreateEntityByName( const char *className );
// creates an entity by name, and ensure it's correctness
// does not spawn the entity
// use the CREATE_ENTITY() macro which wraps this, instead of using it directly
template< class T >
T *_CreateEntity( T *newClass, const char *className )
{
	T *newEnt = dynamic_cast<T*>( CreateEntityByName(className) );
	if ( !newEnt )
	{
		Warning( "classname %s used to create wrong class type\n", className );
		Assert(0);
	}

	return newEnt;
}

#define CREATE_ENTITY( newClass, className ) _CreateEntity( (newClass*)NULL, className )
