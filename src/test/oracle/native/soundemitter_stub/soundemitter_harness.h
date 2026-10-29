//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The world the PRISTINE game/shared/SoundEmitterSystem.cpp is compiled into,
// as the CLIENT compiles it (CLIENT_DLL), by soundemitter_client_oracle_test.cpp.
// That file is everything between "a name from a soundscript" and "the engine
// plays a wave": CSoundEmitterSystem's emit, stop, ambient and close-caption
// paths, its Init / Shutdown and level stages, and the C_BaseEntity statics
// and free functions that front them. In the game it sits on the engine's
// IEngineSound, the soundemittersystem app system's script table, the client
// entity list, the recipient filters and the close-caption HUD element; here
// each of those is a stand-in that answers from a small scripted world and
// writes down what the subject asked of it.
//
// What is stand-in and what is not:
//
//   * The SUBJECT -- every function of SoundEmitterSystem.cpp -- is upstream's
//     text, compiled unedited from the clone at valve-sdk.lock, with the
//     pristine shareddefs.h (EmitSound_t) and igamesystem.h it was written
//     against.
//   * tier0 (Msg, DevMsg, Warning, which the harness captures through its
//     spew hook) and tier1 (the ConVars, CUtlSymbolTable, the string
//     functions) are the real libraries the test links; IEngineSound,
//     ISoundEmitterSystemBase, CSoundParameters, IRecipientFilter and ICvar
//     are the real public headers.
//   * Everything declared below is a stand-in: the entities (an index, a
//     model, whether it is a player, its owner), the client entity list, the
//     recipient filters (stock's client C_RecipientFilter: a list of player
//     slots, AddAllPlayers the local player), the engine client (the level
//     name and the sentence index), the file system's FileExists, the HUD's
//     caption element and GetCleanMapName (stock's non-TF body, the identity).
//     The engine sound and the script table are implemented in the test.
//
//=============================================================================

#pragma once

#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/convar.h"
#include "tier1/strtools.h"
#include "tier1/utlvector.h"
#include "mathlib/vector.h"
#include "string_t.h"
#include "irecipientfilter.h"
#include "icvar.h"
#include "soundflags.h"
#include "shareddefs.h"
#include "engine/IEngineSound.h"

//-----------------------------------------------------------------------------
// The harness's log. Every stand-in the subject reaches writes here.
//-----------------------------------------------------------------------------
void SoundEmitterLog(const char* pFmt, ...) FMTFUNCTION(1, 2);

// A float as its IEEE bits, the golden's spelling of every float.
unsigned int SoundEmitterBits(float f);

//-----------------------------------------------------------------------------
// Globals the client DLL provides.
//-----------------------------------------------------------------------------
struct SoundEmitterGlobals_t
{
	float curtime;
	int maxClients;
};

extern SoundEmitterGlobals_t* gpGlobals;

extern ICvar* cvar;
extern IEngineSound* enginesound;

class IStubEngineClient
{
public:
	const char* GetLevelName();
	int SentenceIndexFromName(const char* pszName);
};

extern IStubEngineClient* engine;

// The file system: the level's override script is asked for by name.
class IFileList;

class IStubFileSystem
{
public:
	bool FileExists(const char* pFileName, const char* pPathID = NULL);
};

extern IStubFileSystem* filesystem;

//-----------------------------------------------------------------------------
// The recipient filters: stock's client C_RecipientFilter, reduced to what
// the subject asks of it -- a list of player slots and a reliable flag;
// AddAllPlayers is the local player, as stock's client answers it.
//-----------------------------------------------------------------------------
class C_RecipientFilter : public IRecipientFilter
{
public:
	C_RecipientFilter()
		: m_bReliable(false)
	{
	}

	virtual ~C_RecipientFilter() {}

	virtual bool IsReliable() const { return m_bReliable; }

	virtual bool IsInitMessage() const { return false; }

	virtual int GetRecipientCount() const { return m_Recipients.Count(); }

	virtual int GetRecipientIndex(int slot) const
	{
		return (slot < 0 || slot >= m_Recipients.Count()) ? -1 : m_Recipients[slot];
	}

	void CopyFrom(const C_RecipientFilter& src);
	void Reset();

	void MakeReliable() { m_bReliable = true; }

	void AddAllPlayers();
	void AddRecipientByPlayerIndex(int nSlot);

	bool m_bReliable;
	CUtlVector<int> m_Recipients;
};

class C_BaseEntity;

// Stock's client attenuation filter is every local player, whatever the
// origin; only the subject's member EmitSounds build one, which the scenario
// does not drive.
class CPASAttenuationFilter : public C_RecipientFilter
{
public:
	CPASAttenuationFilter(C_BaseEntity*) { AddAllPlayers(); }

	CPASAttenuationFilter(C_BaseEntity*, const char*) { AddAllPlayers(); }

	CPASAttenuationFilter(C_BaseEntity*, const char*, HSOUNDSCRIPTHANDLE&) { AddAllPlayers(); }
};

//-----------------------------------------------------------------------------
// The entities: an index, a model, whether it is a player, and its owner --
// the four things the subject asks of one.
//-----------------------------------------------------------------------------
class C_BaseEntity
{
public:
	C_BaseEntity()
		: m_nIndex(-1)
		, m_pszModel(NULL)
		, m_bPlayer(false)
		, m_pOwner(NULL)
	{
	}

	virtual ~C_BaseEntity() {}

	static C_BaseEntity* Instance(int iEnt);

	string_t GetModelName() const { return MAKE_STRING(m_pszModel); }

	bool IsPlayer() const { return m_bPlayer; }

	C_BaseEntity* GetOwnerEntity() const { return m_pOwner; }

	int entindex() const { return m_nIndex; }

	int GetSoundSourceIndex() const { return m_nIndex; }

	// Stock's client body (baseentity_shared.cpp) asks GameRules() to
	// translate the name for a vision filter, and every non-TF game rules
	// answers the name unchanged: the identity, written down as one.
	void ModifyEmitSoundParams(EmitSound_t&) {}

	static bool IsServer() { return false; }

	static bool IsPrecacheAllowed();
	static void RemoveRecipientsIfNotCloseCaptioning(C_RecipientFilter& filter);

	// The subject's definitions, declared as c_baseentity.h declares them.
	void EmitSound(const char* soundname, float soundtime = 0.0f, float* duration = NULL);
	void EmitSound(const char* soundname, HSOUNDSCRIPTHANDLE& handle, float soundtime = 0.0f, float* duration = NULL);
	void StopSound(const char* soundname);
	void StopSound(const char* soundname, HSOUNDSCRIPTHANDLE& handle);
	void GenderExpandString(char const* in, char* out, int maxlen);

	static void EmitSound(IRecipientFilter& filter, int iEntIndex, const char* soundname, const Vector* pOrigin = NULL,
						  float soundtime = 0.0f, float* duration = NULL);
	static void EmitSound(IRecipientFilter& filter, int iEntIndex, const char* soundname, HSOUNDSCRIPTHANDLE& handle,
						  const Vector* pOrigin = NULL, float soundtime = 0.0f, float* duration = NULL);
	static void EmitSound(IRecipientFilter& filter, int iEntIndex, const EmitSound_t& params);
	static void EmitSound(IRecipientFilter& filter, int iEntIndex, const EmitSound_t& params,
						  HSOUNDSCRIPTHANDLE& handle);
	static void StopSound(int iEntIndex, const char* soundname);
	static void StopSound(int iEntIndex, int iChannel, const char* pSample);
	static soundlevel_t LookupSoundLevel(const char* soundname);
	static soundlevel_t LookupSoundLevel(const char* soundname, HSOUNDSCRIPTHANDLE& handle);
	static void EmitAmbientSound(int entindex, const Vector& origin, const char* soundname, int flags,
								 float soundtime = 0.0f, float* duration = NULL);
	static bool GetParametersForSound(const char* soundname, CSoundParameters& params, const char* actormodel);
	static bool GetParametersForSound(const char* soundname, HSOUNDSCRIPTHANDLE& handle, CSoundParameters& params,
									  const char* actormodel);
	static HSOUNDSCRIPTHANDLE PrecacheScriptSound(const char* soundname);
	static void PrefetchScriptSound(const char* soundname);
	static float GetSoundDuration(const char* soundname, char const* actormodel);
	static void EmitCloseCaption(IRecipientFilter& filter, int entindex, char const* token,
								 CUtlVector<Vector>& soundorigin, float duration, bool warnifmissing = false);
	static bool PrecacheSound(const char* name);
	static void PrefetchSound(const char* name);

	int m_nIndex;
	const char* m_pszModel;
	bool m_bPlayer;
	C_BaseEntity* m_pOwner;
};

class C_BasePlayer : public C_BaseEntity
{
public:
	// Only playgamesound asks, which the scenario does not drive.
	static C_BasePlayer* GetLocalPlayer() { return NULL; }

	Vector EyePosition() const { return Vector(0, 0, 0); }

	void GetVectors(Vector*, Vector*, Vector*) const {}
};

#define CBaseEntity C_BaseEntity
#define CBasePlayer C_BasePlayer

class CStubClientEntityList
{
public:
	C_BaseEntity* GetEnt(int iEnt) { return C_BaseEntity::Instance(iEnt); }
};

CStubClientEntityList& ClientEntityList();

//-----------------------------------------------------------------------------
// The HUD's close-caption element, and its lookup.
//-----------------------------------------------------------------------------
class CHudCloseCaption
{
public:
	bool ProcessCaption(char const* tokenname, float duration, bool fromplayer = false, bool direct = false);
};

void* SoundEmitterHudElement(const char* pszName);
#define GET_HUDELEMENT(className) (className*)SoundEmitterHudElement(#className)

// util_shared.cpp's: stock's body for every game but TF answers its argument.
const char* GetCleanMapName(const char* pszUnCleanMapName, char (&pszTmp)[256]);

#ifndef VPROF
#define VPROF(name)
#endif
#ifndef VPROF_BUDGET
#define VPROF_BUDGET(name, group)
#endif
