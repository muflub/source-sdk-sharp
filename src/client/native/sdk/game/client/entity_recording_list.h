//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the tool-recording list, out of c_baseentity.cpp ===//
//
// The list of entities the tool framework is recording this frame, and the one
// pointer everything reaches it through.
//
// WHY IT NEEDED A HEADER AT ALL. `IRecordingList`, `CRecordingList`,
// `g_RecordingList` and `recordinglist` were all declared inside
// game/client/c_baseentity.cpp, with no header anywhere in the tree -- the two
// C_BaseEntity members that use it and the one free function that walks it were
// in the same translation unit, so a declaration was never needed.
//
// Step 6 splits them. EntityTool_RecordEntities walks the list once per frame
// from cdll_client_int.cpp:2295, which SURVIVES step 6 and compiles perfectly
// clean armed, so that function had to be evacuated to
// c_baseentity_statics.cpp. C_BaseEntity::AddToRecordingList and
// RemoveFromRecordingList are members and die with the class. Two translation
// units, so the interface has to be nameable from both.
//
// WHAT IS AND IS NOT HERE. Only the abstract interface and the pointer -- the
// two things c_baseentity.cpp still needs to name. The concrete CRecordingList,
// its four method bodies and the g_RecordingList instance went to
// c_baseentity_statics.cpp with the function that outlives them, because
// nothing outside that file ever names the concrete type.
//
// NO ENTITY TYPE APPEARS IN ANY SIGNATURE HERE, so this header carries no
// `#ifndef SS_CBASEENTITY_ALIAS` block -- there is nothing to guard, rather
// than a guard that was forgotten. It traffics in ClientEntityHandle_t and
// IClientRenderable, neither of which is an entity class.
// alias_mechanics_test's NoForwardDeclarationSurvivesTheAlias sweeps every
// header in the tree and would catch it if that changed.
//
//=============================================================================//
#pragma once

#include "icliententitylist.h"

class IClientRenderable;

abstract_class IRecordingList
{
public:
	virtual ~IRecordingList() {};
	virtual void	AddToList( ClientEntityHandle_t add ) = 0;
	virtual void	RemoveFromList( ClientEntityHandle_t remove ) = 0;

	virtual int		Count() = 0;
	virtual IClientRenderable *Get( int index ) = 0;
};

// The one instance. Defined in c_baseentity_statics.cpp, next to the concrete
// class and to EntityTool_RecordEntities, which is the only reader of it that
// survives step 6.
extern IRecordingList *recordinglist;
