//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: An extra interface implemented by the material system
// implementation of vgui::ISurface

#pragma once

#include "vgui/ISurface.h"

// Forward declarations
class VMatrix;
class IMaterial;
struct InputEvent_t;

// An extra interface implemented by the material system implementation of vgui::ISurface
#define MAT_SYSTEM_SURFACE_INTERFACE_VERSION "MatSystemSurface008"

class IMatSystemSurface : public vgui::ISurface
{
public:
	// Hook needed to get input to work.
	// If the app drives the input (like the engine needs to do for VCR mode),
	// it can set bLetAppDriveInput to true and call HandleInputEvent for the input events.
	virtual void unused_AttachToWindow() = 0;

	// Tells the surface to ignore windows messages
	virtual void unused_EnableWindowsMessages() = 0;

	// Starts, ends 3D painting
	// NOTE: These methods should only be called from within the paint()
	// method of a panel.
	virtual void unused_Begin3DPaint() = 0;
	virtual void unused_End3DPaint() = 0;

	// NOTE: This also should only be called from within the paint()
	// method of a panel. Use it to disable clipping for the rendering
	// of this panel.
	virtual void DisableClipping( bool bDisable ) = 0;
	virtual void GetClippingRect(
		int &left, int &top, int &right, int &bottom,
		bool &
			bClippingDisabled ) = 0; // <<<<< NOTE: output flag is *disabled* state, not enabled, to match the rest of the interface
	virtual void SetClippingRect( int left, int top, int right, int bottom ) = 0;

	// Prevents vgui from changing the cursor
	virtual bool IsCursorLocked() const = 0;

	// Sets the mouse get + set callbacks
	virtual void unused_SetMouseCallbacks() = 0;

	// Installs a function to play sounds
	virtual void unused_InstallPlaySoundFunc() = 0;

	// Some drawing methods that cannot be accomplished under Win32
	virtual void unused_DrawColoredCircle() = 0;
	virtual void unused_DrawColoredText() = 0;

	// Draws text with current font at position and wordwrapped to the rect using color values specified
	virtual void unused_DrawColoredTextRect() = 0;
	virtual void unused_DrawTextHeight() = 0;

	// Returns the length of the text string in pixels
	virtual void unused_DrawTextLen() = 0;

	// Draws a panel in 3D space. Assumes view + projection are already set up
	// Also assumes the (x,y) coordinates of the panels are defined in 640xN coords
	// (N isn't necessary 480 because the panel may not be 4x3)
	// The width + height specified are the size of the panel in world coordinates
	virtual void unused_DrawPanelIn3DSpace() = 0;

	// Binds a material to a surface texture ID
	virtual void DrawSetTextureMaterial( int id, IMaterial *pMaterial ) = 0;

	// Handles an input event, returns true if the event should be filtered from the rest of the game
	virtual void unused_HandleInputEvent() = 0;

	virtual void unused_Set3DPaintTempRenderTarget() = 0;
	virtual void unused_Reset3DPaintTempRenderTarget() = 0;

	// Gets a material bound to a surface texture ID
	virtual void unused_DrawGetTextureMaterial() = 0;

	virtual void unused_GetFullscreenViewportAndRenderTarget() = 0;
	virtual void unused_SetFullscreenViewportAndRenderTarget() = 0;

	// get texture id for a texture
	virtual void unused_DrawGetTextureId() = 0;

	// begin and end skin composition painting
	virtual void unused_BeginSkinCompositionPainting() = 0;
	virtual void unused_EndSkinCompositionPainting() = 0;

	virtual void unused_DrawSetTextureRGBAEx2() = 0;
};
