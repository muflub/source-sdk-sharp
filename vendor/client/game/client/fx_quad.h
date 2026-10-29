//========= Copyright Valve Corporation, All rights reserved. ============//

#include "clientsideeffects.h"

#ifndef FX_QUAD_H
#define FX_QUAD_H
#ifdef _WIN32
#pragma once
#endif

// Flags
#define FXQUAD_BIAS_SCALE 0x0001 //Bias the scale's interpolation function
#define FXQUAD_BIAS_ALPHA 0x0002 //Bias the alpha's interpolation function

//Blend the color towards black via the alpha (overcomes additive ignoring alpha)

struct FXQuadData_t
{
	FXQuadData_t()
	{
		m_flLifeTime = 0.0f;
		m_flDieTime = 0.0f;
		m_uiFlags = 0;
	}

	void SetColor(float r, float g, float b) { m_Color = Vector(r, g, b); }

	unsigned int m_uiFlags;
	IMaterial* m_pMaterial;
	Vector m_vecOrigin;
	Vector m_vecNormal;
	float m_flStartScale;
	float m_flEndScale;
	float m_flDieTime;
	float m_flLifeTime;
	float m_flStartAlpha;
	float m_flEndAlpha;
	Vector m_Color;
	float m_flYaw;
	float m_flDeltaYaw;

	// Only used with FXQUAD_BIAS_ALPHA and FXQUAD_BIAS_SCALE
	float m_flScaleBias;
	float m_flAlphaBias;
};

class CFXQuad : public CClientSideEffect
{
public:
	CFXQuad(const FXQuadData_t& data);

	~CFXQuad();

	virtual void Draw(double frametime);
	virtual bool IsActive();
	virtual void Destroy();
	virtual void Update(double frametime);

	FXQuadData_t m_FXData;
};

#endif // FX_QUAD_H
