//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Helper for the CHudElement class to add themselves to the list of hud elements

#pragma once

class CHudElement;

// Purpose: Used by DECLARE_HUDELEMENT macro to create a linked list of
//  instancing functions
class CHudElementHelper
{
public:
	// Static list of helpers
	static CHudElementHelper *m_sHelpers;
	// Create all the hud elements
	static void CreateAllElements();

public:
	// Construction
	CHudElementHelper( CHudElement *( *pfnCreate )(), int depth );

	// Accessors
	CHudElementHelper *GetNext();

private:
	// Next factory in list
	CHudElementHelper *m_pNext;
	// Creation function to use for this technology
	CHudElement *( *m_pfnCreate )();

	//Depth used to determine hud panel ordering
	int m_iDepth;
};

// This is the macro which implements creation of each hud element
// It creates a function which instances an object of the specified type
// It them hooks that function up to the helper list so that the CHud objects can create
//  the elements by name, with no header file dependency, etc.
#define DECLARE_HUDELEMENT( className )      \
	static CHudElement *Create_##className() \
	{                                        \
		return new className( #className );  \
	};                                       \
	static CHudElementHelper g_##className##_Helper( Create_##className, 50 );

// This macro can be used to get a pointer to a specific hud element

// Things that inherit from vgui::Panel, too, will have ambiguous new operators
//  so this should disambiguate them

// Alias for base hud element
