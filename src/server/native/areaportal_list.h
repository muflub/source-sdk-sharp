//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the engine's area-portal list, without an entity ===//
//
// WHAT THIS REPLACES. Until plan_entity.md step 4g the shim's area portal was
// `CSourceSharpAreaPortal : CFuncAreaPortalBase : CBaseEntity` — a whole SDK
// entity (network property, collision property, datadesc, think list, touch
// links, save/restore) created through CreateEntityByName, for an object that
// used none of it. shim_underive_verdict_test.cpp recorded that as a REFUSAL
// with a named condition for re-taking it:
//
//     "The walk (gameinterface.cpp) touches exactly TWO things on each
//      element: m_portalNumber and UpdateVisibility(). Nothing about
//      CBaseEntity. So the un-derivation is cheap — retype g_AreaPortals to an
//      interface carrying those two — but it is a change to
//      func_areaportalbase.h, an ABOVE-CLIFF SDK OBJECT owned by a step-3
//      lane. This lane is fenced out of it."
//
// plan_entity.md gives `shell_areaportal.cpp` to 4g by name, with no fence, and
// func_areaportalbase.cpp / func_areaportal.cpp / func_areaportalwindow.cpp are
// no longer compiled at all. So the verdict is TAKEN rather than carried: the
// list is retyped here, to a record that is not an entity and never was.
//
// WHY A LIST AT ALL, AND WHY IT IS NOT MANAGED. The decision a func_areaportal
// makes — open or shut — is C#'s, and it is made in FuncAreaPortal.cs. What
// cannot be C#'s is MEMBERSHIP of this list. CServerGameClients::
// ClientSetupVisibility walks it once per client per frame; for each element it
// asks whether the viewer is behind the portal's plane, flows through the open
// ones to decide which areas to send, and sets A BIT PER OPEN PORTAL in the
// array the client culls by. A portal that is never walked never gets its bit
// set, the client reads the area beyond it as closed, and the room stops
// drawing. That is a hole in the world, not a lost optimisation.
//
// The two pieces of arithmetic the record does — the backface test and a
// window's distance-to-volume fade — are the ENGINE's own, asked per client per
// frame, and neither is a decision a mod would change. §6's shape exactly.
//
// WHAT IS NO LONGER HERE, and it is the point of the file: no vtable, no
// factory, no classname, no edict, no datadesc, no handle. A portal is 40-odd
// bytes and a list link.
//
//=============================================================================//
#ifndef SOURCESHARP_AREAPORTAL_LIST_H
#define SOURCESHARP_AREAPORTAL_LIST_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>
#include "mathlib/vector.h"
// CalcDistanceToAABB, for a window's fade distance. mathlib rather than
// CCollisionProperty::CalcDistanceFromPoint because the box is already in world
// space and a window is drawn axis-aligned -- the collision property's version
// would rotate a box that never rotates.
#include "mathlib/mathlib.h"

#include <list>
#include <unordered_map>

//-----------------------------------------------------------------------------
// A sphere around the viewer, so a portal does not snap shut the instant they
// cross its plane. CFuncAreaPortalBase's own constant, carried across with the
// body that used it.
//-----------------------------------------------------------------------------
#define AREAPORTAL_VIEWER_PADDING 80

//-----------------------------------------------------------------------------
// The server keeps sending through a window a little past the point it goes
// opaque, so that net lag does not show as popping. CFuncAreaPortalWindow's
// constant, here for the same reason.
//-----------------------------------------------------------------------------
#define AREAPORTAL_FADE_DIST_BUFFER 10

//-----------------------------------------------------------------------------
// One hole in the world, as far as the engine is concerned.
//
// PLAIN, NOT VIRTUAL. The walk called UpdateVisibility() through a vtable
// because stock had two portal classes — a door and a window — and dispatched
// between them. There is one class now and the window is a FLAG on it, which is
// what the managed side was already saying: FuncAreaPortalWindow.cs creates a
// portal with AreaPortalCreate and then turns it into a window with
// AreaPortalSetWindow. Two shapes of the same record, so one record.
//
// WHY MOST OF IT IS INLINE HERE AND ONE METHOD IS NOT. Everything except the
// backface test is arithmetic over state C# handed down, with no engine in it,
// and areaportal_list_test.cpp drives the real bodies rather than a stand-in --
// including a stale token after the next portal is made, which is the kind of
// bug that is invisible until a map cycles. UpdateVisibility asks the engine
// for the portal's plane, so it stays in shell_areaportal.cpp where `engine` is.
//-----------------------------------------------------------------------------
class CAreaPortal
{
public:
	CAreaPortal( int32_t nPortalNumber, int32_t nPortalVersion );
	~CAreaPortal();

	//-------------------------------------------------------------------------
	// Whether the engine should flow through this portal for the viewer at
	// vOrigin, and — separately — whether the CLIENT should be told it is open.
	//
	// bIsOpenOnClient is usually the same as the return value and deliberately
	// NOT always: the backface test closes a portal for flow purposes while the
	// client can still see through the other dareaportal_t of the same key.
	// func_areaportalbase.h carries the illustration; the rule is that the
	// backface arm leaves bIsOpenOnClient alone and the other two do not.
	//-------------------------------------------------------------------------
	bool UpdateVisibility( const Vector &vOrigin, float fovDistanceAdjustFactor,
	                       bool &bIsOpenOnClient ) const;

	//-------------------------------------------------------------------------
	// The two arms of UpdateVisibility that read what C# decided, split out
	// because they are the half with no engine in it.
	//
	// Returns TRUE when the portal is shut for this viewer on that evidence
	// alone, and in that case bIsOpenOnClient has been set false: both of these
	// arms close the hole on the CLIENT as well, which is what makes a shut
	// door hide the room and a fully faded window stop being drawn through.
	// The arm that does NOT is the backface test, and it is not here.
	//-------------------------------------------------------------------------
	bool ClosedByManagedState( const Vector &vOrigin, float fovDistanceAdjustFactor,
	                           bool &bIsOpenOnClient ) const
	{
		if ( !m_bOpen )
		{
			bIsOpenOnClient = false;
			return true;
		}

		if ( m_bWindow )
		{
			// The distance to the nearest FACE, not to the origin. This is what
			// CCollisionProperty::CalcDistanceFromPoint answers, spelled with
			// the engine's own helper because the box is already in world space
			// and a window is drawn axis-aligned.
			float flDist = CalcDistanceToAABB( m_vecVolumeMins, m_vecVolumeMaxs, vOrigin );

			flDist *= fovDistanceAdjustFactor;

			if ( flDist > ( m_flFadeDist + AREAPORTAL_FADE_DIST_BUFFER ) )
			{
				bIsOpenOnClient = false;
				return true;
			}
		}

		return false;
	}

	// This matches two dareaportal_t::m_PortalKeys. Never negative for a record
	// that exists: SourceSharp_AreaPortalCreate refuses to make one otherwise.
	int32_t  m_portalNumber;

	// Version 0 — shipping Half-Life 2 era — is always reported OPEN to the
	// client whatever the backface test says. The walk special-cases it.
	int32_t  m_iPortalVersion;

	// What C# decided. A door portal has only the first; a window has the rest.
	bool     m_bOpen;
	bool     m_bWindow;
	float    m_flFadeDist;

	// The box a window's fade distance is measured from, in world space.
	//
	// COPIED rather than reached for through a handle, and that is a decision.
	// The alternative is holding the owning entity and asking its collision
	// property per client per frame, which would track a window that MOVES —
	// and none does: a func_areaportalwindow is a pane cut out of the world by
	// the compiler, in the same sense the portal number is.
	Vector   m_vecVolumeMins;
	Vector   m_vecVolumeMaxs;

	// The token C# holds for this record. See NextAreaPortalToken below.
	uint32_t Token() const { return m_nToken; }

private:
	uint32_t                             m_nToken;
	std::list< CAreaPortal * >::iterator m_itList;   // this record's link in g_AreaPortals
};

//-----------------------------------------------------------------------------
// The list the per-client visibility pass walks, in creation order, and the
// tokens C# holds. Both defined in shell_areaportal.cpp. A record joins both in
// its constructor and leaves both in its destructor; a std::list erase is O(1)
// through the record's own iterator and moves no other element.
//-----------------------------------------------------------------------------
extern std::list< CAreaPortal * > g_AreaPortals;
extern std::unordered_map< uint32_t, CAreaPortal * > g_AreaPortalsById;

//-----------------------------------------------------------------------------
// The token C# holds: an id from a counter that only rises, NEVER REUSED.
//
// A removed portal's token therefore names nothing, whatever is created after
// it -- a stale token resolves to NULL, never to somebody else's hole, which
// is the property a slot index needed a serial for. 0 is never a token: it is
// what FuncAreaPortal.cs tests for "no portal" and what AreaPortalCreate
// returns when there is no hole to make. The counter would wrap after 2^32
// portals, which no process lives to see; the arithmetic skips 0 there anyway.
//
// SPLIT IN TWO so the arithmetic can be checked: the counter is a static, and
// a static in a gtest binary carries whatever the tests before it left, so
// "the first token is not 0" is taken over AreaPortalTokenAfter instead.
//-----------------------------------------------------------------------------
inline uint32_t AreaPortalTokenAfter( uint32_t nToken )
{
	return ( nToken == 0xFFFFFFFFu ) ? 1u : nToken + 1u;
}

inline uint32_t NextAreaPortalToken()
{
	static uint32_t s_nLast = 0;
	s_nLast = AreaPortalTokenAfter( s_nLast );
	return s_nLast;
}

inline CAreaPortal *AreaPortalFromToken( uint32_t hPortal )
{
	if ( hPortal == 0 )
		return NULL;

	std::unordered_map< uint32_t, CAreaPortal * >::const_iterator it = g_AreaPortalsById.find( hPortal );
	return ( it != g_AreaPortalsById.end() ) ? it->second : NULL;
}

inline CAreaPortal::CAreaPortal( int32_t nPortalNumber, int32_t nPortalVersion )
	: m_portalNumber( nPortalNumber ),
	  m_iPortalVersion( nPortalVersion ),
	  m_bOpen( true ),
	  m_bWindow( false ),
	  m_flFadeDist( 0.0f ),
	  m_vecVolumeMins( vec3_origin ),
	  m_vecVolumeMaxs( vec3_origin ),
	  m_nToken( NextAreaPortalToken() )
{
	m_itList = g_AreaPortals.insert( g_AreaPortals.end(), this );
	g_AreaPortalsById.emplace( m_nToken, this );
}

inline CAreaPortal::~CAreaPortal()
{
	g_AreaPortalsById.erase( m_nToken );
	g_AreaPortals.erase( m_itList );
}

//-----------------------------------------------------------------------------
// The token for a record that is already in the list.
//-----------------------------------------------------------------------------
inline uint32_t AreaPortalTokenOf( const CAreaPortal *pPortal )
{
	return pPortal->Token();
}

#endif // SOURCESHARP_AREAPORTAL_LIST_H
