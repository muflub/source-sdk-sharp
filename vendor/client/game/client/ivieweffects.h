//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

class Vector;
class QAngle;
class bf_read;

// Purpose: Apply effects to view origin/angles, etc.  Screen fade and shake
abstract_class IViewEffects
{
public:
	// Initialize subsystem
	virtual void	Init( void ) = 0;
	// Initialize after each level change
	virtual void	LevelInit( void ) = 0;
	// Called each frame to determine the current view fade parameters ( color and alpha )
	virtual void unused_GetFadeParams() = 0;
	// Apply directscreen shake
	virtual void unused_Shake() = 0;
	// Apply direct screen fade
	virtual void unused_Fade() = 0;
	// Clear all permanent fades in our fade list
	virtual void unused_ClearPermanentFades() = 0;
	// Clear all fades in our fade list
	virtual void unused_ClearAllFades() = 0;
	// Compute screen shake values for this frame
	virtual void unused_CalcShake() = 0;
	// Apply those values to the passed in vector(s).
	virtual void unused_ApplyShake() = 0;
	// Save / Restore
	virtual void	Save( ISave *pSave ) = 0;
	virtual void	Restore( IRestore *pRestore, bool ) = 0;
};
