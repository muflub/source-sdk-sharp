//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CCollisionProperty::WorldSpaceCenter over an ICollideable's answers, as floats
// so a test can run it without mathlib. Testing "angles are zero" alone picks
// stock's add arm in every reachable case: world-aligned bounds report zero
// angles. Both arms follow stock's evaluation order.
#pragma once

// flXform is the row-major 3x4 CollisionToWorldTransform, read only when
// bAnglesAreZero is false.
inline void CollideableCenter_Compute(const float flMins[3], const float flMaxs[3], bool bAnglesAreZero,
									  const float flOrigin[3], const float flXform[12], float flOut[3])
{
	// OBBCenter, in VectorLerp's own form.
	float flCenter[3];
	for (int i = 0; i < 3; ++i)
	{
		flCenter[i] = flMins[i] + (flMaxs[i] - flMins[i]) * 0.5f;
	}

	if (bAnglesAreZero)
	{
		for (int i = 0; i < 3; ++i)
		{
			flOut[i] = flCenter[i] + flOrigin[i];
		}
		return;
	}

	// VectorTransform: DotProduct( center, row ) + row[3].
	for (int i = 0; i < 3; ++i)
	{
		const float* flRow = &flXform[i * 4];
		flOut[i] = flCenter[0] * flRow[0] + flCenter[1] * flRow[1] + flCenter[2] * flRow[2] + flRow[3];
	}
}
