//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
//
// THE CLIENT'S SERVER-BOUNDED CONVARS, CARRIED OUT (plan_client_novalve C8,
// lane c6-input; replaces game/client/cdll_bounded_cvars.cpp).
//
// The objects stay native because the engine calls them through their vtable:
// the engine's client-state interp query finds cl_interp_ratio
// by name, casts it to ConVar_ServerBounded and calls GetFloat. What each
// answers is DECIDED IN C#: BoundedConVars watches every input (the client's
// three, cl_updaterate, the server's sv_client_* and sv_*updaterate bounds),
// recomputes stock's rules on any change and pushes the results through
// IShimConVarsClient::ConVarsClientSetValue. GetFloat returns the stored value
// -- no crossing per read -- and the client's own value until managed has
// pushed one (stock's answer whenever the server's bounds are absent).
//
// The names, defaults, flags and help texts are stock's, held to the pristine
// file by ClientBoundedConVarOracleTests.
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "tier1/convar_serverbounded.h"
#include "tier0/platform.h"
#include "tier1/convar.h"
#include "tier1/iconvar.h"
#include "shell_convars_client.h"


namespace
{
class CManagedBoundedConVar : public ConVar_ServerBounded
{
public:
	CManagedBoundedConVar( const char *pName, const char *pDefault, int nFlags, const char *pHelp )
		: ConVar_ServerBounded( pName, pDefault, nFlags, pHelp ) {}
	CManagedBoundedConVar( const char *pName, const char *pDefault, int nFlags, const char *pHelp,
	                       bool bMin, float flMin, bool bMax, float flMax )
		: ConVar_ServerBounded( pName, pDefault, nFlags, pHelp, bMin, flMin, bMax, flMax ) {}

	virtual float GetFloat() const OVERRIDE
	{
		float flDecided;
		return SourceSharp_ClientDecidedValue( GetName(), &flDecided ) ? flDecided : GetBaseFloatValue();
	}
};

CManagedBoundedConVar s_cl_predict( "cl_predict", "1.0", FCVAR_USERINFO | FCVAR_NOT_CONNECTED,
	"Perform client side prediction." );
CManagedBoundedConVar s_cl_interp_ratio( "cl_interp_ratio", "2.0", FCVAR_USERINFO | FCVAR_NOT_CONNECTED | FCVAR_ARCHIVE,
	"Sets the interpolation amount (final amount is cl_interp_ratio / cl_updaterate)." );
CManagedBoundedConVar s_cl_interp( "cl_interp", "0.1", FCVAR_USERINFO | FCVAR_NOT_CONNECTED | FCVAR_ARCHIVE,
	"Sets the interpolation amount (bounded on low side by server interp ratio settings).", true, 0.0f, true, 0.5f );
} // namespace

ConVar_ServerBounded *cl_predict = &s_cl_predict;
ConVar_ServerBounded *cl_interp_ratio = &s_cl_interp_ratio;
ConVar_ServerBounded *cl_interp = &s_cl_interp;

// Decided in C# (BoundedConVars.InterpAmountName); stock's own fallback, 0.1,
// until managed has pushed one.
float GetClientInterpAmount()
{
	float flDecided;
	return SourceSharp_ClientDecidedValue( "GetClientInterpAmount", &flDecided ) ? flDecided : 0.1f;
}
