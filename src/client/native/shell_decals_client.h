//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's IDecalEmitterSystem (`decalsystem`), each method one forward to
// IManagedDecalsClient, and its client-system row: a member of the client DLL object,
// handed the managed areas it calls. Name answers are copied into members: native may
// not hold a pointer into managed memory.

#pragma once

#include "decals.h"

#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CShellDecalSystemClient final : public IDecalEmitterSystem, public CClientSystem
{
public:
	explicit CShellDecalSystemClient(const CManagedAreasClient& areas);

	virtual bool Init();

	virtual void Shutdown();

	virtual void LevelInitPreEntity();

	virtual int GetDecalIndexForName(char const* decalname);

	// "" for an index no entry carries, as stock answers.
	virtual const char* GetDecalNameForIndex(int nIndex);

	// The caller's own pointer when managed has no other answer, as stock
	// returns `decalName`; "" is a real answer (the material is not decalled).
	virtual char const* TranslateDecalForGameMaterial(char const* decalName, unsigned char gamematerial);

private:
	const CManagedAreasClient& m_Areas;
	char m_szName[260];
	char m_szTranslated[260];
};
