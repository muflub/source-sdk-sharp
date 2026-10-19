//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the looping-sound controller, as server.so reaches it
//
// plan_server_novalve.md P5 (lane SP). game/shared/soundenvelope.h's
// CSoundEnvelopeController is reached in server.so only by physics.cpp's
// friction sounds, through CSoundEnvelopeController::GetController(); no engine
// reads it (the patches are SourceSharp.Server's SoundController). So the
// server's own interface lives in namespace ss, with the five methods a server
// translation unit calls, on public/ headers alone.
//
// shell_soundenvelope.cpp's controller implements it beside the Valve class;
// ss::SoundController() answers that object.
//=============================================================================//

#ifndef SOURCESHARP_SS_SOUNDCONTROLLER_H
#define SOURCESHARP_SS_SOUNDCONTROLLER_H
#ifdef _WIN32
#pragma once
#endif

#include "soundflags.h"

class CSoundPatch;			// shell_soundenvelope.cpp's patch handle
class IRecipientFilter;

namespace ss
{

class ISoundController
{
public:
	// A looping sound on an entity's channel, not yet playing.
	virtual CSoundPatch *SoundCreate( IRecipientFilter &filter, int nEntIndex, int channel,
	                                  const char *pSoundName, soundlevel_t soundlevel ) = 0;

	// Start it at this volume and pitch, flStartTime seconds into the sound.
	virtual void Play( CSoundPatch *pSound, float volume, float pitch, float flStartTime = 0 ) = 0;

	// Ramp to a new pitch / volume over deltaTime seconds.
	virtual void SoundChangePitch( CSoundPatch *pSound, float pitchTarget, float deltaTime ) = 0;
	virtual void SoundChangeVolume( CSoundPatch *pSound, float volumeTarget, float deltaTime ) = 0;

	// Stop it and free the patch; NULL is ignored.
	virtual void SoundDestroy( CSoundPatch *pSound ) = 0;

protected:
	~ISoundController() {}
};

// The server's controller (shell_soundenvelope.cpp).

} // namespace ss

#endif // SOURCESHARP_SS_SOUNDCONTROLLER_H
