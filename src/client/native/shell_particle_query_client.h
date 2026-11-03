//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The IParticleSystemQuery particles.a calls back into, a member of the client DLL object,
// with each answer from managed. The six methods no definition calls warn and count as
// refusals. Calls with no managed side (the collision-group reads during Init, before the
// runtime exists) answer the interface's default and are counted. Its IShimParticlesClient
// slot is the count of those.

#pragma once

#include <stdint.h>

#include "appframework/IAppSystem.h"
#include "particles/particles.h"
#include "tier0/threadtools.h"

#include "shell_managed_areas_client.h"

class CParticleSystemQueryClient final : public CBaseAppSystem<IParticleSystemQuery>
{
public:
	explicit CParticleSystemQueryClient(const CManagedAreasClient& areas)
		: m_Areas(areas)
	{
	}

	CParticleSystemQueryClient(const CParticleSystemQueryClient&) = delete;
	CParticleSystemQueryClient& operator=(const CParticleSystemQueryClient&) = delete;

	// NULL outside a managed load.
	IManagedParticlesClient* Managed() const { return m_Areas.m_pParticlesClient; }

	enum
	{
		kCollisionGroup,
		kLighting,
		kTrace,
		kHitBoxPoints,
		kHitBoxInfo,
		kMethods
	};

	void GetLightingAtPoint(const Vector& vecOrigin, Color& tint) override;
	void TraceLine(const Vector& vecAbsStart, const Vector& vecAbsEnd, unsigned int mask, const IHandleEntity* ignore,
				   int collisionGroup, CBaseTrace* ptr) override;
	int GetCollisionGroupFromName(const char* pszCollisionGroupName) override;
	void GetRandomPointsOnControllingObjectHitBox(CParticleCollection* pParticles, int nControlPointNumber,
												  int nNumPtsOut, float flBBoxScale,
												  int nNumTrysToGetAPointInsideTheModel, Vector* pPntsOut,
												  Vector vecDirectionalBias, Vector* pHitBoxRelativeCoordOut,
												  int* pHitBoxIndexOut) override;
	int GetControllingObjectHitBoxInfo(CParticleCollection* pParticles, int nControlPointNumber, int nBufSize,
									   ModelHitBoxInfo_t* pHitBoxOutputBuffer) override;

	// Never called by any definition: each warns and counts as a refusal.
	bool MovePointInsideControllingObject(CParticleCollection*, void*, Vector*) override;
	bool IsPointInControllingObjectHitBox(CParticleCollection*, int, Vector, bool) override;
	Vector GetLocalPlayerPos() override;
	void GetLocalPlayerEyeVectors(Vector* pForward, Vector* pRight, Vector* pUp) override;
	float GetPixelVisibility(int*, const Vector&, float) override;
	void SetUpLightingEnvironment(const Vector&) override;

	// IShimParticlesClient: the unanswered calls per method, in the enum's order. Returns the
	// count written.
	int32_t ParticleQueryUnanswered(int32_t* pOut, int32_t nMax);

private:
	// The library's worker threads call these concurrently.
	void Unanswered_Add(int nMethod) { ThreadInterlockedIncrement(&m_nUnanswered[nMethod]); }

	const CManagedAreasClient& m_Areas;

	CThreadFastMutex m_LightMutex;
	volatile int32 m_nUnanswered[kMethods] = {};
};
