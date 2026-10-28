//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "mathlib/mathlib.h"

struct CompressedLightCube
{
	DECLARE_BYTESWAP_DATADESC();
	ColorRGBExp32 m_Color[6];
};
