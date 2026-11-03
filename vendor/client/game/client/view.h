//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class VMatrix;
class Vector;
class QAngle;
class VPlane;

// near and far Z it uses to render the world.
#define VIEW_NEARZ 7
//#define VIEW_FARZ	28400

// There's a difference between the 'current view' and the 'main view'
// The 'main view' is where the player is sitting. Current view is just
// what's currently being rendered, which, owing to monitors or water,
// could be just about anywhere.

// Returns true of the sphere is outside the frustum defined by pPlanes.
// (planes point inwards).
bool R_CullSphere(const VPlane* pPlanes, int nPlanes, const Vector* pCenter, float radius);
