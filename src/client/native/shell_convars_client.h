//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The managed client's convar watch and decided values.
#pragma once

#include "ishim_areas_client.h"
#include "managed_convar_ref.h"
#include "tier1/convar.h"
#include "tier1/convar_serverbounded.h"

class CShimConsole;

class CConVarsClient;

// A bounded client convar whose effective value managed decides.
class CManagedBoundedConVar : public ConVar_ServerBounded
{
public:
	CManagedBoundedConVar(CConVarsClient& owner, const char* pName, const char* pDefault, int nFlags, const char* pHelp)
		: ConVar_ServerBounded(pName, pDefault, nFlags, pHelp)
		, m_Owner(owner)
	{
	}

	CManagedBoundedConVar(CConVarsClient& owner, const char* pName, const char* pDefault, int nFlags, const char* pHelp,
						  bool bMin, float flMin, bool bMax, float flMax)
		: ConVar_ServerBounded(pName, pDefault, nFlags, pHelp, bMin, flMin, bMax, flMax)
		, m_Owner(owner)
	{
	}

	float GetFloat() const override;

private:
	CConVarsClient& m_Owner;
};

class CClientEngineInterfaces;

// cl_software_cursor: its change hands vgui the larger software cursor. A change callback has no
// context, so it reaches the interfaces through the convar it is told changed.
class CSoftwareCursorConVar final : public ConVar
{
public:
	explicit CSoftwareCursorConVar(const CClientEngineInterfaces& engines);

private:
	static void Changed(IConVar* pVar, const char* pOldValue, float flOldValue);

	const CClientEngineInterfaces& m_Engines;
};

// IShimConVarsClient: the watch is the console's, which owns the change callback.
class CConVarsClient final : public IShimConVarsClient
{
public:
	CConVarsClient(CShimConsole& console, const CClientEngineInterfaces& engines)
		: m_cl_software_cursor(engines)
		, m_Console(console)
	{
	}

	// The value managed decided under pName; false when it has decided none yet.
	bool DecidedValue(const char* pName, float* pOut) const;

	// Stock's fallback, 0.1, until managed has pushed a value.
	float ClientInterpAmount() const;

	// view.h's ShouldDrawInWireFrameMode.
	bool ShouldDrawInWireFrameMode() const { return m_SvCheats.GetBool() && m_mat_wireframe.GetInt() != 0; }

	// Members of a member of a file-scope object: they register in Init's ConVar_Register,
	// the server and engine match them by name.
	CManagedBoundedConVar m_cl_predict{*this, "cl_predict", "1.0", FCVAR_USERINFO | FCVAR_NOT_CONNECTED,
									   "Perform client side prediction."};
	CManagedBoundedConVar m_cl_interp_ratio{
		*this, "cl_interp_ratio", "2.0", FCVAR_USERINFO | FCVAR_NOT_CONNECTED | FCVAR_ARCHIVE,
		"Sets the interpolation amount (final amount is cl_interp_ratio / cl_updaterate)."};
	CManagedBoundedConVar m_cl_interp{
		*this,
		"cl_interp",
		"0.1",
		FCVAR_USERINFO | FCVAR_NOT_CONNECTED | FCVAR_ARCHIVE,
		"Sets the interpolation amount (bounded on low side by server interp ratio settings).",
		true,
		0.0f,
		true,
		0.5f};

	// FCVAR_USERINFO: the server reads it back by name to decide whether to lag-compensate.
	ConVar m_cl_interpolate{"cl_interpolate", "1.0f", FCVAR_USERINFO | FCVAR_DEVELOPMENTONLY};
	// The engine resolves it by name in its client-convar query, and the server reads it.
	ConVar m_closecaption{"closecaption", "0", FCVAR_ARCHIVE | FCVAR_USERINFO, "Enable close captioning."};
	// Read by name by the post-process plan and the managed vision filter.
	ConVar m_localplayer_visionflags{"localplayer_visionflags", "0", FCVAR_DEVELOPMENTONLY};

	ConVar m_mat_wireframe{"mat_wireframe", "0", FCVAR_CHEAT};
	ConVar m_r_DrawDetailProps{"r_DrawDetailProps", "1", FCVAR_NONE, "0=Off, 1=Normal, 2=Wireframe"};
	ConVar m_cl_drawshadowtexture{"cl_drawshadowtexture", "0", FCVAR_CHEAT};
	ConVar m_r_updaterefracttexture{"r_updaterefracttexture", "1", FCVAR_CHEAT};
	ConVar m_r_depthoverlay{
		"r_depthoverlay", "0", FCVAR_CHEAT,
		"Replaces opaque objects with their grayscaled depth values. r_showz_power scales the output."};
	ConVar m_r_visualizetraces{"r_visualizetraces", "0", FCVAR_CHEAT};
	CSoftwareCursorConVar m_cl_software_cursor;

	// The frame the refract texture was last updated in (view_scene.h).
	int m_nRefractUpdateFrame = 0;

	int32_t ConVarsClientWatch(const char* pName) override;
	int32_t ConVarsClientSetValue(const char* pName, float flValue) override;

private:
	struct DecidedValue_t
	{
		char szName[64];
		float flValue;
	};

	DecidedValue_t m_Decided[8] = {};
	int m_nDecided = 0;
	CManagedConVarRef m_SvCheats{"sv_cheats"};
	CShimConsole& m_Console;
};
