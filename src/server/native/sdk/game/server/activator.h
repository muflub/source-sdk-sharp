//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ======================================================//
//
// Activator_t, at the path the SDK's includers name. The struct is in
// ss_activator.h, which needs only public/ headers so that server.so's own TUs
// can reach it; this keeps ehandle.h in scope for the SDK TUs that used to get
// it from here.
//
//=============================================================================//
#ifndef ACTIVATOR_H
#define ACTIVATOR_H
#ifdef _WIN32
#pragma once
#endif

#include "ehandle.h"
#include "ss_activator.h"

#endif // ACTIVATOR_H
