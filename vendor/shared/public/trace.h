//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "mathlib/mathlib.h"

// Note: These flags need to match the bspfile.h DISPTRI_TAG_* flags.

// Base Trace Structure
// - shared between engine/game dlls and tools (vrad)

class CBaseTrace
{
public:

public:

	// these members are aligned!!
	Vector			startpos;				// start position
	Vector			endpos;					// final position
	cplane_t		plane;					// surface normal at impact

	float			fraction;				// time completed, 1.0 = didn't hit anything

	int				contents;				// contents on other side of surface hit
	unsigned short	dispFlags;				// displacement flags for marking surfaces with data

	bool			allsolid;				// if true, plane is not valid
	bool			startsolid;				// if true, the initial point was in a solid area

	CBaseTrace() = default;

private:
	// No copy constructors allowed
	CBaseTrace(const CBaseTrace& vOther);
};
