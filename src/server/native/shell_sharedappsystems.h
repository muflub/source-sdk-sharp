//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IServerDLLSharedAppSystems: the two DLLs the engine loads as app systems on the game's
// behalf (gameinterface.cpp). Necessarily native: the engine asks before DLLInit, when
// no managed runtime exists.

#pragma once

#include "eiface.h"

class CServerDLLSharedAppSystems final : public IServerDLLSharedAppSystems
{
public:
	int Count() override;
	char const *GetDllName( int idx ) override;
	char const *GetInterfaceName( int idx ) override;
};
