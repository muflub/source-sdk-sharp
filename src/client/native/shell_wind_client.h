//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The wind managed publishes: what stock's env_wind controllers answered. Managed runs the
// controllers, so the head wind's vector is all the native side keeps. A member of the
// client DLL object.

#pragma once

#include <stdint.h>

#include "mathlib/vector.h"
#include "shell_member_command.h"

class CClientEngineInterfaces;

class CWindClient final
{
public:
	explicit CWindClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	CWindClient(const CWindClient&) = delete;
	CWindClient& operator=(const CWindClient&) = delete;

	// Stock's level-init reset: every controller's vector to zero until it next thinks.
	void Reset() { m_vecPublishedWind.Init(0, 0, 0); }

	void WindPublish(const float* pWind, const float* pSway, int32_t nWinds);
	void ClientWindSpeed(float flTime, float* pOutXYZ);

private:
	void ReportCommand(const CCommand& args);

	CMemberCommand<CWindClient, &CWindClient::ReportCommand> m_ReportCommand{
		*this, "cs_cl_wind_report",
		"the wind vector GetWindspeedAtTime hands the four section-6 readers, and the controllers behind it", 0};

	const CClientEngineInterfaces& m_Engines;
	Vector m_vecPublishedWind{0, 0, 0};
	int m_nPublishedWinds = 0;
};
