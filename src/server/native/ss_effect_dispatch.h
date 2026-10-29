//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The server's effect record, read field by field by TE_DispatchEffect.
// Members, order and defaults match stock's CEffectData.

#pragma once

#include "mathlib/vector.h"

class IRecipientFilter;

namespace ss
{

// m_fFlags bits the server sets (shareddefs.h's FX_WATER_IN_SLIME).
enum EffectFlag
{
	kEffectFlagWaterInSlime = 0x1,
};

// particle_parse.h's PATTACH_ABSORIGIN: create at the origin, don't follow.
enum EffectAttach
{
	kEffectAttachAbsOrigin = 0,
};

// particle_parse.h's te_tf_particle_effects_colors_t / _control_point_t.
struct EffectColors_t
{
	Vector m_vecColor1, m_vecColor2;
};

struct EffectControlPoint_t
{
	int m_eParticleAttachment; // a ParticleAttachment_t
	Vector m_vecOffset;
};

struct CEffectData
{
	Vector m_vOrigin, m_vStart, m_vNormal;
	QAngle m_vAngles;
	int m_fFlags;
	int m_nEntIndex;
	float m_flScale, m_flMagnitude, m_flRadius;
	int m_nAttachmentIndex;
	short m_nSurfaceProp;
	int m_nMaterial, m_nDamageType, m_nHitBox;
	unsigned char m_nColor;

	bool m_bCustomColors;
	EffectColors_t m_CustomColors;

	bool m_bControlPoint1;
	EffectControlPoint_t m_ControlPoint1;

	// Unused on the server; kept so the type's size matches stock's.
	int m_iEffectName;

	CEffectData()
	{
		m_vOrigin.Init();
		m_vStart.Init();
		m_vNormal.Init();
		m_vAngles.Init();
		m_fFlags = 0;
		m_nEntIndex = 0;
		m_flScale = 1.f;
		m_nAttachmentIndex = 0;
		m_nSurfaceProp = 0;
		m_flMagnitude = 0.0f;
		m_flRadius = 0.0f;
		m_nMaterial = 0;
		m_nDamageType = 0;
		m_nHitBox = 0;
		m_nColor = 0;
		m_bCustomColors = false;
		m_CustomColors.m_vecColor1.Init();
		m_CustomColors.m_vecColor2.Init();
		m_bControlPoint1 = false;
		m_ControlPoint1.m_eParticleAttachment = kEffectAttachAbsOrigin;
		m_ControlPoint1.m_vecOffset.Init();
	}
};

} // namespace ss

// Sends the named effect to everyone in its PVS.
void DispatchEffect(const char* pName, const ss::CEffectData& data);
