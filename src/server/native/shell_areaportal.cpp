//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the engine's half of an area portal ================//
// SS_REASON a  a vtable the engine calls through
//
// An area portal is barely an entity. vbsp cuts the brush out of the world,
// numbers the hole, and hands the number back as a keyvalue; by the time the
// game sees one there is no brush left, which is why func_areaportal.cpp opens
// by saying so. What survives to runtime is A NUMBER and a decision about
// whether the hole is open.
//
// The decision is C#'s and lives in FuncAreaPortal.cs. This file is the part
// that cannot be: membership of g_AreaPortals.
//
// That list is walked by CServerGameClients::ClientSetupVisibility EVERY FRAME
// FOR EVERY CLIENT. For each portal in it the engine asks UpdateVisibility
// whether the viewer is behind the portal's plane, flows through the ones that
// are open to decide which areas to send, and sets a BIT PER OPEN PORTAL in the
// array the client then culls by. The last of those is the reason a managed
// entity cannot simply call engine->SetAreaPortalState and stay out of the
// list: a portal that is not walked never gets its bit set, the client reads
// the area beyond it as closed, and the room stops drawing. The failure is not
// a lost optimisation — it is a hole in the world.
//
// So this object joins the list on the managed entity's behalf. It holds only
// what C# told it, and the arithmetic it does is the engine's own: the backface
// test that used to be CFuncAreaPortalBase's, and for a window the distance
// test that decides how far a pane is worth seeing through. Neither is a
// decision a mod would change, and both are asked per client per frame — which
// is exactly the shape of thing this project keeps native.
//
//-----------------------------------------------------------------------------
// IT IS NOT AN ENTITY ANY MORE (plan_entity.md step 4g)
//-----------------------------------------------------------------------------
//
// It used to be, and the reason was the LIST's type rather than anything the
// object did: `CUtlLinkedList<CFuncAreaPortalBase*, unsigned short>`, and
// CFuncAreaPortalBase is a CBaseEntity. shim_underive_verdict_test.cpp recorded
// that as a refusal with the condition for re-taking it written down — retype
// the list, since the walk touches only m_portalNumber, m_iPortalVersion and
// UpdateVisibility() — and fenced this lane out of func_areaportalbase.h
// because a step-3 lane owned it. plan_entity.md names shell_areaportal.cpp in
// 4g with no fence, and none of func_areaportalbase.cpp, func_areaportal.cpp or
// func_areaportalwindow.cpp is compiled any more, so the fence is gone with the
// files. The list is retyped in areaportal_list.h and the record is a struct.
//
// WHAT WENT, PER ITEM, so that "it compiles" is not the claim:
//
//   the base class          CFuncAreaPortalBase, and with it CBaseEntity's
//                           network property, collision property, datadesc,
//                           think list, touch links and save/restore — none of
//                           which a portal used. Its constructor's whole body
//                           was the list join, which is CAreaPortal's now, and
//                           its UpdateVisibility body is inlined below as the
//                           backface arm.
//   the factory             LINK_ENTITY_TO_CLASS( sourcesharp_areaportal ) and
//                           the CreateEntityByName that called it. A record is
//                           `new`ed.
//   the edict refusal       UpdateTransmitState()/EFL_SERVER_ONLY existed to
//                           tell the entity system not to give this thing an
//                           edict. Nothing gives a struct an edict.
//   the dynamic_cast        ResolvePortal() down-cast the entity the slot table
//                           returned. §3.16 family: `dynamic_cast`, server
//                           shim. The token resolves by index and serial now.
//   the SDK window arm      SourceSharp_AreaPortalSetWindow had an
//                           `#ifndef SS_CBASEENTITY_ALIAS` arm reading bounds
//                           off a stock CBaseEntity. DELETED, not guarded out:
//                           the only caller is FuncAreaPortalWindow.cs, which
//                           passes its OWN handle, so the arm resolved
//                           `ent.pStandalone` on every call this build has ever
//                           made. It was dead before it was removed.
//
// The DATADESC went too. It saved m_portalNumber and read "PortalVersion" as a
// keyfield — and neither has anything to save or parse here: this mod cannot
// save (§3.12), and the version arrives as an argument to AreaPortalCreate
// because FuncAreaPortalVars.PortalVersion is the managed key.
//
//=============================================================================//

#include "mathlib/vplane.h"
#include "eiface.h"
#include "areaportal_list.h"
#include "shell_standalone.h"
#include "shell_resolve.h"
#include "sourcesharp.h"

extern IVEngineServer *engine;


//-----------------------------------------------------------------------------
// THE LIST ITSELF, and it is the only thing in this file that is not a function.
//
// Defined here rather than in the header because there is exactly ONE of it,
// with a constructor, and a .cpp definition keeps its initialisation where the
// file's is. Every walk of it -- the per-client
// visibility pass in shell_gameclients.cpp, and the resolver in the header --
// reaches it through the `extern` declaration beside them.
//-----------------------------------------------------------------------------
std::list< CAreaPortal * > g_AreaPortals;
std::unordered_map< uint32_t, CAreaPortal * > g_AreaPortalsById;

//-----------------------------------------------------------------------------
// Whether the engine should flow through this portal for the viewer at vOrigin.
//
// Three tests, in the order stock's two classes applied them:
//
//   1. Closed by the map's own decision -- func_areaportal's state, which is
//      the only one of the three C# owns. A closed portal is closed on the
//      client too, and that is what makes a door hide the room behind it.
//   2. Past a window's fade distance, measured from the volume rather than from
//      its origin: a window is a pane a room wide and the nearest face is what
//      matters. Also closed on the client, since a pane you cannot see through
//      is a pane the far side need not be sent through.
//   3. The viewer is behind the portal's plane. NOT closed on the client -- see
//      the long comment in func_areaportalbase.h, which is about exactly this
//      and is worth reading before touching this function.
//-----------------------------------------------------------------------------
bool CAreaPortal::UpdateVisibility( const Vector &vOrigin, float fovDistanceAdjustFactor,
                                    bool &bIsOpenOnClient ) const
{
	// Tests 1 and 2 -- the map's own switch and a window's fade. Both are
	// arithmetic over what C# decided, both close the hole on the client as
	// well, and both are in the header where a test can drive them.
	if ( ClosedByManagedState( vOrigin, fovDistanceAdjustFactor, bIsOpenOnClient ) )
		return false;

	//-------------------------------------------------------------------------
	// The backface test, which was CFuncAreaPortalBase::UpdateVisibility's whole
	// body. Carried across verbatim rather than called, because there is no base
	// class left to call it on -- and it DELIBERATELY LEAVES bIsOpenOnClient
	// ALONE, which is the one thing about this function that is easy to break.
	//
	// STOCK OPENED WITH `if ( m_portalNumber == -1 ) return false;` AND THAT
	// TEST IS NOT HERE. It was reachable because CFuncAreaPortalBase's
	// constructor set -1 and a mapper's entity might never overwrite it. A
	// CAreaPortal takes its number as a constructor argument and
	// SourceSharp_AreaPortalCreate refuses to build one for a negative, so the
	// record cannot exist in that state. Removed rather than kept as a
	// belt-and-braces: a test that cannot fire is a test whose absence nobody
	// would notice if the invariant broke, which is worse than no test at all.
	//-------------------------------------------------------------------------

	// See if the viewer is on the backside.
	VPlane plane;
	if ( !engine->GetAreaPortalPlane( vOrigin, m_portalNumber, &plane ) )
		return true; // leave it open if there's an error here for some reason

	return ( plane.DistTo( vOrigin ) + AREAPORTAL_VIEWER_PADDING > 0 );
}

//-----------------------------------------------------------------------------
// Joins the list, at the number vbsp assigned.
//
// A portal number of -1 is what an entity created with ent_create has, and what
// a mapper's brush has if the compiler declined to make a hole out of it.
// Refused rather than registered, because a portal in the list with no number
// is asked about every frame and can only answer wrongly.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_AreaPortalCreate( int32_t nPortalNumber, int32_t nPortalVersion )
{
	if ( nPortalNumber < 0 )
		return 0;

	CAreaPortal *pPortal = new CAreaPortal( nPortalNumber, nPortalVersion );

	// Open until told otherwise, and told to the engine as well as remembered:
	// the engine's own state is whatever the last map left there.
	engine->SetAreaPortalState( nPortalNumber, 1 );

	return AreaPortalTokenOf( pPortal );
}

//-----------------------------------------------------------------------------
// Leaves the list, leaving the hole OPEN.
//
// Opening it first is not tidiness. SetAreaPortalStates is only called for
// portals still in the list, so a portal removed while shut stays shut in the
// engine with nobody left to reopen it -- and everything beyond it stops being
// sent for the rest of the map.
//-----------------------------------------------------------------------------
void SourceSharp_AreaPortalDestroy( uint32_t hPortal )
{
	CAreaPortal *pPortal = AreaPortalFromToken( hPortal );
	if ( !pPortal )
		return;

	engine->SetAreaPortalState( pPortal->m_portalNumber, 1 );

	// The destructor takes it out of the list. Immediate rather than deferred:
	// UTIL_Remove existed to keep an ENTITY alive until the end of the frame in
	// case something else in the frame still held it, and nothing holds a
	// portal record except the list and the token that just resolved.
	delete pPortal;
}

void SourceSharp_AreaPortalSetOpen( uint32_t hPortal, int32_t bOpen )
{
	CAreaPortal *pPortal = AreaPortalFromToken( hPortal );
	if ( !pPortal )
		return;

	pPortal->m_bOpen = ( bOpen != 0 );

	// Applied at once as well as remembered. The per-client pass will say the
	// same thing a frame from now, but a portal shut at spawn has to be shut
	// before the first client is set up rather than after it.
	engine->SetAreaPortalState( pPortal->m_portalNumber, bOpen ? 1 : 0 );
}

//-----------------------------------------------------------------------------
// Makes this portal a WINDOW, and names the volume its fade distance is
// measured from.
//
// ONE ARM, and that is a deletion rather than a narrowing. There used to be a
// second, `ent.pBase->CollisionProp()->WorldSpaceSurroundingBounds(...)`, for
// "a mapper pointing a window at a stock brush". The only caller of this slot
// is FuncAreaPortalWindow.cs and it passes its OWN handle — a managed entity,
// which SourceSharp_ResolveEntity answers on `pStandalone` — so the SDK arm
// resolved nothing on any call this build has ever made. It is gone, not
// guarded out.
//
// A fade distance of zero turns the window test off again and leaves a plain
// door portal.
//-----------------------------------------------------------------------------
void SourceSharp_AreaPortalSetWindow( uint32_t hPortal, uint32_t hEntity, float flFadeDist )
{
	CAreaPortal *pPortal = AreaPortalFromToken( hPortal );
	if ( !pPortal )
		return;

	pPortal->m_flFadeDist    = flFadeDist;
	pPortal->m_vecVolumeMins = vec3_origin;
	pPortal->m_vecVolumeMaxs = vec3_origin;

	bool bHaveVolume = false;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->WorldSpaceSurroundingBounds( &pPortal->m_vecVolumeMins,
		                                              &pPortal->m_vecVolumeMaxs );
		bHaveVolume = true;
	}

	// A fade distance with no volume to measure from cannot be applied, and a
	// window that closed on a box at the world origin would shut for every
	// player in the map. Left as a plain door portal instead, which is open.
	pPortal->m_bWindow = bHaveVolume && ( flFadeDist > 0.0f );
}

// cs_sv_areaportals, the area-portal list readout, is a diagnostic: it lives
// in shell_areaportal_debug.cpp, which only a debug build compiles.
