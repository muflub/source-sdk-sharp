//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: CHud handles the message, calculation, and drawing the HUD
#pragma once

#include "utlvector.h"
#include "utldict.h"
#include "convar.h"
#include <vgui/VGUI.h>
#include <Color.h>
#include <bitbuf.h>

namespace vgui
{
	class IScheme;
}

// basic rectangle struct used for drawing
typedef struct wrect_s
{
	int	left;
	int right;
	int top;
	int bottom;
} wrect_t;

class CHudTexture
{
public:
	CHudTexture();
	virtual ~CHudTexture();

	// causes the font manager to generate the glyph, prevents run time hitches on platforms that have slow font managers
	void Precache();

	char		szShortName[ 64 ];
	char		szTextureFile[ 64 ];

	bool		bRenderUsingFont;
	bool		bPrecached;
	char		cCharacterInFont;
	vgui::HFont hFont;

	// vgui texture Id assigned to this item
	int			textureId;
	// s0, t0, s1, t1
	float		texCoords[ 4 ];

	// Original bounds
	wrect_t		rc;
};

#include "hudtexturehandle.h"

class CHudElement;
class CHudRenderGroup;

// Purpose: Main hud manager
class CHud 
{
public:

public:
								CHud();
								~CHud();

	// Init's called when the HUD's created at DLL load
	void						Init();
	// VidInit's called when the video mode's changed
	void						VidInit();
	// Shutdown's called when the engine's shutting down
	void						Shutdown();
	// LevelInit's called whenever a new level is starting
	void						LevelInit();
	// LevelShutdown's called whenever a level is finishing
	void						LevelShutdown();
	
	void						ResetHUD();

	void						ProcessInput( bool bActive );
	void						UpdateHud( bool bActive );

	// Hud element registration
	void						AddHudElement( CHudElement *pHudElement );
	void						RemoveHudElement( CHudElement *pHudElement );
	// Search list for "name" and return the hud element if it exists
	CHudElement					*FindElement( const char *pName );
	
	bool						IsHidden( int iHudFlags );

	float						GetSensitivity();
	float						GetFOVSensitivityAdjust();

	void						SetScreenShotTime( float flTime ){ m_flScreenShotTime = flTime; }

public:

	int							m_iKeyBits;
	float						m_flMouseSensitivity;
	float						m_flMouseSensitivityFactor;
	float						m_flFOVSensitivityAdjust;

	Color						m_clrNormal;
	Color						m_clrCaution;
	Color						m_clrYellowish;

	CUtlVector< CHudElement * >	m_HudList;

private:

	bool						m_bHudTexturesLoaded;

	// Global list of known icons
	CUtlDict< CHudTexture *, int >		m_Icons;

	CUtlVector< const char * >				m_RenderGroupNames;
	CUtlMap< int, CHudRenderGroup * >		m_RenderGroups;

	float						m_flScreenShotTime; // used to take end-game screenshots
};

extern CHud gHUD;

void GetHudSize( int& w, int &h );
