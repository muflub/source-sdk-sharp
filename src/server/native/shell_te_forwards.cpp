//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's native temp-entity forwards ============//
// SS_REASON a  a vtable the engine calls through
//
// ONE FILE. It was eight — shell_te_fwd_{a,b,c,d,e,f,s1,s4}.cpp, 2,388 lines —
// because six lanes were writing forwards at the same time and one file would
// have been one merge conflict per lane. That reason expired when the lanes
// did, and what it left behind was eight copies of the same forty-five-line
// header, eight copies of the `SourceSharpTe_Forward` extern (now declared once
// in shell_tempents.h, where the rest of this side's temp-entity contract
// already lives), and twenty-two forwards nothing calls.
//
// WHAT A FORWARD IS. Porting a temp entity means EVICTING its te_*.cpp: the
// managed side registers a ServerClass named CTESparks and stock's
// IMPLEMENT_SERVERCLASS_ST in te_sparks.cpp registers one too, and two of a name
// in the list the engine negotiates at connect makes the client begin
// instantiating and stop, with nothing in any log (plan 2E). Evicting the file
// takes its TE_Sparks() with it, so the entry point is re-made here over
// shell_tempents.cpp's generic writer, against a table whose props and offsets
// come from a C# declaration.
//
// MARSHALLING, AND NOTHING ELSE, IN EVERY BODY BELOW. Which temp entity class
// this is, which argument becomes which field, and what the field is called are
// all in TeForwardDispatch on the managed side; what crosses is a
// SOURCESHARP_TE_FWD_* id from tempent_forwards.h and the arguments the
// signature already had, packed in declaration order. That sentence used to be
// repeated inside twelve of the bodies; it is here once instead, and it is the
// only text that was dropped rather than moved — every other comment below
// travelled verbatim with the body it explains, and the line above each one
// names the file it came from.
//
// WHICH FORWARDS ARE HERE, AND WHY THESE. Taken with tools/shim-census over the
// debug ninja LINK LINE at c5eab9deb (143 server objects on the graph, 143 on
// disk, 0 ghosts), seventeen of the forty had an importer:
//
//   TE_ArmorRicochet  TE_BeamPoints  TE_BSPDecal   TE_BloodStream  TE_Bubbles
//   TE_BubbleTrail    TE_Decal       TE_Dust       TE_EnergySplash TE_Explosion
//   TE_LargeFunnel    TE_MetalSparks TE_MuzzleFlash TE_ProjectDecal TE_Smoke
//   TE_Sparks         TE_WorldDecal
//
// Four of those seventeen -- TE_ArmorRicochet, TE_BeamPoints, TE_BubbleTrail and
// TE_EnergySplash -- went in the server gc cull (2026-10-01): their importers
// had gone since, --print-gc-sections discarded all four, and they are deleted.
// Their SOURCESHARP_TE_FWD_* ids stay, RESERVED (tempent_forwards.h).
//
// TE_DispatchEffect is the eighteenth and has no EXTERNAL importer: the census
// skips a translation unit importing its own definition, and its only caller is
// DispatchEffect() below, in this same file. Six objects import THAT, so the
// body is reachable and stays. A census count of zero is not a proof of
// deadness on its own; the intra-file caller is why.
//
// The twenty-two with neither an importer nor an intra-file caller are gone.
// None of them was reachable any other way: there is no `te` global and no
// ITempEntsSystem instance anywhere on this side's link line (te.cpp is not on
// the graph at all, which is also why TE_Fizz and TE_FizzIndex are dead — the
// te.cpp:377/380 callers the old shell_te_fwd_d.cpp header cited went with the
// file), no forwarder is registered by name (no DECLARE_CLIENT_EFFECT,
// DECLARE_TEMPENT or REGISTER_EFFECT in any of the eight), and no managed code
// calls one: a TE_* is a C++ symbol with no ABI slot behind it. The one ABI
// entry point that lived among them, SourceSharp_EffectDispatchIndex, is at the
// bottom of this file, unchanged.
//
// WHAT THE MANAGED SIDE STILL CANNOT DO, so this is not read as a port that
// stalled: nothing here decides behaviour. Each body packs numbers and hands
// them over. The reason these stay native at all is that their CALLERS are
// native — EffectsServer.cpp, shell_worldquery.cpp, world_adapter.cpp,
// server_util_library.cpp — and each of those leaves in its own later phase,
// taking the forwards it holds up with it.
//
//=============================================================================//

#include <stddef.h>
#include "edict.h"
#include "networkstringtabledefs.h"
#include "irecipientfilter.h"
#include "tempent_forwards.h"
#include "ss_recipient_filter.h"
#include "shell_tempents.h"
#include "iservernetworkable.h"
#include "ss_effect_dispatch.h"

// What cbase.h and networkstringtable_gamedll.h supplied, declared
// (plan_server_novalve.md P5): the engine's globals (shell_gamedll.cpp) and
// the EffectDispatch string table (created in shell_stringtables.cpp, defined
// in shell_engineboundary.cpp).
extern CGlobalVars *gpGlobals;
extern INetworkStringTable *g_pStringTableEffectDispatch;
#include "ss_string.h"


//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_s4.cpp
//-----------------------------------------------------------------------------
// The name, as an entry in the EffectDispatch network string table.
//
// LEDGER ROW S4 IN ONE LINE. Stock is
//
//     g_pStringTableEffectDispatch->AddString( CBaseEntity::IsServer(), pName );
//
// under an #ifndef SS_CBASEENTITY_ALIAS whose #else passes a literal true
// (te_effect_dispatch.cpp:80-84). BOTH ARMS ARE THE SAME CALL:
// CBaseEntity::IsServer() is baseentity.h:2077's `return true`, a compile-time
// constant that exists "for debugging shared code", and this translation unit is
// game/server/ and is compiled into server.so only. So the answer is true, the
// preprocessor already knew it, and passing true here loses nothing. The row is
// on the ledger because somebody had to check that, not because anything is
// missing — and this comment is the check, written down.
//
// AddString on a name already in the table returns the index it already has, so
// this is idempotent and the same name is the same number for the life of the
// level. That is the property the client's GetString depends on.
//-----------------------------------------------------------------------------
static int EffectNameIndex( const char *pName )
{
	if ( !g_pStringTableEffectDispatch || !pName || !pName[0] )
		return 0;

	return g_pStringTableEffectDispatch->AddString( true, pName );
}


//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_b.cpp
//-----------------------------------------------------------------------------
// te_bspdecal.cpp's TE_BSPDecal, over the managed DT_TEBSPDecal.
//
// Stock dereferences pos without checking and so does everything that calls it;
// the NULL guard here is this file's, not a behaviour change, and it turns a
// crash into a decal that does not appear.
//-----------------------------------------------------------------------------
void TE_BSPDecal( IRecipientFilter& filter, float delay,
	const Vector* pos, int entity, int index )
{
	// Stock returns rather than substituting here, so the guard is the
	// forward's and not the dispatcher's: C# never sees a null pointer.
	if ( !pos )
		return;

	const float f[] =
	{
		pos->x,
		pos->y,
		pos->z,
	};

	const int i[] =
	{
		entity,
		index,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_BSP_DECAL, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}


//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_d.cpp
//-----------------------------------------------------------------------------
// te_bloodstream.cpp's TE_BloodStream, over the managed DT_TEBloodStream.
//
// m_vecOrigin is DT_TEParticleSystem's and not this table's. It is in this
// class's BLOB all the same: the base is nested at datatable offset zero and its
// props read at their own offsets out of the same object, which is why the
// writer reaches it by name here (plan 3.2).
//-----------------------------------------------------------------------------
void TE_BloodStream( IRecipientFilter& filter, float delay,
	const Vector* org, const Vector* dir, int r, int g, int b, int a, int amount )
{
	const float f[] =
	{
		org->x,
		org->y,
		org->z,
		dir->x,
		dir->y,
		dir->z,
	};

	const int i[] =
	{
		r,
		g,
		b,
		a,
		amount,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_BLOOD_STREAM, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}


//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_d.cpp
//-----------------------------------------------------------------------------
// te_bubbles.cpp's TE_Bubbles, over the managed DT_TEBubbles. Two live callers:
// UTIL_Bubbles and SourceSharp_BubbleEffect, which is every managed env_bubbles.
//-----------------------------------------------------------------------------
void TE_Bubbles( IRecipientFilter& filter, float delay,
	const Vector* mins, const Vector* maxs, float height, int modelindex, int count, float speed )
{
	const float f[] =
	{
		mins->x,
		mins->y,
		mins->z,
		maxs->x,
		maxs->y,
		maxs->z,
		height,
		speed,
	};

	const int i[] =
	{
		modelindex,
		count,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_BUBBLES, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_b.cpp
//-----------------------------------------------------------------------------
// te_decal.cpp's TE_Decal, over the managed DT_TEDecal.
//
// Stock's Assert( pos && start ) is kept as a refusal: the client re-traces the
// segment from m_vecStart to m_vecOrigin to find the surface, so a missing
// start is not a decal at a default place, it is a decal on nothing.
//-----------------------------------------------------------------------------
void TE_Decal( IRecipientFilter& filter, float delay,
	const Vector* pos, const Vector* start, int entity, int hitbox, int index )
{
	Assert( pos && start );

	// Stock returns rather than substituting here, so the guard is the
	// forward's and not the dispatcher's: C# never sees a null pointer.
	if ( !pos || !start )
		return;

	const float f[] =
	{
		pos->x,
		pos->y,
		pos->z,
		start->x,
		start->y,
		start->z,
	};

	const int i[] =
	{
		entity,
		hitbox,
		index,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_DECAL, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_s4.cpp
//-----------------------------------------------------------------------------
// te_effect_dispatch.cpp's TE_DispatchEffect, over the managed
// DT_TEEffectDispatch.
//
// EVERY FIELD IS WRITTEN BY NAME AND EVERY ONE OF THEM LIVES IN THE NESTED
// TABLE. DT_TEEffectDispatch declares exactly one prop — a SendPropDataTable
// pointing at DT_EffectData — so nothing below is a field of the class being
// sent. It works because the datatable prop sits at offset zero and the nested
// table's props read at their own offsets out of the same blob, which is the
// same mechanism a base uses (plan 3.2); shell_tempents.cpp's FindField
// descends into a nested table for exactly this reason.
//
// THE THREE-PROP FIELDS ARE WRITTEN AS ONE VECTOR AND THAT IS NOT A SHORTCUT.
// m_vOrigin, m_vStart and m_ControlPoint1.m_vecOffset are each ONE C# field and
// THREE props at offset + i*4. SourceSharpTe_SetVector writes three consecutive
// floats at the field's offset — which, for a stride of exactly one float, IS
// element 0, 1 and 2. A field whose stride were anything else would need a
// per-element setter, and the symptom of getting it wrong is element 0 right
// and the rest silently zero: an origin a few feet away rather than an
// obviously wrong one. TempEntEffectDataTests asserts all three components of
// all three fields for that reason.
//
// The `pos` parameter is stock's and stock ignores it: the position that
// matters is data.m_vOrigin, which the caller has already filled in and which
// the two DispatchEffect overloads below use to build their filter.
//-----------------------------------------------------------------------------
void TE_DispatchEffect( IRecipientFilter& filter, float delay,
	const Vector &pos, const char *pName, const ss::CEffectData &data )
{
	const float f[] =
	{
		data.m_vOrigin.x,
		data.m_vOrigin.y,
		data.m_vOrigin.z,
		data.m_vStart.x,
		data.m_vStart.y,
		data.m_vStart.z,
		data.m_vAngles.x,
		data.m_vAngles.y,
		data.m_vAngles.z,
		data.m_vNormal.x,
		data.m_vNormal.y,
		data.m_vNormal.z,
		data.m_flMagnitude,
		data.m_flScale,
		data.m_flRadius,
		data.m_CustomColors.m_vecColor1.x,
		data.m_CustomColors.m_vecColor1.y,
		data.m_CustomColors.m_vecColor1.z,
		data.m_CustomColors.m_vecColor2.x,
		data.m_CustomColors.m_vecColor2.y,
		data.m_CustomColors.m_vecColor2.z,
		data.m_ControlPoint1.m_vecOffset.x,
		data.m_ControlPoint1.m_vecOffset.y,
		data.m_ControlPoint1.m_vecOffset.z,
	};

	const int i[] =
	{
		data.m_fFlags,
		data.m_nAttachmentIndex,
		data.m_nSurfaceProp + 1,
		EffectNameIndex( pName ),
		data.m_nMaterial,
		data.m_nDamageType,
		data.m_nHitBox,
		data.m_nEntIndex,
		data.m_nColor,
		data.m_bCustomColors ? 1 : 0,
		data.m_bControlPoint1 ? 1 : 0,
		(int)data.m_ControlPoint1.m_eParticleAttachment,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_TE_DISPATCH_EFFECT, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_f.cpp
//-----------------------------------------------------------------------------
// func_dust.cpp's TE_Dust, by way of shell_entity_orphans.cpp, over the managed
// DT_TEDust. LEDGER ROW S6.
//
// THE ORIGIN IS THE BASE'S FIELD. DT_TEDust declares a size, a speed and a
// direction; m_vecOrigin belongs to DT_TEParticleSystem and sits at the front of
// this class's blob. The writer's FindField walks the managed base chain, so
// naming it here resolves — and a forward that set only its own three fields
// would put every puff of dust at the world origin, which looks like dust.
//
// THE ASSERT TRAVELS WITH THE FUNCTION. m_vecDirection is four bits per
// component over an explicit [-1, 1] — stock's "cheap normal" — so a direction
// longer than one is CLAMPED BY THE ENCODER rather than rejected, and comes out
// the other side pointing somewhere else. That is why the check is at the send
// and not in the client.
//-----------------------------------------------------------------------------
void TE_Dust( IRecipientFilter& filter, float delay,
	const Vector &pos, const Vector &dir, float size, float speed )
{
	Assert( dir.Length() < 1.01 );	// make sure it's a normal

	const float f[] =
	{
		pos.x,
		pos.y,
		pos.z,
		size,
		speed,
		dir.x,
		dir.y,
		dir.z,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_DUST, filter, delay, f, ARRAYSIZE( f ), NULL, 0 );
}


//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_e.cpp
//-----------------------------------------------------------------------------
// te_explosion.cpp's TE_Explosion.
//
// THE BUSIEST OF THE SIX and the one with a live caller in this build:
// shell_worldquery.cpp:782, which IEngineEffects.ExplosionEffect reaches. From
// this commit that chain's last link is the managed table instead of the native
// one; nothing in between changed and nothing stopped.
//
// The (0,0,1) for a null normal is stock's own substitution
// (te_explosion.cpp:126) and it is UP, not a zero-length normal the client
// would have to special-case. m_vecOrigin belongs to DT_TEParticleSystem and
// lives at the front of this class's blob; the writer's FindField walks the
// managed base chain, so naming it here resolves.
//
// No default arguments in this definition. te.cpp's declaration carries
// `= NULL` and `= 'C'` and repeating them here is ill-formed; stock's own
// definition omits them for the same reason.
//-----------------------------------------------------------------------------
void TE_Explosion( IRecipientFilter& filter, float delay,
	const Vector* pos, int modelindex, float scale, int framerate, int flags,
	int radius, int magnitude, const Vector* normal, unsigned char materialType )
{
	const float f[] =
	{
		pos ? pos->x : 0.0f,
		pos ? pos->y : 0.0f,
		pos ? pos->z : 0.0f,
		scale,
		normal ? normal->x : 0,
		normal ? normal->y : 0,
		normal ? normal->z : 1,
	};

	const int i[] =
	{
		modelindex,
		framerate,
		flags,
		materialType,
		radius,
		magnitude,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_EXPLOSION, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_e.cpp
//-----------------------------------------------------------------------------
// te_largefunnel.cpp's TE_LargeFunnel.
//
// The other live caller: shell_worldquery.cpp:2077, behind
// IEngineEffects.FunnelEffect. m_vecOrigin is DT_TEParticleSystem's, at the
// front of the blob -- a forward that set only the two own props would draw
// every funnel at the world origin, which looks like a funnel rather than like
// nothing.
//-----------------------------------------------------------------------------
void TE_LargeFunnel( IRecipientFilter& filter, float delay,
	const Vector* pos, int modelindex, int reversed )
{
	const float f[] =
	{
		pos ? pos->x : 0.0f,
		pos ? pos->y : 0.0f,
		pos ? pos->z : 0.0f,
	};

	const int i[] =
	{
		modelindex,
		reversed,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_LARGE_FUNNEL, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_d.cpp
//-----------------------------------------------------------------------------
// te_armorricochet.cpp's TE_MetalSparks, over the managed DT_TEMetalSparks.
//
// The assert is stock's own (te_armorricochet.cpp:100) and stays on this side of
// the boundary: it is a statement about what the CALLER passes, not about what
// the table encodes, and the managed declaration has nowhere to put it.
//-----------------------------------------------------------------------------
void TE_MetalSparks( IRecipientFilter& filter, float delay,
	const Vector* pos, const Vector* dir )
{
	Assert( dir->Length() < 1.01 ); // make sure it's a normal

	const float f[] =
	{
		pos->x,
		pos->y,
		pos->z,
		dir->x,
		dir->y,
		dir->z,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_METAL_SPARKS, filter, delay, f, ARRAYSIZE( f ), NULL, 0 );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_f.cpp
//-----------------------------------------------------------------------------
// te_muzzleflash.cpp's TE_MuzzleFlash, over the managed DT_TEMuzzleFlash.
//
// THE ANGLES GO ON THE WIRE AS A PLAIN COORD VECTOR. Stock's member is a
// CNetworkQAngle and its prop is SendPropVector, not SendPropQAngles — no
// anglemod, no [0,360] range — so a QAngle's three floats are handed to
// SetVector as they are. Lane B's DT_TEProjectedDecal is the other way round
// and the two are deliberately not unified: see TeMuzzleFlash.cs.
//-----------------------------------------------------------------------------
void TE_MuzzleFlash( IRecipientFilter& filter, float delay,
	const Vector &start, const QAngle &angles, float scale, int type )
{
	const float f[] =
	{
		start.x,
		start.y,
		start.z,
		angles.x,
		angles.y,
		angles.z,
		scale,
	};

	const int i[] =
	{
		type,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_MUZZLE_FLASH, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_b.cpp
//-----------------------------------------------------------------------------
// te_projecteddecal.cpp's TE_ProjectDecal, over the managed DT_TEProjectedDecal.
//
// The angles go through SetVector because the BLOB does not know the difference
// -- three floats either way -- and the wire does: DT_TEProjectedDecal declares
// m_angRotation as SendPropQAngles, which anglemods each component into its
// declared [0,360] on the way out. That is the declaration's business, in
// TeProjectedDecal.cs, and nothing here has an opinion about it.
//-----------------------------------------------------------------------------
void TE_ProjectDecal( IRecipientFilter& filer, float delay,
	const Vector* pos, const QAngle *angles, float distance, int index )
{
	// Stock returns rather than substituting here, so the guard is the
	// forward's and not the dispatcher's: C# never sees a null pointer.
	if ( !pos || !angles )
		return;

	const float f[] =
	{
		pos->x,
		pos->y,
		pos->z,
		angles->x,
		angles->y,
		angles->z,
		distance,
	};

	const int i[] =
	{
		index,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_PROJECT_DECAL, filer, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_c.cpp
//-----------------------------------------------------------------------------
// te_smoke.cpp's TE_Smoke.
//
// THE ONE IN THIS LANE WITH A LIVE NATIVE CALLER: EffectsServer.cpp:128, which
// UTIL_Smoke reaches through g_pEffects. From this commit that chain's last
// link is the managed table instead of the native one; nothing in between
// changed and nothing stopped.
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

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SMOKE, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_s1.cpp
//-----------------------------------------------------------------------------
// te_sparks.cpp's TE_Sparks, over the managed DT_TESparks.
//
// The NULL direction is stock's own: vec3_origin means "no particular
// direction" rather than a direction of zero length that would have to be
// special-cased downstream. te_sparks.cpp:84 wrote exactly this and the
// comment travels with the behaviour.
//-----------------------------------------------------------------------------
void TE_Sparks( IRecipientFilter& filter, float delay,
	const Vector* pos, int nMagnitude, int nTrailLength, const Vector *pDir )
{
	const float f[] =
	{
		pos ? pos->x : 0.0f,
		pos ? pos->y : 0.0f,
		pos ? pos->z : 0.0f,
		pDir ? pDir->x : 0.0f,
		pDir ? pDir->y : 0.0f,
		pDir ? pDir->z : 0.0f,
	};

	const int i[] =
	{
		nMagnitude,
		nTrailLength,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SPARKS, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_b.cpp
//-----------------------------------------------------------------------------
// te_worlddecal.cpp's TE_WorldDecal, over the managed DT_TEWorldDecal.
//-----------------------------------------------------------------------------
void TE_WorldDecal( IRecipientFilter& filter, float delay,
	const Vector* pos, int index )
{
	// Stock returns rather than substituting here, so the guard is the
	// forward's and not the dispatcher's: C# never sees a null pointer.
	if ( !pos )
		return;

	const float f[] =
	{
		pos->x,
		pos->y,
		pos->z,
	};

	const int i[] =
	{
		index,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_WORLD_DECAL, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

//-----------------------------------------------------------------------------
// moved verbatim from shell_te_fwd_s4.cpp
//-----------------------------------------------------------------------------
// The two entry points the rest of the server actually calls, unchanged from
// te_effect_dispatch.cpp:93-102.
//
// They used to route through te->, which is stock's own indirection, for the one
// thing it added: IPredictionSystem::SuppressTE. te.cpp went with plan_tmp.md
// Part 7 and that test came with it -- SourceSharpTe_SuppressFilter, in
// shell_tempents.cpp, the same code in the one place all of its callers share.
// It always answers false in this build (2H), and carrying it is how that stays
// a statement about this build rather than about this file.
//-----------------------------------------------------------------------------
// The filtered form: file-local since the server's filters became ss:: types
// (te_effect_dispatch.h's declaration names game/server's CRecipientFilter,
// which nothing defines any more). Its one caller is just below.
// (moved verbatim from shell_te_fwd_s4.cpp)
static void DispatchEffect( const char *pName, const ss::CEffectData &data, ss::CRecipientFilter &filter )
{

	TE_DispatchEffect( filter, 0.0, data.m_vOrigin, pName, data );
}

void DispatchEffect( const char *pName, const ss::CEffectData &data )
{
	ss::CPASFilter filter( data.m_vOrigin );
	DispatchEffect( pName, data, filter );
}

// cs_sv_te_dispatch, the EffectDispatch string-table probe, is a diagnostic: it
// lives in shell_te_forwards_debug.cpp with its two name lists, which only a
// debug build compiles.

//-----------------------------------------------------------------------------
// The ABI slot the header comment above asked for: a name, as an index in the
// EffectDispatch network string table.
//
// EffectNameIndex, exported and nothing more — deliberately the SAME function
// rather than a second one that does the same thing. Two resolutions of one
// name is precisely the failure mode this file's header names: the symptom of
// the two disagreeing is not a missing effect but the WRONG one.
//
// IT OUTLIVES THIS FILE. Part 7 deletes te.cpp and TE_DispatchEffect goes with
// it; this and the two DispatchEffect overloads above do not, because they are
// what the rest of the server and all of managed code call.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EffectDispatchIndex( const char *pName )
{
	return EffectNameIndex( pName );
}

//=============================================================================//
// IEffects, FOLDED IN FROM game/server/EffectsServer.cpp (217 lines).
// plan_shim.md Phase 2c.
//
// WHY HERE RATHER THAN RELOCATED BESIDE IT. Every one of the eight methods
// below was the same three lines -- build a recipient filter, apply the
// SuppressTE rule, call a TE_ function -- and all eight TE_ functions are in
// THIS FILE. 2c offers "relocated under (e, weak); or folded into
// shell_te_forwards.cpp at 1f if that lands first". 1f landed; this is the fold,
// and it removes a translation unit and a vpc row rather than moving them.
//
// The file's own banner already named EffectsServer.cpp as one of the four
// native callers holding these forwards up. It is not a caller any more; it is
// the bottom of this file.
//
// THE EIGHT SuppressTE COPIES BECOME THE ONE THIS FILE ALREADY HAS.
// CEffectsServer::SuppressTE was `if ( GetSuppressHost() ) { if ( !filter.
// GetRecipientCount() ) return true; } return false;`, copied at eight call
// sites. SourceSharpTe_SuppressFilter (shell_tempents.cpp:637) is the SAME
// rule, already reduced to `return false` on a measurement this project has
// written down and GATED: every writer of a suppress host in the compiled set
// was checked one at a time, and suppress_host_dead_test.cpp fails if a fourth
// appears. Nine copies of one always-false test is nine places for the
// measurement to go stale; there is one now, and it is the one with the test
// on it.
//
// WHAT IS REACHED, measured over the debug link line: five of the eight --
// Dust, MetalSparks, Smoke, Sparks and MuzzleFlash -- and those five are the
// whole of ss::IEffects (ss_effects.h). Beam, EnergySplash, Ricochet, Time,
// IsServer and SuppressEffectsSounds were kept only because the Valve IEffects
// was abstract; with its last caller switched to ss::Effects(), they went with
// it (P5 lane SP).
//
// THE IEffects001 EXPOSE AND THE g_pEffects GLOBAL ARE GONE TOO. `IEffects001`
// is in no engine binary -- of the 90 .so files under Source SDK Base 2013
// Multiplayer, only the stock game DLLs (sourcetest/bin/server.so, client.so)
// carry the string, each for its own copy -- and nm finds no server object
// importing g_pEffects. No engine reads the interface, so server.so's effects
// are its own type.
//
// THE THREE g_sModelIndex* externs at the top of EffectsServer.cpp did NOT come
// across: none of them was named in any of the eight bodies. The smoke sprite
// index is the CALLER's argument (server_util_library.cpp:435 passes
// g_sModelIndexSmoke in), so the externs were three declarations doing nothing.
//=============================================================================//

#include "shake.h"
#include "ss_effects.h"

class CEffectsServer : public ss::IEffects
{
public:
	CEffectsServer() {}
	virtual ~CEffectsServer() {}

	virtual void Smoke( const Vector &origin, int mModel, float flScale, float flFramerate )
	{
		ss::CPVSFilter filter( origin );

		// flScale * 0.1f is stock's, and it is the reason this is not a table
		// of function pointers: three of the eight adjust an argument on the way
		// through, so the "marshalling" is not identical even where the shape is.
		TE_Smoke( filter, 0.0, &origin, mModel, flScale * 0.1f, flFramerate );
	}

	virtual void Sparks( const Vector &position, int nMagnitude = 1, int nTrailLength = 1,
	                     const Vector *pvecDir = NULL )
	{
		ss::CPVSFilter filter( position );

		TE_Sparks( filter, 0.0, &position, nMagnitude, nTrailLength, pvecDir );
	}

	virtual void Dust( const Vector &pos, const Vector &dir, float size, float speed )
	{
		ss::CPVSFilter filter( pos );

		TE_Dust( filter, 0.0, pos, dir, size, speed );
	}

	virtual void MuzzleFlash( const Vector &origin, const QAngle &angles, float scale, int type )
	{
		ss::CPVSFilter filter( origin );

		TE_MuzzleFlash( filter, 0.0f, origin, angles, scale, type );
	}

	virtual void MetalSparks( const Vector &position, const Vector &direction )
	{
		ss::CPVSFilter filter( position );

		TE_MetalSparks( filter, 0.0, &position, &direction );
	}
};

//-----------------------------------------------------------------------------
// The server's effects object, reached through ss::Effects().
//-----------------------------------------------------------------------------
static CEffectsServer s_EffectServer;

ss::IEffects *ss::Effects()
{
	return &s_EffectServer;
}
