//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The native side of the managed client shadow manager: what the engine holds pointers to
// and the queue the entity shells write.
#pragma once





class CViewSetup;
class CClientSystem;
class CBaseHandle;

// Shell updates are queued, three int32 an entry, and handed over with the manager's next
// call, so an entity that moves every frame costs no crossing of its own.
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

// The render plan's shadow ops, each one call of the managed manager.
void ShellShadows_AdvanceFrame();
void ShellShadows_PreRender();
void ShellShadows_ComputeShadowTextures( int nLeafCount, const LeafIndex_t *pLeafList );
void ShellShadows_ComputeShadowDepthTextures( const CViewSetup &view );
void ShellShadows_UnlockAllShadowDepthTextures();
void ShellShadows_RenderShadowTexture( int w, int h );

// The queue's entries (what a test reads), and its hand-over count.
int ShellShadows_PendingCount();
const int32_t *ShellShadows_PendingEntries();

CClientSystem *ClientShadowMgrGameSystem();
