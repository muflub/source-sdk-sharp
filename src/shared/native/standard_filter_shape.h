//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// The shape clause out of StandardFilterRules — "is this the sort of thing this
// trace stops on at all" — as one rule, for both hierarchies.
//
// Stock's, verbatim, from util_shared.cpp:
//
//     if ( ( modelinfo->GetModelType( pModel ) != mod_brush ) ||
//          (solid != SOLID_BSP && solid != SOLID_VPHYSICS) )
//     {
//         if ( (fContentsMask & CONTENTS_MONSTER) == 0 )
//             return false;
//     }
//
// Read: a brush model that is solid as a brush is world-like and every trace
// stops on it; anything else — a studio prop, a ragdoll, a player — is an
// ENTITY, and only a mask that asked for entities gets one.
//
// WHY IT IS HERE. Until this file the clause was stated once, over a
// CBaseEntity, and EntityFromEntityHandle answers NULL for everything on the
// parallel hierarchy — so a managed entity took the static-prop arm above it
// and was waved through by every trace unconditionally, which is the opposite
// of what the rule says about a prop. Porting the clause meant a second copy of
// it, and a second copy of a rule that already exists is how the two come to
// disagree; the collision-group table is here for the same reason.
//
// What it cost, measured, is in the commit that added this: the blast's
// blocking rule retraces with CONTENTS_SOLID alone to ask whether the WORLD is
// also in the way, and skips the victim outright when it is. A managed prop was
// waved through that retrace, so the retrace stopped on the victim itself and
// every managed victim standing behind anything took ZERO damage rather than a
// reduced share.
//
// Engine-free and header-only, for the reason collision_group_names.h and
// collideable_center_math.h are: the caller resolves the three facts from
// whichever interfaces its hierarchy answers, and the RULE over them is
// something a test binary can reach with no engine in it.
//
// gtest: sourcesharp/test/standard_filter_shape_test.cpp.
//
//=============================================================================//
#pragma once

//-----------------------------------------------------------------------------
// bIsBrushModel          modelinfo->GetModelType( model ) == mod_brush
// bSolidIsBspOrVPhysics  solid == SOLID_BSP || solid == SOLID_VPHYSICS
// bMaskWantsEntities     fContentsMask & CONTENTS_MONSTER
//
// The mask flag arrives as a bool rather than as the mask, so this header needs
// no bspflags.h and the caller says which question it is asking in the one
// place that knows.
//-----------------------------------------------------------------------------
inline bool StandardFilterShapePasses( bool bIsBrushModel,
                                       bool bSolidIsBspOrVPhysics,
                                       bool bMaskWantsEntities )
{
	// World-like: a brush that is solid as a brush. Every trace stops on it,
	// whatever it asked for — this is the arm a door and a func_wall take.
	if ( bIsBrushModel && bSolidIsBspOrVPhysics )
		return true;

	// Everything else is an entity, and only a trace that asked for entities
	// gets one. BOTH halves of stock's disjunction land here: a studio prop
	// (not a brush model) and a brush entity that has been made non-solid or
	// SOLID_BBOX (a brush model that is not solid as one).
	return bMaskWantsEntities;
}
