//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON b  an interface resolved by name at load
// This class allows the game dll to control what functions 3rd party plugins can 
//  call on clients.

#include "eiface.h"
#include "shell_factory.h"

//-----------------------------------------------------------------------------
// Purpose: An implementation 
//-----------------------------------------------------------------------------
class CPluginHelpersCheck : public IPluginHelpersCheck
{
public:
	virtual bool CreateMessage( const char *plugin, edict_t *pEntity, DIALOG_TYPE type, KeyValues *data );
};

CPluginHelpersCheck s_PluginCheck;
// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetPluginHelpersCheck() { return (IPluginHelpersCheck *)&s_PluginCheck; }
const SourceSharpExposed kExposePluginHelpersCheck = { INTERFACEVERSION_PLUGINHELPERSCHECK, SourceSharp_GetPluginHelpersCheck };

bool CPluginHelpersCheck::CreateMessage( const char *plugin, edict_t *pEntity, DIALOG_TYPE type, KeyValues *data )
{
	// return false here to disallow a plugin from running this command on this client
	return true;
}

