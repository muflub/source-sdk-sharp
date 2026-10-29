//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A model name is either a map submodel ("*23"), which is looked up in the string
// table, or a file, which must be precached first.
#pragma once

// An empty name is not a brush, so it fails on the precache arm, which can say what it looked for.
inline bool SourceSharp_IsBrushModelName(const char* pszModel)
{
	return pszModel != 0 && pszModel[0] == '*';
}
