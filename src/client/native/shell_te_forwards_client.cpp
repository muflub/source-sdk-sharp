//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// CTempEntsSystemClient's bodies (shell_te_system_client.h).

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

#include "itempents.h"
#include "effect_dispatch_data.h"

#include "imanaged_areas_client.h"
#include "shell_te_system_client.h"

void CTempEntsSystemClient::DispatchEffect(IRecipientFilter& filter, float delay, const Vector& pos, const char* pName,
										   const CEffectData& data)
{
	IManagedTempEntsClient* pTempEnts = m_Areas.m_pTempEntsClient;
	if (!pTempEnts || pTempEnts->SuppressLocalTempEnt(CanPredict() ? 1 : 0, filter.GetRecipientCount()))
		return;

	DispatchEffectToCallback(pName, data);
}

void CTempEntsSystemClient::BeamEntPoint(IRecipientFilter&, float, int, const Vector*, int, const Vector*, int, int,
										 int, int, float, float, float, int, float, int, int, int, int, int)
{
}

void CTempEntsSystemClient::BeamEnts(IRecipientFilter&, float, int, int, int, int, int, int, float, float, float, int,
									 float, int, int, int, int, int)
{
}

void CTempEntsSystemClient::BeamFollow(IRecipientFilter&, float, int, int, int, float, float, float, float, float,
									   float, float, float)
{
}

void CTempEntsSystemClient::BeamPoints(IRecipientFilter&, float, const Vector*, const Vector*, int, int, int, int,
									   float, float, float, int, float, int, int, int, int, int)
{
}

void CTempEntsSystemClient::BeamLaser(IRecipientFilter&, float, int, int, int, int, int, int, float, float, float, int,
									  float, int, int, int, int, int)
{
}

void CTempEntsSystemClient::BeamRing(IRecipientFilter&, float, int, int, int, int, int, int, float, float, int, float,
									 int, int, int, int, int, int)
{
}

void CTempEntsSystemClient::BeamRingPoint(IRecipientFilter&, float, const Vector&, float, float, int, int, int, int,
										  float, float, int, float, int, int, int, int, int, int)
{
}

void CTempEntsSystemClient::BreakModel(IRecipientFilter&, float, const Vector&, const QAngle&, const Vector&,
									   const Vector&, int, int, int, float, int)
{
}

void CTempEntsSystemClient::Explosion(IRecipientFilter&, float, const Vector*, int, float, int, int, int, int,
									  const Vector*, unsigned char)
{
}

void CTempEntsSystemClient::ShatterSurface(IRecipientFilter&, float, const Vector*, const QAngle*, const Vector*,
										   const Vector*, float, float, float, ShatterSurface_t, int, int, int, int,
										   int, int)
{
}

void CTempEntsSystemClient::PhysicsProp(IRecipientFilter&, float, int, int, const Vector&, const QAngle&, const Vector&,
										int, int)
{
}

void CTempEntsSystemClient::ClientProjectile(IRecipientFilter&, float, const Vector*, const Vector*, int, int,
											 IGameEntity*)
{
}

void CTempEntsSystemClient::TriggerTempEntity(KeyValues*)
{
	Assert(0);
}
