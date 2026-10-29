//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier1/utldict.h"
#include "vstdlib/random.h"
#include "soundflags.h"
#include "mathlib/compressed_vector.h"
#include "appframework/IAppSystem.h"

#define SOUNDEMITTERSYSTEM_INTERFACE_VERSION "VSoundEmitter002"

// Length of above including $

typedef short HSOUNDSCRIPTHANDLE;
#define SOUNDEMITTER_INVALID_HANDLE ( HSOUNDSCRIPTHANDLE ) - 1

class IFileList;

struct CSoundParameters
{
	CSoundParameters()
	{
		channel = CHAN_AUTO; // 0
		volume = VOL_NORM; // 1.0f
		pitch = PITCH_NORM; // 100

		pitchlow = PITCH_NORM;
		pitchhigh = PITCH_NORM;

		soundlevel = SNDLVL_NORM; // 75dB
		soundname[0] = 0;
		play_to_owner_only = false;
		count = 0;

		delay_msec = 0;
	}

	int channel;
	float volume;
	int pitch;
	int pitchlow, pitchhigh;
	soundlevel_t soundlevel;
	// For weapon sounds...
	bool play_to_owner_only;
	int count;
	char soundname[128];
	int delay_msec;
};

enum gender_t
{
	GENDER_NONE = 0,
	GENDER_MALE,
	GENDER_FEMALE,
};

#pragma pack( 1 )

struct SoundFile
{
	SoundFile()
	{
		symbol = UTL_INVAL_SYMBOL;
		gender = GENDER_NONE;
		available = true;
		COMPILE_TIME_ASSERT( sizeof( SoundFile ) == 4 );
	}

	CUtlSymbol symbol;
	byte gender;
	byte available;
};

#pragma pack()

#pragma pack( 1 )

template <typename T>
struct sound_interval_t
{
	T start;
	T range;

	interval_t &ToInterval( interval_t &dest ) const
	{
		dest.start = start;
		dest.range = range;
		return dest;
	}

	void FromInterval( const interval_t &from )
	{
		start = from.start;
		range = from.range;
	}

	float Random() const { return RandomFloat( start, start + range ); }
};

#pragma pack()

typedef sound_interval_t<float16_with_assign> volume_interval_t;
typedef sound_interval_t<uint16> soundlevel_interval_t;
typedef sound_interval_t<uint8> pitch_interval_t;

#pragma pack( 1 )

struct CSoundParametersInternal
{
	CSoundParametersInternal();
	~CSoundParametersInternal();

	void CopyFrom( const CSoundParametersInternal &src );

private:
	CSoundParametersInternal( const CSoundParametersInternal &src );

	void AddToTail( SoundFile **pDest, uint16 *pDestCount, const SoundFile &source );

	SoundFile *m_pSoundNames; // 4
	SoundFile *m_pConvertedNames; // 8
	uint16 m_nSoundNames; // 10
	uint16 m_nConvertedNames; // 12

	volume_interval_t volume; // 16
	soundlevel_interval_t soundlevel; // 20
	pitch_interval_t pitch; // 22
	uint16 channel; // 24
	uint16 delay_msec; // 26

	bool play_to_owner_only : 1; // For weapon sounds...	// 27
	// Internal use, for warning about missing .wav files
	bool had_missing_wave_files : 1;
	bool uses_gender_token : 1;
	bool m_bShouldPreload : 1;

	byte reserved; // 28
};

#pragma pack()

// Purpose: Base class for sound emitter system handling (can be used by tools)
abstract_class ISoundEmitterSystemBase : public IAppSystem
{
public:
	// Init, shutdown called after we know what mod is running
	virtual bool ModInit() = 0;
	virtual void ModShutdown() = 0;

	virtual int GetSoundIndex( const char *pName ) const = 0;
	virtual bool IsValidIndex( int index ) = 0;
	virtual void unused_GetSoundCount() = 0;

	virtual const char *GetSoundName( int index ) = 0;
	virtual bool GetParametersForSound( const char *soundname, CSoundParameters &params, gender_t gender,
										bool isbeingemitted = false ) = 0;

	virtual const char *GetWaveName( CUtlSymbol & sym ) = 0;
	virtual void unused_AddWaveName() = 0;

	virtual soundlevel_t LookupSoundLevel( const char *soundname ) = 0;
	virtual const char *GetWavFileForSound( const char *soundname, char const *actormodel ) = 0;
	virtual const char *GetWavFileForSound( const char *soundname, gender_t gender ) = 0;
	virtual void unused_CheckForMissingWavFiles() = 0;
	virtual void unused_GetSourceFileForSound() = 0;

	// Iteration methods
	virtual int First() const = 0;
	virtual int Next( int i ) const = 0;
	virtual int InvalidIndex() const = 0;

	virtual CSoundParametersInternal *InternalGetParametersForSound( int index ) = 0;

	// The host application is responsible for dealing with dirty sound scripts, etc.
	virtual void unused_AddSound() = 0;
	virtual void unused_RemoveSound() = 0;
	virtual void unused_MoveSound() = 0;
	virtual void unused_RenameSound() = 0;

	virtual void unused_UpdateSoundParameters() = 0;

	virtual void unused_GetNumSoundScripts() = 0;
	virtual void unused_GetSoundScriptName() = 0;
	virtual void unused_IsSoundScriptDirty() = 0;
	virtual void unused_FindSoundScript() = 0;
	virtual void unused_SaveChangesToSoundScript() = 0;

	virtual void unused_ExpandSoundNameMacros() = 0;
	virtual gender_t GetActorGender( char const *actormodel ) = 0;
	virtual void GenderExpandString( char const *actormodel, char const *in, char *out, int maxlen ) = 0;
	virtual void GenderExpandString( gender_t gender, char const *in, char *out, int maxlen ) = 0;
	virtual void unused_IsUsingGenderToken() = 0;

	// For blowing away caches based on filetimstamps of the manifest, or of any of the
	//  .txt files that are read into the sound emitter system
	virtual void unused_GetManifestFileTimeChecksum() = 0;

	// Called from both client and server (single player) or just one (server only in dedicated server and client only if connected to a remote server)
	// Called by LevelInitPreEntity to override sound scripts for the mod with level specific overrides based on custom mapnames, etc.
	virtual void AddSoundOverrides( char const *scriptfile, bool bPreload = false ) = 0;

	// Called by either client or server in LevelShutdown to clear out custom overrides
	virtual void ClearSoundOverrides() = 0;

	virtual bool GetParametersForSoundEx( const char *soundname, HSOUNDSCRIPTHANDLE &handle, CSoundParameters &params,
										  gender_t gender, bool isbeingemitted = false ) = 0;
	virtual void unused_LookupSoundLevelByHandle() = 0;

	virtual void ReloadSoundEntriesInList( IFileList * pFilesToReload ) = 0;

	// Called by either client or server to force ModShutdown and ModInit
	virtual void Flush() = 0;
};
