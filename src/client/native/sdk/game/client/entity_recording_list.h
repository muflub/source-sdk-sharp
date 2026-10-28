//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The list of entities the tool framework is recording this frame.
#pragma once

#include "icliententitylist.h"

class IClientRenderable;

abstract_class IRecordingList
{
public:
	virtual ~IRecordingList(){};
	virtual void AddToList( ClientEntityHandle_t add ) = 0;
	virtual void RemoveFromList( ClientEntityHandle_t remove ) = 0;

	virtual int Count() = 0;
	virtual IClientRenderable *Get( int index ) = 0;
};

// The one instance, defined in c_baseentity_statics.cpp.
extern IRecordingList *recordinglist;
