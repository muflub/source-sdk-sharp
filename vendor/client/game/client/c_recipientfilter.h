//========= Copyright Valve Corporation, All rights reserved. ============//

//========= SourceSharp's edit of c_recipientfilter.h (vendor/README.md) =====//
// C_RecipientFilter and the client's PAS, PVS and local-player filters, which
// shell_recipientfilter_client.cpp implements: moved out of the fork's edited
// game/client/c_recipientfilter.h (client.so on unmodified Valve headers, user
// 2026-10-25).
#pragma once

#include "irecipientfilter.h"
#include "utlvector.h"
// step 7h: `#include "c_baseentity.h"` stood here. Nothing in this header names
// C_BaseEntity any more -- the one mention left is the comment at the bottom
// recording that RemoveRecipientsIfNotCloseCaptioning stopped being a static
// member of it. The include was holding the entity class inside five
// otherwise entity-free translation units through this file alone.
#include "icliententity.h"
#include "soundflags.h"
#include "bitvec.h"

class C_BasePlayer;

class C_RecipientFilter : public IRecipientFilter
{
public:
	C_RecipientFilter();
	virtual ~C_RecipientFilter();

	virtual bool IsReliable() const;

	virtual int GetRecipientCount() const;
	virtual int GetRecipientIndex(int slot) const;

	virtual bool IsInitMessage() const { return false; };

public:
	void CopyFrom(const C_RecipientFilter& src);

	void Reset();

	void AddAllPlayers();
	void AddRecipientsByPVS(const Vector& origin);
	void AddRecipientsByPAS(const Vector& origin);

	// The add every other add funnels through. See local_recipient.h: the
	// filter stores indices and the engine names the viewer BY index, so the
	// C_BasePlayer in the pointer-shaped adds was only ever a way to ask
	// "which slot" -- and a way that answers nothing for a viewer on the
	// parallel hierarchy, which is every viewer in this mod.
	void AddRecipientByPlayerIndex(int playerindex);

	//private:

	bool m_bReliable;
	alignas(1) unsigned char unused_m_bInitMessage[7];
	CUtlVector<int> m_Recipients;
	// If using prediction rules, the filter itself suppresses local player
	bool m_bUsingPredictionRules;
	// If ignoring prediction cull, then external systems can determine
	//  whether this is a special case where culling should not occur
	bool m_bIgnorePredictionCull;
};

// Purpose: Simple class to create a filter for a single player
class CPASFilter : public C_RecipientFilter
{
public:
	CPASFilter(const Vector& origin) { AddRecipientsByPAS(origin); }
};

class CPASAttenuationFilter : public CPASFilter
{
public:
	CPASAttenuationFilter(IClientEntity* entity, float attenuation = ATTN_NORM)
		: CPASFilter(entity->GetAbsOrigin())
	{
	}

	CPASAttenuationFilter(const Vector& origin, float attenuation = ATTN_NORM)
		: CPASFilter(origin)
	{
	}

	CPASAttenuationFilter(IClientEntity* entity, const char* lookupSound)
		: CPASFilter(entity->GetAbsOrigin())
	{
	}

	CPASAttenuationFilter(const Vector& origin, const char* lookupSound)
		: CPASFilter(origin)
	{
	}

	CPASAttenuationFilter(IClientEntity* entity, const char* lookupSound, HSOUNDSCRIPTHANDLE& handle)
		: CPASFilter(entity->GetAbsOrigin())
	{
	}

	CPASAttenuationFilter(const Vector& origin, const char* lookupSound, HSOUNDSCRIPTHANDLE& handle)
		: CPASFilter(origin)
	{
	}
};

// Purpose: Simple class to create a filter for a single player
class CPVSFilter : public C_RecipientFilter
{
public:
	CPVSFilter(const Vector& origin) { AddRecipientsByPVS(origin); }
};

class CLocalPlayerFilter : public C_RecipientFilter
{
public:
	CLocalPlayerFilter();
};
