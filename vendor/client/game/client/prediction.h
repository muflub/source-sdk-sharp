//========= Copyright Valve Corporation, All rights reserved. ============//

//========= SourceSharp's edit of prediction.h (vendor/README.md) =====//
// CPrediction, the client's IPrediction, which shell_prediction.cpp
// implements: moved out of the fork's edited game/client/prediction.h
// (client.so on unmodified Valve headers, user 2026-10-25).
#pragma once

#include "iprediction.h"
#include "client_entity.h"
#include "cdll_bounded_cvars.h"

class CMoveData;
class CUserCmd;

// Purpose: Implements prediction in the client .dll
class CPrediction : public IPrediction
{
	// Construction
public:
	DECLARE_CLASS_GAMEROOT(CPrediction, IPrediction);

	CPrediction();
	virtual ~CPrediction();

	virtual void Init();
	virtual void Shutdown();

	// Implement IPrediction
public:
	virtual void Update(int startframe, // World update ( un-modded ) most recently received
						bool validframe, // Is frame data valid
						int incoming_acknowledged, // Last command acknowledged to have been run by server (un-modded)
						int outgoing_command // Last command (most recent) sent to server (un-modded)
	);

	virtual void OnReceivedUncompressedPacket();

	virtual void PreEntityPacketReceived(int commands_acknowledged, int current_world_update_packet);
	virtual void PostEntityPacketReceived();
	virtual void PostNetworkDataReceived(int commands_acknowledged);

	virtual bool InPrediction() const;
	virtual bool IsFirstTimePredicted() const;

	virtual int GetIncomingPacketNumber() const;

	// The engine needs to be able to access a few predicted values
	virtual void GetViewOrigin(Vector& org);
	virtual void SetViewOrigin(Vector& org);
	virtual void GetViewAngles(QAngle& ang);
	virtual void SetViewAngles(QAngle& ang);

	virtual void GetLocalViewAngles(QAngle& ang);
	virtual void SetLocalViewAngles(QAngle& ang);

	// SourceSharp: THE FIVE PLAYER-SHAPED VIRTUALS ARE GONE -- RunCommand,
	// SetupMove, FinishMove, SetIdealPitch and _Update. Each took a C_BasePlayer *,
	// and no constructor in this build can produce one, so none of them could be
	// reached with a non-NULL player; each body was stock's own `return on NULL`
	// early-out written down. Being VIRTUAL they still cost a vtable slot and a
	// definition, which is why they are removed here rather than left.
	// Their only caller was prediction.cpp, which is not on the client link line.
	// The IPrediction surface the engine actually calls -- thirteen slots, above --
	// is untouched, and so are InPrediction, IsFirstTimePredicted and
	// GetIncomingPacketNumber, which compiled code does call.

	// Internal
protected:
private:
	void Untouch();

	// Data
protected:
	// Last object the player was standing on
	CHandle<IGameEntity> m_hLastGround;

private:
	bool m_bInPrediction;
	bool m_bFirstTimePredicted;
	bool m_bOldCLPredictValue;
	bool m_bEnginePaused;

	// Last network origin for local player
	int m_nPreviousStartFrame;

	int m_nCommandsPredicted;
	int m_nServerCommandsAcknowledged;
	int m_bPreviousAckHadErrors;
	int m_bPreviousAckErrorTriggersFullLatchReset;
	CUtlVector<CHandle<IGameEntity>> m_EntsWithPredictionErrorsInLastAck;
	int m_nIncomingPacketNumber;
};

extern CPrediction* prediction;
