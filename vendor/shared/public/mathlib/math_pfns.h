//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//=====================================================================================//

#ifndef _MATH_PFNS_H_
#define _MATH_PFNS_H_

#pragma once

#include <math.h>

// misyl: This is faster than doing fsincos these days.
inline void SinCos( float radians, float *RESTRICT sine, float *RESTRICT cosine )
{
	*sine = sinf( radians );
	*cosine = cosf( radians );
}

#define FastRSqrt( x ) ( 1.0f / ::sqrtf( x ) )


#define FastSqrt ::sqrtf



#endif // _MATH_PFNS_H_
