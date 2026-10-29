//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "particle_parse.h"

EXTERN_RECV_TABLE( DT_EffectData );

// NOTE: These flags are specifically *not* networked; so it's placed above the max effect flag bits

// This is the class that holds whatever data we're sending down to the client to make the effect.
class CEffectData
{
public:
	Vector m_vOrigin;
	Vector m_vStart;
	Vector m_vNormal;
	QAngle m_vAngles;
	int m_fFlags;
	ClientEntityHandle_t m_hEntity;
	float m_flScale;
	float m_flMagnitude;
	float m_flRadius;
	int m_nAttachmentIndex;
	short m_nSurfaceProp;

	// Some TF2 specific things
	// misyl: Not a material! This is a model index most of the time!
	int m_nMaterial;
	int m_nDamageType;
	int m_nHitBox;

	unsigned char m_nColor;

	// Color customizability
	bool m_bCustomColors;
	te_tf_particle_effects_colors_t m_CustomColors;

	bool m_bControlPoint1;
	te_tf_particle_effects_control_point_t m_ControlPoint1;

	// Don't mess with stuff below here. DispatchEffect handles all of this.
public:
	CEffectData()
	{
		m_vOrigin.Init();
		m_vStart.Init();
		m_vNormal.Init();
		m_vAngles.Init();

		m_fFlags = 0;
		m_hEntity = INVALID_EHANDLE;
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
		m_ControlPoint1.m_eParticleAttachment = PATTACH_ABSORIGIN;
		m_ControlPoint1.m_vecOffset.Init();
	}

	IClientRenderable *GetRenderable() const;
	C_BaseEntity *GetEntity() const;
	int entindex() const;

private:
	DECLARE_CLIENTCLASS_NOBASE()

	int m_iEffectName; // Entry in the EffectDispatch network string table. The is automatically handled by DispatchEffect().
};
