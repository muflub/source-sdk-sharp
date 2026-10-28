//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A connecting client's avatar on the standalone hierarchy: a CStandaloneEntity bound to
// the edict the engine allocated for the client, implementing IMovementPlayer. It holds
// only the engine-facing state (CPlayerState, CPlayerLocalData, movement scratch, the
// command count); every game decision is managed.
#pragma once

#include "shell_standalone_class.h"
#include "imovementplayer.h"
#include "PlayerState.h"
#include "ss_playerlocaldata.h"
#include "ss_usercmd.h"
#include "sourcesharp.h"

class IPhysicsObject;
class IPhysicsPlayerController;
class IPhysicsEnvironment;

class CStandalonePlayer : public CStandaloneEntity, public IMovementPlayer
{
public:
	typedef CStandaloneEntity BaseClass;

	CStandalonePlayer();
	virtual ~CStandalonePlayer();

	virtual bool IsPlayer() const OVERRIDE { return true; }

	// The managed OnSpawn supplies the model and game state.
	virtual void Spawn() OVERRIDE;

	// CPlayerState lives in the engine block's server tail and is handed to the engine by
	// IServerGameClients::GetPlayerState, which must never answer NULL (the engine writes
	// hltv/replay through it). Constructed in place: it has a virtual destructor.
	CPlayerState *PlayerStatePtr()
	{
		return reinterpret_cast< CPlayerState * >( Block().Server.PlayerState );
	}

	const CPlayerState *PlayerStatePtr() const
	{
		return reinterpret_cast< const CPlayerState * >( Block().Server.PlayerState );
	}

	CPlayerState       &PlayerState()       { return *PlayerStatePtr(); }
	const CPlayerState &PlayerState() const { return *PlayerStatePtr(); }

	// The origin plus the view offset.
	Vector EyePosition() const { return GetAbsOrigin() + m_vecViewOffset; }

	// CBaseEntity::WorldSpaceCenter for a player, which is SOLID_BBOX and never rotates.
	Vector WorldSpaceCenter() const
	{
		return GetAbsOrigin() + ( OBBMins() + OBBMaxs() ) * 0.5f;
	}

	const char *PlayerName() const { return m_szPlayerName; }
	void SetPlayerName( const char *pName );

	// The count of commands C# decoded and queued for this player.
	void QueuedUsercmds( int nCount );
	void RunQueuedCommands();

	// What the managed loop is asked to run this frame.
	int  PendingCommandCount() const { return m_nPendingCmds; }

	void SyncToBlob();

	virtual int TakeDamage( const ss::CTakeDamageInfo &info ) OVERRIDE;

	// --- IMovementPlayer -------------------------------------------------------
	virtual MvLocalData_t &MvLocal() OVERRIDE { return m_Local; }
	virtual CPlayerState     &MvPlayerState() OVERRIDE { return PlayerState(); }

	virtual float          &MvWaterJumpTime() OVERRIDE { return m_flWaterJumpTime; }
	virtual Vector         &MvWaterJumpVel() OVERRIDE { return m_vecWaterJumpVel; }
	virtual float          &MvSwimSoundTime() OVERRIDE { return m_flSwimSoundTime; }
	virtual float          &MvStepSoundTime() OVERRIDE { return m_flStepSoundTime; }
	virtual float          &MvSurfaceFriction() OVERRIDE { return m_flSurfaceFriction; }
	virtual surfacedata_t *&MvSurfaceData() OVERRIDE { return m_pSurfaceData; }
	virtual int            &MvSurfaceProps() OVERRIDE { return m_nSurfaceProps; }
	virtual char           &MvTextureType() OVERRIDE { return m_chTextureType; }
	virtual Vector         &MvLadderNormal() OVERRIDE { return m_vecLadderNormal; }
	virtual int            &MvStuckLast() OVERRIDE { return m_nStuckLast; }

	virtual int             MvHealth() const OVERRIDE { return GetHealth(); }

	virtual IHandleEntity  *MvGetGroundEntity() OVERRIDE;
	virtual void            MvSetGroundEntity( IHandleEntity *pGround ) OVERRIDE;
	virtual const Vector   &MvGetBaseVelocity() const OVERRIDE { return m_vecBaseVelocity; }
	virtual void            MvSetBaseVelocity( const Vector &v ) OVERRIDE { m_vecBaseVelocity = v; }
	virtual MoveType_t      MvGetMoveType() const OVERRIDE { return m_MoveType; }
	virtual void            MvSetMoveType( MoveType_t val ) OVERRIDE { m_MoveType = val; }
	virtual MoveCollide_t   MvGetMoveCollide() const OVERRIDE { return m_MoveCollide; }
	virtual void            MvSetMoveCollide( MoveCollide_t val ) OVERRIDE { m_MoveCollide = val; }
	virtual const Vector   &MvGetViewOffset() const OVERRIDE { return m_vecViewOffset; }
	virtual void            MvSetViewOffset( const Vector &v ) OVERRIDE { m_vecViewOffset = v; }
	virtual float           MvGetGravity() const OVERRIDE { return m_flGravity; }
	virtual void            MvSetGravity( float flGravity ) OVERRIDE { m_flGravity = flGravity; }
	virtual float           MvGetStepSize() const OVERRIDE { return m_Local.m_flStepSize; }

	virtual float           MvGetLaggedMovementValue() OVERRIDE { return 1.0f; }
	virtual float           MvGetPlayerMaxSpeed() OVERRIDE { return m_flMaxSpeed; }

	virtual int             MvGetWaterLevel() const OVERRIDE { return m_nWaterLevel; }
	virtual void            MvSetWaterLevel( int nLevel ) OVERRIDE { m_nWaterLevel = nLevel; }
	virtual int             MvGetWaterType() const OVERRIDE { return m_nWaterType; }
	virtual void            MvSetWaterType( int nType ) OVERRIDE { m_nWaterType = nType; }
	virtual int             MvGetFlags() const OVERRIDE { return m_fFlags; }
	virtual void            MvAddFlag( int flags ) OVERRIDE { m_fFlags |= flags; }
	virtual void            MvRemoveFlag( int flags ) OVERRIDE { m_fFlags &= ~flags; }

	virtual int             MvEntIndex() const OVERRIDE { return EntIndex(); }
	virtual int             MvCurrentCommandNumber() const OVERRIDE
	{
		return 0;   // the command is the managed loop's (T4g)
	}
	virtual bool            MvIsServer() const OVERRIDE { return true; }
	virtual bool            MvIsAlive() const OVERRIDE { return GetHealth() > 0 && !PlayerState().deadflag; }
	virtual bool            MvIsObserver() const OVERRIDE { return m_bObserver; }
	virtual int             MvGetObserverMode() const OVERRIDE { return m_nObserverMode; }
	virtual IGameEntity    *MvGetObserverTarget() const OVERRIDE { return NULL; }
	virtual bool            MvIsInAVehicle() const OVERRIDE { return false; }

	// Footsteps and splashes are not played.
	virtual void            MvPlayStepSound( Vector &vecOrigin, surfacedata_t *psurface,
	                                         float fvol, bool force ) OVERRIDE {}
	virtual void            MvUpdateStepSound( surfacedata_t *psurface, const Vector &vecOrigin,
	                                           const Vector &vecVelocity ) OVERRIDE {}

	virtual char           &MvPreviousTextureType() OVERRIDE { return m_chPreviousTextureType; }
	virtual int            &MvNumCrouches() OVERRIDE { return m_nNumCrouches; }
	virtual void            MvSplash() OVERRIDE {}
	virtual void            MvRumbleEffect( unsigned char index, unsigned char rumbleData,
	                                        unsigned char rumbleFlags ) OVERRIDE {}
	virtual void            MvToggleDuck() OVERRIDE {}
	virtual bool            MvGetToggledDuckState() OVERRIDE { return false; }

	// The same scale the entity draws at, as in stock. Qualified because both bases declare
	// GetModelScale.
	virtual float           GetModelScale() const OVERRIDE
	{
		return CStandaloneEntity::GetModelScale();
	}

	const Vector &AbsVelocity() const { return m_vecAbsVelocity; }
	void SetAbsVelocity( const Vector &v ) { m_vecAbsVelocity = v; }

	// Driven by the managed death cycle; solidity follows it.
	void SetObserver( bool bObserver, int nMode );

	// CBaseEntity::ChangeTeam's store only; every decision is managed. TEAM_UNASSIGNED (0)
	// at birth.
	int  TeamNumber() const { return m_iTeamNum; }
	void ChangeTeam( int iTeamNum ) { m_iTeamNum = iTeamNum; }

	// Physics shadow: a second body kept at the player's position so the solver can push
	// what it meets. Managed builds and drives it; this side holds the pointers and owns
	// the teardown. Idempotent; also called from the disconnect path.
	void DestroyVCollision();

	bool HasVCollision() const { return m_pPhysicsController != NULL; }

	// Facts for the managed shadow and command loop, and their results carried out.
	void ShadowFacts( SourceSharpPlayerShadowFacts *pOut );

	void CommandFacts( SourceSharpPlayerCommandFacts *pOut );
	void FinishCommand( const SourceSharpPlayerCommandResult &result );
	void AdoptShadow( IPhysicsObject *pStand, IPhysicsObject *pCrouch, IPhysicsPlayerController *pController );
	void CommitShadow( int nState, bool bAfterCommand );

	// Stock's Touch ignores the ground: leaning on the floor is not shoving it.
	const CBaseHandle &GroundHandle() const { return m_hGround; }

public:
	// Connected players by entindex (1..MAX_PLAYERS).
	static CStandalonePlayer *FromEdict( const edict_t *pEdict );
	static CStandalonePlayer *FromIndex( int nEntIndex );

	// Once per tick, before the entity thinks, so a think sees this tick's player moves.
	static void RunAllQueuedCommands();

protected:
	// Mirrors the managed death verdict into deadflag.
	virtual void OnManagedKilled( IGameEntity *pAttacker ) OVERRIDE;

private:
	// Which hull is in the solver.
	void SetVCollisionState( const Vector &vecOrigin, const Vector &vecVelocity, int nState );

	ss::CPlayerLocalData m_Local;

	float          m_flWaterJumpTime;
	Vector         m_vecWaterJumpVel;
	float          m_flSwimSoundTime;
	float          m_flStepSoundTime;
	float          m_flSurfaceFriction;
	surfacedata_t *m_pSurfaceData;
	int            m_nSurfaceProps;
	char           m_chTextureType;
	char           m_chPreviousTextureType;
	Vector         m_vecLadderNormal;
	int            m_nStuckLast;
	int            m_nNumCrouches;

	Vector         m_vecAbsVelocity;
	Vector         m_vecBaseVelocity;
	Vector         m_vecViewOffset;
	Vector         m_vecPreviouslyPredictedOrigin;
	float          m_flGravity;
	float          m_flMaxSpeed;
	int            m_fFlags;
	MoveType_t     m_MoveType;
	MoveCollide_t  m_MoveCollide;
	int            m_nWaterLevel;
	int            m_nWaterType;
	CBaseHandle    m_hGround;

	bool           m_bObserver;
	int            m_nObserverMode;

	// TEAM_UNASSIGNED until ChangeTeam.
	int            m_iTeamNum;

	// NULL until managed code builds them.
	IPhysicsObject           *m_pShadowStand;
	IPhysicsObject           *m_pShadowCrouch;
	IPhysicsObject           *m_pShadowActive;
	IPhysicsPlayerController *m_pPhysicsController;

	// The environment that built them. The solver is recreated at every level change, and
	// destroying a stale object through the new one frees the wrong memory.
	IPhysicsEnvironment       *m_pShadowEnv;
	int                       m_nVCollisionState;
	// The frame guard and the waiting command count.
	int            m_nSimulationTick;
	int            m_nPendingCmds;

	char           m_szPlayerName[ 128 ];
};

// Creates the player for a connecting client through the registered factory, bound to
// the engine's edict. NULL, with a warning, when the class was never registered.
CStandalonePlayer *StandalonePlayer_Create( edict_t *pEdict, const char *pPlayerName );

// A client command that is not a registered ConCommand, handed to managed code.
void StandalonePlayer_ClientCommand( CStandalonePlayer *pPlayer, const CCommand &args );

// A managed handle as a standalone player, or NULL for anything else.
CStandalonePlayer *SourceSharp_ResolveStandalonePlayer( uint32_t hEntity );

// The player who issued the console command being dispatched, or NULL.
CStandalonePlayer *StandalonePlayer_CommandClient();

// The shell to the player, checked in debug. Callers ask IsPlayer() first.
inline CStandalonePlayer *SourceSharp_PlayerOf( CStandaloneEntity *pEnt )
{
	Assert( !pEnt || dynamic_cast< CStandalonePlayer * >( pEnt ) != NULL );
	return static_cast< CStandalonePlayer * >( pEnt );
}

inline CStandalonePlayer *SourceSharp_PlayerOf( IGameEntity *pEnt )
{
	return SourceSharp_PlayerOf( SourceSharp_ShellOf( pEnt ) );
}
