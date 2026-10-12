//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the standalone player, server half (Phase 9a) ======//
// SS_REASON a  a vtable the engine calls through
//
// See the header for what this class is. This file is three things:
//
//   1. the player's own state and the IMovementPlayer answers over it;
//   2. the command path — queueing what ProcessUsercmds decoded and running
//      it per tick through the SAME loop the SDK player runs through, one
//      stripped-down CPlayerMove::RunCommand equivalent per command;
//   3. the move helper the loop talks to while a standalone player is the one
//      moving. gamemovement.cpp reaches its helper through the MoveHelper()
//      SINGLETON, not through the argument, so the standalone helper installs
//      itself for exactly the duration of a command and restores the SDK's
//      after — zero lines of SDK diff, and the SDK player's commands are
//      untouched because the two never run inside one another.
//
// What the stripped-down RunCommand deliberately does NOT carry, each either
// owned by managed code or recorded in docs/divergences.md:
//   - weapon selection by ucmd->weaponselect (weapons are managed under this
//     flag; selection arrives through the HUD slots);
//   - vehicles (no standalone vehicle exists);
//   - the commentary system, IK ground contacts, bot/HLTV special cases;
//   - the sv_maxusrcmdprocessticks speedhack budget (recorded — the clamp
//     needs a per-player tick ledger this milestone does not carry).
//
//=============================================================================//

// THE R5 SET (plan_server_novalve.md P5): cbase.h, in_buttons.h,
// movevars_shared.h, usercmd.h, takedamageinfo.h and physics_shared.h went.
#include "ss_util.h"               // engine, ss::IsEntityQAngleReasonable
#include "ss_shareddefs.h"         // kMaxPlayers, kDmgFall
#include "ss_physics_shared.h"     // physenv, physcollision
#include "ss_entityhandle.h"       // ss::GameEntityFromEntityHandle
#include "ss_gamestringpool.h"     // AllocPooledString
extern CGlobalVars *gpGlobals;
// The player-slot helpers util.h declared; shell_playerslots.cpp and
// shell_commandclient.cpp define them.
const char *UTIL_PlayerNameBySlot( int nSlot );
int UTIL_PlayerUserIdBySlot( int nSlot );
int UTIL_PlayerSlotByName( const char *pName );
int UTIL_PlayerSlotByPartialName( const char *pName );
int UTIL_PlayerSlotByCommandArg( const char *pArg );
void UTIL_GetPlayerConnectionInfo( int playerIndex, int &ping, int &packetloss );
int UTIL_GetCommandClientIndex( void );
#include "ss_recipient_filter.h"
#include "igamerules.h"
#include "shell_standalone_player.h"
#include "movement_ground.h"
#include "standalone_player_sync.h"
#include "shell_server.h"
#include "shell_resolve.h"   // SourceSharp_HandleFromEntity: the alias-guarded crossing
#include "sourcesharp.h"
#include "ss_gamemovement.h"	// the server's CMoveData / IGameMovement are server.so's own
#include "ss_movehelper.h"		// CStandaloneMoveHelper's base
using ss::CMoveData;
using ss::IGameMovement;
// SourceSharpMovement_PendingFallDamage: the amount MoveMath.FallDamage chose
// for the landing this move helper is about to apply.
#include "shell_gamemovement.h"
#include "vphysics_interface.h"
#include "player_name_match.h"
#include "vphysics/player_controller.h"
#include "physics_gamedata.h"
#include "anyentity.h"
// The union resolve -- plan_entity.md §3.3, lane entlist-server. MvGetGroundEntity
// asked the entity list because the ground can be on either hierarchy; the three
// named arms that replaced it are declared here.
#include "slot_table.h"
#include "physicsowner.h"     // PhysicsOwnerOf
#include "standalone_relays.h" // StandaloneEntity_MoveType
#include "ss_takedamageinfo.h"	// server.so's damage info


extern IGameMovement *g_pGameMovement;
extern CMoveData *g_pMoveData;

// The lifted precache/tick/prediction/tracer statics. They used to be
// reached as CBaseEntity::Foo() through one-line forwarders in
// game/shared/baseentity_shared_forwarders.cpp; that quarantine is retired
// and the calls below name the free functions directly.
#include "entity_shared_statics.h"

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the calls below name GameSound_* directly.
#include "gamesound.h"
#include "native_command_body.h"
#include "ss_string.h"
#include "ss_entityfactory.h"   // ss::EntityFactoryDictionary, for the ss_player factory
#include "imanaged_areas.h"   // plan_engineapi_vtables E10, E15, E17: IManagedClients, IManagedCommands, IManagedPlayerCommands
#include "tickbase_rule.h"

// The player's physics-shadow states, with stock's values (player.h's
// VPHYS_WALK / VPHYS_CROUCH / VPHYS_NOCLIP). Spelled here because player.h
// is CBasePlayer's header, and server.so builds no CBasePlayer (P5): this
// file is the only one that used them.
namespace
{
	enum
	{
		VPHYS_WALK = 0,
		VPHYS_CROUCH,
		VPHYS_NOCLIP,
	};
}


//-----------------------------------------------------------------------------
// The declared state's placements, asked once per resolve generation and
// shared by every player — the layout is per class, not per instance.
//-----------------------------------------------------------------------------
static SourceSharpPlayerFields_t s_PlayerFields;

static bool PlayerFieldsCurrent()
{
	if ( s_PlayerFields.IsCurrent() )
		return true;

	return SourceSharpPlayerFields_Resolve( &s_PlayerFields );
}

//-----------------------------------------------------------------------------
// Native state -> blob, after anything that moved it: every command, every
// hit, every spawn. What makes the ONE declaration the wire format while the
// loop stays native (9c5).
//-----------------------------------------------------------------------------
void CStandalonePlayer::SyncToBlob()
{
	if ( !PlayerFieldsCurrent() )
		return;

	SourceSharpPlayerExtras_t extras;
	extras.vecOrigin   = GetAbsOrigin();
	extras.vecVelocity = m_vecAbsVelocity;
	extras.flMaxSpeed  = m_flMaxSpeed;
	extras.nTickBase   = m_nTickBase;
	extras.nHealth     = GetHealth();
	extras.bAlive      = MvIsAlive();

	// What they are standing on, as stock's networked ehandle word. The blob is
	// the prediction schema, so leaving this out left FL_ONGROUND in the blob
	// with nothing behind it — see PlayerNetState.GroundEntity.
	extras.nGroundEntity = SourceSharpPlayerSync_EncodeGround( m_hGround );

	SourceSharpPlayerSync_Write( DataBlob(), s_PlayerFields, this, extras );

	NetworkStateChanged();
}

int CStandalonePlayer::TakeDamage( const ss::CTakeDamageInfo &info )
{
	// The base applies the managed verdict to health; the blob hears about it
	// immediately rather than at the next command, because a HUD reading a
	// stale health for a tick is exactly the kind of quiet wrongness the sync
	// exists to prevent.
	const int nTaken = BaseClass::TakeDamage( info );

	if ( nTaken > 0 )
		SyncToBlob();

	return nTaken;
}

//-----------------------------------------------------------------------------
// The connected players, by entindex. A flat array because the arms in
// gameinterface.cpp ask once per packet and per snapshot.
//-----------------------------------------------------------------------------
static CStandalonePlayer *s_StandalonePlayers[ ss::kMaxPlayers + 1 ];

CStandalonePlayer *CStandalonePlayer::FromEdict( const edict_t *pEdict )
{
	if ( !pEdict )
		return NULL;

	return FromIndex( engine->IndexOfEdict( const_cast< edict_t * >( pEdict ) ) );
}

CStandalonePlayer *CStandalonePlayer::FromIndex( int nEntIndex )
{
	if ( nEntIndex < 1 || nEntIndex > ss::kMaxPlayers )
		return NULL;

	return s_StandalonePlayers[ nEntIndex ];
}

//-----------------------------------------------------------------------------
// THE MOVE HELPER SINGLETON'S STORAGE, moved here by the Phase 4 exit lane from
// game/server/movehelper_server.cpp, which then left server_csharp.vpc.
//
// imovehelper.h's own instruction is "add this to the CPP file that implements
// the IMoveHelper", and this is that file: CStandaloneMoveHelper below is the
// ONLY IMoveHelper this build ever constructs. movehelper_server.cpp's
// CMoveHelperServer is created by MoveHelperServer(), whose only callers are in
// game/server/player.cpp -- which this project does not compile -- so the whole
// 428-line file contributed exactly this one declaration to the link.
//
// Measured rather than argued: `tools/4a-owes movehelper_server` reported 2
// symbols owed and BOTH were IMoveHelper::sm_pSingleton, from gamemovement.o
// and this object. Nothing owed MoveHelperServer() or any CMoveHelperServer
// member, and the link runs with -Wl,--no-undefined, so a missed caller is a
// build failure and not a silent one. The vtable hole that argument leaves is
// closed by construction: a CMoveHelperServer can only be REACHED through
// MoveHelper(), which returns this pointer, which only CStandaloneMoveHelper
// ::Install ever writes.
//-----------------------------------------------------------------------------
ss::IMoveHelper *ss::IMoveHelper::sm_pSingleton = 0;

//-----------------------------------------------------------------------------
// The move helper. Lives here because only a standalone player ever installs
// it, and installs it for exactly one command at a time.
//-----------------------------------------------------------------------------
class CStandaloneMoveHelper : public ss::IMoveHelper
{
public:
	CStandaloneMoveHelper() : m_pHost( NULL ) {}

	// The swap. The loop reads the singleton, so for the duration of one
	// standalone command the singleton is this. Scoped by RunOneCommand.
	void Install( CStandalonePlayer *pHost )
	{
		m_pHost = pHost;
		m_pPrevious = ss::MoveHelper();
		SetSingleton( this );
	}

	void Uninstall()
	{
		SetSingleton( m_pPrevious );
		m_pPrevious = NULL;
		m_pHost = NULL;
		m_TouchList.RemoveAll();
	}

	virtual char const *GetName( CBaseHandle handle ) const OVERRIDE { return "standaloneplayer"; }

	virtual void ResetTouchList( void ) OVERRIDE { m_TouchList.RemoveAll(); }

	virtual bool AddToTouched( const CGameTrace &tr, const Vector &impactvelocity ) OVERRIDE
	{
		// The same dedupe stock keeps: one report per entity per command.
		for ( int i = 0; i < m_TouchList.Count(); i++ )
		{
			if ( m_TouchList[i].trace.m_pEnt == tr.m_pEnt )
				return false;
		}

		int i = m_TouchList.AddToTail();
		m_TouchList[i].trace = tr;
		m_TouchList[i].deltavelocity = impactvelocity;

		// CBasePlayer::Touch's job (player.cpp:4761): whether this was a
		// moveable physics object the move shoved is MANAGED's decision
		// (PlayerShadowBodies.TouchedMoveablePhysics, 2026-10-12); native
		// records what was touched, and whether it was the ground then.
		if ( m_pHost && tr.m_pEnt )
			m_pHost->RecordMoveTouch( tr.m_pEnt->GetRefEHandle() );
		return true;
	}

	// The trigger sweep, which is the half of ProcessImpacts gameplay stands
	// on: pickups are triggers, and so is every managed trigger volume. The
	// solid-impact notifications stock also sends (PhysicsImpact on what was
	// run into) are recorded in docs/divergences.md rather than half-done.
	virtual void ProcessImpacts( void ) OVERRIDE;

	virtual void Con_NPrintf( int idx, char const *fmt, ... ) OVERRIDE
	{
		va_list argptr;
		char szString[ 1024 ];

		va_start( argptr, fmt );
		ss::FormatV( szString, sizeof( szString ), fmt, argptr );
		va_end( argptr );

		engine->Con_NPrintf( idx, "%s", szString );
	}

	virtual void StartSound( const Vector &origin, int channel, char const *sample,
	                         float volume, soundlevel_t soundlevel, int fFlags, int pitch ) OVERRIDE
	{
		if ( !m_pHost )
			return;

		ss::CRecipientFilter filter;
		filter.AddRecipientsByPAS( origin );

		ss::EmitSound_t ep;
		ep.m_nChannel = channel;
		ep.m_pSoundName = sample;
		ep.m_flVolume = volume;
		ep.m_SoundLevel = soundlevel;
		ep.m_nFlags = fFlags;
		ep.m_nPitch = pitch;
		ep.m_pOrigin = &origin;

		GameSound_Emit( filter, m_pHost->MvEntIndex(), ep );
	}

	virtual void StartSound( const Vector &origin, const char *soundname ) OVERRIDE
	{
		if ( !m_pHost )
			return;

		ss::CRecipientFilter filter;
		filter.AddRecipientsByPAS( origin );
		GameSound_Emit( filter, m_pHost->MvEntIndex(), soundname );
	}

	virtual void PlaybackEventFull( int flags, int clientindex, unsigned short eventindex,
	                                float delay, Vector &origin, Vector &angles,
	                                float fparam1, float fparam2, int iparam1, int iparam2,
	                                int bparam1, int bparam2 ) OVERRIDE {}

	// Fall damage, computed the way CMoveHelperServer computes it and applied
	// through the shell's damage path, so the managed rules see it exactly as
	// they see every other hit.
	virtual bool PlayerFallingDamage( void ) OVERRIDE;

	virtual void PlayerSetAnimation( int playerAnim ) OVERRIDE {}

	virtual IPhysicsSurfaceProps *GetSurfaceProps( void ) OVERRIDE
	{
		extern IPhysicsSurfaceProps *physprops;
		return physprops;
	}

	virtual bool IsWorldEntity( const CBaseHandle &handle ) OVERRIDE
	{
		// Instance() is a STATIC of CBaseEntity and goes with the class, so the
		// armed arm cannot ask the entity list for the world and compare
		// pointers. It does not have to: the world is entry 0 of the handle
		// table and always has been, and THIS PORT HAS ALREADY WRITTEN THAT
		// ANSWER — the client twin of this same class,
		// sourcesharp/game/client/shell_standalone_player.cpp:283, is
		// `return handle.GetEntryIndex() == 0;` and nothing else.
		//
		// Not made unconditional, deliberately: Instance( 0 ) revalidates the
		// serial where the entry index alone does not, and this is not the shard
		// to change what the SDK build answers.
		return handle.GetEntryIndex() == 0;
	}

private:
	struct StandaloneTouch_t
	{
		trace_t trace;
		Vector  deltavelocity;
	};

	CStandalonePlayer *m_pHost;
	ss::IMoveHelper   *m_pPrevious;
	CUtlVector< StandaloneTouch_t > m_TouchList;
};

static CStandaloneMoveHelper s_StandaloneMoveHelper;

void CStandaloneMoveHelper::ProcessImpacts( void )
{
	if ( !m_pHost )
		return;

	// Which triggers is the player standing in NOW. The same ask the shell's
	// own movers make after every step — SolidMoved enumerates the trigger
	// list on behalf of an entity that asks, and nothing asks for free.
	m_pHost->TouchTriggers( m_pHost->GetAbsOrigin() );

	m_TouchList.RemoveAll();
}

//-----------------------------------------------------------------------------
// APPLY the landing. It does not decide it.
//
// WHAT MOVED, AND WHY. This function used to hold the whole rule: the
// threshold, the slope, and the arithmetic -- a third copy of stock's formula
// beside CHalfLife2::FlPlayerFallDamage's and GameMovement's, and the only one
// of the three fed the player's m_flFallVelocity MEMBER, which is the value
// from before the move that is landing them. So the managed loop armed the call
// on the velocity it had just computed and this body then scaled the damage by
// a different number, and on the commonest path -- the member still 0 because
// the writeback happens at FinishMove -- the arm above returned "no damage" for
// a fall the loop had already decided was lethal.
//
// The amount is MoveMath.FallDamage's now and arrives in
// SourceSharpMovement_PendingFallDamage. What is left here is the three things
// only the server can do: hand the damage to the shell's damage path, make the
// noise, and answer whether they got up.
//-----------------------------------------------------------------------------
bool CStandaloneMoveHelper::PlayerFallingDamage( void )
{
	if ( !m_pHost )
		return true;

	const float flFallDamage = SourceSharpMovement_PendingFallDamage();

	// STILL GUARDED, and not because the caller might get it wrong: the managed
	// loop arms this call only above the threshold, so a zero here means the
	// amount did not arrive. Applying zero damage would still make the noise and
	// still report a hit, which is the shape of a bug that reads as working.
	if ( flFallDamage > 0 )
	{
		// Through the shell's damage path: OnManagedDamage decides what lands,
		// exactly as it does for a bullet.
		ss::CTakeDamageInfo info( NULL, NULL, flFallDamage, ss::kDmgFall );
		info.SetDamagePosition( m_pHost->GetAbsOrigin() );
		m_pHost->TakeDamage( info );

		StartSound( m_pHost->GetAbsOrigin(), "Player.FallDamage" );
	}

	return m_pHost->GetHealth() > 0;
}

//-----------------------------------------------------------------------------
// Construction and identity.
//-----------------------------------------------------------------------------
CStandalonePlayer::CStandalonePlayer()
	: m_flWaterJumpTime( 0.0f ),
	  m_vecWaterJumpVel( 0, 0, 0 ),
	  m_flSwimSoundTime( 0.0f ),
	  m_flStepSoundTime( 0.0f ),
	  m_flSurfaceFriction( 1.0f ),
	  m_pSurfaceData( NULL ),
	  m_nSurfaceProps( 0 ),
	  m_chTextureType( 0 ),
	  m_chPreviousTextureType( 0 ),
	  m_vecLadderNormal( 0, 0, 0 ),
	  m_nStuckLast( 0 ),
	  m_nNumCrouches( 0 ),
	  m_vecAbsVelocity( 0, 0, 0 ),
	  m_vecBaseVelocity( 0, 0, 0 ),
	  m_vecViewOffset( 0, 0, 64 ),
	  m_vecPreviouslyPredictedOrigin( 0, 0, 0 ),
	  m_flGravity( 1.0f ),
	  m_flMaxSpeed( 320.0f ),
	  m_fFlags( 0 ),
	  m_MoveType( MOVETYPE_WALK ),
	  m_MoveCollide( MOVECOLLIDE_DEFAULT ),
	  m_nWaterLevel( 0 ),
	  m_nWaterType( 0 ),
	  m_bObserver( false ),
	  m_nObserverMode( 0 ),
	  m_pShadowStand( NULL ),
	  m_pShadowCrouch( NULL ),
	  m_pShadowActive( NULL ),
	  m_pPhysicsController( NULL ),
	  m_pShadowEnv( NULL ),
	  m_nVCollisionState( -1 ),
	  m_nMoveTouched( 0 ),
	  m_fMoveTouchedGround( 0 ),
	  m_nTickBase( 0 ),
	  m_nSimulationTick( -1 ),
	  m_pCurrentCommand( NULL )
{
	m_szPlayerName[0] = 0;

	// THE PLAYER STATE IS CONSTRUCTED IN THE BLOCK (step 3c), and it has to be
	// constructed rather than merely zeroed: CPlayerState declares a virtual
	// destructor ("This virtual method is necessary to generate a vtable in all
	// cases", PlayerState.h), so it carries a vtable pointer that only a
	// constructor writes. PvAllocEntPrivateData hands back zeroed memory, and a
	// zeroed vptr is a null dereference the first time anything calls through
	// it.
	//
	// Construct() and not a bare placement new: memdbgon.h redefines `new` in
	// every translation unit that includes it, and tier0/platform.h's helper is
	// the spelling the rest of this tree uses to get past that.
	//
	// The base's own constructor has already run, so m_pEngineBlock is set and
	// the block's head is written; the server tail is untouched by it apart
	// from the PVS info, so this is the first thing to claim these 64 bytes.
	Construct( PlayerStatePtr() );

	// NOTHING HAS ASKED WHERE THE EYES ARE. -1 rather than 0 because frame 0 is
	// a real frame -- see EngineBlock.cs on ViewOriginFrame.
	Block().Server.ViewOrigin.Init();
	Block().Server.ViewOriginFrame = -1;

	// The movement-local defaults stock sets at player construction: the step
	// height every ledge is measured against, and auto-movement on — both are
	// re-asserted from Movement.cs's registered params by the convar writes,
	// this is only the value before a command has run.
	m_Local.m_flStepSize = 18.0f;
	m_Local.m_bAllowAutoMovement = true;
}

CStandalonePlayer::~CStandalonePlayer()
{
	// The shadow goes with the player. A controller left in the solver drags a
	// body whose owner has been freed, and the solver keeps its own lists of
	// both.
	DestroyVCollision();

	// The other half of the constructor's Construct(). The storage belongs to
	// the blob and outlives nothing, but the object in it is a real object with
	// a real destructor and leaving it un-destructed would be the kind of thing
	// that is harmless until CPlayerState grows a member that is not.
	Destruct( PlayerStatePtr() );

	const int nIndex = EntIndex();

	if ( nIndex >= 1 && nIndex <= ss::kMaxPlayers && s_StandalonePlayers[ nIndex ] == this )
		s_StandalonePlayers[ nIndex ] = NULL;
}

//-----------------------------------------------------------------------------
// A shove to the view. CBasePlayer::ViewPunch's body, and the whole of it.
//
// The punch is a VELOCITY on the view angle rather than an offset: the movement
// loop's DecayPunchAngle springs m_vecPunchAngle back to zero over the ticks
// that follow, which is what makes a punch a kick rather than a permanent tilt.
// Both halves of the pair are already in the blob (standalone_player_sync.cpp),
// so the client sees it without anything new on the wire.
//
// SYNCED IMMEDIATELY, for the same reason TakeDamage is: a punch arrives from
// an entity's think or an input, not from a command, so without this it would
// sit on the server until the player's next usercmd -- a shove that lands a
// tick late reads as network jitter rather than as a bug, which is precisely
// the kind of quiet wrongness this file's sync exists to prevent.
//-----------------------------------------------------------------------------
void CStandalonePlayer::ViewPunch( const QAngle &angleOffset )
{
	m_Local.m_vecPunchAngleVel += angleOffset * 20;

	SyncToBlob();
}

void CStandalonePlayer::SetPlayerName( const char *pName )
{
	ss::StrCopy( m_szPlayerName, pName ? pName : "", sizeof( m_szPlayerName ) );
	PlayerState().netname = AllocPooledString( m_szPlayerName );
}

//-----------------------------------------------------------------------------
// Placed in the world with a player's shape. The managed OnSpawn the base
// fires from Spawn is where the model, health and game state come from.
//-----------------------------------------------------------------------------
void CStandalonePlayer::Spawn()
{
	m_fFlags |= FL_CLIENT;
	m_MoveType = MOVETYPE_WALK;
	PlayerState().deadflag = false;
	PlayerState().fixangle = FIXANGLE_NONE;

	// Where to enter the map — the same managed rule the SDK player asks
	// through GetPlayerSpawnSpot, asked at the same moment: BEFORE anything
	// is solid, so the player is never solid at the world origin and moved
	// afterwards ("the player is spawning in the wall", learnt once already).
	IManagedClients *pApi = ManagedClients();   // plan_engineapi_vtables E: managed-built

	// THE PLACEMENT HAPPENS INSIDE THIS CALL AND NOT AFTER IT -- step 2e.
	//
	// What stood here was `if ( CStandaloneEntity *pSpot = resolve( hSpot ) )`
	// followed by the transform, the zeroed velocity and the fixangle snap, read
	// off the SHELL of the point this call named. Every spawn-point class --
	// info_player_deathmatch, info_player_start -- is of kind Logical and as of
	// step 2e has no shell, so the resolve answered NULL, the whole block was
	// skipped, and the player spawned at the world origin. Measured on
	// dm_lockdown: ss_player at (-3893.09, 1413.9, 0.83) before, (0, 0, 0)
	// after.
	//
	// The placement is managed code's now and runs INSIDE SelectSpawnPoint --
	// EngineApi::PlayerPlaceAtSpawn, ServerModule.cs -- which is the same moment
	// this block ran and the constraint the ABI comment there records: before
	// the player is made solid and before the hull below is set. This call
	// deliberately IGNORES the handle it answers with, and managed code
	// deliberately answers EntityHandle.None, so that a spawn point which one
	// day does have a shell cannot be placed twice.
	if ( pApi )
		( void )pApi->OnSelectSpawnPoint( ManagedHandle() );

	SetCollisionBounds( g_pGameRules->GetViewVectors()->m_vHullMin, g_pGameRules->GetViewVectors()->m_vHullMax );
	m_vecViewOffset = g_pGameRules->GetViewVectors()->m_vView;

	SetSolidity( SOLID_BBOX, FSOLID_NOT_STANDABLE );

	BaseClass::Spawn();

	// The same two announcements the SDK player's spawn makes, in the same
	// order: OnPlayerSpawn is the shell's (health rule, handle checks), and
	// OnPlayerSpawned is the one the gamerules used to add (the directory's
	// record, which is what starts spawn protection counting).
	if ( pApi )
		pApi->OnPlayerSpawn( ManagedHandle() );
	if ( pApi )
		pApi->OnPlayerSpawned( ManagedHandle() );

	// The spawn state, on the wire before the first command runs.
	SyncToBlob();
}

//-----------------------------------------------------------------------------
// The ground, held as a handle because the thing stood on can die first.
//
// ANSWERED AS AN IHandleEntity, which is what the member has always held and
// what the movement loop now asks for. It used to be converted on the way out
// with IServerUnknown::GetBaseEntity(), which answers NULL for everything on
// the parallel hierarchy — and in this build every brush mover is on that
// hierarchy, so a player standing on a lift had no ground AT ALL. Not a ground
// moving at zero: none. FL_ONGROUND was never set, the whole `if ( newGround )`
// tail of CGameMovement::SetGroundEntity was skipped, and the base velocity a
// moving floor hands its rider was computed against a floor the loop could not
// see. The comment that used to stand here said the rider machinery carried a
// player instead; Riders.Carry does move them, but it is not the ground, and
// everything else the ground decides was being decided for somebody in midair.
//
// Nothing is converted on the way IN either. m_hGround is a bare CBaseHandle
// and every entity of both hierarchies is an IHandleEntity, so this member is
// the one home for "what am I standing on" and it never had a type that could
// refuse half the world.
//-----------------------------------------------------------------------------
IHandleEntity *CStandalonePlayer::MvGetGroundEntity()
{
	// THE UNION RESOLVE, NOT THE ENTITY LIST -- plan_entity.md §3.3, lane
	// entlist-server. This read the entity list's LookupEntity( m_hGround ), and
	// the reason it did is the paragraph above: the ground can be on either
	// hierarchy and the list was the only thing that answered for both. It is
	// not any more -- SourceSharp_EntityFromHandle asks the slot table, then the
	// world, then what is left of the SDK's list -- and the union is now three
	// named populations instead of one list nobody could put down.
	//
	// THE WORLD ARM MATTERS HERE MORE THAN ANYWHERE. A player standing on the
	// map itself is standing on worldspawn, which is the one entity that is in
	// neither the slot table nor the SDK's list, and world_adapter.cpp records
	// what a world that could not be resolved cost the last time: the local
	// player's ground read NULL on every replayed command while FL_ONGROUND said
	// otherwise, the client took AirMove where the server took WalkMove, and
	// every jump the server made was refused.
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
	// The verdict was managed code's; this mirrors it into the block the
	// engine reads. What the death LOOKS like — observer mode, the respawn
	// wait — stays where it has always been, in Players.cs.
	PlayerState().deadflag = true;

	// The announcement the SDK player's Event_Killed makes: the directory
	// counts the death and starts the cycle. BEFORE the scripted relay, so a
	// managed OnKilled that asks the directory sees this death recorded.
	// No attacker is the INVALID handle, never 0: 0 is entity 0, the world,
	// and managed scoring credited it with the kill (A40).
	const uint32_t hAttacker = SourceSharp_HandleFromEntity( pAttacker );

	IManagedClients *pApi = ManagedClients();   // plan_engineapi_vtables E: managed-built
	if ( pApi )
		pApi->OnPlayerKilled( ManagedHandle(), hAttacker );

	BaseClass::OnManagedKilled( pAttacker );
}

//-----------------------------------------------------------------------------
// THE COMMAND PATH -- plan_entity.md §3.10, step 3d.
//
// WHAT IS LEFT HERE IS MECHANISM, AND ALL OF IT IS ENGINE-FACING.
//
// The queue used to decide things: how far dropped packets were covered, how
// far back the backup commands reached, what order the queue ran in. Those are
// decisions and they are in C# now (PlayerCommandQueue.Plan). This side keeps
// the CUserCmds -- their bytes are an engine ABI (plan_entity.md §2) and
// usercmd.cpp, the bit-level delta decoder that fills them, legitimately stays
// C++ (§6) -- and materialises the queue the managed plan describes.
//
// AND IT IS ONE CROSSING PER ProcessUsercmds, WHATEVER N IS. What this
// replaced reached managed code THREE TIMES PER COMMAND:
// OnStandalonePlayerCommand at phase 0 and at phase 1, then OnPlayerRunCommand.
// A packet carrying eight commands cost twenty-four transitions. It now costs
// one here and one in RunQueuedCommands, and neither grows with N -- the loop
// is managed, so those three calls are ordinary method calls inside it.
//
// A COUNT OF CROSSINGS CANNOT SAY THAT ON ITS OWN, because a build that stopped
// running commands altogether would also cost one crossing per packet. The
// commands actually run are counted in the managed loop and printed by
// cs_sv_playercmds, which is the readout that lets the claim fail.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// A managed handle back to the player it names.
//
// THE HANDLE IS CHECKED, not just decoded. A CBaseHandle carries an entry index
// AND a serial, and only the pair identifies an object: decoding the index
// alone would answer whichever player later occupied that slot. So the
// candidate's own handle has to match the one asked for.
//
// 0 IS REFUSED because CStandaloneEntity::ManagedHandle answers 0 for an
// invalid handle, and INVALID_EHANDLE_INDEX because that is the sentinel the
// rest of this ABI uses. Neither can name a player: entry 0 is worldspawn.
//-----------------------------------------------------------------------------
static CStandalonePlayer *StandalonePlayer_FromManagedHandle( uint32_t hPlayer )
{
	if ( hPlayer == 0 || hPlayer == (uint32_t)INVALID_EHANDLE_INDEX )
		return NULL;

	const CBaseHandle handle = CBaseHandle::UnsafeFromIndex( (int)hPlayer );
	CStandalonePlayer *pPlayer = CStandalonePlayer::FromIndex( handle.GetEntryIndex() );

	return ( pPlayer && pPlayer->ManagedHandle() == hPlayer ) ? pPlayer : NULL;
}


//-----------------------------------------------------------------------------
// plan_server_novalve.md P2.2. C# decoded this packet, clamped it, planned it
// and queued its own copy; these are those same commands in the same order,
// so an index into this queue names the same command in C#'s. Only the
// conversion to the struct the native run loop reads is left here.
//-----------------------------------------------------------------------------
void CStandalonePlayer::QueueDecodedUsercmds( const SourceSharpDecodedUserCmd *pCmds, int nCount )
{
	if ( !pCmds || nCount <= 0 )
		return;

	for ( int i = 0; i < nCount; i++ )
	{
		const SourceSharpUserCmd &f = pCmds[ i ].cmd;

		ss::CUserCmd cmd;
		cmd.Reset();
		cmd.command_number = f.command_number;
		cmd.tick_count     = f.tick_count;
		cmd.viewangles.Init( f.viewangles[ 0 ], f.viewangles[ 1 ], f.viewangles[ 2 ] );
		cmd.forwardmove    = f.forwardmove;
		cmd.sidemove       = f.sidemove;
		cmd.upmove         = f.upmove;
		cmd.buttons        = f.buttons;
		cmd.impulse        = (byte)f.impulse;
		cmd.weaponselect   = f.weaponselect;
		cmd.weaponsubtype  = pCmds[ i ].weaponsubtype;
		cmd.random_seed    = pCmds[ i ].random_seed;
		cmd.mousedx        = (short)f.mousedx;
		cmd.mousedy        = (short)f.mousedy;

		m_PendingCmds.AddToTail( cmd );
	}
}

void CStandalonePlayer::RunAllQueuedCommands()
{
	for ( int i = 1; i <= ss::kMaxPlayers; i++ )
	{
		if ( s_StandalonePlayers[ i ] )
			s_StandalonePlayers[ i ]->RunQueuedCommands();
	}
}

void CStandalonePlayer::RunQueuedCommands()
{
	if ( m_nSimulationTick == gpGlobals->tickcount )
		return;

	m_nSimulationTick = gpGlobals->tickcount;

	// The clock the commands run on. Stock corrects it toward the server's
	// within sv_clockcorrection_msecs (60 by default); this applies the same
	// bound with the same number, quoted rather than tuned.
	if ( m_PendingCmds.Count() == 0 )
		return;

	// Stock's AdjustPlayerTimeBase, only on a frame that runs commands (see
	// tickbase_rule.h). A base of 0 has never been placed: put it as single
	// player does.
	m_nTickBase = TickBase_Adjust( m_nTickBase, gpGlobals->tickcount, m_PendingCmds.Count(),
	                               gpGlobals->simTicksThisFrame, (int)( 0.5f + 0.060f / gpGlobals->interval_per_tick ),
	                               m_nTickBase == 0 || gpGlobals->maxClients == 1 );

	const float flSaveCurtime   = gpGlobals->curtime;
	const float flSaveFrametime = gpGlobals->frametime;

	// ONE CROSSING FOR THE WHOLE QUEUE. The loop is managed: it asks for each
	// command's move through EngineApi::PlayerRunMove and makes its own
	// per-command decisions between the two stages.
	// E17: the server's managed-built area.
	IManagedPlayerCommands *pCommands = ManagedPlayerCommands();

	if ( pCommands )
	{
		pCommands->PlayerRunCommands( ManagedHandle(), m_PendingCmds.Count() );
	}
	else
	{
		Warning( "[SourceSharp] no managed run loop; %d queued command(s) dropped\n",
		         m_PendingCmds.Count() );
	}

	// NO TEARDOWN HERE, AND THAT IS CHECKED RATHER THAN ASSUMED. Each command
	// tears its own seed and current command down in stage 2, where the single
	// native function did -- after the managed decisions, because SharedRandom*
	// reads the seed and a weapon frame that ran seedless would mispredict.
	//
	// A repeat of it here would be belt and braces for a refusal that stopped
	// the loop early, and it is not needed: RunCommandStage's index check comes
	// BEFORE it touches the seed, so the stage that refuses is the stage that
	// set nothing, and the last command that ran reached its own stage 2.
	// PlayerCommandTests pins that ordering, because it is the only thing
	// keeping this comment true.
	m_PendingCmds.RemoveAll();

	gpGlobals->curtime   = flSaveCurtime;
	gpGlobals->frametime = flSaveFrametime;
}

//-----------------------------------------------------------------------------
// ONE COMMAND. CPlayerMove::RunCommand, holding only what a standalone player
// has -- see the header comment for what is deliberately absent.
//
// THREE STAGES, AND THE SPLIT IS EXACTLY WHERE THE MANAGED CALLS WERE. The
// single native function this replaces did, in this order:
//
//   prepare  -- current command, seed, curtime, the two sanity rules, the base
//               velocity fold, the view angles, SetupMove
//   PHASE 0     (was a crossing; is a managed call now)
//   move     -- the move helper, step 3e's movement bracket, FinishMove, the
//               pitch fold, ProcessImpacts
//   PHASE 1     (was a crossing)
//   BUTTONS     (was a crossing)
//   finish   -- the seed and current-command teardown, the tickbase step, and
//               SyncToBlob, the publish
//
// TWO STAGES WOULD HAVE BEEN ENOUGH FOR THE SERVER TODAY -- its phase 0 returns
// immediately -- and that is exactly why it is three. A phase 0 called before
// the prepare would run with no current command and with the PREVIOUS command's
// SharedRandom seed, which is a divergence that costs nothing until the first
// person puts work in phase 0 and then costs a mispredicted round.
//
// The split at the far end is load-bearing today: the managed decisions CHANGE
// STATE THE PUBLISH CARRIES -- a weapon frame moves m_flMaxSpeed, an impulse
// cheat changes health -- so publishing before them would put every such change
// one command late in the blob.
//-----------------------------------------------------------------------------
bool CStandalonePlayer::RunCommandStage( int nIndex, int nStage )
{
	if ( nIndex < 0 || nIndex >= m_PendingCmds.Count() )
		return false;

	if ( nStage < 0 || nStage > 2 )
		return false;

	ss::CUserCmd *ucmd = &m_PendingCmds[ nIndex ];

	const float flTickInterval = gpGlobals->interval_per_tick;

	if ( nStage == 1 )
		return RunCommandMove( ucmd );

	if ( nStage == 2 )
	{
		// The teardown that used to sit at the end of the single function, in
		// the same place relative to the managed calls: AFTER them. The seed is
		// what SharedRandom* reads, so clearing it before phase 1 would have
		// left the weapon frame seedless.
		EntityPrediction_SetSeedFromCommand( NULL );
		m_pCurrentCommand = NULL;

		if ( gpGlobals->frametime > 0 )
			m_nTickBase++;

		// The blob is the player at this command now -- what the wire sends and
		// what the client's ring will compare its replay of this command
		// against.
		SyncToBlob();

		// THE SHADOW IS NOT DRIVEN FROM HERE ANY MORE, and its absence is the
		// policy-player lane. It used to be the last line of this stage --
		// UpdateVCollision( nIndex ) -- carrying the whole state machine with
		// it. The managed run loop calls PlayerShadow.Step immediately after
		// this stage returns, which is the same point in the sequence and is
		// where stock runs UpdateVPhysicsPosition (player.cpp:3395); it is there
		// rather than here because that loop is what knows WHICH command just
		// ran, and the shadow's arrival ramp is measured in stock's loop
		// counter. See ShadowProbe / ShadowApply below.
		return true;
	}

	// STOCK'S FIRST LINE OF PlayerRunCommand (player.cpp:3801) clears
	// m_touchedPhysObject; here the touch list it is decided from is
	// per-command: filled by the move that is about to run and read by the
	// shadow step at the end of it.
	m_nMoveTouched = 0;
	m_fMoveTouchedGround = 0;

	m_pCurrentCommand = ucmd;
	EntityPrediction_SetSeedFromCommand( ucmd );

	gpGlobals->curtime   = m_nTickBase * flTickInterval;
	gpGlobals->frametime = flTickInterval;

	// The same two sanity rules stock applies before anything reads them.
	if ( !ucmd->viewangles.IsValid() || !ss::IsEntityQAngleReasonable( ucmd->viewangles ) )
		ucmd->viewangles = vec3_angle;

	// Base velocity becomes real velocity, as stock's CheckMovingGround: the
	// conveyor half is absent because a standalone conveyor carries its riders
	// itself (Phase 3c) and never writes a base velocity at all.
	if ( !( m_fFlags & FL_BASEVELOCITY ) )
	{
		m_vecAbsVelocity += ( 1.0f + ( flTickInterval * 0.5f ) ) * m_vecBaseVelocity;
		m_vecBaseVelocity.Init();
	}
	m_fFlags &= ~FL_BASEVELOCITY;

	// The old angles are last command's authority, recorded before this
	// command's overwrite them — stock's order, player_command.cpp:402.
	g_pMoveData->m_vecOldAngles = PlayerState().v_angle;

	if ( PlayerState().fixangle == FIXANGLE_NONE )
	{
		PlayerState().v_angle = ucmd->viewangles;
	}
	else if ( PlayerState().fixangle == FIXANGLE_RELATIVE )
	{
		PlayerState().v_angle = ucmd->viewangles + PlayerState().anglechange;
		PlayerState().fixangle = FIXANGLE_NONE;
	}

	// SetupMove, over this player's own fields.
	CMoveData *move = g_pMoveData;

	move->m_bFirstRunOfFunctions = true;
	move->m_bGameCodeMovedPlayer = ( m_vecPreviouslyPredictedOrigin != GetAbsOrigin() );
	move->m_nImpulseCommand      = ucmd->impulse;
	move->m_vecViewAngles        = ucmd->viewangles;
	move->m_vecAbsViewAngles     = move->m_vecViewAngles;
	move->m_nButtons             = ucmd->buttons;
	move->m_flForwardMove        = ucmd->forwardmove;
	move->m_flSideMove           = ucmd->sidemove;
	move->m_flUpMove             = ucmd->upmove;
	move->m_flClientMaxSpeed     = m_flMaxSpeed;
	move->m_nOldButtons          = m_Local.m_nOldButtons;
	move->m_flOldForwardMove     = m_Local.m_flOldForwardMove;
	move->m_vecAngles            = PlayerState().v_angle;
	move->m_vecVelocity          = m_vecAbsVelocity;
	move->m_nPlayerHandle        = GetRefEHandle();
	move->SetAbsOrigin( GetAbsOrigin() );
	move->m_vecConstraintCenter  = vec3_origin;
	move->m_flConstraintRadius   = 0.0f;
	move->m_flConstraintWidth    = 0.0f;
	move->m_flConstraintSpeedFactor = 0.0f;

	return true;
}

//-----------------------------------------------------------------------------
// Stage 1: the move itself, between the two managed phases.
//-----------------------------------------------------------------------------
bool CStandalonePlayer::RunCommandMove( ss::CUserCmd *ucmd )
{
	CMoveData *move = g_pMoveData;

	s_StandaloneMoveHelper.Install( this );
	s_StandaloneMoveHelper.ResetTouchList();

	// The bracket stock's two movement drivers put around every call, and
	// which left the build with them: player_command.cpp (evicted at the
	// -ss_player flip) on the server, prediction.cpp (evicted at 9c6) on the
	// client. CGameMovement::player is set HERE and asserted against inside
	// ProcessMovement -- "StartTrackPredictionErrors should have set this" --
	// so without the bracket the first command of every level fires
	// gamemovement.cpp(1168) and the prediction-error checker, which reads
	// the same member, measures a player nobody is moving.
	g_pGameMovement->StartTrackPredictionErrors( this );
	g_pGameMovement->ProcessMovement( this, move );
	g_pGameMovement->FinishTrackPredictionErrors( this );

	// FinishMove.
	SetAbsOrigin( move->GetAbsOrigin() );
	m_vecAbsVelocity = move->m_vecVelocity;
	m_vecPreviouslyPredictedOrigin = move->GetAbsOrigin();
	m_Local.m_nOldButtons = move->m_nButtons;

	// The pitch fold and clamp, then the body's angles: what everyone else
	// sees of where this player faces.
	float pitch = move->m_vecAngles[ PITCH ];
	if ( pitch > 180.0f )
		pitch -= 360.0f;
	pitch = clamp( pitch, -90.0f, 90.0f );
	move->m_vecAngles[ PITCH ] = pitch;

	SetAbsAngles( QAngle( 0, move->m_vecAngles[ YAW ], 0 ) );

	s_StandaloneMoveHelper.ProcessImpacts();
	s_StandaloneMoveHelper.Uninstall();

	return true;
}

//-----------------------------------------------------------------------------
// EngineApi::PlayerRunMove -- the managed run loop asking for one command.
//
// A free function because the ABI table holds free functions; it resolves the
// handle the same way every other player-addressed slot in this file does.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PlayerRunMove( uint32_t hPlayer, int32_t nIndex, int32_t nStage )
{
	CStandalonePlayer *pPlayer = StandalonePlayer_FromManagedHandle( hPlayer );

	if ( !pPlayer )
		return 0;

	return pPlayer->RunCommandStage( nIndex, nStage ) ? 1 : 0;
}

//=============================================================================
// THE PLAYER'S PHYSICS SHADOW (plan_entity.md step 3f, §3.8)
//
// WHAT THE LIVE GATE'S `push` SKIP ACTUALLY WAS, measured before any of this
// was written (tools/3f-probe-push, at 9f370367):
//
//     player walked  27.259 units
//     prop moved      0.000 units
//     final gap      44.338 units, of which 36.76 horizontal
//
// The player accelerates for four ticks, meets the crate, and stops dead --
// so the crate IS solid to them and the movement loop IS tracing against it.
// What never happens is the other direction. The ledger's recorded reason was
// therefore RIGHT, which is worth saying because step 2g's `lift` row's was
// wrong and this one was checked rather than inherited.
//
// STOCK'S MECHANISM, and this is a port of it and not an invention:
//
//     CBasePlayer::InitVCollision      player.cpp:8569   two bbox hulls
//     CBasePlayer::SetupVPhysicsShadow player.cpp:8269   + a player controller
//     CBasePlayer::PostThinkVPhysics   player.cpp:4798   the per-command update
//     CBasePlayer::UpdateVPhysicsPosition player.cpp:4873
//     CBasePlayer::SetVCollisionState  player.cpp:8620   which hull is in play
//
// A player does not MOVE through VPhysics. The movement loop is managed as of
// step 3e and traces the world for itself; the shadow is a SECOND body, held at
// the player's position by an IPhysicsPlayerController, and its whole job is to
// be inside the solver so the solver can shove what it meets. The controller's
// push limits are what turn "a body is there" into "a crate slides".
//
// WHAT IS NOT CARRIED, and each is a stated divergence rather than a silent gap
// (docs/divergences.md, "the player's physics shadow"):
//
//   * VPhysicsShadowUpdate -- the REVERSE direction, where the solver pushes
//     the PLAYER back and crushes them against geometry (player.cpp:8347). It
//     needs g_pShadowEntities, which physics.cpp's own comment records as empty
//     on this hierarchy and dispatched through a #ifndef SS_CBASEENTITY_ALIAS
//     arm. Without it a player is not pushed by props and is not crushed by
//     them; they can still walk into them and shove them, which is what the
//     gate asks and what a deathmatch map needs.
//   * m_touchedPhysObject, which in stock LOWERS the wish velocity handed to
//     the controller so a heavy prop can win the argument. It is set by the
//     reverse path above, so with that path absent the "not touching" arm --
//     stock's own maxSpeed-in-every-axis, player.cpp:4834 -- is the only
//     reachable one, and it is what is written here.
//   * the rideable-ground argument to Update(). Stock passes the physics object
//     under the player's feet so the controller solves in its frame; it applies
//     to standing on a heavy moving PROP, which no map in this build does and no
//     gate step exercises. NULL, and recorded.
//=============================================================================

//-----------------------------------------------------------------------------
// THE DECISIONS ABOVE ARE MANAGED'S (plan_physics_bones_native_zero 2.5).
// PlayerShadowBodies builds the two hulls and the controller through
// vphysics' own tables with stock's parameters, keeps the hull state, applies
// each command's plan (PlayerShadow.Plan) and carries a teleport; it reads the
// switch, counts what it does, and refuses a non-finite value before vphysics
// sees one (PhysicsNanGuard). What is left here is the player's own state, the
// three pointers held, and the teardown below, which the destructor owns.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// HOW MANY SHADOWS ARE IN THE SOLVER RIGHT NOW, and how many of them were built
// by an environment that has since been destroyed.
//
// Not a statistic: the first is a DETECTOR. A shadow is three pointers into the
// physics environment that built it, and physics.cpp destroys and recreates that
// environment at every level change. If this count is not zero at the moment the
// NEW environment is created, some player kept a set of pointers into the old
// one -- and whether that is harmless or catastrophic then rests on nothing but
// whether the allocator happened to hand the new environment a different
// address, because that pointer compare is the only guard there was.
//
// physics.cpp reads both, at level init and in ss_physworld_report.
//-----------------------------------------------------------------------------
int g_nSourceSharpPlayerShadowsLive     = 0;
int g_nSourceSharpPlayerShadowsOutlived = 0;

// The two counters native keeps, for the dev API's /counters (IShimDiagnostics)
// and managed's cs_sv_playershadow, in this order: live, outlived. The rest --
// built, updates, contacts, touches, snaps -- are PlayerShadowBodies' since
// plan_physics_bones_native_zero 2.5. This only copies.
int32_t SourceSharp_PlayerShadowCounts( int32_t *pOut, int32_t nMax )
{
	if ( !pOut || nMax <= 0 )
		return 0;

	const int32_t values[] = { g_nSourceSharpPlayerShadowsLive, g_nSourceSharpPlayerShadowsOutlived };
	const int32_t nAll = int32_t( sizeof( values ) / sizeof( values[0] ) );
	const int32_t n = nMax < nAll ? nMax : nAll;
	for ( int32_t i = 0; i < n; ++i )
		pOut[ i ] = values[ i ];
	return n;
}

//-----------------------------------------------------------------------------
// THE MANAGED SHADOW'S NATIVE HALF (plan_physics_bones_native_zero 2.5).
//
// PlayerShadowBodies decides everything and makes every vphysics call through
// vphysics' own tables. What is left here is the player's own state, read and
// written, and the three pointers held -- because the destructor below the
// constructor is the body's one teardown owner (DestroyVCollision), and a
// managed second owner for that teardown was tried and reverted (SsPlayer's
// OnRemoved has the run it cost).
//-----------------------------------------------------------------------------
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

	pOut->bHasShadow   = m_pPhysicsController && m_pShadowStand && m_pShadowCrouch ? 1 : 0;
	pOut->bBodyCurrent = m_pShadowEnv && m_pShadowEnv == physenv ? 1 : 0;
	pOut->nState       = m_nVCollisionState;
	pOut->pStand       = m_pShadowStand;
	pOut->pCrouch      = m_pShadowCrouch;
	pOut->pController  = m_pPhysicsController;

	const Vector &vecOrigin = GetAbsOrigin();
	pOut->vecOrigin[0]    = vecOrigin.x;
	pOut->vecOrigin[1]    = vecOrigin.y;
	pOut->vecOrigin[2]    = vecOrigin.z;
	pOut->vecVelocity[0]  = m_vecAbsVelocity.x;
	pOut->vecVelocity[1]  = m_vecAbsVelocity.y;
	pOut->vecVelocity[2]  = m_vecAbsVelocity.z;

	if ( CMoveData *move = g_pMoveData )
	{
		pOut->vecOutWishVel[0] = move->m_outWishVel.x;
		pOut->vecOutWishVel[1] = move->m_outWishVel.y;
		pOut->vecOutWishVel[2] = move->m_outWishVel.z;
		pOut->flOutStepHeight  = move->m_outStepHeight;
	}

	pOut->flMaxSpeed         = m_flMaxSpeed;
	pOut->nFlags             = m_fFlags;
	pOut->nMoveType          = m_MoveType;

	pOut->nTouched       = m_nMoveTouched;
	pOut->fTouchedGround = m_fMoveTouchedGround;
	for ( int i = 0; i < m_nMoveTouched && i < SOURCESHARP_PLAYER_TOUCH_MAX; ++i )
		pOut->hTouched[i] = m_hMoveTouched[i];
	pOut->flFrameTime        = gpGlobals->frametime;
	pOut->flTickInterval     = gpGlobals->interval_per_tick;

	// The ground, as lookups: managed decides whether it is physics ground.
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
	m_pShadowStand       = pStand;
	m_pShadowCrouch      = pCrouch;
	m_pPhysicsController = pController;
	m_pShadowActive      = NULL;
	m_nVCollisionState   = -1;

	// Remembered so the teardown can tell "mine to free" from "the environment
	// that owned it has gone" -- see the member.
	m_pShadowEnv = physenv;
	g_nSourceSharpPlayerShadowsLive++;
}

void CStandalonePlayer::CommitShadow( int nState, bool bAfterCommand )
{
	m_nVCollisionState = nState;
	m_pShadowActive = nState == VPHYS_WALK ? m_pShadowStand
	                : nState == VPHYS_CROUCH ? m_pShadowCrouch
	                : NULL;

	if ( !bAfterCommand )
		return;

	// The move data's step height is consumed here, once per command (stock's
	// PostThinkVPhysics). m_oldOrigin, which stock sets beside it, is
	// managed's (PlayerMoveMemory).
	if ( g_pMoveData )
		g_pMoveData->m_outStepHeight = 0.0f;
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

	// A body already held is torn down first, as stock destroys before it
	// builds; managed only asks with none held, so this is the belt.
	pPlayer->DestroyVCollision();
	pPlayer->AdoptShadow( static_cast< IPhysicsObject * >( pStand ), static_cast< IPhysicsObject * >( pCrouch ),
	                      static_cast< IPhysicsPlayerController * >( pController ) );
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
	// THE ENVIRONMENT THAT BUILT THEM, OR NOTHING. `physenv` is a global that is
	// destroyed and recreated at every level change; a shadow whose environment
	// has been replaced is three pointers into freed memory, and handing them to
	// the NEW environment's DestroyObject frees whatever it put there instead.
	// So a mismatch drops the pointers and frees nothing: the old environment
	// took the objects with it when it went.
	IPhysicsEnvironment *pEnv = ( physenv == m_pShadowEnv ) ? physenv : NULL;

	// Counted once per set, whether or not this call frees anything -- the
	// mismatched-environment arm below drops the pointers instead, and a set
	// that is dropped has stopped being in any solver just as much as one that
	// is freed.
	if ( m_pShadowEnv )
		g_nSourceSharpPlayerShadowsLive--;

	if ( m_pPhysicsController )
	{
		if ( pEnv )
			pEnv->DestroyPlayerController( m_pPhysicsController );
		m_pPhysicsController = NULL;
	}

	// The game data goes before the object does, for DestroyPhysics's reason:
	// DestroyObject is DEFERRED while the solver is stepping, so the object
	// outlives this call and VPhysics may still hand it to a callback about a
	// player that is already gone.
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

	m_pShadowActive    = NULL;
	m_pShadowEnv       = NULL;
	m_nVCollisionState = -1;
}

// THE PLAYER'S STEP IS MANAGED'S (plan_physics_bones_native_zero 2.3): which
// hull is awake after the step, its stress (CBasePlayer::VPhysicsUpdate's
// CalculateObjectStress) and its VPhysicsShadowUpdate are PhysicsFrame's,
// over PlayerShadowBodies.AwakeShadow and PlayerShadowStress, reading
// vphysics through its own tables.

//-----------------------------------------------------------------------------
// THE STANDALONE CLIENT'S CONSOLE COMMANDS ARE MANAGED (plan_shim.md §5 3b,
// lane 3c-entityshell), and what is left here is the crossing itself.
//
// A ~120-line verb table stood here -- kill, explode, hurtme, setpos, noclip,
// notarget, setang -- with an sv_cheats gate spelt by hand in front of it, and
// fifteen CON_COMMANDs below it feeding the same table through a formatted
// string and a re-tokenise. Every one of those is a DECISION: which damage type
// a suicide carries, whether a cheat is refused out loud, whether setpos clears
// velocity. SourceSharp.Server/PlayerCheatCommands.cs holds them as [ConCommand]
// classes now, and the sv_cheats gate is DevCommand's, which refuses in stock's
// own words.
//
// TWO CROSSINGS WERE WHAT KEPT IT NATIVE, and they are what this step added:
// PlayerSnapEyeAngles (setang -- CPlayerState::fixangle is engine memory the
// engine reads back through GetPlayerState) and PlayerSetMoveType (noclip -- a
// move-type write from between frames, which the movement loop's state
// writeback cannot be). Both are in shell_entityapi.cpp beside the other player
// slots.
//
// WHAT IS LEFT IS THE MANAGED FIRST REFUSAL, unchanged and still first: this is
// IServerGameClients::ClientCommand's body for a hierarchy player
// (shell_gameclients.cpp:427), and it is reached only by words that are NOT
// registered ConCommands -- a registered name is dispatched by the engine and
// never arrives here. So ss_use, ss_slot, invnext, invprev and lastinv come
// through this and are answered by ServerModule.OnClientCommand; everything the
// verb table used to handle is now a registered name and takes the other door.
//
// AN UNHANDLED WORD IS SAID, NOT SWALLOWED. The gate that used to sit here
// returned in silence for anything it did not know, which is how the stock
// binds appeared dead in a normal game and worked in a cheats one.
//-----------------------------------------------------------------------------
void StandalonePlayer_ClientCommand( CStandalonePlayer *pPlayer, const CCommand &args )
{
	if ( !pPlayer || args.ArgC() < 1 )
		return;

	// E15: the server's managed-built area.
	IManagedCommands *pCommands = ManagedCommands();

	if ( !pCommands )
	{
		Msg( "%s: the managed server is not loaded, so nothing answered.\n", args[0] );
		return;
	}

	// THE WHOLE LINE, quotes and all. ArgS would drop the verb, and the tokens
	// the engine already split are not reachable across the boundary as a set.
	pCommands->OnClientCommand( pPlayer->ManagedHandle(), args.GetCommandString() );
}

//-----------------------------------------------------------------------------
// WHO TYPED IT. A registered name never reaches ClientCommand at all -- the
// engine dispatches it -- so a ConCommand has to ask for its subject rather
// than being handed one, exactly as stock's handlers did through
// UTIL_GetCommandClient. That is what made every stock spelling dead here:
// they asked, downcast the answer to a CBasePlayer, got NULL for a hierarchy
// player and returned without a word.
//
// Two callers left, and they are the two kinds of caller there are: ss_ground
// (shell_standalone_player_debug.cpp, debug builds only), which is native, and
// SourceSharp_GetCommandClientHandle, which is how every managed [ConCommand]
// gets the same fact.
//-----------------------------------------------------------------------------
CStandalonePlayer *StandalonePlayer_CommandClient()
{
	return CStandalonePlayer::FromIndex( UTIL_GetCommandClientIndex() );
}

//-----------------------------------------------------------------------------
// The same fact, for a C#-declared ConCommand.
//
// ConCommandDispatch carries an id and an argument string and nothing else, so
// a [ConCommand] class in C# could not name the player who ran it — and every
// development command that acts on "the player" or on "what the player is
// looking at" needs exactly that. Without this slot the ent_* family could not
// be ordinary managed console commands at all and needed a second dispatcher
// riding on the client-command crossing, which is what this replaces.
//
// Zero when nobody issued it: the server console with no player, or a slot
// whose player has not spawned. The managed side says so out loud rather than
// acting on a handle that resolves to nothing.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_GetCommandClientHandle( void )
{
	if ( CStandalonePlayer *pPlayer = StandalonePlayer_CommandClient() )
		return pPlayer->ManagedHandle();

	return INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// ss_ground, the instrument the ground divergence was measured with, is a
// diagnostic: it lives in shell_standalone_player_debug.cpp, which only a debug
// build compiles, with the note on why the other ss_* spellings are managed.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE DEVELOPMENT CONSOLE — forty-three more of the same bug, and why almost
// none of it is here any more.
//
// docs/divergences.md, "Verdict A": forty-three server ConCommands are
// registered whose handlers open by downcasting UTIL_GetCommandClient's
// answer to a player,
// get NULL because the standalone player is not a CBasePlayer, and RETURN
// WITHOUT A WORD. The whole ent_* debugging family, give, fov, kill, explode,
// setmodel, the prop_*_create pair, cast_ray, surfaceprop. None of them is
// broken by its own logic; the logic never runs.
//
// All of them are answered in managed code now, and REGISTERED there too:
// DevCommands.cs declares one [ConCommand] class per spelling, discovered by
// the same pass that registers cs_hello, so the console builds a real
// ConCommand for each and dispatches it straight to C#. Thirty-nine
// SS_DEV_COMMAND lines and their macro stood here until then, forwarding the
// whole line through the CLIENT COMMAND crossing to a second dispatcher — a
// parallel console-command mechanism whose only reason to exist was that
// ConCommandDispatch could not name the player who typed the word.
// SourceSharp_GetCommandClientHandle above supplies exactly that, and the
// parallel mechanism goes with it.
//
// The stock registrations these replace are still #ifndef SOURCESHARP'd at
// their own sites, and the comment there names this file. Two live
// registrations of one name is a registration-order race whose loser is
// silent; a guard is deterministic and greppable.
//
// WHAT IS LEFT: `use`, and only because it is not one of DevCommands' — it IS
// ss_use, one selection rule under two spellings, answered in
// ServerModule.OnClientCommand beside it. It still needs a registration of its
// own: a client console's unknown word is forwarded to the server as a client
// command and would reach the crossing anyway, but engine->ServerCommand —
// which every tool here drives the game through, this mod's development API
// included — only reaches a ConCommand.
//-----------------------------------------------------------------------------
static void StandalonePlayer_RouteVerbatim( const CCommand &args )
{
	const char *pName = args.ArgC() >= 1 ? args[0] : "command";
	CStandalonePlayer *pPlayer = StandalonePlayer_CommandClient();

	if ( !pPlayer )
	{
		// SAID, not swallowed. "No player issued that" and "the player
		// ignored it" are the same nothing from a console. The server console
		// with nobody connected reaches here, and so does a slot whose player
		// has not spawned yet.
		Msg( "%s: no player issued that, so nothing was done.\n", pName );
		return;
	}

	// E15: the server's managed-built area.
	IManagedCommands *pCommands = ManagedCommands();

	if ( !pCommands )
	{
		Msg( "%s: the managed server is not loaded, so nothing answered.\n", pName );
		return;
	}

	// The WHOLE line, quotes and all. ArgS would drop the verb, and the tokens
	// the engine already split are not reachable across the boundary as a set —
	// GetCommandString is the untouched text, so a quoted argument survives to
	// the managed splitter.
	if ( !pCommands->OnClientCommand( pPlayer->ManagedHandle(), args.GetCommandString() ) )
		Msg( "%s: the managed server did not handle it.\n", pName );
}

SS_NATIVE_COMMAND( use )
{
	StandalonePlayer_RouteVerbatim( args );
}

//-----------------------------------------------------------------------------
// The handle projection the player arms resolve through.
//-----------------------------------------------------------------------------
CStandalonePlayer *SourceSharp_ResolveStandalonePlayer( uint32_t hEntity )
{
	CStandaloneEntity *pEntity = SourceSharp_ResolveStandalone( hEntity );

	if ( !pEntity || !pEntity->IsPlayer() )
		return NULL;

	return SourceSharp_PlayerOf( pEntity );
}

//-----------------------------------------------------------------------------
// Creation, from ClientPutInServer.
//-----------------------------------------------------------------------------
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

	// Claimed by PostConstruct on success; anything left standing here means
	// creation failed before the claim and must not leak onto the next entity.
	SourceSharp_ClaimPendingPlayerEdict();

	if ( !pNet )
		return NULL;

	// On this hierarchy the entity IS its networkable — one static walk down.
	// The factory only ever constructs CStandalonePlayer for the class whose
	// desc says bPlayer, and "ss_player" is that class by registration.
	CStandalonePlayer *pPlayer = SourceSharp_PlayerOf( SourceSharp_ShellOf( pNet ) );

	const int nIndex = pPlayer->EntIndex();

	if ( nIndex >= 1 && nIndex <= ss::kMaxPlayers )
	{
		s_StandalonePlayers[ nIndex ] = pPlayer;
	}
	else
	{
		Warning( "[SourceSharp] standalone player: bound to edict index %d, outside 1..%d\n",
			nIndex, ss::kMaxPlayers );
	}

	pPlayer->SetPlayerName( pPlayerName );

	return pPlayer;
}

//-----------------------------------------------------------------------------
// cs_sv_whois, which shows what the UTIL_PlayerSlotBy* lookups answer, is a
// diagnostic: it lives in shell_standalone_player_debug.cpp, which only a debug
// build compiles.
//-----------------------------------------------------------------------------
