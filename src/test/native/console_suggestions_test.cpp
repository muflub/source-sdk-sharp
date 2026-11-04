#include "gtest/gtest.h"

#include "console_suggestions.h"

#include <cstdlib>
#include <cstring>
#include <set>
#include <string>

namespace
{

class Heap : public ss::IEngineHeap
{
public:
	std::set<void*> m_Live;
	bool m_bRefuse = false;

	void* Alloc(size_t n) override
	{
		if (m_bRefuse)
			return NULL;
		void* p = malloc(n);
		m_Live.insert(p);
		return p;
	}

	void* Realloc(void* p, size_t n) override
	{
		m_Live.erase(p);
		void* q = realloc(p, n);
		m_Live.insert(q);
		return q;
	}

	void Free(void* p) override
	{
		m_Live.erase(p);
		free(p);
	}

	~Heap()
	{
		for (void* p : m_Live)
			free(p);
	}
};

const ss::EngineUtlVector<ss::EngineUtlString>& View(const ss::EngineUtlVectorLayout<ss::EngineUtlString>& vec)
{
	return *reinterpret_cast<const ss::EngineUtlVector<ss::EngineUtlString>*>(&vec);
}

TEST(ConsoleSuggestions, EachLineJoinsTheVector)
{
	Heap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};

	EXPECT_EQ(2, ss::AppendSuggestionLines(&heap, vec, "map dm_lockdown\nmap dm_overwatch\n"));

	ASSERT_EQ(2, vec.m_Size);
	EXPECT_STREQ("map dm_lockdown", vec.m_pMemory[0].m_pString);
	EXPECT_STREQ("map dm_overwatch", vec.m_pMemory[1].m_pString);
}

TEST(ConsoleSuggestions, ALastLineNeedsNoNewline)
{
	Heap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};

	EXPECT_EQ(1, ss::AppendSuggestionLines(&heap, vec, "map dm_lockdown"));
	EXPECT_STREQ("map dm_lockdown", vec.m_pMemory[0].m_pString);
}

TEST(ConsoleSuggestions, NoLinesAddNothing)
{
	Heap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};

	EXPECT_EQ(0, ss::AppendSuggestionLines(&heap, vec, ""));
	EXPECT_EQ(0, ss::AppendSuggestionLines(&heap, vec, NULL));
	EXPECT_EQ(0, vec.m_Size);
}

TEST(ConsoleSuggestions, ALineTooLongIsSkippedAndTheRestKept)
{
	Heap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};
	const std::string lines = std::string(600, 'x') + "\nkept\n";

	EXPECT_EQ(1, ss::AppendSuggestionLines(&heap, vec, lines.c_str()));
	EXPECT_STREQ("kept", vec.m_pMemory[0].m_pString);
}

TEST(ConsoleSuggestions, AHeapThatRefusesStopsTheAppend)
{
	Heap heap;
	heap.m_bRefuse = true;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};

	EXPECT_EQ(0, ss::AppendSuggestionLines(&heap, vec, "one\ntwo\n"));
}

TEST(ConsoleSuggestions, WrittenLinesAreNewlineSeparated)
{
	Heap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};
	ss::AppendSuggestionLines(&heap, vec, "one\ntwo\n");

	char out[32];
	EXPECT_EQ(2, ss::WriteSuggestionLines(View(vec), out, sizeof(out)));
	EXPECT_STREQ("one\ntwo\n", out);
}

TEST(ConsoleSuggestions, OnlyWholeLinesAreWritten)
{
	Heap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};
	ss::AppendSuggestionLines(&heap, vec, "one\ntwo\n");

	char out[6];
	EXPECT_EQ(1, ss::WriteSuggestionLines(View(vec), out, sizeof(out)));
	EXPECT_STREQ("one\n", out);
}

TEST(ConsoleSuggestions, TheCommandIsTheFirstWord)
{
	char name[16];

	ASSERT_TRUE(ss::ConsoleLineCommand("map dm_lockdown", name, sizeof(name)));
	EXPECT_STREQ("map", name);

	ASSERT_TRUE(ss::ConsoleLineCommand("map", name, sizeof(name)));
	EXPECT_STREQ("map", name);
}

TEST(ConsoleSuggestions, ALineWithNoCommandOrOneTooLongHasNone)
{
	char name[4];

	EXPECT_FALSE(ss::ConsoleLineCommand("", name, sizeof(name)));
	EXPECT_FALSE(ss::ConsoleLineCommand(" x", name, sizeof(name)));
	EXPECT_FALSE(ss::ConsoleLineCommand(NULL, name, sizeof(name)));
	EXPECT_FALSE(ss::ConsoleLineCommand("toolong", name, sizeof(name)));
}

} // namespace
