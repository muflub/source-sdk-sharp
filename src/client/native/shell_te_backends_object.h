//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The temp entity backends' draws through IEffects and the decal table: what a managed temp
// entity asks for by name. A member of the client DLL object.

#pragma once

#include <stdint.h>

class CClientEngineInterfaces;
class IEffects;

class CTempEntBackendsClient final
{
public:
	CTempEntBackendsClient(const CClientEngineInterfaces& engines, IEffects& effects)
		: m_Engines(engines)
		, m_Effects(effects)
	{
	}

	CTempEntBackendsClient(const CTempEntBackendsClient&) = delete;
	CTempEntBackendsClient& operator=(const CTempEntBackendsClient&) = delete;

	// Shim slots.
	int32_t ClientDecalNameFromIndex(int32_t nDecalIndex, char* pOut, int32_t nOutSize);
	void ClientEnergySplash(const float* pPosXYZ, const float* pDirXYZ, int32_t bExplosive);
	void ClientMetalSparks(const float* pPositionXYZ, const float* pDirectionXYZ);
	void ClientMuzzleFlash(const float* pOriginXYZ, const float* pAnglesPYR, float flScale, int32_t nType);
	void ClientPlayerDecalShoot(const float*, int32_t, int32_t);
	void ClientRicochet(const float* pPositionXYZ, const float* pDirectionXYZ);
	void ClientSmoke(const float* pOriginXYZ, int32_t nModelIndex, float flScale, float flFrameRate);
	void ClientSparks(const float* pPositionXYZ, int32_t nMagnitude, int32_t nTrailLength, const float* pDirectionXYZ);
	void EffectDispatchName(int32_t nIndex, char* pOut, int32_t nOutBytes);

private:
	const CClientEngineInterfaces& m_Engines;
	IEffects& m_Effects;
};
