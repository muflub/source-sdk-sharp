//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The native side of client physics: the CPhysicsSystem client system row, the DLL's
// physics entry points and the IShimPhysicsClient area. A member of the client DLL
// object, handed the managed areas it calls; the decisions are managed's.

#pragma once

#include "interface.h"

#include "shell_clientsystem.h"
#include "ishim_areas_client.h"
#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;

class CPhysicsClient final : public CClientSystem, public IShimPhysicsClient
{
public:
	CPhysicsClient(const CManagedAreasClient& areas, CClientEngineInterfaces& engines)
		: CClientSystem("CPhysicsSystem")
		, m_Areas(areas)
		, m_Engines(engines)
	{
	}

	// The DLL's Init fails unless vphysics answers all three interfaces, as stock's does.
	bool DLLInit(CreateInterfaceFn physicsFactory);
	void Reset();
	void Simulate();

	void LevelInitPreEntity() override;
	void LevelInitPostEntity() override;
	void LevelShutdownPreEntity() override;
	void LevelShutdownPostEntity() override;

	int32_t PhysicsClientSurfaceAudio(int32_t nSurfaceProps, char* pHard, int32_t nHard, char* pSoft, int32_t nSoft,
									  float* pOut3) override;
	void PhysicsClientCollideExtent(void* pObject, const float* pOrigin, const float* pAngles, const float* pDirection,
									float* pOut) override;
	int32_t PhysicsClientPointContents(const float* pPoint) override;
	void PhysicsClientSplashLighting(const float* pPoint, float* pOut4) override;
	void PhysicsClientSplashEffect(int32_t nKind, const float* pOrigin, const float* pNormal, float flScale,
								   const float* pColor, float flLifetime, float flAlpha) override;

private:
	const CManagedAreasClient& m_Areas;
	// Not const: DLLInit acquires vphysics' two interfaces into it.
	CClientEngineInterfaces& m_Engines;
};
