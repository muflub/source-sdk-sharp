//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The engine's net channel for managed: the messages it sends the server and the statistics
// it reads. A member of the client DLL object.

#pragma once

#include <stdint.h>

#include "ishim_areas_client.h"

class CClientEngineInterfaces;
class INetChannelInfo;

class CNetChannelClient final : public IShimNetClient
{
public:
	explicit CNetChannelClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	CNetChannelClient(const CNetChannelClient&) = delete;
	CNetChannelClient& operator=(const CNetChannelClient&) = delete;

	int32_t SendServerMessage(const char* pKey, const uint8_t* pData, int32_t nLen) override;
	int32_t ClientNetFlow(int32_t nFlow, float* pOut, int32_t nOut) override;
	int32_t ClientNetChannel(float* pOut, int32_t nOut) override;
	int32_t ClientNetPacket(int32_t nFlow, int32_t nFrame, float* pOut, int32_t nOut) override;

private:
	INetChannelInfo* Channel();

	const CClientEngineInterfaces& m_Engines;
};
