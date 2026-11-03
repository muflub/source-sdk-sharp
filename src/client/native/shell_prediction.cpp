//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The client's IPrediction. Prediction itself is managed; this is the engine's vtable,
// one crossing per frame, and the questions managed cannot answer for itself.
//
// Update() is handed absolute command numbers; PostNetworkDataReceived() a delta of
// commands acknowledged. So the latter only records that a world update arrived, and the
// compare runs at the next Update().
//
// The C_BasePlayer * parameters are part of the engine's IPrediction ABI. No C_BasePlayer
// exists in this build, so they are always NULL and never dereferenced.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "shell_prediction_client.h"
#include "ipredictionsystem.h"

#include "shell_standalone_client.h"

#include "imanaged_areas_client.h"

// The prediction-seed statics the client and the server share per command.
#include "entity_shared_statics.h"
#include "shell_gamemovement.h"
#include "shell_standalone_clients.h"
#include "shell_prediction_slots_client.h"
#include "shell_gamemovement_client.h"
#include "shell_client_engine_interfaces.h"

// The base class reduced to stock's no-player early-outs. PreEntityPacketReceived's
// packet number is live: GetIncomingPacketNumber hands it to client-created entities.

CPrediction::CPrediction()
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
}

CPrediction::~CPrediction() {}

void CPrediction::Init() {}

void CPrediction::Shutdown() {}

// The subclass below owns the frame.
void CPrediction::Update(int /*startframe*/, bool /*validframe*/, int /*incoming_acknowledged*/,
						 int /*outgoing_command*/)
{
}

void CPrediction::OnReceivedUncompressedPacket()
{
	m_nCommandsPredicted = 0;
	m_nServerCommandsAcknowledged = 0;
	m_nPreviousStartFrame = -1;
}

void CPrediction::PreEntityPacketReceived(int /*commands_acknowledged*/, int current_world_update_packet)
{
	// Client-created entities ask GetIncomingPacketNumber whether their creation was acked.
	m_nIncomingPacketNumber = current_world_update_packet;
}

void CPrediction::PostEntityPacketReceived() {}

void CPrediction::PostNetworkDataReceived(int commands_acknowledged)
{
	m_nServerCommandsAcknowledged += commands_acknowledged;
}

bool CPrediction::InPrediction() const
{
	return m_bInPrediction;
}

bool CPrediction::IsFirstTimePredicted() const
{
	return m_bFirstTimePredicted;
}

int CPrediction::GetIncomingPacketNumber() const
{
	return m_nIncomingPacketNumber;
}

// Stock's no-player arms: zero on read, refuse on write.
void CPrediction::GetViewOrigin(Vector& org)
{
	org.Init();
}

void CPrediction::SetViewOrigin(Vector& /*org*/) {}

void CPrediction::GetViewAngles(QAngle& ang)
{
	ang.Init();
}

void CPrediction::SetViewAngles(QAngle& /*ang*/) {}

void CPrediction::GetLocalViewAngles(QAngle& ang)
{
	ang.Init();
}

void CPrediction::SetLocalViewAngles(QAngle& /*ang*/) {}

void CSourceSharpPrediction::Update(int startframe, bool validframe, int incoming_acknowledged, int outgoing_command)
{
	// The base first: with no C_BasePlayer it only keeps stock's two clocks.
	BaseClass::Update(startframe, validframe, incoming_acknowledged, outgoing_command);

	if (IManagedPredictionClient* pPrediction = m_Areas.m_pPredictionClient)
	{
		pPrediction->ClientPredictionUpdate(startframe, validframe ? 1 : 0, incoming_acknowledged, outgoing_command,
											m_bWorldUpdateArrived ? 1 : 0);
	}

	// Spent even without managed, or the first frame after the runtime came up would compare
	// against a stale packet.
	m_bWorldUpdateArrived = false;
}

void CSourceSharpPrediction::PostNetworkDataReceived(int commands_acknowledged)
{
	// A delta, not a command number: only record the arrival.
	m_bWorldUpdateArrived = true;

	BaseClass::PostNetworkDataReceived(commands_acknowledged);
}

// One command out of the engine's input ring, by absolute command number. 0 when the ring
// does not hold it, which ends the frame's replay as stock's loop does.
int32_t CPredictionSlotsClient::ClientGetUserCmd(int32_t nCommandNumber, SourceSharpUserCmd* pOut)
{
	if (!pOut || !m_Engines.m_pInput)
		return 0;

	CUserCmd* pCmd = m_Engines.m_pInput->GetUserCmd(nCommandNumber);

	if (!pCmd)
		return 0;

	pOut->struct_size = sizeof(*pOut);
	pOut->viewangles[0] = pCmd->viewangles.x;
	pOut->viewangles[1] = pCmd->viewangles.y;
	pOut->viewangles[2] = pCmd->viewangles.z;
	pOut->forwardmove = pCmd->forwardmove;
	pOut->sidemove = pCmd->sidemove;
	pOut->upmove = pCmd->upmove;
	pOut->buttons = pCmd->buttons;
	pOut->impulse = pCmd->impulse;
	pOut->weaponselect = pCmd->weaponselect;
	pOut->command_number = pCmd->command_number;
	pOut->tick_count = pCmd->tick_count;
	pOut->mousedx = 0;
	pOut->mousedy = 0;

	return 1;
}

// The move helper installed while a replayed command runs. Sounds and impacts are inert:
// nothing on this side replays the touch list or plays the sounds.
class CStandaloneMoveHelperClient : public IMoveHelper
{
public:
	explicit CStandaloneMoveHelperClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	virtual char const* GetName(EntityHandle_t handle) const OVERRIDE { return "standaloneplayer_cl"; }

	virtual void ResetTouchList() OVERRIDE {}

	virtual bool AddToTouched(const CGameTrace& tr, const Vector& impactvelocity) OVERRIDE { return false; }

	virtual void ProcessImpacts() OVERRIDE {}

	virtual void Con_NPrintf(int idx, char const* fmt, ...) OVERRIDE {}

	virtual void StartSound(const Vector& origin, int channel, char const* sample, float volume,
							soundlevel_t soundlevel, int fFlags, int pitch) OVERRIDE
	{
	}

	virtual void StartSound(const Vector& origin, const char* soundname) OVERRIDE {}

	virtual void PlaybackEventFull(int flags, int clientindex, unsigned short eventindex, float delay, Vector& origin,
								   Vector& angles, float fparam1, float fparam2, int iparam1, int iparam2, int bparam1,
								   int bparam2) OVERRIDE
	{
	}

	virtual bool PlayerFallingDamage() OVERRIDE { return true; }

	virtual void PlayerSetAnimation(PLAYER_ANIM playerAnim) OVERRIDE {}

	virtual IPhysicsSurfaceProps* GetSurfaceProps() OVERRIDE { return m_Engines.m_pPhysProps; }

	virtual bool IsWorldEntity(const CBaseHandle& handle) OVERRIDE { return handle.GetEntryIndex() == 0; }

	virtual void SetHost(CBasePlayer* host) OVERRIDE { Assert(host == NULL); }

private:
	const CClientEngineInterfaces& m_Engines;
};

CPredictionSlotsClient::CPredictionSlotsClient(const CClientEngineInterfaces& engines, CStandaloneClients& standalone,
											   CShellGameMovement& gameMovement)
	: m_Engines(engines)
	, m_Standalone(standalone)
	, m_GameMovement(gameMovement)
	, m_pMoveHelper(new CStandaloneMoveHelperClient(engines))
{
}

// The movement bracket. Scale matters: the hull macros multiply the hulls by it, and an
// unscaled hull would walk through walls with plausible trace fractions.
int32_t CPredictionSlotsClient::MoveBeginClientCommand(uint32_t hPlayer, int32_t nCommandNumber,
													   SourceSharpMoveState* pOut)
{
	if (!m_GameMovement.BeginClientCommand(hPlayer, pOut))
		return 0;

	if (C_StandaloneEntity* pEntity = m_Standalone.FromHandle(hPlayer))
		pOut->flModelScale = pEntity->GetModelScale();

	m_GameMovement.SetMoveHelper(m_pMoveHelper);
	return 1;
}

void CPredictionSlotsClient::MoveEndClientCommand(int32_t bRan, int32_t* pOutGroundIndex)
{
	m_GameMovement.SetMoveHelper(NULL);

	m_GameMovement.EndClientCommand(bRan, pOutGroundIndex);
}

// The managed player's view state into its shell. A handle that no longer resolves is a
// player who just left; nothing is done.
void CPredictionSlotsClient::ClientPlayerPredictionDrive(uint32_t hEntity, const SourceSharpClientPlayerView* pView)
{
	if (!pView || pView->struct_size != (int32_t)sizeof(*pView))
		return;

	C_StandaloneEntity* pEntity = m_Standalone.FromHandle(hEntity);

	if (!pEntity)
		return;

	pEntity->PredictionDrive(*pView);
}
