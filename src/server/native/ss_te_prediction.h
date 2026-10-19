//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the temp entity channel's suppress host
//
// plan_server_novalve.md P5 (lane SP). What server.so still wanted of
// game/shared/ipredictionsystem.h's IPredictionSystem: the suppress host of the
// temp entity channel, which takedamageinfo_library.cpp's ApplyMultiDamage
// clears across TakeDamage and puts back. Nothing else in the server reads one,
// and nothing walks the prediction-system list -- nm over every server object
// finds no SuppressEvents, SuppressHostEvents or CDisablePredictionFiltering --
// so this is the host alone, with no list, in namespace ss.
//
// Stock's GetSuppressHost answered NULL while filtering was disabled (a
// CDisablePredictionFiltering in scope) and the stored host otherwise. Nothing
// on the server can disable filtering (no CDisablePredictionFiltering exists in
// server.so), so the stored host is always the answer; ss_te_prediction_test
// pins that, and that a fresh object answers NULL.
//=============================================================================//

#ifndef SOURCESHARP_SS_TE_PREDICTION_H
#define SOURCESHARP_SS_TE_PREDICTION_H
#ifdef _WIN32
#pragma once
#endif

// igameentity_fwd.h's two arms, spelled here: ss_te_prediction_test compiles
// this header with no include path at all.
#if defined( GAME_DLL ) && defined( SS_STOCK_PUBLIC_HEADERS )
class CBaseEntity;
using IGameEntity = CBaseEntity;
#else
class IGameEntity;
#endif

namespace ss
{

class TePrediction
{
public:
	constexpr TePrediction() : m_pSuppressHost( nullptr ) {}

	// The entity whose own client already predicted the effect, or NULL.
	const IGameEntity *GetSuppressHost() const { return m_pSuppressHost; }
	void SetSuppressHost( IGameEntity *pHost ) { m_pSuppressHost = pHost; }

private:
	IGameEntity *m_pSuppressHost;
};

} // namespace ss

#endif // SOURCESHARP_SS_TE_PREDICTION_H
