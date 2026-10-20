//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- kClientSystems[] rows whose system is managed's ====//
// SS_REASON c  static-init registration
//
// A row of shell_clientsystems.cpp's table keeps its place in the walk (the
// walk's order is the table's, and only the table decides it), and this
// adapter hands each of the row's stages to managed's system of the same name
// (IManagedClientSystemsClient::Stage, SourceSharp.Client.ClientSystems). The
// stage numbers are ClientSystemStage's; ManagedClientSystemsAbiTests holds the
// two lists equal.
//
// Its one if on an answer is Init's: managed's 0 refuses the client's start-up,
// as a native row's false does. With no managed load, or a row managed does not
// know, there is nothing to ask and nothing refuses (the second is said once).
//
// The rows: CViewportClientSystem (vgui_game_viewport.cpp, C7),
// CPixelVisibilitySystem (c_pixel_visibility.cpp, C7), CGlowOverlaySystem
// (glow_overlay.cpp, C7).
//=============================================================================//

#include "cbase.h"
#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// ClientSystemStage (C#), same names and numbers.
enum ClientSystemStage_t
{
	SS_CLIENT_SYSTEM_STAGE_Init = 0,
	SS_CLIENT_SYSTEM_STAGE_PostInit = 1,
	SS_CLIENT_SYSTEM_STAGE_Shutdown = 2,
	SS_CLIENT_SYSTEM_STAGE_LevelInitPreEntity = 3,
	SS_CLIENT_SYSTEM_STAGE_LevelInitPostEntity = 4,
	SS_CLIENT_SYSTEM_STAGE_LevelShutdownPreEntity = 5,
	SS_CLIENT_SYSTEM_STAGE_LevelShutdownPostEntity = 6,
};

namespace
{

class CManagedClientSystemRow : public CClientSystem
{
public:
	explicit CManagedClientSystemRow( const char *pszRow ) : CClientSystem( pszRow ), m_pszRow( pszRow ), m_bWarned( false ) {}

	bool Init() override { return Stage( SS_CLIENT_SYSTEM_STAGE_Init ) != 0; }
	void PostInit() override { Stage( SS_CLIENT_SYSTEM_STAGE_PostInit ); }
	void Shutdown() override { Stage( SS_CLIENT_SYSTEM_STAGE_Shutdown ); }
	void LevelInitPreEntity() override { Stage( SS_CLIENT_SYSTEM_STAGE_LevelInitPreEntity ); }
	void LevelInitPostEntity() override { Stage( SS_CLIENT_SYSTEM_STAGE_LevelInitPostEntity ); }
	void LevelShutdownPreEntity() override { Stage( SS_CLIENT_SYSTEM_STAGE_LevelShutdownPreEntity ); }
	void LevelShutdownPostEntity() override { Stage( SS_CLIENT_SYSTEM_STAGE_LevelShutdownPostEntity ); }

private:
	// Managed's answer; -1 when there is no managed load or no system of the name.
	int32_t Stage( int32_t nStage )
	{
		IManagedClientSystemsClient *pManaged = ManagedClientSystemsClient();
		if ( !pManaged )
			return -1;
		const int32_t nAnswer = pManaged->Stage( m_pszRow, nStage );
		if ( nAnswer < 0 && !m_bWarned )
		{
			m_bWarned = true;
			Warning( "[SourceSharp] client system row %s: managed has no system of that name\n", m_pszRow );
		}
		return nAnswer;
	}

	const char *m_pszRow;
	bool m_bWarned;
};

} // namespace

// kClientSystems[]'s "CViewportClientSystem": ViewportClientSystem (C#).
CClientSystem *ClientSystem_ManagedViewport()
{
	static CManagedClientSystemRow s_Row( "CViewportClientSystem" );
	return &s_Row;
}

// kClientSystems[]'s "CGlowOverlaySystem": GlowOverlaySystem (C#).
CClientSystem *ClientSystem_ManagedGlowOverlay()
{
	static CManagedClientSystemRow s_Row( "CGlowOverlaySystem" );
	return &s_Row;
}

// kClientSystems[]'s "CPixelVisibilitySystem": PixelVisibilitySystem (C#).
CClientSystem *ClientSystem_ManagedPixelVisibility()
{
	static CManagedClientSystemRow s_Row( "CPixelVisibilitySystem" );
	return &s_Row;
}
