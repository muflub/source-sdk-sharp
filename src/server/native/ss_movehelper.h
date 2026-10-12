//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the move helper, as server.so owns it
//
// plan_server_novalve.md P5. Replaces game/shared/imovehelper.h for server.so:
// the movement loop (shell_movement.cpp, server arm) calls the installed
// helper, and shell_standalone_player.cpp's CStandaloneMoveHelper is the only
// one this build ever constructs. The engine never calls it, so it is an ss::
// type, per the plan's naming rule.
//
// The virtuals are stock's, in stock's order, with two departures:
//   * PlayerSetAnimation takes the animation as an int: PLAYER_ANIM is a fork
//     enum, and the helper here ignores it.
//   * SetHost( CBasePlayer * ) is not declared. Nothing called it, and the one
//     implementation only asserted its argument was NULL.
//=============================================================================//
#ifndef SOURCESHARP_SS_MOVEHELPER_H
#define SOURCESHARP_SS_MOVEHELPER_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "soundflags.h"           // soundlevel_t

class IPhysicsSurfaceProps;
class Vector;
class CGameTrace;

namespace ss
{

abstract_class IMoveHelper
{
public:
	static IMoveHelper *GetSingleton() { return sm_pSingleton; }

	// The player's name, for debugging.
	virtual char const *GetName( CBaseHandle handle ) const = 0;

	// What the move touched this command.
	virtual void ResetTouchList( void ) = 0;
	virtual bool AddToTouched( const CGameTrace &tr, const Vector &impactvelocity ) = 0;
	virtual void ProcessImpacts( void ) = 0;

	// Numbered debug text on the console.
	virtual void Con_NPrintf( int idx, PRINTF_FORMAT_STRING char const *fmt, ... ) = 0;

	// Sounds the movement makes.
	virtual void StartSound( const Vector &origin, int channel, char const *sample, float volume,
	                         soundlevel_t soundlevel, int fFlags, int pitch ) = 0;
	virtual void StartSound( const Vector &origin, const char *soundname ) = 0;
	virtual void PlaybackEventFull( int flags, int clientindex, unsigned short eventindex, float delay,
	                                Vector &origin, Vector &angles, float fparam1, float fparam2,
	                                int iparam1, int iparam2, int bparam1, int bparam2 ) = 0;

	// Landing hard, and the player's animation.
	virtual bool PlayerFallingDamage( void ) = 0;
	virtual void PlayerSetAnimation( int playerAnim ) = 0;

	virtual IPhysicsSurfaceProps *GetSurfaceProps( void ) = 0;

	// Entry 0 of the handle table is the world.
	virtual bool IsWorldEntity( const CBaseHandle &handle ) = 0;

protected:
	static void SetSingleton( IMoveHelper *pMoveHelper ) { sm_pSingleton = pMoveHelper; }

	virtual ~IMoveHelper() {}

	static IMoveHelper *sm_pSingleton;
};

// The helper the movement loop is running under.
inline IMoveHelper *MoveHelper()
{
	return IMoveHelper::GetSingleton();
}

} // namespace ss

#endif // SOURCESHARP_SS_MOVEHELPER_H
