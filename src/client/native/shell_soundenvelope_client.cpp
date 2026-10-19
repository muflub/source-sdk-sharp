//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client's sound envelope controller, a shim =====//
// SS_REASON a  a vtable the engine calls through
//
// Sound cluster 17 (plan_client_novalve C2, lane c2-sims). The client's
// CSoundControllerImp (game/shared/soundenvelope.cpp) is gone from client.so:
// the RAMPS -- a volume or a pitch approaching a target at a rate -- the sound
// list, the time-sorted command queue and the envelope expansion are C#'s,
// SourceSharp.Shared.SoundEnvelopeController, the same class the server's
// shell forwards to (shell_soundenvelope.cpp there), held bit for bit to the
// stock client controller by SoundEnvelopeOracleTests. The client module
// steps it once a frame.
//
// What is here is the half that is genuinely the engine's, the server
// shell's split: a CSoundPatch is a recipient filter, an entity index, a
// sound-script lookup and an EmitSound_t. The controller below answers
// CSoundEnvelopeController::GetController() so its compiled callers -- the
// client physics.cpp's friction sounds -- are unedited, and forwards every
// decision to IManagedSoundControllerClient. A patch crosses as an int handle.
//
// THE ENTITY IS AN INDEX. Stock stored a handle to the entity at that index
// and dropped a patch whose entity had gone. Its one client caller,
// physics.cpp, always passes 0 (a client-side object has no index; the world
// plays its sound), so the index is kept and reported as stock reported it.
//
// NO FILE-SCOPE STATE: the patch table is a member of the controller, which is
// a function-local static (the server shell's shape).
//=============================================================================//

#include "cbase.h"
#include "soundenvelope.h"
#include "c_recipientfilter.h"
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "gamesound.h"
#include "gamerules.h"
#include "imanaged_areas_client.h"
#include "utlvector.h"
#include "vphysics_interface.h"
#include "physics_shared.h"
#include "shell_standalone_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{

// A copy of the filter the caller built: SoundCreate's filter is almost always
// a stack object, and the patch outlives it. Stock's CCopyRecipientFilter,
// reduced to what the client uses.
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

	// Stock's Init: a sound script is resolved to its wave, its level and its
	// channel, and its volume becomes the multiplier the envelope scales.
	void Init( IRecipientFilter *pFilter, int nEntIndex, int nChannel, const char *pszSoundName,
	           soundlevel_t soundlevel )
	{
		m_nEntIndex = nEntIndex;
		m_nChannel = nChannel;
		CSoundParameters params;
		Q_strncpy( m_szScriptName, pszSoundName, sizeof( m_szScriptName ) );
		if ( !Q_stristr( pszSoundName, ".wav" ) && !Q_stristr( pszSoundName, ".mp3" ) &&
		     GameSound_GetParameters( pszSoundName, params, NULL ) )
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

	// One emit, as managed decided it: stock's StartSound (bEmitCaption, with
	// the base flags) or Update's change (SND_CHANGE_VOL always).
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

		GameSound_Emit( m_Filter, m_nEntIndex, ep );

		if ( bEmitCaption )
		{
			GameSound_EmitCloseCaption( m_Filter, m_nEntIndex, m_szScriptName, ep.m_UtlVecSoundOrigin,
			                            m_flCaptionDuration, true );
			m_bPlaying = true;
		}
	}

	void Stop()
	{
		if ( !m_bPlaying )
			return;
		if ( m_nEntIndex >= 0 )
			GameSound_Stop( m_nEntIndex, m_nChannel, m_szSoundName );
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

	// ---- CSoundEnvelopeController: every decision is managed's ----

	virtual void SystemReset() OVERRIDE
	{
		if ( IManagedSoundControllerClient *pApi = Ask() )
			pApi->ClientSoundControllerReset();
	}

	// The client module steps the controller once a frame.
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

	// Stock's: no vision-filter translation on this overload, and the
	// EmitSound_t's volume and pitch set at once.
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

	// Stock's zero-time destroying fade is a destroy, and a destroy frees a
	// native object, so it is answered here; every other fade is managed's.
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

	// A server-only question (a player joining hears the looping sounds);
	// stock's client never asks it.
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

//-----------------------------------------------------------------------------
// IShimEngine's patch slots, the client's bodies: what managed tells a patch.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// WHAT A MANAGED LOOPING SOUND NEEDS, on the client (plan_client_novalve C6,
// cluster 9; lane c6-input): the server's SoundPatchCreate slot
// (shell_soundenvelope.cpp) with this side's body, so the shared
// FrictionSounds -- stock's PhysFrictionSound -- scrapes on either side. The
// policy is C#'s; a patch on an entity is the engine's. A script's parameters
// and a surface's scrape names read the engine's interfaces through their
// forwarders since 2026-10-19.
//-----------------------------------------------------------------------------

// A sound patch on the entity, as stock's client PhysFrictionSound builds it:
// SoundCreate( CPASAttenuationFilter( entity, level ), entindex, channel, name,
// level ), and "clientside created entites doesn't have a valid entindex, let
// 'world' play the sound for them". The handle the managed controller tracks
// it by, or 0.
//
// The WORLD's handle (entry 0) is that arm asked for by name: managed hands it
// for a clientside physics prop, which is no shell. The world is no shell
// either, so it is entindex 0 at the world's origin. (The client's attenuation
// filter is every local player whatever its origin.)
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
