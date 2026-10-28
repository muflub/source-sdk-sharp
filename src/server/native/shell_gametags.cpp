//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

// IServerGameTags. Tags are declared on their convars in C#, which fills sv_tags.

#include "igamerules.h"
#include "eiface.h"
#include "shell_factory.h"

class CServerGameTags : public IServerGameTags
{
public:
	virtual void GetTaggedConVarList( KeyValues *pCvarTagList );
};

namespace
{
CServerGameTags s_ServerGameTags;
}

static void *SourceSharp_GetServerGameTags()
{
	return (IServerGameTags *)&s_ServerGameTags;
}

const SourceSharpExposed kExposeServerGameTags = { INTERFACEVERSION_SERVERGAMETAGS, SourceSharp_GetServerGameTags };

void CServerGameTags::GetTaggedConVarList( KeyValues *pCvarTagList ) {}
