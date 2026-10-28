// The new-event-system animation event names native code recognises, compared
// by name rather than by a registry ordinal. Includes nothing so a test can hold it.
#pragma once

#define AE_NAME_CL_CREATE_PARTICLE_EFFECT	"AE_CL_CREATE_PARTICLE_EFFECT"
#define AE_NAME_SV_PLAYSOUND				"AE_SV_PLAYSOUND"
#define AE_NAME_SV_EXCLUDE_PLAYER_SOUND		"AE_SV_EXCLUDE_PLAYER_SOUND"
#define AE_NAME_CL_PLAYSOUND				"AE_CL_PLAYSOUND"
#define AE_NAME_CL_EXCLUDE_PLAYER_SOUND		"AE_CL_EXCLUDE_PLAYER_SOUND"

// eventlist.h's AE_TYPE_* bits, respelled so this header includes nothing.
enum
{
	kAnimEventTypeServer = 1 << 0,
	kAnimEventTypeClient = 1 << 4,
	// AE_TYPE_NEWEVENTSYSTEM: the event names itself (szeventindex is valid).
	kAnimEventTypeNewEventSystem = 1 << 10,
};

// ASCII case-insensitive, as stock's CUtlDict lookup was. A NULL name equals nothing.
inline bool AnimEventName_Equal( const char *pA, const char *pB )
{
	if ( !pA || !pB )
		return false;

	for ( ;; pA++, pB++ )
	{
		char a = *pA;
		char b = *pB;

		if ( a >= 'A' && a <= 'Z' ) a += 'a' - 'A';
		if ( b >= 'A' && b <= 'Z' ) b += 'a' - 'A';

		if ( a != b )
			return false;

		if ( a == '\0' )
			return true;
	}
}

// nType is the AE_TYPE_* mask stock registers the name under.
struct AnimEventNameRow_t
{
	const char *pszName;
	int         nType;
};

inline const AnimEventNameRow_t *AnimEventNames_Rows( int *pOutCount )
{
	static const AnimEventNameRow_t s_Rows[] =
	{
		{ AE_NAME_CL_CREATE_PARTICLE_EFFECT, kAnimEventTypeClient },
		{ AE_NAME_SV_PLAYSOUND,              kAnimEventTypeServer },
		{ AE_NAME_SV_EXCLUDE_PLAYER_SOUND,   kAnimEventTypeServer },
		{ AE_NAME_CL_PLAYSOUND,              kAnimEventTypeClient },
		{ AE_NAME_CL_EXCLUDE_PLAYER_SOUND,   kAnimEventTypeClient },
	};

	if ( pOutCount )
		*pOutCount = (int)( sizeof( s_Rows ) / sizeof( s_Rows[ 0 ] ) );

	return s_Rows;
}

// The AE_TYPE_* mask of a named event, or 0 for a name this table does not hold.
inline int AnimEventName_TypeMask( const char *pszName )
{
	int nRows = 0;
	const AnimEventNameRow_t *pRows = AnimEventNames_Rows( &nRows );

	for ( int i = 0; i < nRows; i++ )
	{
		if ( AnimEventName_Equal( pRows[ i ].pszName, pszName ) )
			return pRows[ i ].nType;
	}

	return 0;
}

inline bool AnimEventName_IsClient( const char *pszName )
{
	return ( AnimEventName_TypeMask( pszName ) & kAnimEventTypeClient ) != 0;
}
