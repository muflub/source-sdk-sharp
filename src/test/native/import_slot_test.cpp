//========= SourceSharp — import_slot.h tests ================================//
//
// The GameUI factory swap (plan_gameui_managed.md G4) writes one slot of
// engine.so's import of dlsym. These facts hold the same helper against a
// test library that looks symbols up through its own dlsym import, as
// engine.so does: the slot is found, a swapped slot answers the swap, and the
// slot put back answers dlsym again.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/shared/import_slot.h"

#include <dlfcn.h>

extern "C" void* ImportSlotTestLookup(void* hModule, const char* pszName);

namespace
{

int s_nSwapCalls;

void* Swapped(void*, const char*)
{
	s_nSwapCalls++;
	return reinterpret_cast<void*>(&Swapped);
}

TEST(ImportSlot, FindsTheLibrarysImportOfDlsym)
{
	EXPECT_NE(nullptr, ImportSlot::Find("libimport_slot_testlib.so", "dlsym"));
}

TEST(ImportSlot, AnUnknownModuleOrSymbolHasNoSlot)
{
	EXPECT_EQ(nullptr, ImportSlot::Find("libno_such_module.so", "dlsym"));
	EXPECT_EQ(nullptr, ImportSlot::Find("libimport_slot_testlib.so", "no_such_symbol"));
}

TEST(ImportSlot, ASwappedSlotAnswersTheSwapAndThePutBackSlotAnswersDlsym)
{
	// Resolved once first, so the slot holds dlsym itself rather than the PLT's resolver.
	void* pSelf = dlopen(nullptr, RTLD_NOW);
	ASSERT_NE(nullptr, ImportSlotTestLookup(pSelf, "ImportSlotTestLookup"));

	void** pSlot = ImportSlot::Find("libimport_slot_testlib.so", "dlsym");
	ASSERT_NE(nullptr, pSlot);
	void* pOriginal = *pSlot;

	s_nSwapCalls = 0;
	ASSERT_TRUE(ImportSlot::Write(pSlot, reinterpret_cast<void*>(&Swapped)));
	EXPECT_EQ(reinterpret_cast<void*>(&Swapped), ImportSlotTestLookup(pSelf, "ImportSlotTestLookup"));
	EXPECT_EQ(1, s_nSwapCalls);

	ASSERT_TRUE(ImportSlot::Write(pSlot, pOriginal));
	EXPECT_EQ(reinterpret_cast<void*>(&ImportSlotTestLookup), ImportSlotTestLookup(pSelf, "ImportSlotTestLookup"));
	EXPECT_EQ(1, s_nSwapCalls);
}

TEST(ImportSlot, AReadOnlySlotIsWrittenAndItsProtectionPutBack)
{
	alignas(4096) static void* s_page[4096 / sizeof(void*)];
	ASSERT_TRUE(SS_PageProtect(s_page, SS_PAGE_READ));
	ASSERT_TRUE(ImportSlot::Write(&s_page[0], reinterpret_cast<void*>(&Swapped)));
	EXPECT_EQ(SS_PAGE_READ, VTableSlotPatch::PageProtection(s_page));
	EXPECT_EQ(reinterpret_cast<void*>(&Swapped), s_page[0]);
	SS_PageProtect(s_page, SS_PAGE_READ | SS_PAGE_WRITE);
}

} // namespace
