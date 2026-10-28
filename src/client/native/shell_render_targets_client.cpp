//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The render-target store: managed code declares each slot's texture, group and
// release order; the references are found on a slot's first use and dropped by
// the release function the material system calls on a mode change or device loss.

#include "materialsystem/imaterialsystem.h"
#include "materialsystem/itexture.h"
#include "render_target_store.h"
#include "shell_render_targets.h"
#include "sourcesharp_entrypoints.h"

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

// The two rendertexture.h names view_scene.h's inlines still call. An index
// past the four frame buffers is no texture (stock indexed past its array).
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
