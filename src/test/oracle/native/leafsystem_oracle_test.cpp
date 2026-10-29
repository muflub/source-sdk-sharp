//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE LEAF SYSTEM ORACLE: stock's client leaf system -- CClientLeafSystem's
// registry of renderables and the BSP leaves they sit in, the per-view list
// BuildRenderablesList builds over it (CollateRenderablesInLeaf's arms: the
// static-prop switch, the same-frame dedupe, the translucent render leaf,
// alpha 0, the area frustum under r_PortalTestEnts, the main frustum, the
// occlusion test, the opaque size buckets, the two-pass split, the detail
// objects and the list's overflow), ComputeTranslucentRenderLeaf's nearest
// (and, alternately sorted, furthest) leaf, SortEntities' order,
// CollateViewModelRenderables' split, and the shadows projected into leaves
// and enumerated out of them -- run offline from the PRISTINE
// game/client/clientleafsystem.cpp over a scripted world, and written down as
// the golden src/test/client/Fixtures/leafsystem-stock.tsv
// (README-leafsystem-stock.md says how to read it).
//
// WHY OFFLINE. A live map draws what its designer placed, in the leaves its
// compiler cut; most arms here (a two-pass model at alpha 128, an alternately
// sorted translucent box across three leaves, a list overflowing 4096) are
// never met. Here each is placed on purpose.
//
// THE SUBJECT IS INCLUDED, NOT LINKED: its class is file-local, so the harness
// includes the file into this translation unit. leafsystem_stub/
// leafsystem_harness.h says what is stock and what is stand-in.
//
// WHAT MANAGED REPLAYS. The managed leaf system (SourceSharp.Client's
// LeafRegistry and RenderableListBuilder, L2) owns the registry and every
// decision. The golden carries its own stimulus -- every call the scenario
// makes is a row before it is made, every thing described first (T) -- so
// LeafRegistryOracleTests rebuilds the same world from the golden alone and
// requires stock's rows: handles, leaves and areas, receivers, enumerations,
// blends, the engine's questions, each view's list and state, the view
// models, the PVS, leaf walks, detail state.
//
// THE VB ROWS ARE THE ORACLE'S OWN: stock's registry for each view and the
// engine's answers about each renderable in it, read through
// leafsystem_stub/leafsystem_gather.h's layout mirror (L1 built that reader
// for client.so; L2 left it here). TheScenarioReachesEveryArm names each arm
// from them. Each is written as the registry stands after the view's
// ComputeFxBlend, with each renderable's render frame and leaf from before
// stock's ComputeTranslucentRenderLeaf moved them.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; SourceSharp.Client.Tests' LeafRegistryOracleTests asserts the
// managed port does. Regenerating the golden to make this test green deletes
// the check: the golden changes only when the scenario or a row's format
// does, and then on purpose (touch leafsystem_golden.regen beside this file,
// run, delete the marker, and say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <algorithm>
#include <fstream>
#include <map>
#include <memory>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"
#include "valve_sdk.h"

#include "leafsystem_harness.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;
static bool g_bQuiet = false;

void LeafLog(const char* pFmt, ...)
{
	if (g_bQuiet)
		return;
	// A buffer's row is long (the 4100-leaf view's is 220 KB): sized, never cut.
	va_list ap, copy;
	va_start(ap, pFmt);
	va_copy(copy, ap);
	const int n = vsnprintf(NULL, 0, pFmt, copy);
	va_end(copy);
	std::string line(n > 0 ? n : 0, '\0');
	if (n > 0)
		vsnprintf(&line[0], n + 1, pFmt, ap);
	va_end(ap);
	g_Log += line;
	g_Log += '\n';
}

unsigned int LeafBits(float f)
{
	unsigned int u;
	memcpy(&u, &f, sizeof(u));
	return u;
}

//-----------------------------------------------------------------------------
// The console: ConVar::SetValue tells g_pCVar's global change callbacks.
//-----------------------------------------------------------------------------
#include "icvar.h"

class CLeafCvar final : public ICvar
{
public:
	virtual bool Connect(CreateInterfaceFn) { return true; }

	virtual void Disconnect() {}

	virtual void* QueryInterface(const char*) { return NULL; }

	virtual InitReturnVal_t Init() { return INIT_OK; }

	virtual void Shutdown() {}

	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 1; }

	virtual void RegisterConCommand(ConCommandBase*) {}

	virtual void UnregisterConCommand(ConCommandBase*) {}

	virtual void UnregisterConCommands(CVarDLLIdentifier_t) {}

	virtual const char* GetCommandLineValue(const char*) { return NULL; }

	virtual ConCommandBase* FindCommandBase(const char*) { return NULL; }

	virtual const ConCommandBase* FindCommandBase(const char*) const { return NULL; }

	virtual ConVar* FindVar(const char*) { return NULL; }

	virtual const ConVar* FindVar(const char*) const { return NULL; }

	virtual ConCommand* FindCommand(const char*) { return NULL; }

	virtual const ConCommand* FindCommand(const char*) const { return NULL; }

	virtual ConCommandBase* GetCommands(void) { return NULL; }

	virtual const ConCommandBase* GetCommands(void) const { return NULL; }

	virtual void InstallGlobalChangeCallback(FnChangeCallback_t) {}

	virtual void RemoveGlobalChangeCallback(FnChangeCallback_t) {}

	virtual void CallGlobalChangeCallbacks(ConVar*, const char*, float) {}

	virtual void InstallConsoleDisplayFunc(IConsoleDisplayFunc*) {}

	virtual void RemoveConsoleDisplayFunc(IConsoleDisplayFunc*) {}

	virtual void ConsoleColorPrintf(const Color&, const char*, ...) const {}

	virtual void ConsolePrintf(const char*, ...) const {}

	virtual void ConsoleDPrintf(const char*, ...) const {}

	virtual void RevertFlaggedConVars(int) {}

	virtual void InstallCVarQuery(ICvarQuery*) {}

	virtual void PublishToVXConsole() {}

	virtual bool IsMaterialThreadSetAllowed() const { return true; }

	virtual void QueueMaterialThreadSetValue(ConVar*, const char*) {}

	virtual void QueueMaterialThreadSetValue(ConVar*, int) {}

	virtual void QueueMaterialThreadSetValue(ConVar*, float) {}

	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }

	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }

	virtual ICVarIteratorInternal* FactoryInternalIterator(void) { return NULL; }
};

static CLeafCvar g_LeafCvar;

//-----------------------------------------------------------------------------
// The globals and the stand-ins the subject reaches.
//-----------------------------------------------------------------------------
static LeafGlobals_t s_Globals;
LeafGlobals_t* gpGlobals = &s_Globals;
static LeafEngine s_Engine;
LeafEngine* engine = &s_Engine;
static LeafRender s_Render;
LeafRender* render = &s_Render;
static LeafModelInfo s_ModelInfo;
LeafModelInfo* modelinfo = &s_ModelInfo;
static LeafMdlCache s_MdlCache;
LeafMdlCache* mdlcache = &s_MdlCache;
static LeafShadowMgr s_ShadowMgr;
LeafShadowMgr* g_pClientShadowMgr = &s_ShadowMgr;
static LeafDetailSystem s_DetailSystem;

LeafDetailSystem* DetailObjectSystem()
{
	return &s_DetailSystem;
}

//-----------------------------------------------------------------------------
// The scripted world: leaves are boxes along x, each in an area; a view's
// frustum, the areas' portal frustums and one occluder are boxes too.
//-----------------------------------------------------------------------------
struct LeafBox_t
{
	Vector mins, maxs;
};

static bool Overlaps(const Vector& mins, const Vector& maxs, const LeafBox_t& b)
{
	return mins.x < b.maxs.x && maxs.x > b.mins.x && mins.y < b.maxs.y && maxs.y > b.mins.y && mins.z < b.maxs.z &&
		maxs.z > b.mins.z;
}

static bool Inside(const Vector& mins, const Vector& maxs, const LeafBox_t& b)
{
	return mins.x >= b.mins.x && maxs.x <= b.maxs.x && mins.y >= b.mins.y && maxs.y <= b.maxs.y && mins.z >= b.mins.z &&
		maxs.z <= b.maxs.z;
}

static std::vector<LeafBox_t> s_LeafBoxes;
static std::vector<int> s_LeafAreas;
static LeafBox_t s_Frustum;
static LeafBox_t s_AreaFrustum[4];
static LeafBox_t s_Occluder;
static int s_nLastBoundsId = -1; // the renderable whose box the engine is about to be asked about

static LeafBox_t MakeBox(float x0, float y0, float z0, float x1, float y1, float z1)
{
	LeafBox_t b;
	b.mins.Init(x0, y0, z0);
	b.maxs.Init(x1, y1, z1);
	return b;
}

class CLeafBSPQuery final : public ISpatialQuery
{
public:
	virtual int LeafCount() const { return (int)s_LeafBoxes.size(); }

	virtual bool EnumerateLeavesAtPoint(Vector const&, ISpatialLeafEnumerator*, intp) { return true; }

	virtual bool EnumerateLeavesInBox(Vector const& mins, Vector const& maxs, ISpatialLeafEnumerator* pEnum,
									  intp context)
	{
		for (int i = 0; i < (int)s_LeafBoxes.size(); i++)
		{
			if (Overlaps(mins, maxs, s_LeafBoxes[i]) && !pEnum->EnumerateLeaf(i, context))
				return false;
		}
		return true;
	}

	virtual bool EnumerateLeavesInSphere(Vector const&, float, ISpatialLeafEnumerator*, intp) { return true; }

	virtual bool EnumerateLeavesAlongRay(Ray_t const&, ISpatialLeafEnumerator*, intp) { return true; }
};

static CLeafBSPQuery s_BSPQuery;

int LeafEngine::LevelLeafCount()
{
	return (int)s_LeafBoxes.size();
}

int LeafEngine::GetLeavesArea(int* pLeaves, int nLeaves)
{
	if (nLeaves == 0)
		return -1;
	const int nArea = s_LeafAreas[pLeaves[0]];
	for (int i = 1; i < nLeaves; i++)
	{
		if (s_LeafAreas[pLeaves[i]] != nArea)
			return -1;
	}
	return nArea;
}

ISpatialQuery* LeafEngine::GetBSPTreeQuery()
{
	return &s_BSPQuery;
}

bool LeafEngine::DoesBoxTouchAreaFrustum(const Vector& mins, const Vector& maxs, int iArea)
{
	const bool bTouch = Overlaps(mins, maxs, s_AreaFrustum[iArea]);
	LeafLog("Q\tarea\t%d\t%d\t%d", s_nLastBoundsId, iArea, bTouch ? 1 : 0);
	return bTouch;
}

bool LeafEngine::CullBox(const Vector& mins, const Vector& maxs)
{
	const bool bCull = !Overlaps(mins, maxs, s_Frustum);
	LeafLog("Q\tcull\t%d\t%d", s_nLastBoundsId, bCull ? 1 : 0);
	return bCull;
}

bool LeafEngine::IsOccluded(const Vector& vecAbsMins, const Vector& vecAbsMaxs)
{
	const bool bOccluded = Inside(vecAbsMins, vecAbsMaxs, s_Occluder);
	LeafLog("Q\tocc\t%d\t%d", s_nLastBoundsId, bOccluded ? 1 : 0);
	return bOccluded;
}

void LeafEngine::Con_NPrintf(int pos, const char* fmt, ...)
{
	char szText[512];
	va_list ap;
	va_start(ap, fmt);
	vsnprintf(szText, sizeof(szText), fmt, ap);
	va_end(ap);
	LeafLog("NP\t%d\t%s", pos, szText);
}

// A leaf is visible when it is one of the first six (area 1's, on the
// 12-leaf level): IsRenderableInPVS's answer, and the leaves it was asked of.
bool LeafRender::AreAnyLeavesVisible(int* pLeafList, int nLeaves)
{
	std::string s;
	bool bVisible = false;
	for (int i = 0; i < nLeaves; i++)
	{
		s += (i ? "," : "") + std::to_string(pLeafList[i]);
		bVisible = bVisible || pLeafList[i] < 6;
	}
	LeafLog("PVQ\t%s\t%d", s.c_str(), bVisible ? 1 : 0);
	return bVisible;
}

static int IdOf(IClientRenderable* p)
{
	return p ? static_cast<C_BaseEntity*>(p)->m_nId : -1;
}

void LeafShadowMgr::AddShadowToReceiver(ClientShadowHandle_t handle, IClientRenderable* pRenderable,
										ShadowReceiver_t type)
{
	LeafLog("SR\t%d\t%d\t%d", handle, IdOf(pRenderable), type);
}

void LeafShadowMgr::RemoveAllShadowsFromReceiver(IClientRenderable* pRenderable, ShadowReceiver_t type)
{
	LeafLog("SX\t%d\t%d", IdOf(pRenderable), type);
}

// The detail object system's models, by index; a NULL is a sprite slot.
static std::vector<IClientRenderable*> s_DetailModels;

IClientRenderable* LeafDetailSystem::GetDetailModel(int idx)
{
	return (idx >= 0 && idx < (int)s_DetailModels.size()) ? s_DetailModels[idx] : NULL;
}

// What the subject's header and its interface registration link against:
// igamesystem.cpp's two destructors, and tier1's InterfaceReg, which records
// the name the engine would ask for (ClientLeafSystem002) and nothing more.
#include "igamesystem.h"
#include "tier1/interface.h"

IGameSystem::~IGameSystem() {}

IGameSystemPerFrame::~IGameSystemPerFrame() {}

static const char* s_pszExposedAs = NULL;

InterfaceReg::InterfaceReg(InstantiateInterfaceFn fn, const char* pName)
	: m_pName(pName)
{
	m_CreateFn = fn;
	m_pNext = NULL;
	s_pszExposedAs = pName;
}

//-----------------------------------------------------------------------------
// THE SUBJECT.
//-----------------------------------------------------------------------------
#include "clientleafsystem.cpp"
#include "tier0/memdbgoff.h"

// The oracle's own reading of the registry, for the VB rows.
#include "leafsystem_gather.h"

static CClientLeafSystem& Subject()
{
	return *static_cast<CClientLeafSystem*>(g_pClientLeafSystem);
}

static CLeafRegistryMirror& Registry()
{
	return *CLeafRegistryMirror::Of(g_pClientLeafSystem);
}

// The view models' groups as stock's CollateViewModelRenderables leaves them
// after each one's ComputeFxBlend, in its order (golden mode).
static bool s_bViewModelCapture = false;
static std::vector<std::pair<int, int>> s_ViewModelCapture; // id, group

void C_BaseEntity::ComputeFxBlend()
{
	if (m_nBlendTo >= 0)
		m_nFxBlend = m_nBlendTo;
	if (m_nGroupOnBlend >= 0 && RenderHandle() != INVALID_CLIENT_RENDER_HANDLE)
		ClientLeafSystem()->SetRenderGroup(RenderHandle(), (RenderGroup_t)m_nGroupOnBlend);
	LeafLog("BL\t%d\t%d", m_nId, m_nFxBlend);
	if (s_bViewModelCapture)
		s_ViewModelCapture.push_back({m_nId, Registry().m_Renderables[RenderHandle()].m_RenderGroup});
}

int C_BaseEntity::GetFxBlend()
{
	LeafLog("FB\t%d\t%d", m_nId, m_nFxBlend);
	return m_nFxBlend;
}

void C_BaseEntity::GetRenderBoundsWorldspace(Vector& mins, Vector& maxs)
{
	s_nLastBoundsId = m_nId;
	mins = m_vecAbsMins;
	maxs = m_vecAbsMaxs;
}

namespace
{

//-----------------------------------------------------------------------------
// The scenario's things.
//-----------------------------------------------------------------------------
std::vector<std::unique_ptr<C_BaseEntity>> s_Things;

// A renderable at [x0, x0 + wx] along x, centred on y0, hy and hz either
// side; its origin is the box's low-x face, so its render bounds are not
// centred on it (SortEntities centres them).
C_BaseEntity* Thing(int nId, int nModelType, float x0, float wx, float hy, float hz, float y0 = 0.0f)
{
	s_Things.emplace_back(new C_BaseEntity(nId));
	C_BaseEntity* p = s_Things.back().get();
	p->m_nModelType = nModelType;
	p->m_vecAbsMins.Init(x0, y0 - hy, -hz);
	p->m_vecAbsMaxs.Init(x0 + wx, y0 + hy, hz);
	p->m_vecOrigin.Init(x0, y0, 0.0f);
	p->m_vecMins.Init(0.0f, -hy, -hz);
	p->m_vecMaxs.Init(wx, hy, hz);
	return p;
}

C_BaseEntity* Find(int nId)
{
	for (auto& p : s_Things)
	{
		if (p->m_nId == nId)
			return p.get();
	}
	return NULL;
}

// The answers a thing gives, set before it is registered (so its R row says them).
C_BaseEntity* Transparent(C_BaseEntity* p)
{
	p->m_bTransparent = true;
	return p;
}

C_BaseEntity* BlendTo(C_BaseEntity* p, int n)
{
	p->m_nBlendTo = n;
	return p;
}

C_BaseEntity* GroupOnBlend(C_BaseEntity* p, int g)
{
	p->m_nGroupOnBlend = g;
	return p;
}

C_BaseEntity* Receives(C_BaseEntity* p, int f)
{
	p->m_nReceiveFlags = f;
	return p;
}

// Everything a thing answers, so a replay can build the same one (L2): its
// model type, transparency, two-pass, blend, the blend and group its
// ComputeFxBlend moves to (-1 none), the projected textures it takes, and its
// world box, render box and origin.
void LogDesc(C_BaseEntity* p)
{
	LeafLog(
		"T\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x",
		p->m_nId, p->m_nModelType, p->m_bTransparent ? 1 : 0, p->m_bTwoPass ? 1 : 0, p->m_nFxBlend, p->m_nBlendTo,
		p->m_nGroupOnBlend, p->m_nReceiveFlags, LeafBits(p->m_vecAbsMins.x), LeafBits(p->m_vecAbsMins.y),
		LeafBits(p->m_vecAbsMins.z), LeafBits(p->m_vecAbsMaxs.x), LeafBits(p->m_vecAbsMaxs.y),
		LeafBits(p->m_vecAbsMaxs.z), LeafBits(p->m_vecMins.x), LeafBits(p->m_vecMins.y), LeafBits(p->m_vecMins.z),
		LeafBits(p->m_vecMaxs.x), LeafBits(p->m_vecMaxs.y), LeafBits(p->m_vecMaxs.z), LeafBits(p->m_vecOrigin.x),
		LeafBits(p->m_vecOrigin.y), LeafBits(p->m_vecOrigin.z));
}

void LogThing(const char* pszHow, C_BaseEntity* p, int nGroup)
{
	LeafLog("R\t%d\t%s\t%d\t%d\t%d\t%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%d", p->m_nId, pszHow, nGroup,
			p->m_nModelType, p->m_bTransparent ? 1 : 0, p->m_bTwoPass ? 1 : 0, p->m_nFxBlend,
			LeafBits(p->m_vecAbsMins.x), LeafBits(p->m_vecAbsMins.y), LeafBits(p->m_vecAbsMins.z),
			LeafBits(p->m_vecAbsMaxs.x), LeafBits(p->m_vecAbsMaxs.y), LeafBits(p->m_vecAbsMaxs.z), p->RenderHandle());
}

// A client renderable: AddRenderable, inserted into the tree at PreRender.
C_BaseEntity* Add(C_BaseEntity* p, RenderGroup_t group)
{
	LogDesc(p);
	Subject().AddRenderable(p, group);
	LogThing("add", p, group);
	return p;
}

// A static prop as the engine registers one: CreateRenderableHandle, then
// AddRenderableToLeaves with the leaves its box is in (the SL row, before the
// call, so a replay hands the same leaves).
C_BaseEntity* AddStatic(C_BaseEntity* p)
{
	LogDesc(p);
	Subject().CreateRenderableHandle(p, true);
	LeafLog("CH\t%d\t%d", p->m_nId, p->RenderHandle());
	unsigned short leaves[128];
	int n = 0;
	std::string s;
	for (int i = 0; i < (int)s_LeafBoxes.size() && n < 128; i++)
	{
		if (Overlaps(p->m_vecAbsMins, p->m_vecAbsMaxs, s_LeafBoxes[i]))
		{
			s += (n ? "," : "") + std::to_string(i);
			leaves[n++] = (unsigned short)i;
		}
	}
	LeafLog("SL\t%d\t%s", p->m_nId, s.c_str());
	Subject().AddRenderableToLeaves(p->RenderHandle(), n, leaves);
	LogThing("static", p, Registry().m_Renderables[p->RenderHandle()].m_RenderGroup);
	return p;
}

void Move(int nId, float x0)
{
	C_BaseEntity* p = Find(nId);
	const float wx = p->m_vecAbsMaxs.x - p->m_vecAbsMins.x;
	p->m_vecAbsMins.x = x0;
	p->m_vecAbsMaxs.x = x0 + wx;
	p->m_vecOrigin.x = x0;
	Subject().RenderableChanged(p->RenderHandle());
	LeafLog("MV\t%d\t%08x", nId, LeafBits(x0));
}

void Remove(int nId)
{
	C_BaseEntity* p = Find(nId);
	LeafLog("RM\t%d\t%d", nId, p->RenderHandle());
	Subject().RemoveRenderable(p->RenderHandle());
	LeafLog("RH\t%d\t%d", nId, p->RenderHandle());
}

// The rest of the registry's surface, each call a row before it is made (so
// a replay makes the same call) and its answer, when it has one, a row after.
void SetGroup(int nId, RenderGroup_t group)
{
	LeafLog("RG\t%d\t%d", nId, group);
	Subject().SetRenderGroup(Find(nId)->RenderHandle(), group);
}

void ChangeGroup(int nId, RenderGroup_t group)
{
	LeafLog("CG\t%d\t%d", nId, group);
	// Private in the class; the engine calls it through IClientLeafSystemEngine.
	g_pClientLeafSystem->ChangeRenderableRenderGroup(Find(nId)->RenderHandle(), group);
}

void AltSort(int nId, bool bEnable)
{
	LeafLog("AS\t%d\t%d", nId, bEnable ? 1 : 0);
	Subject().EnableAlternateSorting(Find(nId)->RenderHandle(), bEnable);
}

void StaticProps(bool bEnable)
{
	LeafLog("SP\t%d", bEnable ? 1 : 0);
	Subject().DrawStaticProps(bEnable);
}

void SetConVar(ConVar& var, int nValue)
{
	LeafLog("CV\t%s\t%d", var.GetName(), nValue);
	var.SetValue(nValue);
}

void InPVS(int nId)
{
	LeafLog("PV\t%d", nId);
	const bool b = Subject().IsRenderableInPVS(Find(nId));
	LeafLog("PVA\t%d\t%d", nId, b ? 1 : 0);
}

// GetRenderableLeaf, walked as a caller walks it: the first leaf, then each
// next by the iterator the last call gave.
void WalkLeaves(int nId)
{
	const ClientRenderHandle_t h = Find(nId) ? Find(nId)->RenderHandle() : INVALID_CLIENT_RENDER_HANDLE;
	std::string s;
	int leaf = -1, it = -1;
	bool b = Subject().GetRenderableLeaf(h, &leaf, NULL, &it);
	while (b)
	{
		s += (s.empty() ? "" : ",") + std::to_string(leaf);
		const int in = it;
		b = Subject().GetRenderableLeaf(h, &leaf, &in, &it);
	}
	LeafLog("GL\t%d\t%s", nId, s.c_str());
}

// The leaf's detail range and sub-system data as GetDetailObjectsInLeaf and
// GetSubSystemDataInLeaf answer them, and ShouldDrawDetailObjectsInLeaf over
// every leaf at a frame (decision 3).
void DetailState(int nFrame)
{
	LeafLog("DST\t%d", nFrame);
	for (int leaf = 0; leaf < (int)s_LeafBoxes.size(); leaf++)
	{
		int nFirst = -7, nCount = -7;
		Subject().GetDetailObjectsInLeaf(leaf, nFirst, nCount);
		const bool bData = Subject().GetSubSystemDataInLeaf(leaf, CLSUBSYSTEM_DETAILOBJECTS) != NULL;
		LeafLog("GD\t%d\t%d\t%d\t%d\t%d", leaf, nFirst, nCount, bData ? 1 : 0,
				Subject().ShouldDrawDetailObjectsInLeaf(leaf, nFrame) ? 1 : 0);
	}
}

// A sub-system's data, which says when the leaf system deletes it.
class CLeafSubData : public CClientLeafSubSystemData
{
public:
	explicit CLeafSubData(int nTag)
		: m_nTag(nTag)
	{
	}

	~CLeafSubData() { LeafLog("SDEL\t%d", m_nTag); }

	int m_nTag;
};

void SetSubData(int leaf, int nTag)
{
	LeafLog("DSS\t%d\t%d", leaf, nTag);
	Subject().SetSubSystemDataInLeaf(leaf, CLSUBSYSTEM_DETAILOBJECTS, new CLeafSubData(nTag));
}

void SetDetail(int leaf, int nFirst, int nCount)
{
	LeafLog("DS\t%d\t%d\t%d", leaf, nFirst, nCount);
	Subject().SetDetailObjectsInLeaf(leaf, nFirst, nCount);
}

void DrawDetail(int leaf, int nFrame)
{
	int nFirst = -7, nCount = -7;
	LeafLog("DD\t%d\t%d", leaf, nFrame);
	Subject().DrawDetailObjectsInLeaf(leaf, nFrame, nFirst, nCount);
	LeafLog("DDA\t%d\t%d\t%d", leaf, nFirst, nCount);
}

ClientLeafShadowHandle_t AddShadow(int nUserId, int nFlags)
{
	const ClientLeafShadowHandle_t h = Subject().AddShadow(nUserId, nFlags);
	LeafLog("SH\t%d\t%d\t%d", nUserId, h, nFlags);
	return h;
}

std::string Joined(const int* p, int n)
{
	std::string s;
	for (int i = 0; i < n; i++)
		s += (i ? "," : "") + std::to_string(p[i]);
	return s;
}

void Project(ClientLeafShadowHandle_t h, int n, const int* pLeaves)
{
	LeafLog("PS\t%d\t%s", h, Joined(pLeaves, n).c_str());
	Subject().ProjectShadow(h, n, pLeaves);
}

void ProjectLight(ClientLeafShadowHandle_t h, int n, const int* pLeaves)
{
	LeafLog("PF\t%d\t%s", h, Joined(pLeaves, n).c_str());
	Subject().ProjectFlashlight(h, n, pLeaves);
}

void RemoveShadow(int nUserId, ClientLeafShadowHandle_t h)
{
	LeafLog("SD\t%d\t%d", nUserId, h);
	Subject().RemoveShadow(h);
}

void Frame(int nFrame)
{
	s_Globals.framecount = nFrame;
	s_Globals.curtime = nFrame * 0.015f;
	LeafLog("F\t%d", nFrame);
	Subject().PreRender();
	// Where PreRender put each live renderable: its leaves and area.
	for (auto& p : s_Things)
	{
		const ClientRenderHandle_t h = p->RenderHandle();
		if (h == INVALID_CLIENT_RENDER_HANDLE)
			continue;
		int leaves[128];
		const int n = Subject().GetRenderableLeaves(h, leaves);
		std::string s;
		for (int i = 0; i < n; i++)
			s += (i ? "," : "") + std::to_string(leaves[i]);
		LeafLog("IN\t%d\t%d\t%d\t%s", p->m_nId, h, Registry().m_Renderables[h].m_Area, s.c_str());
	}
}

// LevelShutdownPostEntity, which deletes the sub-systems' data (SDEL).
void LevelShutdown()
{
	LeafLog("LS");
	Subject().LevelShutdownPostEntity();
}

void SetLevel(int nLeaves, float flWidth)
{
	LevelShutdown();
	s_LeafBoxes.clear();
	s_LeafAreas.clear();
	for (int i = 0; i < nLeaves; i++)
	{
		s_LeafBoxes.push_back(MakeBox(i * flWidth, -500, -500, (i + 1) * flWidth, 500, 500));
		s_LeafAreas.push_back(flWidth >= 100.0f ? (i < 6 ? 1 : (i < 10 ? 2 : 3)) : 1);
	}
	Subject().LevelInitPreEntity();
	// The level's leaf count, then (for a replay) the leaves' width along x
	// (each is y and z -500..500) and their areas, in runs.
	LeafLog("LV\t%d\t%08x", nLeaves, LeafBits(flWidth));
	for (int i = 0; i < nLeaves;)
	{
		int j = i;
		while (j + 1 < nLeaves && s_LeafAreas[j + 1] == s_LeafAreas[i])
			++j;
		LeafLog("LA\t%d\t%d\t%d", i, j, s_LeafAreas[i]);
		i = j + 1;
	}
}

// The engine's boxes, for a replay: the view frustum, the areas' portal
// frustums and the occluder.
void LogWorldBoxes()
{
	auto box = [](const char* pszName, const LeafBox_t& b)
	{
		LeafLog("WB\t%s\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x", pszName, LeafBits(b.mins.x), LeafBits(b.mins.y),
				LeafBits(b.mins.z), LeafBits(b.maxs.x), LeafBits(b.maxs.y), LeafBits(b.maxs.z));
	};
	box("frustum", s_Frustum);
	box("area0", s_AreaFrustum[0]);
	box("area1", s_AreaFrustum[1]);
	box("area2", s_AreaFrustum[2]);
	box("area3", s_AreaFrustum[3]);
	box("occluder", s_Occluder);
}

//-----------------------------------------------------------------------------
// A view.
//-----------------------------------------------------------------------------
struct LeafView_t
{
	const char* m_pszTag;
	std::vector<int> m_Leaves;
	int m_nViewID;
	bool m_bTranslucentLeafPass;
	bool m_bDrawTranslucent;
	bool m_bDrawDetail;
	Vector m_vecOrigin;
	Vector m_vecForward;
};

std::vector<int> Range(int a, int b)
{
	std::vector<int> v;
	if (a <= b)
		for (int i = a; i <= b; i++)
			v.push_back(i);
	else
		for (int i = a; i >= b; i--)
			v.push_back(i);
	return v;
}

int s_nRenderFrame = 0;
const int kDetailFrame = 1;

void LogWords(const char* pszTag, const CUtlVector<int32_t>& words)
{
	std::string s = pszTag;
	char sz[16];
	for (int i = 0; i < words.Count(); i++)
	{
		snprintf(sz, sizeof(sz), "\t%08x", (unsigned int)words[i]);
		s += sz;
	}
	LeafLog("%s", s.c_str());
}

void RunView(const LeafView_t& view)
{
	const int nFrame = ++s_nRenderFrame;
	std::vector<LeafIndex_t> leafList(view.m_Leaves.begin(), view.m_Leaves.end());
	std::vector<LeafFogVolume_t> fogList(view.m_Leaves.size(), -1);
	WorldListInfo_t world;
	world.m_ViewFogVolume = -1;
	world.m_LeafCount = (int)leafList.size();
	world.m_pLeafList = leafList.data();
	world.m_pLeafFogVolume = fogList.data();

	// After stock's columns, for a replay: the detail build frame, the view's
	// origin and forward, and its leaves in order.
	LeafLog("V\t%s\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%s", view.m_pszTag, nFrame,
			view.m_nViewID, world.m_LeafCount, view.m_bTranslucentLeafPass ? 1 : 0, view.m_bDrawTranslucent ? 1 : 0,
			view.m_bDrawDetail ? 1 : 0, Registry().m_DrawStaticProps ? 1 : 0, kDetailFrame,
			LeafBits(view.m_vecOrigin.x), LeafBits(view.m_vecOrigin.y), LeafBits(view.m_vecOrigin.z),
			LeafBits(view.m_vecForward.x), LeafBits(view.m_vecForward.y), LeafBits(view.m_vecForward.z),
			Joined(view.m_Leaves.data(), (int)view.m_Leaves.size()).c_str());

	LeafViewRequest_t req;
	req.m_pWorldListInfo = &world;
	req.m_nRenderFrame = nFrame;
	req.m_nDetailBuildFrame = kDetailFrame;
	req.m_nGlobalFrame = gpGlobals->framecount;
	req.m_nViewID = view.m_nViewID;
	req.m_bTranslucentLeafPass = view.m_bTranslucentLeafPass;
	req.m_bDrawTranslucent = view.m_bDrawTranslucent;
	req.m_bDrawDetail = view.m_bDrawDetail;
	req.m_bPortalTestEnts = r_PortalTestEnts.GetBool();
	req.m_bPortalsOpenAll = r_portalsopenall.GetBool();
#ifdef _DEBUG
	req.m_nDrawLeaf = cl_drawleaf.GetInt();
#endif
	req.m_vecOrigin = view.m_vecOrigin;
	req.m_vecForward = view.m_vecForward;

	CLeafViewGather gather;
	{
		// The registry before stock's ComputeTranslucentRenderLeaf, read without
		// the blend upkeep; then stock's pass (which blends); then the read
		// after it, whose blends are done. The buffer is the second read with
		// the first read's render frame and leaf.
		CLeafViewGather before;
		LeafViewRequest_t beforeReq = req;
		beforeReq.m_bTranslucentLeafPass = false;
		g_bQuiet = true;
		EXPECT_TRUE(before.Gather(g_pClientLeafSystem, beforeReq));
		g_bQuiet = false;
		if (view.m_bTranslucentLeafPass)
		{
			LeafLog("CT\t%d\t%d", nFrame, view.m_nViewID);
			Subject().ComputeTranslucentRenderLeaf(world.m_LeafCount, world.m_pLeafList, world.m_pLeafFogVolume, nFrame,
												   view.m_nViewID);
		}
		g_bQuiet = true;
		EXPECT_TRUE(gather.Gather(g_pClientLeafSystem, req));
		g_bQuiet = false;
		EXPECT_EQ(before.m_Handles.Count(), gather.m_Handles.Count());
		const int nRowBase = LEAFVIEW_HEADER_WORDS + LEAFVIEW_LEAF_WORDS * world.m_LeafCount + gather.m_Words[12];
		for (int r = 0; r < gather.m_Handles.Count() && r < before.m_Handles.Count(); r++)
		{
			EXPECT_EQ(before.m_Handles[r], gather.m_Handles[r]);
			const int at = nRowBase + LEAFVIEW_RENDERABLE_WORDS * r;
			gather.m_Words[at + 3] = before.m_Words[at + 3];
			gather.m_Words[at + 4] = before.m_Words[at + 4];
		}
	}
	LogWords("VB", gather.m_Words);

	CClientRenderablesList* pList = new CClientRenderablesList;
	for (int g = 0; g < RENDER_GROUP_COUNT; g++)
		pList->m_RenderGroupCounts[g] = 0;
	SetupRenderInfo_t info;
	info.m_pWorldListInfo = &world;
	info.m_pRenderList = pList;
	info.m_vecRenderOrigin = view.m_vecOrigin;
	info.m_vecRenderForward = view.m_vecForward;
	info.m_nRenderFrame = nFrame;
	info.m_nDetailBuildFrame = kDetailFrame;
	info.m_flRenderDistSq = 0.0f;
	info.m_bDrawDetailObjects = view.m_bDrawDetail;
	info.m_bDrawTranslucentObjects = view.m_bDrawTranslucent;
	Subject().BuildRenderablesList(info);

	// What stock built: each group in order. A source is the buffer's
	// renderable row (r), its detail model row (d), or the leaf's marker (-).
	for (int g = 0; g < RENDER_GROUP_COUNT; g++)
	{
		for (int i = 0; i < pList->m_RenderGroupCounts[g]; i++)
		{
			const CClientRenderablesList::CEntry& e = pList->m_RenderGroups[g][i];
			char szSource[32] = "-\t-1";
			if (e.m_pRenderable && e.m_RenderHandle != DETAIL_PROP_RENDER_HANDLE)
			{
				int nRow = -1;
				for (int r = 0; r < gather.m_Handles.Count(); r++)
					if (gather.m_Handles[r] == e.m_RenderHandle)
						nRow = r;
				snprintf(szSource, sizeof(szSource), "r\t%d", nRow);
			}
			else if (e.m_pRenderable)
			{
				int nRow = -1;
				for (int d = 0; d < gather.m_Details.Count(); d++)
					if (gather.m_Details[d] == e.m_pRenderable)
						nRow = d;
				snprintf(szSource, sizeof(szSource), "d\t%d", nRow);
			}
			LeafLog("O\t%d\t%d\t%s\t%d\t%d\t%d\t%d", g, i, szSource, IdOf(e.m_pRenderable), e.m_iWorldListInfoLeaf,
					e.m_TwoPass, e.m_RenderHandle);
		}
	}
	// And the registry state the buffer's rows are left with.
	for (int r = 0; r < gather.m_Handles.Count(); r++)
	{
		const CLeafRegistryMirror::RenderableInfo_t& ri = Registry().m_Renderables[gather.m_Handles[r]];
		LeafLog("ST\t%d\t%d\t%d\t%d\t%d", r, IdOf(ri.m_pRenderable), ri.m_RenderFrame, (int)ri.m_RenderLeaf,
				ri.m_RenderFrame2);
	}
	pList->Release();
	LeafLog("VE\t%s", view.m_pszTag);
}

void RunViewModels()
{
	LeafLog("VM\t%d", gpGlobals->framecount);
	CUtlVector<IClientRenderable*> opaque, translucent;
	s_ViewModelCapture.clear();
	s_bViewModelCapture = true;
	Subject().CollateViewModelRenderables(opaque, translucent);
	s_bViewModelCapture = false;

	CUtlVector<int32_t> words;
	words.AddToTail((int32_t)s_ViewModelCapture.size());
	std::string ids = "VMI";
	for (const auto& c : s_ViewModelCapture)
	{
		words.AddToTail(c.second);
		ids += "\t" + std::to_string(c.first);
	}
	LogWords("VMB", words);
	LeafLog("%s", ids.c_str());
	std::string o = "VML\to", t = "VML\tt";
	for (int i = 0; i < opaque.Count(); i++)
		o += "\t" + std::to_string(IdOf(opaque[i]));
	for (int i = 0; i < translucent.Count(); i++)
		t += "\t" + std::to_string(IdOf(translucent[i]));
	LeafLog("%s", o.c_str());
	LeafLog("%s", t.c_str());
}

void EnumShadows(std::vector<int> leaves)
{
	class CEnum : public IClientLeafShadowEnum
	{
	public:
		virtual void EnumShadow(ClientShadowHandle_t userId) { LeafLog("EN\t%d", userId); }
	} e;

	std::string s;
	for (size_t i = 0; i < leaves.size(); i++)
		s += (i ? "," : "") + std::to_string(leaves[i]);
	LeafLog("ES\t%s", s.c_str());
	std::vector<LeafIndex_t> list(leaves.begin(), leaves.end());
	Subject().EnumerateShadowsInLeaves((int)list.size(), list.data(), &e);
}

//-----------------------------------------------------------------------------
// The scenario.
//-----------------------------------------------------------------------------
std::string RunScenario()
{
	EXPECT_TRUE(ThreadInMainThread()) << "the subject inserts on the main thread only";
	g_pCVar = &g_LeafCvar;
	g_Log.clear();
	s_nRenderFrame = 0;
	s_Globals.framecount = 0;
	s_Things.clear();
	s_DetailModels.clear();
	LeafLog("# leafsystem-stock.tsv: written by src/test/oracle/native/leafsystem_oracle_test.cpp");
	LeafLog("# from the pristine game/client/clientleafsystem.cpp; README-leafsystem-stock.md says how to read it.");

	StaticProps(true);
	SetConVar(r_PortalTestEnts, 1);
	SetConVar(r_portalsopenall, 0);
#ifdef _DEBUG
	SetConVar(cl_drawleaf, -1);
#endif

	SetLevel(12, 100.0f);
	s_Frustum = MakeBox(-1000, -400, -400, 2000, 400, 400);
	s_AreaFrustum[0] = MakeBox(0, 0, 0, 0, 0, 0);
	s_AreaFrustum[1] = MakeBox(-1000, -500, -500, 2000, 500, 500);
	s_AreaFrustum[2] = MakeBox(-1000, -200, -500, 2000, 200, 500);
	s_AreaFrustum[3] = MakeBox(-1000, -500, -500, 2000, 500, 500);
	s_Occluder = MakeBox(1000, -100, -100, 1100, 100, 100);
	LogWorldBoxes();

	const int kBrush = 1, kSprite = 2, kStudio = 3;

	// Opaque entities, one per size bucket, and one across two leaves.
	Add(Receives(Thing(1, kStudio, 10, 10, 5, 5), SHADOW_FLAGS_PROJECTED_TEXTURE_TYPE_MASK),
		RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(2, kStudio, 20, 10, 25, 5), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(3, kStudio, 120, 10, 5, 50), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(4, kStudio, 130, 10, 5, 150), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(5, kStudio, 250, 100, 10, 10), RENDER_GROUP_OPAQUE_ENTITY);
	// Exactly on each bucket's threshold (30, 80 and 200 tall): stock's >=.
	Add(Thing(45, kStudio, 810, 10, 5, 15), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(46, kStudio, 830, 10, 5, 40), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(47, kStudio, 850, 10, 5, 100), RENDER_GROUP_OPAQUE_ENTITY);
	// Invisible: its first blend is 0.
	Add(BlendTo(Thing(6, kStudio, 30, 10, 5, 5), 0), RENDER_GROUP_OPAQUE_ENTITY);
	// A brush model, which takes shadows.
	Add(Receives(Thing(7, kBrush, 40, 10, 5, 5), SHADOW_FLAGS_SHADOW), RENDER_GROUP_OPAQUE_BRUSH);
	// Across areas 1 and 2 (area -1): the main frustum; one outside it.
	Add(Thing(8, kStudio, 560, 80, 15, 5, 465), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(9, kStudio, 570, 60, 5, 5), RENDER_GROUP_OPAQUE_ENTITY);
	// In area 2: outside its portal frustum (inside the main one), and inside.
	Add(Thing(10, kStudio, 710, 10, 20, 5, 300), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(11, kStudio, 720, 10, 5, 5), RENDER_GROUP_OPAQUE_ENTITY);
	// Behind the occluder: a studio model, a brush model and a sprite.
	Add(Thing(12, kStudio, 1010, 10, 10, 10), RENDER_GROUP_OPAQUE_ENTITY);
	Add(Thing(13, kBrush, 1030, 10, 10, 10), RENDER_GROUP_OPAQUE_BRUSH);
	Add(Thing(14, kSprite, 1050, 10, 10, 10), RENDER_GROUP_OTHER);
	// View models: two in the leaves, as a view model is, and three out of
	// every leaf, whose blends move their groups only at the split.
	Add(Thing(15, kStudio, 60, 10, 5, 5), RENDER_GROUP_VIEW_MODEL_OPAQUE);
	Add(Thing(16, kStudio, 62, 10, 5, 5), RENDER_GROUP_VIEW_MODEL_TRANSLUCENT);
	Add(GroupOnBlend(Thing(17, kStudio, 5000, 10, 5, 5), RENDER_GROUP_OPAQUE_ENTITY), RENDER_GROUP_VIEW_MODEL_OPAQUE);
	Add(GroupOnBlend(Thing(18, kStudio, 5020, 10, 5, 5), RENDER_GROUP_TRANSLUCENT_ENTITY),
		RENDER_GROUP_VIEW_MODEL_OPAQUE);
	Add(GroupOnBlend(Thing(19, kStudio, 5040, 10, 5, 5), RENDER_GROUP_VIEW_MODEL_OPAQUE),
		RENDER_GROUP_VIEW_MODEL_TRANSLUCENT);
	// Six translucent boxes in leaf 3, for the sort; render bounds off-centre.
	const float sortX[6] = {390, 310, 350, 330, 370, 320};
	for (int i = 0; i < 6; i++)
		Add(Transparent(Thing(20 + i, kStudio, sortX[i], 5 + i, 5, 5)), RENDER_GROUP_TRANSLUCENT_ENTITY);
	// Translucent across leaves 2-4, nearest-leaf and alternately sorted.
	Add(Transparent(Thing(26, kStudio, 250, 200, 5, 5)), RENDER_GROUP_TRANSLUCENT_ENTITY);
	Add(Transparent(Thing(27, kStudio, 255, 190, 5, 5)), RENDER_GROUP_TRANSLUCENT_ENTITY);
	AltSort(27, true);
	// Two-pass at 255 and at 128.
	Add(Transparent(Thing(28, kStudio, 410, 10, 5, 5)), RENDER_GROUP_TWOPASS);
	Add(BlendTo(Transparent(Thing(29, kStudio, 420, 10, 5, 5)), 128), RENDER_GROUP_TWOPASS);
	// Blends that move the render group, as a temp entity's do.
	Add(GroupOnBlend(Thing(30, kStudio, 430, 10, 5, 5), RENDER_GROUP_OPAQUE_ENTITY), RENDER_GROUP_TRANSLUCENT_ENTITY);
	Add(GroupOnBlend(Thing(31, kStudio, 440, 10, 5, 5), RENDER_GROUP_TRANSLUCENT_ENTITY), RENDER_GROUP_OPAQUE_ENTITY);
	// Translucent in leaf 1 beside the detail objects; and one in leaf 9.
	Add(Transparent(Thing(32, kStudio, 150, 10, 5, 5)), RENDER_GROUP_TRANSLUCENT_ENTITY);
	Add(Transparent(Thing(33, kStudio, 950, 10, 5, 5)), RENDER_GROUP_TRANSLUCENT_ENTITY);
	// Static props, as the engine adds them: one translucent, four opaque
	// sizes, one of which takes flashlights.
	AddStatic(Transparent(Thing(34, kStudio, 510, 10, 5, 5)));
	AddStatic(Receives(Thing(40, kStudio, 610, 10, 5, 5), SHADOW_FLAGS_FLASHLIGHT));
	AddStatic(Thing(41, kStudio, 620, 50, 5, 5));
	AddStatic(Thing(42, kStudio, 680, 10, 50, 5));
	AddStatic(Thing(43, kStudio, 690, 5, 5, 150));
	// A translucent two-pass static prop.
	C_BaseEntity* pStaticTwo = Thing(44, kStudio, 520, 10, 5, 5);
	pStaticTwo->m_bTransparent = true;
	pStaticTwo->m_bTwoPass = true;
	AddStatic(pStaticTwo);

	// Detail objects: leaf 1's are drawn this frame, leaf 7's are not, and
	// leaf 8 has only a sub-system's data.
	const int detailSpec[6][3] = {{-1, 0, 0},   {1001, 0, 255}, {1002, 1, 200},
								  {1003, 1, 0}, {1004, 0, 255}, {1005, 1, 100}};
	for (int i = 0; i < 6; i++)
	{
		if (detailSpec[i][0] < 0)
		{
			s_DetailModels.push_back(NULL);
			LeafLog("DM\t%d\t-1", i);
			continue;
		}
		C_BaseEntity* p = Thing(detailSpec[i][0], kStudio, 160 + 5 * i, 2, 2, 2);
		p->m_bTransparent = detailSpec[i][1] != 0;
		p->m_nFxBlend = detailSpec[i][2];
		s_DetailModels.push_back(p);
		LogDesc(p);
		LeafLog("DM\t%d\t%d", i, p->m_nId);
	}
	SetDetail(1, 0, 4);
	SetDetail(7, 4, 2);
	SetSubData(8, 1);
	DrawDetail(1, kDetailFrame);
	DrawDetail(7, kDetailFrame + 7);
	DrawDetail(8, kDetailFrame);

	const Vector fwd(1, 0, 0), back(-1, 0, 0);
	const Vector front(-100, 0, 0), behind(1300, 0, 0);

	// FRAME 1: the main view front to back, the shadow depth view, the view
	// models; shadows projected and enumerated.
	Frame(1);
	ClientLeafShadowHandle_t sh0 = AddShadow(100, SHADOW_FLAGS_SHADOW);
	int shadowLeaves[] = {0, 1};
	Project(sh0, 2, shadowLeaves);
	ClientLeafShadowHandle_t sh1 = AddShadow(101, SHADOW_FLAGS_FLASHLIGHT);
	int flashLeaves[] = {5, 6, 7};
	ProjectLight(sh1, 3, flashLeaves);
	EnumShadows({0, 1, 5, 6});

	RunView({"main", Range(0, 11), VIEW_MAIN, true, true, true, front, fwd});
	RunView({"shadowdepth", Range(0, 11), VIEW_SHADOW_DEPTH_TEXTURE, false, false, false, front, fwd});
	RunViewModels();

	// FRAME 2: one renderable moves under the flashlight, one goes and its
	// handle is reused; the main view from the far end, twice; a reflection
	// over three leaves; static props off.
	Move(1, 610);
	Remove(2);
	Add(Thing(50, kStudio, 15, 10, 5, 5), RENDER_GROUP_OPAQUE_ENTITY);
	Frame(2);
	RunView({"main-back", Range(11, 0), VIEW_MAIN, true, true, true, behind, back});
	RunView({"main-back-again", Range(11, 0), VIEW_MAIN, true, true, true, behind, back});
	RunView({"reflection", {3, 2, 4}, VIEW_REFLECTION, true, true, true, front, fwd});
	StaticProps(false);
	RunView({"nostaticprops", Range(0, 11), VIEW_MAIN, true, true, true, front, fwd});
	StaticProps(true);
	RunViewModels();
	RemoveShadow(100, sh0);
	EnumShadows({0, 1, 5, 6});
	EnumShadows({});

	// FRAME 3: the portal ConVars, no detail objects, and a shadow depth view
	// over leaves whose translucent renderables were last placed elsewhere.
	Frame(3);
	SetConVar(r_PortalTestEnts, 0);
	RunView({"noportaltest", Range(0, 11), VIEW_MAIN, true, true, true, front, fwd});
	SetConVar(r_PortalTestEnts, 1);
	SetConVar(r_portalsopenall, 1);
	RunView({"portalsopenall", Range(0, 11), VIEW_MAIN, true, true, true, front, fwd});
	SetConVar(r_portalsopenall, 0);
	RunView({"nodetail", Range(0, 11), VIEW_MAIN, true, true, false, front, fwd});
	RunView({"detail-opaque-only", Range(0, 11), VIEW_MONITOR, false, false, true, front, fwd});
	RunView({"shadowdepth-back", Range(4, 2), VIEW_SHADOW_DEPTH_TEXTURE, false, false, false, behind, back});
#ifdef _DEBUG
	SetConVar(cl_drawleaf, 3);
	RunView({"drawleaf", Range(0, 11), VIEW_MAIN, true, true, true, front, fwd});
	SetConVar(cl_drawleaf, -1);
#endif
	RunViewModels();

	// FRAME 4, THE REGISTRY (L2): a shadow whose flags carry more than its
	// kind, projected over leaves 0-2; two static props added into leaf 1
	// after it, of which only the first takes it (AddRenderableToLeaves does
	// not advance the shadow enumerator, and the first one's add stamped the
	// shadow); a brush model moved inside it (its receivers cleared and added
	// again); a renderable moved and removed in the same frame (it leaves the
	// dirty list); render groups into and out of the view models' list, two-pass
	// on and off; a static prop the engine fades to translucent; alternate
	// sorting off; the shadow projected elsewhere; the PVS, a renderable's
	// leaves walked, the detail state per leaf, and the sub-systems' data
	// replaced.
	ClientLeafShadowHandle_t sh2 = AddShadow(102, SHADOW_FLAGS_SHADOW | (SHADOW_FLAGS_LAST_FLAG << 1));
	int shadow2Leaves[] = {0, 1, 2};
	Project(sh2, 3, shadow2Leaves);
	AddStatic(Receives(Thing(48, kStudio, 170, 10, 5, 5), SHADOW_FLAGS_SHADOW));
	AddStatic(Receives(Thing(49, kStudio, 180, 10, 5, 5), SHADOW_FLAGS_SHADOW));
	EnumShadows({0, 1, 2, 5});
	Move(7, 45);
	Move(3, 125);
	Remove(3);
	SetGroup(9, RENDER_GROUP_VIEW_MODEL_OPAQUE);
	SetGroup(15, RENDER_GROUP_OPAQUE_ENTITY);
	SetGroup(32, RENDER_GROUP_TWOPASS);
	SetGroup(28, RENDER_GROUP_TRANSLUCENT_ENTITY);
	ChangeGroup(41, RENDER_GROUP_TRANSLUCENT_ENTITY);
	AltSort(27, false);
	Frame(4);
	int shadow2Elsewhere[] = {4, 5};
	Project(sh2, 2, shadow2Elsewhere);
	EnumShadows({0, 1, 2, 4, 5});
	RunView({"registry", Range(0, 11), VIEW_MAIN, true, true, true, front, fwd});
	RunViewModels();
	InPVS(1);
	InPVS(50);
	InPVS(40);
	InPVS(33);
	InPVS(2);
	WalkLeaves(26);
	WalkLeaves(1);
	WalkLeaves(2);
	DetailState(kDetailFrame);
	DetailState(kDetailFrame + 7);
	SetSubData(8, 2);
	SetSubData(1, 3);
	DetailState(kDetailFrame);
	RemoveShadow(102, sh2);
	EnumShadows({0, 1, 2, 4, 5});

	// A LEVEL OF 4100 LEAVES: the markers alone overflow the opaque groups.
	// Its change deletes the sub-systems' data, and a handle from the old
	// level, removed after it, names nothing.
	const ClientRenderHandle_t hStale = Find(33)->RenderHandle();
	s_Things.clear();
	s_DetailModels.clear();
	SetLevel(4100, 10.0f);
	Add(Thing(60, kStudio, 5, 2, 2, 2), RENDER_GROUP_OPAQUE_ENTITY);
	LeafLog("RMS\t%d", hStale);
	Subject().RemoveRenderable(hStale);
	Frame(5);
	RunView({"overflow", Range(0, 4099), VIEW_MAIN, true, true, true, front, fwd});

	LevelShutdown();
	s_Things.clear();
	LeafLog("END");
	return g_Log;
}

std::string RepoFile(const char* pszRel)
{
	const char* pszRepo = getenv("SOURCESHARP_REPO");
	return std::string(pszRepo ? pszRepo : ".") + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoFile("src/test/client/Fixtures/leafsystem-stock.tsv");
}

std::string MarkerPath()
{
	return RepoFile("src/test/oracle/native/leafsystem_golden.regen");
}

std::vector<std::string> Lines(const std::string& s)
{
	std::vector<std::string> v;
	std::istringstream in(s);
	std::string line;
	while (std::getline(in, line))
		v.push_back(line);
	return v;
}

std::vector<std::string> Fields(const std::string& line)
{
	std::vector<std::string> v;
	size_t at = 0;
	while (true)
	{
		const size_t tab = line.find('\t', at);
		v.push_back(line.substr(at, tab == std::string::npos ? std::string::npos : tab - at));
		if (tab == std::string::npos)
			break;
		at = tab + 1;
	}
	return v;
}

} // namespace

TEST(LeafSystemOracle, StockProducesTheGolden)
{
	SS_SKIP_ON_CI(
		"a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit");
	const std::string run = RunScenario();

	if (std::ifstream(MarkerPath()).good())
	{
		std::ofstream out(GoldenPath());
		out << run;
	}

	std::ifstream in(GoldenPath());
	ASSERT_TRUE(in.good()) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();

	ASSERT_GT(golden.size(), 100000u) << "the golden is empty or truncated";

	const std::vector<std::string> a = Lines(run), b = Lines(golden);
	for (size_t n = 0; n < a.size() || n < b.size(); n++)
	{
		ASSERT_TRUE(n < a.size() && n < b.size() && a[n] == b[n])
			<< "line " << n + 1 << ": stock now writes\n  " << (n < a.size() ? a[n].substr(0, 300) : "<end>")
			<< "\nand the golden has\n  " << (n < b.size() ? b[n].substr(0, 300) : "<end>")
			<< "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

//-----------------------------------------------------------------------------
// THE SCENARIO REACHES EVERY ARM of CollateRenderablesInLeaf (decision 1),
// ComputeTranslucentRenderLeaf and SortEntities (2) and
// CollateViewModelRenderables (5), each a non-zero number of times. An arm is
// named from the buffer's state in stock's order of tests, and each naming is
// held to what stock did: a culled occurrence is absent from its leaf's
// entries, a kept one present, and the engine was asked (or not) as the arm
// says. The render-distance cutoff under INVASION_CLIENT_DLL is not compiled
// in this game, so it has no arm here.
//-----------------------------------------------------------------------------
namespace
{

struct ViewRun_t
{
	std::vector<int32_t> words;
	std::vector<std::vector<std::string>> outs; // O rows
	std::vector<std::vector<std::string>> queries; // Q rows (stock's, during the build)
	std::vector<std::vector<std::string>> blends; // FB rows (stock's, during the build)
	std::vector<std::string> np; // NP rows
	std::map<int, std::vector<int>> state; // row -> frame, leaf, frame2 after
};

std::vector<int32_t> Words(const std::vector<std::string>& f)
{
	std::vector<int32_t> w;
	for (size_t i = 1; i < f.size(); i++)
		w.push_back((int32_t)strtoul(f[i].c_str(), NULL, 16));
	return w;
}

std::vector<ViewRun_t> ParseViews(const std::string& run)
{
	std::vector<ViewRun_t> views;
	bool bIn = false;
	for (const std::string& line : Lines(run))
	{
		const std::vector<std::string> f = Fields(line);
		if (f[0] == "VB")
		{
			views.emplace_back();
			views.back().words = Words(f);
			bIn = true;
		}
		else if (!bIn)
			continue;
		else if (f[0] == "O")
			views.back().outs.push_back(f);
		else if (f[0] == "Q")
			views.back().queries.push_back(f);
		else if (f[0] == "FB")
			views.back().blends.push_back(f);
		else if (f[0] == "NP")
			views.back().np.push_back(line);
		else if (f[0] == "ST")
			views.back().state[atoi(f[1].c_str())] = {atoi(f[3].c_str()), atoi(f[4].c_str()), atoi(f[5].c_str())};
		else if (f[0] == "VE")
			bIn = false;
	}
	return views;
}

float F(int32_t w)
{
	float f;
	memcpy(&f, &w, sizeof(f));
	return f;
}

} // namespace

TEST(LeafSystemOracle, TheScenarioReachesEveryArm)
{
	const std::string run = RunScenario();
	const std::vector<ViewRun_t> views = ParseViews(run);
	ASSERT_GE(views.size(), 10u);

	std::map<std::string, int> arms;
	int nMisnamed = 0;
	for (const ViewRun_t& v : views)
		try
		{
			const std::vector<int32_t>& w = v.words;
			const int nFrame = w.at(1), nFlags = w.at(2), nDrawLeaf = w.at(3);
			const bool bTranslucent = (nFlags & 1) != 0, bDetail = (nFlags & 2) != 0, bStatic = (nFlags & 4) != 0;
			const bool bPortal = (nFlags & 8) && !(nFlags & 16);
			const int L = w.at(10), R = w.at(11), E = w.at(12);
			const int leafAt = LEAFVIEW_HEADER_WORDS, occAt = leafAt + LEAFVIEW_LEAF_WORDS * L, rowAt = occAt + E;
			const int detailAt = rowAt + LEAFVIEW_RENDERABLE_WORDS * R;
			auto row = [&](int r, int k)
			{
				return w.at(rowAt + LEAFVIEW_RENDERABLE_WORDS * r + k);
			};

			// Stock's entries for (leaf index, row) and its engine questions by id.
			std::set<std::pair<int, int>> kept;
			std::set<std::pair<int, int>> keptDetail;
			int nMarkers = 0;
			for (const auto& o : v.outs)
			{
				if (o[3] == "r")
					kept.insert({atoi(o[6].c_str()), atoi(o[4].c_str())});
				else if (o[3] == "d")
					keptDetail.insert({atoi(o[6].c_str()), atoi(o[4].c_str())});
				else
					++nMarkers;
			}
			if (nMarkers > 0)
				arms["the leaves' empty markers"]++;
			if (!v.np.empty())
				arms["a group overflowed and the console said so"]++;

			std::set<int> seen;
			for (int i = 0; i < L; i++)
			{
				const int leaf = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i);
				const int first = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 1),
						  count = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 2);
				for (int k = 0; k < count; k++)
				{
					const int r = w.at(occAt + first + k);
					const int group = row(r, 1), flags = row(r, 2), frame2 = row(r, 5), area = row(r, 6);
					const int alpha = row(r, 7), answers = row(r, 8);
					const int renderLeafAfter = v.state.count(r) ? v.state.at(r)[1] : -1;
					const bool bKept = kept.count({i, r}) != 0;
					const bool bIsTranslucent = group == RENDER_GROUP_TRANSLUCENT_ENTITY;
					std::string arm;
					bool bExpectKept = false;
					if (!bStatic && (flags & 2))
						arm = "a static prop with static props off";
					else if (!bIsTranslucent && (seen.count(r) || frame2 == nFrame))
						arm = "an opaque renderable already met this frame";
					else if (bIsTranslucent && renderLeafAfter != leaf)
						arm = "a translucent renderable outside its render leaf";
					else if (bTranslucent && alpha == 0)
						arm = "alpha 0 while drawing translucent objects";
					else if (bPortal && area != -1 && !(answers & LEAFVIEW_ANSWER_AREA))
						arm = "outside its area's portal frustum";
					else if (!(bPortal && area != -1) && (answers & LEAFVIEW_ANSWER_CULLED))
						arm = "outside the main frustum";
					else if ((flags & 8) && (answers & LEAFVIEW_ANSWER_OCCLUDED))
						arm = "a studio model the occlusion system hides";
					else
					{
						bExpectKept = true;
						if (bPortal && area != -1)
							arms["inside its area's portal frustum"]++;
						else
							arms["inside the main frustum"]++;
						if (!(flags & 8) && (answers & LEAFVIEW_ANSWER_OCCLUDED))
							arms["occluded but not a studio model, so drawn"]++;
						if ((flags & 8))
							arms["a studio model the occlusion system shows"]++;
						if (!bIsTranslucent && group >= RENDER_GROUP_OPAQUE_STATIC &&
							group <= RENDER_GROUP_OPAQUE_ENTITY)
						{
							const float d = std::max(
								std::max(fabsf(F(row(r, 12)) - F(row(r, 9))), fabsf(F(row(r, 13)) - F(row(r, 10)))),
								fabsf(F(row(r, 14)) - F(row(r, 11))));
							const int bucket = d >= 200.f ? 0 : d >= 80.f ? 1 : d >= 30.f ? 2 : 3;
							arm = std::string(group == RENDER_GROUP_OPAQUE_STATIC ? "static" : "entity") +
								" size bucket " + std::to_string(bucket);
						}
						else if (!bIsTranslucent)
							arm = "an opaque group with no size buckets";
						else if (!bTranslucent)
							arm = (flags & 1) ? "two-pass, opaque half only, translucent off"
											  : "translucent, not drawn with translucent off";
						else if ((flags & 1) && alpha == 255)
							arm = "two-pass at 255: translucent and opaque";
						else if (flags & 1)
							arm = "two-pass below 255: translucent only";
						else
							arm = "translucent, drawn";
					}
					if (!bIsTranslucent)
						seen.insert(r);
					// "translucent, not drawn" is kept in no list; the rest of the
					// kept arms are in the leaf's entries, unless cl_drawleaf (a
					// debug build's filter on the world list's leaf index) drops it.
					bool bShouldBeListed = bExpectKept && arm != "translucent, not drawn with translucent off";
					if (bShouldBeListed && nDrawLeaf >= 0 && i != nDrawLeaf)
					{
						bShouldBeListed = false;
						arms["cl_drawleaf drops an entry outside its leaf"]++;
					}
					if (bKept != bShouldBeListed)
					{
						++nMisnamed;
						ADD_FAILURE() << "view frame " << nFrame << " leaf " << leaf << " row " << r << ": named '"
									  << arm << "' but stock " << (bKept ? "kept" : "dropped") << " it";
					}
					arms[arm]++;
				}

				// The detail objects.
				const int should = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 3);
				const int dFirst = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 4),
						  dCount = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 5);
				if (!should && leaf == 7)
					arms["detail objects in a leaf not drawn this frame"]++;
				if (should && dCount == 0)
					arms["a leaf with a sub-system's data and no detail objects"]++;
				for (int d = dFirst; d < dFirst + dCount; d++)
				{
					const int* pd = &w.at(detailAt + LEAFVIEW_DETAIL_WORDS * d + LEAFVIEW_DETAIL_WORDS - 1) -
						(LEAFVIEW_DETAIL_WORDS - 1);
					const bool bListed = keptDetail.count({i, d}) != 0;
					std::string arm;
					bool bExpect = false;
					if (!bDetail)
						arm = "detail objects off";
					else if (!pd[0])
						arm = "a detail slot with no model";
					else if (pd[1] && !bTranslucent)
						arm = "a translucent detail model, translucent off";
					else if (pd[1] && pd[2] <= 0)
						arm = "a translucent detail model at alpha 0";
					else
					{
						bExpect = nDrawLeaf < 0 || i == nDrawLeaf;
						arm = pd[1] ? "a translucent detail model, drawn" : "an opaque detail model";
					}
					if (bListed != bExpect)
					{
						++nMisnamed;
						ADD_FAILURE() << "view frame " << nFrame << " detail row " << d << ": named '" << arm << "'";
					}
					arms[arm]++;
				}
			}

			// The sort: a leaf whose translucent entries stock did not leave in the
			// order it added them.
			std::map<int, std::vector<int>> translucentByLeaf;
			for (const auto& o : v.outs)
				if (atoi(o[1].c_str()) == RENDER_GROUP_TRANSLUCENT_ENTITY && o[3] == "r")
					translucentByLeaf[atoi(o[6].c_str())].push_back(atoi(o[4].c_str()));
			for (const auto& kv : translucentByLeaf)
			{
				std::vector<int> added;
				const int first = w.at(leafAt + LEAFVIEW_LEAF_WORDS * kv.first + 1),
						  count = w.at(leafAt + LEAFVIEW_LEAF_WORDS * kv.first + 2);
				for (int k = 0; k < count; k++)
					for (int r : kv.second)
						if (r == w.at(occAt + first + k))
							added.push_back(r);
				if (kv.second.size() > 1 && added != kv.second)
					arms["the back-to-front sort reordered a leaf"]++;
			}

			// The translucent render leaf: a renderable met first in one leaf and
			// placed in another (the alternate sorting), and a nearest-leaf one.
			if (nFlags & 32)
			{
				for (int r = 0; r < R; r++)
				{
					if (row(r, 1) != RENDER_GROUP_TRANSLUCENT_ENTITY || !v.state.count(r))
						continue;
					int firstLeaf = -1, lastLeaf = -1;
					for (int i = 0; i < L; i++)
					{
						const int first = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 1),
								  count = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i + 2);
						for (int k = 0; k < count; k++)
							if (w.at(occAt + first + k) == r)
							{
								if (firstLeaf < 0)
									firstLeaf = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i);
								lastLeaf = w.at(leafAt + LEAFVIEW_LEAF_WORDS * i);
							}
					}
					if (firstLeaf == lastLeaf)
						continue;
					if (v.state.at(r)[1] == lastLeaf && (row(r, 2) & 0x20))
						arms["alternate sorting: the furthest leaf"]++;
					else if (v.state.at(r)[1] == firstLeaf)
						arms["the nearest leaf"]++;
				}
			}
		}
		catch (const std::exception& e)
		{
			ADD_FAILURE() << "view frame " << v.words.at(1) << " (" << v.words.size() << " words, L " << v.words.at(10)
						  << " R " << v.words.at(11) << " E " << v.words.at(12) << "): " << e.what();
		}

	// The view models: each group the split sees, and the blend that moved one.
	int nViewModelOpaque = 0, nViewModelTranslucent = 0, nMovedOut = 0;
	for (const std::string& line : Lines(run))
	{
		const std::vector<std::string> f = Fields(line);
		if (f[0] != "VMB")
			continue;
		const std::vector<int32_t> w = Words(f);
		for (int i = 1; i <= w.at(0); i++)
		{
			if (w.at(i) == RENDER_GROUP_VIEW_MODEL_OPAQUE)
				++nViewModelOpaque;
			else if (w.at(i) == RENDER_GROUP_OPAQUE_ENTITY)
				++nMovedOut;
			else
				++nViewModelTranslucent;
		}
	}
	arms["a view model, opaque"] += nViewModelOpaque;
	arms["a view model, translucent"] += nViewModelTranslucent;
	arms["a view model whose blend made it an opaque entity"] += nMovedOut;

	const char* kArms[] = {
		"the leaves' empty markers",
		"a group overflowed and the console said so",
		"a static prop with static props off",
		"an opaque renderable already met this frame",
		"a translucent renderable outside its render leaf",
		"alpha 0 while drawing translucent objects",
		"outside its area's portal frustum",
		"inside its area's portal frustum",
		"outside the main frustum",
		"inside the main frustum",
		"a studio model the occlusion system hides",
		"a studio model the occlusion system shows",
		"occluded but not a studio model, so drawn",
		"static size bucket 0",
		"static size bucket 1",
		"static size bucket 2",
		"static size bucket 3",
		"entity size bucket 0",
		"entity size bucket 1",
		"entity size bucket 2",
		"entity size bucket 3",
		"an opaque group with no size buckets",
		"translucent, drawn",
		"translucent, not drawn with translucent off",
		"two-pass at 255: translucent and opaque",
		"two-pass below 255: translucent only",
		"two-pass, opaque half only, translucent off",
		"detail objects off",
		"detail objects in a leaf not drawn this frame",
		"a leaf with a sub-system's data and no detail objects",
		"a detail slot with no model",
		"a translucent detail model, translucent off",
		"a translucent detail model at alpha 0",
		"a translucent detail model, drawn",
		"an opaque detail model",
		"the back-to-front sort reordered a leaf",
		"alternate sorting: the furthest leaf",
		"the nearest leaf",
		"a view model, opaque",
		"a view model, translucent",
		"a view model whose blend made it an opaque entity",
#ifdef _DEBUG
		"cl_drawleaf drops an entry outside its leaf",
#endif
	};
	for (const char* pszArm : kArms)
		EXPECT_GT(arms[pszArm], 0) << "the scenario never reaches: " << pszArm;
	if (getenv("SS_LEAF_ARMS"))
		for (const auto& kv : arms)
			printf("arm\t%d\t%s\n", kv.second, kv.first.c_str());
	EXPECT_EQ(0, nMisnamed);
#ifdef _DEBUG
	EXPECT_NE(run.find("V\tdrawleaf\t"), std::string::npos) << "the cl_drawleaf view did not run";
#endif
}

//-----------------------------------------------------------------------------
// THE MIRROR IS THE CLASS. leafsystem_gather.h reads CClientLeafSystem through
// CLeafRegistryMirror; every member it declares must sit where the real
// class's does. The real class's members are private, so they are named
// where access is not checked: in an explicit instantiation's arguments.
//-----------------------------------------------------------------------------
namespace
{

template <class S, class T>
size_t LeafOffsetOf(T S::* m)
{
	alignas(S) static unsigned char s_Buf[sizeof(S)];
	S* p = reinterpret_cast<S*>(s_Buf);
	return (size_t)(reinterpret_cast<unsigned char*>(&(p->*m)) - s_Buf);
}

template <class S, class T>
size_t LeafOwnerSize(T S::*)
{
	return sizeof(S);
}

template <typename Tag, auto M>
struct LeafRob
{
	friend size_t LeafRobOffset(Tag) { return LeafOffsetOf(M); }

	friend size_t LeafRobOwnerSize(Tag) { return LeafOwnerSize(M); }
};

#define LEAF_ROB(name, member)     \
	struct name                    \
	{                              \
	};                             \
	size_t LeafRobOffset(name);    \
	size_t LeafRobOwnerSize(name); \
	template struct LeafRob<name, &member>;

LEAF_ROB(T_Leaf, CClientLeafSystem::m_Leaf)
LEAF_ROB(T_Renderables, CClientLeafSystem::m_Renderables)
LEAF_ROB(T_Shadows, CClientLeafSystem::m_Shadows)
LEAF_ROB(T_RenderablesInLeaf, CClientLeafSystem::m_RenderablesInLeaf)
LEAF_ROB(T_ShadowsInLeaf, CClientLeafSystem::m_ShadowsInLeaf)
LEAF_ROB(T_ShadowsOnRenderable, CClientLeafSystem::m_ShadowsOnRenderable)
LEAF_ROB(T_DirtyRenderables, CClientLeafSystem::m_DirtyRenderables)
LEAF_ROB(T_ViewModels, CClientLeafSystem::m_ViewModels)
LEAF_ROB(T_DrawStaticProps, CClientLeafSystem::m_DrawStaticProps)
LEAF_ROB(T_DrawSmallObjects, CClientLeafSystem::m_DrawSmallObjects)
LEAF_ROB(T_ShadowEnum, CClientLeafSystem::m_ShadowEnum)
LEAF_ROB(T_DeferredInserts, CClientLeafSystem::m_DeferredInserts)
LEAF_ROB(T_RI_Renderable, CClientLeafSystem::RenderableInfo_t::m_pRenderable)
LEAF_ROB(T_RI_RenderFrame, CClientLeafSystem::RenderableInfo_t::m_RenderFrame)
LEAF_ROB(T_RI_RenderFrame2, CClientLeafSystem::RenderableInfo_t::m_RenderFrame2)
LEAF_ROB(T_RI_EnumCount, CClientLeafSystem::RenderableInfo_t::m_EnumCount)
LEAF_ROB(T_RI_TranslucencyCalculated, CClientLeafSystem::RenderableInfo_t::m_TranslucencyCalculated)
LEAF_ROB(T_RI_LeafList, CClientLeafSystem::RenderableInfo_t::m_LeafList)
LEAF_ROB(T_RI_RenderLeaf, CClientLeafSystem::RenderableInfo_t::m_RenderLeaf)
LEAF_ROB(T_RI_Flags, CClientLeafSystem::RenderableInfo_t::m_Flags)
LEAF_ROB(T_RI_RenderGroup, CClientLeafSystem::RenderableInfo_t::m_RenderGroup)
LEAF_ROB(T_RI_FirstShadow, CClientLeafSystem::RenderableInfo_t::m_FirstShadow)
LEAF_ROB(T_RI_Area, CClientLeafSystem::RenderableInfo_t::m_Area)
LEAF_ROB(T_RI_TranslucencyCalculatedView, CClientLeafSystem::RenderableInfo_t::m_TranslucencyCalculatedView)
LEAF_ROB(T_CL_FirstElement, CClientLeafSystem::ClientLeaf_t::m_FirstElement)
LEAF_ROB(T_CL_FirstShadow, CClientLeafSystem::ClientLeaf_t::m_FirstShadow)
LEAF_ROB(T_CL_FirstDetailProp, CClientLeafSystem::ClientLeaf_t::m_FirstDetailProp)
LEAF_ROB(T_CL_DetailPropCount, CClientLeafSystem::ClientLeaf_t::m_DetailPropCount)
LEAF_ROB(T_CL_DetailPropRenderFrame, CClientLeafSystem::ClientLeaf_t::m_DetailPropRenderFrame)
LEAF_ROB(T_CL_SubSystemData, CClientLeafSystem::ClientLeaf_t::m_pSubSystemData)
LEAF_ROB(T_SI_Shadow, CClientLeafSystem::ShadowInfo_t::m_Shadow)
LEAF_ROB(T_SI_Flags, CClientLeafSystem::ShadowInfo_t::m_Flags)

typedef CLeafRegistryMirror M;

} // namespace

TEST(LeafSystemOracle, MirrorMatchesTheRealClass)
{
	struct Row_t
	{
		const char* pszName;
		size_t real, mirror;
	};

	const Row_t rows[] = {
		{"sizeof(CClientLeafSystem)", sizeof(CClientLeafSystem), sizeof(M)},
		{"m_Leaf", LeafRobOffset(T_Leaf()), LeafOffsetOf(&M::m_Leaf)},
		{"m_Renderables", LeafRobOffset(T_Renderables()), LeafOffsetOf(&M::m_Renderables)},
		{"m_Shadows", LeafRobOffset(T_Shadows()), LeafOffsetOf(&M::m_Shadows)},
		{"m_RenderablesInLeaf", LeafRobOffset(T_RenderablesInLeaf()), LeafOffsetOf(&M::m_RenderablesInLeaf)},
		{"m_ShadowsInLeaf", LeafRobOffset(T_ShadowsInLeaf()), LeafOffsetOf(&M::m_ShadowsInLeaf)},
		{"m_ShadowsOnRenderable", LeafRobOffset(T_ShadowsOnRenderable()), LeafOffsetOf(&M::m_ShadowsOnRenderable)},
		{"m_DirtyRenderables", LeafRobOffset(T_DirtyRenderables()), LeafOffsetOf(&M::m_DirtyRenderables)},
		{"m_ViewModels", LeafRobOffset(T_ViewModels()), LeafOffsetOf(&M::m_ViewModels)},
		{"m_DrawStaticProps", LeafRobOffset(T_DrawStaticProps()), LeafOffsetOf(&M::m_DrawStaticProps)},
		{"m_DrawSmallObjects", LeafRobOffset(T_DrawSmallObjects()), LeafOffsetOf(&M::m_DrawSmallObjects)},
		{"m_ShadowEnum", LeafRobOffset(T_ShadowEnum()), LeafOffsetOf(&M::m_ShadowEnum)},
		{"m_DeferredInserts", LeafRobOffset(T_DeferredInserts()), LeafOffsetOf(&M::m_DeferredInserts)},
		{"sizeof(RenderableInfo_t)", LeafRobOwnerSize(T_RI_Renderable()), sizeof(M::RenderableInfo_t)},
		{"RenderableInfo_t::m_pRenderable", LeafRobOffset(T_RI_Renderable()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_pRenderable)},
		{"RenderableInfo_t::m_RenderFrame", LeafRobOffset(T_RI_RenderFrame()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_RenderFrame)},
		{"RenderableInfo_t::m_RenderFrame2", LeafRobOffset(T_RI_RenderFrame2()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_RenderFrame2)},
		{"RenderableInfo_t::m_EnumCount", LeafRobOffset(T_RI_EnumCount()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_EnumCount)},
		{"RenderableInfo_t::m_TranslucencyCalculated", LeafRobOffset(T_RI_TranslucencyCalculated()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_TranslucencyCalculated)},
		{"RenderableInfo_t::m_LeafList", LeafRobOffset(T_RI_LeafList()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_LeafList)},
		{"RenderableInfo_t::m_RenderLeaf", LeafRobOffset(T_RI_RenderLeaf()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_RenderLeaf)},
		{"RenderableInfo_t::m_Flags", LeafRobOffset(T_RI_Flags()), LeafOffsetOf(&M::RenderableInfo_t::m_Flags)},
		{"RenderableInfo_t::m_RenderGroup", LeafRobOffset(T_RI_RenderGroup()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_RenderGroup)},
		{"RenderableInfo_t::m_FirstShadow", LeafRobOffset(T_RI_FirstShadow()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_FirstShadow)},
		{"RenderableInfo_t::m_Area", LeafRobOffset(T_RI_Area()), LeafOffsetOf(&M::RenderableInfo_t::m_Area)},
		{"RenderableInfo_t::m_TranslucencyCalculatedView", LeafRobOffset(T_RI_TranslucencyCalculatedView()),
		 LeafOffsetOf(&M::RenderableInfo_t::m_TranslucencyCalculatedView)},
		{"sizeof(ClientLeaf_t)", LeafRobOwnerSize(T_CL_FirstElement()), sizeof(M::ClientLeaf_t)},
		{"ClientLeaf_t::m_FirstElement", LeafRobOffset(T_CL_FirstElement()),
		 LeafOffsetOf(&M::ClientLeaf_t::m_FirstElement)},
		{"ClientLeaf_t::m_FirstShadow", LeafRobOffset(T_CL_FirstShadow()),
		 LeafOffsetOf(&M::ClientLeaf_t::m_FirstShadow)},
		{"ClientLeaf_t::m_FirstDetailProp", LeafRobOffset(T_CL_FirstDetailProp()),
		 LeafOffsetOf(&M::ClientLeaf_t::m_FirstDetailProp)},
		{"ClientLeaf_t::m_DetailPropCount", LeafRobOffset(T_CL_DetailPropCount()),
		 LeafOffsetOf(&M::ClientLeaf_t::m_DetailPropCount)},
		{"ClientLeaf_t::m_DetailPropRenderFrame", LeafRobOffset(T_CL_DetailPropRenderFrame()),
		 LeafOffsetOf(&M::ClientLeaf_t::m_DetailPropRenderFrame)},
		{"ClientLeaf_t::m_pSubSystemData", LeafRobOffset(T_CL_SubSystemData()),
		 LeafOffsetOf(&M::ClientLeaf_t::m_pSubSystemData)},
		{"sizeof(ShadowInfo_t)", LeafRobOwnerSize(T_SI_Shadow()), sizeof(M::ShadowInfo_t)},
		{"ShadowInfo_t::m_Shadow", LeafRobOffset(T_SI_Shadow()), LeafOffsetOf(&M::ShadowInfo_t::m_Shadow)},
		{"ShadowInfo_t::m_Flags", LeafRobOffset(T_SI_Flags()), LeafOffsetOf(&M::ShadowInfo_t::m_Flags)},
	};
	int nNonZero = 0;
	for (const Row_t& r : rows)
	{
		EXPECT_EQ(r.real, r.mirror) << r.pszName;
		nNonZero += r.real != 0 ? 1 : 0;
	}
	// The stimulus reached the subject: offsets past the two vtables are not 0.
	EXPECT_GT(nNonZero, 20);

	// And at run time: the gather reads the live registry through the mirror
	// and every renderable's handle names the slot it was read from (the
	// scenario's views above would have failed Gather() otherwise).
	EXPECT_EQ((void*)&Subject(), (void*)&Registry());
}
