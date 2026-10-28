//========= Copyright Valve Corporation, All rights reserved. ============//
// worldsize.h  -- extent of world and resolution/size of coordinate messages used in engine

#pragma once

// These definitions must match the coordinate message sizes in coordsize.h

// Following values should be +16384, -16384, +15/16, -15/16
// NOTE THAT IF THIS GOES ANY BIGGER THEN DISK NODES/LEAVES CANNOT USE SHORTS TO STORE THE BOUNDS
#define MAX_COORD_INTEGER			(16384)

#define MAX_COORD_FLOAT				(16384.0f)

// Width of the coord system, which is TOO BIG to send as a client/server coordinate value
#define COORD_EXTENT				(2*MAX_COORD_INTEGER)

// Maximum traceable distance ( assumes cubic world and trace from one corner to opposite )
// COORD_EXTENT * sqrt(3)
#define MAX_TRACE_LENGTH			( 1.732050807569 * COORD_EXTENT )		

// This value is the LONGEST possible range (limited by max valid coordinate number, not 2x)
