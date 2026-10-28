// What a scripted entity kind installs its standalone class with. Engine-free so it
// can be unit tested; the values are const.h's, pinned by COMPILE_TIME_ASSERTs in
// shell_scripted.cpp.
#pragma once

// Matches ScriptedEntityKind in C#. The numbers cross the ABI, so 4 stays reserved.
enum SourceSharpScriptedKind
{
	SOURCESHARP_SCRIPTED_LOGICAL = 0,
	SOURCESHARP_SCRIPTED_ITEM = 1,
	SOURCESHARP_SCRIPTED_BRUSH = 2,
	SOURCESHARP_SCRIPTED_PROP = 3,

	// An item that flies rather than waiting to be walked into.
	SOURCESHARP_SCRIPTED_PROJECTILE = 5,

	// Drawn by the client, touched by nothing: no model, no collision.
	SOURCESHARP_SCRIPTED_EFFECT = 6,

	// A connecting client's avatar, created by the engine via ClientPutInServer.
	SOURCESHARP_SCRIPTED_PLAYER = 7,
};

struct SourceSharpScriptedKindParams_t
{
	bool bServerOnly; // no edict, never sent to a client
	int nSolidType; // SolidType_t
	int nSolidFlags; // FSOLID_*
	bool bTouchEveryTick; // every frame of overlap, rather than the edges only
	bool bPlayer; // instantiated as CStandalonePlayer

	// True for kinds with no native shell; no factory is installed, so nothing
	// native can construct one.
	bool bShellLess;
};

enum
{
	SOURCESHARP_KIND_SOLID_NONE = 0,
	SOURCESHARP_KIND_SOLID_BSP = 1,
	SOURCESHARP_KIND_SOLID_BBOX = 2,
	SOURCESHARP_KIND_SOLID_VPHYSICS = 6,

	SOURCESHARP_KIND_FSOLID_NOT_SOLID = 0x0004,
	SOURCESHARP_KIND_FSOLID_TRIGGER = 0x0008,
	SOURCESHARP_KIND_FSOLID_NOT_STANDABLE = 0x0010,
};

// Total: anything unrecognised gets LOGICAL's answer.
inline SourceSharpScriptedKindParams_t SourceSharp_ScriptedKindParams( int nKind )
{
	SourceSharpScriptedKindParams_t p;

	p.bPlayer = false;
	p.bShellLess = false;

	switch ( nKind )
	{
		case SOURCESHARP_SCRIPTED_ITEM:
			// NOT_SOLID with TRIGGER puts it in the trigger list but not the solid one.
			p.bServerOnly = false;
			p.nSolidType = SOURCESHARP_KIND_SOLID_BBOX;
			p.nSolidFlags = SOURCESHARP_KIND_FSOLID_TRIGGER | SOURCESHARP_KIND_FSOLID_NOT_SOLID;
			p.bTouchEveryTick = true;
			return p;

		case SOURCESHARP_SCRIPTED_BRUSH:
			// Starts solid; volumes change that from C# at spawn.
			p.bServerOnly = false;
			p.nSolidType = SOURCESHARP_KIND_SOLID_BSP;
			p.nSolidFlags = 0;
			p.bTouchEveryTick = false;
			return p;

		case SOURCESHARP_SCRIPTED_PROP:
			// Not simulated until a prop_physics asks for it in OnSpawn.
			p.bServerOnly = false;
			p.nSolidType = SOURCESHARP_KIND_SOLID_VPHYSICS;
			p.nSolidFlags = 0;
			p.bTouchEveryTick = false;
			return p;

		case SOURCESHARP_SCRIPTED_PROJECTILE:
			// NOT_STANDABLE so a projectile passing under a player cannot lift them.
			p.bServerOnly = false;
			p.nSolidType = SOURCESHARP_KIND_SOLID_BBOX;
			p.nSolidFlags = SOURCESHARP_KIND_FSOLID_NOT_STANDABLE;
			p.bTouchEveryTick = false;
			return p;

		case SOURCESHARP_SCRIPTED_EFFECT:
			p.bServerOnly = false;
			p.nSolidType = SOURCESHARP_KIND_SOLID_NONE;
			p.nSolidFlags = SOURCESHARP_KIND_FSOLID_NOT_SOLID;
			p.bTouchEveryTick = false;
			return p;

		case SOURCESHARP_SCRIPTED_PLAYER:
			p.bServerOnly = false;
			p.nSolidType = SOURCESHARP_KIND_SOLID_BBOX;
			p.nSolidFlags = 0;
			p.bTouchEveryTick = false;
			p.bPlayer = true;
			return p;

		case SOURCESHARP_SCRIPTED_LOGICAL:
		default:
			p.bServerOnly = true;
			p.nSolidType = SOURCESHARP_KIND_SOLID_NONE;
			p.nSolidFlags = SOURCESHARP_KIND_FSOLID_NOT_SOLID;
			p.bTouchEveryTick = false;
			p.bShellLess = true;
			return p;
	}
}
