//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IInput as a thunk set over managed ClientInput, and the IShimInputClient area: a
// by-value member of the client DLL object, handed the managed areas it calls.
// No vgui_controls headers: their "using namespace vgui" makes ::IInput ambiguous.

#pragma once

#include "iinput.h"
#include "usercmd.h"

#include "imanaged_areas_client.h"
#include "ishim_areas_client.h"
#include "shell_managed_areas_client.h"

class CViewerFacts;

// The input mailbox: one block both halves read without a crossing.
//   - native writes the two sample times with a sequence number, and
//     ClientInput applies a changed one right before it reads the value;
//   - managed publishes which calls would change nothing right now, and what
//     CAM_IsThirdPerson answers. Native skips a call only when managed said it
//     is a no-op; until the VALID bit is set it makes every call.
// Fixed-width words, no padding: the layout must match InputMailbox.cs.
struct SourceSharpInputMailbox
{
	uint32_t nStructSize;
	uint32_t nKeyboardSampleSeq;
	float flKeyboardSampleTime;
	uint32_t nJoystickSampleSeq;
	float flJoystickSampleTime;
	uint32_t nPublished;
	uint32_t nShakeSkip;
	uint32_t nShakeBits;
};

COMPILE_TIME_ASSERT(sizeof(SourceSharpInputMailbox) == 32);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, nKeyboardSampleSeq) == 4);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, flKeyboardSampleTime) == 8);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, nJoystickSampleSeq) == 12);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, flJoystickSampleTime) == 16);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, nPublished) == 20);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, nShakeSkip) == 24);
COMPILE_TIME_ASSERT(offsetof(SourceSharpInputMailbox, nShakeBits) == 28);

class CSourceSharpInput final : public ::IInput, public IShimInputClient
{
public:
	CSourceSharpInput(const CManagedAreasClient& areas, CViewerFacts& viewerFacts);

	SourceSharpInputMailbox* Mailbox();

	// Whether managed published BIT (and has published at all).
	bool Published(uint32_t nBit) const;

	virtual void Init_All() OVERRIDE;

	virtual void Shutdown_All() OVERRIDE;

	virtual int GetButtonBits(int bResetState) OVERRIDE;

	virtual void CreateMove(int sequence_number, float input_sample_frametime, bool active) OVERRIDE;

	virtual void ExtraMouseSample(float frametime, bool active) OVERRIDE;

	// What the engine asks to put on the wire. A slot the ring has moved past
	// answers the null command; a slot whose checksum moved since CreateMove
	// answers the verified copy (stock's rules). Managed encodes each command
	// into one BitBlock, which is copied into the engine's buffer whole.
	virtual bool WriteUsercmdDeltaToBuffer(bf_write* buf, int from, int to, bool isnewcommand) OVERRIDE;

	virtual void EncodeUserCmdToBuffer(bf_write& buf, int sequence_number) OVERRIDE;

	// A demo's command (CDemoPlayer): the reader's remaining bits cross as one
	// BitBlock, managed decodes one command against the null one, and the
	// engine's reader moves past exactly the bits it used.
	virtual void DecodeUserCmdFromBuffer(bf_read& buf, int sequence_number) OVERRIDE;

	virtual CUserCmd* GetUserCmd(int sequence_number) OVERRIDE;

	// No C_BaseCombatWeapon exists on this client; selection is managed.
	virtual void MakeWeaponSelection(C_BaseCombatWeapon* pWeapon) OVERRIDE {}

	virtual float KeyState(kbutton_t* key) OVERRIDE;

	virtual int KeyEvent(int down, ButtonCode_t keynum, const char* pszCurrentBinding) OVERRIDE;

	// The engine's names (in_graph, in_jlook) as tokens; the state is managed's.
	virtual kbutton_t* FindKey(const char* name) OVERRIDE;

	virtual void ControllerCommands() OVERRIDE {}

	virtual void Joystick_Advanced() OVERRIDE;

	// The sample times go into the mailbox: ClientInput reads them there.
	virtual void Joystick_SetSampleTime(float frametime) OVERRIDE;

	virtual void IN_SetSampleTime(float frametime) OVERRIDE;

	virtual void AccumulateMouse() OVERRIDE;

	// The engine asks these every frame its window gains or lacks focus;
	// managed publishes when one would change nothing.
	virtual void ActivateMouse() OVERRIDE;

	virtual void DeactivateMouse() OVERRIDE;

	virtual void ClearStates() OVERRIDE;

	virtual float GetLookSpring() OVERRIDE;

	virtual void GetFullscreenMousePos(int* mx, int* my, int* unclampedx, int* unclampedy) OVERRIDE;

	virtual void SetFullscreenMousePos(int mx, int my) OVERRIDE;

	virtual void ResetMouse() OVERRIDE;

	virtual float GetLastForwardMove() OVERRIDE;

	virtual float Joystick_GetForward() OVERRIDE;

	virtual float Joystick_GetSide() OVERRIDE;

	virtual float Joystick_GetPitch() OVERRIDE;

	virtual float Joystick_GetYaw() OVERRIDE;

	virtual void CAM_Think() OVERRIDE;

	// The view asks this every frame: managed publishes the answer.
	virtual int CAM_IsThirdPerson() OVERRIDE;

	virtual void CAM_ToThirdPerson() OVERRIDE;

	virtual void CAM_ToFirstPerson() OVERRIDE;

	virtual void CAM_StartMouseMove() OVERRIDE;

	virtual void CAM_EndMouseMove() OVERRIDE;

	virtual void CAM_StartDistance() OVERRIDE;

	virtual void CAM_EndDistance() OVERRIDE;

	virtual int CAM_InterceptingMouse() OVERRIDE;

	virtual void CAM_ToOrthographic() OVERRIDE;

	virtual bool CAM_IsOrthographic() const OVERRIDE;

	virtual void CAM_OrthographicSize(float& w, float& h) const OVERRIDE;

	virtual void SetPreferredGameActionSet(GameActionSet_t action_set) OVERRIDE;

	virtual GameActionSet_t GetPreferredGameActionSet() OVERRIDE;

	virtual void SetGameActionSetFlags(GameActionSetFlags_t action_set_flags) OVERRIDE;

	virtual void LevelInit() OVERRIDE;

	virtual void ClearInputButton(int bits) OVERRIDE;

	virtual void CAM_SetCameraThirdData(CameraThirdData_t* pCameraData, const QAngle& vecCameraOffset) OVERRIDE;

	virtual void CAM_CameraThirdThink() OVERRIDE;

	virtual bool EnableJoystickMode() OVERRIDE;

	virtual bool IsSteamControllerActive() OVERRIDE;

	// IShimInputClient.
	void InputCursorState(int32_t* pVisible, int32_t* pLocked) override;
	void InputGetCursorPos(int32_t* pX, int32_t* pY) override;
	void InputSetCursorPos(int32_t x, int32_t y) override;
	void InputSetCursorArrow() override;
	void InputVguiScreenSize(int32_t* pWide, int32_t* pTall) override;
	void InputHudFacts(float* pSensitivity, int32_t* pKeyBits, float* pFovAdjust) override;
	void InputStopPitchDrift() override;
	void InputSetLocalViewAngles(const float* pAngles) override;
	void InputViewerCameraModeChanged() override;
	int32_t InputViewerAimIsAssisted() override;
	void InputShowScoreboard(int32_t bShow) override;
	void* InputMailbox() override;

	// Stock's CThirdPersonManager::Init, at level shutdown: the manager is managed's.
	void ThirdPersonInit();

	// The client DLL's level shutdown stops the rumble mixer, which is managed's.
	void StopAllRumble();

private:
	class CVerifiedUserCmd
	{
	public:
		CUserCmd m_cmd;
		CRC32_t m_crc;
	};

	// The most one encoded command takes (every field present).
	enum
	{
		kMaxEncodedBytes = 64
	};

	// The fields a managed command carries, onto a CUserCmd (its number and
	// tick are the caller's).
	static void FromFlat(const SourceSharpDecodedUserCmd& flat, CUserCmd* cmd);

	// A CUserCmd as the managed codec reads it: every wire field, as stored.
	static void ToFlat(const CUserCmd& cmd, SourceSharpDecodedUserCmd* flat);

	// One command into the engine's buffer as a delta against another: one
	// crossing (managed encodes into a BitBlock), one copy. Answers the bits,
	// or -1 when managed is down or the bits did not fit (the buffer's
	// overflow flag is then set, as WriteUsercmd's writes would have).
	int Encode(bf_write* buf, const CUserCmd* pTo, const CUserCmd* pFrom) const;

	// The command at a sequence number as the wire should carry it: the
	// fallback when the ring has moved past it, the verified copy when its
	// checksum no longer matches CreateMove's.
	CUserCmd* Verified(int sequence_number, CUserCmd& fallback);

	float JoystickAxis(int nAxis);

	int CamOp(int nOp) const;

	// The managed input area, from the client DLL's holders; NULL outside a load.
	IManagedInputClient* Managed() const;

	const CManagedAreasClient& m_Areas;
	CViewerFacts& m_ViewerFacts;
	CUserCmd* m_pCommands;
	CVerifiedUserCmd* m_pVerifiedCommands;
	CameraThirdData_t* m_pCameraThirdData;
	SourceSharpInputMailbox m_Mailbox;
};
