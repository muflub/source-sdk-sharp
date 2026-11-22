//========= SourceSharp — the dynamic mesh taken off its override ============//
//
// The shader API's dynamic mesh kept the world's vertex buffer as an override
// (IMatRenderContext::BindBatch) across a level change, and the first buffered
// draw after the world's meshes were freed flushed through it: a use after free
// that crashed the menu background's quad at SIGNONSTATE_CONNECTED when the
// freed block held a model path (crash-uidraw). The client's level shutdown
// fetches a dynamic mesh with no override, which sets the mesh's format while
// the buffer still lives. These facts hold that fetch over fakes whose
// recorders sit at the compiler's slots.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string.h>

#include <string>

#include "client/native/dynamic_mesh_override.h"
#include "shared/native/vtable_hook.h"

namespace
{

struct FakeTable
{
	void* slots[512];

	FakeTable() { memset(slots, 0, sizeof(slots)); }

	template <typename Fn>
	void Put(Fn pmf, void* pRecorder)
	{
		const int slot = VTableHook::MemberOf(pmf).slot;
		ASSERT_GE(slot, 0);
		ASSERT_LT(slot, 512);
		slots[slot] = pRecorder;
	}
};

struct FakeObject
{
	void** vtable;
};

struct Seen
{
	std::string found;
	int fetches;
	bool buffered;
	void* vertexOverride;
	void* indexOverride;
	void* autoBind;
	int begins;
	int ends;
	int releases;
	bool error;
};

Seen g_seen;

FakeObject g_whiteObject;
FakeObject g_contextObject;

bool Rec_IsErrorMaterial(void*)
{
	return g_seen.error;
}

IMaterial* Rec_FindMaterial(void*, const char* pName, const char*, bool, const char*)
{
	g_seen.found = pName ? pName : "";
	return reinterpret_cast<IMaterial*>(&g_whiteObject);
}

IMatRenderContext* Rec_GetRenderContext(void*)
{
	return reinterpret_cast<IMatRenderContext*>(&g_contextObject);
}

void Rec_BeginRender(void*)
{
	g_seen.begins++;
}

void Rec_EndRender(void*)
{
	g_seen.ends++;
}

int Rec_Release(void*)
{
	g_seen.releases++;
	return 0;
}

IMesh* Rec_GetDynamicMesh(void*, bool buffered, IMesh* pVertexOverride, IMesh* pIndexOverride, IMaterial* pAutoBind)
{
	g_seen.fetches++;
	g_seen.buffered = buffered;
	g_seen.vertexOverride = pVertexOverride;
	g_seen.indexOverride = pIndexOverride;
	g_seen.autoBind = pAutoBind;
	return nullptr;
}

struct MaterialTable : FakeTable
{
	MaterialTable() { Put(&IMaterial::IsErrorMaterial, (void*)&Rec_IsErrorMaterial); }
};

struct SystemTable : FakeTable
{
	SystemTable()
	{
		Put(&IMaterialSystem::FindMaterial, (void*)&Rec_FindMaterial);
		Put(&IMaterialSystem::GetRenderContext, (void*)&Rec_GetRenderContext);
	}
};

struct ContextTable : FakeTable
{
	ContextTable()
	{
		Put(&IMatRenderContext::BeginRender, (void*)&Rec_BeginRender);
		Put(&IMatRenderContext::EndRender, (void*)&Rec_EndRender);
		Put(&IMatRenderContext::Release, (void*)&Rec_Release);
		Put(&IMatRenderContext::GetDynamicMesh, (void*)&Rec_GetDynamicMesh);
	}
};

MaterialTable g_materialTable;
SystemTable g_systemTable;
ContextTable g_contextTable;
FakeObject g_systemObject;

IMaterialSystem* Fakes()
{
	g_seen = Seen{};
	g_whiteObject.vtable = g_materialTable.slots;
	g_contextObject.vtable = g_contextTable.slots;
	g_systemObject.vtable = g_systemTable.slots;
	return reinterpret_cast<IMaterialSystem*>(&g_systemObject);
}

} // namespace

TEST(DynamicMeshOverride, TheMeshIsFetchedWithNoVertexOrIndexOverride)
{
	SourceSharp_DropDynamicMeshOverride(Fakes());

	ASSERT_EQ(1, g_seen.fetches);
	EXPECT_EQ(nullptr, g_seen.vertexOverride);
	EXPECT_EQ(nullptr, g_seen.indexOverride);
}

TEST(DynamicMeshOverride, TheFetchIsBufferedAsTheMenuDrawsAre)
{
	SourceSharp_DropDynamicMeshOverride(Fakes());

	ASSERT_EQ(1, g_seen.fetches);
	EXPECT_TRUE(g_seen.buffered);
}

TEST(DynamicMeshOverride, TheFetchBindsTheSurfacesWhiteMaterial)
{
	SourceSharp_DropDynamicMeshOverride(Fakes());

	EXPECT_EQ("vgui/white", g_seen.found);
	EXPECT_EQ(reinterpret_cast<IMaterial*>(&g_whiteObject), g_seen.autoBind);
}

TEST(DynamicMeshOverride, TheRenderContextIsBalancedAndReleased)
{
	SourceSharp_DropDynamicMeshOverride(Fakes());

	EXPECT_EQ(1, g_seen.begins);
	EXPECT_EQ(1, g_seen.ends);
	EXPECT_EQ(1, g_seen.releases);
}

TEST(DynamicMeshOverride, AnErrorMaterialFetchesNothing)
{
	IMaterialSystem* pMaterials = Fakes();
	g_seen.error = true;

	SourceSharp_DropDynamicMeshOverride(pMaterials);

	EXPECT_EQ("vgui/white", g_seen.found);
	EXPECT_EQ(0, g_seen.fetches);
	EXPECT_EQ(0, g_seen.begins);
}

TEST(DynamicMeshOverride, NoMaterialSystemFetchesNothing)
{
	Fakes();

	SourceSharp_DropDynamicMeshOverride(nullptr);

	EXPECT_EQ("", g_seen.found);
	EXPECT_EQ(0, g_seen.fetches);
}
