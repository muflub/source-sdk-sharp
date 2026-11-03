//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The new-style particle systems the client starts, as one object, a member of the client
// DLL object. The library owns each system. An owned system (started by a managed entity)
// is held in one of four slots and kept at its owner every frame; a one-shot (an impact) is
// only watched, and the manager removes it. Both are watched by a weak reference so a
// stopped system stays listed until deleted. Its IShimEffectsRenderClient slots are the
// slot rows, and its IShimParticlesClient slots the one-shots and the dev API's read.
//
// A managed entity has no ParticleProp(), so its systems are created ownerless and their
// control points are driven from the owner's origin.

#pragma once

#include <stdint.h>

#include "mathlib/vector.h"
#include "smartptr.h"
#include "utlvector.h"
#include "utlobjectreference.h"

#include "ishim_areas_client.h"
#include "particle_follow_mask.h"
#include "shell_clientsystem.h"
#include "shell_particle_systems.h"

class CShellNewParticleEffect;
class CNewParticlesClient;
class CStandaloneClients;

class CParticleSystemsClient
{
public:
	// Out of line, as the destructor is: the slots' entries hold an effect type this header
	// only declares.
	CParticleSystemsClient(CStandaloneClients& standalone, CNewParticlesClient& newParticles);
	~CParticleSystemsClient();

	CParticleSystemsClient(const CParticleSystemsClient&) = delete;
	CParticleSystemsClient& operator=(const CParticleSystemsClient&) = delete;

	CClientSystem& SystemRow() { return m_SystemRow; }

	// IShimEffectsRenderClient.
	int32_t ParticleSystemStart(uint32_t hOwner, const char* pszName);
	int32_t ParticleSystemFollowPoints(uint32_t hOwner, int32_t nPointMask);
	int32_t ParticleSystemFollowEntity(uint32_t hOwner, int32_t nPoint, uint32_t hEntity, int32_t nAttachment,
									   const float* pOffsetXYZ);
	void ParticleSystemStop(uint32_t hOwner);
	int32_t ParticleSystemStartExtra(uint32_t hOwner, const char* pszName);
	void ParticleSystemStopExtra(uint32_t hOwner);
	int32_t ParticleSystemStartSlot(uint32_t hOwner, const char* pszName, int32_t nSlot);
	void ParticleSystemStopSlot(uint32_t hOwner, int32_t nSlot);
	int32_t ParticleSystemFollowEntitySlot(uint32_t hOwner, int32_t nPoint, uint32_t hEntity, int32_t nAttachment,
										   int32_t nSlot, const float* pOffsetXYZ);

	// IShimParticlesClient: the one-shots, watched and not held, and the dev API's read. The
	// impact's is in shell_impact_effects.cpp.
	int32_t ParticleSystemOneShot(const char* pszName, const float* pSortOriginXYZ, const float* pPoints,
								  int32_t nPoints, const float* pOrientation0);
	int32_t ParticleSystemImpact(const char* pszName, const float* pPoints, int32_t nEntity, float flScale,
								 const float* pColorXYZ);
	int32_t ParticleSystemsRead(int32_t nIndex, SourceSharpParticleSystemInfo_t* pOut, char* pszName, int32_t nNameMax);

private:
	// Keeps each held system at its owner every frame, and stops them all at a level's end.
	class CSystemRow final : public CClientSystemPerFrame
	{
	public:
		explicit CSystemRow(CParticleSystemsClient& systems)
			: CClientSystemPerFrame("CSourceSharpParticleSystemsSystem")
			, m_Systems(systems)
		{
		}

		void PreRender() override;
		void LevelShutdownPostEntity() override;

	private:
		CParticleSystemsClient& m_Systems;
	};

	struct System_t
	{
		uint32_t hOwner;
		CSmartPtr<CShellNewParticleEffect> pEffect;

		// Which control points the sweep keeps at the owner, as a bitmask. An unfed control
		// point is the world origin.
		int32_t nFollowMask;

		// Points handed to an entity's attachment instead (ParticleSystemFollowEntity): the
		// medigun beam's far end rides its patient.
		uint32_t hFollowEntity[kSourceSharpParticleMaxFollowPoint + 1];
		int32_t nFollowAttachment[kSourceSharpParticleMaxFollowPoint + 1];
		Vector vecFollowOffset[kSourceSharpParticleMaxFollowPoint + 1];
	};

	typedef CUtlVector<System_t> Systems_t;

	// Heap entries: a CUtlReference links itself into its object's list and must not move.
	struct Watched_t
	{
		CUtlReference<CShellNewParticleEffect> ref;
		uint32_t hOwner;
		int32_t nKind;
		float flStarted;
	};

	// The slots by index: 0 the first, 1 the crit glow's extra, 2 the urine's drips, 3 the
	// milk's, 4 and 5 the blast jump's smoke, a foot per slot. NULL is a slot outside the
	// table: its start refuses, its stop has nothing to free.
	enum
	{
		kSlots = 6
	};

	Systems_t* Slot(int nSlot) { return (nSlot >= 0 && nSlot < kSlots) ? &m_Slots[nSlot] : NULL; }

	static void ReleaseAt(Systems_t& systems, int nIndex);
	void Reap();
	void WatchOneShot(CShellNewParticleEffect* pEffect);
	void Watch(CShellNewParticleEffect* pEffect, uint32_t hOwner, int32_t nKind);
	CSmartPtr<CShellNewParticleEffect> CreateWatched(uint32_t hOwner, const char* pszName);
	int32_t StartAttached(Systems_t& systems, uint32_t hOwner, const char* pszName);
	void StopAttached(Systems_t& systems, uint32_t hOwner);
	void Sweep(Systems_t& systems);
	void PreRender();
	void LevelShutdown();

	CStandaloneClients& m_Standalone;
	CNewParticlesClient& m_NewParticles;

	// Slot 0 holds one system per owner; the crit glow runs beside the trail in slot 1, the
	// condition drips (jarate's and the milk's) beside the burn, the glow and each other in
	// slots 2 and 3, and the blast jump's smoke wears a foot per slot in 4 and 5, as stock
	// holds one member per effect on the player.
	Systems_t m_Slots[kSlots];

	CUtlVector<Watched_t*> m_Watched;

	CSystemRow m_SystemRow{*this};
};
