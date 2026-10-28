//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the standalone player (Phase 9a) ===================//
//
// A connecting client's avatar on the parallel hierarchy: a CStandaloneEntity
// bound to the edict the ENGINE allocated for the client, implementing
// IMovementPlayer so the one movement loop 9b retyped can move it.
//
// THE player, unconditionally, since Phase 13c flipped the -ss_player flag
// and retired CSourceSharpPlayer. The class registration and the send tables
// have always existed unconditionally on both sides, because an unpaired
// ServerClass hangs the join silently and that lesson has been learned here
// once already (shell_standalone.cpp:14).
//
// What this class holds is exactly what the engine and the movement loop
// demand of a player and nothing else:
//
//   - CPlayerState, because IServerGameClients::GetPlayerState hands the
//     engine a pointer to it (view angles, fixangle, the network name);
//   - CPlayerLocalData, because MvLocal() returns a reference to one and the
//     loop compound-assigns its members (ducking, fall velocity, step size);
//   - the loop's scratch state (water-jump, surface, stuck bookkeeping);
//   - the usercmd queue, because commands arrive per packet and run per tick.
//
// Every game DECISION — spawn state, damage rules, death, what the buttons
// mean — is the managed side's, reached through the same scripted contract
// every other class on the hierarchy speaks. This file is the player's
// engine-facing arms, not its behaviour.
//
//=============================================================================//
#pragma once

#include "shell_standalone_class.h"
#include "imovementplayer.h"
#include "PlayerState.h"
#include "ss_playerlocaldata.h"
#include "ss_usercmd.h"
// SourceSharpPlayerShadowState / SourceSharpPlayerShadowPlan -- the two structs
// the managed shadow step reads and writes through.
#include "sourcesharp.h"

class IPhysicsObject;
class IPhysicsPlayerController;
class IPhysicsEnvironment;

class CStandalonePlayer : public CStandaloneEntity, public IMovementPlayer
{
public:
	// THE BASE IS THE SHELL ITSELF NOW (plan_entity.md §3.1, step 1f).
	// CStandaloneScriptedEntity stood between these two and held thirteen
	// relays into thirteen ABI slots; there is one slot and one op number now,
	// so the relays are four lines each and live on the base. The player is the
	// only subclass of the shell that remains, and it is one because the
	// usercmd loop and the movement state are still C++ (§3.10 moves them).
	typedef CStandaloneEntity BaseClass;

	CStandalonePlayer();
	virtual ~CStandalonePlayer();

	// --- identity ------------------------------------------------------------
	virtual bool IsPlayer() const OVERRIDE { return true; }

	// Placed in the world with a player's shape. The managed OnSpawn that the
	// base fires is where the model and the game state come from.
	virtual void Spawn() OVERRIDE;

	// THE ENGINE-FACING STATE BLOCK, AND IT IS IN THE ENGINE BLOCK (step 3c).
	//
	// IServerGameClients::GetPlayerState hands the engine this pointer, and the
	// storage behind it is the sixty-four bytes the block's SERVER TAIL reserves
	// (plan_entity.md §3.2, EngineBlockServerTail.PlayerState). It used to be a
	// member of this class, and moving it is not a tidy-up:
	//
	//   * TWO ENGINE SITES WRITE THROUGH THE RESULT WITH NO NULL TEST --
	//     `player->hltv = true` and `player->replay = true` for the HLTV and
	//     Replay master clients. So
	//     GetPlayerState may not answer NULL, and shell_playerinfo.cpp is where
	//     that promise is kept for an edict this class does not claim.
	//   * The block is the memory the shell's engine-facing answers come out of.
	//     A player's engine-facing state living in a C++ member while everything
	//     else lives in the block is the duplication §3.2 exists to remove.
	//
	// A reinterpret_cast and not a member: the reservation is uint64[8] because
	// the SIZE and the PLACE are C#'s and the LAYOUT is Valve's, and restating
	// CPlayerState on the C# side would be a second opinion about a struct this
	// project does not own. The object is CONSTRUCTED IN PLACE in the
	// constructor -- CPlayerState has a virtual destructor, so its vtable
	// pointer has to be written by something and zeroed storage would not do it.
	CPlayerState *PlayerStatePtr()
	{
		return reinterpret_cast< CPlayerState * >( Block().Server.PlayerState );
	}

	const CPlayerState *PlayerStatePtr() const
	{
		return reinterpret_cast< const CPlayerState * >( Block().Server.PlayerState );
	}

	// The same thing as a reference, which is what the bodies in the .cpp read
	// where they used to name the member.
	CPlayerState       &PlayerState()       { return *PlayerStatePtr(); }
	const CPlayerState &PlayerState() const { return *PlayerStatePtr(); }

	// Where this player's eyes are: the origin plus the view offset the loop
	// maintains through ducking. What visibility, hearing and aiming share.
	Vector EyePosition() const { return GetAbsOrigin() + m_vecViewOffset; }

	// The middle of the player's box, in world space. What a screen shake
	// measures its falloff from — CBaseEntity::WorldSpaceCenter's answer for a
	// player, arrived at directly: a player is SOLID_BBOX and never rotates, so
	// the collision property's general case reduces to exactly this.
	Vector WorldSpaceCenter() const
	{
		return GetAbsOrigin() + ( OBBMins() + OBBMaxs() ) * 0.5f;
	}

	const char *PlayerName() const { return m_szPlayerName; }
	void SetPlayerName( const char *pName );

	// --- the command path (Phase 9a/9c4's server half) ------------------------
	// Commands arrive per packet (ProcessUsercmds) and run per tick, paced the
	// way stock paces them: one simulate per tick, dropped packets padded with
	// the last known command.
	// plan_server_novalve.md P2.2: the commands C# decoded, planned and queued
	// for this player, in run order; appended as CUserCmds for the run loop.
	void QueuedUsercmds( int nCount );
	void RunQueuedCommands();

	// plan_entity.md step 3d. ONE QUEUED COMMAND, BY INDEX, for the managed run
	// loop that drives this queue -- EngineApi::PlayerRunMove. Public because
	// the ABI slot is a free function by necessity, which is the same reason
	// step 3e's four movement members are public on CGameMovement.
	//
	// What the managed loop is being asked to run this frame.
	int  PendingCommandCount() const { return m_nPendingCmds; }

	// Native state -> blob (9c5); see the definition.
	void SyncToBlob();

	// Damage syncs the blob the moment it lands — see the definition.
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

	// Footsteps and splashes are presentation the loop triggers. Left silent
	// for now and RECORDED (docs/divergences.md): a standalone player walks
	// quietly until the step-sound port lands, rather than half-heard through
	// a path nothing verifies.
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

	// THE SHELL'S OWN NUMBER, and no longer a constant. This used to read
	// `return 1.0f;` and gib_verdict_test.cpp cited that line as half of its
	// BLOCKER 2 -- "the standalone hierarchy has no per-instance model scale".
	//
	// It is the same number the entity draws at, deliberately, because in stock
	// it is literally the same member: CBasePlayer::GetModelScale forwards to
	// BaseClass::GetModelScale, which is CBaseAnimating::m_flModelScale. There
	// is one size a player is and both questions ask it.
	//
	// The two questions ARE different, which is worth saying because the shared
	// answer looks like a coincidence. IMovementPlayer wants it for the HULL --
	// shareddefs.h multiplies every view height and every duck-hull corner by
	// it, so a half-size player ducks under half-size doorways. The shell wants
	// it for the SKELETON. Stock ties them together and so does this; a player
	// drawn at one size and collided at another is the divergence.
	//
	// Qualified, because CStandaloneEntity::GetModelScale and
	// IMovementPlayer::GetModelScale are two members of two bases and an
	// unqualified call inside this override is ambiguous.
	virtual float           GetModelScale() const OVERRIDE
	{
		return CStandaloneEntity::GetModelScale();
	}

	// --- state the arms read and the commands write ---------------------------
	const Vector &AbsVelocity() const { return m_vecAbsVelocity; }
	void SetAbsVelocity( const Vector &v ) { m_vecAbsVelocity = v; }

	// The observer switch, which the managed death cycle drives: a dead player
	// watches, a respawning one stops watching. Solidity follows it.
	void SetObserver( bool bObserver, int nMode );

	// The team, and the switch. What ITeams.Join decides (through its
	// single-owner policy hook) and the PlayerSetTeam slot carries out:
	// the store of CBaseEntity::ChangeTeam (baseentity.cpp:4947 --
	// `m_iTeamNum = iTeamNum`), and nothing more. No validation, no
	// roster move, no model swap, no suicide -- those are managed
	// decisions, and this side must not make them (plan porting rules).
	// TEAM_UNASSIGNED (0) is the born answer, as stock gives a player
	// who has not picked a side.
	int  TeamNumber() const { return m_iTeamNum; }
	void ChangeTeam( int iTeamNum ) { m_iTeamNum = iTeamNum; }

	// --- THE PHYSICS SHADOW (plan_entity.md step 3f, §3.8) --------------------
	//
	// What makes a player able to PUSH a crate. Until this step the live gate's
	// `push` step was a recorded skip for exactly this absence, and the ledger's
	// reason was right: stock builds a player's shadow in
	// CBasePlayer::InitVCollision / SetupVPhysicsShadow (player.cpp:8569, :8269)
	// and nothing on this hierarchy did.
	//
	// A player does not MOVE through VPhysics -- the movement loop is managed
	// (step 3e) and traces the world itself. The shadow is a second body,
	// kept at the player's position by an IPhysicsPlayerController, whose only
	// job is to be in the solver so the solver can push what it meets. That is
	// stock's arrangement too and not a simplification of it.
	//
	// THE BODY IS MANAGED'S (plan_physics_bones_native_zero 2.5):
	// PlayerShadowBodies builds it through vphysics' own tables, with the
	// numbers SsPlayer states, and decides everything after. This side holds
	// the pointers (AdoptShadow) and owns the teardown.

	// Tears the shadow down. Idempotent, and called from the destructor as well
	// as from the disconnect path, because a player edict outlives a disconnect
	// on some paths and a controller left in the solver is a dangling body.
	void DestroyVCollision();

	// Whether this player has one. What the readout and the tests ask.
	bool HasVCollision() const { return m_pPhysicsController != NULL; }

	// THE MANAGED SHADOW'S NATIVE HALF (plan_physics_bones_native_zero 2.5).
	// PlayerShadowBodies builds the body and calls vphysics itself; these
	// read the player, hold the pointers it built, and write what a command
	// leaves on the player. Nothing here decides.
	void ShadowFacts( SourceSharpPlayerShadowFacts *pOut );

	// One managed command's two engine-side halves (plan_server_native_v2
	// T4g): what the engine's side holds, and the move's result carried out.
	void CommandFacts( SourceSharpPlayerCommandFacts *pOut );
	void FinishCommand( const SourceSharpPlayerCommandResult &result );
	void AdoptShadow( IPhysicsObject *pStand, IPhysicsObject *pCrouch, IPhysicsPlayerController *pController );
	void CommitShadow( int nState, bool bAfterCommand );

	// B7: whether a MOVETYPE_VPHYSICS mover is penetrating the player.


	// STOCK'S m_touchedPhysObject IS MANAGED'S (2026-10-12). CBasePlayer clears
	// it at the top of every command (player.cpp:3801) and sets it from
	// CBasePlayer::Touch (player.cpp:4761) when the move ran into something the
	// solver can move; both arms of PostThinkVPhysics that soften the shove are
	// gated on it (measured: with the controller's IsInContact() standing in,
	// 94% of a push ran the full-force arm). The decision is
	// PlayerShadowBodies.TouchedMoveablePhysics; native records the touch list.


	// What this player is standing on, for the one rule that has to know:
	// stock's Touch ignores the ground, because leaning on the floor is not
	// shoving it.
	const CBaseHandle &GroundHandle() const { return m_hGround; }

public:
	// One entry per connected client, NULL when the slot is empty. Index by
	// entindex (1..MAX_PLAYERS); [0] is never used. A flat array rather than a
	// walk because the arms in gameinterface.cpp ask per packet.
	static CStandalonePlayer *FromEdict( const edict_t *pEdict );
	static CStandalonePlayer *FromIndex( int nEntIndex );

	// Runs every connected standalone player's queued commands. Called once
	// per server tick, before the standalone frame sweep, so an entity's think
	// sees where the players moved THIS tick.
	static void RunAllQueuedCommands();

protected:
	// Death is a game rule; the base reports the managed verdict and this
	// override only mirrors what the verdict already decided into the state
	// the engine reads (deadflag).
	virtual void OnManagedKilled( IGameEntity *pAttacker ) OVERRIDE;

private:
	// Which hull is in the solver. Stock's SetVCollisionState (player.cpp:8620)
	// with VPhysicsSwapObject dropped -- see the definition for why the shell's
	// own m_pPhysics is deliberately left NULL.
	void SetVCollisionState( const Vector &vecOrigin, const Vector &vecVelocity, int nState );

	// m_PlayerState IS GONE -- step 3c put it in the block's server tail. See
	// PlayerStatePtr() above; playerinfo_block_test.cpp's
	// PlayerStateInTheBlock.TheShellDeclaresNoSecondCopyOfIt pins its absence by
	// name (over a comment-stripped read of this header, so this very line
	// cannot be what keeps that check green), so a member reintroduced beside
	// the block's copy is a red test rather than two players' worth of divergent
	// view angles.
	ss::CPlayerLocalData m_Local;

	// The loop's scratch state.
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

	// Position-adjacent state the loop reads through the interface.
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

	// The team this player is on (TEAM_UNASSIGNED until something calls
	// ChangeTeam). What GetPlayerTeam answers and PlayerSetTeam writes.
	int            m_iTeamNum;

	// The shadow, and the controller that drags it after the player. NULL until
	// managed code asks for one; see InitVCollision.
	IPhysicsObject           *m_pShadowStand;
	IPhysicsObject           *m_pShadowCrouch;
	IPhysicsObject           *m_pShadowActive;
	IPhysicsPlayerController *m_pPhysicsController;

	// WHICH ENVIRONMENT BUILT THEM. The solver is destroyed and recreated at
	// every level change (CPhysicsHook::LevelShutdownPostEntity /
	// PhysicsLevelInit), and an object outlives its environment only as a
	// pointer. Destroying a stale one THROUGH THE NEW ENVIRONMENT is not a
	// no-op: it is a free of whatever the new environment put at that address,
	// and the visible symptom is the NEXT map's props falling through its floor.
	// So the teardown compares, and drops the pointers rather than freeing them
	// when the environment that owned them has gone.
	IPhysicsEnvironment       *m_pShadowEnv;
	int                       m_nVCollisionState;
	// The frame guard and how many commands are waiting: the commands, their
	// clock and the tick base are the managed command loop's (T4g).
	int            m_nSimulationTick;
	int            m_nPendingCmds;

	char           m_szPlayerName[ 128 ];
};

// Creates the standalone player for a connecting client, through the factory
// the managed registration installed, bound to the engine's edict. NULL — with
// a warning — when the managed side never registered the class, which the
// caller treats as fatal: this is the only player this build has.
CStandalonePlayer *StandalonePlayer_Create( edict_t *pEdict, const char *pPlayerName );

// A standalone client's console commands, handled where the SDK player's
// ::ClientCommand used to swallow them against a NULL resolve. The dev set —
// kill, hurtme, setpos, noclip — is what live verification stands on, and
// every one but kill keeps stock's sv_cheats gate.
void StandalonePlayer_ClientCommand( CStandalonePlayer *pPlayer, const CCommand &args );



// A managed handle as a standalone player, or NULL for everything else —
// including a standalone entity that is not one. The projection the player
// arms in shell_entityapi.cpp resolve through, shaped like the rest of the
// shell_resolve.h family.
CStandalonePlayer *SourceSharp_ResolveStandalonePlayer( uint32_t hEntity );

// FOR THE DEBUG-ONLY CONSOLE (shell_standalone_player_debug.cpp). The state
// behind both stays file-static in shell_standalone_player.cpp.
//
// The player who issued the console command being dispatched, or NULL: the
// server console, or a slot whose player has not spawned. ss_ground's subject.
CStandalonePlayer *StandalonePlayer_CommandClient();

// cs_sv_playershadow is managed's (PlayerShadowCommand, debug builds): the
// counters it printed are PlayerShadowBodies', and the two that stay native
// (live, outlived) cross through SourceSharp_PlayerShadowCounts.


//-----------------------------------------------------------------------------
// THE SHELL TO THE PLAYER, on the same rule and with the same debug check as
// SourceSharp_ShellOf in shell_standalone.h. The caller still asks IsPlayer()
// first -- that is the game's question and it stays at the call site; this is
// only the conversion, in one place instead of five.
//-----------------------------------------------------------------------------
inline CStandalonePlayer *SourceSharp_PlayerOf( CStandaloneEntity *pEnt )
{
	Assert( !pEnt || dynamic_cast< CStandalonePlayer * >( pEnt ) != NULL );
	return static_cast< CStandalonePlayer * >( pEnt );
}

inline CStandalonePlayer *SourceSharp_PlayerOf( IGameEntity *pEnt )
{
	return SourceSharp_PlayerOf( SourceSharp_ShellOf( pEnt ) );
}
