//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — one emitted sound's parameters, as server.so owns them
//
// plan_server_novalve.md P5 (lane SP). game/shared/shareddefs.h's EmitSound_t
// is the argument GameSound_Emit (gamesound.h) takes; no engine reads it
// (IEngineSound::EmitSound takes its fields one by one), so on the server it is
// server.so's own type, in namespace ss, on public/ headers alone. The client
// keeps shareddefs.h's.
//
// Same members, same defaults. The CSoundParameters constructor is defined in
// sound_emitter.cpp (it reads gpGlobals for the delay).
//=============================================================================//

#ifndef SOURCESHARP_SS_EMITSOUND_H
#define SOURCESHARP_SS_EMITSOUND_H
#ifdef _WIN32
#pragma once
#endif

#include "tier1/utlvector.h"
#include "mathlib/vector.h"
#include "soundflags.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

namespace ss
{

struct EmitSound_t
{
	EmitSound_t()
		: m_nChannel( 0 ), m_pSoundName( 0 ), m_flVolume( VOL_NORM ), m_SoundLevel( SNDLVL_NONE ),
		  m_nFlags( 0 ), m_nPitch( PITCH_NORM ), m_nSpecialDSP( 0 ), m_pOrigin( 0 ),
		  m_flSoundTime( 0.0f ), m_pflSoundDuration( 0 ),
		  m_bEmitCloseCaption( true ), m_bWarnOnMissingCloseCaption( false ),
		  m_bWarnOnDirectWaveReference( false ), m_nSpeakerEntity( -1 ),
		  m_UtlVecSoundOrigin(), m_hSoundScriptHandle( -1 )
	{
	}

	// What and how loud.
	int				m_nChannel;
	const char		*m_pSoundName;
	float			m_flVolume;
	soundlevel_t	m_SoundLevel;
	int				m_nFlags, m_nPitch, m_nSpecialDSP;

	// Where and when: m_flSoundTime is an absolute time to delay until, not a
	// duration; m_pflSoundDuration receives the length when asked.
	const Vector	*m_pOrigin;
	float			m_flSoundTime;
	float			*m_pflSoundDuration;

	// Captions and diagnostics.
	bool			m_bEmitCloseCaption, m_bWarnOnMissingCloseCaption, m_bWarnOnDirectWaveReference;

	int				m_nSpeakerEntity;

	// Filled by the engine with where the sound actually played from (more than
	// one when routed through speakers); the script handle is cached on first use.
	mutable CUtlVector< Vector >		m_UtlVecSoundOrigin;
	mutable HSOUNDSCRIPTHANDLE			m_hSoundScriptHandle;
};

} // namespace ss

#endif // SOURCESHARP_SS_EMITSOUND_H
