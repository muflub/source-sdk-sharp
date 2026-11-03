//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// IInput as a thunk set: every decision is managed ClientInput's, reached
// through IManagedInputClient. What stays native is what the engine holds or
// reads:
//   - the CUserCmd ring and its verified copies: the engine asks the DLL to
//     encode commands out of it, and prediction reads them back by pointer;
//   - kbutton_t tokens for buttons other native code names by address; their
//     state is managed's and a token is only an id;
//   - the IShimInputClient bodies.
// Until the managed runtime is up every thunk answers as an idle CInput would.
// No vgui_controls headers: their "using namespace vgui" makes ::IInput ambiguous.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "iinput.h"
#include "kbutton.h"
#include "usercmd.h"

#include "iclientmode.h"
#include "iviewrender.h"
#include "prediction.h"

#include "game/client/iviewport.h"
#include "voice_status.h"
#include "vgui/ISurface.h"

#include "view_facts.h"

#include "shell_input.h"
#include "shell_viewer_facts.h"

#include "ss_bitblock.h"
#include "ss_command_line.h"
#include "shell_client_engine_interfaces.h"

// vgui_int.cpp's cursor, as in_mouse.cpp reached it.
void GetVGUICursorPos(const CClientEngineInterfaces& engines, int& x, int& y);
void SetVGUICursorPos(const CClientEngineInterfaces& engines, int x, int y);

// The button tokens input.h names as globals. Each address is an id into
// KeyButtons.All, in in_main.cpp's declaration order; the state is managed's.
const CSourceSharpInput::ButtonToken_t CSourceSharpInput::s_ButtonTokens[] = {
	{&CSourceSharpInput::m_in_speed, 0},     {&CSourceSharpInput::m_in_jlook, 2},
	{&CSourceSharpInput::m_in_strafe, 3},    {&CSourceSharpInput::m_in_forward, 5},
	{&CSourceSharpInput::m_in_back, 6},      {&CSourceSharpInput::m_in_moveleft, 7},
	{&CSourceSharpInput::m_in_moveright, 8}, {&CSourceSharpInput::m_in_graph, 9},
	{&CSourceSharpInput::m_in_joyspeed, 10},
};

int32_t CSourceSharpInput::ButtonTokenId(const kbutton_t* pKey) const
{
	for (int i = 0; i < (int)ARRAYSIZE(s_ButtonTokens); i++)
	{
		if (&(this->*s_ButtonTokens[i].pKey) == pKey)
			return s_ButtonTokens[i].nId;
	}
	return -1;
}

// IInput's CAM_* operations, InputCamOp's argument (InputCamOps in C#).
enum
{
	CAMOP_IS_THIRD_PERSON = 0,
	CAMOP_TO_THIRD_PERSON,
	CAMOP_TO_FIRST_PERSON,
	CAMOP_START_MOUSE_MOVE,
	CAMOP_END_MOUSE_MOVE,
	CAMOP_START_DISTANCE,
	CAMOP_END_DISTANCE,
	CAMOP_INTERCEPTING_MOUSE,
	CAMOP_TO_ORTHOGRAPHIC,
	CAMOP_IS_ORTHOGRAPHIC,
	CAMOP_CAMERA_THIRD_THINK,
};

// nPublished's bits (InputPublished in InputMailbox.cs).
enum
{
	INPUT_PUBLISHED_VALID = 1 << 0,
	INPUT_PUBLISHED_ACTIVATE_NOOP = 1 << 1,
	INPUT_PUBLISHED_DEACTIVATE_NOOP = 1 << 2,
	INPUT_PUBLISHED_THIRD_PERSON = 1 << 3,
};

CSourceSharpInput::CSourceSharpInput(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
									 CHud& hud, CViewerFacts& viewerFacts, CPrediction& prediction,
									 CVoiceStatus& voiceStatus)
	: m_Areas(areas)
	, m_Engines(engines)
	, m_Hud(hud)
	, m_Prediction(prediction)
	, m_VoiceStatus(voiceStatus)
	, m_ViewerFacts(viewerFacts)
	, m_pCommands(NULL)
	, m_pVerifiedCommands(NULL)
	, m_pCameraThirdData(NULL)
{
	V_memset(&m_Mailbox, 0, sizeof(m_Mailbox));
	m_Mailbox.nStructSize = sizeof(m_Mailbox);
}

SourceSharpInputMailbox* CSourceSharpInput::Mailbox()
{
	return &m_Mailbox;
}

bool CSourceSharpInput::Published(uint32_t nBit) const
{
	return (m_Mailbox.nPublished & INPUT_PUBLISHED_VALID) && (m_Mailbox.nPublished & nBit);
}

void CSourceSharpInput::Init_All()
{
	Assert(!m_pCommands);
	m_pCommands = new CUserCmd[MULTIPLAYER_BACKUP];
	m_pVerifiedCommands = new CVerifiedUserCmd[MULTIPLAYER_BACKUP];

	if (IManagedInputClient* p = Managed())
		p->InputInit(ss::CommandLineHas("-nomouse") ? 1 : 0);
}

void CSourceSharpInput::Shutdown_All()
{
	if (IManagedInputClient* p = Managed())
		p->InputShutdown();

	delete[] m_pCommands;
	m_pCommands = NULL;
	delete[] m_pVerifiedCommands;
	m_pVerifiedCommands = NULL;
}

int CSourceSharpInput::GetButtonBits(int bResetState)
{
	IManagedInputClient* p = Managed();
	return p ? p->InputGetButtonBits(bResetState) : 0;
}

void CSourceSharpInput::CreateMove(int sequence_number, float input_sample_frametime, bool active)
{
	CUserCmd* cmd = &m_pCommands[sequence_number % MULTIPLAYER_BACKUP];
	CVerifiedUserCmd* pVerified = &m_pVerifiedCommands[sequence_number % MULTIPLAYER_BACKUP];

	cmd->Reset();
	cmd->command_number = sequence_number;
	cmd->tick_count = m_Engines.m_pGlobals->tickcount;

	if (IManagedInputClient* p = Managed())
	{
		SourceSharpDecodedUserCmd flat;
		V_memset(&flat, 0, sizeof(flat));
		flat.cmd.struct_size = sizeof(flat.cmd);
		flat.cmd.command_number = cmd->command_number;
		flat.cmd.tick_count = cmd->tick_count;

		p->InputCreateMove(sequence_number, input_sample_frametime, active ? 1 : 0, &flat);
		FromFlat(flat, cmd);
	}

	pVerified->m_cmd = *cmd;
	pVerified->m_crc = cmd->GetChecksum();
}

void CSourceSharpInput::ExtraMouseSample(float frametime, bool active)
{
	if (IManagedInputClient* p = Managed())
		p->InputExtraMouseSample(frametime, active ? 1 : 0);
}

bool CSourceSharpInput::WriteUsercmdDeltaToBuffer(bf_write* buf, int from, int to, bool isnewcommand)
{
	Assert(m_pCommands);

	CUserCmd zero;
	const CUserCmd* pFrom = (from == -1) ? &zero : Verified(from, zero);
	const CUserCmd* pTo = Verified(to, zero);

	const int nBits = Encode(buf, pTo, pFrom);
	if (nBits >= 0 && !buf->IsOverflowed())
		return true;

	Msg("WARNING! User command buffer overflow(%i %i), last cmd was %i bits long\n", from, to, nBits);
	return false;
}

void CSourceSharpInput::EncodeUserCmdToBuffer(bf_write& buf, int sequence_number)
{
	CUserCmd zero;
	const CUserCmd* pCmd = GetUserCmd(sequence_number);
	Encode(&buf, pCmd ? pCmd : &zero, &zero);
}

void CSourceSharpInput::DecodeUserCmdFromBuffer(bf_read& buf, int sequence_number)
{
	IManagedInputClient* p = Managed();
	uint8_t block[ss::kBitBlockHeaderBytes + kMaxEncodedBytes];
	if (!p || ss::BitBlockFromEngine(buf, block, sizeof(block)) < 0)
	{
		buf.SetOverflowFlag();
		return;
	}

	SourceSharpDecodedUserCmd flat;
	V_memset(&flat, 0, sizeof(flat));
	const int nUsed = p->InputDecodeUserCmd(block, sizeof(block), &flat);
	if (nUsed < 0)
	{
		buf.m_iCurBit = buf.m_nDataBits;
		buf.SetOverflowFlag();
		return;
	}
	buf.m_iCurBit += nUsed;

	CUserCmd* cmd = &m_pCommands[sequence_number % MULTIPLAYER_BACKUP];
	cmd->Reset();
	FromFlat(flat, cmd);
	cmd->command_number = flat.cmd.command_number;
	cmd->tick_count = flat.cmd.tick_count;
}

CUserCmd* CSourceSharpInput::GetUserCmd(int sequence_number)
{
	Assert(m_pCommands);
	CUserCmd* pSlot = &m_pCommands[sequence_number % MULTIPLAYER_BACKUP];
	// NULL once a newer command has taken the slot.
	return pSlot->command_number == sequence_number ? pSlot : NULL;
}

float CSourceSharpInput::KeyState(kbutton_t* key)
{
	IManagedInputClient* p = Managed();
	int32_t nId = ButtonTokenId(key);
	return (p && nId >= 0) ? p->InputKeyState(nId) : 0.0f;
}

int CSourceSharpInput::KeyEvent(int down, ButtonCode_t keynum, const char* pszCurrentBinding)
{
	IManagedInputClient* p = Managed();
	int nAnswer = p ? p->InputKeyEvent(down, (int32_t)keynum, pszCurrentBinding) : -1;
	if (nAnswer >= 0)
		return nAnswer;

	// Managed's -1: the client mode's to answer, as stock's last line.
	if (m_Engines.m_pClientMode)
		return m_Engines.m_pClientMode->KeyInput(down, keynum, pszCurrentBinding);
	return 1;
}

kbutton_t* CSourceSharpInput::FindKey(const char* name)
{
	if (!Q_stricmp(name, "in_graph"))
		return &m_in_graph;
	if (!Q_stricmp(name, "in_jlook"))
		return &m_in_jlook;
	return NULL;
}

void CSourceSharpInput::Joystick_Advanced()
{
	if (IManagedInputClient* p = Managed())
		p->InputJoystickAdvanced();
}

void CSourceSharpInput::Joystick_SetSampleTime(float frametime)
{
	m_Mailbox.flJoystickSampleTime = frametime;
	m_Mailbox.nJoystickSampleSeq++;
}

void CSourceSharpInput::IN_SetSampleTime(float frametime)
{
	m_Mailbox.flKeyboardSampleTime = frametime;
	m_Mailbox.nKeyboardSampleSeq++;
}

void CSourceSharpInput::AccumulateMouse()
{
	if (IManagedInputClient* p = Managed())
		p->InputAccumulateMouse();
}

void CSourceSharpInput::ActivateMouse()
{
	if (Published(INPUT_PUBLISHED_ACTIVATE_NOOP))
		return;
	if (IManagedInputClient* p = Managed())
		p->InputActivateMouse();
}

void CSourceSharpInput::DeactivateMouse()
{
	if (Published(INPUT_PUBLISHED_DEACTIVATE_NOOP))
		return;
	if (IManagedInputClient* p = Managed())
		p->InputDeactivateMouse();
}

void CSourceSharpInput::ClearStates()
{
	if (IManagedInputClient* p = Managed())
		p->InputClearStates();
}

float CSourceSharpInput::GetLookSpring()
{
	IManagedInputClient* p = Managed();
	return p ? p->InputGetLookSpring() : 0.0f;
}

void CSourceSharpInput::GetFullscreenMousePos(int* mx, int* my, int* unclampedx, int* unclampedy)
{
	IManagedInputClient* p = Managed();
	if (!p)
		return;
	int32_t pos[4] = {*mx, *my, unclampedx ? *unclampedx : 0, unclampedy ? *unclampedy : 0};
	p->InputGetFullscreenMousePos(pos);
	*mx = pos[0];
	*my = pos[1];
	if (unclampedx)
		*unclampedx = pos[2];
	if (unclampedy)
		*unclampedy = pos[3];
}

void CSourceSharpInput::SetFullscreenMousePos(int mx, int my)
{
	if (IManagedInputClient* p = Managed())
		p->InputSetFullscreenMousePos(mx, my);
}

void CSourceSharpInput::ResetMouse()
{
	if (IManagedInputClient* p = Managed())
		p->InputResetMouse();
}

float CSourceSharpInput::GetLastForwardMove()
{
	IManagedInputClient* p = Managed();
	return p ? p->InputGetLastForwardMove() : 0.0f;
}

float CSourceSharpInput::Joystick_GetForward()
{
	return JoystickAxis(0);
}

float CSourceSharpInput::Joystick_GetSide()
{
	return JoystickAxis(1);
}

float CSourceSharpInput::Joystick_GetPitch()
{
	return JoystickAxis(2);
}

float CSourceSharpInput::Joystick_GetYaw()
{
	return JoystickAxis(3);
}

void CSourceSharpInput::CAM_Think()
{
	IManagedInputClient* p = Managed();
	if (!p)
		return;

	// A mod's camera data is held by pointer, as stock held it: its current
	// values go to managed each think.
	if (m_pCameraThirdData)
	{
		float data[4] = {m_pCameraThirdData->m_flPitch, m_pCameraThirdData->m_flYaw, m_pCameraThirdData->m_flDist,
						 m_pCameraThirdData->m_flLag};
		p->InputCamSetCameraThirdData(data, NULL);
	}
	p->InputCamThink();
}

int CSourceSharpInput::CAM_IsThirdPerson()
{
	if (m_Mailbox.nPublished & INPUT_PUBLISHED_VALID)
		return Published(INPUT_PUBLISHED_THIRD_PERSON) ? 1 : 0;
	return CamOp(CAMOP_IS_THIRD_PERSON);
}

void CSourceSharpInput::CAM_ToThirdPerson()
{
	CamOp(CAMOP_TO_THIRD_PERSON);
}

void CSourceSharpInput::CAM_ToFirstPerson()
{
	CamOp(CAMOP_TO_FIRST_PERSON);
}

void CSourceSharpInput::CAM_StartMouseMove()
{
	CamOp(CAMOP_START_MOUSE_MOVE);
}

void CSourceSharpInput::CAM_EndMouseMove()
{
	CamOp(CAMOP_END_MOUSE_MOVE);
}

void CSourceSharpInput::CAM_StartDistance()
{
	CamOp(CAMOP_START_DISTANCE);
}

void CSourceSharpInput::CAM_EndDistance()
{
	CamOp(CAMOP_END_DISTANCE);
}

int CSourceSharpInput::CAM_InterceptingMouse()
{
	return CamOp(CAMOP_INTERCEPTING_MOUSE);
}

void CSourceSharpInput::CAM_ToOrthographic()
{
	CamOp(CAMOP_TO_ORTHOGRAPHIC);
}

bool CSourceSharpInput::CAM_IsOrthographic() const
{
	return CamOp(CAMOP_IS_ORTHOGRAPHIC) != 0;
}

void CSourceSharpInput::CAM_OrthographicSize(float& w, float& h) const
{
	IManagedInputClient* p = Managed();
	float size[2] = {0.0f, 0.0f};
	if (p)
		p->InputCamOrthographicSize(size);
	w = size[0];
	h = size[1];
}

void CSourceSharpInput::SetPreferredGameActionSet(GameActionSet_t action_set)
{
	if (IManagedInputClient* p = Managed())
		p->InputSetPreferredGameActionSet((int32_t)action_set);
}

GameActionSet_t CSourceSharpInput::GetPreferredGameActionSet()
{
	IManagedInputClient* p = Managed();
	return p ? (GameActionSet_t)p->InputGetPreferredGameActionSet() : GAME_ACTION_SET_MENUCONTROLS;
}

void CSourceSharpInput::SetGameActionSetFlags(GameActionSetFlags_t action_set_flags)
{
	if (IManagedInputClient* p = Managed())
		p->InputSetGameActionSetFlags((int32_t)action_set_flags);
}

void CSourceSharpInput::LevelInit()
{
	if (IManagedInputClient* p = Managed())
		p->InputLevelInit();
}

void CSourceSharpInput::ClearInputButton(int bits)
{
	if (IManagedInputClient* p = Managed())
		p->InputClearInputButton(bits);
}

void CSourceSharpInput::CAM_SetCameraThirdData(CameraThirdData_t* pCameraData, const QAngle& vecCameraOffset)
{
	m_pCameraThirdData = pCameraData;
	IManagedInputClient* p = Managed();
	if (!p)
		return;
	float offset[3] = {vecCameraOffset[0], vecCameraOffset[1], vecCameraOffset[2]};
	if (pCameraData)
	{
		float data[4] = {pCameraData->m_flPitch, pCameraData->m_flYaw, pCameraData->m_flDist, pCameraData->m_flLag};
		p->InputCamSetCameraThirdData(data, offset);
	}
	else
	{
		p->InputCamSetCameraThirdData(NULL, offset);
	}
}

void CSourceSharpInput::CAM_CameraThirdThink()
{
	CamOp(CAMOP_CAMERA_THIRD_THINK);
}

bool CSourceSharpInput::EnableJoystickMode()
{
	IManagedInputClient* p = Managed();
	return p && p->InputEnableJoystickMode() != 0;
}

bool CSourceSharpInput::IsSteamControllerActive()
{
	IManagedInputClient* p = Managed();
	return p && p->InputIsSteamControllerActive() != 0;
}

void CSourceSharpInput::FromFlat(const SourceSharpDecodedUserCmd& flat, CUserCmd* cmd)
{
	cmd->viewangles[0] = flat.cmd.viewangles[0];
	cmd->viewangles[1] = flat.cmd.viewangles[1];
	cmd->viewangles[2] = flat.cmd.viewangles[2];
	cmd->forwardmove = flat.cmd.forwardmove;
	cmd->sidemove = flat.cmd.sidemove;
	cmd->upmove = flat.cmd.upmove;
	cmd->buttons = flat.cmd.buttons;
	cmd->impulse = (byte)flat.cmd.impulse;
	cmd->weaponselect = flat.cmd.weaponselect;
	cmd->weaponsubtype = flat.weaponsubtype;
	cmd->random_seed = flat.random_seed;
	cmd->mousedx = (short)flat.cmd.mousedx;
	cmd->mousedy = (short)flat.cmd.mousedy;
}

void CSourceSharpInput::ToFlat(const CUserCmd& cmd, SourceSharpDecodedUserCmd* flat)
{
	V_memset(flat, 0, sizeof(*flat));
	flat->cmd.struct_size = sizeof(flat->cmd);
	flat->cmd.command_number = cmd.command_number;
	flat->cmd.tick_count = cmd.tick_count;
	flat->cmd.viewangles[0] = cmd.viewangles[0];
	flat->cmd.viewangles[1] = cmd.viewangles[1];
	flat->cmd.viewangles[2] = cmd.viewangles[2];
	flat->cmd.forwardmove = cmd.forwardmove;
	flat->cmd.sidemove = cmd.sidemove;
	flat->cmd.upmove = cmd.upmove;
	flat->cmd.buttons = cmd.buttons;
	flat->cmd.impulse = cmd.impulse;
	flat->cmd.weaponselect = cmd.weaponselect;
	flat->weaponsubtype = cmd.weaponsubtype;
	flat->random_seed = cmd.random_seed;
	flat->cmd.mousedx = cmd.mousedx;
	flat->cmd.mousedy = cmd.mousedy;
}

int CSourceSharpInput::Encode(bf_write* buf, const CUserCmd* pTo, const CUserCmd* pFrom) const
{
	IManagedInputClient* p = Managed();
	if (!p)
	{
		buf->SetOverflowFlag();
		return -1;
	}
	SourceSharpDecodedUserCmd to, from;
	ToFlat(*pTo, &to);
	ToFlat(*pFrom, &from);
	uint8_t block[ss::kBitBlockHeaderBytes + kMaxEncodedBytes];
	const int nBits = p->InputEncodeUserCmd(&to, &from, block, sizeof(block));
	if (!ss::BitBlockToEngine(buf, block, sizeof(block)))
		return -1;
	return nBits;
}

CUserCmd* CSourceSharpInput::Verified(int sequence_number, CUserCmd& fallback)
{
	CUserCmd* pCmd = GetUserCmd(sequence_number);
	if (!pCmd)
		return &fallback;
	const CVerifiedUserCmd& verified = m_pVerifiedCommands[sequence_number % MULTIPLAYER_BACKUP];
	if (pCmd->GetChecksum() != verified.m_crc)
		*pCmd = verified.m_cmd;
	return pCmd;
}

float CSourceSharpInput::JoystickAxis(int nAxis)
{
	IManagedInputClient* p = Managed();
	float axes[4] = {0.0f, 0.0f, 0.0f, 0.0f};
	if (p)
		p->InputJoystickAxes(axes);
	return axes[nAxis];
}

int CSourceSharpInput::CamOp(int nOp) const
{
	IManagedInputClient* p = Managed();
	return p ? p->InputCamOp(nOp) : 0;
}

IManagedInputClient* CSourceSharpInput::Managed() const
{
	return m_Areas.m_pInputClient;
}

// The client DLL object's member; set from its Init to its Shutdown.

void CSourceSharpInput::ThirdPersonInit()
{
	if (IManagedInputClient* p = Managed())
		p->InputThirdPersonInit();
}

void CSourceSharpInput::StopAllRumble()
{
	if (IManagedInputClient* p = Managed())
		p->InputRumbleStopAll();
}

// IShimInputClient's bodies.
void CSourceSharpInput::InputCursorState(int32_t* pVisible, int32_t* pLocked)
{
	vgui::ISurface* pSurface = m_Engines.m_pVGuiSurface;
	if (pVisible)
		*pVisible = pSurface && pSurface->IsCursorVisible() ? 1 : 0;
	if (pLocked)
		*pLocked = pSurface && pSurface->IsCursorLocked() ? 1 : 0;
}

void CSourceSharpInput::InputGetCursorPos(int32_t* pX, int32_t* pY)
{
	int x = 0, y = 0;
	GetVGUICursorPos(m_Engines, x, y);
	if (pX)
		*pX = x;
	if (pY)
		*pY = y;
}

void CSourceSharpInput::InputSetCursorPos(int32_t x, int32_t y)
{
	SetVGUICursorPos(m_Engines, x, y);
}

void CSourceSharpInput::InputSetCursorArrow()
{
	if (m_Engines.m_pVGuiSurface)
		m_Engines.m_pVGuiSurface->SetCursor(vgui::dc_arrow);
}

void CSourceSharpInput::InputVguiScreenSize(int32_t* pWide, int32_t* pTall)
{
	int w = 0, h = 0;
	if (m_Engines.m_pVGuiSurface)
		m_Engines.m_pVGuiSurface->GetScreenSize(w, h);
	if (pWide)
		*pWide = w;
	if (pTall)
		*pTall = h;
}

void CSourceSharpInput::InputHudFacts(float* pSensitivity, int32_t* pKeyBits, float* pFovAdjust)
{
	if (pSensitivity)
		*pSensitivity = m_Hud.GetSensitivity();
	if (pKeyBits)
		*pKeyBits = m_Hud.m_iKeyBits;
	if (pFovAdjust)
		*pFovAdjust = m_Hud.GetFOVSensitivityAdjust();
}

void CSourceSharpInput::InputStopPitchDrift()
{
	if (m_Engines.m_pViewRender)
		m_Engines.m_pViewRender->StopPitchDrift();
}

void CSourceSharpInput::InputSetLocalViewAngles(const float* pAngles)
{
	if (!pAngles)
		return;
	QAngle angles(pAngles[0], pAngles[1], pAngles[2]);
	m_Prediction.SetLocalViewAngles(angles);
}

void CSourceSharpInput::InputViewerCameraModeChanged()
{
	m_ViewerFacts.ViewerCameraModeChanged();
}

int32_t CSourceSharpInput::InputViewerAimIsAssisted()
{
	return SourceSharp_ViewerAimIsAssisted() ? 1 : 0;
}

void* CSourceSharpInput::InputMailbox()
{
	return Mailbox();
}

// +score / +showscores (IN_ScoreDown / IN_ScoreUp's viewport half).
void CSourceSharpInput::InputShowScoreboard(int32_t bShow)
{
	if (!m_Engines.m_pViewPort)
		return;

	m_Engines.m_pViewPort->ShowPanel(PANEL_SCOREBOARD, bShow != 0);
	if (!bShow)
		m_VoiceStatus.StopSquelchMode();
}
