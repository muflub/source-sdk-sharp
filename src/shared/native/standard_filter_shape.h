//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The shape clause of stock's StandardFilterRules (util_shared.cpp), over plain
// facts so both hierarchies share it: a brush model solid as a brush stops every
// trace; anything else is an entity, hit only when the mask asks for entities.
#pragma once

// bIsBrushModel          modelinfo->GetModelType( model ) == mod_brush
// bSolidIsBspOrVPhysics  solid == SOLID_BSP || solid == SOLID_VPHYSICS
// bMaskWantsEntities     fContentsMask & CONTENTS_MONSTER
inline bool StandardFilterShapePasses( bool bIsBrushModel, bool bSolidIsBspOrVPhysics, bool bMaskWantsEntities )
{
	// World-like: every trace stops on it.
	if ( bIsBrushModel && bSolidIsBspOrVPhysics )
		return true;

	// Studio props, and brush entities that are not solid as brushes.
	return bMaskWantsEntities;
}
