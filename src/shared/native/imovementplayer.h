//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what the movement loop asks of a player ============//
//
// Phase 9b (plan_baseentity.md): retype IGameMovement off CBasePlayer, so the
// standalone player can be moved by the same loop the SDK player is. CMoveData
// needed nothing — its player is already a CBaseHandle and every trace in the
// loop filters on that handle, not on the pointer (a plan correction, recorded
// there). What actually named CBasePlayer was the loop itself: the three
// IGameMovement signatures, CGameMovement::player, and about 350 member asks
// across gamemovement.cpp. This interface is those asks, COUNTED from that
// file, and nothing else — the same membership rule IGameEntity lives by.
//
// EVERY MEMBER IS PREFIXED Mv, deliberately, except one. The alternative was
// keeping CBasePlayer's spellings, and it fails on arithmetic: an interface
// member named GetFlags collides with the CBaseEntity chain's GetFlags inside
// CBasePlayer, which makes every unqualified call ambiguous until CBasePlayer
// redeclares it — and CBasePlayer's members are called from a hundred files
// that are none of this phase's business. A prefix keeps the blast radius to
// gamemovement.* plus one forwarder block per player class, and the rename is
// one mechanical rule a reviewer can hold: player->X() becomes player->MvX(),
// player->m_Foo becomes player->MvFoo(). The exception is GetModelScale,
// which keeps its exact name because the VEC_*_SCALED macros in shareddefs.h
// spell it, and those macros are the SDK's, used from files this phase must
// not touch.
//
// THE DATA MEMBERS BECOME REFERENCE ACCESSORS, not get/set pairs, because the
// loop compound-assigns them (m_flWaterJumpTime -= ..., m_Local.m_bDucked ^=)
// and a get/set split would rewrite arithmetic rather than rename it. On the
// server, MvLocal() returning CPlayerLocalData& preserves networking: the
// change hooks are DECLARE_EMBEDDED_NETWORKVAR virtuals on CPlayerLocalData
// itself (networkvar.h:153), so a write through the base reference dispatches
// to the same NetworkVar_m_Local override a direct member write does.
//
// WHAT THIS COSTS CBasePlayer: a second base class, which is a second vtable
// pointer and every member offset moving by eight bytes. That is safe for the
// same reason it was always safe on the client — C_BaseEntity already
// multiply-inherits four interfaces — and every offset consumer (datadescs,
// send tables, the shim's blob) computes at compile time or asks at runtime.
//
//=============================================================================//
#pragma once

#include "mathlib/vector.h"
#include "const.h"

#include "igameentity.h"
class IHandleEntity;
// The player-local block MvLocal() answers: the client's own CPlayerLocalData, and
// on the server ss::CPlayerLocalData (ss_playerlocaldata.h), server.so's own type
// (plan_server_novalve.md P5). Same member names on both sides, so the shared
// movement code reads either.
#ifdef CLIENT_DLL
class CPlayerLocalData;
typedef CPlayerLocalData MvLocalData_t;
#else
namespace ss { class CPlayerLocalData; }
typedef ss::CPlayerLocalData MvLocalData_t;
#endif
class CPlayerState;
struct surfacedata_t;

abstract_class IMovementPlayer
{
public:
	//-------------------------------------------------------------------------
	// The two state blocks the loop lives in. m_Local is the predicted
	// player-local block (ducking, fall velocity, step size); pl is the
	// engine-facing CPlayerState, of which the loop reads exactly deadflag —
	// but the block is what a standalone player must own anyway, because
	// IServerGameClients::GetPlayerState hands the engine a pointer to it.
	//-------------------------------------------------------------------------
	virtual MvLocalData_t &MvLocal() = 0;
	virtual CPlayerState     &MvPlayerState() = 0;

	//-------------------------------------------------------------------------
	// The loop's scratch state on the player: written by one movement frame,
	// read by the next, predicted on the client by being rewound with
	// everything else. References because the loop compound-assigns them.
	//-------------------------------------------------------------------------
	virtual float          &MvWaterJumpTime() = 0;
	virtual Vector         &MvWaterJumpVel() = 0;
	virtual float          &MvSwimSoundTime() = 0;
	virtual float          &MvStepSoundTime() = 0;
	virtual float          &MvSurfaceFriction() = 0;
	virtual surfacedata_t *&MvSurfaceData() = 0;
	virtual int            &MvSurfaceProps() = 0;
	virtual char           &MvTextureType() = 0;
	virtual Vector         &MvLadderNormal() = 0;
	virtual int            &MvStuckLast() = 0;

	// Read once, in CheckWater's dead-in-water test. Read-only because the
	// loop never writes health, and widening this to a reference would hand
	// the movement code a damage path nothing audits.
	virtual int             MvHealth() const = 0;

	//-------------------------------------------------------------------------
	// Position, velocity, orientation plumbing.
	//-------------------------------------------------------------------------
	//-------------------------------------------------------------------------
	// The ground, as an IHandleEntity and NOT as a CBaseEntity.
	//
	// The one entity in this loop whose hierarchy the loop does not choose: it
	// arrives out of a trace, and a trace hits anything in the spatial
	// partition. Spelled as a CBaseEntity, it silently did not arrive at all
	// for every brush mover in this build -- all of which are on the parallel
	// hierarchy -- so a player on a lift had no ground rather than a ground
	// standing still. IHandleEntity is the base both hierarchies share and the
	// exact type trace_t::m_pEnt already holds, so nothing is converted on the
	// way in and every `ground != NULL` test in the loop keeps meaning what it
	// says. What the loop needs BEYOND existence is asked of movement_ground.h,
	// which answers each question once for both kinds.
	//-------------------------------------------------------------------------
	virtual IHandleEntity  *MvGetGroundEntity() = 0;
	virtual void            MvSetGroundEntity( IHandleEntity *pGround ) = 0;
	virtual const Vector   &MvGetBaseVelocity() const = 0;
	virtual void            MvSetBaseVelocity( const Vector &v ) = 0;
	virtual MoveType_t      MvGetMoveType() const = 0;
	virtual void            MvSetMoveType( MoveType_t val ) = 0;
	virtual MoveCollide_t   MvGetMoveCollide() const = 0;
	virtual void            MvSetMoveCollide( MoveCollide_t val ) = 0;
	virtual const Vector   &MvGetViewOffset() const = 0;
	virtual void            MvSetViewOffset( const Vector &v ) = 0;
	virtual float           MvGetGravity() const = 0;
	virtual void            MvSetGravity( float flGravity ) = 0;
	virtual float           MvGetStepSize() const = 0;

	// The two asks ProcessMovement makes before the loop starts: the lag
	// compensation timescale it multiplies frametime by, and the speed cap
	// the command runs under.
	virtual float           MvGetLaggedMovementValue() = 0;
	virtual float           MvGetPlayerMaxSpeed() = 0;

	//-------------------------------------------------------------------------
	// Water and flags.
	//-------------------------------------------------------------------------
	virtual int             MvGetWaterLevel() const = 0;
	virtual void            MvSetWaterLevel( int nLevel ) = 0;
	virtual int             MvGetWaterType() const = 0;
	virtual void            MvSetWaterType( int nType ) = 0;
	virtual int             MvGetFlags() const = 0;
	virtual void            MvAddFlag( int flags ) = 0;
	virtual void            MvRemoveFlag( int flags ) = 0;

	//-------------------------------------------------------------------------
	// Identity and mode.
	//-------------------------------------------------------------------------
	virtual int             MvEntIndex() const = 0;
	virtual int             MvCurrentCommandNumber() const = 0;
	virtual bool            MvIsServer() const = 0;
	virtual bool            MvIsAlive() const = 0;
	virtual bool            MvIsObserver() const = 0;
	virtual int             MvGetObserverMode() const = 0;
	virtual IGameEntity    *MvGetObserverTarget() const = 0;
	virtual bool            MvIsInAVehicle() const = 0;

	//-------------------------------------------------------------------------
	// Feedback the loop triggers: footsteps, splashes, rumble, duck toggling.
	//-------------------------------------------------------------------------
	virtual void            MvPlayStepSound( Vector &vecOrigin, surfacedata_t *psurface,
	                                         float fvol, bool force ) = 0;
	virtual void            MvUpdateStepSound( surfacedata_t *psurface, const Vector &vecOrigin,
	                                           const Vector &vecVelocity ) = 0;

#ifndef CLIENT_DLL
	//-------------------------------------------------------------------------
	// Server-only, because their call sites are: the splash, the fall rumble,
	// the console crouch-hint counter and the surface-trigger texture memory all
	// sit under !CLIENT_DLL guards in the loop, and the duck toggle's only
	// live caller is inside the crouch-hint block. Guarded here rather than
	// implemented with dummies, so the client half of a player never carries
	// members whose callers cannot exist on its side (Part 4b's rule).
	//-------------------------------------------------------------------------
	virtual char           &MvPreviousTextureType() = 0;
	virtual int            &MvNumCrouches() = 0;
	virtual void            MvSplash() = 0;
	virtual void            MvRumbleEffect( unsigned char index, unsigned char rumbleData,
	                                        unsigned char rumbleFlags ) = 0;
	virtual void            MvToggleDuck() = 0;
	virtual bool            MvGetToggledDuckState() = 0;
#endif

	// The one exact-name member, for the VEC_*_SCALED macros — see the header.
	virtual float           GetModelScale() const = 0;

#ifdef CLIENT_DLL
	//-------------------------------------------------------------------------
	// Interpolation latching, which only exists where interpolation does. The
	// one caller is ResetDuckLatched, already client-guarded in the loop.
	//-------------------------------------------------------------------------
	virtual bool            MvInFirstPersonView() = 0;
	virtual void            MvResetLatched() = 0;
#endif

protected:
	virtual ~IMovementPlayer() {}
};
