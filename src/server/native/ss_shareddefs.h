//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the stock game constants server.so's own code uses ==//
//
// plan_server_novalve.md P5. A translation unit built without game/ headers
// still needs a handful of stock's numbers: blood colours and tracer flags
// (game/shared/shareddefs.h), a language id (game/shared/util_shared.h) and two
// player-relationship codes (game/shared/gamerules.h), surface materials
// (game/shared/decals.h), a list bound (game/shared/baseentity_shared.h). They
// are restated here,
// in namespace ss and with stock's values, ONLY for the constants server TUs
// use -- and ss_shareddefs_oracle_test reads the pristine upstream headers and
// checks every one, so a restated value cannot drift from its source.
//=============================================================================//

#pragma once

namespace ss
{

// shareddefs.h: an entity's blood colour (DONT_BLEED for none).
enum
{
	DONT_BLEED       = -1,
	BLOOD_COLOR_RED  = 0,
	BLOOD_COLOR_MECH = 3,
};

// shareddefs.h: UTIL_Tracer's flags and its "no attachment" index
// (TRACER_FLAG_WHIZ, TRACER_FLAG_USEATTACHMENT, TRACER_DONT_USE_ATTACHMENT).
// Stock's are macros, so these take k-names: a macro of the same name in a TU
// that still includes shareddefs.h would rewrite an enumerator spelled like it.
enum
{
	kTracerFlagWhiz          = 0x0001,
	kTracerFlagUseAttachment = 0x0002,
	kTracerDontUseAttachment = -1,
};

// shareddefs.h's DMG_* damage-type bits (macros there, so k-names here), the
// ones server TUs use. Each is DMG_<NAME> with its words run together.
enum
{
	kDmgGeneric                  = 0,
	kDmgCrush                    = 1 << 0,
	kDmgBullet                   = 1 << 1,
	kDmgBurn                     = 1 << 3,
	kDmgFall                     = 1 << 5,
	kDmgBlast                    = 1 << 6,
	kDmgClub                     = 1 << 7,
	kDmgShock                    = 1 << 8,
	kDmgSonic                    = 1 << 9,
	kDmgPreventPhysicsForce      = 1 << 11,
	kDmgDrown                    = 1 << 14,
	kDmgParalyze                 = 1 << 15,
	kDmgNervegas                 = 1 << 16,
	kDmgPoison                   = 1 << 17,
	kDmgRadiation                = 1 << 18,
	kDmgDrownrecover             = 1 << 19,
	kDmgAcid                     = 1 << 20,
	kDmgSlowburn                 = 1 << 21,
	kDmgPhysgun                  = 1 << 23,
	kDmgPlasma                   = 1 << 24,
};

// shareddefs.h's MAX_PLAYERS (a macro): the arm a build that defines none of
// CSTRIKE_DLL / TF_DLL / HL2MP takes, which is this one.
enum
{
	kMaxPlayers = 33,
};

// util_shared.h's LANGUAGE_ENGLISH and LANGUAGE_GERMAN (macros there too): the
// default language id, and the one whose blood is green.
enum
{
	kLanguageEnglish = 0,
	kLanguageGerman = 1,
};

// util.h's MAX_SPHERE_QUERY (a macro): how many entities a sphere query
// collects on the server (cdll_util.h's client value differs).
enum
{
	kMaxSphereQuery = 512,
};

// shareddefs.h's HIDEHUD_BITCOUNT (a macro): how many HIDEHUD_* bits DT_Local
// sends. The #else arm: TF builds send 18, this one 12.
enum
{
	kHideHudBitCount = 12,
};

// shareddefs.h's DAMAGE_YES (a macro): an entity's m_takedamage when it takes
// damage.
enum
{
	kDamageYes = 2,
};

// soundent.h: the sound-hint bits and channel RadiusDamage announces a blast with.
enum
{
	SOUND_COMBAT                 = 0x00000001,
	SOUND_CONTEXT_EXPLOSION      = 0x02000000,
	SOUNDENT_CHANNEL_UNSPECIFIED = 0,
};

// rumble_shared.h's RUMBLE_FLAGS_NONE (a macro): a rumble effect with no flags.
enum
{
	kRumbleFlagsNone = 0x0000,
};

// gamerules.h: what IGameRules::PlayerRelationship answers.
enum
{
	GR_NOTTEAMMATE = 0,
	GR_TEAMMATE    = 1,
};

// decals.h's CHAR_TEX_* surface materials (macros there, so k-names here), the
// ones physics_shared.cpp's impact and friction sounds test.
enum
{
	kCharTexConcrete = 'C',
	kCharTexDirt     = 'D',
	kCharTexGrate    = 'G',
	kCharTexMetal    = 'M',
};

// baseentity_shared.h's VPHYSICS_MAX_OBJECT_LIST_COUNT (a macro): the most
// physics objects one entity's list is read into.
enum
{
	kMaxPhysicsObjectList = 1024,
};

// shareddefs.h's data-object kinds: the two server.so registers (as refusals,
// physics_shared.cpp) and the count CDataObjectAccessSystem sizes against.
enum
{
	POSITIONWATCHER      = 4,
	VPHYSICSWATCHER      = 7,
	NUM_DATAOBJECT_TYPES = 8,
};

} // namespace ss
