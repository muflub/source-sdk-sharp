//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

const float PIXELVIS_DEFAULT_PROXY_SIZE = 2.0f;
const float PIXELVIS_DEFAULT_FADE_TIME = 0.0625f;

typedef int pixelvis_handle_t;

struct pixelvis_queryparams_t
{
	pixelvis_queryparams_t() { bSetup = false; }

	void Init( const Vector &origin, float proxySizeIn = PIXELVIS_DEFAULT_PROXY_SIZE, float proxyAspectIn = 1.0f,
			   float fadeTimeIn = PIXELVIS_DEFAULT_FADE_TIME )
	{
		position = origin;
		proxySize = proxySizeIn;
		proxyAspect = proxyAspectIn;
		fadeTime = fadeTimeIn;
		bSetup = true;
		bSizeInScreenspace = false;
	}

	Vector position;
	float proxySize;
	float proxyAspect;
	float fadeTime;
	bool bSetup;
	bool bSizeInScreenspace;
};
