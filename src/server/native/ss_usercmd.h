//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// One player command as the server holds it, filled from C#'s decoding; the
// engine never sees it. The client keeps Valve's CUserCmd.

#pragma once

namespace ss
{

struct CUserCmd
{
	CUserCmd() { Reset(); }

	// Every field to its empty value, as a fresh command starts.
	void Reset()
	{
		command_number = 0;
		tick_count = 0;
		viewangles.Init();
		forwardmove = sidemove = upmove = 0.0f;
		buttons = 0;
		impulse = 0;
		weaponselect = weaponsubtype = 0;
		random_seed = server_random_seed = 0;
		mousedx = mousedy = 0;
		hasbeenpredicted = false;
	}

	int command_number; // the client's sequence number for it
	int tick_count; // the tick it was created for
	QAngle viewangles; // where the player looks
	float forwardmove; // the three move axes
	float sidemove;
	float upmove;
	int buttons; // IN_* bits
	unsigned char impulse;
	int weaponselect; // entity index of a weapon to switch to, 0 for none
	int weaponsubtype;
	int random_seed; // the client's shared random seed for this command
	int server_random_seed; // the server's own, never sent to the client
	short mousedx; // raw mouse deltas
	short mousedy;
	bool hasbeenpredicted; // client side only; carried for the shared readers
};

} // namespace ss
