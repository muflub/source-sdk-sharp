//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
//
// The standalone player's engine-facing state, the command path's native half, and the
// physics shadow's pointers.

#include "ss_util.h"
#include "ss_shareddefs.h"
#include "ss_physics_shared.h"

#include "ss_gamestringpool.h"
extern CGlobalVars *gpGlobals;
int UTIL_GetCommandClientIndex();
#include "ss_recipient_filter.h"
#include "igamerules.h"
#include "shell_standalone_player.h"
#include "movement_ground.h"
#include "standalone_player_sync.h"
#include "shell_server.h"

#include "shell_gamemovement.h"

#include "vphysics/player_controller.h"

#include "physicsowner.h"

#include "ss_takedamageinfo.h"

#include "native_command_body.h"
#include "ss_string.h"
#include "ss_entityfactory.h"

// Stock's player physics-shadow states (player.h VPHYS_*).
namespace
{
enum
{
	VPHYS_WALK = 0,
	VPHYS_CROUCH,
	VPHYS_NOCLIP,
};
}

// Field placements, resolved once per generation and shared by every player.
static SourceSharpPlayerFields_t s_PlayerFields;

static bool PlayerFieldsCurrent()
{
	if ( s_PlayerFields.IsCurrent() )
		return true;

	return SourceSharpPlayerFields_Resolve( &s_PlayerFields );
}

// Native state to blob, after anything that moved it.
void CStandalonePlayer::SyncToBlob()
{
	if ( !PlayerFieldsCurrent() )
		return;

	// The engine side's three words only; the managed command loop publishes movement state.
	SourceSharpPlayerSync_WriteEngineSide( DataBlob(), s_PlayerFields, GetAbsOrigin(), GetHealth(), MvIsAlive() );

	NetworkStateChanged();
}

int CStandalonePlayer::TakeDamage( const ss::CTakeDamageInfo &info )
{
	// Synced immediately so the HUD never reads a stale health.
	const int nTaken = BaseClass::TakeDamage( info );

	if ( nTaken > 0 )
		SyncToBlob();

	return nTaken;
}

// Connected players by entindex, asked once per packet and per snapshot.
static CStandalonePlayer *s_StandalonePlayers[ss::kMaxPlayers + 1];

CStandalonePlayer *CStandalonePlayer::FromEdict( const edict_t *pEdict )
{
	if ( !pEdict )
		return NULL;

	return FromIndex( engine->IndexOfEdict( const_cast<edict_t *>( pEdict ) ) );
}

CStandalonePlayer *CStandalonePlayer::FromIndex( int nEntIndex )
{
	if ( nEntIndex < 1 || nEntIndex > ss::kMaxPlayers )
		return NULL;

	return s_StandalonePlayers[nEntIndex];
}

CStandalonePlayer::CStandalonePlayer()
	: m_flWaterJumpTime( 0.0f )
	, m_vecWaterJumpVel( 0, 0, 0 )
	, m_flSwimSoundTime( 0.0f )
	, m_flStepSoundTime( 0.0f )
	, m_flSurfaceFriction( 1.0f )
	, m_pSurfaceData( NULL )
	, m_nSurfaceProps( 0 )
	, m_chTextureType( 0 )
	, m_chPreviousTextureType( 0 )
	, m_vecLadderNormal( 0, 0, 0 )
	, m_nStuckLast( 0 )
	, m_nNumCrouches( 0 )
	, m_vecAbsVelocity( 0, 0, 0 )
	, m_vecBaseVelocity( 0, 0, 0 )
	, m_vecViewOffset( 0, 0, 64 )
	, m_vecPreviouslyPredictedOrigin( 0, 0, 0 )
	, m_flGravity( 1.0f )
	, m_flMaxSpeed( 320.0f )
	, m_fFlags( 0 )
	, m_MoveType( MOVETYPE_WALK )
	, m_MoveCollide( MOVECOLLIDE_DEFAULT )
	, m_nWaterLevel( 0 )
	, m_nWaterType( 0 )
	, m_bObserver( false )
	, m_nObserverMode( 0 )
	, m_pShadowStand( NULL )
	, m_pShadowCrouch( NULL )
	, m_pShadowActive( NULL )
	, m_pPhysicsController( NULL )
	, m_pShadowEnv( NULL )
	, m_nVCollisionState( -1 )
	, m_nSimulationTick( -1 )
	, m_nPendingCmds( 0 )
{
	m_szPlayerName[0] = 0;

	// Constructed, not just zeroed: CPlayerState has a virtual destructor and so a vtable
	// pointer. Construct() because memdbgon.h redefines new.
	Construct( PlayerStatePtr() );

	// -1 because frame 0 is a real frame.
	Block().Server.ViewOrigin.Init();
	Block().Server.ViewOriginFrame = -1;

	// Stock's construction defaults, until the movement convars are applied.
	m_Local.m_flStepSize = 18.0f;
	m_Local.m_bAllowAutoMovement = true;
}

CStandalonePlayer::~CStandalonePlayer()
{
	// A controller left in the solver would drag a body whose owner was freed.
	DestroyVCollision();

	Destruct( PlayerStatePtr() );

	const int nIndex = EntIndex();

	if ( nIndex >= 1 && nIndex <= ss::kMaxPlayers && s_StandalonePlayers[nIndex] == this )
		s_StandalonePlayers[nIndex] = NULL;
}

void CStandalonePlayer::SetPlayerName( const char *pName )
{
	ss::StrCopy( m_szPlayerName, pName ? pName : "", sizeof( m_szPlayerName ) );
	PlayerState().netname = AllocPooledString( m_szPlayerName );
}

// Placed with a player's shape; the managed OnSpawn supplies model, health and state.
void CStandalonePlayer::Spawn()
{
	m_fFlags |= FL_CLIENT;
	m_MoveType = MOVETYPE_WALK;
	PlayerState().deadflag = false;
	PlayerState().fixangle = FIXANGLE_NONE;

	IManagedClients *pApi = ManagedClients();

	// Managed code places the player inside this call, before it is solid and before the
	// hull is set. The returned handle is deliberately ignored.
	if ( pApi )
		(void)pApi->OnSelectSpawnPoint( ManagedHandle() );

	SetCollisionBounds( g_pGameRules->GetViewVectors()->m_vHullMin, g_pGameRules->GetViewVectors()->m_vHullMax );
	m_vecViewOffset = g_pGameRules->GetViewVectors()->m_vView;

	SetSolidity( SOLID_BBOX, FSOLID_NOT_STANDABLE );

	BaseClass::Spawn();

	// The SDK player's two spawn announcements, in its order.
	if ( pApi )
		pApi->OnPlayerSpawn( ManagedHandle() );
	if ( pApi )
		pApi->OnPlayerSpawned( ManagedHandle() );

	SyncToBlob();
}

// The ground, held as a handle because the thing stood on can die first. It may be on
// either hierarchy.
IHandleEntity *CStandalonePlayer::MvGetGroundEntity()
{
	// Resolves the world too, which is in neither the slot table nor the SDK list.
	return SourceSharp_EntityFromHandle( m_hGround );
}

void CStandalonePlayer::MvSetGroundEntity( IHandleEntity *pGround )
{
	m_hGround = pGround ? pGround->GetRefEHandle() : CBaseHandle();

	if ( pGround )
		m_fFlags |= FL_ONGROUND;
	else
		m_fFlags &= ~FL_ONGROUND;
}

void CStandalonePlayer::SetObserver( bool bObserver, int nMode )
{
	m_bObserver = bObserver;
	m_nObserverMode = nMode;

	if ( bObserver )
	{
		SetSolidity( SOLID_NONE, FSOLID_NOT_SOLID );
		m_MoveType = MOVETYPE_NOCLIP;
	}
	else
	{
		SetSolidity( SOLID_BBOX, FSOLID_NOT_STANDABLE );
		m_MoveType = MOVETYPE_WALK;
	}
}

void CStandalonePlayer::OnManagedKilled( IGameEntity *pAttacker )
{
	// Mirrors the managed verdict into the block.
	PlayerState().deadflag = true;

	// Announced before the scripted relay so managed OnKilled sees the death recorded. No
	// attacker is the invalid handle, never 0 (the world).
	const uint32_t hAttacker = SourceSharp_HandleFromEntity( pAttacker );

	IManagedClients *pApi = ManagedClients();
	if ( pApi )
		pApi->OnPlayerKilled( ManagedHandle(), hAttacker );

	BaseClass::OnManagedKilled( pAttacker );
}

// A managed handle back to its player. The serial is checked, not just the index, so a
// later occupant of the slot never answers. 0 and INVALID_EHANDLE_INDEX are refused.
static CStandalonePlayer *StandalonePlayer_FromManagedHandle( uint32_t hPlayer )
{
	if ( hPlayer == 0 || hPlayer == (uint32_t)INVALID_EHANDLE_INDEX )
		return NULL;

	const CBaseHandle handle = CBaseHandle::UnsafeFromIndex( (int)hPlayer );
	CStandalonePlayer *pPlayer = CStandalonePlayer::FromIndex( handle.GetEntryIndex() );

	return ( pPlayer && pPlayer->ManagedHandle() == hPlayer ) ? pPlayer : NULL;
}

// C# decoded, planned and runs the queue; native counts what waits for the frame.
void CStandalonePlayer::QueuedUsercmds( int nCount )
{
	if ( nCount > 0 )
		m_nPendingCmds += nCount;
}

void CStandalonePlayer::RunAllQueuedCommands()
{
	for ( int i = 1; i <= ss::kMaxPlayers; i++ )
	{
		if ( s_StandalonePlayers[i] )
			s_StandalonePlayers[i]->RunQueuedCommands();
	}
}

void CStandalonePlayer::RunQueuedCommands()
{
	if ( m_nSimulationTick == gpGlobals->tickcount )
		return;

	m_nSimulationTick = gpGlobals->tickcount;

	if ( m_nPendingCmds == 0 )
		return;

	// The managed loop sets each command's clock; the frame's is restored after the batch.
	const float flSaveCurtime = gpGlobals->curtime;
	const float flSaveFrametime = gpGlobals->frametime;

	// One crossing for the whole queue.
	IManagedPlayerCommands *pCommands = ManagedPlayerCommands();

	if ( pCommands )
	{
		pCommands->PlayerRunCommands( ManagedHandle(), m_nPendingCmds );
	}
	else
	{
		Warning( "[SourceSharp] no managed run loop; %d queued command(s) dropped\n", m_nPendingCmds );
	}

	m_nPendingCmds = 0;

	gpGlobals->curtime = flSaveCurtime;
	gpGlobals->frametime = flSaveFrametime;
}

// One managed command's engine side: native answers what the engine holds (origin,
// CPlayerState, health, ground velocity) and applies the result.
COMPILE_TIME_ASSERT( sizeof( SourceSharpPlayerCommandFacts ) == 80 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandFacts, vecOrigin ) == 8 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandFacts, nFixAngle ) == 44 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandFacts, nEntIndex ) == 60 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandFacts, vecGroundVelocity ) == 68 );

COMPILE_TIME_ASSERT( sizeof( SourceSharpPlayerCommandResult ) == 76 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandResult, flYaw ) == 20 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandResult, vecViewOffset ) == 32 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerCommandResult, hGround ) == 68 );

void CStandalonePlayer::CommandFacts( SourceSharpPlayerCommandFacts *pOut )
{
	const Vector &vecOrigin = GetAbsOrigin();
	pOut->vecOrigin[0] = vecOrigin.x;
	pOut->vecOrigin[1] = vecOrigin.y;
	pOut->vecOrigin[2] = vecOrigin.z;

	const CPlayerState &pl = PlayerState();
	pOut->angVAngle[0] = pl.v_angle.x;
	pOut->angVAngle[1] = pl.v_angle.y;
	pOut->angVAngle[2] = pl.v_angle.z;
	pOut->angAngleChange[0] = pl.anglechange.x;
	pOut->angAngleChange[1] = pl.anglechange.y;
	pOut->angAngleChange[2] = pl.anglechange.z;
	pOut->nFixAngle = pl.fixangle;
	pOut->bDeadFlag = pl.deadflag ? 1 : 0;

	pOut->nHealth = GetHealth();
	pOut->flModelScale = GetModelScale();
	pOut->nEntIndex = EntIndex();

	// NULL for a ground entity that has gone.
	IHandleEntity *pGround = SourceSharp_EntityFromHandle( CBaseHandle::UnsafeFromIndex( (int)pOut->hGround ) );
	pOut->bGroundResolves = pGround ? 1 : 0;

	const Vector vecGroundVelocity = MovementGroundVelocity( pGround );
	pOut->vecGroundVelocity[0] = vecGroundVelocity.x;
	pOut->vecGroundVelocity[1] = vecGroundVelocity.y;
	pOut->vecGroundVelocity[2] = vecGroundVelocity.z;
}

void CStandalonePlayer::FinishCommand( const SourceSharpPlayerCommandResult &result )
{
	// Written before the trigger sweep below can call back into anything that reads them.
	m_fFlags = result.nFlags;
	m_MoveType = (MoveType_t)result.nMoveType;
	m_vecViewOffset.Init( result.vecViewOffset[0], result.vecViewOffset[1], result.vecViewOffset[2] );
	m_vecAbsVelocity.Init( result.vecVelocity[0], result.vecVelocity[1], result.vecVelocity[2] );
	m_vecBaseVelocity.Init( result.vecBaseVelocity[0], result.vecBaseVelocity[1], result.vecBaseVelocity[2] );
	m_hGround = CBaseHandle::UnsafeFromIndex( (int)result.hGround );
	m_Local.m_flFallVelocity = result.flFallVelocity;

	// FinishMove's engine-facing writes; the body takes the yaw alone.
	SetAbsOrigin( Vector( result.vecOrigin[0], result.vecOrigin[1], result.vecOrigin[2] ) );
	SetAbsAngles( QAngle( 0, result.flYaw, 0 ) );

	// ProcessImpacts' trigger sweep: which triggers the player stands in now.
	TouchTriggers( GetAbsOrigin() );
}

int32_t SourceSharp_PlayerCommandBegin( uint32_t hPlayer, SourceSharpPlayerCommandFacts *pFacts )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );

	if ( !pPlayer || !pFacts || pFacts->struct_size != sizeof( *pFacts ) )
		return 0;

	// Managed movement is the only movement.
	SourceSharp_MovementRequireManaged();

	pPlayer->CommandFacts( pFacts );
	return 1;
}

void SourceSharp_PlayerCommandView( uint32_t hPlayer, const float *pAnglePYR, int32_t nFixAngle )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );

	if ( !pPlayer || !pAnglePYR )
		return;

	pPlayer->PlayerState().v_angle.Init( pAnglePYR[0], pAnglePYR[1], pAnglePYR[2] );
	pPlayer->PlayerState().fixangle = nFixAngle;
}

void SourceSharp_PlayerCommandFinish( uint32_t hPlayer, const SourceSharpPlayerCommandResult *pResult )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );

	if ( !pPlayer || !pResult || pResult->struct_size != sizeof( *pResult ) )
		return;

	pPlayer->FinishCommand( *pResult );

	// Counts commands that ran, not ones asked for.
	if ( pResult->bRan )
		SourceSharp_MoveNoteServerCommand();
}

// A landing's damage, through the shell's damage path so managed rules see it. Answers
// whether the player is still alive.
int32_t SourceSharp_PlayerFallDamage( uint32_t hPlayer, float flDamage )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );

	if ( !pPlayer )
		return 1;

	if ( flDamage > 0 )
	{
		ss::CTakeDamageInfo info( NULL, NULL, flDamage, ss::kDmgFall );
		info.SetDamagePosition( pPlayer->GetAbsOrigin() );
		pPlayer->TakeDamage( info );
	}

	return pPlayer->GetHealth() > 0 ? 1 : 0;
}

// The engine side's three words into the networked state, after each command.
void SourceSharp_PlayerSyncEngineSide( uint32_t hPlayer )
{
	if ( CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer ) )
		pPlayer->SyncToBlob();
}

// The player's physics shadow: two hulls and a player controller kept in the solver at
// the player's position so it can push props (stock's CBasePlayer::InitVCollision and
// SetupVPhysicsShadow). Managed builds and drives it; native holds the pointers and
// owns the teardown. The solver never pushes the player back.
//
// Shadows still in the solver, and those that outlived their environment, which is
// recreated at every level change. physics.cpp checks both.
int g_nSourceSharpPlayerShadowsLive = 0;
int g_nSourceSharpPlayerShadowsOutlived = 0;

// Copies live, outlived into pOut.
int32_t SourceSharp_PlayerShadowCounts( int32_t *pOut, int32_t nMax )
{
	if ( !pOut || nMax <= 0 )
		return 0;

	const int32_t values[] = { g_nSourceSharpPlayerShadowsLive, g_nSourceSharpPlayerShadowsOutlived };
	const int32_t nAll = int32_t( sizeof( values ) / sizeof( values[0] ) );
	const int32_t n = nMax < nAll ? nMax : nAll;
	for ( int32_t i = 0; i < n; ++i )
		pOut[i] = values[i];
	return n;
}

// Facts for the managed shadow. The destructor is the body's one teardown owner.
COMPILE_TIME_ASSERT( sizeof( SourceSharpPlayerShadowFacts ) == 192 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, pStand ) == 16 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, pController ) == 32 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, vecOrigin ) == 40 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, flOutStepHeight ) == 76 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, hGround ) == 100 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, pGroundObject ) == 112 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, nTouched ) == 120 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpPlayerShadowFacts, hTouched ) == 128 );

void CStandalonePlayer::ShadowFacts( SourceSharpPlayerShadowFacts *pOut )
{
	memset( pOut, 0, sizeof( *pOut ) );
	pOut->struct_size = sizeof( *pOut );

	pOut->bHasShadow = m_pPhysicsController && m_pShadowStand && m_pShadowCrouch ? 1 : 0;
	pOut->bBodyCurrent = m_pShadowEnv && m_pShadowEnv == physenv ? 1 : 0;
	pOut->nState = m_nVCollisionState;
	pOut->pStand = m_pShadowStand;
	pOut->pCrouch = m_pShadowCrouch;
	pOut->pController = m_pPhysicsController;

	const Vector &vecOrigin = GetAbsOrigin();
	pOut->vecOrigin[0] = vecOrigin.x;
	pOut->vecOrigin[1] = vecOrigin.y;
	pOut->vecOrigin[2] = vecOrigin.z;
	pOut->vecVelocity[0] = m_vecAbsVelocity.x;
	pOut->vecVelocity[1] = m_vecAbsVelocity.y;
	pOut->vecVelocity[2] = m_vecAbsVelocity.z;

	pOut->flMaxSpeed = m_flMaxSpeed;
	pOut->nFlags = m_fFlags;
	pOut->nMoveType = m_MoveType;

	pOut->flFrameTime = gpGlobals->frametime;
	pOut->flTickInterval = gpGlobals->interval_per_tick;

	// Managed decides whether the ground is physics ground.
	pOut->hGround = INVALID_EHANDLE_INDEX;
	if ( IHandleEntity *pGround = MvGetGroundEntity() )
	{
		pOut->hGround = (uint32_t)pGround->GetRefEHandle().ToInt();
		const PhysicsOwner_t owner = PhysicsOwnerOfHandle( pGround->GetRefEHandle() );
		if ( owner.IsValid() )
		{
			pOut->nGroundMoveType = (int32_t)owner.GetMoveType();
			if ( owner.pGame )
				pOut->pGroundObject = StandaloneEntity_PhysicsObject( owner.pGame );
		}
	}
}

void CStandalonePlayer::AdoptShadow( IPhysicsObject *pStand, IPhysicsObject *pCrouch,
									 IPhysicsPlayerController *pController )
{
	m_pShadowStand = pStand;
	m_pShadowCrouch = pCrouch;
	m_pPhysicsController = pController;
	m_pShadowActive = NULL;
	m_nVCollisionState = -1;

	// Remembered so the teardown can tell whether the environment that built it still exists.
	m_pShadowEnv = physenv;
	g_nSourceSharpPlayerShadowsLive++;
}

void CStandalonePlayer::CommitShadow( int nState, bool bAfterCommand )
{
	m_nVCollisionState = nState;
	m_pShadowActive = nState == VPHYS_WALK ? m_pShadowStand : nState == VPHYS_CROUCH ? m_pShadowCrouch : NULL;

	// The managed command loop consumes the step height.
	(void)bAfterCommand;
}

int32_t SourceSharp_PlayerShadowFacts( uint32_t hPlayer, SourceSharpPlayerShadowFacts *pOut )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );
	if ( !pPlayer || !pOut )
		return 0;

	pPlayer->ShadowFacts( pOut );
	return 1;
}

int32_t SourceSharp_PlayerShadowAdopt( uint32_t hPlayer, void *pStand, void *pCrouch, void *pController )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );
	if ( !pPlayer || !pStand || !pCrouch || !pController || !physenv )
		return 0;

	// Stock destroys before it builds.
	pPlayer->DestroyVCollision();
	pPlayer->AdoptShadow( static_cast<IPhysicsObject *>( pStand ), static_cast<IPhysicsObject *>( pCrouch ),
						  static_cast<IPhysicsPlayerController *>( pController ) );
	return 1;
}

void SourceSharp_PlayerShadowRelease( uint32_t hPlayer )
{
	if ( CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer ) )
		pPlayer->DestroyVCollision();
}

void SourceSharp_PlayerShadowCommit( uint32_t hPlayer, int32_t nState, int32_t bAfterCommand )
{
	if ( CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer ) )
		pPlayer->CommitShadow( nState, bAfterCommand != 0 );
}

void CStandalonePlayer::DestroyVCollision()
{
	// physenv is recreated at every level change. A shadow from a replaced environment is
	// dangling, so it is dropped without freeing: the old environment took it.
	IPhysicsEnvironment *pEnv = ( physenv == m_pShadowEnv ) ? physenv : NULL;

	// Counted once per set, whether freed or dropped.
	if ( m_pShadowEnv )
		g_nSourceSharpPlayerShadowsLive--;

	if ( m_pPhysicsController )
	{
		if ( pEnv )
			pEnv->DestroyPlayerController( m_pPhysicsController );
		m_pPhysicsController = NULL;
	}

	// Clear the game data first: DestroyObject is deferred while the solver steps, and a
	// callback may still see the object.
	if ( m_pShadowStand )
	{
		if ( pEnv )
		{
			m_pShadowStand->EnableCollisions( false );
			m_pShadowStand->SetGameData( NULL );
			pEnv->DestroyObject( m_pShadowStand );
		}
		m_pShadowStand = NULL;
	}

	if ( m_pShadowCrouch )
	{
		if ( pEnv )
		{
			m_pShadowCrouch->EnableCollisions( false );
			m_pShadowCrouch->SetGameData( NULL );
			pEnv->DestroyObject( m_pShadowCrouch );
		}
		m_pShadowCrouch = NULL;
	}

	m_pShadowActive = NULL;
	m_pShadowEnv = NULL;
	m_nVCollisionState = -1;
}

// ClientCommand for a hierarchy player: words that are not registered ConCommands
// (ss_use, ss_slot, invnext, ...) go to managed OnClientCommand.
void StandalonePlayer_ClientCommand( CStandalonePlayer *pPlayer, const CCommand &args )
{
	if ( !pPlayer || args.ArgC() < 1 )
		return;

	IManagedCommands *pCommands = ManagedCommands();

	if ( !pCommands )
	{
		Msg( "%s: the managed server is not loaded, so nothing answered.\n", args[0] );
		return;
	}

	// The whole line, quotes and all; ArgS would drop the verb.
	pCommands->OnClientCommand( pPlayer->ManagedHandle(), args.GetCommandString() );
}

// Who typed it. A registered ConCommand is dispatched by the engine and must ask for its
// subject, as stock's handlers did through UTIL_GetCommandClient.
CStandalonePlayer *StandalonePlayer_CommandClient()
{
	return CStandalonePlayer::FromIndex( UTIL_GetCommandClientIndex() );
}

// The same, for a C# [ConCommand]. INVALID_EHANDLE_INDEX when no player issued it.
uint32_t SourceSharp_GetCommandClientHandle()
{
	if ( CStandalonePlayer *pPlayer = StandalonePlayer_CommandClient() )
		return pPlayer->ManagedHandle();

	return INVALID_EHANDLE_INDEX;
}

// `use` is ss_use under stock's name. It needs a registration because
// engine->ServerCommand, which tools drive the game through, only reaches a ConCommand.
static void StandalonePlayer_RouteVerbatim( const CCommand &args )
{
	const char *pName = args.ArgC() >= 1 ? args[0] : "command";
	CStandalonePlayer *pPlayer = StandalonePlayer_CommandClient();

	if ( !pPlayer )
	{
		Msg( "%s: no player issued that, so nothing was done.\n", pName );
		return;
	}

	IManagedCommands *pCommands = ManagedCommands();

	if ( !pCommands )
	{
		Msg( "%s: the managed server is not loaded, so nothing answered.\n", pName );
		return;
	}

	// The whole line, quotes and all, so a quoted argument survives to the managed splitter.
	if ( !pCommands->OnClientCommand( pPlayer->ManagedHandle(), args.GetCommandString() ) )
		Msg( "%s: the managed server did not handle it.\n", pName );
}

SS_NATIVE_COMMAND( use )
{
	StandalonePlayer_RouteVerbatim( args );
}

CStandalonePlayer *SourceSharp_ResolveStandalonePlayer( uint32_t hEntity )
{
	CStandaloneEntity *pEntity = SourceSharp_ResolveStandalone( hEntity );

	if ( !pEntity || !pEntity->IsPlayer() )
		return NULL;

	return SourceSharp_PlayerOf( pEntity );
}

// Creation, from ClientPutInServer.
CStandalonePlayer *StandalonePlayer_Create( edict_t *pEdict, const char *pPlayerName )
{
	if ( !pEdict )
		return NULL;

	ss::IEntityFactory *pFactory = ss::EntityFactoryDictionary()->FindFactory( "ss_player" );

	if ( !pFactory )
	{
		Warning( "[SourceSharp] standalone player: no 'ss_player' class was registered — "
				 "is the managed assembly older than -ss_player?\n" );
		return NULL;
	}

	SourceSharp_SetPendingPlayerEdict( pEdict );

	IServerNetworkable *pNet = pFactory->Create( "ss_player" );

	// Normally claimed by PostConstruct; a failed creation must not leak it to the next entity.
	SourceSharp_ClaimPendingPlayerEdict();

	if ( !pNet )
		return NULL;

	// The factory constructs CStandalonePlayer only for the bPlayer class, which ss_player is.
	CStandalonePlayer *pPlayer = SourceSharp_PlayerOf( SourceSharp_ShellOf( pNet ) );

	const int nIndex = pPlayer->EntIndex();

	if ( nIndex >= 1 && nIndex <= ss::kMaxPlayers )
	{
		s_StandalonePlayers[nIndex] = pPlayer;
	}
	else
	{
		Warning( "[SourceSharp] standalone player: bound to edict index %d, outside 1..%d\n", nIndex, ss::kMaxPlayers );
	}

	pPlayer->SetPlayerName( pPlayerName );

	return pPlayer;
}

ss::CPlayerLocalData::CPlayerLocalData()
{
#ifdef _DEBUG
	m_vecOverViewpoint.Init();
	m_vecPunchAngle.Init();
#endif
	m_audio.soundscapeIndex = 0;
	m_audio.localBits = 0;
	m_audio.entIndex = 0;
	m_pOldSkyCamera = NULL;
	m_bDrawViewmodel = true;

	m_szScriptOverlayMaterial.GetForModify()[0] = '\0';
}

// The sky camera's parameters, else area 255 (no 3D skybox).
static void StandaloneClientData_Update( CStandalonePlayer *pPlayer )
{
	MvLocalData_t &local = pPlayer->MvLocal();

	ss::sky3dparams_t managed;
	if ( SourceSharp_FillSkyboxParams( managed ) )
	{
		local.m_skybox3d.CopyFrom( managed );
		return;
	}

	local.m_skybox3d.area = 255;
}

void UpdateAllClientData()
{
	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromIndex( i ) )
			StandaloneClientData_Update( pStandalone );
	}
}
