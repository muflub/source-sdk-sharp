//=========== (C) Copyright 1999 Valve, L.L.C. All rights reserved. ===========
//
// The copyright to the contents herein is the property of Valve, L.L.C.
// The contents may be used and/or copied only with the written permission of
// Valve, L.L.C., or in accordance with the terms and conditions stipulated in
// the agreement/contract under which the contents have been supplied.
//=============================================================================

// No spaces in event names, max length 32
// All strings are case sensitive
//
// valid data key types are:
//   string : a zero terminated string
//   bool   : unsigned int, 1 bit
//   byte   : unsigned int, 8 bit
//   short  : signed int, 16 bit
//   long   : signed int, 32 bit
//   float  : float, 32 bit
//   local  : any data, but not networked to clients
//
// following key names are reserved:
//   local      : if set to 1, event is not networked to clients
//   unreliable : networked, but unreliable
//   suppress   : never fire this event
//   time	: firing server time
//   eventid	: holds the event ID

"modevents"
{
	"player_death"				// a game event, name may be 32 charaters long
	{
		"userid"	"short"   	// user ID who died
		"attacker"	"short"	 	// user ID who killed
		"weapon"	"string" 	// weapon name killed used
	}

	"player_hurt"				// stock TF2's player_hurt (tf2_misc_dir.vpk resource/modevents.res)
	{
		"userid"	"short"   	// user ID who was hurt
		"health"	"short"		// the victim's health after the hit
		"attacker"	"short"		// user ID who hurt them
		"damageamount"	"short"	// the post-armour damage taken
		"custom"	"short"		// TF's kill custom
		"showdisguisedcrit"	"bool"	// if our attribute specifically crits disguised enemies we need to show it on the client
		"crit"	"bool"			// was the hit a critical
		"minicrit"	"bool"		// was the hit a mini-crit
		"allseecrit"	"bool"	// should everyone see the crit
		"weaponid"	"short"		// TF's weapon id
		"bonuseffect"	"byte"	// TF's bonus effect
	}

	"controlpoint_starttouch"		// stock TF2's controlpoint_starttouch (tf2_misc_dir.vpk resource/modevents.res)
	{
		"player"	"short"		// entindex of the player
		"area"		"short"		// index of the control point area
	}

	"controlpoint_endtouch"			// stock TF2's controlpoint_endtouch (tf2_misc_dir.vpk resource/modevents.res)
	{
		"player"	"short"		// entindex of the player
		"area"		"short"		// index of the control point area
	}

	"teamplay_round_start"			// round restart
	{
		"full_reset"	"bool"		// is this a full reset of the map
	}
	
	"spec_target_updated"
	{
	}
	
	"achievement_earned"
	{
		"player"	"byte"		// entindex of the player
		"achievement"	"short"		// achievement ID
	}
}
