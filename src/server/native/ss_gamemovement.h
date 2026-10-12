//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's movement interface and its move data ===//
//
// plan_server_novalve.md P5. CMoveData is the block one player's move reads and
// writes; IGameMovement runs a move over it. No engine binary asks for
// "GameMovement001" (grep over every .so of the SDK Base 2013 Multiplayer and
// Dedicated Server installs: only the stock game DLLs name it), so on the server
// both are server.so's own types, in namespace ss, on public/ headers alone.
// The client keeps game/shared/igamemovement.h: it predicts with them.
//
// Members and virtuals are in stock's order. shell_movement.cpp implements the
// interface; shell_shared.cpp owns the one move-data block.
//=============================================================================//

#ifndef SOURCESHARP_SS_GAMEMOVEMENT_H
#define SOURCESHARP_SS_GAMEMOVEMENT_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"
#include "basehandle.h"
#include "tier0/platform.h"   // PRINTF_FORMAT_STRING, abstract_class

class IMovementPlayer;

// The interface's registered name, for the server's expose in shell_movement.cpp
// until that is retired (nothing in the engine asks for it).
#ifndef INTERFACENAME_GAMEMOVEMENT
#define INTERFACENAME_GAMEMOVEMENT "GameMovement001"
#endif

namespace ss
{

class CMoveData
{
public:
	bool  m_bFirstRunOfFunctions : 1;
	bool  m_bGameCodeMovedPlayer : 1;

	CBaseHandle    m_nPlayerHandle;   // imovehelper.h's EntityHandle_t on the server
	int            m_nImpulseCommand;

	// The command: view angles (local, world), buttons now and last frame, and
	// the four movement axes with the speed caps.
	QAngle m_vecViewAngles;
	QAngle m_vecAbsViewAngles;
	int    m_nButtons;
	int    m_nOldButtons;
	float  m_flForwardMove, m_flOldForwardMove, m_flSideMove, m_flUpMove;
	float  m_flMaxSpeed, m_flClientMaxSpeed;

	// The player's state in and out of the move.
	Vector m_vecVelocity;
	QAngle m_vecAngles, m_vecOldAngles;
	float  m_outStepHeight;
	Vector m_outWishVel, m_outJumpVel;

	// A movement constraint sphere.
	Vector m_vecConstraintCenter;
	float  m_flConstraintRadius, m_flConstraintWidth, m_flConstraintSpeedFactor;

	void          SetAbsOrigin( const Vector &vec ) { m_vecAbsOrigin = vec; }
	const Vector &GetAbsOrigin() const              { return m_vecAbsOrigin; }

private:
	Vector m_vecAbsOrigin;
};

abstract_class IGameMovement
{
public:
	virtual ~IGameMovement( void ) {}

	virtual void   ProcessMovement( IMovementPlayer *pPlayer, CMoveData *pMove ) = 0;
	virtual void   StartTrackPredictionErrors( IMovementPlayer *pPlayer ) = 0;
	virtual void   FinishTrackPredictionErrors( IMovementPlayer *pPlayer ) = 0;
	virtual void   DiffPrint( PRINTF_FORMAT_STRING char const *fmt, ... ) = 0;

	virtual Vector GetPlayerMins( bool ducked ) const = 0;
	virtual Vector GetPlayerMaxs( bool ducked ) const = 0;
	virtual Vector GetPlayerViewOffset( bool ducked ) const = 0;
};

} // namespace ss

#endif // SOURCESHARP_SS_GAMEMOVEMENT_H
