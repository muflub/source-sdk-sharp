//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// CPhysicsClient: the native side of client physics. The decisions are managed's.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"
#include "vphysics_interface.h"

#include "fx_water.h"

#include "shell_physics_client.h"
#include "imanaged_areas_client.h"

// Read by name by the client's shells (physics_shared.h).
IPhysicsSurfaceProps* physprops = NULL;
IPhysicsCollision* physcollision = NULL;

bool CPhysicsClient::DLLInit(CreateInterfaceFn physicsFactory)
{
	if (!physicsFactory(VPHYSICS_INTERFACE_VERSION, NULL) ||
		(physprops = (IPhysicsSurfaceProps*)physicsFactory(VPHYSICS_SURFACEPROPS_INTERFACE_VERSION, NULL)) == NULL ||
		(physcollision = (IPhysicsCollision*)physicsFactory(VPHYSICS_COLLISION_INTERFACE_VERSION, NULL)) == NULL)
	{
		return false;
	}

	IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient;
	if (!pPhysics || pPhysics->ParseSurfaceData() < 0)
		Warning("[SourceSharp] the surface property manifest was not parsed: %s\n",
				pPhysics ? "managed has no surface table" : "no managed load to parse it");
	return true;
}

void CPhysicsClient::Reset()
{
	if (IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient)
		pPhysics->Reset();
}

void CPhysicsClient::Simulate()
{
	if (IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient)
		pPhysics->Simulate();
}

// The four level events, forwarded to managed.
void CPhysicsClient::LevelInitPreEntity()
{
	if (IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient)
		pPhysics->LevelInitPreEntity();
}

void CPhysicsClient::LevelInitPostEntity()
{
	if (IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient)
		pPhysics->LevelInitPostEntity();
}

void CPhysicsClient::LevelShutdownPreEntity()
{
	if (IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient)
		pPhysics->LevelShutdownPreEntity();
}

void CPhysicsClient::LevelShutdownPostEntity()
{
	if (IManagedPhysicsClient* pPhysics = m_Areas.m_pPhysicsClient)
		pPhysics->LevelShutdownPostEntity();
}

int32_t CPhysicsClient::PhysicsClientSurfaceAudio(int32_t nSurfaceProps, char* pHard, int32_t nHard, char* pSoft,
												  int32_t nSoft, float* pOut3)
{
	if (pHard && nHard > 0)
		pHard[0] = 0;
	if (pSoft && nSoft > 0)
		pSoft[0] = 0;

	if (!physprops || nSurfaceProps < 0)
		return 0;

	const surfacedata_t* pSurf = physprops->GetSurfaceData(nSurfaceProps);
	if (!pSurf)
		return 0;

	if (pSurf->sounds.impactHard && pHard && nHard > 0)
		V_strncpy(pHard, physprops->GetString(pSurf->sounds.impactHard), nHard);
	if (pSurf->sounds.impactSoft && pSoft && nSoft > 0)
		V_strncpy(pSoft, physprops->GetString(pSurf->sounds.impactSoft), nSoft);

	if (pOut3)
	{
		pOut3[0] = pSurf->audio.hardnessFactor;
		pOut3[1] = pSurf->audio.hardThreshold;
		pOut3[2] = pSurf->audio.hardVelocityThreshold;
	}
	return 1;
}

void CPhysicsClient::PhysicsClientCollideExtent(void* pObject, const float* pOrigin, const float* pAngles,
												const float* pDirection, float* pOut)
{
	if (!pOut)
		return;

	pOut[0] = pOut[1] = pOut[2] = 0.0f;

	IPhysicsObject* pPhysics = static_cast<IPhysicsObject*>(pObject);
	if (!pPhysics || !physcollision || !pOrigin || !pAngles || !pDirection)
		return;

	const Vector extent = physcollision->CollideGetExtent(
		pPhysics->GetCollide(), Vector(pOrigin[0], pOrigin[1], pOrigin[2]), QAngle(pAngles[0], pAngles[1], pAngles[2]),
		Vector(pDirection[0], pDirection[1], pDirection[2]));

	pOut[0] = extent.x;
	pOut[1] = extent.y;
	pOut[2] = extent.z;
}

int32_t CPhysicsClient::PhysicsClientPointContents(const float* pPoint)
{
	if (!pPoint || !enginetrace)
		return 0;

	return enginetrace->GetPointContents(Vector(pPoint[0], pPoint[1], pPoint[2]));
}

void CPhysicsClient::PhysicsClientSplashLighting(const float* pPoint, float* pOut4)
{
	if (!pOut4)
		return;

	Vector color = vec3_origin;
	float flLuminosity = 1.0f;
	if (pPoint)
		FX_GetSplashLighting(m_Areas.m_pParticlesClient, Vector(pPoint[0], pPoint[1], pPoint[2]), &color,
							 &flLuminosity);

	pOut4[0] = color.x;
	pOut4[1] = color.y;
	pOut4[2] = color.z;
	pOut4[3] = flLuminosity;
}

void CPhysicsClient::PhysicsClientSplashEffect(int32_t nKind, const float* pOrigin, const float* pNormal, float flScale,
											   const float* pColor, float flLifetime, float flAlpha)
{
	if (!pOrigin || !pNormal)
		return;

	const Vector origin(pOrigin[0], pOrigin[1], pOrigin[2]);
	const Vector normal(pNormal[0], pNormal[1], pNormal[2]);

	IManagedParticlesClient* pArea = m_Areas.m_pParticlesClient;
	if (!pArea)
		return;

	// ClientSplashKind's order, as the managed splash's: the gunshot's, the slime's, a ripple.
	if (nKind == 0 || nKind == 1)
	{
		pArea->WaterSplash(nKind, origin.Base(), normal.Base(), flScale, NULL, 0.0f, 0.0f);
	}
	else if (nKind == 2)
	{
		const Vector color = pColor ? Vector(pColor[0], pColor[1], pColor[2]) : vec3_origin;
		pArea->WaterSplash(2, origin.Base(), NULL, flScale, color.Base(), flLifetime, flAlpha);
	}
}
