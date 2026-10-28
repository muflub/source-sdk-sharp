//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: Client-server neutral sound interface
//
// $NoKeywords: $
//=============================================================================//

#ifndef IENGINESOUND_H
#define IENGINESOUND_H

#ifdef _WIN32
#pragma once
#endif


#include "basetypes.h"
#include "interface.h"
#include "soundflags.h"
#include "irecipientfilter.h"
#include "utlvector.h"
#include "engine/SndInfo.h"

//-----------------------------------------------------------------------------
// forward declaration
//-----------------------------------------------------------------------------
class Vector;

// Handy defines for EmitSound
		// Sound being played inside a UI panel on the client
#define SOUND_FROM_LOCAL_PLAYER		-1
#define SOUND_FROM_WORLD			0



// These are used to feed a soundlevel to the sound system and have it use
// goldsrc-type attenuation. We should use this as little as possible and 
// phase it out as soon as possible.

// Take a regular sndlevel and convert it to compatibility mode.


// Take a compatibility-mode sndlevel and get the REAL sndlevel out of it.


// Tells if the given sndlevel is marked as compatibility mode.



// IAudioOutputStream from source2
// a separate stream that connects to the main output
class IAudioOutputStream
{
public:
	virtual ~IAudioOutputStream() {}
	// queue a set of samples for output.  
	// NOTE: nSampleCount is the number of samples being written to each channel, not the total # of all channels' samples
	virtual void unused_WriteAudioData() = 0;

	// change output volume (will be ramped linearly per sample over one mix quantum)
	virtual void unused_SetVolume() = 0;

	// how many samples are queued to play?
	virtual void unused_QueuedSampleCount() = 0;

	// how many samples can we write in a WriteAudioData call without truncation in the internal buffer?
	virtual void unused_MaxWriteSampleCount() = 0;

	// how many samples of latency
	virtual void unused_LatencySamplesCount() = 0;

	virtual void unused_Pause() = 0;
	virtual void unused_Resume() = 0;
};



//-----------------------------------------------------------------------------
// Client-server neutral effects interface
//-----------------------------------------------------------------------------
#define IENGINESOUND_CLIENT_INTERFACE_VERSION	"IEngineSoundClient003"
#define IENGINESOUND_SERVER_INTERFACE_VERSION	"IEngineSoundServer003"

struct AudioState_t;

abstract_class IEngineSound
{
public:
	// Precache a particular sample
	virtual bool PrecacheSound( const char *pSample, bool bPreload = false, bool bIsUISound = false ) = 0;
	virtual bool IsSoundPrecached( const char *pSample ) = 0;
	virtual void PrefetchSound( const char *pSample ) = 0;

	// Just loads the file header and checks for duration (not hooked up for .mp3's yet)
	// Is accessible to server and client though
	virtual float GetSoundDuration( const char *pSample ) = 0;  

	// Pitch of 100 is no pitch shift.  Pitch > 100 up to 255 is a higher pitch, pitch < 100
	// down to 1 is a lower pitch.   150 to 70 is the realistic range.
	// EmitSound with pitch != 100 should be used sparingly, as it's not quite as
	// fast (the pitchshift mixer is not native coded).

	// NOTE: setting iEntIndex to -1 will cause the sound to be emitted from the local
	// player (client-side only)
	virtual void EmitSound( IRecipientFilter& filter, int iEntIndex, int iChannel, const char *pSample, 
		float flVolume, float flAttenuation, int iFlags = 0, int iPitch = PITCH_NORM, int iSpecialDSP = 0, 
		const Vector *pOrigin = NULL, const Vector *pDirection = NULL, CUtlVector< Vector >* pUtlVecOrigins = NULL, bool bUpdatePositions = true, float soundtime = 0.0f, int speakerentity = -1 ) = 0;

	virtual void EmitSound( IRecipientFilter& filter, int iEntIndex, int iChannel, const char *pSample, 
		float flVolume, soundlevel_t iSoundlevel, int iFlags = 0, int iPitch = PITCH_NORM, int iSpecialDSP = 0, 
		const Vector *pOrigin = NULL, const Vector *pDirection = NULL, CUtlVector< Vector >* pUtlVecOrigins = NULL, bool bUpdatePositions = true, float soundtime = 0.0f, int speakerentity = -1 ) = 0;

	virtual void unused_EmitSentenceByIndex() = 0;

	virtual void StopSound( int iEntIndex, int iChannel, const char *pSample ) = 0;

	// stop all active sounds (client only)
	virtual void unused_StopAllSounds() = 0;

	// Set the room type for a player (client only)
	virtual void SetRoomType( IRecipientFilter& filter, int roomType ) = 0;

	// Set the dsp preset for a player (client only)
	virtual void SetPlayerDSP( IRecipientFilter& filter, int dspType, bool fastReset ) = 0;
	
	// emit an "ambient" sound that isn't spatialized
	// only available on the client, assert on server
	virtual void EmitAmbientSound( const char *pSample, float flVolume, int iPitch = PITCH_NORM, int flags = 0, float soundtime = 0.0f ) = 0;


//	virtual EntChannel_t	CreateEntChannel() = 0;

	virtual void unused_GetDistGainFromSoundLevel() = 0;

	// Client .dll only functions
	virtual void unused_GetGuidForLastSoundEmitted() = 0;
	virtual void unused_IsSoundStillPlaying() = 0;
	virtual void unused_StopSoundByGuid() = 0;
	// Set's master volume (0.0->1.0)
	virtual void unused_SetVolumeByGuid() = 0;

	// Retrieves list of all active sounds
	virtual void unused_GetActiveSounds() = 0;

	virtual void unused_PrecacheSentenceGroup() = 0;
	virtual void unused_NotifyBeginMoviePlayback() = 0;
	virtual void unused_NotifyEndMoviePlayback() = 0;

	// create/destroy an audio stream
	virtual void unused_CreateOutputStream() = 0;
	virtual void unused_DestroyOutputStream() = 0;

	// Force an update, for instances where we are otherwise deadlocked from the main loop.
	virtual void unused_ManualUpdate() = 0;
	// Force an extra update.
	virtual void unused_ExtraUpdate() = 0;
};


#endif // IENGINESOUND_H
