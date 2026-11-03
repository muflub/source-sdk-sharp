//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// CEffectsClient (shell_effects_client.h), the client's IEffects. No binary asks the
// factory for IEffects001 (the string is in no engine binary), so it is not exposed;
// the temp entities' draws reach it through g_pEffects.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "IEffects.h"
#include "fx.h"
#include "c_recipientfilter.h"

#include "imanaged_areas_client.h"
#include "shell_effects_client.h"

void CEffectsClient::Beam(const Vector& Start, const Vector& End, int nModelIndex, int nHaloIndex,
						  unsigned char frameStart, unsigned char frameRate, float flLife, unsigned char width,
						  unsigned char endWidth, unsigned char fadeLength, unsigned char noise, unsigned char red,
						  unsigned char green, unsigned char blue, unsigned char brightness, unsigned char speed)
{
	// Stock's is Assert(0) and draws nothing; nothing calls it.
}

void CEffectsClient::Smoke(const Vector& origin, int modelIndex, float scale, float framerate)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->Smoke(origin.Base(), modelIndex, scale, framerate);
}

void CEffectsClient::Sparks(const Vector& position, int nMagnitude, int nTrailLength, const Vector* pvecDir)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->Sparks(position.Base(), nMagnitude, nTrailLength, pvecDir ? pvecDir->Base() : NULL);
}

void CEffectsClient::Dust(const Vector& pos, const Vector& dir, float size, float speed)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->Dust(pos.Base(), dir.Base(), size, speed);
}

void CEffectsClient::MuzzleFlash(const Vector& origin, const QAngle& angles, float fScale, int type)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->MuzzleFlash(origin.Base(), angles.Base(), fScale, type);
}

void CEffectsClient::MetalSparks(const Vector& position, const Vector& direction)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->MetalSparks(position.Base(), direction.Base());
}

void CEffectsClient::EnergySplash(const Vector& position, const Vector& direction, bool bExplosive)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->EnergySplash(position.Base(), direction.Base(), bExplosive ? 1 : 0);
}

void CEffectsClient::Ricochet(const Vector& position, const Vector& direction)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->Ricochet(position.Base(), direction.Base());
}

float CEffectsClient::Time()
{
	return gpGlobals->curtime;
}

bool CEffectsClient::IsServer()
{
	return false;
}

void CEffectsClient::SuppressEffectsSounds(bool bSuppress)
{
	if (IManagedFxClient* pFx = m_Areas.m_pFxClient)
		pFx->SuppressEffectsSounds(bSuppress ? 1 : 0);
}

// The client DLL object points it at its member from Init.
IEffects* g_pEffects = NULL;

int32_t CEffectsClient::FxCanPredict()
{
	return CanPredict() ? 1 : 0;
}

// What SuppressTE reads beside FxCanPredict: a client PVS filter's count.
int32_t SourceSharp_FxRecipientCount(const float* pOriginXYZ)
{
	CPVSFilter filter(Vector(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]));
	return filter.GetRecipientCount();
}
