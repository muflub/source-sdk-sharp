//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a sound's precache ==================================//
// SS_REASON a  a vtable the engine calls through
//
// This file made the noise of every C#-owned entity until plan_server_native_v2
// T4a, when the emitting went to C# too (SoundEmitter, ServerSounds): which
// sound plays, from where and to whom are all managed now. What is left is the
// IShimPrecache slot that precaches a sound by name, through the native sound
// emitter system's precache path.
//
//=============================================================================//

#include "sourcesharp.h"
// NO soundent.h. It was here for the two SoundEnt bodies below, and both left
// with plan_gamedll_after.md Phase A3's second pair -- see the note where they
// were. Nothing else in this file names CSoundEnt, CSound or SOUNDLIST_EMPTY,
// which was checked rather than assumed.

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the calls below name GameSound_* directly.
#include "gamesound.h"



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
// THE EMITS WENT TO C# (plan_server_native_v2 T4a): SourceSharp_EntityEmitSound,
// SourceSharp_EntityStopSound, SourceSharp_EmitSoundAtPosition,
// SourceSharp_EmitWorldSoundScript and SourceSharp_EmitAmbientSound, and the
// IShimSound area they were. SourceSharp.Server's ServerSounds answers each
// over the managed SoundEmitter, which chooses the recipients too; the
// IRecipientFilter the engine reads is libss_fwd_server's object over them.
// What is left here is the precache, which is not the emit path.
//-----------------------------------------------------------------------------

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
