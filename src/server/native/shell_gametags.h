//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IServerGameTags. Tags are declared on their convars in C#, which fills sv_tags.

#pragma once

#include "eiface.h"

class CServerGameTags final : public IServerGameTags
{
public:
	void GetTaggedConVarList(KeyValues* pCvarTagList) override;
};
