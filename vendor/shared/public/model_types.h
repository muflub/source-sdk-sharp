//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#define STUDIO_RENDER 0x00000001

#define STUDIO_TWOPASS 0x00000008

// Not a studio flag, but used to flag when we want studio stats

// Not a studio flag, but used to flag model as using shadow depth material override
#define STUDIO_SSAODEPTHTEXTURE 0x08000000

// Not a studio flag, but used to flag model as using shadow depth material override
#define STUDIO_SHADOWDEPTHTEXTURE 0x40000000

// Not a studio flag, but used to flag model as a non-sorting brush model
#define STUDIO_TRANSPARENCY 0x80000000

enum modtype_t
{
	mod_bad = 0,
	mod_brush,
	mod_sprite,
	mod_studio
};
