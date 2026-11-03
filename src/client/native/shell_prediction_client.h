//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The IPrediction the engine asks for by name, a member of the client DLL object. It
// overrides the two methods that carry the numbers the managed replay needs and calls up
// for the rest.

#pragma once

#include "prediction.h"

#include "shell_managed_areas_client.h"

class CSourceSharpPrediction final : public CPrediction
{
	DECLARE_CLASS(CSourceSharpPrediction, CPrediction);

public:
	explicit CSourceSharpPrediction(const CManagedAreasClient& areas)
		: m_Areas(areas)
	{
	}

	void Update(int startframe, bool validframe, int incoming_acknowledged, int outgoing_command) override;
	void PostNetworkDataReceived(int commands_acknowledged) override;

private:
	const CManagedAreasClient& m_Areas;

	// Set by PostNetworkDataReceived, spent by the next Update: compares run only after a
	// world update, since between packets the state cannot have moved.
	bool m_bWorldUpdateArrived = false;
};
