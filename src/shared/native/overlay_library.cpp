//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data

#include "overlay_library.h"

#include "mathlib/mathlib.h"
#include "engine/ivdebugoverlay.h"
#include "tier1/strtools.h"

// Stock's MAX_OVERLAY_DIST_SQR.
#define OVERLAY_MAX_DIST_SQR 90000000.0f

COverlayLibrary::COverlayLibrary(IVDebugOverlay* pOverlay)
	: m_pOverlay(pOverlay)
	, m_Viewer{Vector(0, 0, 0), Vector(0, 0, 0), false}
	, m_Counts{0, 0, 0, 0}
	, m_nNextPoolLine(0)
{
	for (PoolLine_t& line : m_Pool)
		line = {Vector(0, 0, 0), Vector(0, 0, 0), 255, 255, 255, true, false};
}

bool COverlayLibrary::HaveOverlay()
{
	if (m_pOverlay)
		return true;

	m_Counts.nNoOverlay++;
	return false;
}

void COverlayLibrary::ResetCounts()
{
	m_Counts = {0, 0, 0, 0};
}

void COverlayLibrary::SetViewer(const Vector& vecEyeOrigin, const Vector& vecEyeForward)
{
	m_Viewer.vecOrigin = vecEyeOrigin;
	m_Viewer.vecForward = vecEyeForward;
	m_Viewer.bSet = true;
}

bool COverlayLibrary::SegmentIsVisible(const Vector& vecStart, const Vector& vecEnd) const
{
	const Viewer_t& v = m_Viewer;
	if (!v.bSet)
		return false;

	const float flStartSqr = (v.vecOrigin - vecStart).LengthSqr();
	const float flEndSqr = (v.vecOrigin - vecEnd).LengthSqr();
	if (flStartSqr > OVERLAY_MAX_DIST_SQR && flEndSqr > OVERLAY_MAX_DIST_SQR)
		return false;

	const float flDotStart = DotProduct(v.vecForward, vecStart - v.vecOrigin);
	const float flDotEnd = DotProduct(v.vecForward, vecEnd - v.vecOrigin);
	if (flDotStart < 0.0f && flDotEnd < 0.0f)
		return false;

	return true;
}

void COverlayLibrary::BoxAngles(const Vector& origin, const Vector& mins, const Vector& maxs, const QAngle& angles,
								int r, int g, int b, int a, float flDuration)
{
	if (!HaveOverlay())
		return;

	m_pOverlay->AddBoxOverlay(origin, mins, maxs, angles, r, g, b, a, flDuration);
	m_Counts.nDrawn++;
}

void COverlayLibrary::Box(const Vector& origin, const Vector& mins, const Vector& maxs, int r, int g, int b, int a,
						  float flDuration)
{
	BoxAngles(origin, mins, maxs, vec3_angle, r, g, b, a, flDuration);
}

void COverlayLibrary::TextAtPosition(const Vector& origin, int nLineOffset, const char* pText, float flDuration, int r,
									 int g, int b, int a)
{
	if (!pText)
		return;

	if (!HaveOverlay())
		return;

	m_pOverlay->AddTextOverlayRGB(origin, nLineOffset, flDuration, r, g, b, a, "%s", pText);
	m_Counts.nDrawn++;
}

void COverlayLibrary::ClearAll()
{
	if (!HaveOverlay())
		return;

	m_pOverlay->ClearAllOverlays();
}

void COverlayLibrary::Line(const Vector& origin, const Vector& target, int r, int g, int b, bool bNoDepthTest,
						   float flDuration)
{
	if (!ViewerIsSet())
	{
		m_Counts.nNoViewer++;
		return;
	}

	if (!SegmentIsVisible(origin, target))
	{
		m_Counts.nClipped++;
		return;
	}

	if (!HaveOverlay())
		return;

	m_pOverlay->AddLineOverlay(origin, target, r, g, b, bNoDepthTest, flDuration);
	m_Counts.nDrawn++;
}

void COverlayLibrary::AddLine(const Vector& vecStart, const Vector& vecEnd, bool bNoDepthTest, int r, int g, int b)
{
	m_Pool[m_nNextPoolLine] = {vecStart, vecEnd, r, g, b, bNoDepthTest, true};
	m_nNextPoolLine = (m_nNextPoolLine + 1) % kPoolLines;
}

void COverlayLibrary::DrawPooledLines()
{
	for (const PoolLine_t& line : m_Pool)
	{
		if (line.bDraw)
			Line(line.vecOrigin, line.vecDest, line.r, line.g, line.b, line.bNoDepthTest, 0);
	}
}
