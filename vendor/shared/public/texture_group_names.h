//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//===========================================================================//

#ifndef TEXTURE_GROUP_NAMES_H
#define TEXTURE_GROUP_NAMES_H
#ifdef _WIN32
#pragma once
#endif

// These are given to FindMaterial to reference the texture groups that show up on the 


#define TEXTURE_GROUP_MODEL							"Model textures"

#define TEXTURE_GROUP_PARTICLE						"Particle textures"
#define TEXTURE_GROUP_DECAL							"Decal textures"

#define TEXTURE_GROUP_CLIENT_EFFECTS				"ClientEffect textures"
#define TEXTURE_GROUP_OTHER							"Other textures"
#define TEXTURE_GROUP_PRECACHED						"Precached"				// TODO: assign texture groups to the precached materials

#define TEXTURE_GROUP_RENDER_TARGET					"RenderTargets"

	// Textures that weren't assigned a texture group.
//#define TEXTURE_GROUP_STATIC_VERTEX_BUFFER		"Static Vertex"















#endif // TEXTURE_GROUP_NAMES_H
