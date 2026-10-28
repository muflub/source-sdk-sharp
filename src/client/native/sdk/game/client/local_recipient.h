//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// WHICH SLOT a client-side recipient filter holds, decided without a
// C_BasePlayer.
//
// A C_RecipientFilter has exactly one possible recipient: the viewer. Every add
// on it went through C_BasePlayer::GetLocalPlayer() — to test it, to read an
// index off it, or both — and under SourceSharp the viewer is a
// C_StandalonePlayer, so that answers NULL and EVERY CLIENT FILTER WAS EMPTY.
//
// An empty client filter is not a quiet degrade. Both client temp-entity
// dispatchers ask it first:
//
//     bool SuppressTE( C_RecipientFilter &filter )     // c_te.cpp, EffectsClient.cpp
//     {
//         if ( !filter.GetRecipientCount() ) return true;   // suppress it
//
// so every predicted temp entity the viewer's own actions produce — muzzle
// flashes, tracers, impacts, decals — was thrown away before the effect
// function was called. That is the exact twin of the SERVER outbreak that made
// explosions invisible while their damage worked, and it hid the same way: the
// server sends its own copy a round trip later, so things appear, late, and
// nothing anywhere says a filter came out empty.
//
// The engine already answers the only question these filters have. Its
// GetLocalPlayer() is an INDEX, stated before any entity has decoded, and the
// filter has only ever STORED indices — so the C_BasePlayer in the middle was
// never carrying information, only a type.
//
// Engine-free on purpose: the rule is two comparisons and is tested without a
// running client (sourcesharp/test/local_recipient_test.cpp).
//
//=============================================================================//
#ifndef LOCAL_RECIPIENT_H
#define LOCAL_RECIPIENT_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Should a client-side filter hold this slot?
//
// nLocalSlot is the engine's own GetLocalPlayer(), which is negative or zero
// when there is no viewer yet — a state a filter must hold nobody in, and the
// state every filter built before the first entity decodes is in.
//
// Stock's other arm, the re-prediction suppression (m_bUsingPredictionRules
// and IPredictionSystem::CanPredict), has no subject here: nothing on the
// client link edge calls UsePredictionRules, so it left with that member
// (2026-10-25); the managed prediction decides what a replay emits.
//-----------------------------------------------------------------------------
inline bool LocalRecipient_ShouldHold( int nSlot, int nLocalSlot )
{
	if ( nSlot < 1 || nLocalSlot < 1 )
		return false;

	return nSlot == nLocalSlot;
}

#endif // LOCAL_RECIPIENT_H
