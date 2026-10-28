//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The player's hull and eye offsets, as the rules object's GetViewVectors answers
// them. Complete, because shareddefs.h's VEC_* macros dereference it.

#pragma once

#include "mathlib/vector.h"

namespace ss
{

class CViewVectors
{
public:
	CViewVectors() {}

	CViewVectors( Vector vView, Vector vHullMin, Vector vHullMax,
	              Vector vDuckHullMin, Vector vDuckHullMax, Vector vDuckView,
	              Vector vObsHullMin, Vector vObsHullMax, Vector vDeadViewHeight )
	{
		m_vView = vView;  m_vHullMin = vHullMin;  m_vHullMax = vHullMax;
		m_vDuckHullMin = vDuckHullMin;  m_vDuckHullMax = vDuckHullMax;  m_vDuckView = vDuckView;
		m_vObsHullMin = vObsHullMin;  m_vObsHullMax = vObsHullMax;  m_vDeadViewHeight = vDeadViewHeight;
	}

	Vector m_vView, m_vHullMin, m_vHullMax;
	Vector m_vDuckHullMin, m_vDuckHullMax, m_vDuckView;
	Vector m_vObsHullMin, m_vObsHullMax;
	Vector m_vDeadViewHeight;
};

} // namespace ss
