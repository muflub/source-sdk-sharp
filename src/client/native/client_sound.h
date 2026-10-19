//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client's sounds, as native's callers reach them ===//
//
// sound_emitter.cpp's client emit path is managed (SourceSharp.Client.Sounds:
// ClientSoundEmitter over the shared SoundEmitterCore). What native code still
// makes sounds -- a bullet impact (shell_impact_effects.cpp), a sound patch
// (shell_soundenvelope_client.cpp), the team-255 broadcast (shell_client.cpp),
// the RPG's shoot-down (shell_explosion_overlay.cpp) -- hands its call to the
// managed emitter across IManagedSoundEmitterClient, through the inlines
// below. They CARRY OUT, and decide nothing: each one copies the filter the
// caller built into the length-prefixed slot buffer the slot takes
// ([count, flags, slots...]), spells the EmitSound_t's fields out, and makes
// the one crossing. Which sound plays, from where and with what caption is
// the managed emitter's.
//
// GameSound_GetParameters' one native caller (a sound patch's Init) asks the
// script table directly, as stock's body did: the actor's gender and the
// table's answer, no choice of its own.
//
// With no managed side (outside a load) nothing plays, as a missing shim's did.
//=============================================================================//
#ifndef CLIENT_SOUND_H
#define CLIENT_SOUND_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>

#include "const.h"
#include "irecipientfilter.h"
#include "mathlib/vector.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "imanaged_areas_client.h"

extern ISoundEmitterSystemBase *soundemitterbase;

namespace ClientSound
{

// IManagedSoundEmitterClient::ClientSoundEmit's nOptions bits
// (SoundEmitterClientBinding's): the EmitSound_t's three flags, and the
// handle overloads' route, which skips the raw-wave arm.
enum
{
	OPTION_EMIT_CLOSE_CAPTION = 1,
	OPTION_WARN_ON_MISSING_CLOSE_CAPTION = 2,
	OPTION_WARN_ON_DIRECT_WAVE_REFERENCE = 4,
	OPTION_BY_HANDLE = 8,
};

// A filter's recipients as the slot reads them: [count, flags (1 reliable,
// 2 init message), slots...]. A client filter holds the viewer at most; the
// buffer holds as many as any filter can (ABSOLUTE_PLAYER_LIMIT).
struct Recipients_t
{
	int32_t m_n[ 2 + ABSOLUTE_PLAYER_LIMIT ];

	explicit Recipients_t( const IRecipientFilter &filter )
	{
		int c = filter.GetRecipientCount();
		if ( c < 0 )
			c = 0;
		if ( c > ABSOLUTE_PLAYER_LIMIT )
			c = ABSOLUTE_PLAYER_LIMIT;
		m_n[ 0 ] = c;
		m_n[ 1 ] = ( filter.IsReliable() ? 1 : 0 ) | ( filter.IsInitMessage() ? 2 : 0 );
		for ( int i = 0; i < c; i++ )
			m_n[ 2 + i ] = filter.GetRecipientIndex( i );
	}
};

inline int32_t Options( bool bEmitCloseCaption, bool bWarnOnMissingCloseCaption, bool bWarnOnDirectWaveReference )
{
	return ( bEmitCloseCaption ? OPTION_EMIT_CLOSE_CAPTION : 0 )
	       | ( bWarnOnMissingCloseCaption ? OPTION_WARN_ON_MISSING_CLOSE_CAPTION : 0 )
	       | ( bWarnOnDirectWaveReference ? OPTION_WARN_ON_DIRECT_WAVE_REFERENCE : 0 );
}

// GameSound_Emit( filter, entindex, params ): the EmitSound_t's own handle,
// which the first lookup fills in (stock's mutable m_hSoundScriptHandle).
inline void Emit( IRecipientFilter &filter, int iEntIndex, const EmitSound_t &ep )
{
	IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient();
	if ( !pApi )
		return;

	Recipients_t recipients( filter );
	int32_t nHandle = ep.m_hSoundScriptHandle;
	pApi->ClientSoundEmit( recipients.m_n, iEntIndex, ep.m_nChannel, ep.m_pSoundName, ep.m_flVolume,
	                       (int32_t)ep.m_SoundLevel, ep.m_nFlags, ep.m_nPitch, ep.m_nSpecialDSP,
	                       ep.m_pOrigin ? &ep.m_pOrigin->x : NULL, ep.m_flSoundTime, ep.m_pflSoundDuration,
	                       Options( ep.m_bEmitCloseCaption, ep.m_bWarnOnMissingCloseCaption,
	                                ep.m_bWarnOnDirectWaveReference ),
	                       ep.m_nSpeakerEntity, &nHandle );
	ep.m_hSoundScriptHandle = (HSOUNDSCRIPTHANDLE)nHandle;
}

// GameSound_Emit( filter, entindex, soundname, handle, origin, soundtime,
// duration ): stock's EmitSound_t of those four, the direct-wave warning on,
// by the caller's handle.
inline void Emit( IRecipientFilter &filter, int iEntIndex, const char *pszSoundName, HSOUNDSCRIPTHANDLE &handle,
                  const Vector *pOrigin = NULL, float flSoundTime = 0.0f, float *pflDuration = NULL )
{
	IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient();
	if ( !pApi || !pszSoundName )
		return;

	Recipients_t recipients( filter );
	int32_t nHandle = handle;
	pApi->ClientSoundEmit( recipients.m_n, iEntIndex, 0, pszSoundName, VOL_NORM, (int32_t)SNDLVL_NONE, 0, PITCH_NORM, 0,
	                       pOrigin ? &pOrigin->x : NULL, flSoundTime, pflDuration,
	                       Options( true, false, true ) | OPTION_BY_HANDLE, -1, &nHandle );
	handle = (HSOUNDSCRIPTHANDLE)nHandle;
}

// GameSound_Emit( filter, entindex, soundname, origin, soundtime, duration ):
// the same, by a handle of its own.
inline void Emit( IRecipientFilter &filter, int iEntIndex, const char *pszSoundName, const Vector *pOrigin = NULL,
                  float flSoundTime = 0.0f, float *pflDuration = NULL )
{
	HSOUNDSCRIPTHANDLE handle = SOUNDEMITTER_INVALID_HANDLE;
	Emit( filter, iEntIndex, pszSoundName, handle, pOrigin, flSoundTime, pflDuration );
}

// GameSound_Stop( entindex, channel, sample ).
inline void Stop( int iEntIndex, int iChannel, const char *pszSample )
{
	if ( IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient() )
		pApi->ClientSoundStop( iEntIndex, iChannel, pszSample );
}

// GameSound_EmitCloseCaption( filter, entindex, token, origins, duration,
// warnifmissing ): the origin list was never read on the client.
inline void EmitCloseCaption( IRecipientFilter &filter, int iEntIndex, const char *pszToken, float flDuration,
                              bool bWarnIfMissing )
{
	IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient();
	if ( !pApi || !pszToken )
		return;

	Recipients_t recipients( filter );
	pApi->ClientSoundEmitCloseCaption( recipients.m_n, iEntIndex, pszToken, flDuration, bWarnIfMissing ? 1 : 0 );
}

// GameSound_GetParameters( soundname, params, actormodel ): stock's body.
inline bool GetParameters( const char *pszSoundName, CSoundParameters &params, const char *pszActorModel )
{
	return soundemitterbase->GetParametersForSound( pszSoundName, params,
	                                                soundemitterbase->GetActorGender( pszActorModel ) );
}

} // namespace ClientSound

#endif // CLIENT_SOUND_H
