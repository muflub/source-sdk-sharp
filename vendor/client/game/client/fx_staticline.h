//========= Copyright Valve Corporation, All rights reserved. ============//

#include "clientsideeffects.h"

#if !defined( FXSTATICLINE_H )
#define FXSTATICLINE_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"

class IMaterial;

	//Swaps the TC's so the texture is flipped horizontally
	//Swaps the TC's so the texture is flipped vertically

class CFXStaticLine : public CClientSideEffect
{
public:

	CFXStaticLine( const char *name, const Vector& start, const Vector& end, float scale, float life, const char *shader, unsigned int flags );
	~CFXStaticLine();

	virtual void	Draw( double frametime );
	virtual bool	IsActive();
	virtual void	Destroy();
	virtual	void	Update( double frametime );

protected:

	IMaterial		*m_pMaterial;
	Vector			m_vecStart, m_vecEnd;
	unsigned int	m_uiFlags;
	float			m_fLife;
	float			m_fScale;
};

#endif
