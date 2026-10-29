//========= Copyright Valve Corporation, All rights reserved. ============//
// Define local cl_dll hooks to the renderable textures in material system

#pragma once

ITexture *GetPowerOfTwoFrameBufferTexture();
ITexture *GetFullFrameFrameBufferTexture( int textureIndex );
