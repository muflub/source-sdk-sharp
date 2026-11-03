//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
#ifdef CLIENT_DLL
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
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
#endif
#include "shell_gamerules.h"
#ifdef CLIENT_DLL
#include "shell_gamerules_client.h"
#endif
#include "shell_diagnostics.h"
#include "sourcesharp.h"
#ifndef CLIENT_DLL
#include "shell_managed_areas.h"
#endif
#ifdef CLIENT_DLL
#include "ammodef.h"
#include "takedamageinfo.h"
#include "blast_blocking.h"
#include "networkstringtable_clientdll.h"
#else
#include "networkstringtabledefs.h"
#include "edict.h"
#endif

#ifndef CLIENT_DLL
uint32_t SourceSharp_HandleFromEntity(IGameEntity* pEntity);
#endif

#ifndef CLIENT_DLL
#include "shell_server.h"
#include "shell_standalone_class.h"

#include "ss_shareddefs.h"
#include "ss_takedamageinfo.h"
#include "standalone_relays.h"

// shareddefs.h's damage bits by their stock names, for the mask table below.
#define DMG_ACID ss::kDmgAcid
#define DMG_BLAST ss::kDmgBlast
#define DMG_BURN ss::kDmgBurn
#define DMG_CLUB ss::kDmgClub
#define DMG_CRUSH ss::kDmgCrush
#define DMG_DROWN ss::kDmgDrown
#define DMG_DROWNRECOVER ss::kDmgDrownrecover
#define DMG_FALL ss::kDmgFall
#define DMG_NERVEGAS ss::kDmgNervegas
#define DMG_PARALYZE ss::kDmgParalyze
#define DMG_PHYSGUN ss::kDmgPhysgun
#define DMG_PLASMA ss::kDmgPlasma
#define DMG_POISON ss::kDmgPoison
#define DMG_PREVENT_PHYSICS_FORCE ss::kDmgPreventPhysicsForce
#define DMG_RADIATION ss::kDmgRadiation
#define DMG_SHOCK ss::kDmgShock
#define DMG_SLOWBURN ss::kDmgSlowburn
#define DMG_SONIC ss::kDmgSonic
#include "imanaged_areas.h"

// Stock's value.
#define ROBUST_RADIUS_PROBE_DIST 16.0f
#endif

// The hulls before managed registers its own: stock's values, so reads during level
// init see a real hull.
#ifdef CLIENT_DLL
CGameRulesClient::CGameRulesClient()
	: m_ViewVectors(Vector(0, 0, 64), // VEC_VIEW
					Vector(-16, -16, 0), // VEC_HULL_MIN
					Vector(16, 16, 72), // VEC_HULL_MAX
					Vector(-16, -16, 0), // VEC_DUCK_HULL_MIN
					Vector(16, 16, 36), // VEC_DUCK_HULL_MAX
					Vector(0, 0, 28), // VEC_DUCK_VIEW
					Vector(-10, -10, -10), // VEC_OBS_HULL_MIN
					Vector(10, 10, 10), // VEC_OBS_HULL_MAX
					Vector(0, 0, 14) // VEC_DEAD_VIEWHEIGHT
	  )
{
}
#else
static RulesViewVectors_t g_DefaultViewVectors(Vector(0, 0, 64), // VEC_VIEW
											   Vector(-16, -16, 0), // VEC_HULL_MIN
											   Vector(16, 16, 72), // VEC_HULL_MAX
											   Vector(-16, -16, 0), // VEC_DUCK_HULL_MIN
											   Vector(16, 16, 36), // VEC_DUCK_HULL_MAX
											   Vector(0, 0, 28), // VEC_DUCK_VIEW
											   Vector(-10, -10, -10), // VEC_OBS_HULL_MIN
											   Vector(10, 10, 10), // VEC_OBS_HULL_MAX
											   Vector(0, 0, 14) // VEC_DEAD_VIEWHEIGHT
);
#endif

static Vector SourceSharpVec3(const float* p)
{
	return Vector(p[0], p[1], p[2]);
}

// A standing hull with no volume would give every player no collision box, so it is
// refused and stock's hulls kept. The other eight vectors may legally be zero.
static bool SourceSharpHullIsDegenerate(const SourceSharpMovementParams* p)
{
	return p->vecHullMax[2] <= p->vecHullMin[2] || p->vecHullMax[0] <= p->vecHullMin[0] ||
		p->vecHullMax[1] <= p->vecHullMin[1];
}

static void PublishViewVectorsTo(RulesViewVectors_t& vectors, const SourceSharpMovementParams* pParams)
{
	if (!pParams)
		return;

	if (SourceSharpHullIsDegenerate(pParams))
	{
		Warning("[SourceSharp] movement params carry a standing hull with no "
				"volume (min %.1f %.1f %.1f, max %.1f %.1f %.1f). KEEPING the "
				"stock hull: publishing that one is a player with no collision "
				"box, which looks like a broken map rather than a missing "
				"number. Set Movement.Current's HullMin/HullMax in "
				"SourceSharp.Shared.\n",
				pParams->vecHullMin[0], pParams->vecHullMin[1], pParams->vecHullMin[2], pParams->vecHullMax[0],
				pParams->vecHullMax[1], pParams->vecHullMax[2]);
		return;
	}

	vectors.m_vView = SourceSharpVec3(pParams->vecView);
	vectors.m_vHullMin = SourceSharpVec3(pParams->vecHullMin);
	vectors.m_vHullMax = SourceSharpVec3(pParams->vecHullMax);
	vectors.m_vDuckHullMin = SourceSharpVec3(pParams->vecDuckHullMin);
	vectors.m_vDuckHullMax = SourceSharpVec3(pParams->vecDuckHullMax);
	vectors.m_vDuckView = SourceSharpVec3(pParams->vecDuckView);
	vectors.m_vObsHullMin = SourceSharpVec3(pParams->vecObsHullMin);
	vectors.m_vObsHullMax = SourceSharpVec3(pParams->vecObsHullMax);
	vectors.m_vDeadViewHeight = SourceSharpVec3(pParams->vecDeadViewHeight);
}

#ifdef CLIENT_DLL
void CGameRulesClient::PublishViewVectors(const SourceSharpMovementParams* pParams)
{
	PublishViewVectorsTo(m_ViewVectors, pParams);
}
#else
void SourceSharp_PublishViewVectors(const SourceSharpMovementParams* pParams)
{
	PublishViewVectorsTo(g_DefaultViewVectors, pParams);
}
#endif

// The server writes the rules classname into this table to tell the client to build
// its own rules object; the client constructs its one class whatever the string.
#define GAMERULES_STRINGTABLE_NAME "GameRulesCreation"

#ifdef CLIENT_DLL

// The server has made its rules object; make ours.
//-----------------------------------------------------------------------------
void CGameRulesClient::OnStringChanged(void* object, INetworkStringTable* stringTable, int stringNumber,
									   const char* newString, void const* newData)
{
	CGameRulesClient* pThis = static_cast<CGameRulesClient*>(object);

	// Two rules objects would leave the first leaked and unreachable.
	delete pThis->m_pRules;
	pThis->m_pRules = new CSourceSharpGameRules(pThis->m_ViewVectors);
}

void CGameRulesClient::InstallStringTableCallback(INetworkStringTableContainer* pStringTables)
{
	if (m_pTable)
		return;

	m_pTable = pStringTables->FindTable(GAMERULES_STRINGTABLE_NAME);

	if (m_pTable)
		m_pTable->SetStringChangedCallback(this, OnStringChanged);
}

#else

INetworkStringTable* CreateNetworkStringTables_GameRules(INetworkStringTableContainer* pTables)
{
	return pTables->CreateStringTable(GAMERULES_STRINGTABLE_NAME, 1);
}

IGameRules* SourceSharp_CreateGameRules(INetworkStringTable* pRulesTable, const CManagedAreas& managed)
{
	IGameRules* pRules = new CSourceSharpGameRules(managed);

	// Tell the client to build its own; without it, the client has no rules object.
	Assert(pRulesTable);
	if (pRulesTable)
	{
		const char* pszName = "CSourceSharpGameRules";
		pRulesTable->AddString(true, "classname", (int)strlen(pszName) + 1, pszName);
	}

	pRules->CreateCustomNetworkStringTables();
	return pRules;
}

#endif // CLIENT_DLL

#ifndef CLIENT_DLL
CSourceSharpGameRules::CSourceSharpGameRules(const CManagedAreas& managed)
	: m_Managed(managed)
{
	SOURCESHARP_TRACE();
}
#else
CSourceSharpGameRules::CSourceSharpGameRules(const RulesViewVectors_t& viewVectors)
	: m_ViewVectors(viewVectors)
{
	SOURCESHARP_TRACE();
}
#endif

CSourceSharpGameRules::~CSourceSharpGameRules()
{
	SOURCESHARP_TRACE();
}

// Stock's values as the defaults: damage paths can run before managed registers, and
// a zeroed table would make nothing time-based and everything take physics force.
#ifdef CLIENT_DLL
// The client's copy stays stock's: only the server registers its own.
static const SourceSharpDamageRules s_DamageRules = {
#else
static SourceSharpDamageRules s_DamageRules = {
#endif
	// nTimeBased
	DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION | DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN,

	// nNoPhysicsForce, which includes the time-based set as stock's does.
	DMG_FALL | DMG_BURN | DMG_PLASMA | DMG_DROWN | DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION |
		DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN | DMG_CRUSH | DMG_PHYSGUN | DMG_PREVENT_PHYSICS_FORCE,

	// nShouldGibCorpse
	DMG_CRUSH | DMG_FALL | DMG_BLAST | DMG_SONIC | DMG_CLUB,

	// nShouldNotBleed
	DMG_POISON | DMG_ACID,

	// nShowOnHud
	DMG_POISON | DMG_ACID | DMG_DROWN | DMG_BURN | DMG_SLOWBURN | DMG_NERVEGAS | DMG_RADIATION | DMG_SHOCK,
};

#ifndef CLIENT_DLL
// C# states them once at startup.
void SourceSharp_RegisterDamageRules(const SourceSharpDamageRules* pRules)
{
	if (!pRules)
		return;

	s_DamageRules = *pRules;
}

// Defaults for paths that run before C# registers.
static SourceSharpGameRulesPolicy s_Policy = {
	true, // bMultiplayer
};

// C# states them once at startup.
void SourceSharp_RegisterGameRulesPolicy(const SourceSharpGameRulesPolicy* pPolicy)
{
	if (!pPolicy)
		return;

	s_Policy = *pPolicy;
}
#endif // !CLIENT_DLL

int CSourceSharpGameRules::Damage_GetTimeBased()
{
	return s_DamageRules.nTimeBased;
}

int CSourceSharpGameRules::Damage_GetNoPhysicsForce()
{
	return s_DamageRules.nNoPhysicsForce;
}

bool CSourceSharpGameRules::IsMultiplayer()
{
#ifdef CLIENT_DLL
	// The policy table is registered on the server; the client is multiplayer too.
	return true;
#else
	return s_Policy.bMultiplayer != 0;
#endif
}

#ifndef CLIENT_DLL

// Connection admission. C# decides; with no managed side every connection is allowed.
bool CSourceSharpGameRules::ClientConnected(edict_t* pEntity, const char* pszName, const char* pszAddress, char* reject,
											int maxrejectlen)
{
	SOURCESHARP_TRACE_MSG(pszName);

	IManagedClients* pApi = m_Managed.m_pClients;
	if (pApi)
	{
		if (reject && maxrejectlen > 0)
			reject[0] = '\0';

		if (pApi->OnClientConnect(pszName ? pszName : "", pszAddress ? pszAddress : "", reject, maxrejectlen) == 0)
		{
			Msg("[SourceSharp] C# refused '%s': %s\n", pszName ? pszName : "?",
				(reject && reject[0]) ? reject : "(no reason given)");
			return false;
		}
	}

	return true;
}

// Empty: managed's GameFrame runs the frame's last managed point and the late drain.
void CSourceSharpGameRules::EndGameFrame() {}

#endif // !CLIENT_DLL

const RulesViewVectors_t* CSourceSharpGameRules::GetViewVectors() const
{
#ifdef CLIENT_DLL
	return &m_ViewVectors;
#else
	return &g_DefaultViewVectors;
#endif
}
