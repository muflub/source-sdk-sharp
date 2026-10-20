//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client shadow manager's native residue ==========//
//
// C7 cluster 12 S2 (docs/plans/c7_shadow_manager.md). The shadow manager is
// managed's (IManagedShadowsClient); shell_shadows_client.cpp keeps what the
// engine holds pointers to (the materials, the render targets, the
// flashlight's textures), the restore callback, kClientSystems[]'s
// CClientShadowMgr row, and the queue the shells write. These are its
// callers' calls.
//=============================================================================//
#ifndef SHELL_SHADOWS_CLIENT_H
#define SHELL_SHADOWS_CLIENT_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>

#include "bsptreedata.h"

class CViewSetup;
class CClientSystem;
class CBaseHandle;

// THE QUEUE. What an entity's shell tells the manager is queued, three int32
// an entry, and handed over with the manager's next call (PreRender's
// arguments, or Pending before any other call or when the queue is full), so
// an entity that moves every frame costs no crossing of its own. The kinds
// are IManagedShadowsClient's (docs/managed-interfaces.tsv).
enum ShellShadowPending_t
{
	SHELL_SHADOW_PENDING_DIRTY = 1,     // entity, 1 when its render-to-texture texture is dirty too
	SHELL_SHADOW_PENDING_REFRESH = 2,   // entity, shadow type | shadow flags << 8 (the block's, as managed wrote them)
	SHELL_SHADOW_PENDING_DESTROY = 3,   // entity
	SHELL_SHADOW_PENDING_WORDS = 3,
	SHELL_SHADOW_PENDING_MAX = 256,
};

// C_BaseEntity::InvalidatePhysicsRecursive's AddToDirtyShadowList( this ) and,
// when the pose moved, MarkRenderToTextureShadowDirty( GetShadowHandle() ).
// Collapsed per entity: a second mark ORs into the first.
void ShellShadows_MarkDirty( const CBaseHandle &hEntity, bool bTextureDirty );

// The entity's shadow as its block now says (C_BaseEntity::CreateShadow).
void ShellShadows_Refresh( const CBaseHandle &hEntity, int nShadowType, int nShadowFlags );

// The entity's shadow gone (C_BaseEntity::DestroyShadow). Drops the entity's
// queued entries before it: the renderable may be freed next.
void ShellShadows_Destroy( const CBaseHandle &hEntity );

// The plan's shadow ops (the view executor), each one call of the managed
// manager, with the render trace's `shadow.` line around it.
void ShellShadows_AdvanceFrame();
void ShellShadows_PreRender();
void ShellShadows_ComputeShadowTextures( int nLeafCount, const LeafIndex_t *pLeafList );
void ShellShadows_ComputeShadowDepthTextures( const CViewSetup &view );
void ShellShadows_UnlockAllShadowDepthTextures();
void ShellShadows_RenderShadowTexture( int w, int h );

// The queue's entries (what a test reads), and its hand-over count.
int ShellShadows_PendingCount();
const int32_t *ShellShadows_PendingEntries();

// shell_clientsystems.cpp's CClientShadowMgr row.
CClientSystem *ClientShadowMgrGameSystem();

#endif // SHELL_SHADOWS_CLIENT_H
