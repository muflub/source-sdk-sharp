//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
//
// The client's server-bounded convars. The engine finds cl_interp_ratio by name and calls
// GetFloat through the vtable; managed computes the bounded values and pushes them, and
// until it has, GetFloat answers the client's own value.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "tier1/convar_serverbounded.h"

#include "shell_convars_client.h"

namespace
{
class CManagedBoundedConVar : public ConVar_ServerBounded
{
public:
	CManagedBoundedConVar(const char* pName, const char* pDefault, int nFlags, const char* pHelp)
		: ConVar_ServerBounded(pName, pDefault, nFlags, pHelp)
	{
	}

	CManagedBoundedConVar(const char* pName, const char* pDefault, int nFlags, const char* pHelp, bool bMin,
						  float flMin, bool bMax, float flMax)
		: ConVar_ServerBounded(pName, pDefault, nFlags, pHelp, bMin, flMin, bMax, flMax)
	{
	}

	virtual float GetFloat() const OVERRIDE
	{
		float flDecided;
		return SourceSharp_ClientDecidedValue(GetName(), &flDecided) ? flDecided : GetBaseFloatValue();
	}
};

CManagedBoundedConVar s_cl_predict("cl_predict", "1.0", FCVAR_USERINFO | FCVAR_NOT_CONNECTED,
								   "Perform client side prediction.");
CManagedBoundedConVar s_cl_interp_ratio(
	"cl_interp_ratio", "2.0", FCVAR_USERINFO | FCVAR_NOT_CONNECTED | FCVAR_ARCHIVE,
	"Sets the interpolation amount (final amount is cl_interp_ratio / cl_updaterate).");
CManagedBoundedConVar s_cl_interp(
	"cl_interp", "0.1", FCVAR_USERINFO | FCVAR_NOT_CONNECTED | FCVAR_ARCHIVE,
	"Sets the interpolation amount (bounded on low side by server interp ratio settings).", true, 0.0f, true, 0.5f);
} // namespace

ConVar_ServerBounded* cl_predict = &s_cl_predict;
ConVar_ServerBounded* cl_interp_ratio = &s_cl_interp_ratio;
ConVar_ServerBounded* cl_interp = &s_cl_interp;

// Stock's fallback, 0.1, until managed has pushed a value.
float GetClientInterpAmount()
{
	float flDecided;
	return SourceSharp_ClientDecidedValue("GetClientInterpAmount", &flDecided) ? flDecided : 0.1f;
}
