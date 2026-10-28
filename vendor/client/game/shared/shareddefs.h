//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Definitions that are shared by the game DLL and the client DLL.

#pragma once

#define TICK_INTERVAL			(gpGlobals->interval_per_tick)

// Each mod defines these for itself.
class CViewVectors
{
public:
	CViewVectors() {}

	CViewVectors( 
		Vector vView,
		Vector vHullMin,
		Vector vHullMax,
		Vector vDuckHullMin,
		Vector vDuckHullMax,
		Vector vDuckView,
		Vector vObsHullMin,
		Vector vObsHullMax,
		Vector vDeadViewHeight )
	{
		m_vView = vView;
		m_vHullMin = vHullMin;
		m_vHullMax = vHullMax;
		m_vDuckHullMin = vDuckHullMin;
		m_vDuckHullMax = vDuckHullMax;
		m_vDuckView = vDuckView;
		m_vObsHullMin = vObsHullMin;
		m_vObsHullMax = vObsHullMax;
		m_vDeadViewHeight = vDeadViewHeight;
	}

	// Height above entity position where the viewer's eye is.
	Vector m_vView;
	
	Vector m_vHullMin;
	Vector m_vHullMax;
	
	Vector m_vDuckHullMin;
	Vector m_vDuckHullMax;
	Vector m_vDuckView;
	
	Vector m_vObsHullMin;
	Vector m_vObsHullMax;
	
	Vector m_vDeadViewHeight;
};

// Height above entity position where the viewer's eye is.
#define VEC_VIEW			g_pGameRules->GetViewVectors()->m_vView
#define VEC_HULL_MIN		g_pGameRules->GetViewVectors()->m_vHullMin
#define VEC_HULL_MAX		g_pGameRules->GetViewVectors()->m_vHullMax

#define VEC_DUCK_HULL_MIN	g_pGameRules->GetViewVectors()->m_vDuckHullMin
#define VEC_DUCK_HULL_MAX	g_pGameRules->GetViewVectors()->m_vDuckHullMax
#define VEC_DUCK_VIEW		g_pGameRules->GetViewVectors()->m_vDuckView

#define VEC_OBS_HULL_MIN	g_pGameRules->GetViewVectors()->m_vObsHullMin
#define VEC_OBS_HULL_MAX	g_pGameRules->GetViewVectors()->m_vObsHullMax

#define VEC_DEAD_VIEWHEIGHT	g_pGameRules->GetViewVectors()->m_vDeadViewHeight

// If the player (enemy bots) are scaled, adjust the hull
#define VEC_VIEW_SCALED( player )				( g_pGameRules->GetViewVectors()->m_vView * player->GetModelScale() )
#define VEC_HULL_MIN_SCALED( player )			( g_pGameRules->GetViewVectors()->m_vHullMin * player->GetModelScale() )
#define VEC_HULL_MAX_SCALED( player )			( g_pGameRules->GetViewVectors()->m_vHullMax * player->GetModelScale() )

#define VEC_DUCK_HULL_MIN_SCALED( player )		( g_pGameRules->GetViewVectors()->m_vDuckHullMin * player->GetModelScale() )
#define VEC_DUCK_HULL_MAX_SCALED( player )		( g_pGameRules->GetViewVectors()->m_vDuckHullMax * player->GetModelScale() )
#define VEC_DUCK_VIEW_SCALED( player )			( g_pGameRules->GetViewVectors()->m_vDuckView * player->GetModelScale() )

	// hud item selection slots
	// max number of items within a slot
	// hud item selection slots
	// Max number of weapons available

	// hard coded item types

	// clip sizes set to this tell the weapon it doesn't use a clip

#define	MAX_AMMO_TYPES	32		// ???
		// not really slots

// Vote creation or processing failure codes
typedef enum
{
	VOTE_FAILED_GENERIC = 0,
	VOTE_FAILED_TRANSITIONING_PLAYERS,
	VOTE_FAILED_RATE_EXCEEDED,
	VOTE_FAILED_YES_MUST_EXCEED_NO,
	VOTE_FAILED_QUORUM_FAILURE,
	VOTE_FAILED_ISSUE_DISABLED,
	VOTE_FAILED_MAP_NOT_FOUND,
	VOTE_FAILED_MAP_NAME_REQUIRED,
	VOTE_FAILED_ON_COOLDOWN,
	VOTE_FAILED_TEAM_CANT_CALL,
	VOTE_FAILED_WAITINGFORPLAYERS,
	VOTE_FAILED_PLAYERNOTFOUND,
	VOTE_FAILED_CANNOT_KICK_ADMIN,
	VOTE_FAILED_SCRAMBLE_IN_PROGRESS,
	VOTE_FAILED_SPECTATOR,
	VOTE_FAILED_NEXTLEVEL_SET,
	VOTE_FAILED_MAP_NOT_VALID,
	VOTE_FAILED_CANNOT_KICK_FOR_TIME,
	VOTE_FAILED_CANNOT_KICK_DURING_ROUND,
	VOTE_FAILED_VOTE_IN_PROGRESS,
	VOTE_FAILED_KICK_LIMIT_REACHED,
	VOTE_FAILED_KICK_DENIED_BY_GC,
	// Special reason - the vote issue itself will handle this request specially -- the vote system shouldn't proceed
	// with the vote.  Shows no error to user.
	// Used by match-based votekicks to send a votekick request to the match system when the user tries to call one --
	// which then owns starting a proper vote later or telling the user why the match system isn't allowing the
	// votekick.
	VOTE_FAILED_REQUEST_HANDLED_BY_ISSUE,

	// TF-specific?
	VOTE_FAILED_MODIFICATION_ALREADY_ACTIVE,

	VOTE_FAILED_PLAYER_TRANSITIONING,

	VOTE_FAILED_INVALID_ARGUMENT,
} vote_create_failed_t;

enum
{
	SERVER_MODIFICATION_ITEM_DURATION_IN_MINUTES = 120
};

// Close caption flags

// Hud Element hiding flags
	// Hide ammo count & weapon selection

	// Hide health & armor / suit battery
#define HIDEHUD_PLAYERDEAD			( 1<<4 )	// Hide when local player's dead
	// Hide when the local player doesn't have the HEV suit
#define HIDEHUD_MISCSTATUS			( 1<<6 )	// Hide miscellaneous status elements (trains, pickup history, death notices, etc)
	// Hide all communication elements (saytext, voice icon, etc)
	// Hide crosshairs
	// Hide vehicle crosshair

	// Hide bonus progress display (for bonus map challenges)

#define HIDEHUD_BITCOUNT			12

// suit usage bits

// Player Defines

// Max number of players in a game ( see const.h for ABSOLUTE_PLAYER_LIMIT (256 ) )
// The Source engine is really designed for 32 or less players.  If you raise this number above 32, you better know what you are doing
//  and have a good answer for a bunch of perf question related to player simulation, thinking logic, tracelines, networking overhead, etc.
// But if you are brave or are doing something interesting, go for it...   ywb 9/22/03

//You might be wondering why these aren't multiple of 2. Well the reason is that if servers decide to have HLTV or Replay enabled we need the extra slot.
//This is ok since MAX_PLAYERS is used for code specific things like arrays and loops, but it doesn't really means that this is the max number of players allowed
//Since this is decided by the gamerules (and it can be whatever number as long as its less than MAX_PLAYERS).
	#define MAX_PLAYERS				33  // Absolute max players supported

// Josh: Accounts for code that may index this array by an entindex
// of player rather than the player index... :s

// Team Defines

#define TEAM_UNASSIGNED			0	// not assigned to a team
	// spectator team
// Start your team numbers after this

// The first team that's game specific (i.e. not unassigned / spectator)

	// Max number of teams in a game
	// Max length of a team's name

// Weapon m_iState

	// Weapon is on the ground
	// This client is carrying this weapon.
	// This client is carrying this weapon and it's the currently held weapon

// Skill Level

// Weapon flags
//	Flags - NOTE: KEEP g_ItemFlags IN WEAPON_PARSE.CPP UPDATED WITH THESE

	// A player can totally exhaust their ammo supply and lose this weapon
	// This weapon take hit location into account when applying damage
	// Don't draw ammo pickup sprites/sounds when ammo is received
	// Don't draw weapon pickup when this weapon is picked up by the player
// NOTE: KEEP g_ItemFlags IN WEAPON_PARSE.CPP UPDATED WITH THESE

// Humans only have left and right hands, though we might have aliens with more
//  than two, sigh

#define TRACER_TYPE_DEFAULT		0x00000001
#define TRACER_TYPE_GUNSHIP		0x00000002
#define TRACER_TYPE_STRIDER		0x00000004 // Here ya go, Jay!
#define TRACER_TYPE_GAUSS		0x00000008
#define TRACER_TYPE_WATERBULLET	0x00000010

// Muzzle flash definitions (for the flags field of the "MuzzleFlash" DispatchEffect)
enum
{
	MUZZLEFLASH_AR2				= 0,
	MUZZLEFLASH_SHOTGUN,
	MUZZLEFLASH_SMG1,
	MUZZLEFLASH_SMG2,
	MUZZLEFLASH_PISTOL,
	MUZZLEFLASH_COMBINE,
	MUZZLEFLASH_357,
	MUZZLEFLASH_RPG,
	MUZZLEFLASH_COMBINE_TURRET,

	MUZZLEFLASH_FIRSTPERSON		= 0x100,
};

// Tracer Flags
#define TRACER_FLAG_WHIZ			0x0001
#define TRACER_FLAG_USEATTACHMENT	0x0002

#define TRACER_DONT_USE_ATTACHMENT	-1

// Entity Dissolve types
enum
{
	ENTITY_DISSOLVE_NORMAL = 0,
	ENTITY_DISSOLVE_ELECTRICAL,
	ENTITY_DISSOLVE_ELECTRICAL_LIGHT,
	ENTITY_DISSOLVE_CORE,

	// NOTE: Be sure to up the bits if you make more dissolve types
	ENTITY_DISSOLVE_BITS = 3
};

//  Hit Group standards

			// alerts NPC, but doesn't do damage or bleed (1/100th damage)

// Enumerations for setting player animation.
enum PLAYER_ANIM
{
	PLAYER_IDLE,
	PLAYER_WALK,
	PLAYER_JUMP,
	PLAYER_SUPERJUMP,
	PLAYER_DIE,
	PLAYER_ATTACK1,
	PLAYER_IN_VEHICLE,

	// TF Player animations
	PLAYER_RELOAD,
	PLAYER_START_AIMING,
	PLAYER_LEAVE_AIMING,
};

 // approx 60 feet
 // approx 20 feet
 // Can go another 200 units without getting hurt

 // won't punch player's screen/make scrape noise unless player falling at least this fast.
 // damage per unit per second.

// instant damage

// For a means of resolving these consts into debug string text, see function
// CTakeDamageInfo::DebugGetDamageTypeString(unsigned int DamageType, char *outbuf, unsigned int outbuflength )
			// generic damage -- do not use if you want players to flinch and bleed!
#define DMG_CRUSH			(1 << 0)	// crushed by falling or moving object. 
										// NOTE: It's assumed crush damage is occurring as a result of physics collision, so no extra physics force is generated by crush damage.
										// DON'T use DMG_CRUSH when damaging entities unless it's the result of a physics collision. You probably want DMG_CLUB instead.
	// shot
	// cut, clawed, stabbed
#define DMG_BURN			(1 << 3)	// heat burned
	// hit by a vehicle
#define DMG_FALL			(1 << 5)	// fell too far
#define DMG_BLAST			(1 << 6)	// explosive blast damage
#define DMG_CLUB			(1 << 7)	// crowbar, punch, headbutt
#define DMG_SHOCK			(1 << 8)	// electric shock
#define DMG_SONIC			(1 << 9)	// sound pulse shockwave
	// laser or other high energy beam 
#define DMG_PREVENT_PHYSICS_FORCE		(1 << 11)	// Prevent a physics force 
	// with this bit OR'd in, no damage type will be able to gib victims upon death
	// with this bit OR'd in, any damage type can be made to gib victims upon death.
#define DMG_DROWN			(1 << 14)	// Drowning

#define DMG_PARALYZE		(1 << 15)	// slows affected creature down
#define DMG_NERVEGAS		(1 << 16)	// nerve toxins, very bad
#define DMG_POISON			(1 << 17)	// blood poisoning - heals over time like drowning damage
#define DMG_RADIATION		(1 << 18)	// radiation exposure
#define DMG_DROWNRECOVER	(1 << 19)	// drowning recovery
#define DMG_ACID			(1 << 20)	// toxic chemicals or acid burns
#define DMG_SLOWBURN		(1 << 21)	// in an oven

		// with this bit OR'd in, no ragdoll will be created, and the target will be quietly removed.
										// use this to kill an entity that you've already got a server-side ragdoll for

#define DMG_PHYSGUN			(1<<23)		// Hit by manipulator. Usually doesn't do any damage.
#define DMG_PLASMA			(1<<24)		// Shot by Cremator
		// Hit by the airboat's gun

		// Dissolving!
		// A blast on the surface of water that cannot harm things underwater

#define DMG_BUCKSHOT		(1<<29)		// not quite a bullet. Little, rounder, different.

// NOTE: DO NOT ADD ANY MORE CUSTOM DMG_ TYPES. MODS USE THE DMG_LASTGENERICFLAG BELOW, AND
//		 IF YOU ADD NEW DMG_ TYPES, THEIR TYPES WILL BE HOSED. WE NEED A BETTER SOLUTION.

// TODO: keep this up to date so all the mod-specific flags don't overlap anything.
#define DMG_LASTGENERICFLAG	DMG_BUCKSHOT

// settings for m_takedamage
#define	DAMAGE_NO				0
#define DAMAGE_EVENTS_ONLY		1		// Call damage functions, but don't modify health
#define	DAMAGE_YES				2

// Spectator Movement modes
enum {
	OBS_MODE_NONE = 0,	// not in spectator mode
	OBS_MODE_DEATHCAM,	// special mode for death cam animation
	OBS_MODE_FREEZECAM,	// zooms to a target, and freeze-frames on them
	OBS_MODE_FIXED,		// view from a fixed camera position
	OBS_MODE_IN_EYE,	// follow a player in first person view
	OBS_MODE_CHASE,		// follow a player in third person view
	OBS_MODE_POI,		// PASSTIME point of interest - game objective, big fight, anything interesting; added in the middle of the enum due to tons of hard-coded "<ROAMING" enum compares
	OBS_MODE_ROAMING,	// free roaming

	NUM_OBSERVER_MODES,
};

// Force Camera Restrictions with mp_forcecamera
enum {
	OBS_ALLOW_ALL = 0,	// allow all modes, all targets
	OBS_ALLOW_TEAM,		// allow only own team & first person, no PIP
	OBS_ALLOW_NONE,		// don't allow any spectating after death (fixed & fade to black)

	OBS_ALLOW_NUM_MODES,
};

enum
{
	TYPE_TEXT = 0,	// just display this plain text
	TYPE_INDEX,		// lookup text & title in stringtable
	TYPE_URL,		// show this URL
	TYPE_FILE,		// show this local file
} ;

// HPE_BEGIN:
// [Forrest] Replaced text window command string with TEXTWINDOW_CMD enumeration
// of options.  Passing a command string is dangerous and allowed a server network
// message to run arbitrary commands on the client.
enum
{
	TEXTWINDOW_CMD_NONE = 0,
	TEXTWINDOW_CMD_JOINGAME,
	TEXTWINDOW_CMD_CHANGETEAM,
	TEXTWINDOW_CMD_IMPULSE101,
	TEXTWINDOW_CMD_MAPINFO,
	TEXTWINDOW_CMD_CLOSED_HTMLPAGE,
	TEXTWINDOW_CMD_CHOOSETEAM,
};
// HPE_END

// VGui Screen Flags
enum
{
	VGUI_SCREEN_ACTIVE = 0x1,
	VGUI_SCREEN_VISIBLE_TO_TEAMMATES = 0x2,
	VGUI_SCREEN_ATTACHED_TO_VIEWMODEL = 0x4,
	VGUI_SCREEN_TRANSPARENT = 0x8,
	VGUI_SCREEN_ONLY_USABLE_BY_OWNER = 0x10,

	VGUI_SCREEN_MAX_BITS = 5
};

typedef enum
{
	USE_OFF = 0, 
	USE_ON = 1, 
	USE_SET = 2, 
	USE_TOGGLE = 3
} USE_TYPE;

// basic team colors

// All NPCs need this data
enum
{
	DONT_BLEED = -1,

	BLOOD_COLOR_RED = 0,
	BLOOD_COLOR_YELLOW,
	BLOOD_COLOR_GREEN,
	BLOOD_COLOR_MECH,

};

// Water splash effect flags
enum
{
	FX_WATER_IN_SLIME = 0x1,
};

// Shared think context stuff

// entity flags, CBaseEntity::m_iEFlags
enum
{
	EFL_KILLME	=				(1<<0),	// This entity is marked for death -- This allows the game to actually delete ents at a safe time
	EFL_DORMANT	=				(1<<1),	// Entity is dormant, no updates to client
	EFL_NOCLIP_ACTIVE =			(1<<2),	// Lets us know when the noclip command is active.
	EFL_SETTING_UP_BONES =		(1<<3),	// Set while a model is setting up its bones.
	EFL_KEEP_ON_RECREATE_ENTITIES = (1<<4), // This is a special entity that should not be deleted when we restart entities only

	EFL_HAS_PLAYER_CHILD=		(1<<4),	// One of the child entities is a player.

	EFL_DIRTY_SHADOWUPDATE =	(1<<5),	// Client only- need shadow manager to update the shadow...
	EFL_NOTIFY =				(1<<6),	// Another entity is watching events on this entity (used by teleport)

	// The default behavior in ShouldTransmit is to not send an entity if it doesn't
	// have a model. Certain entities want to be sent anyway because all the drawing logic
	// is in the client DLL. They can set this flag and the engine will transmit them even
	// if they don't have a model.
	EFL_FORCE_CHECK_TRANSMIT =	(1<<7),

	EFL_BOT_FROZEN =			(1<<8),	// This is set on bots that are frozen.
	EFL_SERVER_ONLY =			(1<<9),	// Non-networked entity.
	EFL_NO_AUTO_EDICT_ATTACH =	(1<<10), // Don't attach the edict; we're doing it explicitly
	
	// Some dirty bits with respect to abs computations
	EFL_DIRTY_ABSTRANSFORM =	(1<<11),
	EFL_DIRTY_ABSVELOCITY =		(1<<12),
	EFL_DIRTY_ABSANGVELOCITY =	(1<<13),
	EFL_DIRTY_SURROUNDING_COLLISION_BOUNDS	= (1<<14),
	EFL_DIRTY_SPATIAL_PARTITION = (1<<15),
	EFL_FORCE_ALLOW_MOVEPARENT	= (1<<16),

	EFL_IN_SKYBOX =				(1<<17),	// This is set if the entity detects that it's in the skybox.
											// This forces it to pass the "in PVS" for transmission.
	EFL_USE_PARTITION_WHEN_NOT_SOLID = (1<<18),	// Entities with this flag set show up in the partition even when not solid
	EFL_TOUCHING_FLUID =		(1<<19),	// Used to determine if an entity is floating

	// FIXME: Not really sure where I should add this...
	EFL_IS_BEING_LIFTED_BY_BARNACLE = (1<<20),
	EFL_NO_ROTORWASH_PUSH =		(1<<21),		// I shouldn't be pushed by the rotorwash
	EFL_NO_THINK_FUNCTION =		(1<<22),
	EFL_NO_GAME_PHYSICS_SIMULATION = (1<<23),

	EFL_CHECK_UNTOUCH =			(1<<24),
	EFL_DONTBLOCKLOS =			(1<<25),		// I shouldn't block NPC line-of-sight
	EFL_DONTWALKON =			(1<<26),		// NPC;s should not walk on this entity
	EFL_NO_DISSOLVE =			(1<<27),		// These guys shouldn't dissolve
	EFL_NO_MEGAPHYSCANNON_RAGDOLL = (1<<28),	// Mega physcannon can't ragdoll these guys.
	EFL_NO_WATER_VELOCITY_CHANGE  =	(1<<29),	// Don't adjust this entity's velocity when transitioning into water
	EFL_NO_PHYSCANNON_INTERACTION =	(1<<30),	// Physcannon can't pick these up or punt them
	EFL_NO_DAMAGE_FORCES =		(1<<31),	// Doesn't accept forces from physics damage
};

// These are the types of data that hang off of CBaseEntities and the flag bits used to mark their presence
enum
{
	GROUNDLINK = 0,
	TOUCHLINK,
	STEPSIMULATION,
	MODELSCALE,
	POSITIONWATCHER,
	PHYSICSPUSHLIST,
	VPHYSICSUPDATEAI,
	VPHYSICSWATCHER,

	// Must be last and <= 32
	NUM_DATAOBJECT_TYPES,
};

class CBaseEntity;

// Bullet firing information
class CBaseEntity;

struct FireBulletsInfo_t
{
	FireBulletsInfo_t()
	{
		m_iShots = 1;
		m_vecSpread.Init( 0, 0, 0 );
		m_flDistance = 8192;
		m_iTracerFreq = 4;
		m_flDamage = 0;
		m_iPlayerDamage = 0;
		m_pAttacker = NULL;
		m_nFlags = 0;
		m_pAdditionalIgnoreEnt = NULL;
		m_flDamageForceScale = 1.0f;

#ifdef _DEBUG
		m_iAmmoType = -1;
		m_vecSrc.Init( VEC_T_NAN, VEC_T_NAN, VEC_T_NAN );
		m_vecDirShooting.Init( VEC_T_NAN, VEC_T_NAN, VEC_T_NAN );
#endif
		m_bPrimaryAttack = true;
		m_bUseServerRandomSeed = false;
	}

	FireBulletsInfo_t( int nShots, const Vector &vecSrc, const Vector &vecDir, const Vector &vecSpread, float flDistance, int nAmmoType, bool bPrimaryAttack = true )
	{
		m_iShots = nShots;
		m_vecSrc = vecSrc;
		m_vecDirShooting = vecDir;
		m_vecSpread = vecSpread;
		m_flDistance = flDistance;
		m_iAmmoType = nAmmoType;
		m_iTracerFreq = 4;
		m_flDamage = 0;
		m_iPlayerDamage = 0;
		m_pAttacker = NULL;
		m_nFlags = 0;
		m_pAdditionalIgnoreEnt = NULL;
		m_flDamageForceScale = 1.0f;
		m_bPrimaryAttack = bPrimaryAttack;
		m_bUseServerRandomSeed = false;
	}

	int m_iShots;
	Vector m_vecSrc;
	Vector m_vecDirShooting;
	Vector m_vecSpread;
	float m_flDistance;
	int m_iAmmoType;
	int m_iTracerFreq;
	float m_flDamage;
	int m_iPlayerDamage;	// Damage to be used instead of m_flDamage if we hit a player
	int m_nFlags;			// See FireBulletsFlags_t
	float m_flDamageForceScale;
	CBaseEntity *m_pAttacker;
	CBaseEntity *m_pAdditionalIgnoreEnt;
	bool m_bPrimaryAttack;
	bool m_bUseServerRandomSeed;
};

// Purpose: Data for making the MOVETYPE_STEP entities appear to simulate every frame
//  We precompute the simulation and then meter it out each tick during networking of the 
//  entities origin and orientation.  Uses a bit more bandwidth, but it solves the NPCs interacting
//  with elevators/lifts bugs.
struct StepSimulationStep
{
	int			nTickCount;
	Vector		vecOrigin;
	Quaternion	qRotation;
};

struct StepSimulationData
{
	// Are we using the Step Simulation Data
	bool		m_bOriginActive;
	bool		m_bAnglesActive;

	// This is the pre-pre-Think position, orientation (Quaternion) and tick count
	StepSimulationStep	m_Previous2;

	// This is the pre-Think position, orientation (Quaternion) and tick count
	StepSimulationStep	m_Previous;

	// This is a potential mid-think position, orientation (Quaternion) and tick count
	// Used to mark motion discontinuities that happen between thinks
	StepSimulationStep	m_Discontinuity;

	// This is the goal or post-Think position and orientation (and Quaternion for blending) and next think time tick
	StepSimulationStep	m_Next;
	QAngle		m_angNextRotation;

	// This variable is used so that we only compute networked origin/angles once per tick
	int			m_nLastProcessTickCount;
	// The computed/interpolated network origin/angles to use
	Vector		m_vecNetworkOrigin;
	QAngle		m_angNetworkAngles;
};

#include "soundflags.h"

struct CSoundParameters;
typedef short HSOUNDSCRIPTHANDLE;
// Purpose: Aggregates and sets default parameters for EmitSound function calls
struct EmitSound_t
{
	EmitSound_t() :
		m_nChannel( 0 ),
		m_pSoundName( 0 ),
		m_flVolume( VOL_NORM ),
		m_SoundLevel( SNDLVL_NONE ),
		m_nFlags( 0 ),
		m_nPitch( PITCH_NORM ),
		m_nSpecialDSP( 0 ),
		m_pOrigin( 0 ),
		m_flSoundTime( 0.0f ),
		m_pflSoundDuration( 0 ),
		m_bEmitCloseCaption( true ),
		m_bWarnOnMissingCloseCaption( false ),
		m_bWarnOnDirectWaveReference( false ),
		m_nSpeakerEntity( -1 ),
		m_UtlVecSoundOrigin(),
		m_hSoundScriptHandle( -1 )
	{
	}

	EmitSound_t( const CSoundParameters &src );

	int							m_nChannel;
	char const					*m_pSoundName;
	float						m_flVolume;
	soundlevel_t				m_SoundLevel;
	int							m_nFlags;
	int							m_nPitch;
	int							m_nSpecialDSP;
	const Vector				*m_pOrigin;
	float						m_flSoundTime; ///< NOT DURATION, but rather, some absolute time in the future until which this sound should be delayed
	float						*m_pflSoundDuration;
	bool						m_bEmitCloseCaption;
	bool						m_bWarnOnMissingCloseCaption;
	bool						m_bWarnOnDirectWaveReference;
	int							m_nSpeakerEntity;
	mutable CUtlVector< Vector >	m_UtlVecSoundOrigin;  ///< Actual sound origin(s) (can be multiple if sound routed through speaker entity(ies) )
	mutable HSOUNDSCRIPTHANDLE		m_hSoundScriptHandle;
};

// Multiplayer specific defines

// Maximum number of points that a control point may need owned to be cappable

// The maximum number of teams the control point system knows how to deal with

// Maximum length of the cap layout string

// Maximum length of the current round printname

// Maximum length of the current round name

// Score added to the team score for a round win

enum
{
	CP_WARN_NORMAL = 0,
	CP_WARN_FINALCAP,
	CP_WARN_NO_ANNOUNCEMENTS
};

// YWB:  3/12/2007
// Changing the following #define for Prediction Error checking (See gamemovement.cpp for overview) will to 1 or 2 enables the system, 0 turns it off
// Level 1 enables it, but doesn't force "full precision" networking, so you can still get lots of errors in position/velocity/etc.
// Level 2 enables it but also forces origins/angles to be sent full precision, so other fields can be error / tolerance checked
// NOTE:  This stuff only works on a listen server since it punches a hole from the client .dll to server .dll!!!
#define PREDICTION_ERROR_CHECK_LEVEL 0

// Round timer states
enum
{
	RT_STATE_SETUP,		// Timer is in setup mode
	RT_STATE_NORMAL,	// Timer is in normal mode
};

enum
{
	SIMULATION_TIME_WINDOW_BITS = 8,
};

// Commentary Mode

// The player's method of starting / stopping commentary
#ifdef GAME_HAS_NO_USE_KEY
#define COMMENTARY_BUTTONS		(IN_ATTACK | IN_ATTACK2 | IN_USE)
#endif

enum
{
	HILL_TYPE_NONE = 0,
	HILL_TYPE_UPHILL,
	HILL_TYPE_DOWNHILL,
};

#define NOINTERP_PARITY_MAX_BITS	2

// Generic activity lookup support
enum
{
	kActivityLookup_Unknown = -2,			// hasn't been searched for
	kActivityLookup_Missing = -1,			// has been searched for but wasn't found
};

class CPhysCollide;
struct collidelist_t
{
	const CPhysCollide	*pCollide;
	Vector			origin;
	QAngle			angles;
};
