//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the player's hull and eye offsets, server side =====//
//
// plan_server_novalve.md P5. The rules object answers GetViewVectors() with
// nine vectors: the eye height, the standing / ducked / observer hulls and the
// dead eye height. Nothing outside server.so reads them, so on the server they
// are server.so's own type, in namespace ss, on public/ headers alone.
// igamerules.h names it per side as RulesViewVectors_t (the client keeps
// shareddefs.h's CViewVectors). Complete rather than forward-declared,
// because shareddefs.h's VEC_* macros dereference GetViewVectors() in the TUs
// that still use them.
//=============================================================================//

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

	// In stock's order: the standing eye and hull, the ducked hull and eye,
	// the observer hull, the dead eye height.
	Vector m_vView, m_vHullMin, m_vHullMax;
	Vector m_vDuckHullMin, m_vDuckHullMax, m_vDuckView;
	Vector m_vObsHullMin, m_vObsHullMax;
	Vector m_vDeadViewHeight;
};

} // namespace ss
