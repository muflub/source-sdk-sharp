//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <math.h>

// misyl: This is faster than doing fsincos these days.
inline void SinCos( float radians, float *RESTRICT sine, float *RESTRICT cosine )
{
	*sine = sinf( radians );
	*cosine = cosf( radians );
}

#define FastSqrt ::sqrtf
