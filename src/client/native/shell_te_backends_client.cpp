//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's temp entity backends ==================//
// SS_REASON a  a vtable the engine calls through
//
// plan_tmp.md Part 4. Porting a c_te_*.cpp moves its DECODE and its DECISION to
// C#; what it calls at the end is a drawing primitive, and those stay native
// behind ABI slots — exactly how ClientSprite, ClientBeam, ClientTrails and
// ClientDynamicLight already work.
//
// SIXTEEN SLOTS. C-Phase 1 landed the first (ClientEnergySplash) with one temp
// entity received end to end behind it; C-Phase 2 filled in the other fifteen in
// a single pass, because they all land in this file and in the gamesystem's fill
// block and parallel lanes would collide in both.
//
// FIFTEEN OF THEM HAVE NO MANAGED CALLER YET, AND THAT IS THE DESIGN. C-Phase 3
// evicts the c_te_*.cpp files one nested-table subtree at a time (2E-bis: the
// client resolves a table name to exactly one owner), and a lane that arrived to
// find its backend missing would be a lane appending an ABI slot on its own —
// which is the collision this phase exists to prevent. Until then every effect
// still arrives through its native client half and nothing here is reached.
//
// WHAT EACH SLOT REACHES is named in its comment and in sourcesharp.h, because a
// slot that cannot be shown to end in real drawing code is a promise that
// lapses. All sixteen end in one of four places and nowhere else:
//
//   (beams           -- moved to C#: src/client/managed/Beams)
//   g_pEffects       -- CEffectsClient, EffectsClient.cpp
//   effects          -- IVEfx, the ENGINE's decal system (iefx.h)
//   the registry     -- DispatchEffectToCallback, c_te_effect_dispatch.cpp
//
// THE ARGUMENTS ARE UNPACKED IN A HEADER, not here. shell_te_backend_args.h has
// no engine in it and te_backend_args_test.cpp reads it, which is the only way
// a seventeen-parameter call's argument ORDER can be checked without a running
// Source. What is left in each body below is one assignment per line into the
// primitive, where a mistake is visible by reading.
//
//=============================================================================//

#include "cbase.h"

// plan_entity.md Phase 5a: an entity index or handle resolves through §3.3's
// slot table, not through cl_entitylist. shell_cliententitylist.h says which of
// the five resolvers answers which question.
#include "shell_cliententitylist.h"

#include "IEffects.h"
#include "iviewrender_beams.h"
#include "beam_flags.h"
#include "iefx.h"
#include "fx.h"
#include "decals.h"
// C-Phase 3 lane CX. IStaticPropMgr.h is the static-prop decal's
// staticpropmgr (IStaticPropMgrClient, declared in cdll_client_int.cpp);
// util_shared.h is UTIL_IsLowViolence. decals.h above is already here for
// FDECAL_*, and is also where IDecalEmitterSystem and `decalsystem` live.
#include "engine/IStaticPropMgr.h"
#include "util_shared.h"
#include "c_te_effect_dispatch.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/itexture.h"
#include "materialsystem/imaterialvar.h"
#include "filesystem.h"
#include "clienteffectprecachesystem.h"
#include "view.h"

#include "beam_shared.h"

// C-Phase 3, lane CP's eight. fx_explosion.h is CExplosionOverlay (its two
// explosion singletons are C# now, lane c5-particles; so is the funnel);
// (c_te_particlesystem.h left with the blood stream and the line: their
// CTEParticleRenderer is C#'s TeParticles, lane c5-particles.)
#include "fx_explosion.h"

// sharedInterface.h is where `random` -- the ENGINE's IUniformRandomStream --
// is declared, and it is what c_te_bloodstream.cpp included for it. Without it `random` resolves to libc's random(), which
// compiles as far as the arrow and then says the pointer is a function. Every
// position in the blood stream is drawn from this stream.
#include "sharedInterface.h"

// networkstringtable_clientdll.h is g_StringTableEffectDispatch, the client's
// copy of the table EffectDispatchName reads backwards. (The shattered pane's
// shards left with lane c5-particles: ShardEffects, C#.)
#include "networkstringtable_clientdll.h"

// C-Phase 3 lane DECALS. The three slots that close behaviour ledger rows C2
// and C3 reach the model renderer and ask what a model IS, neither of which
// anything else in this file needed.
#include "engine/ivmodelrender.h"
#include "model_types.h"

#include "shell_te_backend_args.h"
#include "sourcesharp.h"

#include "managed_convar_ref.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// r_decals, read by name (managed_convar_ref.h; lane c2-fx). It is the ENGINE's
// convar: stock's EffectsClient.cpp declared a second one under the same name,
// which tier1 parents onto the engine's (one value), and it left with IEffects'
// port. A managed declaration is refused for the same reason (ConVarRegistry:
// "managed code declares convars the engine defines"). Not named `r_decals`:
// fx.h still declares an extern ConVar of that name.
static CManagedConVarRef s_rDecals( "r_decals" );

using namespace TeBackendArgs;


//-----------------------------------------------------------------------------
// A round off an energy shield, where C# decided one should be.
//
// The whole of C_TEEnergySplash::PostDataUpdate minus the decode — which is the
// division this phase is about. Nothing is lost with the class: stock's
// Precache() was empty and its PostDataUpdate was this one line.
//
// bExplosive crosses as an int32 because a bool has no fixed width in C; the
// BLOB still holds one byte, which is what stock's CNetworkVar( bool ) is and
// what the prop's read proxy is chosen for (dt_recv.cpp:202).
//-----------------------------------------------------------------------------
void SourceSharp_ClientEnergySplash( const float *pPosXYZ, const float *pDirXYZ,
                                     int32_t bExplosive )
{
	if ( !pPosXYZ || !pDirXYZ || !g_pEffects )
		return;

	const Vector vecPos( pPosXYZ[0], pPosXYZ[1], pPosXYZ[2] );
	const Vector vecDir( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] );

	g_pEffects->EnergySplash( vecPos, vecDir, bExplosive != 0 );
}


//=============================================================================//
// THE BEAMS left this file with the managed beam system (plan_client_novalve
// §2 cluster 5): the six c_te_beam*.cpp players call IClientBeams in C#
// directly, and the BeamCreate* slots that forwarded them to
// CViewRenderBeams went with view_beams.cpp.
//=============================================================================//

//=============================================================================//
// THE CLIENT'S IEffects FAMILY.
//
// g_pEffects on this side is CEffectsClient (EffectsClient.cpp). Each of these
// applies its OWN CPVSFilter inside the call and drops the effect if the local
// player cannot see it — so the managed caller does not get a filter and must
// not invent one. Two filters would be two chances to drop the same effect, and
// a PVS filter with no recipients sends nothing and prints nothing.
//=============================================================================//

//-----------------------------------------------------------------------------
// Electric sparks. c_te_sparks.cpp; g_pEffects->Sparks is FX_ElectricSpark.
//
// The direction is a POINTER all the way down, and stays one: NULL is a spark
// with no preferred direction, which is a different effect from a spark aimed
// at the origin.
//-----------------------------------------------------------------------------
void SourceSharp_ClientSparks( const float *pPositionXYZ,
                               int32_t nMagnitude, int32_t nTrailLength,
                               const float *pDirectionXYZ )
{
	const EffectRequest req = Sparks( pPositionXYZ, nMagnitude, nTrailLength, pDirectionXYZ );

	if ( !req.bDraw || !g_pEffects )
		return;

	const Vector vecPosition ( req.vecPosition.x,  req.vecPosition.y,  req.vecPosition.z  );
	const Vector vecDirection( req.vecDirection.x, req.vecDirection.y, req.vecDirection.z );

	g_pEffects->Sparks( vecPosition, req.nMagnitude, req.nTrailLength,
	                    req.bHasDirection ? &vecDirection : NULL );
}

// THE TOOL RECORDING THAT CAME OFF c_te_armorricochet.cpp WITH THE FILE
// (RecordSparkImpact: stock's RecordMetalSparks and RecordArmorRicochet, a
// KeyValues message to ToolFramework_PostToolMessage beside each draw) stood
// here. Deleted: SFM is dead (user ruling 2026-10-19).

//-----------------------------------------------------------------------------
// A ricochet: metal sparks AND the sound. c_te_armorricochet.cpp's
// C_TEArmorRicochet half.
//-----------------------------------------------------------------------------
void SourceSharp_ClientRicochet( const float *pPositionXYZ, const float *pDirectionXYZ )
{
	const EffectRequest req = PositionAndDirection( pPositionXYZ, pDirectionXYZ );

	if ( !req.bDraw || !g_pEffects )
		return;

	const Vector vecPosition ( req.vecPosition.x,  req.vecPosition.y,  req.vecPosition.z  );
	const Vector vecDirection( req.vecDirection.x, req.vecDirection.y, req.vecDirection.z );

	g_pEffects->Ricochet( vecPosition, vecDirection );
}

//-----------------------------------------------------------------------------
// The same sparks with NO SOUND, which is the whole difference between this and
// the one above and is stock's own comment on IEffects. c_te_armorricochet.cpp's
// C_TEMetalSparks half; g_pEffects->MetalSparks is FX_MetalSpark.
//-----------------------------------------------------------------------------
void SourceSharp_ClientMetalSparks( const float *pPositionXYZ, const float *pDirectionXYZ )
{
	const EffectRequest req = PositionAndDirection( pPositionXYZ, pDirectionXYZ );

	if ( !req.bDraw || !g_pEffects )
		return;

	const Vector vecPosition ( req.vecPosition.x,  req.vecPosition.y,  req.vecPosition.z  );
	const Vector vecDirection( req.vecDirection.x, req.vecDirection.y, req.vecDirection.z );

	g_pEffects->MetalSparks( vecPosition, vecDirection );
}

//-----------------------------------------------------------------------------
// Smoke sprites. c_te_smoke.cpp; g_pEffects->Smoke is FX_Smoke.
//
// flScale arrives ALREADY multiplied by ten, which is where stock does it —
// both of its callers pass m_fScale * 10.0f under a comment saying "the number
// passed down is 10 times smaller". That multiply is part of the decode and
// lives in C# with the rest of it.
//-----------------------------------------------------------------------------
void SourceSharp_ClientSmoke( const float *pOriginXYZ, int32_t nModelIndex,
                              float flScale, float flFrameRate )
{
	const EffectRequest req = Smoke( pOriginXYZ, nModelIndex, flScale, flFrameRate );

	if ( !req.bDraw || !g_pEffects )
		return;

	g_pEffects->Smoke( Vector( req.vecPosition.x, req.vecPosition.y, req.vecPosition.z ),
	                   req.nModelIndex, req.flScale, req.flFrameRate );
}

//-----------------------------------------------------------------------------
// A muzzle flash. c_te_muzzleflash.cpp; g_pEffects->MuzzleFlash switches on the
// type over FX_MuzzleEffect, FX_GunshipMuzzleEffect and FX_StriderMuzzleEffect
// and prints a line for anything else — which is stock's own behaviour and is
// left alone rather than turned into a silent default.
//-----------------------------------------------------------------------------
void SourceSharp_ClientMuzzleFlash( const float *pOriginXYZ, const float *pAnglesPYR,
                                    float flScale, int32_t nType )
{
	const EffectRequest req = MuzzleFlash( pOriginXYZ, pAnglesPYR, flScale, nType );

	if ( !req.bDraw || !g_pEffects )
		return;

	g_pEffects->MuzzleFlash( Vector( req.vecPosition.x, req.vecPosition.y, req.vecPosition.z ),
	                         QAngle( req.vecAngles.x, req.vecAngles.y, req.vecAngles.z ),
	                         req.flScale, req.nType );
}


//=============================================================================//
// THE DECALS.
//=============================================================================//

//-----------------------------------------------------------------------------
// A decal on the world, a brush model or a static prop. c_te_worlddecal.cpp,
// c_te_bspdecal.cpp and c_te_footprint.cpp all end here.
//
// THE ENTITY IS RESOLVED HERE and it has to be: DecalShoot wants a model_t*,
// an abs origin and abs angles, and a model_t* is not a thing that crosses this
// boundary. SourceSharp_ClientEntityFromIndex answers for BOTH hierarchies — it
// hands back an IClientEntity rather than a C_BaseEntity — so a managed entity
// resolves here as readily as a stock one. Entity 0 is the world.
//
// r_decals is checked HERE, once, because it is a renderer setting and all
// three of stock's callers check it immediately before this same call.
//
// LEDGER ROWS C2 AND C3 ARE UNTOUCHED BY THIS SLOT. Those are decals on
// ENTITIES (C_BaseEntity::AddDecal, which this hierarchy has no version of) and
// the world/brush arm of the PROJECTED decal — a different call in a different
// file, reached after a trace. This is effects->DecalShoot, which is the
// engine's own decal system and was never guarded. Neither row is closed here
// and neither is made to look closed.
//-----------------------------------------------------------------------------
void SourceSharp_ClientDecalShoot( int32_t nDecalIndex, int32_t nEntity,
                                   const float *pPositionXYZ, const float *pSAxisXYZ,
                                   int32_t nFlags )
{
	const DecalRequest req = DecalShoot( nDecalIndex, nEntity, pPositionXYZ, pSAxisXYZ, nFlags );

	if ( !req.bDraw || !effects )
		return;

	if ( !s_rDecals.GetInt() )
		return;

	IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( req.nEntity );

	if ( !pEntity )
	{
		// Stock's own diagnostic, from c_te_bspdecal.cpp. Verbosity 1 rather
		// than a Warning: an entity that has left the PVS between the send and
		// the arrival is ordinary, not a defect.
		DevMsg( 1, "SourceSharp decal: no entity %i\n", req.nEntity );
		return;
	}

	const Vector vecPosition( req.vecPosition.x, req.vecPosition.y, req.vecPosition.z );
	const Vector vecSAxis   ( req.vecSAxis.x,    req.vecSAxis.y,    req.vecSAxis.z    );

	effects->DecalShoot( req.nDecalIndex, req.nEntity,
	                     pEntity->GetModel(), pEntity->GetAbsOrigin(), pEntity->GetAbsAngles(),
	                     vecPosition,
	                     req.bHasSAxis ? &vecSAxis : NULL,
	                     req.nFlags );
}

//=============================================================================//
// WHAT LEFT WITH c_te_playerdecal.cpp AND HAD TO BE CAUGHT.
//
// C-Phase 3 evicts that file, and two things in it were nobody's temp entity:
// a file-static ConVar and a client-effect precache list. Both are read by the
// backend below, so both live here now -- next to the code that reads them, and
// in a file that OUTLIVES the port. shell_te_fwd_cd.cpp would have been the
// wrong home: Part 7 deletes every forward file with c_te.cpp, and these two
// have to survive that.
//
// cl_spraydisable was read here through a ConVarRef, then declared here as a
// native ConVar when c_te_playerdecal.cpp -- which owned it as a file-static --
// left the project. It is a MANAGED declaration now
// (SourceSharp.Client/ConVars.cs) and a by-name ref here, which is the same
// three-line shape the thirty-six precipitation knobs took: a user setting's
// name, default, flags and help text are the setting, and while they sit in C++
// no managed code can read them or list them.
//
// FCVAR_ARCHIVE is stock's and is carried: it is a user setting and it has to
// persist. The default is stock's own #else branch -- TF_CLIENT_DLL is
// undefined in this build (plan 2G is the same finding for HL2_DLL and TF_DLL),
// so sprays are ENABLED by default here, as they were before the eviction.
//=============================================================================//
static CManagedConVarRef cl_spraydisable( "cl_spraydisable" );

//-----------------------------------------------------------------------------
// The sixty-four spray logo materials, precached.
//
// WITHOUT THIS THE SPRAY STILL DRAWS, which is exactly why it is easy to lose:
// materials->FindMaterial loads on demand, so the only symptom of dropping the
// list is a hitch on the first spray of each level and a material that was not
// there when the level's precache pass ran. A capability that degrades silently
// is the shape this port is scored against.
//
// EVERY ONE OF THE SIXTY-FOUR, because stock's guard is
// `#if !defined(HL2_DLL) || defined(HL2MP)` around logos 02..64 and HL2_DLL is
// undefined in this build (plan 2G) -- so stock registers all of them here too.
//
// GENERATED RATHER THAN PASTED. Stock's list is hand-typed and repeats
// playerlogo40..49 twice (c_te_playerdecal.cpp:83-92 duplicate :73-82), which
// is a stock bug and not a thing to reproduce: CLIENTEFFECT_MATERIAL twice on
// one name precaches it twice. The set below is the same sixty-four names with
// each appearing once.
//-----------------------------------------------------------------------------
#ifndef _XBOX
CLIENTEFFECT_REGISTER_BEGIN( PrecachePlayerDecal )
CLIENTEFFECT_MATERIAL( "decals/playerlogo01" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo02" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo03" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo04" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo05" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo06" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo07" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo08" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo09" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo10" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo11" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo12" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo13" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo14" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo15" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo16" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo17" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo18" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo19" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo20" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo21" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo22" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo23" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo24" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo25" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo26" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo27" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo28" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo29" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo30" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo31" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo32" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo33" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo34" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo35" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo36" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo37" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo38" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo39" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo40" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo41" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo42" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo43" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo44" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo45" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo46" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo47" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo48" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo49" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo50" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo51" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo52" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo53" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo54" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo55" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo56" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo57" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo58" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo59" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo60" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo61" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo62" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo63" )
CLIENTEFFECT_MATERIAL( "decals/playerlogo64" )
CLIENTEFFECT_REGISTER_END()
#endif

//-----------------------------------------------------------------------------
// The material for one player's spray, or NULL if there is not one yet.
//
// c_te_playerdecal.cpp's CreateTempMaterialForPlayerLogo, kept here rather than
// called there, and the reason is the same one shell_te_fwd_c1.cpp gives for
// going the other way: C-Phase 3 EVICTS c_te_playerdecal.cpp, and a backend that
// called into it would stop existing at exactly the moment its managed caller
// arrived. What a backend must not do is depend on the file it replaces.
//
// Everything in it is a pointer type that cannot cross the ABI — a player_info_t
// out of the engine, a custom-file hash, a download check against the
// filesystem, a FindMaterial and a texture-variable swap — which is why the
// managed half of a player decal is three numbers and this is the rest.
//-----------------------------------------------------------------------------
static IMaterial *SourceSharpTe_PlayerLogoMaterial( int iPlayerIndex, player_info_t *pInfo,
                                                    char *pTexName, int nChars )
{
	if ( !pInfo->customFiles[0] )
		return NULL;

	IMaterial *pLogo = materials->FindMaterial( VarArgs( "decals/playerlogo%2.2d", iPlayerIndex ),
	                                            TEXTURE_GROUP_DECAL );

	if ( IsErrorMaterial( pLogo ) )
		return NULL;

	char szLogoHex[ 16 ];
	Q_binarytohex( (byte *)&pInfo->customFiles[0], sizeof( pInfo->customFiles[0] ),
	               szLogoHex, sizeof( szLogoHex ) );

	// Downloaded yet?
	Q_snprintf( pTexName, nChars, "temp/%s", szLogoHex );

	char szFullTexName[ 512 ];
	Q_snprintf( szFullTexName, sizeof( szFullTexName ), "materials/temp/%s.vtf", szLogoHex );

	if ( !filesystem->FileExists( szFullTexName ) )
	{
		char szCustomName[ 512 ];
		Q_snprintf( szCustomName, sizeof( szCustomName ),
		            "download/user_custom/%c%c/%s.dat", szLogoHex[0], szLogoHex[1], szLogoHex );

		if ( !filesystem->FileExists( szCustomName ) )
			return NULL;   // not downloaded yet

		// The material system can only reach materials/*.vtf, so it is copied
		// out of the download folder rather than read where it landed.
		if ( !engine->CopyLocalFile( szCustomName, szFullTexName ) )
			return NULL;
	}

	return pLogo;
}

//-----------------------------------------------------------------------------
// A player's spray logo. c_te_playerdecal.cpp.
//
// cl_spraydisable is a by-name ref a few hundred lines above, into a
// declaration managed code owns. It was a file-static inside
// c_te_playerdecal.cpp, which C-Phase 3 evicts; see the block that refs it for
// where the declaration went and why it did not land in a forward file.
//-----------------------------------------------------------------------------
void SourceSharp_ClientPlayerDecalShoot( const float *pPositionXYZ,
                                         int32_t nPlayer, int32_t nEntity )
{
	const PlayerDecalRequest req = PlayerDecalShoot( pPositionXYZ, nPlayer, nEntity );

	if ( !req.bDraw || !effects )
		return;

	if ( !s_rDecals.GetInt() )
		return;

	if ( cl_spraydisable.GetBool() )
		return;

	IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( req.nEntity );

	if ( !pEntity )
		return;

	player_info_t info;

	if ( !engine->GetPlayerInfo( req.nPlayer, &info ) )
		return;

	char szTexName[ 512 ];
	IMaterial *pLogo = SourceSharpTe_PlayerLogoMaterial( req.nPlayer, &info,
	                                                     szTexName, sizeof( szTexName ) );

	if ( !pLogo )
		return;

	ITexture *pTexture = materials->FindTexture( szTexName, TEXTURE_GROUP_DECAL );

	if ( IsErrorTexture( pTexture ) )
		return;

	// Point the material at this player's texture, if it is not already.
	bool bFound = false;
	IMaterialVar *pMatVar = pLogo->FindVar( "$basetexture", &bFound );

	if ( bFound && pMatVar && pMatVar->GetTextureValue() != pTexture )
	{
		pMatVar->SetTextureValue( pTexture );
		pLogo->RefreshPreservingMaterialVars();
	}

	const color32 rgbaColour = { 255, 255, 255, 255 };

	effects->PlayerDecalShoot( pLogo,
	                           (void *)(intp)req.nPlayer,
	                           req.nEntity,
	                           pEntity->GetModel(),
	                           pEntity->GetAbsOrigin(),
	                           pEntity->GetAbsAngles(),
	                           Vector( req.vecPosition.x, req.vecPosition.y, req.vecPosition.z ),
	                           0,
	                           0,
	                           rgbaColour );
}

//-----------------------------------------------------------------------------
// A decal's index from its name, or -1 for a name the engine does not know.
//
// The one slot in this group that ANSWERS rather than draws. A received temp
// entity never needs it — the wire carries the index — but a client MAKING a
// decal for itself does, which is how the tool-playback paths in
// c_te_decal.cpp and c_te_worlddecal.cpp get from a recorded name back to an
// index.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientDecalIndexFromName( const char *pName )
{
	if ( !pName || !pName[0] || !effects )
		return -1;

	return effects->Draw_DecalIndexFromName( (char *)pName );
}

//-----------------------------------------------------------------------------
// A decal on a STATIC PROP. c_te_decal.cpp and c_te_projecteddecal.cpp both end
// here, which is the whole reason there is one slot rather than two.
//
// THIS IS THE ARM THAT ALWAYS WORKED. Ledger rows C2 and C3 are about the OTHER
// arm of each of those two functions -- the one that reaches
// C_BaseEntity::AddDecal, which this client's parallel hierarchy has no version
// of. The static prop arm never had an entity in it: it goes straight from two
// world-space points and a prop index into the engine's own static prop
// renderer, so it draws today and it draws through here. Neither row is closed
// by this slot and neither is made to look closed; what changed is that the
// arm is now reachable from managed code, which is what let the two classes
// move at all.
//
// THE TRACE IS OUT-ONLY. Stock declares a bare `trace_t tr;` at both call sites
// and hands it in uninitialised, and IStaticPropMgrClient's own comment on the
// member says it "returns point of decal in trace_t". So nothing about the
// trace that selected the prop needs to cross this boundary, and a local is the
// whole of it.
//
// bDoTrace is false here because it is false at both of stock's call sites.
//-----------------------------------------------------------------------------
void SourceSharp_ClientDecalStaticProp( const float *pStartXYZ, const float *pEndXYZ,
                                        int32_t nStaticPropIndex, int32_t nDecalIndex )
{
	const StaticPropDecalRequest req = DecalStaticProp( pStartXYZ, pEndXYZ,
	                                                    nStaticPropIndex, nDecalIndex );

	if ( !req.bDraw || !staticpropmgr )
		return;

	// r_decals is NOT checked here, and that is stock's behaviour rather than
	// an omission: neither TE_Decal nor TE_ProjectDecal tests it before this
	// call. ClientDecalShoot checks it because all three of ITS stock callers
	// do; this one has none that do.
	const Vector vecStart( req.vecStart.x, req.vecStart.y, req.vecStart.z );
	const Vector vecEnd  ( req.vecEnd.x,   req.vecEnd.y,   req.vecEnd.z   );

	trace_t tr;
	staticpropmgr->AddDecalToStaticProp( vecStart, vecEnd,
	                                     req.nStaticPropIndex, req.nDecalIndex,
	                                     false, tr );
}

//-----------------------------------------------------------------------------
// Whether this client is running with gore turned down.
//
// UTIL_IsLowViolence is SHARED code (util_shared.cpp:1258), so this is that
// question reaching this side's convars and this side's engine, not a second
// implementation of it. Four of its five terms are convars C# could have read
// through FindConVarValue; the fifth is engine->IsLowViolence(), which managed
// code cannot reach at all -- and stock's own comment says the four are legacy
// and that "the engine is the final arbiter of violence settings". A managed
// copy would have been the four that do not decide anything and a missing fifth
// that does.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientIsLowViolence( void )
{
	return UTIL_IsLowViolence() ? 1 : 0;
}

//-----------------------------------------------------------------------------
// A decal's NAME from its index, into the caller's buffer.
//
// THE GAME'S TABLE, NOT THE ENGINE'S, and it is not the inverse of
// ClientDecalIndexFromName above. That one asks effects->Draw_DecalIndexFromName
// -- the ENGINE's precached decal table. This one asks decalsystem, which is
// CDecalEmitterSystem in game/shared/decals.cpp, loaded from
// scripts/decals_subrect.txt and keyed on each entry's precache_index. Stock
// uses each of them in its own place and c_te_worlddecal.cpp's low-violence
// test reads THIS one, so this slot reads it too.
//
// An index the table does not hold answers "" (decals.cpp:161), and CopyName
// turns that into a length of zero rather than into an empty name -- the
// caller's next step is a substring test for "blood".
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientDecalNameFromIndex( int32_t nDecalIndex, char *pOut, int32_t nOutSize )
{
	if ( !decalsystem )
		return CopyName( NULL, pOut, nOutSize );

	return CopyName( decalsystem->GetDecalNameForIndex( nDecalIndex ), pOut, nOutSize );
}



//=============================================================================//
// C-PHASE 3 LANE DECALS. BEHAVIOUR LEDGER ROWS C2 AND C3, CLOSED.
//
// Both rows named one member: C_BaseEntity::AddDecal (c_baseentity.cpp:3453).
// Its two arms are AddStudioDecal and AddBrushModelDecal, and this client's
// parallel hierarchy declared neither -- C_StandaloneEntity has no decal path
// at all -- so a decal aimed at an entity, or at the world with no hitbox, was
// dropped. Silently before the port; once per level with the row's name in it
// after lane CX; drawn now.
//
// THE SWITCH IS NOT HERE. AddDecal's `switch ( modelType )` lives in C#, over
// ClientEntityModelType below, and these two functions are its arms. That is
// the same argument lane CX wrote against a trace-and-decal-in-one slot: a
// branch that lives in its backend is a branch no scanner finds and no ledger
// row can name, and this switch has a THIRD arm -- a sprite, or a model that
// is neither -- that draws nothing at all and is worth being able to say.
//
// doTrace IS FALSE AT BOTH OF STOCK'S CALL SITES, so neither of these takes it
// and no trace_t crosses in either direction. With it false the whole of
// AddStudioDecal above `CreateModelInstance()` and the whole of
// AddBrushModelDecal above its one call are dead, and what is left is what is
// written out below.
//=============================================================================//

//-----------------------------------------------------------------------------
// What a client entity's model IS. modelinfo->GetModelType of whatever entity
// nEntity resolves to, as a SourceSharpModelType; -1 for no such entity.
//
// -1 IS NOT mod_bad AND THE DIFFERENCE IS STOCK'S. TE_Decal's line above the
// call is `if ( ( ent = cl_entitylist->GetEnt( entity ) ) == NULL ) return;` (stock's spelling),
// and a caller that could not tell "no entity" from "an entity whose model is
// nothing" would have no way to reproduce it. SourceSharp_ModelType answers -1
// for its own "no such index" for the same reason.
//
// GetClientEntity AND NOT GetEnt, which is ClientDecalShoot's lookup and is
// strictly more than stock reaches from here: GetEnt filters to C_BaseEntity,
// so in this build a managed entity answers NULL to it, and stock's own line
// would drop a decal aimed at one before the model type was ever asked for.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientEntityModelType( int32_t nEntity )
{
	if ( nEntity < 0 || !modelinfo )
		return -1;

	IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( nEntity );

	if ( !pEntity )
		return -1;

	const model_t *pModel = pEntity->GetModel();

	if ( !pModel )
		return -1;

	// PASSED THROUGH RATHER THAN TRANSLATED, for SourceSharp_ModelType's
	// reason: SourceSharpModelType is written to modtype_t's numbering, and a
	// switch here would be a second table to keep in step whose unknown value
	// falls out as mod_bad -- which would send a studio model down the brush
	// arm and paint a decal through the wrong subsystem.
	return (int32_t)modelinfo->GetModelType( pModel );
}

//-----------------------------------------------------------------------------
// A decal on a STUDIO model. C_BaseEntity::AddStudioDecal, doTrace false.
//
// r_drawmodeldecals is referenced rather than redeclared, and a second ConVar of
// the same name here would be a duplicate registration. WHAT IT IS A REFERENCE
// TO HAS CHANGED TWICE. It was c_baseentity.cpp's own ConVar; the flip moved
// that file out, so lane `f-block` moved the DEFINITION to
// shell_entitystatics.cpp and this bound there through an `extern ConVar`.
//
// PHASE 1c FOUND THAT BOTH OF THOSE WERE DUPLICATES. The ENGINE declares this
// name itself (`strings -a` finds it in engine.so) and reads it for static
// props -- and the engine's
// constructor runs before the client DLL's, so the engine's object is the one
// the console and every ConVarRef have always bound to. The game DLL's copy
// was never the toggle anybody was setting. It is deleted, and this reader
// resolves the engine's BY NAME.
//
// LAZILY, and that is the reason for CManagedConVarRef rather than a plain
// file-scope ConVarRef: a file-scope ConVarRef resolves in its constructor, at
// DLL load, and this class re-tries until the name is there. The engine's is
// there from the start, so the re-try never fires -- but a ref that caches a
// miss is a silent permanent zero, and that is not a failure mode worth
// leaving available in a decal path.
//
// It is stock's gate on this arm and it is not r_decals: this path tests one
// and never the other, which is the mirror of the asymmetry
// SourceSharp_ClientDecalStaticProp records above.
//
// CreateModelInstance() then GetModelInstance(), both off IClientRenderable,
// which is what lets this reach the managed hierarchy: C_StandaloneEntity
// implements both, and modelrender keeps a model's decals per INSTANCE, so an
// entity with no instance has nowhere to put one.
//
// GetBody() rather than GetStudioBody(). They are the same number on every
// class in this build -- C_BaseEntity answers 0 from both, C_BaseAnimating
// m_nBody from both -- and only the first is on the interface an entity from
// either hierarchy is reached through.
//-----------------------------------------------------------------------------
static CManagedConVarRef r_drawmodeldecals( "r_drawmodeldecals" );

void SourceSharp_ClientDecalStudioModel( int32_t nEntity,
                                         const float *pRayStartXYZ, const float *pRayEndXYZ,
                                         int32_t nDecalIndex )
{
	const StudioDecalRequest req = DecalStudioModel( nEntity, pRayStartXYZ, pRayEndXYZ,
	                                                 nDecalIndex );

	if ( !req.bDraw || !modelrender )
		return;

	IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( req.nEntity );

	if ( !pEntity )
		return;

	// Stock's own order: the trace first (there is none here), then this test,
	// then the instance. The comment on it is "Exit out after doing the trace
	// so any other effects that want to happen can happen".
	if ( !r_drawmodeldecals.GetBool() )
		return;

	pEntity->CreateModelInstance();

	const Vec3 vecDelta = BloatedRayDelta( req.vecStart, req.vecEnd );

	Ray_t ray;
	ray.Init( Vector( req.vecStart.x, req.vecStart.y, req.vecStart.z ),
	          Vector( req.vecEnd.x,   req.vecEnd.y,   req.vecEnd.z   ) );

	// `ray.m_Delta *= 1.1f`, computed where a test can read it. Assigned rather
	// than multiplied here so that the live path and te_backend_args_test.cpp
	// are running the same arithmetic and not two spellings of it.
	ray.m_Delta.x = vecDelta.x;
	ray.m_Delta.y = vecDelta.y;
	ray.m_Delta.z = vecDelta.z;

	// FIXME: Pass in decal up? -- stock's own comment, kept because the
	// question is still open in stock.
	const Vector up( 0.0f, 0.0f, 1.0f );

	modelrender->AddDecal( pEntity->GetModelInstance(), ray, up, req.nDecalIndex,
	                       pEntity->GetBody(), false, ADDDECAL_TO_ALL_LODS );
}

//-----------------------------------------------------------------------------
// A decal on a BRUSH model, the world included.
// C_BaseEntity::AddBrushModelDecal, doTrace false -- which is one call.
//
// r_decals IS NOT CHECKED, and that is the whole reason this is not
// SourceSharp_ClientDecalShoot with a shorter argument list. That function
// checks it because all three of ITS stock callers check it on the line before;
// AddBrushModelDecal has no such test and neither of the two temp entities that
// reach it has one either. A decal that stopped appearing when someone turned
// r_decals down would be a divergence visible on one machine and invisible
// everywhere it was looked for.
//
// The last two arguments to DecalShoot are stock's literal 0 and 0: no S axis,
// so the engine picks one, and no flags, so the decal is not permanent.
//-----------------------------------------------------------------------------
void SourceSharp_ClientDecalBrushModel( int32_t nEntity, const float *pDecalCenterXYZ,
                                        int32_t nDecalIndex )
{
	const BrushDecalRequest req = DecalBrushModel( nEntity, pDecalCenterXYZ, nDecalIndex );

	if ( !req.bDraw || !effects )
		return;

	IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( req.nEntity );

	if ( !pEntity )
		return;

	const Vector vecCenter( req.vecCenter.x, req.vecCenter.y, req.vecCenter.z );

	effects->DecalShoot( req.nDecalIndex, req.nEntity,
	                     pEntity->GetModel(), pEntity->GetAbsOrigin(), pEntity->GetAbsAngles(),
	                     vecCenter, 0, 0 );
}




//=============================================================================//
// TEMPORARY -- GATE 3 SCAFFOLDING. C-PHASE 3 DELETES THIS BLOCK.
//
// Fifteen of the sixteen backends above have no managed caller yet, on purpose
// (2E-bis: a c_te_*.cpp keeps its table name until its whole subtree is
// evicted). That leaves gate 3 with nothing to look at: a live join proves the
// slots did not BREAK anything, and proves nothing about whether they draw.
//
// So this fires one directly from the console. It is scaffolding and it says so
// -- when C-Phase 3 gives each slot its real managed caller, the effect is
// reachable by firing the temp entity and this command has nothing left to do.
//
// Msg, not DevMsg. DevMsg prints nothing without `developer 2`, which also
// turns on a heavy ABI trace -- an effect that never arrived and an effect
// whose diagnostic was suppressed read identically, and this tree has lost a
// reading to exactly that.
//
// MainViewOrigin/MainViewForward rather than the local player: this needs a
// place in front of the camera, and the render view has one whether or not a
// player entity has resolved yet.
//=============================================================================//

//=============================================================================//
// THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP).
//
// EIGHT SLOTS FOR SEVEN EFFECTS, and not one ClientParticleTeSpawn( kind,
// params ). The seven TEs on DT_TEParticleSystem share a base TABLE and nothing
// else: two of them are one FX_ call, two build a CTEParticleRenderer by hand,
// one runs a five-hundred-particle CSimpleEmitter of its own, and the explosion
// picks between two singletons and an overlay. A kind+params slot over that
// would be a switch statement over six unrelated bodies wearing a generic name.
//
// SPARKS NEEDS NO SLOT HERE. ClientSparks above is exactly its primitive
// already, which is the whole of what C_TESparks::PostDataUpdate did. That is
// why there are eight backends for seven effects rather than seven.
//
// THE BODIES ARE VALVE'S, MOVED NOT REWRITTEN. Each one below is the drawing
// half of the c_te_*.cpp this lane evicted, with the members it read off the
// class replaced by the arguments C# decoded. What is NOT here is each file's
// decode -- that went to SourceSharp.Client/TempEnts -- and each file's tool
// RECORDING, which is not carried; see docs/divergences.md.
//=============================================================================//

//-----------------------------------------------------------------------------
// (The ordinary and the underwater explosion left with lane c5-particles:
// fx_explosion.cpp's effects are C#, ExplosionEffects, and ClientTeExplosion
// calls them; the fireball's flash below is still drawn here.)
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The fireball's flash: one CExplosionOverlay, which is a CWarpOverlay that
// grows and fades over a tenth of a second. c_te_explosion.cpp, and the numbers
// are its.
//
// GATED IN C#, on TE_EXPLFLAG_NOFIREBALL and on not being underwater -- both of
// which are decisions, so neither is here. Activate() hands the overlay to the
// glow overlay list, which owns it from then on; there is nothing to release.
//-----------------------------------------------------------------------------
void SourceSharp_ClientExplosionFireball( const float *pOriginXYZ )
{
	if ( !pOriginXYZ )
		return;

	CExplosionOverlay *pOverlay = new CExplosionOverlay;

	if ( !pOverlay )
		return;

	pOverlay->m_flLifetime = 0;
	pOverlay->m_vPos       = Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	pOverlay->m_nSprites   = 1;

	pOverlay->m_vBaseColors[0].Init( 1.0f, 0.9f, 0.7f );

	pOverlay->m_Sprites[0].m_flHorzSize = 0.05f;
	pOverlay->m_Sprites[0].m_flVertSize = pOverlay->m_Sprites[0].m_flHorzSize * 0.5f;

	pOverlay->Activate();
}

//-----------------------------------------------------------------------------
// The funnel (c_te_largefunnel.cpp's CreateFunnel) is C#'s: LargeFunnel, lane
// c5-particles, called by ClientTeLargeFunnel.
//-----------------------------------------------------------------------------

// (The blood stream's and the debug line's backends, SourceSharp_ClientBloodStream
// and SourceSharp_ClientShowLine, left with lane c5-particles: TE_BloodStream,
// C_TEShowLine's dots and the CTEParticleRenderer they fed are C#, TeParticles.)

// (The dust puff's backend, SourceSharp_ClientDust, left with lane c5-particles:
// FX_Dust is C#, DustPuff, and ClientTeDust starts it directly.)

// (The gauss discharge's backend left with lane c5-particles: FX_GaussExplosion
// is C#, SparkEffects.GaussExplosion, and ClientTeGaussExplosion calls it.)

//-----------------------------------------------------------------------------
// The blood sprite temp entity's second effect, FX_Blood's puff, is C#'s
// (lane c5-particles: ClientTeBloodSprite calls ImpactSprays.Blood), so the
// ClientBloodPuff slot that reached the native FX_Blood is gone.
//-----------------------------------------------------------------------------


//=============================================================================//
// C-PHASE 3, LANE CE. One drawing primitive and one string-table read.
//
// The lane's other four temp entities needed nothing new here: CTESmoke and
// CTEMuzzleFlash end at ClientSmoke and ClientMuzzleFlash above,
// CTEEffectDispatch ends at the managed dispatch table (shell_effect_dispatch_client.cpp), and C_TEImpact's whole
// PostDataUpdate is a VPROF -- so its managed half draws nothing, because
// stock's draws nothing. CTEDynamicLight ends at DynamicLightUpdate in
// shell_standalone_client.cpp, which needed one FIELD rather than a slot.
//=============================================================================//

//-----------------------------------------------------------------------------
// The materials the shattered pane draws with, precached.
//
// MOVED HERE FROM c_te_glassshatter.cpp, which is evicted, and this file is
// where it has to land: c_impact_effects.cpp CACHES handles for these four
// (g_Mat_Fleck_Glass and g_Mat_Fleck_Tile, in FX_CacheMaterialHandles) but is
// NOT where they are precached -- its own CLIENTEFFECT_REGISTER block names ten
// other materials and none of these. So the evicted file was the only thing in
// the build asking for them at level load, and dropping it would have left
// GetPMaterial to fetch four uncached materials during a firefight.
//
// Lane CD moved c_te_playerdecal.cpp's sixty-four logo precaches here for the
// same reason; this file survives Part 7's deletion of c_te.cpp and the
// forwards do not.
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectGlassShatter )
CLIENTEFFECT_MATERIAL( "effects/fleck_glass1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_glass2" )
CLIENTEFFECT_MATERIAL( "effects/fleck_tile1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_tile2" )
CLIENTEFFECT_REGISTER_END()

//-----------------------------------------------------------------------------
// The EffectDispatch network string table, read backwards.
//
// c_te_effect_dispatch.cpp:153's own first line, and the whole of why one temp
// entity stands for fifty-two client effects: the wire carries an INDEX and the
// registry is keyed by NAME.
//
// AN EMPTY STRING IS "the table does not hold it", which is how stock's NULL
// return crosses a boundary that cannot carry one. Stock's PostDataUpdate does
// nothing at all for a NULL and the managed player does nothing at all for an
// empty string, so the two answer the same question the same way -- and neither
// dispatches the empty NAME, which would walk all fifty-two registrations and
// log about a name rather than about itself.
//
// g_StringTableEffectDispatch is NULL until the client has the table from the
// server, which is every frame before the first join. Guarded here rather than
// in managed code, because "no table yet" is this side's fact.
//-----------------------------------------------------------------------------
void SourceSharp_EffectDispatchName( int32_t nIndex, char *pOut, int32_t nOutBytes )
{
	if ( !pOut || nOutBytes <= 0 )
		return;

	pOut[0] = '\0';

	if ( nIndex < 0 || !g_StringTableEffectDispatch )
		return;

	const char *pName = g_StringTableEffectDispatch->GetString( nIndex );

	if ( !pName )
		return;

	Q_strncpy( pOut, pName, nOutBytes );
}


CON_COMMAND_F( cs_cl_te_backend,
               "TEMPORARY (C-Phase 2): fire one temp entity backend in front of the camera.",
               FCVAR_CLIENTDLL )
{
	if ( args.ArgC() < 2 )
	{
		Msg( "cs_cl_te_backend <sparks|metalsparks|ricochet|muzzleflash|smoke|splash|"
		     "decal|dispatch|"
		     "fireball|bloodstream|showline|dust>\n" );
		return;
	}

	const char *pWhich = args.Arg( 1 );

	const Vector vecForward = MainViewForward();
	const Vector vecAt      = MainViewOrigin() + vecForward * 96.0f;

	const float pAt[3]  = { vecAt.x, vecAt.y, vecAt.z };
	const float pDir[3] = { -vecForward.x, -vecForward.y, -vecForward.z };

	// A second point off to one side, so a beam has two ends rather than none.
	const Vector vecSide = vecAt + Vector( 0.0f, 0.0f, 64.0f );
	const float pSide[3] = { vecSide.x, vecSide.y, vecSide.z };

	const float pAngles[3] = { 0.0f, 0.0f, 0.0f };

	Msg( "[SourceSharp] cs_cl_te_backend %s at (%.0f %.0f %.0f)\n",
	     pWhich, pAt[0], pAt[1], pAt[2] );

	if ( !Q_stricmp( pWhich, "sparks" ) )
	{
		SourceSharp_ClientSparks( pAt, 4, 4, pDir );
	}
	else if ( !Q_stricmp( pWhich, "metalsparks" ) )
	{
		SourceSharp_ClientMetalSparks( pAt, pDir );
	}
	else if ( !Q_stricmp( pWhich, "ricochet" ) )
	{
		SourceSharp_ClientRicochet( pAt, pDir );
	}
	else if ( !Q_stricmp( pWhich, "muzzleflash" ) )
	{
		SourceSharp_ClientMuzzleFlash( pAt, pAngles, 1.0f, 0 );
	}
	else if ( !Q_stricmp( pWhich, "smoke" ) )
	{
		SourceSharp_ClientSmoke( pAt, 0, 10.0f, 4.0f );
	}
	else if ( !Q_stricmp( pWhich, "splash" ) )
	{
		SourceSharp_ClientEnergySplash( pAt, pDir, 1 );
	}
	else if ( !Q_stricmp( pWhich, "decal" ) )
	{
		// Both decal slots at once: the name lookup answers an index, and the
		// index is what the shoot takes. A -1 back means the engine has no
		// decal by that name, which is a different failure from a decal that
		// did not appear.
		const char *pName = ( args.ArgC() > 2 ) ? args.Arg( 2 ) : "Impact.Concrete";
		const int32_t nIndex = SourceSharp_ClientDecalIndexFromName( pName );

		Msg( "[SourceSharp]   decal '%s' -> index %d\n", pName, nIndex );

		// Entity 0 is the world, which is what a world decal asks for.
		SourceSharp_ClientDecalShoot( nIndex, 0, pAt, NULL, 0 );
	}
	else if ( !Q_stricmp( pWhich, "dispatch" ) )
	{
		const char *pName = ( args.ArgC() > 2 ) ? args.Arg( 2 ) : "Impact";

		// Through the dispatch every native caller takes, which resolves the
		// name in the managed table (shell_effect_dispatch_client.cpp). The
		// default CEffectData is stock's: scale 1, no entity.
		CEffectData effect;
		effect.m_vOrigin.Init( pAt[0], pAt[1], pAt[2] );
		effect.m_vNormal.Init( pDir[0], pDir[1], pDir[2] );
		DispatchEffectToCallback( pName, effect );
	}
	//-------------------------------------------------------------------------
	// C-Phase 3, lane CP's eight. Each fires the primitive ALONE, with no
	// decode and no decision in front of it, which is what makes this command
	// able to tell a broken backend from a broken managed player: if
	// `cs_cl_te_backend explosion` draws and a fired CTEExplosion does not, the
	// fault is on the managed side of the slot.
	//-------------------------------------------------------------------------
	else if ( !Q_stricmp( pWhich, "fireball" ) )
	{
		SourceSharp_ClientExplosionFireball( pAt );
	}
	// "bloodstream" and "showline" left with their backends (lane c5-particles:
	// TeParticles; cs_te_fire plays both through their temp entities).
	// "dust" left with its backend (lane c5-particles: the puff is C#'s
	// DustPuff; cs_te_fire dust plays it through the temp entity).
	else
	{
		// The beams are managed: cs_te_fire beam* fires them through their
		// temp entities, the path they take in a game.
		Msg( "[SourceSharp] cs_cl_te_backend: no backend called '%s'\n", pWhich );
	}
}
