//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The client's sound envelope controller. Managed runs the ramps, the sound list and the
// command queue; a CSoundPatch here is the engine's half: a filter, an entity index, a
// sound-script lookup and an EmitSound_t. A patch crosses as an int handle.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/dbg.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "soundenvelope.h"
#include "c_recipientfilter.h"
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "igamerules.h"
#include "tier1/utlvector.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "client_entity.h"
#include "const.h"
#include "irecipientfilter.h"
#include "shared_classnames.h"
#include "shareddefs.h"
#include "soundflags.h"
#include "string_t.h"
#include "client_sound.h"
#include "imanaged_areas_client.h"
#include "shell_standalone_client.h"

namespace
{

// A copy of the caller's filter: it is usually a stack object, and the patch outlives it.
class CClientCopyFilter : public IRecipientFilter
{
public:
	CClientCopyFilter() : m_bActive( false ), m_bReliable( false ), m_bInitMessage( false ) {}

	void Init( IRecipientFilter *pSrc )
	{
		m_bActive = true;
		m_bReliable = pSrc->IsReliable();
		m_bInitMessage = pSrc->IsInitMessage();
		for ( int i = 0; i < pSrc->GetRecipientCount(); i++ )
		{
			const int index = pSrc->GetRecipientIndex( i );
			if ( index >= 0 )
				m_Recipients.AddToTail( index );
		}
	}

	bool IsActive() const { return m_bActive; }
	virtual bool IsReliable() const { return m_bReliable; }
	virtual bool IsInitMessage() const { return m_bInitMessage; }
	virtual int GetRecipientCount() const { return m_Recipients.Count(); }
	virtual int GetRecipientIndex( int slot ) const { return m_Recipients[ slot ]; }

private:
	bool m_bActive;
	bool m_bReliable;
	bool m_bInitMessage;
	CUtlVector< int > m_Recipients;
};

} // namespace

class CSoundPatch
{
public:
	CSoundPatch()
		: m_nHandle( 0 ), m_nEntIndex( -1 ), m_nChannel( CHAN_AUTO ), m_SoundLevel( SNDLVL_NORM ),
		  m_flScriptVolume( 1.0f ), m_nBaseFlags( 0 ), m_bPlaying( false ), m_flCaptionDuration( 2.0f )
	{
		m_szSoundName[0] = 0;
		m_szScriptName[0] = 0;
	}

	// A sound script resolves to its wave, level and channel; its volume becomes the
	// multiplier the envelope scales.
	void Init( IRecipientFilter *pFilter, int nEntIndex, int nChannel, const char *pszSoundName,
	           soundlevel_t soundlevel )
	{
		m_nEntIndex = nEntIndex;
		m_nChannel = nChannel;
		CSoundParameters params;
		Q_strncpy( m_szScriptName, pszSoundName, sizeof( m_szScriptName ) );
		if ( !Q_stristr( pszSoundName, ".wav" ) && !Q_stristr( pszSoundName, ".mp3" ) &&
		     ClientSound::GetParameters( pszSoundName, params, NULL ) )
		{
			m_flScriptVolume = params.volume;
			pszSoundName = params.soundname;
			m_SoundLevel = params.soundlevel;
			m_nChannel = params.channel;
		}
		else
		{
			m_flScriptVolume = 1.0f;
			m_SoundLevel = soundlevel;
		}
		Q_strncpy( m_szSoundName, pszSoundName, sizeof( m_szSoundName ) );
		m_bPlaying = false;
		m_Filter.Init( pFilter );
	}

	int Handle() const { return m_nHandle; }
	void SetHandle( int nHandle ) { m_nHandle = nHandle; }
	void SetCaptionDuration( float flDuration ) { m_flCaptionDuration = flDuration; }
	void SetBaseFlags( int nFlags ) { m_nBaseFlags = nFlags; }
	const char *Name() const { return m_szSoundName; }
	bool FilterIsActive() const { return m_Filter.IsActive(); }
	int EntIndex() const { return m_nEntIndex; }

	// One emit as managed decided it: a start (bEmitCaption, base flags) or a change
	// (SND_CHANGE_VOL always).
	void Emit( float flVolume01, float flPitch, int nFlags, float flStartTime, bool bEmitCaption )
	{
		if ( m_nEntIndex < 0 || !m_Filter.IsActive() )
			return;

		EmitSound_t ep;
		ep.m_nChannel = m_nChannel;
		ep.m_pSoundName = m_szSoundName;
		ep.m_flVolume = m_flScriptVolume * flVolume01;
		ep.m_SoundLevel = m_SoundLevel;
		ep.m_nFlags = nFlags | m_nBaseFlags;
		ep.m_nPitch = (int)flPitch;
		if ( bEmitCaption )
		{
			ep.m_bEmitCloseCaption = false;
			if ( flStartTime != 0.0f )
				ep.m_flSoundTime = flStartTime;
		}

		ClientSound::Emit( m_Filter, m_nEntIndex, ep );

		if ( bEmitCaption )
		{
			ClientSound::EmitCloseCaption( m_Filter, m_nEntIndex, m_szScriptName, m_flCaptionDuration, true );
			m_bPlaying = true;
		}
	}

	void Stop()
	{
		if ( !m_bPlaying )
			return;
		if ( m_nEntIndex >= 0 )
			ClientSound::Stop( m_nEntIndex, m_nChannel, m_szSoundName );
		m_bPlaying = false;
	}

private:
	int               m_nHandle;
	int               m_nEntIndex;
	int               m_nChannel;
	soundlevel_t      m_SoundLevel;
	float             m_flScriptVolume;
	int               m_nBaseFlags;
	bool              m_bPlaying;
	float             m_flCaptionDuration;
	char              m_szSoundName[ 256 ];
	char              m_szScriptName[ 256 ];
	CClientCopyFilter m_Filter;
};

class CShellClientSoundController : public CSoundEnvelopeController
{
public:
	CShellClientSoundController() : m_nNextHandle( 1 ) {}

	CSoundPatch *Resolve( int nHandle )
	{
		for ( int i = 0; i < m_Patches.Count(); i++ )
		{
			if ( m_Patches[ i ]->Handle() == nHandle )
				return m_Patches[ i ];
		}
		return NULL;
	}

	void Free( int nHandle )
	{
		CSoundPatch *pSound = Resolve( nHandle );
		if ( !pSound )
			return;
		m_Patches.FindAndRemove( pSound );
		delete pSound;
	}

	virtual void SystemReset() OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			pApi->ClientSoundControllerReset();
	}

	// The client module steps the managed controller once a frame.
	virtual void SystemUpdate() OVERRIDE {}

	virtual void Play( CSoundPatch *pSound, float volume, float pitch, float flStartTime ) OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			if ( pSound ) pApi->ClientSoundControllerPlay( pSound->Handle(), volume, pitch, flStartTime );
	}

	virtual void CommandAdd( CSoundPatch *pSound, float executeDeltaTime, soundcommands_t command,
	                         float commandTime, float value ) OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			if ( pSound ) pApi->ClientSoundControllerCommandAdd( pSound->Handle(), executeDeltaTime, (int32_t)command, commandTime, value );
	}

	virtual void CommandClear( CSoundPatch *pSound ) OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			if ( pSound ) pApi->ClientSoundControllerCommandClear( pSound->Handle() );
	}

	virtual void Shutdown( CSoundPatch *pSound ) OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			if ( pSound ) pApi->ClientSoundControllerShutdown( pSound->Handle() );
	}

	virtual CSoundPatch *SoundCreate( IRecipientFilter &filter, int nEntIndex, const char *pSoundName ) OVERRIDE
	{
		return Build( filter, nEntIndex, CHAN_AUTO, pSoundName, SNDLVL_NORM );
	}

	virtual CSoundPatch *SoundCreate( IRecipientFilter &attnFilter, int nEntIndex, int channel, const char *pSoundName, float attenuation ) OVERRIDE
	{
		return Build( attnFilter, nEntIndex, channel, pSoundName, ATTN_TO_SNDLVL( attenuation ) );
	}

	virtual CSoundPatch *SoundCreate( IRecipientFilter &levelFilter, int nEntIndex, int channel, const char *pSoundName, soundlevel_t soundlevel ) OVERRIDE
	{
		return Build( levelFilter, nEntIndex, channel, pSoundName, soundlevel );
	}

	// No vision-filter translation on this overload, as stock's.
	virtual CSoundPatch *SoundCreate( IRecipientFilter &filter, int nEntIndex, const EmitSound_t &es ) OVERRIDE
	{
		CSoundPatch *pSound = Track( filter, nEntIndex, es.m_nChannel, es.m_pSoundName, es.m_SoundLevel );
		SoundChangeVolume( pSound, es.m_flVolume, 0 );
		SoundChangePitch( pSound, es.m_nPitch, 0 );
		if ( es.m_nFlags & SND_SHOULDPAUSE )
			pSound->SetBaseFlags( SND_SHOULDPAUSE );
		return pSound;
	}

	virtual void SoundDestroy( CSoundPatch *pSound ) OVERRIDE
	{
		if ( !pSound )
			return;
		if ( IManagedSoundControllerClient *pApi = Ask() )
			pApi->ClientSoundControllerDestroy( pSound->Handle() );
		m_Patches.FindAndRemove( pSound );
		delete pSound;
	}

	virtual void SoundChangePitch( CSoundPatch *pSound, float pitchTarget, float deltaTime ) OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			if ( pSound ) pApi->ClientSoundControllerChangePitch( pSound->Handle(), pitchTarget, deltaTime );
	}

	virtual void SoundChangeVolume( CSoundPatch *pSound, float volumeTarget, float deltaTime ) OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			if ( pSound ) pApi->ClientSoundControllerChangeVolume( pSound->Handle(), volumeTarget, deltaTime );
	}

	// A zero-time destroying fade frees a native object, so it is handled here; every other
	// fade is managed's.
	virtual void SoundFadeOut( CSoundPatch *pSound, float deltaTime, bool destroyOnFadeout ) OVERRIDE
	{
		if ( !pSound )
			return;
		if ( destroyOnFadeout && deltaTime == 0.0f )
		{
			SoundDestroy( pSound );
			return;
		}
		if ( IManagedSoundControllerClient *pApi = Ask() )
			pApi->ClientSoundControllerFadeOut( pSound->Handle(), deltaTime, destroyOnFadeout ? 1 : 0 );
	}

	virtual float SoundGetPitch( CSoundPatch *pSound ) OVERRIDE
	{
		IManagedSoundControllerClient *pApi = Ask();
		return ( pApi && pSound ) ? pApi->ClientSoundControllerGetPitch( pSound->Handle() ) : 0.0f;
	}

	virtual float SoundGetVolume( CSoundPatch *pSound ) OVERRIDE
	{
		IManagedSoundControllerClient *pApi = Ask();
		return ( pApi && pSound ) ? pApi->ClientSoundControllerGetVolume( pSound->Handle() ) : 0.0f;
	}

	virtual float SoundPlayEnvelope( CSoundPatch *pSound, soundcommands_t soundCommand, envelopePoint_t *points, int numPoints ) OVERRIDE
	{
		IManagedSoundControllerClient *pApi = Ask();
		if ( !pApi || !pSound || !points || numPoints <= 0 )
			return 0.0f;
		return pApi->ClientSoundControllerPlayEnvelope( pSound->Handle(), (int32_t)soundCommand,
		                                                &points[0].amplitudeMin, numPoints );
	}

	virtual float SoundPlayEnvelope( CSoundPatch *pSound, soundcommands_t soundCommand, envelopeDescription_t *envelope ) OVERRIDE
	{
		return envelope ? SoundPlayEnvelope( pSound, soundCommand, envelope->pPoints, envelope->nNumPoints ) : 0.0f;
	}

	// Server-only; the client never asks.
	virtual void CheckLoopingSoundsForPlayer( CBasePlayer *pPlayer ) OVERRIDE {}

	virtual string_t SoundGetName( CSoundPatch *pSound ) OVERRIDE
	{
		return pSound ? MAKE_STRING( pSound->Name() ) : NULL_STRING;
	}

	virtual void SoundSetCloseCaptionDuration( CSoundPatch *pSound, float flDuration ) OVERRIDE
	{
		if ( pSound )
			pSound->SetCaptionDuration( flDuration );
	}

private:
	CSoundPatch *Build( IRecipientFilter &filter, int nEntIndex, int nChannel, const char *pszSoundName,
	                    soundlevel_t soundlevel )
	{
		if ( GameRules() )
			pszSoundName = GameRules()->TranslateEffectForVisionFilter( "sounds", pszSoundName );
		return Track( filter, nEntIndex, nChannel, pszSoundName, soundlevel );
	}

	CSoundPatch *Track( IRecipientFilter &filter, int nEntIndex, int nChannel, const char *pszSoundName,
	                    soundlevel_t soundlevel )
	{
		CSoundPatch *pSound = new CSoundPatch;
		pSound->Init( &filter, nEntIndex, nChannel, pszSoundName, soundlevel );
		pSound->SetHandle( m_nNextHandle++ );
		m_Patches.AddToTail( pSound );
		if ( IManagedSoundControllerClient *pApi = Ask() )
			pApi->ClientSoundControllerCreate( pSound->Handle() );
		return pSound;
	}

	static IManagedSoundControllerClient *Ask() { return ManagedSoundControllerClient(); }

	CUtlVector< CSoundPatch * > m_Patches;
	int m_nNextHandle;
};

static CShellClientSoundController *ShellClientSoundController()
{
	static CShellClientSoundController s_Controller;
	return &s_Controller;
}

CSoundEnvelopeController &CSoundEnvelopeController::GetController()
{
	return *ShellClientSoundController();
}

void SourceSharp_ClientSoundPatchEmit( int32_t hPatch, float flVolume01, float flPitch, int32_t nFlags,
                                       float flStartTime, int32_t bEmitCaption )
{
	if ( CSoundPatch *pSound = ShellClientSoundController()->Resolve( hPatch ) )
		pSound->Emit( flVolume01, flPitch, nFlags, flStartTime, bEmitCaption != 0 );
}

void SourceSharp_ClientSoundPatchStop( int32_t hPatch )
{
	if ( CSoundPatch *pSound = ShellClientSoundController()->Resolve( hPatch ) )
		pSound->Stop();
}

void SourceSharp_ClientSoundPatchState( int32_t hPatch, int32_t *pnEntIndex, int32_t *pbFilterActive )
{
	CSoundPatch *pSound = ShellClientSoundController()->Resolve( hPatch );
	if ( pnEntIndex )
		*pnEntIndex = pSound ? pSound->EntIndex() : -1;
	if ( pbFilterActive )
		*pbFilterActive = ( pSound && pSound->FilterIsActive() ) ? 1 : 0;
}

void SourceSharp_ClientSoundPatchFree( int32_t hPatch )
{
	ShellClientSoundController()->Free( hPatch );
}

// A sound patch on the entity, as stock's client PhysFrictionSound builds it, or 0.
// Handle 0 is the world, which managed passes for a clientside physics prop: it plays at
// entindex 0 at the world's origin.
int32_t SourceSharp_ClientSoundPatchCreate( uint32_t hEntity, int32_t nChannel,
                                            const char *pSoundName, int32_t nSoundLevel )
{
	if ( !pSoundName )
		return 0;

	int nEntIndex = 0;
	Vector vecOrigin = vec3_origin;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );
	if ( pEntity )
	{
		nEntIndex = pEntity->entindex();
		if ( nEntIndex < 0 )
			nEntIndex = 0;
		vecOrigin = pEntity->GetAbsOrigin();
	}
	else if ( hEntity == INVALID_EHANDLE_INDEX || ( hEntity & ENT_ENTRY_MASK ) != 0 )
	{
		return 0;
	}

	const soundlevel_t level = (soundlevel_t)nSoundLevel;
	CPASAttenuationFilter filter( vecOrigin, level );
	CSoundPatch *pPatch = ShellClientSoundController()->SoundCreate( filter, nEntIndex, nChannel, pSoundName, level );
	return pPatch ? pPatch->Handle() : 0;
}
