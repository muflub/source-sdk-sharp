//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// Movement parameters, the counters and refusals around the managed movement loop, and
// the client's IGameMovement shim with the CMoveData copy and its servicing calls.
// Duck timing stays native: m_flDucktime is predicted state initialised from a constant.

#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif
#ifdef CLIENT_DLL
#include "igamerules.h"
#else
#include "igamerules.h"
#endif
// From their own headers: baseplayer_shared.h would bring in the whole player.
#include "PlayerState.h"
#ifdef CLIENT_DLL
#include "client_entity.h"
#include "c_playerlocaldata.h"
#else
#include "ss_playerlocaldata.h"
#endif
#include "imovementplayer.h"
#ifdef CLIENT_DLL
#include "igamemovement.h"
#endif
#ifdef CLIENT_DLL

#define SS_MOVE_HELPER() MoveHelper()
#define SS_PLAYER_ANIM( n ) ( (PLAYER_ANIM)( n ) )
#endif
#ifdef CLIENT_DLL
#include "movevars_shared.h"
#endif
#ifndef CLIENT_DLL
#include "managed_convar_ref.h"
#include "server_engine.h"
#endif
#include "movement_ground.h"
#include "movement_ground_writeback.h"

#include "imanaged_areas.h"
#ifdef CLIENT_DLL
#include "imanaged_areas_client.h"
#endif
#include "shell_gamemovement.h"

#ifndef CLIENT_DLL
// Server: stock macro names aliased to server.so's own spellings so the shared body
// reads the same on both sides.
#include "vphysics_interface.h"
#include "ss_util.h"
#include "ss_shareddefs.h"
#include "server_movement.h"
#include "server_entity_resolver.h"

#define RUMBLE_FLAGS_NONE ss::kRumbleFlagsNone
#define VEC_VIEW_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vView * player->GetModelScale() )
#define VEC_HULL_MIN_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vHullMin * player->GetModelScale() )
#define VEC_HULL_MAX_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vHullMax * player->GetModelScale() )
#define VEC_DUCK_HULL_MIN_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vDuckHullMin * player->GetModelScale() )
#define VEC_DUCK_HULL_MAX_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vDuckHullMax * player->GetModelScale() )
#define VEC_DUCK_VIEW_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vDuckView * player->GetModelScale() )

#endif
// A handle's entity through the slot table; stock's CBaseHandle::Get() reads an entity
// list neither side builds.
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle );
#define SS_HANDLE_ENTITY( h ) SourceSharp_EntityFromHandle( h )

#ifdef CLIENT_DLL
#define SOURCESHARP_MOVE_SIDE_TAG "client"
#else
#define SOURCESHARP_MOVE_SIDE_TAG "server"
#endif

#ifdef CLIENT_DLL
unsigned int SourceSharp_MovementRefusalCount( int nReason );
#endif

// Defined in shell_gamerules.cpp: the hulls are read through g_pGameRules->GetViewVectors().
void SourceSharp_PublishViewVectors( const SourceSharpMovementParams *pParams );

// Stock's numbers, so movement works from the first tick, before managed registers any.
static SourceSharpMovementParams s_Movement = {
	21.0f, // flJumpHeight — GAMEMOVEMENT_JUMP_HEIGHT
	600.0f, // flGravity
	4.0f, // flFriction
	10.0f, // flAccelerate
	10.0f, // flAirAccelerate
	75.0f, // flStopSpeed
	3500.0f, // flMaxVelocity
	18.0f, // flStepSize
	30.0f, // flAirSpeedCap — CGameMovement::GetAirSpeedCap
	0.33333333f, // flDuckSpeedScale — HandleDuckingSpeedCrop
	320.0f, // flMaxSpeed
};

const SourceSharpMovementParams *SourceSharp_MovementParams()
{
	return &s_Movement;
}

// Called once per side during managed startup. Values are defaults: a server config read
// later still overrides them.
void SourceSharp_RegisterMovementParams( const SourceSharpMovementParams *pParams )
{
	if ( !pParams )
		return;

	s_Movement = *pParams;

	// Both sides need the hulls, or the client predicts a player standing elsewhere.
	SourceSharp_PublishViewVectors( &s_Movement );

#ifndef CLIENT_DLL
	// Nothing here: the server's tuning ConVars are managed's.
#else
	// Names the numbers the predicted loop reads, so a mismatch with the server shows.
	Msg( "[SourceSharp] movement(client): predicted params registered — jump %.0fu, "
		 "air cap %.0f, duck %.2fx\n",
		 s_Movement.flJumpHeight, s_Movement.flAirSpeedCap, s_Movement.flDuckSpeedScale );
#endif
}

// How many commands the managed movement loop ran, counted in the crossing so a build
// where it never ran cannot pass the movement gate. Unsigned and allowed to wrap: the
// gate reads that it moved.
enum SourceSharpMoveCounter_t
{
	kMoveProcessMovement = 0,

	kMoveCounterCount
};

#ifdef CLIENT_DLL
// The client's counts; the server's command count is CServerMovement's.
static unsigned int *MoveCounters()
{
	static unsigned int s_nCounts[kMoveCounterCount] = { 0 };
	return s_nCounts;
}

unsigned int SourceSharp_MoveManagedCalls()
{
	return MoveCounters()[kMoveProcessMovement];
}
#endif

// MoveStateBlob in SourceSharp.Abi mirrors this: 147 int32/float words, no padding.
COMPILE_TIME_ASSERT( sizeof( SourceSharpMoveState ) == 147 * 4 );

#ifdef CLIENT_DLL
// The client's whole-loop crossing, one per command. A missing managed side is fatal
// rather than a fallback: there is no stock movement in this build.
bool SourceSharp_MoveProcessMovement( SourceSharpMoveState *pState )
{
	IManagedPredictionClient *pCommands = ManagedPredictionClient();

	// Nothing to move this player with.
	if ( !pCommands || !pState )
		SourceSharp_MovementFatal( kMovementFatal_NoManagedSide, 0 );

	// false: the managed loop declined this move type, or caught an exception (the ABI
	// cannot tell them apart). Not fatal; the caller counts it.
	if ( !pCommands->MoveProcessMovement( pState ) )
		return false;

	MoveCounters()[kMoveProcessMovement]++;
	return true;
}
#endif // CLIENT_DLL

// The same count from the client's managed replay, once per command it ran.
#ifdef CLIENT_DLL
void SourceSharp_MoveNoteManagedCommand()
{
	MoveCounters()[kMoveProcessMovement]++;
}
#endif

// One report body for both sides' move-managed-report commands.
void SourceSharp_MoveManagedReport( unsigned int nCalls )
{
	const char *const pszNames[kMoveCounterCount] = {
		"ProcessMovement",
	};

	const unsigned int pCounts[kMoveCounterCount] = { nCalls };
	unsigned int nTotal = 0;

	for ( int i = 0; i < kMoveCounterCount; i++ )
		nTotal += pCounts[i];

	Msg( "[SourceSharp] movemath(%s): %u managed call(s) total\n",
#ifdef CLIENT_DLL
		 "client",
#else
		 "server",
#endif
		 nTotal );

	for ( int i = 0; i < kMoveCounterCount; i++ )
		Msg( "  %-16s %u\n", pszNames[i], pCounts[i] );

#ifdef CLIENT_DLL
	// Commands that could not be run, printed even when zero. The client's alone: a server
	// refusal is fatal, so nothing would be left to print it.
	static const char *const pszRefusals[kMovementFatal_Count] = {
		"no-managed-side",
		"no-stock-body",
		"declined-movetype",
	};

	for ( int i = 0; i < kMovementFatal_Count; i++ )
		Msg( "  refused:%-8s %u\n", pszRefusals[i], SourceSharp_MovementRefusalCount( i ) );
#endif
}

// [MD] I'll remove this eventually. For now, I want the ability to A/B the optimizations.
#ifdef CLIENT_DLL
bool g_bMovementOptimizations = true;
#endif

// Replicated, so both sides run the same movement. 0 selects a stock body this build
// does not have, which is fatal with a named reason. The server's half is declared in
// managed; an unregistered ref reads as 1, since 0 would be the fatal arm.
#ifdef CLIENT_DLL
ConVar sourcesharp_movement_managed(
	"sourcesharp_movement_managed", "1", FCVAR_REPLICATED | FCVAR_NOTIFY,
	"Run the ten ported CGameMovement methods in C# (1) or in the stock C++ they forward from (0)." );

static bool MovementManaged()
{
	return sourcesharp_movement_managed.GetBool();
}
#else
static CManagedConVarRef sourcesharp_movement_managed( "sourcesharp_movement_managed" );

static bool MovementManaged()
{
	return !sourcesharp_movement_managed.IsValid() || sourcesharp_movement_managed.GetBool();
}

// Asked before each managed command; the 0 arm is fatal.
void SourceSharp_MovementRequireManaged()
{
	if ( !MovementManaged() )
		SourceSharp_MovementFatal( kMovementFatal_NoStock, 0 );
}
#endif

// The client's IGameMovement, standalone rather than a CGameMovement subclass.
#ifdef CLIENT_DLL
class CShellGameMovement : public IGameMovement
{
public:
	CShellGameMovement();
	virtual ~CShellGameMovement();

	// Stock's signatures; nothing calls them, the interface is published by name.
	virtual void ProcessMovement( CBasePlayer *pPlayer, CMoveData *pMove ) OVERRIDE;
	virtual void StartTrackPredictionErrors( CBasePlayer *pPlayer ) OVERRIDE;
	virtual void FinishTrackPredictionErrors( CBasePlayer *pPlayer ) OVERRIDE;

	static IMovementPlayer *Player( CBasePlayer *pPlayer ) { return reinterpret_cast<IMovementPlayer *>( pPlayer ); }
#ifdef CLIENT_DLL
	virtual void DiffPrint( PRINTF_FORMAT_STRING char const *fmt, ... ) OVERRIDE;
	virtual Vector GetPlayerMins( bool ducked ) const OVERRIDE;
	virtual Vector GetPlayerMaxs( bool ducked ) const OVERRIDE;
	virtual Vector GetPlayerViewOffset( bool ducked ) const OVERRIDE;
#endif

	bool RunManagedCommand();
#ifdef CLIENT_DLL
	void FillConstants( struct SourceSharpMoveState *pState );
#endif
	void WorldTrace( const float *pStart, const float *pEnd, const float *pMins, const float *pMaxs, int nMask,
					 int nCollisionGroup, struct SourceSharpMoveTraceResult *pOut );
	int WorldEffect( int nKind, const float *pXYZ, float flValue, int nValue );

	// The client replay never calls ProcessMovement, so it sets the move data here.
	void SetMoveData( CMoveData *pMove ) { mv = pMove; }

	IHandleEntity *LastGround() const { return m_pLastGround; }

	IMovementPlayer *player;

private:
	void ResetDuckLatched();

	CMoveData *mv;

	int m_nOldWaterLevel;
	int m_nOnLadder;

	// The last standable ground a serviced trace found: the loop carries only whether
	// there is ground, not which entity. NULL until the first trace.
	IHandleEntity *m_pLastGround;

	// The last serviced trace, so the touch list can be replayed against it.
	trace_t m_LastTrace;
};

// GameMovement001: no engine binary asks for it, but it is what HL2MP and TF export.
static CShellGameMovement g_ShellGameMovement;
IGameMovement *g_pGameMovement = (IGameMovement *)&g_ShellGameMovement;

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CShellGameMovement, IGameMovement, INTERFACENAME_GAMEMOVEMENT, g_ShellGameMovement );
#endif // CLIENT_DLL

#ifdef CLIENT_DLL
// Commands that could not be run, counted by reason.
static unsigned int *MovementRefusals()
{
	static unsigned int s_nRefusals[kMovementFatal_Count] = { 0, 0, 0 };
	return s_nRefusals;
}
#endif

// Fatal through Error(), so the reason reaches the console and log before the process
// stops. The text is passed as "%s", never as the format.
void SourceSharp_MovementFatal( int nReason, int nDetail )
{
#ifdef CLIENT_DLL
	if ( nReason >= 0 && nReason < kMovementFatal_Count )
		MovementRefusals()[nReason]++;
#endif

	char szMessage[1536];
	SourceSharpMovement_Format( szMessage, sizeof( szMessage ), SOURCESHARP_MOVE_SIDE_TAG, nReason, nDetail );

	Error( "%s", szMessage );
}

// Loud but not fatal, for a declined move type: the ABI cannot tell it from a managed
// exception, which is already reported. Printed once per reason, counted always.
#ifdef CLIENT_DLL
static void SourceSharp_MovementRefused( int nReason, int nDetail )
{
	if ( nReason < 0 || nReason >= kMovementFatal_Count )
		return;

	unsigned int *pCounts = MovementRefusals();
	const bool bFirst = ( pCounts[nReason] == 0 );
	pCounts[nReason]++;

	if ( !bFirst )
		return;

	char szMessage[1536];
	SourceSharpMovement_Format( szMessage, sizeof( szMessage ), SOURCESHARP_MOVE_SIDE_TAG, nReason, nDetail );

	Warning( "%s", szMessage );
}
#endif // CLIENT_DLL

#ifdef CLIENT_DLL
unsigned int SourceSharp_MovementRefusalCount( int nReason )
{
	if ( nReason < 0 || nReason >= kMovementFatal_Count )
		return 0;

	return MovementRefusals()[nReason];
}
#endif

#ifdef CLIENT_DLL
// The mover whose command is running, set only for the duration of one crossing.
static CShellGameMovement **ActiveMover()
{
	static CShellGameMovement *s_pActive = NULL;
	return &s_pActive;
}
#endif // CLIENT_DLL

static void SS3E_FillVec( float *pOut, const Vector &v )
{
	pOut[0] = v.x;
	pOut[1] = v.y;
	pOut[2] = v.z;
}

#ifdef CLIENT_DLL
static void SS3E_FillVec( float *pOut, const QAngle &a )
{
	pOut[0] = a.x;
	pOut[1] = a.y;
	pOut[2] = a.z;
}

static QAngle SS3E_ReadAng( const float *p )
{
	return QAngle( p[0], p[1], p[2] );
}
#endif // CLIENT_DLL

static Vector SS3E_ReadVec( const float *p )
{
	return Vector( p[0], p[1], p[2] );
}

// The client's fill reads its own movement ConVars.
#ifdef CLIENT_DLL
#define SS_MOVEVAR( name ) name
#endif

#ifdef CLIENT_DLL
// The crossing's constant words for the client's managed replay. Read fresh each
// command: operators can change any of them mid-round.
void CShellGameMovement::FillConstants( SourceSharpMoveState *pState )
{
	const SourceSharpMovementParams *pParams = SourceSharp_MovementParams();

	pState->flCurrentGravity = GetCurrentGravity();

	pState->flCvFriction = SS_MOVEVAR( sv_friction ).GetFloat();
	pState->flCvAccelerate = SS_MOVEVAR( sv_accelerate ).GetFloat();
	pState->flCvAirAccelerate = SS_MOVEVAR( sv_airaccelerate ).GetFloat();
	pState->flCvStopSpeed = SS_MOVEVAR( sv_stopspeed ).GetFloat();
	pState->flCvMaxVelocity = SS_MOVEVAR( sv_maxvelocity ).GetFloat();
	pState->flCvMaxSpeed = SS_MOVEVAR( sv_maxspeed ).GetFloat();
	pState->flCvBounce = SS_MOVEVAR( sv_bounce ).GetFloat();
	pState->flCvRollAngle = SS_MOVEVAR( sv_rollangle ).GetFloat();
	pState->flCvRollSpeed = SS_MOVEVAR( sv_rollspeed ).GetFloat();
	pState->flCvNoClipSpeed = SS_MOVEVAR( sv_noclipspeed ).GetFloat();
	pState->flCvNoClipAccelerate = SS_MOVEVAR( sv_noclipaccelerate ).GetFloat();
	pState->flCvSpecSpeed = SS_MOVEVAR( sv_specspeed ).GetFloat();
	pState->flCvSpecAccelerate = SS_MOVEVAR( sv_specaccelerate ).GetFloat();
	pState->flCvAirSpeedCap = pParams->flAirSpeedCap;
	pState->flCvDuckSpeedScale = pParams->flDuckSpeedScale;
	pState->bCvOptimizations = g_bMovementOptimizations ? 1 : 0;
	pState->bCvSpecNoClip = SS_MOVEVAR( sv_specnoclip ).GetBool() ? 1 : 0;
	pState->nCvMaxClients = gpGlobals->maxClients;
	pState->flCvTickInterval = TICK_INTERVAL;
	pState->flCvJumpHeight = pParams->flJumpHeight;
	pState->flEngineTime = engine->Time();

	const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
	SS3E_FillVec( pState->vecCvView, vv->m_vView );
	SS3E_FillVec( pState->vecCvHullMin, vv->m_vHullMin );
	SS3E_FillVec( pState->vecCvHullMax, vv->m_vHullMax );
	SS3E_FillVec( pState->vecCvDuckHullMin, vv->m_vDuckHullMin );
	SS3E_FillVec( pState->vecCvDuckHullMax, vv->m_vDuckHullMax );
	SS3E_FillVec( pState->vecCvDuckView, vv->m_vDuckView );
	SS3E_FillVec( pState->vecCvObsHullMin, vv->m_vObsHullMin );
	SS3E_FillVec( pState->vecCvObsHullMax, vv->m_vObsHullMax );
	SS3E_FillVec( pState->vecCvDeadViewHeight, vv->m_vDeadViewHeight );
}
#endif // CLIENT_DLL

#ifdef CLIENT_DLL
bool CShellGameMovement::RunManagedCommand()
{
	SourceSharpMoveState state;
	memset( &state, 0, sizeof( state ) );

	state.nCommandNumber = player->MvCurrentCommandNumber();
	state.flFrameTime = gpGlobals->frametime;
	state.nMoveType = (int)player->MvGetMoveType();
	state.nFlags = player->MvGetFlags();
	state.nWaterLevel = player->MvGetWaterLevel();
	state.nWaterType = player->MvGetWaterType();
	state.bDeadFlag = player->MvPlayerState().deadflag ? 1 : 0;
	SS3E_FillVec( state.vecOrigin, mv->GetAbsOrigin() );
	SS3E_FillVec( state.vecVelocity, mv->m_vecVelocity );
	SS3E_FillVec( state.vecBaseVelocity, player->MvGetBaseVelocity() );
	SS3E_FillVec( state.angViewAngles, mv->m_vecViewAngles );
	state.flForwardMove = mv->m_flForwardMove;
	state.flSideMove = mv->m_flSideMove;
	state.flUpMove = mv->m_flUpMove;
	state.nButtons = mv->m_nButtons;
	state.nOldButtons = mv->m_nOldButtons;
	state.flMaxSpeed = mv->m_flMaxSpeed;
	state.flClientMaxSpeed = mv->m_flClientMaxSpeed;
	state.bDucked = player->MvLocal().m_bDucked ? 1 : 0;
	state.bDucking = player->MvLocal().m_bDucking ? 1 : 0;
	state.bInDuckJump = player->MvLocal().m_bInDuckJump ? 1 : 0;
	state.flDuckTime = player->MvLocal().m_flDucktime;
	state.flDuckJumpTime = player->MvLocal().m_flDuckJumpTime;
	state.flJumpTime = player->MvLocal().m_flJumpTime;
	state.flFallVelocity = player->MvLocal().m_flFallVelocity;
	state.flStepSize = player->MvLocal().m_flStepSize;
	SS3E_FillVec( state.vecViewOffset, player->MvGetViewOffset() );
	SS3E_FillVec( state.angPunchAngle, (const QAngle &)player->MvLocal().m_vecPunchAngle );
	SS3E_FillVec( state.angPunchAngleVel, (const QAngle &)player->MvLocal().m_vecPunchAngleVel );
	state.flEntityGravity = player->MvGetGravity();
	state.flSurfaceFriction = player->MvSurfaceFriction();

	IHandleEntity *pGround = player->MvGetGroundEntity();
	state.bHasGround = pGround ? 1 : 0;
	SS3E_FillVec( state.vecGroundVelocity, MovementGroundVelocity( pGround ) );

	state.flWaterJumpTime = player->MvWaterJumpTime();
	SS3E_FillVec( state.vecWaterJumpVel, player->MvWaterJumpVel() );
	state.flSwimSoundTime = player->MvSwimSoundTime();
	state.flStepSoundTime = player->MvStepSoundTime();
	state.nHealth = player->MvHealth();
	state.bGameCodeMovedPlayer = mv->m_bGameCodeMovedPlayer ? 1 : 0;
	state.flConstraintRadius = mv->m_flConstraintRadius;
	state.flConstraintWidth = mv->m_flConstraintWidth;
	state.flConstraintSpeedFactor = mv->m_flConstraintSpeedFactor;
	SS3E_FillVec( state.vecConstraintCenter, mv->m_vecConstraintCenter );
	state.flOldForwardMove = mv->m_flOldForwardMove;
	state.nImpulseCommand = mv->m_nImpulseCommand;
	state.flLaggedMovementValue = player->MvGetLaggedMovementValue();
	state.nSurfaceProps = player->MvSurfaceProps();
	SS3E_FillVec( state.vecLadderNormal, player->MvLadderNormal() );
	state.flOutStepHeight = mv->m_outStepHeight;
	SS3E_FillVec( state.vecOutWishVel, mv->m_outWishVel );
	SS3E_FillVec( state.vecOutJumpVel, mv->m_outJumpVel );
	state.nMoveCollide = (int)player->MvGetMoveCollide();
	state.nOnLadder = m_nOnLadder;
	state.nOldWaterLevel = m_nOldWaterLevel;
	state.bIsAlive = player->MvIsAlive() ? 1 : 0;
	state.nObserverMode = player->MvGetObserverMode();
	state.bInAVehicle = player->MvIsInAVehicle() ? 1 : 0;
	state.flModelScale = player->GetModelScale();
	state.nEntIndex = player->MvEntIndex();
	state.nStuckLast = player->MvStuckLast();
	SS3E_FillVec( state.angAngles, mv->m_vecAngles );
	SS3E_FillVec( state.angOldAngles, mv->m_vecOldAngles );
	state.bFirstRunOfFunctions = mv->m_bFirstRunOfFunctions ? 1 : 0;
	state.flCurTime = gpGlobals->curtime;
	state.bSurfaceDataValid = player->MvSurfaceData() ? 1 : 0;
	state.bAllowAutoMovement = player->MvLocal().m_bAllowAutoMovement ? 1 : 0;
	state.bSlowMovement = player->MvLocal().m_bSlowMovement ? 1 : 0;
	state.bIsServer = player->MvIsServer() ? 1 : 0;

	FillConstants( &state );

	CShellGameMovement **ppActive = ActiveMover();
	CShellGameMovement *pWas = *ppActive;
	*ppActive = this;
	const bool bRan = SourceSharp_MoveProcessMovement( &state );
	*ppActive = pWas;

	if ( !bRan )
		return false;

	// Only what the loop writes, as stock reads only CMoveData's outputs.
	player->MvSetMoveType( (MoveType_t)state.nMoveType );
	player->MvSetMoveCollide( (MoveCollide_t)state.nMoveCollide );

	// Through Add/Remove: the flags are networked and the change hooks matter.
	const int nAdd = state.nFlags & ~player->MvGetFlags();
	const int nRemove = player->MvGetFlags() & ~state.nFlags;
	if ( nAdd )
		player->MvAddFlag( nAdd );
	if ( nRemove )
		player->MvRemoveFlag( nRemove );

	player->MvSetWaterLevel( state.nWaterLevel );
	player->MvSetWaterType( state.nWaterType );
	mv->SetAbsOrigin( SS3E_ReadVec( state.vecOrigin ) );
	mv->m_vecVelocity = SS3E_ReadVec( state.vecVelocity );
	player->MvSetBaseVelocity( SS3E_ReadVec( state.vecBaseVelocity ) );
	mv->m_flForwardMove = state.flForwardMove;
	mv->m_flSideMove = state.flSideMove;
	mv->m_flUpMove = state.flUpMove;
	mv->m_nOldButtons = state.nOldButtons;
	mv->m_flMaxSpeed = state.flMaxSpeed;
	player->MvLocal().m_bDucked = state.bDucked != 0;
	player->MvLocal().m_bDucking = state.bDucking != 0;
	player->MvLocal().m_bInDuckJump = state.bInDuckJump != 0;
	player->MvLocal().m_flDucktime = state.flDuckTime;
	player->MvLocal().m_flDuckJumpTime = state.flDuckJumpTime;
	player->MvLocal().m_flJumpTime = state.flJumpTime;
	player->MvLocal().m_flFallVelocity = state.flFallVelocity;
	player->MvSetViewOffset( SS3E_ReadVec( state.vecViewOffset ) );
	player->MvLocal().m_vecPunchAngle = SS3E_ReadAng( state.angPunchAngle );
	player->MvLocal().m_vecPunchAngleVel = SS3E_ReadAng( state.angPunchAngleVel );
	player->MvSetGravity( state.flEntityGravity );
	player->MvSurfaceFriction() = state.flSurfaceFriction;

	// The loop carries whether there is ground; native knows which entity, from the last
	// serviced trace. See movement_ground_writeback.h.
	switch ( MovementGround_WriteBack( state.bHasGround != 0, player->MvGetGroundEntity() != NULL,
									   player->MvGetGroundEntity() == m_pLastGround ) )
	{
		case MOVEMENT_GROUND_CLEAR:
			player->MvSetGroundEntity( NULL );
			break;

		case MOVEMENT_GROUND_SET:
			player->MvSetGroundEntity( m_pLastGround );
			break;

		case MOVEMENT_GROUND_LEAVE:
			break;
	}

	player->MvWaterJumpTime() = state.flWaterJumpTime;
	player->MvWaterJumpVel() = SS3E_ReadVec( state.vecWaterJumpVel );
	player->MvSwimSoundTime() = state.flSwimSoundTime;
	player->MvStepSoundTime() = state.flStepSoundTime;
	player->MvSurfaceProps() = state.nSurfaceProps;
	player->MvLadderNormal() = SS3E_ReadVec( state.vecLadderNormal );
	player->MvStuckLast() = state.nStuckLast;

	if ( state.bSurfaceDataValid )
	{
		player->MvSurfaceData() = SS_MOVE_HELPER()->GetSurfaceProps()->GetSurfaceData( state.nSurfaceProps );
		if ( player->MvSurfaceData() )
			player->MvTextureType() = player->MvSurfaceData()->game.material;
	}

	mv->m_outStepHeight = state.flOutStepHeight;
	mv->m_outWishVel = SS3E_ReadVec( state.vecOutWishVel );
	mv->m_outJumpVel = SS3E_ReadVec( state.vecOutJumpVel );
	mv->m_vecAngles = SS3E_ReadAng( state.angAngles );
	mv->m_flOldForwardMove = state.flOldForwardMove;

	m_nOnLadder = state.nOnLadder;
	m_nOldWaterLevel = state.nOldWaterLevel;

	return true;
}

void CShellGameMovement::WorldTrace( const float *pStart, const float *pEnd, const float *pMins, const float *pMaxs,
									 int nMask, int nCollisionGroup, SourceSharpMoveTraceResult *pOut )
{
	trace_t pm;
	Ray_t ray;
	ray.Init( SS3E_ReadVec( pStart ), SS3E_ReadVec( pEnd ), SS3E_ReadVec( pMins ), SS3E_ReadVec( pMaxs ) );
	UTIL_TraceRay( ray, (unsigned int)nMask, SS_HANDLE_ENTITY( mv->m_nPlayerHandle ), nCollisionGroup, &pm );

	IHandleEntity *pGround = MovementGroundFromTrace( pm );

	// Kept so AddToTouched can be replayed with it: every AddToTouched in the loop is made
	// with the trace it just asked for.
	m_LastTrace = pm;

	// The entity every SetGroundEntity in the loop was fed; the loop cannot carry one.
	if ( pGround )
		m_pLastGround = pGround;

	pOut->flFraction = pm.fraction;
	SS3E_FillVec( pOut->vecEndPos, pm.endpos );
	SS3E_FillVec( pOut->vecPlaneNormal, pm.plane.normal );
	pOut->flPlaneDist = pm.plane.dist;
	pOut->bStartSolid = pm.startsolid ? 1 : 0;
	pOut->bAllSolid = pm.allsolid ? 1 : 0;
	pOut->nSurfaceProps = pm.surface.surfaceProps;
	pOut->nSurfaceFlags = pm.surface.flags;
	pOut->nContents = pm.contents;
	pOut->bHasGround = pGround ? 1 : 0;
	SS3E_FillVec( pOut->vecGroundVelocity, MovementGroundVelocity( pGround ) );
	pOut->bHasEntity = pm.m_pEnt ? 1 : 0;
	pOut->bHitWorld = SourceSharp_IsTheWorld( ss::TraceHit( pm ) ) ? 1 : 0;
}

int CShellGameMovement::WorldEffect( int nKind, const float *pXYZ, float flValue, int nValue )
{
	const Vector where = pXYZ ? SS3E_ReadVec( pXYZ ) : vec3_origin;

	switch ( nKind )
	{
		case SOURCESHARP_MOVE_EFFECT_RESET_TOUCH_LIST:
			SS_MOVE_HELPER()->ResetTouchList();
			break;

		case SOURCESHARP_MOVE_EFFECT_ADD_TO_TOUCHED:
			// The last serviced trace. Without this, nothing is touched: no trigger_push,
			// no trigger_teleport, no walk-over pickups.
			SS_MOVE_HELPER()->AddToTouched( m_LastTrace, mv->m_vecVelocity );
			break;

		case SOURCESHARP_MOVE_EFFECT_START_SOUND_SWIM:
			SS_MOVE_HELPER()->StartSound( where, "Player.Swim" );
			break;

		case SOURCESHARP_MOVE_EFFECT_SET_ANIMATION:
			SS_MOVE_HELPER()->PlayerSetAnimation( SS_PLAYER_ANIM( nValue ) );
			break;

		case SOURCESHARP_MOVE_EFFECT_FALLING_DAMAGE:
			// The damage travels beside PlayerFallingDamage, which takes no arguments.
			SourceSharpMovement_PendingFallDamage() = flValue;
			return SS_MOVE_HELPER()->PlayerFallingDamage() ? 1 : 0;

		// player is NULL for a client command (its state is managed), so these are guarded.
		case SOURCESHARP_MOVE_EFFECT_PLAY_STEP_SOUND:
			if ( player )
				player->MvPlayStepSound( const_cast<Vector &>( where ), player->MvSurfaceData(), flValue, true );
			break;

		case SOURCESHARP_MOVE_EFFECT_UPDATE_STEP_SOUND:
			if ( player )
				player->MvUpdateStepSound( player->MvSurfaceData(), where, mv->m_vecVelocity );
			break;

		case SOURCESHARP_MOVE_EFFECT_SPLASH:
			break;

		case SOURCESHARP_MOVE_EFFECT_RUMBLE:
			break;

		case SOURCESHARP_MOVE_EFFECT_RESET_DUCK_LATCHED:
			ResetDuckLatched();
			break;

		case SOURCESHARP_MOVE_EFFECT_SET_PLAYER_SURFACE:
			break;

		default:
			break;
	}

	return 1;
}
#endif // CLIENT_DLL

// The ABI slots, forwarding to the mover mid-crossing. Outside a crossing they refuse:
// a zero trace with fraction 1 would read as "nothing in the way".
void SourceSharp_MoveTrace( const float *pStart, const float *pEnd, const float *pMins, const float *pMaxs,
							int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult *pOut )
{
	if ( !pOut )
		return;

	memset( pOut, 0, sizeof( *pOut ) );

#ifdef CLIENT_DLL
	CShellGameMovement *pMover = *ActiveMover();
	if ( !pMover )
#endif
	// The server always refuses: its hull traces go through SourceSharp_MoveTraceServer.
	{
		// Blocked at the start: never "the way is clear".
		pOut->bStartSolid = 1;
		pOut->bAllSolid = 1;
		SS3E_FillVec( pOut->vecEndPos, SS3E_ReadVec( pStart ) );
		return;
	}

#ifdef CLIENT_DLL
	pMover->WorldTrace( pStart, pEnd, pMins, pMaxs, nMask, nCollisionGroup, pOut );
#endif
}

#ifndef CLIENT_DLL
// The server's movement hull trace through managed's filter, with no mover behind it.
// pOutHandles[0] is the entity hit, [1] the standable ground; invalid for none.
void CServerMovement::MoveTraceServer( const float *pStart, const float *pEnd, const float *pMins, const float *pMaxs,
									   int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult *pOut,
									   void *pFilter, uint32_t *pOutHandles )
{
	if ( !pOut || !pOutHandles )
		return;

	memset( pOut, 0, sizeof( *pOut ) );
	pOutHandles[0] = pOutHandles[1] = INVALID_EHANDLE_INDEX;

	if ( !pFilter )
	{
		pOut->bStartSolid = 1;
		pOut->bAllSolid = 1;
		SS3E_FillVec( pOut->vecEndPos, SS3E_ReadVec( pStart ) );
		return;
	}

	trace_t pm;
	Ray_t ray;
	ray.Init( SS3E_ReadVec( pStart ), SS3E_ReadVec( pEnd ), SS3E_ReadVec( pMins ), SS3E_ReadVec( pMaxs ) );
	ServerTrace()->TraceRay( ray, (unsigned int)nMask, static_cast<ITraceFilter *>( pFilter ), &pm );

	IHandleEntity *pGround = MovementGroundFromTrace( pm );

	pOut->flFraction = pm.fraction;
	SS3E_FillVec( pOut->vecEndPos, pm.endpos );
	SS3E_FillVec( pOut->vecPlaneNormal, pm.plane.normal );
	pOut->flPlaneDist = pm.plane.dist;
	pOut->bStartSolid = pm.startsolid ? 1 : 0;
	pOut->bAllSolid = pm.allsolid ? 1 : 0;
	pOut->nSurfaceProps = pm.surface.surfaceProps;
	pOut->nSurfaceFlags = pm.surface.flags;
	pOut->nContents = pm.contents;
	pOut->bHasGround = pGround ? 1 : 0;
	SS3E_FillVec( pOut->vecGroundVelocity, MovementGroundVelocity( pGround ) );
	pOut->bHasEntity = pm.m_pEnt ? 1 : 0;
	pOut->bHitWorld = SourceSharp_IsTheWorld( ss::TraceHit( pm ) ) ? 1 : 0;

	if ( pm.m_pEnt )
		pOutHandles[0] = (uint32_t)ss::TraceHit( pm )->GetRefEHandle().ToInt();
	if ( pGround )
		pOutHandles[1] = (uint32_t)pGround->GetRefEHandle().ToInt();
}
#endif

int32_t SourceSharp_MoveEffect( int32_t nKind, const float *pXYZ, float flValue, int32_t nValue )
{
#ifdef CLIENT_DLL
	CShellGameMovement *pMover = *ActiveMover();
	if ( !pMover )
		return 1;

	return pMover->WorldEffect( nKind, pXYZ, flValue, nValue );
#else
	// The server's movement answers its own effects.
	(void)nKind;
	(void)pXYZ;
	(void)flValue;
	(void)nValue;
	return 1;
#endif
}

#ifdef CLIENT_DLL
// The client's CMoveData singleton, borrowed for the servicing methods.
extern CMoveData *g_pMoveData;

extern void SourceSharp_MoveNoteManagedCommand();

// The client's replay bracket: fills the constant words of the crossing and makes this
// mover the active one, so the slots answer for this command. player stays NULL.
int32_t SourceSharp_MoveBeginClientCommand( uint32_t hPlayer, SourceSharpMoveState *pOut )
{
	if ( !pOut )
		return 0;

	CShellGameMovement *pMover = &g_ShellGameMovement;

	// m_nPlayerHandle is the trace filter's skip entity; without it every trace starts
	// solid inside the player's own hull.
	CMoveData *pMove = g_pMoveData;

	if ( !pMove )
		return 0;

	// The word came from ToInt() on this side's own valid handle, as UnsafeFromIndex needs.
	pMove->m_nPlayerHandle = CBaseHandle::UnsafeFromIndex( (int)hPlayer );

	pMover->SetMoveData( pMove );
	pMover->FillConstants( pOut );

	*ActiveMover() = pMover;
	return 1;
}

void SourceSharp_MoveEndClientCommand( int32_t bRan, int32_t *pOutGroundIndex )
{
	CShellGameMovement *pMover = *ActiveMover();

	// The ground the loop meant: the last trace's standable ground, or -1 to keep what
	// the player had.
	if ( pOutGroundIndex )
	{
		IHandleEntity *pGround = pMover ? pMover->LastGround() : NULL;

		*pOutGroundIndex = pGround ? pGround->GetRefEHandle().GetEntryIndex() : -1;
	}

	*ActiveMover() = NULL;

	// Counts runs, not asks.
	if ( bRan )
		SourceSharp_MoveNoteManagedCommand();
}
#endif // CLIENT_DLL

#ifdef CLIENT_DLL
CShellGameMovement::CShellGameMovement()
{
	player = NULL;
	mv = NULL;
	m_nOldWaterLevel = 0;
	m_nOnLadder = 0;
	m_pLastGround = NULL;

	// trace_t has a user-declared constructor, so the byte-wise zeroing is cast explicitly.
	memset( (void *)&m_LastTrace, 0, sizeof( m_LastTrace ) );
}

CShellGameMovement::~CShellGameMovement() {}

// One command. There is no stock fallback body.
void CShellGameMovement::ProcessMovement( CBasePlayer *pBasePlayer, CMoveData *pMove )
{
	IMovementPlayer *pPlayer = Player( pBasePlayer );
	Assert( pMove && pPlayer );

	if ( !pMove || !pPlayer )
		return;

	if ( !MovementManaged() )
	{
		// No stock mover exists; fatal. The return only marks the arm as ending, as
		// the declaration carries no NORETURN.
		SourceSharp_MovementFatal( kMovementFatal_NoStock, 0 );
		return;
	}

	const float flStoreFrametime = gpGlobals->frametime;

	//!!HACK HACK: Adrian - slow down all player movement by this factor.
	//!!Blame Yahn for this one.
	gpGlobals->frametime *= pPlayer->MvGetLaggedMovementValue();

	// StartTrackPredictionErrors should have set this
	Assert( player == pPlayer );
	player = pPlayer;

	mv = pMove;
	mv->m_flMaxSpeed = pPlayer->MvGetPlayerMaxSpeed();

	if ( !RunManagedCommand() )
	{
		// Unreachable: the loop declines no move type. Counted, not assumed.
		SourceSharp_MovementRefused( kMovementFatal_Declined, (int)pPlayer->MvGetMoveType() );
	}

	//This is probably not needed, but just in case.
	gpGlobals->frametime = flStoreFrametime;
}

// PREDICTION_ERROR_CHECK_LEVEL is 0, so stock's pair is this assignment and nothing.
// ProcessMovement asserts on it, and the servicing methods read `player`.
void CShellGameMovement::StartTrackPredictionErrors( CBasePlayer *pPlayer )
{
	player = Player( pPlayer );
}

void CShellGameMovement::FinishTrackPredictionErrors( CBasePlayer *pPlayer ) {}

// Empty, as stock's is at PREDICTION_ERROR_CHECK_LEVEL 0.
void CShellGameMovement::DiffPrint( char const *fmt, ... )
{
	va_list argptr;
	va_start( argptr, fmt );
	va_end( argptr );
}

// Stock's hulls; without a player they answer the unscaled vector rather than zero.
Vector CShellGameMovement::GetPlayerMins( bool ducked ) const
{
	if ( !player )
	{
		const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
		return ducked ? vv->m_vDuckHullMin : vv->m_vHullMin;
	}

	return ducked ? VEC_DUCK_HULL_MIN_SCALED( player ) : VEC_HULL_MIN_SCALED( player );
}

Vector CShellGameMovement::GetPlayerMaxs( bool ducked ) const
{
	if ( !player )
	{
		const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
		return ducked ? vv->m_vDuckHullMax : vv->m_vHullMax;
	}

	return ducked ? VEC_DUCK_HULL_MAX_SCALED( player ) : VEC_HULL_MAX_SCALED( player );
}

Vector CShellGameMovement::GetPlayerViewOffset( bool ducked ) const
{
	if ( !player )
	{
		const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
		return ducked ? vv->m_vDuckView : vv->m_vView;
	}

	return ducked ? VEC_DUCK_VIEW_SCALED( player ) : VEC_VIEW_SCALED( player );
}

// Resets interpolation when the player's duck state changes.
void CShellGameMovement::ResetDuckLatched()
{
	// NULL-guarded: no client IMovementPlayer exists.
	if ( player && !player->MvInFirstPersonView() )
		player->MvResetLatched();
}
#endif // CLIENT_DLL
