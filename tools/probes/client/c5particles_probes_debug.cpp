//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — lane c5-particles's oracle probes (debug only) =====//
//
// DEBUG BUILDS ONLY, AND OPT-IN, as shell_probes_debug.cpp beside it: compiled
// into client.so only when farm/debug/build is configured with
// -DSS_CLIENT_PROBES=ON. Records the STOCK code a lane c5-particles port
// replaces (plan_client_novalve.md §5.1 step 1) into a fixture under
// $SOURCESHARP_C5P_FIXTURE_DIR; nothing runs unless that is set.
//
//   particlequery-stock.tsv   particlesystemquery.cpp's CParticleSystemQuery,
//                             as particles.a calls it: a forwarder swapped into
//                             g_pParticleSystemQuery before the particle
//                             manager's Init hands it to the library records
//                             every call's inputs, the arm state the answer
//                             turns on (engine->IsInGame, the engine's own
//                             GetLightForPoint vector, whether a controlling
//                             object is bound, the control point's position)
//                             and the outputs. Up to kMaxRows per method.
//
// Row shapes (tab-separated; floats %.9g, which round-trips a float):
//   cg     name result ns   (ns: CLOCK_MONOTONIC, the clock of the C0 probe
//          log's lines, so a call is placed against "client-dll Init exit")
//   light  ox oy oz ingame rawx rawy rawz | r g b a
//   trace  sx sy sz ex ey ez mask ignore group ingame | spx spy spz epx epy epz
//          nx ny nz dist type signbits fraction contents dispflags allsolid startsolid
//   rp     cp npts scale tries bx by bz hasobject cpx cpy cpz hasidx hasrel |
//          then per point: px py pz [idx] [rx ry rz]
//   hbi    cp bufsize hasobject | result
//   never  method          (one per call of a method C0 measured as never called)
//
//=============================================================================//

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
#include "particles/particles.h"
#include "cdll_int.h"
#include "c_te_effect_dispatch.h"
#include "IEffects.h"
#include "fx.h"
#include "decals.h"
#include "shell_cliententitylist.h"
#include "c_te_legacytempents.h"
#include "tempent.h"
#include "engine/ivmodelinfo.h"
#include "model_types.h"
#include "studio.h"

#include <time.h>

#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// c5p_dispatch NAME x y z nx ny nz scale color flags [magnitude]: a client effect run
// locally with the CEffectData fields the blood and splash effects read
// (cs_sv_te_dispatch sets no colour, and the blood bullet impact is reached
// only by a colour the impact table has no system for). Probe builds only.
//-----------------------------------------------------------------------------
CON_COMMAND(c5p_dispatch,
			"c5p_dispatch NAME x y z nx ny nz scale color flags -- run a client effect locally (probe builds)")
{
	if (args.ArgC() < 11)
	{
		Msg("usage: c5p_dispatch NAME x y z nx ny nz scale color flags\n");
		return;
	}
	CEffectData data;
	data.m_vOrigin.Init(atof(args[2]), atof(args[3]), atof(args[4]));
	data.m_vNormal.Init(atof(args[5]), atof(args[6]), atof(args[7]));
	data.m_flScale = atof(args[8]);
	data.m_nColor = atoi(args[9]);
	data.m_fFlags = atoi(args[10]);
	if (args.ArgC() > 11)
		data.m_flMagnitude = atof(args[11]);
	DispatchEffectToCallback(args[1], data);
}

// c5p_muzzle_attached (FX_MuzzleEffectAttached on the local player) left with
// fx.cpp: it drove the fx-stock recording in the base worktree, and that
// function is managed now (FxEffects).

//-----------------------------------------------------------------------------
// c5p_te_muzzle type attachment firstperson count: the temp entities' muzzle
// flash on the local player's attachment, count times (the NPC variants have no
// NPC in this map to fire them). Probe builds only.
//-----------------------------------------------------------------------------
CON_COMMAND(
	c5p_te_muzzle,
	"c5p_te_muzzle type attachment firstperson count -- tempents->MuzzleFlash on the local player (probe builds)")
{
	if (args.ArgC() < 5 || !tempents)
		return;
	IClientEntity* pPlayer = SourceSharp_ClientEntityFromIndex(engine->GetLocalPlayer());
	if (!pPlayer)
		return;
	for (int i = 0; i < atoi(args[4]); i++)
		tempents->MuzzleFlash(atoi(args[1]), pPlayer->GetRefEHandle(), atoi(args[2]), atoi(args[3]) != 0);
}

//-----------------------------------------------------------------------------
// c5p_ieffects muzzle x y z p y r scale type | smoke x y z scale | ricochet x y z dx dy dz |
// dust x y z dx dy dz size speed (FX_Dust itself, lane c5-particles):
// the client's IEffects, called locally. Probe builds only.
//-----------------------------------------------------------------------------
CON_COMMAND(c5p_ieffects, "c5p_ieffects muzzle|smoke|ricochet ... -- the client's IEffects, locally (probe builds)")
{
	if (args.ArgC() < 2 || !g_pEffects)
		return;
	if (!Q_stricmp(args[1], "muzzle") && args.ArgC() >= 10)
		g_pEffects->MuzzleFlash(Vector(atof(args[2]), atof(args[3]), atof(args[4])),
								QAngle(atof(args[5]), atof(args[6]), atof(args[7])), atof(args[8]), atoi(args[9]));
	else if (!Q_stricmp(args[1], "smoke") && args.ArgC() >= 6)
		g_pEffects->Smoke(Vector(atof(args[2]), atof(args[3]), atof(args[4])), 0, atof(args[5]), 10.0f);
	else if (!Q_stricmp(args[1], "ricochet") && args.ArgC() >= 8)
		g_pEffects->Ricochet(Vector(atof(args[2]), atof(args[3]), atof(args[4])),
							 Vector(atof(args[5]), atof(args[6]), atof(args[7])));
	// "dust" (FX_Dust itself) left with the native puff (lane c5-particles: DustPuff).
}

extern IParticleSystemQuery* g_pParticleSystemQuery;

namespace
{

const int kMaxRows = 4000;

// THE PARTICLE SIMULATION IS THREADED (r_threaded_particles: ParallelProcess
// over the systems), so these methods are called from several worker threads
// at once. Stock serialises its own GetLightForPoint with s_LightMutex for that
// reason; the recorder's second GetLightForPoint and its rows take this one.
// (Without it, two of three sweeps crashed or hung at the first lit system.)
CThreadFastMutex g_Mutex;

FILE* g_pFixture;
int g_nRows[8];

enum
{
	ROW_CG,
	ROW_LIGHT,
	ROW_TRACE,
	ROW_RP,
	ROW_HBI,
	ROW_NEVER
};

bool Take(int nKind)
{
	if (!g_pFixture || g_nRows[nKind] >= kMaxRows)
		return false;
	g_nRows[nKind]++;
	return true;
}

void Flush()
{
	if (g_pFixture)
		fflush(g_pFixture);
}

class CRecordingParticleQuery : public IParticleSystemQuery
{
public:
	explicit CRecordingParticleQuery(IParticleSystemQuery* pInner)
		: m_pInner(pInner)
	{
	}

	virtual bool Connect(CreateInterfaceFn factory) OVERRIDE { return m_pInner->Connect(factory); }

	virtual void Disconnect() OVERRIDE { m_pInner->Disconnect(); }

	virtual void* QueryInterface(const char* pName) OVERRIDE { return m_pInner->QueryInterface(pName); }

	virtual InitReturnVal_t Init() OVERRIDE { return m_pInner->Init(); }

	virtual void Shutdown() OVERRIDE { m_pInner->Shutdown(); }

	virtual void GetLightingAtPoint(const Vector& vecOrigin, Color& tint) OVERRIDE
	{
		m_pInner->GetLightingAtPoint(vecOrigin, tint);
		AUTO_LOCK(g_Mutex);
		if (!Take(ROW_LIGHT))
			return;
		const bool bInGame = engine->IsInGame();
		Vector raw(0, 0, 0);
		if (bInGame)
			raw = engine->GetLightForPoint(vecOrigin, true);
		fprintf(g_pFixture, "light\t%.9g\t%.9g\t%.9g\t%d\t%.9g\t%.9g\t%.9g\t|\t%d\t%d\t%d\t%d\n", vecOrigin.x,
				vecOrigin.y, vecOrigin.z, bInGame ? 1 : 0, raw.x, raw.y, raw.z, tint.r(), tint.g(), tint.b(), tint.a());
		Flush();
	}

	virtual void TraceLine(const Vector& vecAbsStart, const Vector& vecAbsEnd, unsigned int mask,
						   const class IHandleEntity* ignore, int collisionGroup, CBaseTrace* ptr) OVERRIDE
	{
		m_pInner->TraceLine(vecAbsStart, vecAbsEnd, mask, ignore, collisionGroup, ptr);
		AUTO_LOCK(g_Mutex);
		if (!Take(ROW_TRACE))
			return;
		fprintf(g_pFixture,
				"trace\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%u\t%d\t%d\t%d\t|"
				"\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%d\t%d\t%.9g\t%d\t%d\t%d\t%d\n",
				vecAbsStart.x, vecAbsStart.y, vecAbsStart.z, vecAbsEnd.x, vecAbsEnd.y, vecAbsEnd.z, mask,
				ignore ? 1 : 0, collisionGroup, engine->IsInGame() ? 1 : 0, ptr->startpos.x, ptr->startpos.y,
				ptr->startpos.z, ptr->endpos.x, ptr->endpos.y, ptr->endpos.z, ptr->plane.normal.x, ptr->plane.normal.y,
				ptr->plane.normal.z, ptr->plane.dist, (int)ptr->plane.type, (int)ptr->plane.signbits, ptr->fraction,
				ptr->contents, (int)ptr->dispFlags, ptr->allsolid ? 1 : 0, ptr->startsolid ? 1 : 0);
		Flush();
	}

	virtual bool MovePointInsideControllingObject(CParticleCollection* pParticles, void* pObject, Vector* pPnt) OVERRIDE
	{
		Never("MovePointInsideControllingObject");
		return m_pInner->MovePointInsideControllingObject(pParticles, pObject, pPnt);
	}

	virtual bool IsPointInControllingObjectHitBox(CParticleCollection* pParticles, int nControlPointNumber,
												  Vector vecPos, bool bBBoxOnly) OVERRIDE
	{
		Never("IsPointInControllingObjectHitBox");
		return m_pInner->IsPointInControllingObjectHitBox(pParticles, nControlPointNumber, vecPos, bBBoxOnly);
	}

	virtual int GetCollisionGroupFromName(const char* pszCollisionGroupName) OVERRIDE
	{
		const int nResult = m_pInner->GetCollisionGroupFromName(pszCollisionGroupName);
		AUTO_LOCK(g_Mutex);
		if (Take(ROW_CG))
		{
			timespec ts;
			clock_gettime(CLOCK_MONOTONIC, &ts);
			fprintf(g_pFixture, "cg\t%s\t%d\t%lld\n", pszCollisionGroupName ? pszCollisionGroupName : "(null)", nResult,
					(long long)ts.tv_sec * 1000000000LL + ts.tv_nsec);
			Flush();
		}
		return nResult;
	}

	virtual void GetRandomPointsOnControllingObjectHitBox(CParticleCollection* pParticles, int nControlPointNumber,
														  int nNumPtsOut, float flBBoxScale,
														  int nNumTrysToGetAPointInsideTheModel, Vector* pPntsOut,
														  Vector vecDirectionBias, Vector* pHitBoxRelativeCoordOut,
														  int* pHitBoxIndexOut) OVERRIDE
	{
		m_pInner->GetRandomPointsOnControllingObjectHitBox(pParticles, nControlPointNumber, nNumPtsOut, flBBoxScale,
														   nNumTrysToGetAPointInsideTheModel, pPntsOut,
														   vecDirectionBias, pHitBoxRelativeCoordOut, pHitBoxIndexOut);
		AUTO_LOCK(g_Mutex);
		if (!Take(ROW_RP))
			return;
		const Vector& cp = pParticles->m_ControlPoints[nControlPointNumber].m_Position;
		const bool bObject = pParticles->m_ControlPoints[nControlPointNumber].m_pObject != NULL;
		fprintf(g_pFixture, "rp\t%d\t%d\t%.9g\t%d\t%.9g\t%.9g\t%.9g\t%d\t%.9g\t%.9g\t%.9g\t%d\t%d\t|",
				nControlPointNumber, nNumPtsOut, flBBoxScale, nNumTrysToGetAPointInsideTheModel, vecDirectionBias.x,
				vecDirectionBias.y, vecDirectionBias.z, bObject ? 1 : 0, cp.x, cp.y, cp.z, pHitBoxIndexOut ? 1 : 0,
				pHitBoxRelativeCoordOut ? 1 : 0);
		for (int i = 0; i < nNumPtsOut; i++)
		{
			fprintf(g_pFixture, "\t%.9g\t%.9g\t%.9g", pPntsOut[i].x, pPntsOut[i].y, pPntsOut[i].z);
			if (pHitBoxIndexOut)
				fprintf(g_pFixture, "\t%d", pHitBoxIndexOut[i]);
			if (pHitBoxRelativeCoordOut)
				fprintf(g_pFixture, "\t%.9g\t%.9g\t%.9g", pHitBoxRelativeCoordOut[i].x, pHitBoxRelativeCoordOut[i].y,
						pHitBoxRelativeCoordOut[i].z);
		}
		fprintf(g_pFixture, "\n");
		Flush();
	}

	virtual int GetControllingObjectHitBoxInfo(CParticleCollection* pParticles, int nControlPointNumber, int nBufSize,
											   ModelHitBoxInfo_t* pHitBoxOutputBuffer) OVERRIDE
	{
		const int nResult =
			m_pInner->GetControllingObjectHitBoxInfo(pParticles, nControlPointNumber, nBufSize, pHitBoxOutputBuffer);
		AUTO_LOCK(g_Mutex);
		if (Take(ROW_HBI))
		{
			const bool bObject = pParticles->m_ControlPoints[nControlPointNumber].m_pObject != NULL;
			fprintf(g_pFixture, "hbi\t%d\t%d\t%d\t|\t%d\n", nControlPointNumber, nBufSize, bObject ? 1 : 0, nResult);
			Flush();
		}
		return nResult;
	}

	virtual Vector GetLocalPlayerPos() OVERRIDE
	{
		Never("GetLocalPlayerPos");
		return m_pInner->GetLocalPlayerPos();
	}

	virtual void GetLocalPlayerEyeVectors(Vector* pForward, Vector* pRight, Vector* pUp) OVERRIDE
	{
		Never("GetLocalPlayerEyeVectors");
		m_pInner->GetLocalPlayerEyeVectors(pForward, pRight, pUp);
	}

	virtual float GetPixelVisibility(int* pQueryHandle, const Vector& vecOrigin, float flScale) OVERRIDE
	{
		Never("GetPixelVisibility");
		return m_pInner->GetPixelVisibility(pQueryHandle, vecOrigin, flScale);
	}

	virtual void SetUpLightingEnvironment(const Vector& pos) OVERRIDE
	{
		Never("SetUpLightingEnvironment");
		m_pInner->SetUpLightingEnvironment(pos);
	}

private:
	static void Never(const char* pMethod)
	{
		AUTO_LOCK(g_Mutex);
		if (Take(ROW_NEVER))
		{
			fprintf(g_pFixture, "never\t%s\n", pMethod);
			Flush();
		}
	}

	IParticleSystemQuery* m_pInner;
};

// Runs at static init, before the client's Init hands the query to the
// library (particlemgr.cpp's CParticleMgr::Init), as shell_probes_debug.cpp's
// pq probe does; the two wrap in either order.
struct CInstaller
{
	CInstaller()
	{
		const char* pDir = getenv("SOURCESHARP_C5P_FIXTURE_DIR");
		if (!pDir || !*pDir || !g_pParticleSystemQuery)
			return;
		char path[1024];
		snprintf(path, sizeof(path), "%s/particlequery-stock.tsv", pDir);
		g_pFixture = fopen(path, "w");
		if (!g_pFixture)
			return;
		fprintf(
			g_pFixture,
			"# particlesystemquery.cpp's CParticleSystemQuery as particles.a called it (lane c5-particles probe)\n");
		g_pParticleSystemQuery = new CRecordingParticleQuery(g_pParticleSystemQuery);
	}
} g_Installer;

} // namespace

// c5p_spray (FX_DebrisFlecks, FX_DustImpact, FX_Blood and FX_BugBlood run
// directly) left with those bodies: it drove the sprays-stock recording in the
// base worktree, and the four are ImpactSprays now.

// c5p_funnel (the large funnel's native backend, either direction) left with
// that backend: it drove the funnel-stock recording in the base worktree, and
// the funnel is LargeFunnel now.

//-----------------------------------------------------------------------------
// c5p_te_bones: every live studio tempent's render state, and whether its
// SetupBones writes the slot the studio draw places the mesh with (a sentinel
// buffer, read back). "UNWRITTEN" was the invisible shell casing.
//-----------------------------------------------------------------------------
C_LocalTempEntity* SourceSharpTePool_AtSlot(int nSlot);

CON_COMMAND(c5p_te_bones, "c5p_te_bones -- each live studio tempent's blend, pass and bone setup (probe builds)")
{
	int nStudio = 0, nWritten = 0;

	for (int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++)
	{
		C_LocalTempEntity* pTemp = SourceSharpTePool_AtSlot(i);
		if (!pTemp || !pTemp->GetModel() || modelinfo->GetModelType(pTemp->GetModel()) != mod_studio)
			continue;

		matrix3x4_t bones[2];
		memset(bones, 0x7f, sizeof(bones));
		const bool bOk = pTemp->SetupBones(bones, 2, BONE_USED_BY_ANYTHING, gpGlobals->curtime);
		unsigned int nFirst;
		memcpy(&nFirst, &bones[0][0][0], sizeof(nFirst));
		const bool bWritten = nFirst != 0x7f7f7f7fu;

		nStudio++;
		nWritten += bWritten ? 1 : 0;

		const Vector& o = pTemp->GetAbsOrigin();
		const Vector& v = pTemp->GetVelocity();
		Msg("[SourceSharp]   %3d %s mode %d alpha %d blend %d group %d leaf %d draw %d "
			"bones %s/%s org (%.1f %.1f %.1f) vel (%.1f %.1f %.1f) die-in %.2f\n",
			i, modelinfo->GetModelName(pTemp->GetModel()), (int)pTemp->GetRenderMode(), (int)pTemp->m_clrRender.a,
			pTemp->GetFxBlend(), (int)pTemp->m_RenderGroup, pTemp->IsInLeafSystem() ? 1 : 0,
			pTemp->ShouldDraw() ? 1 : 0, bOk ? "ok" : "refused", bWritten ? "written" : "UNWRITTEN", o.x, o.y, o.z, v.x,
			v.y, v.z, pTemp->die - gpGlobals->curtime);
	}

	Msg("[SourceSharp] c5p_te_bones: %d studio tempent(s), %d with bones written\n", nStudio, nWritten);
}
