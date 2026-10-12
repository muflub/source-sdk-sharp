//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON b  an interface resolved by name at load
//
// IServerGameTags -- THE SERVER-BROWSER TAG LIST.
//
// plan_gamedll_after.md Phase A9 step 2. One virtual, forwarded to the game
// rules; carried out of game/server/gameinterface.cpp:3072-3079 with the class
// declaration and the EXPOSE_ that were in gameinterface.h:238-244.
//
// THE POINT OF THIS FILE IS THAT THE EXPOSE_ IS IN A .cpp.
//
// An EXPOSE_SINGLE_INTERFACE in a HEADER gives every translation unit that
// includes it its own singleton and its own InterfaceReg. gameinterface.h was
// included by six objects in the compiled server set -- gameinterface.o,
// ServerNetworkProperty.o, shell_frame.o, shell_gamedll.o,
// shell_level.o and shell_server.o -- so "ServerGameTags001" was
// registered SIX times, which `nm` shows and `strings | grep -c` cannot.
//
// Six identical registrations are harmless and are not safe. Two InterfaceReg
// entries for one version string do not collide at link time: both link, and
// which one CreateInterface hands the engine is decided by the order of static
// initialisation across translation units. While all six are the same class
// answering the same way that is invisible; the moment one of them is a
// different implementation -- which is exactly what shell_gamedll.cpp's own
// comment warns Part 1 will try next -- the engine starts getting a silent,
// nondeterministic answer with no error anywhere.
//
// One .cpp, one registration.
//
//===========================================================================//

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
// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetServerGameTags() { return (IServerGameTags *)&s_ServerGameTags; }
const SourceSharpExposed kExposeServerGameTags = { INTERFACEVERSION_SERVERGAMETAGS, SourceSharp_GetServerGameTags };

//-----------------------------------------------------------------------------
//
//-----------------------------------------------------------------------------
void CServerGameTags::GetTaggedConVarList( KeyValues *pCvarTagList )
{
	if ( pCvarTagList && g_pGameRules )
	{
		g_pGameRules->GetTaggedConVarList( pCvarTagList );
	}
}
