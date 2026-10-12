//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON e  a synchronous mid-step answer
//
// ToucherOf, out of line.
//
// toucher.h's other three helpers ask nothing of a CBaseEntity: they start from
// an IHandleEntity or a CBaseHandle and go looking. ToucherOf is the fast path
// for a caller that already holds the SDK entity, and its one line
//
//     Toucher_t toucher = { pEntity, pEntity, NULL };
//
// initialises an IHandleEntity * from a CBaseEntity *. That conversion is real
// today -- a CBaseEntity is an IServerEntity is an IHandleEntity -- and it is
// exactly what Phase 13's typedef takes away: IGameEntity is not an
// IHandleEntity, and nothing on the parallel hierarchy says it is. So this is
// not a header being greedy with an inline body; it is a statement about what a
// CBaseEntity IS, and the typedef is where it gets answered, not here.
//
// What moving it out of line buys is that the statement is made ONCE. toucher.h
// is included by 26 of the server's translation units, and while it was inline
// every one of them made it. Three of those -- shell_motioncontrollers.cpp,
// shell_standalone_hierarchy.cpp and standalone_relays.cpp, all of them
// SourceSharp's own code and all of them files the port keeps -- had no other
// ask of any kind, so this single initialiser was their whole failure under the
// Phase 13 survey.
//
// Deliberately not in gameinterface.cpp, where two of the three call sites are:
// the third is physicsowner.h, and a function declared in toucher.h belongs in
// toucher.cpp.
//
//=============================================================================//
#include "toucher.h"


Toucher_t ToucherOf( IGameEntity *pEntity )
{
	// THE TYPEDEF ANSWERING THE QUESTION THIS FILE WAS OPENED TO ASK, and the
	// answer is NOT a route. Armed, CBaseEntity IS IGameEntity, and
	// igameentity.h keeps that interface off IHandleEntity deliberately -- so
	// there is no pHandle to be had from this argument and none is invented.
	// The refusal is the same one entitylist.{h,cpp} records for the same
	// conversion; what makes THIS site answerable rather than merely refused is
	// that the OTHER two fields have a correct value here.
	//
	// pBase is the SDK entity and armed there is no such thing -- the class is
	// an alias for the boundary interface, so a "CBaseEntity" over there is an
	// entity outside the SDK and belongs on pGame. That keeps IsValid() true,
	// which is what every reader of a Toucher_t tests, and it keeps
	// gameinterface.cpp's symmetry intact: `toucher.pGame->Touch( other )`
	// still fires for both sides of a pair.
	//
	// WHAT IS LOST, stated rather than hidden: GetRefEHandle() answers an
	// invalid CBaseHandle for a toucher built this way, because pHandle is the
	// only field it can read. Nothing in the tree calls it on the result of
	// ToucherOf today -- the two gameinterface.cpp call sites read pGame and
	// IsValid(), and PhysicsOwner_t::AsToucher's callers do the same -- and a
	// link that needs to REMEMBER a toucher across frames builds it from
	// ToucherFromEntityHandle or ToucherFromHandle, both of which start at an
	// IHandleEntity and keep it.
	Toucher_t toucher = { NULL, NULL, pEntity };
	return toucher;
}
