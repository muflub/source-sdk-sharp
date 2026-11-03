//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// The door an effect dispatch comes through; managed picks the effect by name.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "c_te_effect_dispatch.h"

#include "c_recipientfilter.h"
#include "itempents.h"

#include "shell_cliententitylist.h"

#include "imanaged_areas_client.h"
#include "shell_te_system_client.h"
#include "client_entity_list.h"

namespace
{

// The handle crosses as its entry index, -1 for none.
void EffectDataFromClient(const CEffectData& data, SourceSharpEffectData_t* pOut)
{
	SourceSharpEffectData_t& d = *pOut;

	d.OriginXYZ[0] = data.m_vOrigin.x;
	d.OriginXYZ[1] = data.m_vOrigin.y;
	d.OriginXYZ[2] = data.m_vOrigin.z;
	d.StartXYZ[0] = data.m_vStart.x;
	d.StartXYZ[1] = data.m_vStart.y;
	d.StartXYZ[2] = data.m_vStart.z;
	d.NormalXYZ[0] = data.m_vNormal.x;
	d.NormalXYZ[1] = data.m_vNormal.y;
	d.NormalXYZ[2] = data.m_vNormal.z;
	d.AnglesPYR[0] = data.m_vAngles.x;
	d.AnglesPYR[1] = data.m_vAngles.y;
	d.AnglesPYR[2] = data.m_vAngles.z;

	d.Flags = data.m_fFlags;
	d.EntIndex = data.m_hEntity.IsValid() ? data.m_hEntity.GetEntryIndex() : -1;
	d.Scale = data.m_flScale;
	d.Magnitude = data.m_flMagnitude;
	d.Radius = data.m_flRadius;
	d.AttachmentIndex = data.m_nAttachmentIndex;
	d.SurfaceProp = data.m_nSurfaceProp;
	d.Material = data.m_nMaterial;
	d.DamageType = data.m_nDamageType;
	d.HitBox = data.m_nHitBox;
	d.Color = data.m_nColor;

	d.CustomColors = data.m_bCustomColors ? 1 : 0;
	const Vector& c1 = data.m_CustomColors.m_vecColor1;
	const Vector& c2 = data.m_CustomColors.m_vecColor2;
	d.Color1XYZ[0] = c1.x;
	d.Color1XYZ[1] = c1.y;
	d.Color1XYZ[2] = c1.z;
	d.Color2XYZ[0] = c2.x;
	d.Color2XYZ[1] = c2.y;
	d.Color2XYZ[2] = c2.z;

	d.ControlPoint1 = data.m_bControlPoint1 ? 1 : 0;
	d.ControlPointAttachment = (int32_t)data.m_ControlPoint1.m_eParticleAttachment;
	const Vector& off = data.m_ControlPoint1.m_vecOffset;
	d.ControlPointOffsetXYZ[0] = off.x;
	d.ControlPointOffsetXYZ[1] = off.y;
	d.ControlPointOffsetXYZ[2] = off.z;
}

} // namespace

void CTempEntsSystemClient::DispatchEffectToCallback(const char* pEffectName, const CEffectData& data)
{
	IManagedFxClient* pFx = m_Areas.m_pFxClient;
	if (!pFx || !pEffectName)
		return;

	SourceSharpEffectData_t d;
	EffectDataFromClient(data, &d);
	pFx->DispatchEffect(pEffectName, &d);
}

// The effect's entity is cleared on this side whatever it is handed: it has no owner.
void CTempEntsSystemClient::FxDispatchParticleEffect(int32_t nSystem, const float* pStartXYZ, const float* pEndXYZ,
													 const float* pAnglesPYR, int32_t nEntIndex)
{
	CEffectData data;

	data.m_nHitBox = nSystem;
	data.m_vOrigin.Init(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]);
	data.m_vStart.Init(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]);
	data.m_vAngles.Init(pAnglesPYR[0], pAnglesPYR[1], pAnglesPYR[2]);
	data.m_hEntity = NULL;

	CPASFilter filter(m_Engines.m_pEngine, data.m_vOrigin);
	DispatchEffect(filter, 0.0, data.m_vOrigin, "ParticleEffect", data);
}
