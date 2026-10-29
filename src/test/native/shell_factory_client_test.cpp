// The client's interface factory (src/client/native/shell_factory_client.cpp,
// plan_client_novalve C9): InterfaceReg rows chain newest first, and
// CreateInterface answers the row whose name matches exactly, with IFACE_OK,
// or NULL with IFACE_FAILED. The rows here are this binary's own, made by
// the same EXPOSE macro the client's TUs use.

#include "gtest/gtest.h"

#include "tier1/interface.h"
#include "client_factorylist.h"

namespace
{
class IThing
{
public:
	virtual int Id() = 0;
};

class CThingA : public IThing
{
public:
	int Id() override { return 1; }
};

class CThingB : public IThing
{
public:
	int Id() override { return 2; }
};

class CThingShadow : public IThing
{
public:
	int Id() override { return 3; }
};

CThingA g_ThingA;
CThingB g_ThingB;
CThingShadow g_ThingShadow;
}

EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CThingA, IThing, "Thing001", g_ThingA);
EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CThingB, IThing, "Thing002", g_ThingB);

TEST(ShellFactoryClient, ARegisteredNameIsAnsweredWithItsObject)
{
	int nCode = -1;
	IThing* p = (IThing*)CreateInterface("Thing002", &nCode);
	ASSERT_TRUE(p != NULL);
	EXPECT_EQ(2, p->Id());
	EXPECT_EQ(IFACE_OK, nCode);
	EXPECT_EQ(1, ((IThing*)CreateInterface("Thing001", NULL))->Id());
}

TEST(ShellFactoryClient, ANameMatchesExactlyOrNotAtAll)
{
	for (const char* psz : {"thing001", "Thing00", "Thing0011", "", "Nothing"})
	{
		int nCode = -1;
		EXPECT_EQ(NULL, CreateInterface(psz, &nCode)) << psz;
		EXPECT_EQ(IFACE_FAILED, nCode) << psz;
	}
}

TEST(ShellFactoryClient, TheNewestRowOfANameWins)
{
	// A second row under a used name is found first: the chain is newest first.
	static InterfaceReg s_Shadow([]() -> void* { return (IThing*)&g_ThingShadow; }, "Thing001");
	EXPECT_EQ(3, ((IThing*)CreateInterface("Thing001", NULL))->Id());
	EXPECT_EQ(&s_Shadow, InterfaceReg::s_pInterfaceRegs);
}

// client_factorylist.cpp's pair, now this file's: what is stored is what
// comes back, and a second store replaces the first.
static void* FactoryA(const char*, int*)
{
	return NULL;
}

static void* FactoryB(const char*, int*)
{
	return NULL;
}

TEST(ShellFactoryClient, TheFactoryListGivesBackWhatWasStored)
{
	factorylist_t in = {&FactoryA, &FactoryB}, out = {NULL, NULL};
	FactoryList_Store(in);
	FactoryList_Retrieve(out);
	EXPECT_EQ(&FactoryA, out.appSystemFactory);
	EXPECT_EQ(&FactoryB, out.physicsFactory);

	factorylist_t swapped = {&FactoryB, NULL};
	FactoryList_Store(swapped);
	FactoryList_Retrieve(out);
	EXPECT_EQ(&FactoryB, out.appSystemFactory);
	EXPECT_EQ(NULL, out.physicsFactory);
}
