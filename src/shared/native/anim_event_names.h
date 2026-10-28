//========= SourceSharp — animation events by NAME, not by ordinal ===========//
//
// THE FIVE NEW-SYSTEM EVENT NAMES NATIVE CODE STILL HAS TO RECOGNISE, and
// whether each is a client event.
//
// WHY THIS EXISTS. An mstudioevent_t with AE_TYPE_NEWEVENTSYSTEM set carries a
// NAME in the .mdl and a NUMBER that means nothing until somebody fills it in.
// Stock filled it in: game/shared/animation.cpp's SetEventIndexForSequence
// looked the name up in a per-level string registry and WROTE THE ORDINAL BACK
// INTO MDL MEMORY (:78-91), ORing the registry's type bits into the event's
// type field as it went. Every later comparison — `pEvent->event ==
// AE_SV_PLAYSOUND` — was then a comparison against an enum value that happened
// to match the registry's insertion order. Two orderings, kept in step by
// nothing but care.
//
// All of that machinery exists so that a native reader can compare two
// integers. Managed code already owns the table
// (SourceSharp.Abi/EventCatalog.cs); the registry it filled is 389 lines of
// shim plus a 283-line header; and the ordinal written into the model is
// meaningless to anything that did not take part in the arrangement. Comparing
// the NAME the model actually carries needs none of it.
//
// SO THIS IS THE WHOLE OF WHAT NATIVE CODE NEEDS: five names, and one bit
// each. It is not a copy of EventCatalog.cs — it is the five rows of it that
// native code reads — and anim_event_names_test.cpp asserts that every row
// here says what EventCatalog.cs says about the same name, in both directions,
// and that the bit below is eventlist.h's AE_TYPE_CLIENT. A table that drifted
// from the managed one silently is exactly the failure this port keeps having
// to unpick.
//
// ENGINE-FREE ON PURPOSE, in the bone_cache_key.h and sequence_by_activity.h
// tradition. It includes nothing: the part that could be silently wrong is a
// string comparison and a table, so it is a header a test binary can hold
// still with no engine, no studio header and no tier1. The caller keeps
// mstudioevent_t on its own side of the line and hands over a name — which is
// also the guard that matters, because szeventindex is meaningless on an
// old-system event and only the caller knows whether the flag is set.
//
// THE OLD-SYSTEM NUMBERS ARE SOMEWHERE ELSE, AND STAY THERE. CL_EVENT_SOUND
// (5004), CL_EVENT_FOOTSTEP_LEFT/RIGHT (6004/6005), SCRIPT_EVENT_SOUND (1004)
// and SCRIPT_EVENT_SOUND_VOICE (1008) are genuine numbers a .mdl carries with
// no name at all; they live in cl_animevent.h and scriptevent.h and a caller
// compares them as numbers because that is all there is. The two populations
// cannot collide — every old-style constant is over 1000 and every registry
// ordinal is under 50 — which is why splitting a switch that mixed them is a
// refactor and not a behaviour change.
//
//=============================================================================//
#pragma once

//-----------------------------------------------------------------------------
// The names. Spelled once, so a typo is a compile error at the call site
// rather than a comparison that is quietly always false.
//-----------------------------------------------------------------------------
#define AE_NAME_CL_CREATE_PARTICLE_EFFECT	"AE_CL_CREATE_PARTICLE_EFFECT"
#define AE_NAME_SV_PLAYSOUND				"AE_SV_PLAYSOUND"
#define AE_NAME_SV_EXCLUDE_PLAYER_SOUND		"AE_SV_EXCLUDE_PLAYER_SOUND"
#define AE_NAME_CL_PLAYSOUND				"AE_CL_PLAYSOUND"
#define AE_NAME_CL_EXCLUDE_PLAYER_SOUND		"AE_CL_EXCLUDE_PLAYER_SOUND"

//-----------------------------------------------------------------------------
// eventlist.h's type bits, respelled so this header includes nothing.
//
// A LOCAL COPY OF A NUMBER IS A THING THAT DRIFTS, so the test reads
// eventlist.h and requires these to be those. They are not "the enum values"
// in any loose sense: AE_TYPE_CLIENT is 1 << 4 with four unrelated flags below
// it, and EventCatalog.cs says the same in its own comment because it had to
// make the same decision.
//-----------------------------------------------------------------------------
enum
{
	kAnimEventTypeServer = 1 << 0,
	kAnimEventTypeClient = 1 << 4,
	// AE_TYPE_NEWEVENTSYSTEM: the event names itself (szeventindex is valid).
	// Its managed twin is AnimationEventTypes.NewEventSystem, by which the
	// managed StudioModelDescriptor reads an event's name.
	kAnimEventTypeNewEventSystem = 1 << 10,
};

//-----------------------------------------------------------------------------
// ASCII case-insensitive equality, local so no engine header is needed.
//
// CASE-INSENSITIVE BECAUSE STOCK'S LOOKUP WAS: the registry behind
// EventList_IndexForName is a CUtlDict, whose default compare type is
// k_eDictCompareTypeCaseInsensitive (utldict.h:51), so a model naming
// "ae_muzzleflash" resolved there and must resolve here. EventCatalog.cs's
// IndexForName carries the same note and the same behaviour.
//
// A NULL name equals nothing, including another NULL. "This event has no name"
// is not a name two events can share.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The five rows. nType is the AE_TYPE_* mask stock's
// EventList_RegisterSharedEvents registers the name under.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The AE_TYPE_* mask of a named event, or 0 for a name this table does not
// hold.
//
// ZERO RATHER THAN -1, and the distinction is EventCatalog.cs's: -1 is what
// EventList_GetEventType answered for an INDEX it had never seen, while a name
// that is simply not one of the five has no type here rather than an invalid
// one. Nothing in this build acts on a sixth name.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// Is the event with this name a CLIENT event?
//
// This is the half stock computed by WRITING to the model:
// SetEventIndexForSequence did `pevent->type |= EventList_GetEventType(...)`,
// an OR that kept whatever the .mdl declared and added the registry's. A
// caller that also has the event's own type word ORs this with it and gets
// the same answer, without the write.
//-----------------------------------------------------------------------------
inline bool AnimEventName_IsClient( const char *pszName )
{
	return ( AnimEventName_TypeMask( pszName ) & kAnimEventTypeClient ) != 0;
}
