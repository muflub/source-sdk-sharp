//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// IEffects001, the client's IEffects, a thunk: each effect method carries its
// arguments to IManagedFxClient. It is still an IPredictionSystem node, which is
// the state CanPredict() reads. IShimFxClient's Fx* slots are the FX_* routines
// managed's decisions end in.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "IEffects.h"
#include "fx.h"
#include "c_recipientfilter.h"

#include "imanaged_areas_client.h"

namespace
{

class CEffectsThunk final : public IEffects
{
public:
	void Beam(const Vector& Start, const Vector& End, int nModelIndex, int nHaloIndex, unsigned char frameStart,
			  unsigned char frameRate, float flLife, unsigned char width, unsigned char endWidth,
			  unsigned char fadeLength, unsigned char noise, unsigned char red, unsigned char green, unsigned char blue,
			  unsigned char brightness, unsigned char speed) override
	{
		// Stock's is Assert(0) and draws nothing; nothing calls it.
	}

	void Smoke(const Vector& origin, int modelIndex, float scale, float framerate) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->Smoke(origin.Base(), modelIndex, scale, framerate);
	}

	void Sparks(const Vector& position, int nMagnitude = 1, int nTrailLength = 1, const Vector* pvecDir = NULL) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->Sparks(position.Base(), nMagnitude, nTrailLength, pvecDir ? pvecDir->Base() : NULL);
	}

	void Dust(const Vector& pos, const Vector& dir, float size, float speed) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->Dust(pos.Base(), dir.Base(), size, speed);
	}

	void MuzzleFlash(const Vector& origin, const QAngle& angles, float fScale, int type) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->MuzzleFlash(origin.Base(), angles.Base(), fScale, type);
	}

	void MetalSparks(const Vector& position, const Vector& direction) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->MetalSparks(position.Base(), direction.Base());
	}

	void EnergySplash(const Vector& position, const Vector& direction, bool bExplosive = false) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->EnergySplash(position.Base(), direction.Base(), bExplosive ? 1 : 0);
	}

	void Ricochet(const Vector& position, const Vector& direction) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->Ricochet(position.Base(), direction.Base());
	}

	float Time() override { return gpGlobals->curtime; }

	bool IsServer() override { return false; }

	void SuppressEffectsSounds(bool bSuppress) override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->SuppressEffectsSounds(bSuppress ? 1 : 0);
	}
};

CEffectsThunk s_EffectsThunk;

Vector V(const float* p)
{
	return Vector(p[0], p[1], p[2]);
}

} // namespace

EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CEffectsThunk, IEffects, IEFFECTS_INTERFACE_VERSION, s_EffectsThunk);
IEffects* g_pEffects = &s_EffectsThunk;

// What SuppressTE reads: this IEffects' IPredictionSystem state, and a client
// PVS filter's count.
int32_t SourceSharp_FxCanPredict()
{
	return s_EffectsThunk.CanPredict() ? 1 : 0;
}

int32_t SourceSharp_FxRecipientCount(const float* pOriginXYZ)
{
	CPVSFilter filter(V(pOriginXYZ));
	return filter.GetRecipientCount();
}
