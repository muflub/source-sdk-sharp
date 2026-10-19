//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — making a noise =====================================//
// SS_REASON a  a vtable the engine calls through
//
// Until this file the only sound a managed entity could make was a weapon
// firing, and that went through the weapon script rather than through anything
// C# chose. So every C#-owned entity was silent: a door opened without a
// sound, a button clicked without a click — and dm_lockdown's own doors NAME
// the sounds they want, in keyvalues nothing was reading.
//
// Which sound plays when is game logic, which is why the choosing is in C# and
// only the emitting is here.
//
//=============================================================================//

#include "ss_recipient_filter.h"
#include "shell_standalone_class.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "engine/IEngineSound.h"
// NO soundent.h. It was here for the two SoundEnt bodies below, and both left
// with plan_gamedll_after.md Phase A3's second pair -- see the note where they
// were. Nothing else in this file names CSoundEnt, CSound or SOUNDLIST_EMPTY,
// which was checked rather than assumed.

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the calls below name GameSound_* directly.
#include "gamesound.h"

// The ambient emitter sound_emitter.cpp defines (its declaration lived in
// util.h, which an R5 TU does not include).
void UTIL_EmitAmbientSound( int entindex, const Vector &vecOrigin, const char *samp, float vol,
                            soundlevel_t soundlevel, int fFlags, int pitch,
                            float soundtime = 0.0f, float *duration = NULL );


//-----------------------------------------------------------------------------
// Whether a name is a soundscript entry or a path to a wave.
//
// The engine keeps them in different places and precaches them differently, and
// it does not guess: "Doors.Move18" is a script entry, "ambient/alarms/foo.wav"
// is a file. Telling them apart on the dot is what the SDK does everywhere it
// has to make this decision, and a path always has a slash and an extension.
//-----------------------------------------------------------------------------
static bool IsScriptSound( const char *pName )
{
	return strchr( pName, '/' ) == NULL && strchr( pName, '\\' ) == NULL;
}

//-----------------------------------------------------------------------------
// Must happen during level load.
//
// A sound emitted without being precached produces a warning and silence. That
// is the worst kind of failure this project keeps meeting: the entity works,
// the map works, and one thing makes no noise for a reason nothing states.
//-----------------------------------------------------------------------------
void SourceSharp_PrecacheSound( const char *pSoundName )
{
	if ( !pSoundName || !pSoundName[0] )
		return;

	if ( IsScriptSound( pSoundName ) )
		GameSound_PrecacheScript( pSoundName );
	else
		GameSound_Precache( pSoundName );
}

//-----------------------------------------------------------------------------
// A sound from an entity, on either hierarchy.
//
// Resolving only a CBaseEntity here made every [ScriptedEntity] in the mod
// silent, and silently: the handle answered NULL, this returned, and a door
// that NAMES its noise1 in a keyvalue played nothing. Precaching was never
// affected, being global, which is what made it look like the sound was fine.
//
// The parallel hierarchy needs no EmitSound of its own. A sound is attached to
// an entity INDEX rather than to a C++ object, and the static form takes exactly
// that; what the instance method adds on top is ModifyEmitSoundParams, which
// nothing on this hierarchy overrides. So the two paths differ only in how the
// index is found.
//-----------------------------------------------------------------------------
void SourceSharp_EntityEmitSound( uint32_t hEntity, const char *pSoundName )
{
	if ( !pSoundName || !pSoundName[0] )
		return;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
	{
		//---------------------------------------------------------------------
		// A HANDLE THIS SIDE CANNOT PLACE. plan_entity.md step 8e-sound.
		//
		// WHAT THIS USED TO BE, because the change is the point. Step 4b found
		// a bare `return;` here -- §7's silent no-op in its hardest-to-notice
		// shape -- and turned it into a declared refusal reading "a shell-less
		// entity cannot make a noise". It was right: SourceSharp_ResolveEntity
		// resolves through g_pEntityList, and a shell-less entity is never in
		// that list by construction -- no edict, no entity index and NO C++
		// OBJECT AT ALL. Its position lives only in managed state, so there was
		// nothing here to ask where the sound came from.
		//
		// THE REFUSAL IS CLOSED AND THIS IS NOT IT. Engine.EntityEmitSound now
		// answers a shell-less handle on the managed side and routes it to
		// EmitSoundAtPosition with the origin that side already holds, exactly
		// as it answers EntityGetOrigin and CreateEntity. So a shell-less
		// handle no longer arrives here at all, and step 2e's
		// logical.nativeReach counter stays FLAT across such an emit where step
		// 4b measured it moving 24 -> 25 for one.
		//
		// WHAT IS LEFT IS A DIAGNOSTIC WITH TWO CASES, and they are separated
		// because they mean opposite things:
		//
		//   an entry in the ENGINE-INVISIBLE half is a shell-less entity that
		//     got past the managed route. That is a REGRESSION, not a refusal,
		//     and it says so;
		//   any other entry is a handle to an entity that is gone or whose slot
		//     was reused -- an ordinary stale handle, which every caller of an
		//     entity API can produce and which nothing here can fix.
		//
		// ONCE PER PROCESS. A sound that is refused is usually refused every
		// frame, and a line per refusal is a line nobody reads.
		//---------------------------------------------------------------------
		static bool s_bSaid = false;

		if ( !s_bSaid )
		{
			s_bSaid = true;

			const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

			if ( nEntry >= MAX_EDICTS && nEntry < NUM_ENT_ENTRIES )
				Warning( "[SourceSharp] a shell-less entity asked to emit '%s' and "
				         "reached NATIVE code to do it. That is a regression: "
				         "Engine.EntityEmitSound is supposed to answer a shell-less "
				         "handle on the managed side and route it to "
				         "EmitSoundAtPosition, because this side has no object to ask "
				         "where the sound comes from. plan_entity.md step 8e-sound. "
				         "Said once.\n",
				         pSoundName );
			else
				Warning( "[SourceSharp] '%s' was emitted from a handle that resolves "
				         "to nothing -- the entity is gone, or its slot was reused. "
				         "Said once.\n",
				         pSoundName );
		}

		return;
	}

	// The engine does NOT complain about a soundscript name it does not know.
	// That is worth stating because it makes a typo in a map keyvalue, or in a
	// managed class, completely silent — the call succeeds, nothing plays, and
	// nothing anywhere says why.

	const int nEntIndex = ent.SoundSourceIndex();

	// ONE CONSTRUCTION FOR BOTH HIERARCHIES, ADDRESSED BY HANDLE — step 4b.
	//
	// This was two: an `ent.pBase` arm that built the filter from a
	// CBaseEntity *, and an origin arm for everything else. The handle-shaped
	// constructor is the single answer, and it is a better one than either:
	// it crops around the source's CENTRE, which is what stock's
	// GetSoundEmissionOrigin() means, where the origin arm cropped around
	// ent.Origin() -- 0,0,0 for a brush entity without an origin brush, which
	// is most of dm_lockdown's doors.
	ss::CPASAttenuationFilter filter(
		CBaseHandle::UnsafeFromIndex( (int)hEntity ), pSoundName );

	// Attached to the entity when the client knows one, so the sound TRAVELS
	// with it — a door's move loop has to follow the door. An explicit origin
	// would pin it where the door started, which is the same wrong answer as
	// no sound in a different key.
	//
	// A server-only entity has no index to attach to, so its sound comes from
	// where it is instead. That costs nothing: nothing on this hierarchy without
	// an edict moves.
	//
	// THE POSITION FOR THAT CASE IS THE ORIGIN, not the centre the filter used.
	// A shell-less entity is Logical kind: no model, so no OBB, so its centre
	// IS its origin -- Bounds() answers vec3_origin for both ends. Two names
	// for one point here, and the filter's name is the right one there.
	const Vector vecOrigin = ent.Origin();
	const bool bFromWorld = ( nEntIndex == SOUND_FROM_WORLD );

	// Volume, pitch and soundlevel deliberately left at their defaults, which is
	// what a soundscript emit does: the soundscript owns them, and writing
	// VOL_NORM here would override what the artist wrote.
	ss::EmitSound_t params;
	params.m_pSoundName = pSoundName;
	params.m_pOrigin    = bFromWorld ? &vecOrigin : NULL;
	params.m_bWarnOnDirectWaveReference = true;

	GameSound_Emit( filter, nEntIndex, params );
}

//-----------------------------------------------------------------------------
// Stops a looping sound.
//
// A one-shot ends by itself and does not need this. A loop does, and forgetting
// it is not subtle: a door's travel sound runs until the map changes.
//-----------------------------------------------------------------------------
void SourceSharp_EntityStopSound( uint32_t hEntity, const char *pSoundName )
{
	if ( !pSoundName || !pSoundName[0] )
		return;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// By index for both, because that is what the instance method reduces to.
	// It must be the SAME index the sound was started on or the engine has
	// nothing to match, which is why both halves ask SoundSourceIndex.
	//
	// AND SOUND_FROM_WORLD FOR A HANDLE THAT RESOLVES TO NOTHING -- step
	// 8e-sound, and this is the counterpart of the managed emit route rather
	// than a guess at what an unknown handle meant. A shell-less entity's sound
	// leaves through Engine.EntityEmitSound -> EmitSoundAtPosition, which emits
	// on SOUND_FROM_WORLD with an origin override; the engine keys a playing
	// sound by (entity index, name), so the only index that can stop it is that
	// one. Until this line a shell-less loop could be started and never stopped,
	// which is a worse defect than the silence the emit route closed.
	//
	// It also covers the stale-handle case the emitter warns about, and it is
	// the right answer there too: stopping a sound that is not playing is a
	// no-op in the engine, where leaving one playing is forever.
	GameSound_Stop( ent.IsValid() ? ent.SoundSourceIndex() : SOUND_FROM_WORLD,
	                pSoundName );
}

//-----------------------------------------------------------------------------
// A noise with nothing to come from.
//
// Everyone hears it, because a sound at a place has no owner to decide who is
// near enough — the engine's own distance falloff does that, out of the sound's
// soundlevel.
//-----------------------------------------------------------------------------
void SourceSharp_EmitSoundAtPosition( const char *pSoundName, const float *pXYZ )
{
	if ( !pSoundName || !pSoundName[0] || !pXYZ )
		return;

	const Vector vecOrigin( pXYZ[0], pXYZ[1], pXYZ[2] );

	ss::CPASAttenuationFilter filter( vecOrigin );

	ss::EmitSound_t params;
	params.m_pSoundName = pSoundName;
	params.m_pOrigin    = &vecOrigin;
	params.m_flVolume   = VOL_NORM;
	params.m_nPitch     = PITCH_NORM;

	GameSound_Emit( filter, SOUND_FROM_WORLD, params );
}

//-----------------------------------------------------------------------------
// A soundscript from the world, at a place, on a channel, at a scale of the
// script's own volume -- the body of vphysics_sound.h's PlayImpactSounds and
// PlayBreakSounds, whose queues and choices are managed's PhysicsSoundBatch
// (plan_physics_bones_native_zero 2.3). From the world because the entity may
// be gone by the time it plays. 0 when the script has no parameters, which is
// the answer the batch's walk stops on. Carry-out only.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EmitWorldSoundScript( const char *pszName, const float *pXYZ, int32_t nChannel,
                                          float flVolume )
{
	if ( !pszName || !pszName[0] || !pXYZ )
		return 0;

	CSoundParameters params;
	if ( !GameSound_GetParameters( pszName, params, NULL ) )
		return 0;

	const Vector vecOrigin( pXYZ[0], pXYZ[1], pXYZ[2] );
	ss::CPASAttenuationFilter filter( vecOrigin, params.soundlevel );

	ss::EmitSound_t ep;
	ep.m_nChannel   = nChannel;
	ep.m_pSoundName = params.soundname;
	ep.m_flVolume   = params.volume * flVolume;
	ep.m_SoundLevel = params.soundlevel;
	ep.m_nPitch     = params.pitch;
	ep.m_pOrigin    = &vecOrigin;
	GameSound_Emit( filter, SOUND_FROM_WORLD, ep );
	return 1;
}

//-----------------------------------------------------------------------------
// Soundscapes.
//
// The split here is the same one the rest of the sound API takes and it is
// worth naming, because a soundscape looks like one thing and is two.
//
// What a soundscape IS -- its loops, its random waves, its positioned sounds,
// the other soundscapes it nests -- is parsed out of scripts/soundscapes_*.txt
// by CSoundscapeSystem at level load, and the CLIENT parses the same files into
// the same order. Both sides then index that list by one number. None of that
// is a decision this mod makes, and moving it would mean moving the client's
// copy too, so it stays.
//
// WHICH ONE you are hearing is entirely a decision, and all of it is managed
// now: which soundscapes are in range, which have a clear line to you, which is
// nearest, which trigger volume you are standing in, and what happens when you
// leave it.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// An ambient sound: not another EmitSound, but an update to a CHANNEL.
//
// The engine keys an ambient by (entity index, sound name). A second call with
// SND_CHANGE_PITCH or SND_CHANGE_VOL retunes what is already playing rather than
// starting a second copy; SND_STOP ends it. That is the whole mechanism a
// generator spinning up, an alarm throbbing and a fan fading out are built on,
// and it cannot be expressed by starting sounds.
//
// Nothing is decided here. The index and the origin arrive already chosen —
// which entity the sound comes from is exactly what a mapper's SourceEntityName
// key settles, and that belongs on the managed side. The index must also be
// allowed to outlive the entity: a stop has to reach the same index the sound
// started on, and by then the source may be gone.
//
// UTIL_EmitAmbientSound rather than engine->EmitAmbientSound because the sentence
// form ("!SENTENCE") has to go through the sentence table first, and because the
// soundscript path is where a name like "k_lab.ambient_powergenerators" is
// resolved to a wave at all — every ambient_generic in dm_lockdown names one.
//-----------------------------------------------------------------------------
void SourceSharp_EmitAmbientSound( int32_t nEntIndex, const float *pXYZ,
                                   const char *pSoundName, float flVolume,
                                   int32_t nSoundLevel, int32_t nFlags, int32_t nPitch )
{
	if ( !pSoundName || !pSoundName[0] || !pXYZ )
		return;

	const Vector vecOrigin( pXYZ[0], pXYZ[1], pXYZ[2] );

	UTIL_EmitAmbientSound( nEntIndex, vecOrigin, pSoundName, flVolume,
	                       (soundlevel_t)nSoundLevel, nFlags, nPitch );
}

//-----------------------------------------------------------------------------
// SourceSharp_SoundEntQuery AND SourceSharp_SoundEntInsert WERE HERE.
//
// plan_gamedll_after.md Phase A3, second pair. game/server/soundent.cpp has
// left server_csharp.vpc and the pool is `SoundEnt` in SourceSharp.Server, so
// the two entities these bodies existed for -- env_microphone, which reads the
// list, and ai_sound, which writes it -- resolve IServerSoundEnt and call it.
// Two engine slots went with them.
//
// THE HOT ONE WAS THE READ. Its own note recorded 19,241 SOURCESHARP_TRACE_HOT
// lines in one session, one per AI think, second only to the sphere query. It
// is not a boundary crossing at all now: the caller and the list are the same
// side.
//
// What replaced them is in src/sourcesharp/game/server/shell_soundent.cpp,
// which forwards the FOUR statics native code still spells -- InsertSound,
// InitSoundEnt, ShutdownSoundEnt, ResetClientSound -- in the other direction.
//-----------------------------------------------------------------------------
