//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's IEffects, a thunk ======================//
// SS_REASON b  an interface resolved by name at load
//
// plan_client_novalve.md §2 cluster 3: `IEffects` becomes a thunk. Stock's
// CEffectsClient (game/client/EffectsClient.cpp) decided, per call, whether to
// throw the effect away (SuppressTE), Smoke's grey, which muzzle-flash routine
// a type means and whether a ricochet makes a sound. Those decisions are C#'s
// now (SourceSharp.Client.Effects.ClientEffects, replaying the recorded stock
// calls, Fixtures/ieffects-stock.tsv). What stays here:
//
//   CEffectsThunk           the IEffects vtable every native caller reaches
//                           through g_pEffects (fx.cpp, soundenvelope.cpp,
//                           physics_shared.cpp, our shells), still exported as
//                           IEffects001 (K1), and still an IPredictionSystem
//                           node, which is the state CanPredict() reads; each
//                           effect method carries its arguments to managed
//                           (IManagedFxClient) and nothing else. Time() and
//                           IsServer() answer gpGlobals->curtime and false,
//                           as stock's did: no decision, no crossing.
//   IShimFxClient::Fx*      the FX_* routines the managed decision ends in,
//                           and the two answers SuppressTE reads.
//=============================================================================//

#include "cbase.h"
#include "IEffects.h"
#include "fx.h"
#include "c_recipientfilter.h"

#include "sourcesharp.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{

class CEffectsThunk final : public IEffects
{
public:
	void Beam( const Vector &Start, const Vector &End, int nModelIndex,
		int nHaloIndex, unsigned char frameStart, unsigned char frameRate,
		float flLife, unsigned char width, unsigned char endWidth, unsigned char fadeLength,
		unsigned char noise, unsigned char red, unsigned char green,
		unsigned char blue, unsigned char brightness, unsigned char speed ) override
	{
		// Stock's is Assert(0) and draws nothing; nothing calls it.
	}

	void Smoke( const Vector &origin, int modelIndex, float scale, float framerate ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->Smoke( origin.Base(), modelIndex, scale, framerate );
	}

	void Sparks( const Vector &position, int nMagnitude = 1, int nTrailLength = 1, const Vector *pvecDir = NULL ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->Sparks( position.Base(), nMagnitude, nTrailLength, pvecDir ? pvecDir->Base() : NULL );
	}

	void Dust( const Vector &pos, const Vector &dir, float size, float speed ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->Dust( pos.Base(), dir.Base(), size, speed );
	}

	void MuzzleFlash( const Vector &origin, const QAngle &angles, float fScale, int type ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->MuzzleFlash( origin.Base(), angles.Base(), fScale, type );
	}

	void MetalSparks( const Vector &position, const Vector &direction ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->MetalSparks( position.Base(), direction.Base() );
	}

	void EnergySplash( const Vector &position, const Vector &direction, bool bExplosive = false ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->EnergySplash( position.Base(), direction.Base(), bExplosive ? 1 : 0 );
	}

	void Ricochet( const Vector &position, const Vector &direction ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->Ricochet( position.Base(), direction.Base() );
	}

	float Time() override
	{
		return gpGlobals->curtime;
	}

	bool IsServer() override
	{
		return false;
	}

	void SuppressEffectsSounds( bool bSuppress ) override
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
			pFx->SuppressEffectsSounds( bSuppress ? 1 : 0 );
	}
};

CEffectsThunk s_EffectsThunk;

Vector V( const float *p )
{
	return Vector( p[0], p[1], p[2] );
}

} // namespace

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CEffectsThunk, IEffects, IEFFECTS_INTERFACE_VERSION, s_EffectsThunk );
IEffects *g_pEffects = &s_EffectsThunk;

// (IShimFxClient::FxDust left with lane c5-particles: FX_Dust is C#, DustPuff,
// and ClientEffects starts it without a crossing.)

//-----------------------------------------------------------------------------
// What stock's SuppressTE read: this IEffects' own IPredictionSystem state
// (SuppressEvents during a re-prediction), and a client PVS filter's count.
//-----------------------------------------------------------------------------
int32_t SourceSharp_FxCanPredict( void )
{
	return s_EffectsThunk.CanPredict() ? 1 : 0;
}

int32_t SourceSharp_FxRecipientCount( const float *pOriginXYZ )
{
	CPVSFilter filter( V( pOriginXYZ ) );
	return filter.GetRecipientCount();
}
