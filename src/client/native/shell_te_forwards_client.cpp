//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The client's ITempEntsSystem. Only DispatchEffect has a caller; the other overrides are
// empty because ITempEntsSystem's methods are pure virtual and its vtable layout must stay.
// Whether a predicted effect is suppressed is decided by managed.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

#include "itempents.h"
#include "effect_dispatch_data.h"

#include "imanaged_areas_client.h"

void TE_DispatchEffect(IRecipientFilter& filter, float delay, const Vector& pos, const char* pName,
					   const CEffectData& data);

// Still an IPredictionSystem, so `te` is on the list SuppressHostEvents walks; CanPredict()
// reads that suppress host.
class C_TempEntsSystem : public ITempEntsSystem
{
public:
	virtual void DispatchEffect(IRecipientFilter& filter, float delay, const Vector& pos, const char* pName,
								const CEffectData& data)
	{
		IManagedTempEntsClient* pTempEnts = ManagedTempEntsClient();
		if (!pTempEnts || pTempEnts->SuppressLocalTempEnt(CanPredict() ? 1 : 0, filter.GetRecipientCount()))
			return;

		TE_DispatchEffect(filter, delay, pos, pName, data);
	}

	// Nothing in this build calls these.
	virtual void ArmorRicochet(IRecipientFilter&, float, const Vector*, const Vector*) {}

	virtual void BeamEntPoint(IRecipientFilter&, float, int, const Vector*, int, const Vector*, int, int, int, int,
							  float, float, float, int, float, int, int, int, int, int)
	{
	}

	virtual void BeamEnts(IRecipientFilter&, float, int, int, int, int, int, int, float, float, float, int, float, int,
						  int, int, int, int)
	{
	}

	virtual void BeamFollow(IRecipientFilter&, float, int, int, int, float, float, float, float, float, float, float,
							float)
	{
	}

	virtual void BeamPoints(IRecipientFilter&, float, const Vector*, const Vector*, int, int, int, int, float, float,
							float, int, float, int, int, int, int, int)
	{
	}

	virtual void BeamLaser(IRecipientFilter&, float, int, int, int, int, int, int, float, float, float, int, float, int,
						   int, int, int, int)
	{
	}

	virtual void BeamRing(IRecipientFilter&, float, int, int, int, int, int, int, float, float, int, float, int, int,
						  int, int, int, int)
	{
	}

	virtual void BeamRingPoint(IRecipientFilter&, float, const Vector&, float, float, int, int, int, int, float, float,
							   int, float, int, int, int, int, int, int)
	{
	}

	virtual void BeamSpline(IRecipientFilter&, float, int, Vector*) {}

	virtual void BloodStream(IRecipientFilter&, float, const Vector*, const Vector*, int, int, int, int, int) {}

	virtual void BloodSprite(IRecipientFilter&, float, const Vector*, const Vector*, int, int, int, int, int) {}

	virtual void BreakModel(IRecipientFilter&, float, const Vector&, const QAngle&, const Vector&, const Vector&, int,
							int, int, float, int)
	{
	}

	virtual void BSPDecal(IRecipientFilter&, float, const Vector*, int, int) {}

	virtual void ProjectDecal(IRecipientFilter&, float, const Vector*, const QAngle*, float, int) {}

	virtual void Bubbles(IRecipientFilter&, float, const Vector*, const Vector*, float, int, int, float) {}

	virtual void Smoke(IRecipientFilter&, float, const Vector*, int, float, int) {}

	virtual void BubbleTrail(IRecipientFilter&, float, const Vector*, const Vector*, float, int, int, float) {}

	virtual void Decal(IRecipientFilter&, float, const Vector*, const Vector*, int, int, int) {}

	virtual void DynamicLight(IRecipientFilter&, float, const Vector*, int, int, int, int, float, float, float) {}

	virtual void Explosion(IRecipientFilter&, float, const Vector*, int, float, int, int, int, int,
						   const Vector* = NULL, unsigned char = 'C')
	{
	}

	virtual void ShatterSurface(IRecipientFilter&, float, const Vector*, const QAngle*, const Vector*, const Vector*,
								float, float, float, ShatterSurface_t, int, int, int, int, int, int)
	{
	}

	virtual void GlowSprite(IRecipientFilter&, float, const Vector*, int, float, float, int) {}

	virtual void FootprintDecal(IRecipientFilter&, float, const Vector*, const Vector*, int, int, unsigned char) {}

	virtual void Fizz(IRecipientFilter&, float, const IGameEntity*, int, int, int) {}

	virtual void KillPlayerAttachments(IRecipientFilter&, float, int) {}

	virtual void LargeFunnel(IRecipientFilter&, float, const Vector*, int, int) {}

	virtual void MetalSparks(IRecipientFilter&, float, const Vector*, const Vector*) {}

	virtual void EnergySplash(IRecipientFilter&, float, const Vector*, const Vector*, bool) {}

	virtual void PlayerDecal(IRecipientFilter&, float, const Vector*, int, int) {}

	virtual void ShowLine(IRecipientFilter&, float, const Vector*, const Vector*) {}

	virtual void Sparks(IRecipientFilter&, float, const Vector*, int, int, const Vector*) {}

	virtual void Sprite(IRecipientFilter&, float, const Vector*, int, float, int) {}

	virtual void SpriteSpray(IRecipientFilter&, float, const Vector*, const Vector*, int, int, float, int) {}

	virtual void WorldDecal(IRecipientFilter&, float, const Vector*, int) {}

	virtual void MuzzleFlash(IRecipientFilter&, float, const Vector&, const QAngle&, float, int) {}

	virtual void Dust(IRecipientFilter&, float, const Vector&, const Vector&, float, float) {}

	virtual void GaussExplosion(IRecipientFilter&, float, const Vector&, const Vector&, int) {}

	virtual void PhysicsProp(IRecipientFilter&, float, int, int, const Vector&, const QAngle&, const Vector&, int, int)
	{
	}

	virtual void ClientProjectile(IRecipientFilter&, float, const Vector*, const Vector*, int, int, IGameEntity*) {}

	// Stock's body: triggering a temp entity from KeyValues exists only in tool builds.
	virtual void TriggerTempEntity(KeyValues*) { Assert(0); }
};

static C_TempEntsSystem g_TESystem;

ITempEntsSystem* te = &g_TESystem;
