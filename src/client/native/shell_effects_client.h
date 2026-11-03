//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's IEffects, a member of the client DLL object (g_pEffects points at it from
// Init): each effect method carries its arguments to the managed FX area it was handed. It
// is still an IPredictionSystem, which is the state CanPredict() reads.

#pragma once

#include <stdint.h>

#include "IEffects.h"
#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;

class CEffectsClient final : public IEffects
{
public:
	CEffectsClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines)
		: m_Areas(areas)
		, m_Engines(engines)
	{
	}

	CEffectsClient(const CEffectsClient&) = delete;
	CEffectsClient& operator=(const CEffectsClient&) = delete;

	// Shim slots: what SuppressTE reads.
	int32_t FxRecipientCount(const float* pOriginXYZ);

	void Beam(const Vector& Start, const Vector& End, int nModelIndex, int nHaloIndex, unsigned char frameStart,
			  unsigned char frameRate, float flLife, unsigned char width, unsigned char endWidth,
			  unsigned char fadeLength, unsigned char noise, unsigned char red, unsigned char green, unsigned char blue,
			  unsigned char brightness, unsigned char speed) override;

	void Smoke(const Vector& origin, int modelIndex, float scale, float framerate) override;

	void Sparks(const Vector& position, int nMagnitude = 1, int nTrailLength = 1,
				const Vector* pvecDir = NULL) override;

	void Dust(const Vector& pos, const Vector& dir, float size, float speed) override;

	void MuzzleFlash(const Vector& origin, const QAngle& angles, float fScale, int type) override;

	void MetalSparks(const Vector& position, const Vector& direction) override;

	void EnergySplash(const Vector& position, const Vector& direction, bool bExplosive = false) override;

	void Ricochet(const Vector& position, const Vector& direction) override;

	float Time() override;

	bool IsServer() override;

	void SuppressEffectsSounds(bool bSuppress) override;

	// IShimFxClient: what SuppressTE reads, this IEffects' IPredictionSystem state.
	int32_t FxCanPredict();

private:
	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
};
