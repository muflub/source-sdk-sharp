//====== Copyright © 1996-2008, Valve Corporation, All rights reserved. =======
// Purpose: interface to steam managing game server/client match making

#pragma once
#define ISTEAMMATCHMAKING

#include "steam_api_common.h"
#include "matchmakingtypes.h"

typedef void* HServerListRequest;

// Purpose: Callback interface for receiving responses after a server list refresh
// or an individual server update.
// Since you get these callbacks after requesting full list refreshes you will
// usually implement this interface inside an object like CServerBrowser.  If that
// object is getting destructed you should use ISteamMatchMakingServers()->CancelQuery()
// to cancel any in-progress queries so you don't get a callback into the destructed
// object and crash.
class ISteamMatchmakingServerListResponse
{
public:
	// Server has responded ok with updated data
	virtual void ServerResponded(HServerListRequest hRequest, int iServer) = 0;

	// Server has failed to respond
	virtual void ServerFailedToRespond(HServerListRequest hRequest, int iServer) = 0;

	// A list refresh you had initiated is now 100% completed
	virtual void RefreshComplete(HServerListRequest hRequest, EMatchMakingServerResponse response) = 0;
};
