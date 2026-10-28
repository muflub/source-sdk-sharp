//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

struct kbutton_t
{
	// key nums holding it down
	int down[2];
	// low bit is down state
	int state;
};
