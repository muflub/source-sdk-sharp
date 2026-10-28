//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
// Entity-free utilities from game/shared/util_shared.cpp, for both DLLs; the
// declarations stay in util_shared.h.

#ifdef CLIENT_DLL
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif

#include "convar.h"
#include "managed_convar_ref.h"
#ifdef CLIENT_DLL
#include "igamerules.h"
#else
#include "igamerules.h"
#endif
#ifdef CLIENT_DLL
#include "util_shared.h"
#endif

// The random stream, IEffects, the particle-system index and the per-side
// temp-entity dispatch the effect emitters below use.
#include <vstdlib/random.h>
#ifdef CLIENT_DLL
#include "sharedInterface.h"
#endif
#ifdef CLIENT_DLL
#include "IEffects.h"
#endif

#ifdef CLIENT_DLL
	#include "client_entity.h"
	#include <vgui/ILocalize.h>
	extern vgui::ILocalize *g_pVGuiLocalize;
	#include "c_te_effect_dispatch.h"
	// The slot table's index -> handle ask.
	#include "shell_cliententitylist.h"
#else
	#include "ss_effect_dispatch.h"
	using ss::CEffectData;
	#include "ss_effects.h"
#endif

#ifndef CLIENT_DLL
// The server half's replacements for cbase.h, util_shared.h and sharedInterface.h.
#include "ss_util.h"
#include "ss_shareddefs.h"
extern IUniformRandomStream *random_valve;
#define random random_valve
// shareddefs.h's names for the constants this file uses.
using ss::BLOOD_COLOR_RED;
using ss::BLOOD_COLOR_MECH;
using ss::DONT_BLEED;
#define LANGUAGE_GERMAN            ss::kLanguageGerman
#define TRACER_FLAG_WHIZ           ss::kTracerFlagWhiz
#define TRACER_FLAG_USEATTACHMENT  ss::kTracerFlagUseAttachment
#define TRACER_DONT_USE_ATTACHMENT ss::kTracerDontUseAttachment
// Defined by server.so: UTIL_Tracer and UTIL_BloodImpact below, UTIL_Smoke in
// server_util_library.cpp, GetParticleSystemIndex in shell_stringtables.cpp.
// Parameters unnamed: tests find the definitions below by text.
void UTIL_Tracer( const Vector &, const Vector &, int = 0, int = TRACER_DONT_USE_ATTACHMENT,
                  float = 0, bool = false, const char * = NULL, int = 0 );
void UTIL_BloodImpact( const Vector &, const Vector &, int, int );
void UTIL_Smoke( const Vector &, const float, const float );
int GetParticleSystemIndex( const char * );
#endif
#include "checksum_crc.h"
#include "entity_prediction_seed.h"
#include <vstdlib/random.h>
#include "ss_string.h"

// The violence convars are declared in C#; the predicate reads them through the
// engine's registry (see managed_convar_ref.h on why a plain ConVarRef would not).
static CManagedConVarRef	violence_hblood( "violence_hblood" );
static CManagedConVarRef	violence_hgibs( "violence_hgibs" );
static CManagedConVarRef	violence_ablood( "violence_ablood" );
static CManagedConVarRef	violence_agibs( "violence_agibs" );

bool UTIL_ShouldShowBlood( int color )
{
	if ( color != DONT_BLEED )
	{
		if ( color == BLOOD_COLOR_RED )
		{
			return violence_hblood.GetBool();
		}
		else
		{
			return violence_ablood.GetBool();
		}
	}
	return false;
}

// Makes a tracer effect using the old, non-particle tracer system.
void UTIL_Tracer( const Vector &vecStart, const Vector &vecEnd, int iEntIndex, 
				 int iAttachment, float flVelocity, bool bWhiz, const char *pCustomTracerName, int iParticleID )
{
	CEffectData data;
	data.m_vStart = vecStart;
	data.m_vOrigin = vecEnd;
#ifdef CLIENT_DLL
	// Stock's EntIndexToHandle, through the slot table.
	data.m_hEntity = SourceSharp_ClientHandleFromIndex( iEntIndex );
#else
	data.m_nEntIndex = iEntIndex;
#endif
	data.m_flScale = flVelocity;
	data.m_nHitBox = iParticleID;

	// Flags
	if ( bWhiz )
	{
		data.m_fFlags |= TRACER_FLAG_WHIZ;
	}

	if ( iAttachment != TRACER_DONT_USE_ATTACHMENT )
	{
		data.m_fFlags |= TRACER_FLAG_USEATTACHMENT;
		data.m_nAttachmentIndex = iAttachment;
	}

	// Fire it off
	if ( pCustomTracerName )
	{
		DispatchEffect( pCustomTracerName, data );
	}
	else
	{
		DispatchEffect( "Tracer", data );
	}
}

#ifndef CLIENT_DLL
// Server only; g_Language is declared in C# and read by name.
static CManagedConVarRef s_Language( "g_Language" );

void UTIL_BloodDrips( const Vector &origin, const Vector &direction, int color, int amount )
{
	if ( !UTIL_ShouldShowBlood( color ) )
		return;

	if ( color == DONT_BLEED || amount == 0 )
		return;

	if ( s_Language.GetInt() == LANGUAGE_GERMAN && color == BLOOD_COLOR_RED )
		color = 0;

	if ( g_pGameRules->IsMultiplayer() )
	{
		// scale up blood effect in multiplayer for better visibility
		amount *= 5;
	}

	if ( amount > 255 )
		amount = 255;

	if (color == BLOOD_COLOR_MECH)
	{
		ss::Effects()->Sparks(origin);
		if (random->RandomFloat(0, 2) >= 1)
		{
			UTIL_Smoke(origin, random->RandomInt(10, 15), 10);
		}
	}
	else
	{
		// Normal blood impact
		UTIL_BloodImpact( origin, direction, color, amount );
	}
}
#endif // !CLIENT_DLL

void UTIL_BloodImpact( const Vector &pos, const Vector &dir, int color, int amount )
{
	CEffectData	data;

	data.m_vOrigin = pos;
	data.m_vNormal = dir;
	data.m_flScale = (float)amount;
	data.m_nColor = (unsigned char)color;

	DispatchEffect( "bloodimpact", data );
}
