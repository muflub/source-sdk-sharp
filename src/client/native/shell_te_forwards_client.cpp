//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's native temp-entity forwards ===========//
// SS_REASON a  a vtable the engine calls through
//
// ONE FILE, AND IT REPLACES TEN. Nine were
// shell_te_fwd_{c1,c4,ca,cd,ce,cn,cp,cx,pool2}.cpp — 2,469 lines carrying 52
// `TE_*` entry points — and the tenth is stock `game/client/c_te.cpp`, which is
// out of client_csharp.vpc with this commit. What is here is the part of the
// ten that anything in this build can reach.
//
// WHAT A CLIENT-SIDE TE_* WAS FOR, because it is not the receive path and that
// is the usual confusion. A temp entity ARRIVING from the server is a datatable
// — C_BaseTempEntity and the managed RecvTables handle that, and nothing in
// this file is on that path. These entry points are the client MAKING an effect
// for ITSELF: `te->Smoke(...)` from client code, straight into
// CTempEntsSystem's virtual, straight into TE_Smoke. Porting a temp entity
// evicts its c_te_*.cpp (the client resolves a data table BY NAME and cannot
// hold two of one), which takes that TE_* with it, so the entry point was
// re-made over shell_tempents_client.cpp's generic writer.
//
// WHY ONLY THREE OVERRIDES DO ANYTHING, and this is the measurement the whole
// deletion rests on. Every one of the 52 was imported by exactly one object,
// `c_te.o`, and by nothing else — `tools/shim-census who-imports` over the
// debug link line at c5eab9deb, 271 client objects on the graph, 271 on disk, 0
// ghosts. c_te.o in turn exported exactly one strong symbol, `te`, imported by
// two objects. So the whole cluster hung off virtual calls through `te`, which
// emit no symbol at all and which a census therefore cannot see (memory:
// consumer-count-is-not-the-flip-distance). Reading every `te->` on the link
// line instead, and opening each one to tell code from prose, there are six and
// three of them are comments:
//
//   game/client/cdll_util.cpp:301          te->Bubbles              CODE
//   game/client/cdll_util.cpp:378          te->Smoke                CODE
//   game/client/c_te_effect_dispatch.cpp:223 te->DispatchEffect     CODE
//   game/client/c_te_legacytempents.cpp:1373 te->PhysicsProp        comment
//   sourcesharp/game/client/shell_steamjet_sim.cpp:601 te->Fizz     comment
//   sourcesharp/game/client/shell_te_fwd_c4.cpp:74 te->KillPlayerAttachments comment
//
// THREE, BUT NOT THE THREE THE ANALYSIS LANE NAMED. Lane C2 read
// `te->Decal/Bubbles/Smoke` and cited `baseentity_shared.cpp` for the Decal.
// baseentity_shared.cpp is not on the CLIENT link line at all — it is a server
// object — so there is no `te->Decal` on this side and TE_Decal is dead here.
// The count agreed and the set did not, which is the shape that would have kept
// a dead body and dropped a live one.
//
// THE OTHER 38 OVERRIDES ARE EMPTY, NOT DELETED. ITempEntsSystem declares 41
// pure virtuals; a class that does not override one does not compile. An empty
// override is therefore the smallest thing this build can say, and it says the
// true thing: nothing calls it. It is not the same as dropping the virtual,
// which would change the vtable layout under any caller that came back.
//
// THE SUPPRESSION IS CARRIED, not dropped with the class. CTempEntsSystem::
// SuppressTE is the prediction filter — when a player predicts an entity
// locally the server must not also send that player its effects — and it is
// reproduced below verbatim so that a rewiring which quietly stopped culling
// would be a behaviour change with a cause rather than one with none.
//
// NOTHING HERE IS REGISTERED BY NAME. There is no DECLARE_CLIENT_EFFECT,
// DECLARE_TEMPENT or REGISTER_EFFECT in this file or in the nine it replaces,
// and stock c_te.cpp exposed no interface either — `te` was a plain global with
// no EXPOSE_SINGLE_INTERFACE and no CreateInterface behind it. That matters
// because a name-resolved entry point is reachable with zero importers, and
// checking for one is the third leg of the proof the census cannot supply.
//
//=============================================================================//

#include "cbase.h"
#include "irecipientfilter.h"
#include "c_recipientfilter.h"
#include "tempent_forwards.h"
#include "itempents.h"
#include "effect_dispatch_data.h"
#include "tier1/KeyValues.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// The generic writer, from shell_tempents_client.cpp. No per-TE knowledge on
// either side of this boundary: a handle, some named fields, and a play.
bool SourceSharpTe_Forward( int nId, const float *pFloats, int nFloats,
                            const int *pInts, int nInts );

// te_effect_dispatch.cpp's, in stock game/client/c_te_effect_dispatch.cpp,
// which stays: it owns the RecvTable a dispatched effect arrives on.
void TE_DispatchEffect( IRecipientFilter& filter, float delay,
	const Vector &pos, const char *pName, const CEffectData &data );

//-----------------------------------------------------------------------------
// c_te_bubbles.cpp's TE_Bubbles, over the managed DT_TEBubbles.
//
// (moved verbatim from shell_te_fwd_pool2.cpp; the per-body "MARSHALLING, AND
// NOTHING ELSE" comment it carried is stated once, above, instead.)
//-----------------------------------------------------------------------------
void TE_Bubbles( IRecipientFilter& filter, float delay,
	const Vector* mins, const Vector* maxs, float height, int modelindex, int count, float speed )
{
	const float f[] =
	{
		mins ? mins->x : 0.0f,
		mins ? mins->y : 0.0f,
		mins ? mins->z : 0.0f,
		maxs ? maxs->x : 0.0f,
		maxs ? maxs->y : 0.0f,
		maxs ? maxs->z : 0.0f,
		height,
		speed,
	};

	const int i[] =
	{
		modelindex,
		count,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_BUBBLES, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// c_te_smoke.cpp's TE_Smoke, over the managed DT_TESmoke.
//
// THE TIMES TEN IS NOT DONE HERE, and that is the whole reason this goes
// through the play path rather than calling the backend. Stock's client-side
// TE_Smoke multiplies the scale by ten before drawing, under Valve's own "the
// number passed down is 10 times smaller" — and so does its PostDataUpdate,
// separately. The blob carries the UNMULTIPLIED value, exactly as the wire
// does, and ClientTeSmoke.Play does the multiply once for both paths.
//
// (moved verbatim from shell_te_fwd_ce.cpp.)
//-----------------------------------------------------------------------------
void TE_Smoke( IRecipientFilter& filter, float delay,
	const Vector* pos, int modelindex, float scale, int framerate )
{
	const float f[] =
	{
		pos ? pos->x : 0.0f,
		pos ? pos->y : 0.0f,
		pos ? pos->z : 0.0f,
		scale,
	};

	const int i[] =
	{
		modelindex,
		framerate,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SMOKE, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//=============================================================================//
// The client's ITempEntsSystem, which was c_te.cpp's C_TempEntsSystem.
//
// Three overrides forward; the rest are empty because nothing on this build's
// link line calls them. The class still derives from ITempEntsSystem, and so
// from IPredictionSystem, so `te` is still on the list SuppressHostEvents walks
// and still carries a suppress host — that is what SuppressTE below reads.
//=============================================================================//
class C_TempEntsSystem : public ITempEntsSystem
{
private:
	//-----------------------------------------------------------------------------
	// Purpose: Returning true means don't even call TE func
	// (verbatim from game/client/c_te.cpp)
	//-----------------------------------------------------------------------------
	bool SuppressTE( IRecipientFilter& filter )
	{
		if ( !CanPredict() )
			return true;

		C_RecipientFilter& _filter = (( C_RecipientFilter & )filter);

		if ( !_filter.GetRecipientCount() )
		{
			// Suppress it
			return true;
		}

		// There's at least one recipient
		return false;
	}

public:
	//-------------------------------------------------------------------------
	// THE THREE WITH A CALLER.
	//-------------------------------------------------------------------------
	virtual void Bubbles( IRecipientFilter& filter, float delay,
		const Vector* mins, const Vector* maxs, float height, int modelindex, int count, float speed )
	{
		if ( !SuppressTE( filter ) )
		{
			TE_Bubbles( filter, delay, mins, maxs, height, modelindex, count, speed );
		}
	}

	virtual void Smoke( IRecipientFilter& filter, float delay,
		const Vector* pos, int modelindex, float scale, int framerate )
	{
		if ( !SuppressTE( filter ) )
		{
			TE_Smoke( filter, delay,
				pos, modelindex, scale, framerate );
		}
	}

	virtual void DispatchEffect( IRecipientFilter& filter, float delay,
				const Vector &pos, const char *pName, const CEffectData &data )
	{
		if ( !SuppressTE( filter ) )
		{
			TE_DispatchEffect( filter, delay, pos, pName, data );
		}
	}

	//-------------------------------------------------------------------------
	// THE THIRTY-EIGHT WITH NONE.
	//
	// Every signature is ITempEntsSystem's, unchanged. A body here would be a
	// second description of an effect this client cannot ask for — the managed
	// declaration is the first — and the two would agree until one changed.
	// An arriving temp entity does not come through here at all; it comes
	// through its RecvTable.
	//-------------------------------------------------------------------------
	virtual void ArmorRicochet( IRecipientFilter&, float,
		const Vector*, const Vector* ) {}
	virtual void BeamEntPoint( IRecipientFilter&, float,
		int, const Vector *, int, const Vector*,
		int, int, int, int,
		float, float, float, int, float,
		int, int, int, int, int ) {}
	virtual void BeamEnts( IRecipientFilter&, float,
		int, int, int, int, int, int,
		float, float, float, int, float,
		int, int, int, int, int ) {}
	virtual void BeamFollow( IRecipientFilter&, float,
		int, int, int, float, float, float,
		float, float, float, float, float ) {}
	virtual void BeamPoints( IRecipientFilter&, float,
		const Vector*, const Vector*, int, int, int, int,
		float, float, float, int, float,
		int, int, int, int, int ) {}
	virtual void BeamLaser( IRecipientFilter&, float,
		int, int, int, int, int, int,
		float, float, float, int, float, int, int, int, int, int ) {}
	virtual void BeamRing( IRecipientFilter&, float,
		int, int, int, int, int, int,
		float, float, int, float, int, int, int, int, int, int ) {}
	virtual void BeamRingPoint( IRecipientFilter&, float,
		const Vector&, float, float, int, int, int, int,
		float, float, int, float, int, int, int, int, int, int ) {}
	virtual void BeamSpline( IRecipientFilter&, float,
		int, Vector* ) {}
	virtual void BloodStream( IRecipientFilter&, float,
		const Vector*, const Vector*, int, int, int, int, int ) {}
	virtual void BloodSprite( IRecipientFilter&, float,
		const Vector*, const Vector *, int, int, int, int, int ) {}
	virtual void BreakModel( IRecipientFilter&, float,
		const Vector&, const QAngle &, const Vector&, const Vector&,
		int, int, int, float, int ) {}
	virtual void BSPDecal( IRecipientFilter&, float,
		const Vector*, int, int ) {}
	virtual void ProjectDecal( IRecipientFilter&, float,
		const Vector*, const QAngle *, float, int ) {}
	virtual void BubbleTrail( IRecipientFilter&, float,
		const Vector*, const Vector*, float, int, int, float ) {}
	virtual void Decal( IRecipientFilter&, float,
		const Vector*, const Vector*, int, int, int ) {}
	virtual void DynamicLight( IRecipientFilter&, float,
		const Vector*, int, int, int, int, float, float, float ) {}
	virtual void Explosion( IRecipientFilter&, float,
		const Vector*, int, float, int, int, int, int, const Vector* = NULL,
		unsigned char = 'C' ) {}
	virtual void ShatterSurface( IRecipientFilter&, float,
		const Vector*, const QAngle*, const Vector*, const Vector*,
		float, float, float, ShatterSurface_t,
		int, int, int, int, int, int ) {}
	virtual void GlowSprite( IRecipientFilter&, float,
		const Vector*, int, float, float, int ) {}
	virtual void FootprintDecal( IRecipientFilter&, float, const Vector *, const Vector*,
		int, int, unsigned char ) {}
	virtual void Fizz( IRecipientFilter&, float,
		const IGameEntity *, int, int, int ) {}
	virtual void KillPlayerAttachments( IRecipientFilter&, float,
		int ) {}
	virtual void LargeFunnel( IRecipientFilter&, float,
		const Vector*, int, int ) {}
	virtual void MetalSparks( IRecipientFilter&, float,
		const Vector*, const Vector* ) {}
	virtual void EnergySplash( IRecipientFilter&, float,
		const Vector*, const Vector*, bool ) {}
	virtual void PlayerDecal( IRecipientFilter&, float,
		const Vector*, int, int ) {}
	virtual void ShowLine( IRecipientFilter&, float,
		const Vector*, const Vector* ) {}
	virtual void Sparks( IRecipientFilter&, float,
		const Vector*, int, int, const Vector * ) {}
	virtual void Sprite( IRecipientFilter&, float,
		const Vector*, int, float, int ) {}
	virtual void SpriteSpray( IRecipientFilter&, float,
		const Vector*, const Vector*, int, int, float, int ) {}
	virtual void WorldDecal( IRecipientFilter&, float,
		const Vector*, int ) {}
	virtual void MuzzleFlash( IRecipientFilter&, float,
		const Vector &, const QAngle &, float, int ) {}
	virtual void Dust( IRecipientFilter&, float,
		const Vector &, const Vector &, float, float ) {}
	virtual void GaussExplosion( IRecipientFilter&, float,
		const Vector &, const Vector &, int ) {}
	virtual void PhysicsProp( IRecipientFilter&, float, int, int,
		const Vector&, const QAngle &, const Vector&, int, int ) {}
	virtual void ClientProjectile( IRecipientFilter&, float,
		const Vector*, const Vector*, int, int, IGameEntity * ) {}

	//-------------------------------------------------------------------------
	// BEHAVIOUR LEDGER ROW S5, on this side.
	//
	// Stock's own body was `Assert(0)` and nothing else: triggering a temp
	// entity from a KeyValues block is implemented only in tool builds. Kept as
	// stock — a loud refusal — because inventing behaviour here would be a
	// divergence rather than a fix.
	//-------------------------------------------------------------------------
	virtual void TriggerTempEntity( KeyValues * )
	{
		Assert( 0 );
	}
};

static C_TempEntsSystem g_TESystem;

// Expose to rest of the client, exactly as c_te.cpp:690 did.
ITempEntsSystem *te = &g_TESystem;
