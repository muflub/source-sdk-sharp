//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
#pragma once

#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
#include "tier0/dbg.h"

struct ShellCamera_t
{
	Vector origin;
	QAngle angles;
	Vector forward, right, up;
	VMatrix worldToView;
};

// The view state stock kept in statics: the main view SetUpViews made (and the last
// frame's) and the view being drawn now. CShellViewRender writes it; it depends on
// nothing, so whatever draws can hold it without holding the view render.
class CViewCameras
{
public:
	const Vector& MainOrigin() const { return m_Main.origin; }

	const QAngle& MainAngles() const { return m_Main.angles; }

	const Vector& MainForward() const { return m_Main.forward; }

	const Vector& MainRight() const { return m_Main.right; }

	const Vector& MainUp() const { return m_Main.up; }

	const VMatrix& MainWorldToView() const { return m_Main.worldToView; }

	const Vector& PrevMainOrigin() const { return m_vecPrevMainOrigin; }

	const QAngle& PrevMainAngles() const { return m_angPrevMainAngles; }

	const Vector& CurrentOrigin() const
	{
		Assert(m_bCanAccessCurrentView);
		return m_Current.origin;
	}

	const QAngle& CurrentAngles() const
	{
		Assert(m_bCanAccessCurrentView);
		return m_Current.angles;
	}

	const Vector& CurrentForward() const
	{
		Assert(m_bCanAccessCurrentView);
		return m_Current.forward;
	}

	const Vector& CurrentRight() const
	{
		Assert(m_bCanAccessCurrentView);
		return m_Current.right;
	}

	const Vector& CurrentUp() const
	{
		Assert(m_bCanAccessCurrentView);
		return m_Current.up;
	}

	const VMatrix& CurrentWorldToView() const
	{
		Assert(m_bCanAccessCurrentView);
		return m_Current.worldToView;
	}

	int CurrentID() const { return m_nCurrentViewID; }

	ShellCamera_t m_Main;
	Vector m_vecPrevMainOrigin = vec3_origin;
	QAngle m_angPrevMainAngles = vec3_angle;
	ShellCamera_t m_Current;
	int m_nCurrentViewID = -1; // VIEW_NONE
	bool m_bCanAccessCurrentView = false;
};
