//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — telling a map's own model from a file =============//
//
// A model name is one of two completely different things, and what you must do
// to turn it into a model index depends on which:
//
//   "*23"                          the twenty-third submodel the map compiler
//                                  carved out of the BSP. It came in with the
//                                  level and is already in the string table, so
//                                  it is LOOKED UP. Asking to precache one is
//                                  asking to load a file that does not exist.
//
//   "models/props_junk/..."        a file on disk, which has to be PRECACHED
//                                  before anything can be given its index.
//
// Both hierarchies need the rule and both had their own copy of it — the
// parallel one in CStandaloneEntity::SetModelName, the SDK one in
// EntityInitPhysics, written second and by reading the first. One comparison
// duplicated is one comparison that can drift, and the failure if it does is
// not an error anywhere: a brush sent down the precache arm gets -1 and reports
// itself as a missing model, and a studio model sent down the lookup arm gets
// -1 and reports the same thing. Neither says which arm it took.
//
// Header-only and engine-free, so the rule is checked by gtest rather than by
// standing in front of a door
// (src/sourcesharp/test/model_name_test.cpp).
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_MODEL_NAME_H
#define SOURCESHARP_SHELL_MODEL_NAME_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Whether this name is a submodel of the map rather than a file.
//
// An empty name is not one. It is not a file either, but "not a brush" is the
// answer this function owes: the precache arm reports a name it cannot load,
// and the lookup arm would report the same thing about a name that was never
// going to be in the string table, so the reportable failure belongs on the
// side that can say what it looked for.
//-----------------------------------------------------------------------------
inline bool SourceSharp_IsBrushModelName( const char *pszModel )
{
	return pszModel != 0 && pszModel[0] == '*';
}

#endif // SOURCESHARP_SHELL_MODEL_NAME_H
