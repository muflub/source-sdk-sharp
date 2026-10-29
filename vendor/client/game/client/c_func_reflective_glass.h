//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

struct cplane_t;
class CViewSetup;

// Do we have reflective glass in view? If so, what's the reflection plane?
bool IsReflectiveGlassInView(const CViewSetup& view, cplane_t& plane);
