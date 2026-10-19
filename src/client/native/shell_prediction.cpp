//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's prediction seam ======================//
// SS_REASON b  an interface resolved by name at load
//
// WHAT IS LEFT OF PREDICTION IN C++ (plan_entity.md step 6f).
//
// The whole of the client's prediction is managed: the ring, the basis, the
// compare, the meter and the movement loop itself all live in
// ClientPlayerPrediction (SourceSharp.Client). §3.9's sentence is that
// "IPrediction remains a shim vtable of thirteen methods whose bodies call
// managed code", and this file is that vtable, ONE crossing, and the three
// questions managed cannot answer for itself.
//
// WHAT WENT, AND WHY IT IS A DELETION RATHER THAN A MOVE. Until this step the
// file carried a second prediction machine: a list of tracked classnames, a
// SourceSharpPredictionRing per matching entity, a field-aware verdict walker
// asking managed per offset, and four cs_cl_predict_* dev commands to drive it.
// It was 9c1's scaffolding, and its stated job — "the ring and the compare, on
// something harmless, verified against hierarchy entities WHILE THE PLAYER
// STILL PREDICTS NATIVELY" — is finished: the player predicts in managed code
// and carries its own meter, PredictionRing.cs is the ring, and
// PredictionJudge/PredictionErrors are the verdict. Keeping a second native
// copy of all three would be two implementations of one rule, which is the
// drift the port exists to remove. prediction_ring.h went with it; its gtests
// were ported to xUnit facts against the managed ring rather than dropped.
//
// WHERE THE NUMBERS COME FROM, because the wiring is only as honest as they
// are, and the first live run of this file caught it reading them wrong.
// Update() is handed ABSOLUTE command numbers: the last command the server has
// acknowledged and the last one sent. PostNetworkDataReceived() is handed a
// DELTA — how many commands this packet acknowledged — which stock accumulates
// and never uses as a slot index. So the split is: PostNetworkDataReceived only
// RECORDS that a world update arrived, and the compare runs at the next
// Update(), whose incoming_acknowledged is the absolute number the slots were
// stamped with. Packets are processed before the frame's Update, so the decoded
// state holds the server's word by then and nothing has replayed on top of it.
//
// THE FOUR C_BasePlayer * PARAMETERS STAY, AND ARE NEVER DEREFERENCED. They
// are the engine's ABI for IPrediction (§2): the vtable's shape is fixed by the
// interface version the engine resolves by name, so a signature that dropped
// them would be a different interface silently accepted. No C_BasePlayer can
// exist in this build, so every one of them is NULL, and each body below is
// stock's own no-player early-out written down.
//
//=============================================================================//

#include "cbase.h"
#include "prediction.h"
#include "ipredictionsystem.h"
#include "shell_standalone_client.h"
#include "input.h"
#include "usercmd.h"
#include "imovehelper.h"
#include "vphysics_interface.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE8: IManagedPredictionClient

// The lifted prediction-seed statics: the ONE storage the client and the server
// must share per command. See entity_shared_statics.h.
#include "entity_shared_statics.h"
#include "shell_gamemovement.h"   // SourceSharp_MoveManagedReport, for cs_cl_move_managed_report

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// 9c6: prediction.cpp and predictioncopy.cpp are out of the build, and this
// is what was actually left of the stock machine — the base class reduced to
// the behaviour it has had since 13c evicted C_BasePlayer. Every player-driven
// path in stock CPrediction began with GetLocalPlayer() and returned on NULL,
// and NULL is the only answer that call has; the bodies below are those
// early-outs written down, not stubs guessed at. The one line with live
// behaviour is PreEntityPacketReceived caching the incoming packet number,
// which GetIncomingPacketNumber still hands to client-created entities.
//
// The view accessors reproduce stock's no-player arm exactly (org.Init(),
// return) because that IS the measured behaviour the SMG burst held under;
// the standalone player's view goes through its own replay, not IPrediction.
//-----------------------------------------------------------------------------

// The TE_* dispatch chain still hangs off IPredictionSystem; its static lived
// at prediction.cpp:33 and moves here with the eviction.
IPredictionSystem *IPredictionSystem::g_pPredictionSystems = NULL;

CPrediction::CPrediction( void )
{
	m_bInPrediction = false;
	m_bFirstTimePredicted = false;
	m_bOldCLPredictValue = false;
	m_bEnginePaused = false;
	m_nPreviousStartFrame = -1;
	m_nCommandsPredicted = 0;
	m_nServerCommandsAcknowledged = 0;
	m_bPreviousAckHadErrors = false;
	m_bPreviousAckErrorTriggersFullLatchReset = false;
	m_nIncomingPacketNumber = 0;
	m_flIdealPitch = 0.0f;
}

CPrediction::~CPrediction( void )
{
}

void CPrediction::Init( void )
{
}

void CPrediction::Shutdown( void )
{
}

// Stock Update saved gpGlobals, called _Update, and restored them; _Update's
// first act was GetLocalPlayer(), which has answered NULL since 13c. The
// subclass below owns the frame.
void CPrediction::Update( int /*startframe*/, bool /*validframe*/,
	int /*incoming_acknowledged*/, int /*outgoing_command*/ )
{
}

void CPrediction::OnReceivedUncompressedPacket( void )
{
	m_nCommandsPredicted = 0;
	m_nServerCommandsAcknowledged = 0;
	m_nPreviousStartFrame = -1;
}

void CPrediction::PreEntityPacketReceived( int /*commands_acknowledged*/,
	int current_world_update_packet )
{
	// The one live line: client-created entities ask GetIncomingPacketNumber
	// to decide whether their creation was acked.
	m_nIncomingPacketNumber = current_world_update_packet;
}

void CPrediction::PostEntityPacketReceived( void )
{
}

void CPrediction::PostNetworkDataReceived( int commands_acknowledged )
{
	m_nServerCommandsAcknowledged += commands_acknowledged;
}

bool CPrediction::InPrediction( void ) const
{
	return m_bInPrediction;
}

bool CPrediction::IsFirstTimePredicted( void ) const
{
	return m_bFirstTimePredicted;
}

int CPrediction::GetIncomingPacketNumber( void ) const
{
	return m_nIncomingPacketNumber;
}

// Stock's no-player arms, verbatim: zero on read, refuse on write.
void CPrediction::GetViewOrigin( Vector& org )
{
	org.Init();
}

void CPrediction::SetViewOrigin( Vector& /*org*/ )
{
}

void CPrediction::GetViewAngles( QAngle& ang )
{
	ang.Init();
}

void CPrediction::SetViewAngles( QAngle& /*ang*/ )
{
}

void CPrediction::GetLocalViewAngles( QAngle& ang )
{
	ang.Init();
}

void CPrediction::SetLocalViewAngles( QAngle& /*ang*/ )
{
}

// THE PLAYER-SHAPED SURFACE IS GONE, AND SO IS _Update (plan_shim.md §6).
// RunCommand, SetupMove, FinishMove and SetIdealPitch each took a
// C_BasePlayer *, and every constructor for that type left the build in 13c --
// so none could ever be called with a non-NULL player, and each body was
// stock's own `return on NULL` early-out written down. _Update went with them:
// its first act was GetLocalPlayer(), which answers NULL.
//
// THEY WERE VIRTUAL, which is why they had to be deleted rather than ignored:
// a virtual with no definition is an undefined reference at vtable emission,
// so each cost a slot and a body for as long as it was declared.
//
// A SYMBOL CENSUS CANNOT SETTLE THIS ONE, and that is worth recording. Every
// CPrediction:: method reads DEAD to `tools/shim-orphan-reach` -- 23 of 29 --
// because every call goes through `prediction->`, which is a VIRTUAL call and
// emits no symbol. The five removed here were settled by finding their only
// caller, prediction.cpp, and confirming it is not on the client link line.

//-----------------------------------------------------------------------------
// THE SEAM, AND THE ONE CROSSING.
//
// CPrediction is compiled into client_base; the singleton the engine asks for
// by name lived in shell_client.cpp and lives here, one subclass deeper. The
// subclass overrides exactly the two methods that carry the numbers the replay
// needs and calls up for everything else, which is what makes "delegate
// everything not yet ported" a property of the class shape rather than a rule
// somebody has to keep.
//
// ONE CROSSING PER CLIENT FRAME. Everything the crossing enters — the ring
// compare, the basis restore, ninety commands of movement and the weapon
// machine — is managed calling managed. It REPLACES three crossings per
// REPLAYED command (the two OnStandalonePlayerCommand phases and
// MoveProcessMovement's client arm), so this step lowers the client's
// per-frame crossing count rather than raising it; cs_cl_crossings is where
// that is read.
//-----------------------------------------------------------------------------

// Set by PostNetworkDataReceived, spent by the next Update: a world update
// arrived, so the decoded state holds the server's word and the compare has
// something to stand on. Compares are gated on this rather than run every frame
// because between packets the state cannot have moved and a compare would
// re-report the same acknowledgement as many times as the framerate exceeds the
// packet rate.
//
// A NATIVE STATIC AND NOT A MANAGED FIELD, deliberately: it is set by an engine
// callback that does not cross, and carrying it as an argument of the crossing
// that DOES is one word against one more slot.
static bool g_bWorldUpdateArrived = false;

class CSourceSharpPrediction : public CPrediction
{
	DECLARE_CLASS( CSourceSharpPrediction, CPrediction );

public:
	virtual void Update( int startframe, bool validframe,
	                     int incoming_acknowledged, int outgoing_command ) OVERRIDE
	{
		// The base first, exactly as before: with no C_BasePlayer it is a
		// no-op that keeps the two clocks stock keeps, and running it after
		// the replay would be running it after the thing it precedes.
		BaseClass::Update( startframe, validframe, incoming_acknowledged, outgoing_command );

		// plan_client_vtables CE8: the client's managed-built area.
		if ( IManagedPredictionClient *pPrediction = ManagedPredictionClient() )
		{
			pPrediction->ClientPredictionUpdate( startframe, validframe ? 1 : 0,
			                                     incoming_acknowledged, outgoing_command,
			                                     g_bWorldUpdateArrived ? 1 : 0 );
		}

		// SPENT WHETHER OR NOT MANAGED WAS THERE TO SPEND IT. A build with no
		// managed side yet would otherwise accumulate a flag that is true
		// forever, and the first frame after the runtime came up would compare
		// against a packet several seconds old.
		g_bWorldUpdateArrived = false;
	}

	virtual void PostNetworkDataReceived( int commands_acknowledged ) OVERRIDE
	{
		// A DELTA, not a command number — see the file header. It records that
		// a world update arrived and nothing else; the compare runs at the next
		// Update, which carries the absolute number.
		g_bWorldUpdateArrived = true;

		BaseClass::PostNetworkDataReceived( commands_acknowledged );
	}
};

static CSourceSharpPrediction g_Prediction;
CPrediction *prediction = &g_Prediction;

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CSourceSharpPrediction, IPrediction,
	VCLIENT_PREDICTION_INTERFACE_VERSION, g_Prediction );

//-----------------------------------------------------------------------------
// THE THREE QUESTIONS THE MANAGED REPLAY CANNOT ANSWER FOR ITSELF.
//
// Two live here and the third (the movement bracket) lives in
// gamemovement.cpp, where the point-contents cache and the trace filter it
// needs are. Everything else about the replay — which commands to run, from
// what basis, what the loop produced, whether it held — is managed.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// One command out of the engine's input ring, by ABSOLUTE command number.
//
// CInput owns the ring and the engine owns CInput; there is no managed copy of
// either, and there should not be — a second ring would be a second answer to
// "what did this client send", which is precisely the disagreement prediction
// exists to measure.
//
// 0 when the ring does not hold that command, which ends the frame's replay
// rather than replaying a command that is not there. That happens at the top of
// a session and after a long stall, and stock's own loop breaks on the same
// NULL (prediction.cpp's RunCommand walk).
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientGetUserCmd( int32_t nCommandNumber, SourceSharpUserCmd *pOut )
{
	if ( !pOut || !::input )
		return 0;

	CUserCmd *pCmd = ::input->GetUserCmd( nCommandNumber );

	if ( !pCmd )
		return 0;

	pOut->struct_size    = sizeof( *pOut );
	pOut->viewangles[0]  = pCmd->viewangles.x;
	pOut->viewangles[1]  = pCmd->viewangles.y;
	pOut->viewangles[2]  = pCmd->viewangles.z;
	pOut->forwardmove    = pCmd->forwardmove;
	pOut->sidemove       = pCmd->sidemove;
	pOut->upmove         = pCmd->upmove;
	pOut->buttons        = pCmd->buttons;
	pOut->impulse        = pCmd->impulse;
	pOut->weaponselect   = pCmd->weaponselect;
	pOut->command_number = pCmd->command_number;
	pOut->tick_count     = pCmd->tick_count;
	pOut->mousedx        = 0;
	pOut->mousedy        = 0;

	return 1;
}

//-----------------------------------------------------------------------------
// The move helper the loop reads while a replayed command runs.
//
// SOUNDS AND IMPACTS ARE DELIBERATELY INERT, and this class exists to make them
// so rather than to add behaviour. The stock CMoveHelperClient would otherwise
// be the installed singleton: it records a touch list nothing on this side
// replays, and it plays sounds through a host pointer no standalone player ever
// sets. The standalone player has no step sounds anywhere yet, and predicted
// touch events are recorded in docs/divergences.md rather than half-fired.
//
// IT MOVED HERE FROM shell_standalone_player.cpp WITH THE REPLAY IT SERVES.
// The class is the same class; what changed is that the replay driving it is
// managed, so the install and the uninstall bracket the crossing rather than
// the C++ loop.
//-----------------------------------------------------------------------------
class CStandaloneMoveHelperClient : public IMoveHelper
{
public:
	CStandaloneMoveHelperClient() : m_pPrevious( NULL ), m_bInstalled( false ) {}

	void Install()
	{
		// NOT RE-ENTRANT, and the guard is not defensive: a Begin that failed
		// leaves End to run anyway (the managed side closes the bracket in a
		// finally), and a second Install would remember THIS helper as the
		// previous one and never hand the singleton back.
		if ( m_bInstalled )
			return;

		m_pPrevious = MoveHelper();
		SetSingleton( this );
		m_bInstalled = true;
	}

	void Uninstall()
	{
		if ( !m_bInstalled )
			return;

		SetSingleton( m_pPrevious );
		m_pPrevious = NULL;
		m_bInstalled = false;
	}

	virtual char const *GetName( EntityHandle_t handle ) const OVERRIDE { return "standaloneplayer_cl"; }
	virtual void ResetTouchList( void ) OVERRIDE {}
	virtual bool AddToTouched( const CGameTrace &tr, const Vector &impactvelocity ) OVERRIDE { return false; }
	virtual void ProcessImpacts( void ) OVERRIDE {}

	virtual void Con_NPrintf( int idx, char const *fmt, ... ) OVERRIDE {}

	virtual void StartSound( const Vector &origin, int channel, char const *sample,
	                         float volume, soundlevel_t soundlevel, int fFlags, int pitch ) OVERRIDE {}
	virtual void StartSound( const Vector &origin, const char *soundname ) OVERRIDE {}
	virtual void PlaybackEventFull( int flags, int clientindex, unsigned short eventindex,
	                                float delay, Vector &origin, Vector &angles,
	                                float fparam1, float fparam2, int iparam1, int iparam2,
	                                int bparam1, int bparam2 ) OVERRIDE {}

	virtual bool PlayerFallingDamage( void ) OVERRIDE { return true; }
	virtual void PlayerSetAnimation( PLAYER_ANIM playerAnim ) OVERRIDE {}

	virtual IPhysicsSurfaceProps *GetSurfaceProps( void ) OVERRIDE
	{
		extern IPhysicsSurfaceProps *physprops;
		return physprops;
	}

	virtual bool IsWorldEntity( const CBaseHandle &handle ) OVERRIDE
	{
		return handle.GetEntryIndex() == 0;
	}

	virtual void SetHost( CBasePlayer *host ) OVERRIDE { Assert( host == NULL ); }

private:
	IMoveHelper *m_pPrevious;
	bool         m_bInstalled;
};

static CStandaloneMoveHelperClient s_ClientMoveHelper;

// IMoveHelper's singleton, which movehelper_client.cpp's IMPLEMENT_MOVEHELPER
// defined until it left the build (C6 cluster 9, lane c6-input). Stock's
// CMoveHelperClient installed itself at static init; nothing reads the
// singleton outside the bracket above, so NULL until the first command is
// the stand-in's to fill.
IMPLEMENT_MOVEHELPER();

//-----------------------------------------------------------------------------
// The movement bracket, plus the two things about THIS SIDE that belong to it.
//
// gamemovement.cpp opens and closes the inner bracket and fills the constants,
// because the point-contents cache and the trace filter it points at this
// player are its own. It knows nothing about standalone shells or about which
// move helper this side wants, and should not.
//
// SCALE MATTERS TO THE LOOP, and its absence would be silent. shareddefs.h's
// hull macros multiply the standing and ducked hulls by it, so a scaled player
// traced with an unscaled hull walks through the walls its own model is
// touching -- and every trace would still come back with a plausible fraction.
//-----------------------------------------------------------------------------
int32_t SourceSharp_MoveBeginClientCommand( uint32_t hPlayer, SourceSharpMoveState *pOut );
void    SourceSharp_MoveEndClientCommand( int32_t bRan, int32_t *pOutGroundIndex );

int32_t SourceSharp_ClientBeginMoveCommand( uint32_t hPlayer, int32_t nCommandNumber,
                                            SourceSharpMoveState *pOut )
{
	if ( !SourceSharp_MoveBeginClientCommand( hPlayer, pOut ) )
		return 0;

	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hPlayer ) )
		pOut->flModelScale = pEntity->GetModelScale();

	// THE SHARED RANDOM STREAM, seeded from the command being replayed. Its
	// whole job is that the two sides agree: SharedRandomInt, SharedRandomFloat,
	// SharedRandomVector and SharedRandomAngle all hash this seed, so a replay
	// running at -1 would disagree with the server about every random decision
	// -- silently, and only under prediction, which is the worst possible way
	// for it to be wrong. The server does the same thing at the same point in
	// its own command runner.
	if ( ::input )
		EntityPrediction_SetSeedFromCommand( ::input->GetUserCmd( nCommandNumber ) );

	s_ClientMoveHelper.Install();
	return 1;
}

void SourceSharp_ClientEndMoveCommand( int32_t bRan, int32_t *pOutGroundIndex )
{
	s_ClientMoveHelper.Uninstall();

	// -1: "not inside a usercmd", which is what the four SharedRandom* asserts
	// are written against. Losing this would make outside-a-command
	// indistinguishable from seed zero.
	EntityPrediction_SetSeedFromCommand( NULL );

	SourceSharp_MoveEndClientCommand( bRan, pOutGroundIndex );
}

//-----------------------------------------------------------------------------
// The managed player's view state, into the shell it belongs to.
//
// EVERY FRAME THERE IS A LOCAL PLAYER, driving or not — see
// C_StandaloneEntity::PredictionDrive, which carries the argument for why the
// origin is conditional and the rest of the record is not.
//
// The handle is resolved through the slot table like every other handle on this
// hierarchy (§3.3); a handle that no longer resolves is a player who left
// between the managed frame and this call, and answering nothing is right.
//-----------------------------------------------------------------------------
void SourceSharp_ClientPlayerPredictionDrive( uint32_t hEntity,
                                              const SourceSharpClientPlayerView *pView )
{
	if ( !pView || pView->struct_size != (int32_t)sizeof( *pView ) )
		return;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return;

	pEntity->PredictionDrive( *pView );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S MOVEMENT REPORT. From shell_movement.cpp, where it was #ifdef
// CLIENT_DLL (plan_server_native_v2 T2, 2026-10-18: a shared TU keeps only what
// both DLLs import). SourceSharp_MoveManagedReport stays there, beside the
// counters it prints, and the server's debug-only cs_sv_move_managed_report
// prints the same lines from the same body.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_move_managed_report,
	"cs_cl_move_managed_report -- how many times each ported movement method ran in C# on the CLIENT" )
{
	SourceSharp_MoveManagedReport();
}
