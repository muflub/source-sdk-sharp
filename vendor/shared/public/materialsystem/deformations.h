//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// nonlinear transformations which may be applied to model vertices when rendering. must be powers of two
enum DeformationType_t	
{
	DEFORMATION_CLAMP_TO_BOX_IN_WORLDSPACE = 1,							// minxyz.minsoftness / maxxyz.maxsoftness
};

struct DeformationBase_t										// base class. don't use this
{
	DeformationType_t m_eType;
};
