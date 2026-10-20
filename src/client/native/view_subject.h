//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the three rules behind the view's facts =============//
//
// plan_entity.md step 7c. view_facts.h declares WHAT the view pass may ask;
// this holds the small amount of DECIDING those answers do, with no engine in
// it, so it can be checked by view_subject_test.cpp rather than by a
// photograph.
//
// Same shape and the same reason as thirdperson_camera.h (and
// view_effect_reach.h, until its punch walk went managed): shell_view_facts.cpp includes cbase.h and can never be
// linked into a gtest, so a rule left inside it is a rule nothing can assert.
// Each of the three below replaced a question a stock TU used to put to the
// local player entity, and each has an edge that is wrong in a way a running
// game does not obviously show.
//
//=============================================================================//
#ifndef SOURCESHARP_VIEW_SUBJECT_H
#define SOURCESHARP_VIEW_SUBJECT_H
#ifdef _WIN32
#pragma once
#endif

namespace ViewSubject
{

//-----------------------------------------------------------------------------
// The water level at which the SOUND LISTENER is under the surface.
//
// imovehelper.h's WL_Eyes, quoted rather than included: the enum runs
// WL_NotInWater 0, WL_Feet 1, WL_Waist 2, WL_Eyes 3, and it is the EYES that
// decide this — a player standing waist-deep hears the room, not the water.
// Restated here so this header stays engine-free; the value is asserted against
// the enum in shell_view_facts.cpp, which sees both.
//-----------------------------------------------------------------------------
const int WATER_LEVEL_EYES = 3;

//-----------------------------------------------------------------------------
// Is the sound listener underwater?
//
// Stock: `C_BasePlayer::AudioStateIsUnderwater`, whose live arm is
// `GetWaterLevel() >= WL_Eyes` (c_baseplayer.cpp:524).
//
// THE COMPARISON IS >= AND NOT >, and the difference is the whole effect: 3 is
// the top of the scale, so `> WL_Eyes` is never true and the underwater DSP
// would never switch on for anybody. That is precisely the failure this build
// already had by another route, so it is worth a rule with a test rather than a
// comparison written twice.
//-----------------------------------------------------------------------------
inline bool AudioIsUnderwater( int nWaterLevel )
{
	return nWaterLevel >= WATER_LEVEL_EYES;
}

//-----------------------------------------------------------------------------
// Are any of these EF_* bits set?
//
// Stock: `C_BaseEntity::IsEffectActive`, which is `( m_fEffects & nEffectMask )
// != 0`. ANY of the bits, not all of them — the one caller asks for a single
// bit (EF_DIMLIGHT) so the two readings agree there, and they part company the
// moment a caller passes a pair.
//-----------------------------------------------------------------------------
inline bool EffectActive( int nEffects, int nEffectMask )
{
	return ( nEffects & nEffectMask ) != 0;
}

//-----------------------------------------------------------------------------
// Should the frame's view come from the engine's VIEW ENTITY rather than from
// the viewer's own eyes?
//
// nViewEntity   - render->GetViewEntity(), the index the engine renders from
// nLocalPlayer  - engine->GetLocalPlayer(), this client's own player index
//
// TWO REFUSALS, and the second is not stock's.
//
//   The two being equal is the ordinary case — a player looking out of their
//   own head — and stock's test (`pPlayer->entindex() != viewentity`) is
//   exactly this one.
//
//   Index 0 or below is refused as well, and stock did not refuse it because
//   stock could not reach here without a player. Entity 0 is the world: a view
//   entity of 0 would resolve, would answer an origin of (0,0,0) and angles of
//   nothing, and would move the camera to the map's origin — a black frame, or
//   a frame inside a wall, which reads as "the renderer broke" and not as "the
//   view entity was wrong". A negative index is the engine's "no view entity".
//-----------------------------------------------------------------------------
inline bool ViewEntityOverridesTheEye( int nViewEntity, int nLocalPlayer )
{
	if ( nViewEntity <= 0 )
		return false;

	return nViewEntity != nLocalPlayer;
}

// THE VIEWMODEL PASS'S KEEP/REMOVE RULE (ViewModelIsUnwanted) stood here: it
// told a game entity's viewmodel from a tool's when the tool framework was
// running. Deleted with the tool framework: SFM is dead (user ruling
// 2026-10-19), so the pass draws the player's viewmodels or none.

} // namespace ViewSubject

#endif // SOURCESHARP_VIEW_SUBJECT_H
