//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Stock game constants server TUs use, with stock's values (checked against the
// upstream headers by an oracle test). Stock's macros become k-names here, so a
// TU that still includes the stock header is not rewritten by them.

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

// shareddefs.h: UTIL_Tracer's flags and its "no attachment" index.
enum
{
	kTracerFlagWhiz          = 0x0001,
	kTracerFlagUseAttachment = 0x0002,
	kTracerDontUseAttachment = -1,
};

// shareddefs.h's DMG_* damage-type bits.
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

// shareddefs.h's MAX_PLAYERS for a build that is not CSTRIKE_DLL, TF_DLL or HL2MP.
enum
{
	kMaxPlayers = 33,
};

// util_shared.h: the default language id, and the one whose blood is green.
enum
{
	kLanguageEnglish = 0,
	kLanguageGerman = 1,
};

// util.h's MAX_SPHERE_QUERY (the client's value differs).
enum
{
	kMaxSphereQuery = 512,
};

// shareddefs.h's HIDEHUD_BITCOUNT for non-TF builds.
enum
{
	kHideHudBitCount = 12,
};

// shareddefs.h's DAMAGE_YES.
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

// baseentity_shared.h's VPHYSICS_MAX_OBJECT_LIST_COUNT.
enum
{
	kMaxPhysicsObjectList = 1024,
};

// shareddefs.h's data-object kinds server.so registers, and their count.
enum
{
	POSITIONWATCHER      = 4,
	VPHYSICSWATCHER      = 7,
	NUM_DATAOBJECT_TYPES = 8,
};

} // namespace ss
