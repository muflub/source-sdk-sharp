//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

struct FXLineData_t
{
	Vector m_vecStart;
	Vector m_vecEnd;
	Vector m_vecStartVelocity;
	Vector m_vecEndVelocity;
	float m_flStartAlpha;
	float m_flEndAlpha;
	float m_flStartScale;
	float m_flEndScale;
	float m_flDieTime;
	float m_flLifeTime;

	IMaterial *m_pMaterial;
};

#include "fx_staticline.h"

class CFXLine : public CClientSideEffect
{
public:
	CFXLine( const char *name, const FXLineData_t &data );

	~CFXLine();

	virtual void Draw( double frametime );
	virtual bool IsActive();
	virtual void Destroy();
	virtual void Update( double frametime );

protected:
	FXLineData_t m_FXData;
};
