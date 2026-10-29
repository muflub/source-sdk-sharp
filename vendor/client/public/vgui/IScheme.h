//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui/VGUI.h>
#include "tier1/interface.h"

class Color;
class KeyValues;

namespace vgui
{

typedef uint32 HScheme;
typedef uint32 HTexture;

class IBorder;
class IImage;

// Purpose: Holds all panel rendering data
//			This functionality is all wrapped in the Panel::GetScheme*() functions
class IScheme : public IBaseInterface
{
public:
	// gets a string from the default settings section
	virtual void unused_GetResourceString() = 0;

	// returns a pointer to an existing border
	virtual void unused_GetBorder() = 0;

	// returns a pointer to an existing font
	virtual void unused_GetFont() = 0;

	// inverse font lookup
	virtual void unused_GetFontName() = 0;

	// colors
	virtual void unused_GetColor() = 0;

	// Get the number of borders
	virtual void unused_GetBorderCount() = 0;

	// Get the border at the given index
	virtual void unused_GetBorderAtIndex() = 0;

	// Get the number of fonts
	virtual void unused_GetFontCount() = 0;

	// Get the font at the given index
	virtual void unused_GetFontAtIndex() = 0;

	// Get color data
	virtual void unused_GetColorData() = 0;

	// returns a pointer to an existing border
	virtual void unused_GetBorder2() = 0;

	virtual void unused_GetSizingPanel() = 0;
};

class ISchemeManager : public IBaseInterface
{
public:
	// loads a scheme from a file
	// first scheme loaded becomes the default scheme, and all subsequent loaded scheme are derivitives of that
	virtual void unused_LoadSchemeFromFile() = 0;

	// reloads the scheme from the file - should only be used during development
	virtual void unused_ReloadSchemes() = 0;

	// reloads scheme fonts
	virtual void unused_ReloadFonts() = 0;

	// returns a handle to the default (first loaded) scheme
	virtual HScheme GetDefaultScheme() = 0;

	// returns a handle to the scheme identified by "tag"
	virtual HScheme GetScheme(const char* tag) = 0;

	// returns a pointer to an image
	virtual void unused_GetImage() = 0;
	virtual void unused_GetImageID() = 0;

	// This can only be called at certain times, like during paint()
	// It will assert-fail if you call it at the wrong time...

	// FIXME: This interface should go away!!! It's an icky back-door
	// If you're using this interface, try instead to cache off the information
	// in ApplySchemeSettings
	virtual void unused_GetIScheme() = 0;

	// unload all schemes
	virtual void Shutdown(bool full = true) = 0;

	// gets the proportional coordinates for doing screen-size independant panel layouts
	// use these for font, image and panel size scaling (they all use the pixel height of the display for scaling)
	virtual void unused_GetProportionalScaledValue() = 0;
	virtual void unused_GetProportionalNormalizedValue() = 0;

	// loads a scheme from a file
	// first scheme loaded becomes the default scheme, and all subsequent loaded scheme are derivitives of that
	virtual void unused_LoadSchemeFromFileEx() = 0;
	// gets the proportional coordinates for doing screen-size independant panel layouts
	// use these for font, image and panel size scaling (they all use the pixel height of the display for scaling)
	virtual void unused_GetProportionalScaledValueEx() = 0;
	virtual void unused_GetProportionalNormalizedValueEx() = 0;

	// misyl: Added this to scale much quicker and easier
	// lots of things were broken with scaling.
	// Not a real virtual method etc, just a helper to derp into existing code.

	// Returns true if image evicted, false otherwise
	virtual void unused_DeleteImage() = 0;

	virtual void unused_UpdateBorders() = 0;
};

#define VGUI_SCHEME_INTERFACE_VERSION "VGUI_Scheme010"

} // namespace vgui
