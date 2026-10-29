//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// edict.h's CBaseEdict::StateChanged, with the engine's change accessor and change list
// handed in rather than found. The game DLL marks its own edicts through this; edict.h's
// inlines, which find both through free functions, are not used.

#pragma once

class IChangeInfoAccessor;
class CSharedEdictChangeInfo;
struct edict_t;

// Records nOffset in the edict's change info for this frame; a negative offset, or a
// list with no room left, marks the whole edict changed.
void SourceSharp_MarkEdictChanged( IChangeInfoAccessor *pAccessor, CSharedEdictChangeInfo &shared, edict_t *pEdict,
								   int nOffset );
