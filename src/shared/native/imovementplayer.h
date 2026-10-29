//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// What the movement loop asks of a player, so the loop does not name CBasePlayer.
// Members are prefixed Mv to avoid colliding with the CBaseEntity chain inside
// CBasePlayer; GetModelScale keeps its name because the VEC_*_SCALED macros spell it.
// Data members are reference accessors because the loop compound-assigns them.
#pragma once

#include "igameentity.h"
class IHandleEntity;
// The player-local block: the client's CPlayerLocalData, or server.so's ss::CPlayerLocalData.
#ifdef CLIENT_DLL
class CPlayerLocalData;
typedef CPlayerLocalData MvLocalData_t;
#else
namespace ss
{
class CPlayerLocalData;
}

typedef ss::CPlayerLocalData MvLocalData_t;
#endif
class CPlayerState;
struct surfacedata_t;

abstract_class IMovementPlayer
{
public:
	virtual MvLocalData_t &MvLocal() = 0;
	virtual CPlayerState &MvPlayerState() = 0;

	// The loop's scratch state on the player, carried from one movement frame to the next.
	virtual float &MvWaterJumpTime() = 0;
	virtual Vector &MvWaterJumpVel() = 0;
	virtual float &MvSwimSoundTime() = 0;
	virtual float &MvStepSoundTime() = 0;
	virtual float &MvSurfaceFriction() = 0;
	virtual surfacedata_t *&MvSurfaceData() = 0;
	virtual int &MvSurfaceProps() = 0;
	virtual char &MvTextureType() = 0;
	virtual Vector &MvLadderNormal() = 0;
	virtual int &MvStuckLast() = 0;

	// Read-only: the loop never writes health.
	virtual int MvHealth() const = 0;

	// The ground is an IHandleEntity, as trace_t::m_pEnt holds it, so brush movers
	// on the parallel hierarchy count as ground too.
	virtual IHandleEntity *MvGetGroundEntity() = 0;
	virtual void MvSetGroundEntity( IHandleEntity * pGround ) = 0;
	virtual const Vector &MvGetBaseVelocity() const = 0;
	virtual void MvSetBaseVelocity( const Vector &v ) = 0;
	virtual MoveType_t MvGetMoveType() const = 0;
	virtual void MvSetMoveType( MoveType_t val ) = 0;
	virtual MoveCollide_t MvGetMoveCollide() const = 0;
	virtual void MvSetMoveCollide( MoveCollide_t val ) = 0;
	virtual const Vector &MvGetViewOffset() const = 0;
	virtual void MvSetViewOffset( const Vector &v ) = 0;
	virtual float MvGetGravity() const = 0;
	virtual void MvSetGravity( float flGravity ) = 0;
	virtual float MvGetStepSize() const = 0;

	// Lag compensation timescale and the command's speed cap, read by ProcessMovement.
	virtual float MvGetLaggedMovementValue() = 0;
	virtual float MvGetPlayerMaxSpeed() = 0;

	virtual int MvGetWaterLevel() const = 0;
	virtual void MvSetWaterLevel( int nLevel ) = 0;
	virtual int MvGetWaterType() const = 0;
	virtual void MvSetWaterType( int nType ) = 0;
	virtual int MvGetFlags() const = 0;
	virtual void MvAddFlag( int flags ) = 0;
	virtual void MvRemoveFlag( int flags ) = 0;

	virtual int MvEntIndex() const = 0;
	virtual int MvCurrentCommandNumber() const = 0;
	virtual bool MvIsServer() const = 0;
	virtual bool MvIsAlive() const = 0;
	virtual bool MvIsObserver() const = 0;
	virtual int MvGetObserverMode() const = 0;
	virtual IGameEntity *MvGetObserverTarget() const = 0;
	virtual bool MvIsInAVehicle() const = 0;

	virtual void MvPlayStepSound( Vector & vecOrigin, surfacedata_t * psurface, float fvol, bool force ) = 0;
	virtual void MvUpdateStepSound( surfacedata_t * psurface, const Vector &vecOrigin, const Vector &vecVelocity ) = 0;

#ifndef CLIENT_DLL
	// Server-only because their call sites in the loop are.
	virtual char &MvPreviousTextureType() = 0;
	virtual int &MvNumCrouches() = 0;
	virtual void MvSplash() = 0;
	virtual void MvRumbleEffect( unsigned char index, unsigned char rumbleData, unsigned char rumbleFlags ) = 0;
	virtual void MvToggleDuck() = 0;
	virtual bool MvGetToggledDuckState() = 0;
#endif

	virtual float GetModelScale() const = 0;

#ifdef CLIENT_DLL
	virtual bool MvInFirstPersonView() = 0;
	virtual void MvResetLatched() = 0;
#endif

protected:
	virtual ~IMovementPlayer() {}
};
