//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Whether setting a standalone entity's model may also set its collision size.
// For nearly everything on the parallel hierarchy it must. A brush entity
// carries its own size and is not centred on its origin -- a door is a slab
// somewhere in the map whose origin is usually the world's -- so taking the
// bounds from the model is what makes it collide where the mapper drew it.
// CStandaloneEntity::SetModel has always done that.
// For a PLAYER it must not, and the reason is that a player has two boxes that
// are not the same box. The movement hull (VEC_HULL_MIN..VEC_HULL_MAX, feet at
// the origin) is what every question about standing is answered against: the
// ground trace, the rider test, the blocker test, the step. The model's bounds
// are an art fact -- where a mesh happens to sit around its own origin, which
// for a citizen is about six units BELOW the feet and three units narrower
// each way.
// CStandalonePlayer::Spawn sets the hull and then calls the managed
// OnPlayerSpawn, and SsPlayer.OnSpawn's first line is SetModel(PlayerModel).
// So the hull was installed and then immediately replaced, on every spawn,
// and every measurement of a player since has been of the citizen mesh.
// Measured on ss_sandbox at 821aea34, standing on belts_lift:
//     cand 'ss_player' originZ=26 mins=-13,-13,-5.93788 maxs=13,13,72
// asks whether the feet are within Riders.Sink of the mover's top face; six
// units of phantom shin put them outside it, so a lift's own passenger failed
// the standing-on test every tick of the rise and the brush went through them.
// The DESCENDING case never noticed, being gravity's work rather than the
// carry's.
// The rule: a player's collision bounds are its movement hull, and a model
// never gets a say. Everything else adopts the model's bounds when the model
// has any -- an empty box (mins == maxs) is what modelinfo answers for a model
// that carries no size, and adopting that would collapse the entity.
// Header-only and engine-free so the rule is checked by gtest rather than by
// standing on a lift (src/sourcesharp/test/standalone_model_bounds_test.cpp).

#ifndef SOURCESHARP_STANDALONE_MODEL_BOUNDS_H
#define SOURCESHARP_STANDALONE_MODEL_BOUNDS_H

inline bool StandaloneModel_AdoptsBounds( bool bIsPlayer, bool bModelHasBounds )
{
	if ( bIsPlayer )
		return false;

	return bModelHasBounds;
}

#endif // SOURCESHARP_STANDALONE_MODEL_BOUNDS_H
