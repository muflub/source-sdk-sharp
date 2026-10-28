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
// ONE SINCE C8 (lane c6-input) took cdll_util.cpp's two with it: te->DispatchEffect
// (shell_effect_dispatch_client.cpp) is the only `te->` call on the client link
// line (tempent_evicted_test's TheOnlyTeArrowCallsLeftAreTheOnesThatForward
// counts it), so Bubbles and Smoke joined the empty overrides and TE_Bubbles
// and TE_Smoke, which only they called, are gone (lane M1-C, 2026-10-25).
//
// THREE, BUT NOT THE THREE THE ANALYSIS LANE NAMED. Lane C2 read
// `te->Decal/Bubbles/Smoke` and cited `baseentity_shared.cpp` for the Decal.
// baseentity_shared.cpp is not on the CLIENT link line at all — it is a server
// object — so there is no `te->Decal` on this side and TE_Decal is dead here.
// The count agreed and the set did not, which is the shape that would have kept
// a dead body and dropped a live one.
//
// THE OTHER 39 OVERRIDES ARE EMPTY, NOT DELETED. ITempEntsSystem declares 41
// pure virtuals; a class that does not override one does not compile. An empty
// override is therefore the smallest thing this build can say, and it says the
// true thing: nothing calls it. It is not the same as dropping the virtual,
// which would change the vtable layout under any caller that came back.
//
// THE SUPPRESSION IS CARRIED, not dropped with the class. CTempEntsSystem::
// SuppressTE is the prediction filter — when a player predicts an entity
// locally the server must not also send that player its effects. It is C#'s
// since lane M1-C (LocalTempEntSuppression, through IManagedTempEntsClient::
// SuppressLocalTempEnt): DispatchEffect hands over the two facts this side
// holds, CanPredict() and the filter's recipient count, and carries out the
// answer. With no managed side there is nothing to decide, and nothing plays.
//
// NOTHING HERE IS REGISTERED BY NAME. There is no DECLARE_CLIENT_EFFECT,
// DECLARE_TEMPENT or REGISTER_EFFECT in this file or in the nine it replaces,
// and stock c_te.cpp exposed no interface either — `te` was a plain global with
// no EXPOSE_SINGLE_INTERFACE and no CreateInterface behind it. That matters
// because a name-resolved entry point is reachable with zero importers, and
// checking for one is the third leg of the proof the census cannot supply.
//
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "irecipientfilter.h"
#include "itempents.h"
#include "effect_dispatch_data.h"
#include "tier0/dbg.h"
#include "collisionproperty.h"
#include "eiface.h"
#include "ipredictionsystem.h"
#include "shattersurfacetypes.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!

// te_effect_dispatch.cpp's, in stock game/client/c_te_effect_dispatch.cpp,
// which stays: it owns the RecvTable a dispatched effect arrives on.
void TE_DispatchEffect( IRecipientFilter& filter, float delay,
	const Vector &pos, const char *pName, const CEffectData &data );

//=============================================================================//
// The client's ITempEntsSystem, which was c_te.cpp's C_TempEntsSystem.
//
// One override forwards; the rest are empty because nothing on this build's
// link line calls them. The class still derives from ITempEntsSystem, and so
// from IPredictionSystem, so `te` is still on the list SuppressHostEvents walks
// and still carries a suppress host — that is what CanPredict() below reads.
//=============================================================================//
class C_TempEntsSystem : public ITempEntsSystem
{
public:
	//-------------------------------------------------------------------------
	// THE ONE WITH A CALLER.
	//-------------------------------------------------------------------------
	virtual void DispatchEffect( IRecipientFilter& filter, float delay,
				const Vector &pos, const char *pName, const CEffectData &data )
	{
		IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();
		if ( !pTempEnts || pTempEnts->SuppressLocalTempEnt( CanPredict() ? 1 : 0, filter.GetRecipientCount() ) )
			return;

		TE_DispatchEffect( filter, delay, pos, pName, data );
	}

	//-------------------------------------------------------------------------
	// THE THIRTY-NINE WITH NONE.
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
	virtual void Bubbles( IRecipientFilter&, float,
		const Vector*, const Vector*, float, int, int, float ) {}
	virtual void Smoke( IRecipientFilter&, float,
		const Vector*, int, float, int ) {}
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
