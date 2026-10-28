//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- the render targets client.so holds ================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C7 (§3.3: the executor holds the render targets and the
// material system's release callback). rendertexture.cpp's getters are
// managed's decisions now -- RenderTargetCatalog names each slot's texture and
// group and says which references the release drops, in what order -- and
// this file is the store that carries them out (render_target_store.h): the
// references, found on a slot's first use, and the release function the
// material system calls on a mode change or a device loss, through the
// pointer it is handed.
//
//   SourceSharp_RenderTargetDeclare   IShimViewClient: one slot's row, from
//                                     RenderTargetCatalog.DeclareTo at the
//                                     managed client's start (and again, the
//                                     same rows, after a reload).
//   ShellRenderTarget                 the texture in a slot, for the plan's
//                                     executor and the view scene's depth copy.
//   GetPowerOfTwoFrameBufferTexture,  rendertexture.h's two names view_scene.h's
//   GetFullFrameFrameBufferTexture    inlines (fork) still call: their slots.
//
// rendertexture.cpp left client.so with this store (docs/client_evictions.md).
//
// No decision is made here: a slot nobody declared answers no texture.
//=============================================================================//

#include "materialsystem/imaterialsystem.h"
#include "materialsystem/itexture.h"
#include "render_target_store.h"
#include "shell_render_targets.h"
#include "sourcesharp_entrypoints.h"

// memdbgon must be the last include file in a .cpp file!!!

namespace
{

CRenderTargetStore g_RenderTargets;

// The material system's release function: the declared references, dropped.
void ReleaseRenderTargetsThunk()
{
	g_RenderTargets.Release();
}

} // namespace

int32_t SourceSharp_RenderTargetDeclare( int32_t nSlot, const char *pszName, const char *pszGroup, int32_t bComplain,
                                         int32_t nCreationFlags, int32_t nReleaseOrder )
{
	g_RenderTargets.SetReleaseFunc( &ReleaseRenderTargetsThunk );
	return g_RenderTargets.Declare( nSlot, pszName, pszGroup, bComplain, nCreationFlags, nReleaseOrder ) ? 1 : 0;
}

ITexture *ShellRenderTarget( int nSlot )
{
	return g_RenderTargets.Get( nSlot );
}

//-----------------------------------------------------------------------------
// rendertexture.h's two names something in client.so still imports: the
// inlines in view_scene.h (fork), UpdateRefractTexture and
// UpdateScreenEffectTexture, which the plan's executor, the refracting
// particles and the screen-space effects call. Each is its slot; an index
// past the four frame buffers is no texture (stock indexed past its array).
//-----------------------------------------------------------------------------
ITexture *GetPowerOfTwoFrameBufferTexture( void )
{
	return g_RenderTargets.Get( SS_RT_PowerOfTwoFrameBuffer );
}

ITexture *GetFullFrameFrameBufferTexture( int textureIndex )
{
	if ( textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES )
		return NULL;
	return g_RenderTargets.Get( SS_RT_FullFrameFrameBuffer0 + textureIndex );
}
