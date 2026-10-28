//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

struct client_textmessage_t
{
	int		effect;
	byte	r1, g1, b1, a1;		// 2 colors for effects
	byte	r2, g2, b2, a2;
	float	x;
	float	y;
	float	fadein;
	float	fadeout;
	float	holdtime;
	float	fxtime;
	alignas( 8 ) unsigned char unused_pVGuiSchemeFontName[ 16 ];
	const char *pMessage;
	alignas( 8 ) unsigned char unused_bRoundedRectBackdropBox[ 16 ];
	char const *pClearMessage; // message to clear
};
