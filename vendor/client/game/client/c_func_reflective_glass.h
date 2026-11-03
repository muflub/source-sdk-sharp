//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

struct cplane_t;
class CViewSetup;
class CStandaloneClients;

// Do we have reflective glass in view? If so, what's the reflection plane?
// SourceSharp: the shells are handed in, not found through a global.
bool IsReflectiveGlassInView(CStandaloneClients& clients, const CViewSetup& view, cplane_t& plane);
