//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "mathlib/vector.h"

// Used to represent sides of things like planes.
#define SIDE_FRONT 0
#define SIDE_BACK 1
#define SIDE_ON 2

class VPlane
{
public:
	VPlane() = default;
	VPlane(const Vector& vNormal, vec_t dist);

	void Init(const Vector& vNormal, vec_t dist);

	// Return the distance from the point to the plane.
	vec_t DistTo(const Vector& vVec) const;

	// Get a point on the plane (normal*dist).
	Vector GetPointOnPlane() const;

public:
	Vector m_Normal;
	vec_t m_Dist;
};

// Inlines.

inline VPlane::VPlane(const Vector& vNormal, vec_t dist)
{
	m_Normal = vNormal;
	m_Dist = dist;
}

inline void VPlane::Init(const Vector& vNormal, vec_t dist)
{
	m_Normal = vNormal;
	m_Dist = dist;
}

inline vec_t VPlane::DistTo(const Vector& vVec) const
{
	return vVec.Dot(m_Normal) - m_Dist;
}

inline Vector VPlane::GetPointOnPlane() const
{
	return m_Normal * m_Dist;
}
