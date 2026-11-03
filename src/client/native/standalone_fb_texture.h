//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The frame-buffer-sampler decision of a model entity (the cloak pass's
// refraction source), which the translucent passes refresh the framebuffer
// texture for before they draw it.
#pragma once

#include "engine/ivmodelinfo.h"

// C_BaseAnimating::UsesPowerOfTwoFrameBufferTexture
// (c_baseanimating.cpp:829-832): the model's own materials decide, through the
// engine's modelinfo. A shell with no model, or an interface not yet
// connected, does not sample the frame buffer.
inline bool SourceSharp_ShellUsesFBTexture(IVModelInfoClient* pModelInfo, const model_t* pModel, int nSkin, int nBody,
										   void* pClientRenderable)
{
	return pModel != NULL && pModelInfo != NULL &&
		pModelInfo->IsUsingFBTexture(pModel, nSkin, nBody, pClientRenderable);
}
