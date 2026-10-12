//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the input thunk set =================================//
// SS_REASON b  an interface resolved by name at load
//
// IInput (game/client/iinput.h) AS A THUNK SET (plan_client_novalve §2
// cluster 13, lane c6-input). Every decision stock's CInput made -- the key
// buttons, the user command a frame's input builds, the mouse's filtering and
// acceleration, the joystick's curves, the Steam Controller's actions, the
// third-person camera -- is ClientInput's (src/client/managed/Input), reached
// through IManagedInputClient. in_main.cpp, in_mouse.cpp, in_joystick.cpp,
// in_steamcontroller.cpp, in_camera.cpp and cam_thirdperson.cpp left the build
// with this file's rewrite, and c_rumble.cpp after it (the mixer is ClientRumble's).
//
// WHAT STAYS HERE IS WHAT THE ENGINE HOLDS OR READS:
//
//   - the CUserCmd ring and its verified copies, because the engine asks the
//     DLL to encode commands out of it (WriteUsercmdDeltaToBuffer,
//     EncodeUserCmdToBuffer) and prediction reads them back by pointer
//     (GetUserCmd). Managed fills a field copy (SourceSharpDecodedUserCmd), so
//     CUserCmd is no K2 row (docs/abi-parity.tsv);
//   - kbutton_t tokens for the buttons other native code names by address
//     (input.h's externs: view.cpp's demo camera asks KeyState(&in_forward)).
//     Their state is managed's; a token is only an id;
//   - g_ThirdPersonManager, because clientmode_shared.cpp calls its Init();
//   - the IShimInputClient bodies: vgui's cursor, the HUD's facts, the view's
//     pitch drift, prediction's view angles and the viewport's scoreboard.
//
// Until the managed runtime is up (and after it goes) every thunk answers what
// an idle CInput would: nothing pressed, nothing moved.
//
// This file stays free of the vgui_controls headers: they carry
// "using namespace vgui", which makes ::IInput ambiguous against vgui::IInput.
//
//=============================================================================//

#include "cbase.h"
#include "iinput.h"
#include "kbutton.h"
#include "usercmd.h"
#include "bitbuf.h"
#include "cam_thirdperson.h"
#include "c_rumble.h"
#include "iclientmode.h"
#include "iviewrender.h"
#include "prediction.h"
#include "hud.h"
#include "view_facts.h"
#include "game/client/iviewport.h"
#include "voice_status.h"
#include "vgui/ISurface.h"
#include "vgui/Cursor.h"
#include "tier0/icommandline.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "sourcesharp_entrypoints.h"
#include "ss_bitblock.h"
#include "ss_command_line.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// vgui_int.cpp's cursor, as in_mouse.cpp reached it.
void GetVGUICursorPos( int &x, int &y );
void SetVGUICursorPos( int x, int y );

//-----------------------------------------------------------------------------
// THE BUTTON TOKENS. input.h names these nine as globals; managed owns their
// state, and each address is an id into KeyButtons.All (KeyButtons.cs: the
// order of in_main.cpp's declarations). InputTokenIdsTests holds the ids.
//-----------------------------------------------------------------------------
kbutton_t in_speed;
kbutton_t in_jlook;
kbutton_t in_strafe;
kbutton_t in_forward;
kbutton_t in_back;
kbutton_t in_moveleft;
kbutton_t in_moveright;
kbutton_t in_graph;
kbutton_t in_joyspeed;

static const struct { const kbutton_t *pToken; int32_t nId; } s_ButtonTokens[] =
{
	{ &in_speed, 0 }, { &in_jlook, 2 }, { &in_strafe, 3 }, { &in_forward, 5 }, { &in_back, 6 },
	{ &in_moveleft, 7 }, { &in_moveright, 8 }, { &in_graph, 9 }, { &in_joyspeed, 10 },
};

static int32_t ButtonTokenId( const kbutton_t *pKey )
{
	for ( int i = 0; i < (int)ARRAYSIZE( s_ButtonTokens ); i++ )
	{
		if ( s_ButtonTokens[ i ].pToken == pKey )
			return s_ButtonTokens[ i ].nId;
	}
	return -1;
}

// IInput's CAM_* operations, InputCamOp's argument (InputCamOps in
// InputClientBinding.cs).
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

//-----------------------------------------------------------------------------
// THE INPUT MAILBOX: one block both halves read without a crossing
// (IShimInputClient::InputMailbox hands managed its address once).
//
// Two directions, and in each the decision stays managed's:
//
//   - native WRITES the two sample times (IN_SetSampleTime,
//     Joystick_SetSampleTime) with a sequence number, and ClientInput applies
//     a changed one right before it reads the value. A store is a plain
//     assignment that only ClientInput reads, so applying the last one late is
//     the same as applying each one on time;
//   - managed PUBLISHES which calls would change nothing right now (activating
//     an active mouse, deactivating an inactive one, a screen shake equal to
//     the mixer's), and what CAM_IsThirdPerson answers. Native skips a call
//     only when managed said it is a no-op; until the VALID bit is set it
//     makes every call.
//
// Fixed-width words, no padding: InputMailboxLayoutTests holds the same
// offsets and size on the managed side (InputMailbox.cs).
//-----------------------------------------------------------------------------
struct SourceSharpInputMailbox
{
	uint32_t nStructSize;
	uint32_t nKeyboardSampleSeq;
	float    flKeyboardSampleTime;
	uint32_t nJoystickSampleSeq;
	float    flJoystickSampleTime;
	uint32_t nPublished;
	uint32_t nShakeSkip;
	uint32_t nShakeBits;
};
COMPILE_TIME_ASSERT( sizeof( SourceSharpInputMailbox ) == 32 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, nKeyboardSampleSeq ) == 4 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, flKeyboardSampleTime ) == 8 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, nJoystickSampleSeq ) == 12 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, flJoystickSampleTime ) == 16 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, nPublished ) == 20 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, nShakeSkip ) == 24 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpInputMailbox, nShakeBits ) == 28 );

// nPublished's bits (InputPublished in InputMailbox.cs).
enum
{
	INPUT_PUBLISHED_VALID = 1 << 0,
	INPUT_PUBLISHED_ACTIVATE_NOOP = 1 << 1,
	INPUT_PUBLISHED_DEACTIVATE_NOOP = 1 << 2,
	INPUT_PUBLISHED_THIRD_PERSON = 1 << 3,
};

// nShakeSkip (ShakeSkip in InputMailbox.cs).
enum
{
	INPUT_SHAKE_SKIP_NEVER = 0,
	INPUT_SHAKE_SKIP_ALWAYS = 1,
	INPUT_SHAKE_SKIP_WHEN_EQUAL = 2,
};

//-----------------------------------------------------------------------------
class CSourceSharpInput : public ::IInput
{
public:
	CSourceSharpInput() : m_pCommands( NULL ), m_pVerifiedCommands( NULL ), m_pCameraThirdData( NULL )
	{
		V_memset( &m_Mailbox, 0, sizeof( m_Mailbox ) );
		m_Mailbox.nStructSize = sizeof( m_Mailbox );
	}

	SourceSharpInputMailbox *Mailbox() { return &m_Mailbox; }

	// Whether managed published BIT (and has published at all).
	bool Published( uint32_t nBit ) const
	{
		return ( m_Mailbox.nPublished & INPUT_PUBLISHED_VALID ) && ( m_Mailbox.nPublished & nBit );
	}

	// Whether managed said a screen shake of SHAKE would change nothing.
	bool ShakeIsNoop( float shake ) const
	{
		if ( m_Mailbox.nShakeSkip == INPUT_SHAKE_SKIP_ALWAYS )
			return true;
		if ( m_Mailbox.nShakeSkip != INPUT_SHAKE_SKIP_WHEN_EQUAL )
			return false;
		uint32_t nBits;
		V_memcpy( &nBits, &shake, sizeof( nBits ) );
		return nBits == m_Mailbox.nShakeBits;
	}

	virtual void Init_All( void ) OVERRIDE
	{
		Assert( !m_pCommands );
		m_pCommands = new CUserCmd[ MULTIPLAYER_BACKUP ];
		m_pVerifiedCommands = new CVerifiedUserCmd[ MULTIPLAYER_BACKUP ];

		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputInit( ss::CommandLineHas( "-nomouse" ) ? 1 : 0 );
	}

	virtual void Shutdown_All( void ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputShutdown();

		delete[] m_pCommands;
		m_pCommands = NULL;
		delete[] m_pVerifiedCommands;
		m_pVerifiedCommands = NULL;
	}

	virtual int GetButtonBits( int bResetState ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		return p ? p->InputGetButtonBits( bResetState ) : 0;
	}

	virtual void CreateMove( int sequence_number, float input_sample_frametime, bool active ) OVERRIDE
	{
		CUserCmd *cmd = &m_pCommands[ sequence_number % MULTIPLAYER_BACKUP ];
		CVerifiedUserCmd *pVerified = &m_pVerifiedCommands[ sequence_number % MULTIPLAYER_BACKUP ];

		cmd->Reset();
		cmd->command_number = sequence_number;
		cmd->tick_count = gpGlobals->tickcount;

		if ( IManagedInputClient *p = ManagedInputClient() )
		{
			SourceSharpDecodedUserCmd flat;
			V_memset( &flat, 0, sizeof( flat ) );
			flat.cmd.struct_size = sizeof( flat.cmd );
			flat.cmd.command_number = cmd->command_number;
			flat.cmd.tick_count = cmd->tick_count;

			p->InputCreateMove( sequence_number, input_sample_frametime, active ? 1 : 0, &flat );
			FromFlat( flat, cmd );
		}

		pVerified->m_cmd = *cmd;
		pVerified->m_crc = cmd->GetChecksum();
	}

	virtual void ExtraMouseSample( float frametime, bool active ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputExtraMouseSample( frametime, active ? 1 : 0 );
	}

	// THE CODEC, over this file's ring: what the engine asks to put on the
	// wire. A slot the ring has moved past answers the null command; a slot
	// whose checksum moved since CreateMove answers the copy CreateMove
	// verified (stock in_main.cpp's rules). The bits are managed's
	// (UserCmdCodec, plan_client_novalve C9): each command crosses once, the
	// encoded bits come back as one BitBlock, and native copies it into the
	// engine's buffer whole (ss_bitblock.h).
	virtual bool WriteUsercmdDeltaToBuffer( bf_write *buf, int from, int to, bool isnewcommand ) OVERRIDE
	{
		Assert( m_pCommands );

		CUserCmd zero;
		const CUserCmd *pFrom = ( from == -1 ) ? &zero : Verified( from, zero );
		const CUserCmd *pTo = Verified( to, zero );

		const int nBits = Encode( buf, pTo, pFrom );
		if ( nBits >= 0 && !buf->IsOverflowed() )
			return true;

		Msg( "WARNING! User command buffer overflow(%i %i), last cmd was %i bits long\n", from, to, nBits );
		return false;
	}

	virtual void EncodeUserCmdToBuffer( bf_write &buf, int sequence_number ) OVERRIDE
	{
		CUserCmd zero;
		const CUserCmd *pCmd = GetUserCmd( sequence_number );
		Encode( &buf, pCmd ? pCmd : &zero, &zero );
	}

	// A demo's command (CDemoPlayer): the reader's remaining bits cross as one
	// BitBlock, managed decodes one command against the null one, and the
	// engine's reader moves past exactly the bits it used.
	virtual void DecodeUserCmdFromBuffer( bf_read &buf, int sequence_number ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		uint8_t block[ ss::kBitBlockHeaderBytes + kMaxEncodedBytes ];
		if ( !p || ss::BitBlockFromEngine( buf, block, sizeof( block ) ) < 0 )
		{
			buf.SetOverflowFlag();
			return;
		}

		SourceSharpDecodedUserCmd flat;
		V_memset( &flat, 0, sizeof( flat ) );
		const int nUsed = p->InputDecodeUserCmd( block, sizeof( block ), &flat );
		if ( nUsed < 0 )
		{
			buf.m_iCurBit = buf.m_nDataBits;
			buf.SetOverflowFlag();
			return;
		}
		buf.m_iCurBit += nUsed;

		CUserCmd *cmd = &m_pCommands[ sequence_number % MULTIPLAYER_BACKUP ];
		cmd->Reset();
		FromFlat( flat, cmd );
		cmd->command_number = flat.cmd.command_number;
		cmd->tick_count = flat.cmd.tick_count;
	}

	virtual CUserCmd *GetUserCmd( int sequence_number ) OVERRIDE
	{
		Assert( m_pCommands );
		CUserCmd *pSlot = &m_pCommands[ sequence_number % MULTIPLAYER_BACKUP ];
		// NULL once a newer command has taken the slot.
		return pSlot->command_number == sequence_number ? pSlot : NULL;
	}

	virtual void MakeWeaponSelection( int nWeaponEntIndex ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputMakeWeaponSelection( nWeaponEntIndex );
	}

	virtual float KeyState( kbutton_t *key ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		int32_t nId = ButtonTokenId( key );
		return ( p && nId >= 0 ) ? p->InputKeyState( nId ) : 0.0f;
	}

	virtual int KeyEvent( int down, ButtonCode_t keynum, const char *pszCurrentBinding ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		int nAnswer = p ? p->InputKeyEvent( down, (int32_t)keynum, pszCurrentBinding ) : -1;
		if ( nAnswer >= 0 )
			return nAnswer;

		// Managed's -1: the client mode's to answer, as stock's last line.
		if ( g_pClientMode )
			return g_pClientMode->KeyInput( down, keynum, pszCurrentBinding );
		return 1;
	}

	// The engine's names (in_graph, in_jlook) as tokens; the state is managed's
	// (IN_IsKeyDown asks it, shell_clientdll.cpp).
	virtual kbutton_t *FindKey( const char *name ) OVERRIDE
	{
		if ( !Q_stricmp( name, "in_graph" ) )
			return &in_graph;
		if ( !Q_stricmp( name, "in_jlook" ) )
			return &in_jlook;
		return NULL;
	}

	virtual void ControllerCommands( void ) OVERRIDE {}

	virtual void Joystick_Advanced( void ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputJoystickAdvanced();
	}

	// The sample times go into the mailbox: ClientInput reads them there.
	virtual void Joystick_SetSampleTime( float frametime ) OVERRIDE
	{
		m_Mailbox.flJoystickSampleTime = frametime;
		m_Mailbox.nJoystickSampleSeq++;
	}

	virtual void IN_SetSampleTime( float frametime ) OVERRIDE
	{
		m_Mailbox.flKeyboardSampleTime = frametime;
		m_Mailbox.nKeyboardSampleSeq++;
	}

	virtual void AccumulateMouse( void ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputAccumulateMouse();
	}

	// The engine asks these every frame its window gains or lacks focus;
	// managed publishes when one would change nothing.
	virtual void ActivateMouse( void ) OVERRIDE
	{
		if ( Published( INPUT_PUBLISHED_ACTIVATE_NOOP ) )
			return;
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputActivateMouse();
	}

	virtual void DeactivateMouse( void ) OVERRIDE
	{
		if ( Published( INPUT_PUBLISHED_DEACTIVATE_NOOP ) )
			return;
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputDeactivateMouse();
	}

	virtual void ClearStates( void ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputClearStates();
	}

	virtual float GetLookSpring( void ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		return p ? p->InputGetLookSpring() : 0.0f;
	}

	virtual void GetFullscreenMousePos( int *mx, int *my, int *unclampedx, int *unclampedy ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		if ( !p )
			return;
		int32_t pos[ 4 ] = { *mx, *my, unclampedx ? *unclampedx : 0, unclampedy ? *unclampedy : 0 };
		p->InputGetFullscreenMousePos( pos );
		*mx = pos[ 0 ];
		*my = pos[ 1 ];
		if ( unclampedx )
			*unclampedx = pos[ 2 ];
		if ( unclampedy )
			*unclampedy = pos[ 3 ];
	}

	virtual void SetFullscreenMousePos( int mx, int my ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputSetFullscreenMousePos( mx, my );
	}

	virtual void ResetMouse( void ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputResetMouse();
	}

	virtual float GetLastForwardMove( void ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		return p ? p->InputGetLastForwardMove() : 0.0f;
	}

	virtual float Joystick_GetForward( void ) OVERRIDE { return JoystickAxis( 0 ); }
	virtual float Joystick_GetSide( void ) OVERRIDE { return JoystickAxis( 1 ); }
	virtual float Joystick_GetPitch( void ) OVERRIDE { return JoystickAxis( 2 ); }
	virtual float Joystick_GetYaw( void ) OVERRIDE { return JoystickAxis( 3 ); }

	virtual void CAM_Think( void ) OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		if ( !p )
			return;

		// A mod's camera data is held by pointer, as stock held it: its current
		// values go to managed each think.
		if ( m_pCameraThirdData )
		{
			float data[ 4 ] = { m_pCameraThirdData->m_flPitch, m_pCameraThirdData->m_flYaw,
			                    m_pCameraThirdData->m_flDist, m_pCameraThirdData->m_flLag };
			p->InputCamSetCameraThirdData( data, NULL );
		}
		p->InputCamThink();
	}

	// The view asks this every frame: managed publishes the answer.
	virtual int CAM_IsThirdPerson( void ) OVERRIDE
	{
		if ( m_Mailbox.nPublished & INPUT_PUBLISHED_VALID )
			return Published( INPUT_PUBLISHED_THIRD_PERSON ) ? 1 : 0;
		return CamOp( CAMOP_IS_THIRD_PERSON );
	}
	virtual void CAM_ToThirdPerson( void ) OVERRIDE { CamOp( CAMOP_TO_THIRD_PERSON ); }
	virtual void CAM_ToFirstPerson( void ) OVERRIDE { CamOp( CAMOP_TO_FIRST_PERSON ); }
	virtual void CAM_StartMouseMove( void ) OVERRIDE { CamOp( CAMOP_START_MOUSE_MOVE ); }
	virtual void CAM_EndMouseMove( void ) OVERRIDE { CamOp( CAMOP_END_MOUSE_MOVE ); }
	virtual void CAM_StartDistance( void ) OVERRIDE { CamOp( CAMOP_START_DISTANCE ); }
	virtual void CAM_EndDistance( void ) OVERRIDE { CamOp( CAMOP_END_DISTANCE ); }
	virtual int CAM_InterceptingMouse( void ) OVERRIDE { return CamOp( CAMOP_INTERCEPTING_MOUSE ); }
	virtual void CAM_ToOrthographic() OVERRIDE { CamOp( CAMOP_TO_ORTHOGRAPHIC ); }
	virtual bool CAM_IsOrthographic() const OVERRIDE { return CamOp( CAMOP_IS_ORTHOGRAPHIC ) != 0; }

	virtual void CAM_OrthographicSize( float &w, float &h ) const OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		float size[ 2 ] = { 0.0f, 0.0f };
		if ( p )
			p->InputCamOrthographicSize( size );
		w = size[ 0 ];
		h = size[ 1 ];
	}

	virtual void SetPreferredGameActionSet( GameActionSet_t action_set ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputSetPreferredGameActionSet( (int32_t)action_set );
	}

	virtual GameActionSet_t GetPreferredGameActionSet() OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		return p ? (GameActionSet_t)p->InputGetPreferredGameActionSet() : GAME_ACTION_SET_MENUCONTROLS;
	}

	virtual void SetGameActionSetFlags( GameActionSetFlags_t action_set_flags ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputSetGameActionSetFlags( (int32_t)action_set_flags );
	}

	virtual void LevelInit( void ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputLevelInit();
	}

	virtual void ClearInputButton( int bits ) OVERRIDE
	{
		if ( IManagedInputClient *p = ManagedInputClient() )
			p->InputClearInputButton( bits );
	}

	virtual void CAM_SetCameraThirdData( CameraThirdData_t *pCameraData, const QAngle &vecCameraOffset ) OVERRIDE
	{
		m_pCameraThirdData = pCameraData;
		IManagedInputClient *p = ManagedInputClient();
		if ( !p )
			return;
		float offset[ 3 ] = { vecCameraOffset[ 0 ], vecCameraOffset[ 1 ], vecCameraOffset[ 2 ] };
		if ( pCameraData )
		{
			float data[ 4 ] = { pCameraData->m_flPitch, pCameraData->m_flYaw, pCameraData->m_flDist, pCameraData->m_flLag };
			p->InputCamSetCameraThirdData( data, offset );
		}
		else
		{
			p->InputCamSetCameraThirdData( NULL, offset );
		}
	}

	virtual void CAM_CameraThirdThink( void ) OVERRIDE { CamOp( CAMOP_CAMERA_THIRD_THINK ); }

	virtual bool EnableJoystickMode() OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		return p && p->InputEnableJoystickMode() != 0;
	}

	virtual bool IsSteamControllerActive() OVERRIDE
	{
		IManagedInputClient *p = ManagedInputClient();
		return p && p->InputIsSteamControllerActive() != 0;
	}

private:
	class CVerifiedUserCmd
	{
	public:
		CUserCmd m_cmd;
		CRC32_t m_crc;
	};

	// The most one encoded command takes (about 45 bytes: every field present).
	enum { kMaxEncodedBytes = 64 };

	// The fields a managed command carries, onto a CUserCmd (its number and
	// tick are the caller's).
	static void FromFlat( const SourceSharpDecodedUserCmd &flat, CUserCmd *cmd )
	{
		cmd->viewangles[ 0 ] = flat.cmd.viewangles[ 0 ];
		cmd->viewangles[ 1 ] = flat.cmd.viewangles[ 1 ];
		cmd->viewangles[ 2 ] = flat.cmd.viewangles[ 2 ];
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

	// A CUserCmd as the managed codec reads it: every wire field, as stored.
	static void ToFlat( const CUserCmd &cmd, SourceSharpDecodedUserCmd *flat )
	{
		V_memset( flat, 0, sizeof( *flat ) );
		flat->cmd.struct_size = sizeof( flat->cmd );
		flat->cmd.command_number = cmd.command_number;
		flat->cmd.tick_count = cmd.tick_count;
		flat->cmd.viewangles[ 0 ] = cmd.viewangles[ 0 ];
		flat->cmd.viewangles[ 1 ] = cmd.viewangles[ 1 ];
		flat->cmd.viewangles[ 2 ] = cmd.viewangles[ 2 ];
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

	// One command into the engine's buffer as a delta against another: one
	// crossing (managed encodes into a BitBlock), one copy. Answers the bits,
	// or -1 when managed is down or the bits did not fit (the buffer's
	// overflow flag is then set, as WriteUsercmd's writes would have).
	static int Encode( bf_write *buf, const CUserCmd *pTo, const CUserCmd *pFrom )
	{
		IManagedInputClient *p = ManagedInputClient();
		if ( !p )
		{
			buf->SetOverflowFlag();
			return -1;
		}
		SourceSharpDecodedUserCmd to, from;
		ToFlat( *pTo, &to );
		ToFlat( *pFrom, &from );
		uint8_t block[ ss::kBitBlockHeaderBytes + kMaxEncodedBytes ];
		const int nBits = p->InputEncodeUserCmd( &to, &from, block, sizeof( block ) );
		if ( !ss::BitBlockToEngine( buf, block, sizeof( block ) ) )
			return -1;
		return nBits;
	}

	// The command at a sequence number as the wire should carry it: the
	// fallback when the ring has moved past it, the verified copy when its
	// checksum no longer matches CreateMove's.
	CUserCmd *Verified( int sequence_number, CUserCmd &fallback )
	{
		CUserCmd *pCmd = GetUserCmd( sequence_number );
		if ( !pCmd )
			return &fallback;
		const CVerifiedUserCmd &verified = m_pVerifiedCommands[ sequence_number % MULTIPLAYER_BACKUP ];
		if ( pCmd->GetChecksum() != verified.m_crc )
			*pCmd = verified.m_cmd;
		return pCmd;
	}

	float JoystickAxis( int nAxis )
	{
		IManagedInputClient *p = ManagedInputClient();
		float axes[ 4 ] = { 0.0f, 0.0f, 0.0f, 0.0f };
		if ( p )
			p->InputJoystickAxes( axes );
		return axes[ nAxis ];
	}

	int CamOp( int nOp ) const
	{
		IManagedInputClient *p = ManagedInputClient();
		return p ? p->InputCamOp( nOp ) : 0;
	}

	CUserCmd *m_pCommands;
	CVerifiedUserCmd *m_pVerifiedCommands;
	CameraThirdData_t *m_pCameraThirdData;
	SourceSharpInputMailbox m_Mailbox;
};

static CSourceSharpInput g_Input;
::IInput *input = &g_Input;

//-----------------------------------------------------------------------------
// g_ThirdPersonManager: clientmode_shared.cpp resets it at level shutdown.
// The manager is managed's (ThirdPersonManager.cs); this object is the name.
//-----------------------------------------------------------------------------
CThirdPersonManager::CThirdPersonManager( void )
{
}

void CThirdPersonManager::Init( void )
{
	if ( IManagedInputClient *p = ManagedInputClient() )
		p->InputThirdPersonInit();
}

CThirdPersonManager g_ThirdPersonManager;

//-----------------------------------------------------------------------------
// c_rumble.h's names (lane c6-input): the mixer is ClientRumble's, stepped by
// the managed client frame, and the Rumble message reaches it in managed code
// (ClientMessageEffects). These are the remaining native callers' way in.
//-----------------------------------------------------------------------------

// ClientModeShared::Update's, which this build does not call: the managed
// client frame steps the mixer (ClientInputService.RumbleFrame) instead, so
// this stays empty rather than step it twice.
void UpdateRumbleEffects()
{
}

// view_effects.cpp's, once a frame with the shake's amplitude.
void UpdateScreenShakeRumble( float shake, float balance )
{
	if ( g_Input.ShakeIsNoop( shake ) )
		return;
	if ( IManagedInputClient *p = ManagedInputClient() )
		p->InputRumbleScreenShake( shake, balance );
}

// The client DLL's level shutdown.
void StopAllRumbleEffects( void )
{
	if ( IManagedInputClient *p = ManagedInputClient() )
		p->InputRumbleStopAll();
}

//-----------------------------------------------------------------------------
// IShimInputClient's bodies.
//-----------------------------------------------------------------------------
void SourceSharp_InputCursorState( int32_t *pVisible, int32_t *pLocked )
{
	vgui::ISurface *pSurface = vgui::surface();
	if ( pVisible )
		*pVisible = pSurface && pSurface->IsCursorVisible() ? 1 : 0;
	if ( pLocked )
		*pLocked = pSurface && pSurface->IsCursorLocked() ? 1 : 0;
}

void SourceSharp_InputGetCursorPos( int32_t *pX, int32_t *pY )
{
	int x = 0, y = 0;
	GetVGUICursorPos( x, y );
	if ( pX )
		*pX = x;
	if ( pY )
		*pY = y;
}

void SourceSharp_InputSetCursorPos( int32_t x, int32_t y )
{
	SetVGUICursorPos( x, y );
}

void SourceSharp_InputSetCursorArrow( void )
{
	if ( vgui::surface() )
		vgui::surface()->SetCursor( vgui::dc_arrow );
}

void SourceSharp_InputVguiScreenSize( int32_t *pWide, int32_t *pTall )
{
	int w = 0, h = 0;
	if ( vgui::surface() )
		vgui::surface()->GetScreenSize( w, h );
	if ( pWide )
		*pWide = w;
	if ( pTall )
		*pTall = h;
}

void SourceSharp_InputHudFacts( float *pSensitivity, int32_t *pKeyBits, float *pFovAdjust )
{
	if ( pSensitivity )
		*pSensitivity = gHUD.GetSensitivity();
	if ( pKeyBits )
		*pKeyBits = gHUD.m_iKeyBits;
	if ( pFovAdjust )
		*pFovAdjust = gHUD.GetFOVSensitivityAdjust();
}

void SourceSharp_InputStopPitchDrift( void )
{
	if ( view )
		view->StopPitchDrift();
}

void SourceSharp_InputSetLocalViewAngles( const float *pAngles )
{
	if ( !prediction || !pAngles )
		return;
	QAngle angles( pAngles[ 0 ], pAngles[ 1 ], pAngles[ 2 ] );
	prediction->SetLocalViewAngles( angles );
}

void SourceSharp_InputViewerCameraModeChanged( void )
{
	SourceSharp_ViewerCameraModeChanged();
}

int32_t SourceSharp_InputViewerAimIsAssisted( void )
{
	return SourceSharp_ViewerAimIsAssisted() ? 1 : 0;
}

void *SourceSharp_InputMailbox( void )
{
	return g_Input.Mailbox();
}

// +score / +showscores (IN_ScoreDown / IN_ScoreUp's viewport half).
void SourceSharp_InputShowScoreboard( int32_t bShow )
{
	if ( !gViewPortInterface )
		return;

	gViewPortInterface->ShowPanel( PANEL_SCOREBOARD, bShow != 0 );
	if ( !bShow )
		GetClientVoiceMgr()->StopSquelchMode();
}
