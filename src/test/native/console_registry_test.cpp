//========= SourceSharp — the managed console's carriers, by id and name =====//
//
// console_registry.h is the bookkeeping behind SourceSharp_EngineRegister
// ConVar/ConCommand, SetConVarValue/GetConVarValue and the convar change
// callback (sourcesharp_gamesystem.cpp). C# holds the ids it hands out.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "console_registry.h"

#include <string>
#include <vector>

namespace
{

struct Carrier
{
	int nId;
	std::string strName;
};

// Registers as the game does: build the carrier with NextId(), then Add.
int Register(CConsoleRegistry<Carrier>& reg, const char* pName, std::vector<Carrier*>& owned)
{
	if (Carrier* pExisting = reg.ByName(pName))
		return pExisting->nId;

	Carrier* p = new Carrier{reg.NextId(), pName};
	owned.push_back(p);
	const int nId = reg.Add(pName, p);
	EXPECT_EQ(p->nId, nId);
	return nId;
}

struct Owned
{
	std::vector<Carrier*> v;

	~Owned()
	{
		for (Carrier* p : v)
			delete p;
	}
};

TEST(ConsoleRegistry, IdsAreDenseAndInRegistrationOrder)
{
	CConsoleRegistry<Carrier> reg;
	Owned owned;

	const char* const kNames[] = {"sv_gravity", "mp_timelimit", "ss_debug", "cl_foo", "sv_cheats"};
	for (int i = 0; i < 5; i++)
		EXPECT_EQ(i, Register(reg, kNames[i], owned.v));

	EXPECT_EQ(5, reg.Count());
	for (int i = 0; i < 5; i++)
	{
		ASSERT_TRUE(reg.ById(i) != NULL);
		EXPECT_EQ(kNames[i], reg.ById(i)->strName);
	}
}

TEST(ConsoleRegistry, ReRegisteringANameInAnotherCaseAnswersTheExistingId)
{
	CConsoleRegistry<Carrier> reg;
	Owned owned;

	Register(reg, "sv_first", owned.v);
	const int nId = Register(reg, "SS_Friction", owned.v);
	EXPECT_EQ(nId, Register(reg, "ss_friction", owned.v));
	EXPECT_EQ(nId, Register(reg, "SS_FRICTION", owned.v));
	EXPECT_EQ(2, reg.Count()) << "no second carrier for the same name";
	EXPECT_EQ(2, reg.NextId()) << "and no id spent on it";
}

TEST(ConsoleRegistry, AnUnknownOrNegativeIdFindsNothing)
{
	CConsoleRegistry<Carrier> reg;
	Owned owned;
	Register(reg, "sv_gravity", owned.v);

	EXPECT_TRUE(reg.ById(-1) == NULL);
	EXPECT_TRUE(reg.ById(-2) == NULL) << "the engine-owned sentinel";
	EXPECT_TRUE(reg.ById(1) == NULL);
	EXPECT_TRUE(reg.ById(1000) == NULL);
	EXPECT_EQ(-1, reg.IdOf("sv_unknown"));
	EXPECT_EQ(-1, reg.IdOf(NULL));
}

// The change callback asks by the ConVar's name; it must reach the carrier that
// name registered, in whatever case the engine spells it.
TEST(ConsoleRegistry, ANameReachesTheCarrierItRegistered)
{
	CConsoleRegistry<Carrier> reg;
	Owned owned;
	Register(reg, "sv_gravity", owned.v);
	Register(reg, "Mp_TimeLimit", owned.v);
	Register(reg, "ss_debug", owned.v);

	ASSERT_TRUE(reg.ByName("mp_timelimit") != NULL);
	EXPECT_EQ(1, reg.ByName("mp_timelimit")->nId);
	EXPECT_EQ(reg.ById(2), reg.ByName("SS_DEBUG"));
	EXPECT_TRUE(reg.ByName("ss_debu") == NULL);
}

} // namespace

//-----------------------------------------------------------------------------
// DeleteAll: the release after UnregisterConCommands (P4.9, live at unload).
//-----------------------------------------------------------------------------
namespace
{
int g_nDestroyed = 0;

struct CCountedCarrier
{
	~CCountedCarrier() { ++g_nDestroyed; }
};
}

TEST(ConsoleRegistry, DeleteAllDeletesEveryCarrierOnceAndForgetsEveryName)
{
	g_nDestroyed = 0;
	CConsoleRegistry<CCountedCarrier> reg;
	reg.Add("cs_one", new CCountedCarrier);
	reg.Add("cs_two", new CCountedCarrier);
	reg.Add("cs_three", new CCountedCarrier);
	ASSERT_EQ(3, reg.Count());

	reg.DeleteAll();

	EXPECT_EQ(3, g_nDestroyed);
	EXPECT_EQ(0, reg.Count());
	EXPECT_EQ(nullptr, reg.ByName("cs_two"));
	EXPECT_EQ(-1, reg.IdOf("cs_one"));
}

TEST(ConsoleRegistry, AnIdIsNotHandedOutAgainAfterDeleteAll)
{
	CConsoleRegistry<CCountedCarrier> reg;
	const int nFirst = reg.Add("cs_one", new CCountedCarrier);
	reg.DeleteAll();
	const int nAfter = reg.Add("cs_one", new CCountedCarrier);
	EXPECT_GT(nAfter, nFirst);
	reg.DeleteAll();
}
