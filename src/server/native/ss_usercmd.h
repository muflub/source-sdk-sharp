//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — one player command, as server.so holds it
//
// plan_server_novalve.md P5. game/shared/usercmd.h's CUserCmd is, on the
// server, only a record our own code fills and reads: the engine never sees
// it (commands arrive as bits, and P2.2 moved their decoding to C#, which
// hands CStandalonePlayer::QueueDecodedUsercmds the fields below). Per the
// plan's naming rule it is server.so's own type, ss::CUserCmd, on public/
// headers alone. The client keeps Valve's CUserCmd, which it predicts with.
//
// What is not carried over: the virtual destructor (nothing deletes one
// through a base), the CRC (the server checks none), the bit codec
// (ReadUsercmd/WriteUsercmd; C# decodes), and entitygroundcontact (HL2_DLL
// only, never defined here).
//=============================================================================//
#ifndef SOURCESHARP_SS_USERCMD_H
#define SOURCESHARP_SS_USERCMD_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"   // QAngle

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

	int command_number;        // the client's sequence number for it
	int tick_count;            // the tick it was created for
	QAngle viewangles;         // where the player looks
	float forwardmove;         // the three move axes
	float sidemove;
	float upmove;
	int buttons;               // IN_* bits
	unsigned char impulse;
	int weaponselect;          // entity index of a weapon to switch to, 0 for none
	int weaponsubtype;
	int random_seed;           // the client's shared random seed for this command
	int server_random_seed;    // the server's own, never sent to the client
	short mousedx;             // raw mouse deltas
	short mousedy;
	bool hasbeenpredicted;     // client side only; carried for the shared readers
};

} // namespace ss

#endif // SOURCESHARP_SS_USERCMD_H
