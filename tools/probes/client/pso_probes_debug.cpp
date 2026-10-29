//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — lane particles-so's spike probes (debug only) ======//
//
// DEBUG BUILDS ONLY, AND OPT-IN: compiled into client.so only when
// farm/debug/build is configured with -DSS_PSO_PROBES=ON. The A/B instrument
// of plan_particles_so.md section 5 steps 3-4. It was built into a client.so
// that linked particles.a and into one that forwards to libss_particles.so
// (the spike's switch, gone since: plan section 7 has the readings).
//
// WHY NOT THE RENDER CALL RECORDER. shell_render_trace_client.cpp hooks the
// view-level calls (render targets, views, world lists, model draws); a
// particle system draws below that, through IMatRenderContext::Bind, the
// dynamic mesh and IMesh::Draw, none of which it hooks. And it keeps a call
// only when it came from client.so, which after the split a particle draw
// does not.
//
//   cs_cl_pso_trace_start   hooks Bind, GetDynamicMesh(Ex) and the dynamic
//                           meshes' Draw, counting each call by the module
//                           that made it (client.so, libss_particles.so,
//                           other) and each bound material by name
//   cs_cl_pso_trace_stop    unhooks and prints, per module, the calls per
//                           frame and the materials bound, by name
//   cs_cl_pso_bench [n]     n calls of two particle-manager getters, timed
//                           (ns per call): the direct call with the archive
//                           linked, the forward with the library
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
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imesh.h"
#include "vtable_slot_patch.h"

#include <dlfcn.h>
#include <time.h>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{

enum Module_t
{
	kClient,
	kParticles,
	kOther,
	kModules
};

const char* const s_ModuleNames[kModules] = {"client.so", "libss_particles.so", "other"};

struct Counts_t
{
	int64 nBind;
	int64 nGetMesh;
	int64 nDraw;
	int64 nDrawIndices;
};

bool g_bArmed = false;
const void* g_pClientBase = NULL;
const void* g_pParticlesBase = NULL;
Counts_t g_Counts[kModules];
CUtlDict<int64, int> g_Materials[kModules];
int g_nStartFrame = 0;
CUtlVector<VTableSlotPatch::Patch> g_Patches;
CUtlVector<void**> g_MeshTables; // mesh vtables whose Draw is hooked
CUtlVector<void*> g_MeshDraws; // and each one's original Draw

void* g_pBind = NULL;
void* g_pGetDynamicMesh = NULL;
void* g_pGetDynamicMeshEx = NULL;

Module_t ModuleOf(const void* pReturn)
{
	Dl_info info;
	if (!dladdr(pReturn, &info))
		return kOther;
	if (info.dli_fbase == g_pClientBase)
		return kClient;
	if (g_pParticlesBase && info.dli_fbase == g_pParticlesBase)
		return kParticles;
	return kOther;
}

void H_MeshDraw(IMesh* self, int firstIndex, int numIndices);

void HookMesh(IMesh* pMesh)
{
	if (!pMesh || !g_bArmed)
		return;
	void** pTable = *(void***)pMesh;
	if (g_MeshTables.Find(pTable) >= 0)
		return;
	VTableSlotPatch::Patch patch;
	if (!VTableSlotPatch::Install(pMesh, static_cast<void (IMesh::*)(int, int)>(&IMesh::Draw), (void*)&H_MeshDraw,
								  &patch))
		return;
	g_Patches.AddToTail(patch);
	g_MeshTables.AddToTail(pTable);
	g_MeshDraws.AddToTail(patch.pOriginal);
}

void H_Bind(IMatRenderContext* self, IMaterial* pMaterial, void* pProxyData)
{
	if (g_bArmed)
	{
		Module_t m = ModuleOf(__builtin_return_address(0));
		g_Counts[m].nBind++;
		const char* pszName = pMaterial ? pMaterial->GetName() : "(null)";
		int i = g_Materials[m].Find(pszName);
		if (i == g_Materials[m].InvalidIndex())
			i = g_Materials[m].Insert(pszName, 0);
		g_Materials[m][i]++;
	}
	((void (*)(IMatRenderContext*, IMaterial*, void*))g_pBind)(self, pMaterial, pProxyData);
}

IMesh* H_GetDynamicMesh(IMatRenderContext* self, bool bBuffered, IMesh* pVertexOverride, IMesh* pIndexOverride,
						IMaterial* pAutoBind)
{
	if (g_bArmed)
		g_Counts[ModuleOf(__builtin_return_address(0))].nGetMesh++;
	IMesh* pMesh = ((IMesh * (*)(IMatRenderContext*, bool, IMesh*, IMesh*, IMaterial*))
						g_pGetDynamicMesh)(self, bBuffered, pVertexOverride, pIndexOverride, pAutoBind);
	HookMesh(pMesh);
	return pMesh;
}

IMesh* H_GetDynamicMeshEx(IMatRenderContext* self, VertexFormat_t fmt, bool bBuffered, IMesh* pVertexOverride,
						  IMesh* pIndexOverride, IMaterial* pAutoBind)
{
	if (g_bArmed)
		g_Counts[ModuleOf(__builtin_return_address(0))].nGetMesh++;
	IMesh* pMesh = ((IMesh * (*)(IMatRenderContext*, VertexFormat_t, bool, IMesh*, IMesh*, IMaterial*))
						g_pGetDynamicMeshEx)(self, fmt, bBuffered, pVertexOverride, pIndexOverride, pAutoBind);
	HookMesh(pMesh);
	return pMesh;
}

void H_MeshDraw(IMesh* self, int firstIndex, int numIndices)
{
	if (g_bArmed)
	{
		Module_t m = ModuleOf(__builtin_return_address(0));
		g_Counts[m].nDraw++;
		g_Counts[m].nDrawIndices += numIndices;
	}
	const int i = g_MeshTables.Find(*(void***)self);
	((void (*)(IMesh*, int, int))g_MeshDraws[i])(self, firstIndex, numIndices);
}

void Unhook()
{
	for (int i = g_Patches.Count() - 1; i >= 0; --i)
		VTableSlotPatch::Remove(g_Patches[i]);
	g_Patches.RemoveAll();
	g_MeshTables.RemoveAll();
	g_MeshDraws.RemoveAll();
}

bool Hook()
{
	if (!g_pClientBase)
	{
		Dl_info info;
		if (dladdr((void*)&Hook, &info))
			g_pClientBase = info.dli_fbase;
		void* h = dlopen("libss_particles.so", RTLD_NOW | RTLD_NOLOAD);
		if (h)
		{
			void* pFactory = dlsym(h, "CreateInterface");
			if (pFactory && dladdr(pFactory, &info))
				g_pParticlesBase = info.dli_fbase;
			dlclose(h);
		}
	}
	CMatRenderContextPtr pContext(materials);
	VTableSlotPatch::Patch patch;
	bool bOk = true;
	bOk =
		bOk && VTableSlotPatch::Install((IMatRenderContext*)pContext, &IMatRenderContext::Bind, (void*)&H_Bind, &patch);
	if (bOk)
	{
		g_pBind = patch.pOriginal;
		g_Patches.AddToTail(patch);
	}
	bOk = bOk &&
		VTableSlotPatch::Install((IMatRenderContext*)pContext, &IMatRenderContext::GetDynamicMesh,
								 (void*)&H_GetDynamicMesh, &patch);
	if (bOk)
	{
		g_pGetDynamicMesh = patch.pOriginal;
		g_Patches.AddToTail(patch);
	}
	bOk = bOk &&
		VTableSlotPatch::Install((IMatRenderContext*)pContext, &IMatRenderContext::GetDynamicMeshEx,
								 (void*)&H_GetDynamicMeshEx, &patch);
	if (bOk)
	{
		g_pGetDynamicMeshEx = patch.pOriginal;
		g_Patches.AddToTail(patch);
	}
	if (!bOk)
		Unhook();
	return bOk;
}

double NowNs()
{
	timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return ts.tv_sec * 1e9 + ts.tv_nsec;
}

} // namespace

CON_COMMAND(cs_cl_pso_trace_start, "particles-so spike: count Bind/dynamic mesh/Draw calls per module")
{
	if (g_bArmed)
		return;
	memset(g_Counts, 0, sizeof(g_Counts));
	for (int m = 0; m < kModules; ++m)
		g_Materials[m].RemoveAll();
	if (!Hook())
	{
		Warning("[SourceSharp/pso] a hook could not be installed\n");
		return;
	}
	g_nStartFrame = gpGlobals->framecount;
	g_bArmed = true;
	Msg("[SourceSharp/pso] trace armed at frame %d (libss_particles.so %s)\n", g_nStartFrame,
		g_pParticlesBase ? "loaded" : "not loaded");
}

CON_COMMAND(cs_cl_pso_trace_stop, "particles-so spike: stop counting and print")
{
	if (!g_bArmed)
		return;
	g_bArmed = false;
	Unhook();
	const int nFrames = MAX(1, gpGlobals->framecount - g_nStartFrame);
	Msg("[SourceSharp/pso] trace: %d frames\n", nFrames);
	for (int m = 0; m < kModules; ++m)
	{
		const Counts_t& c = g_Counts[m];
		Msg("[SourceSharp/pso] module %s binds %lld getmesh %lld draws %lld indices %lld (per frame %.2f %.2f %.2f %.1f)\n",
			s_ModuleNames[m], c.nBind, c.nGetMesh, c.nDraw, c.nDrawIndices, (double)c.nBind / nFrames,
			(double)c.nGetMesh / nFrames, (double)c.nDraw / nFrames, (double)c.nDrawIndices / nFrames);
		for (int i = g_Materials[m].First(); i != g_Materials[m].InvalidIndex(); i = g_Materials[m].Next(i))
			Msg("[SourceSharp/pso] material %s %s %lld\n", s_ModuleNames[m], g_Materials[m].GetElementName(i),
				g_Materials[m][i]);
	}
}

CON_COMMAND(cs_cl_pso_bench, "particles-so spike: time n particle-manager getter calls")
{
	const int n = args.ArgC() > 1 ? MAX(1000, atoi(args[1])) : 10000000;
	volatile float flSink = 0.0f;
	double t0 = NowNs();
	for (int i = 0; i < n; ++i)
		flSink = flSink + g_pParticleSystemMgr->GetLastSimulationTime();
	double t1 = NowNs();
	for (int i = 0; i < n; ++i)
		flSink = flSink + g_pParticleSystemMgr->ParticleThrottleScaling();
	double t2 = NowNs();
	Msg("[SourceSharp/pso] bench %d calls: GetLastSimulationTime %.2f ns/call, ParticleThrottleScaling %.2f ns/call (%s)\n",
		n, (t1 - t0) / n, (t2 - t1) / n, "forwarded to libss_particles.so");
}
