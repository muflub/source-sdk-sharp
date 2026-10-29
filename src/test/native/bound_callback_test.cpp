// CBoundCallback: a plain function pointer that calls a method of the instance it was bound
// to, for each callback shape the server installs (the spew hook, the assert notify and the
// global ConVar change callback), and the bytes of the thunk behind it on both ABIs.

#include "gtest/gtest.h"

#include "bound_callback.h"

#include <cstdio>
#include <string>
#include <vector>

namespace
{

class CRecorder
{
public:
	// The spew hook's shape: ( SpewType_t, const char * ) -> SpewRetval_t.
	int Spew(int nType, const char* pMsg)
	{
		m_Seen.push_back(std::to_string(nType) + ":" + pMsg);
		return m_nAnswer;
	}

	// The assert notify's: ( file, line, message ).
	void Assert(const char* pFile, int nLine, const char* pMsg)
	{
		m_Seen.push_back(std::string(pFile) + "(" + std::to_string(nLine) + "):" + pMsg);
	}

	// The ConVar change callback's: ( IConVar *, old value, old float ).
	void ConVarChanged(void* pVar, const char* pOld, float flOld)
	{
		m_pVar = pVar;
		m_Seen.push_back(std::string(pOld) + "=" + std::to_string(flOld));
	}

	std::vector<std::string> m_Seen;
	int m_nAnswer = 7;
	void* m_pVar = nullptr;
};

typedef CBoundCallback<int(int, const char*)> SpewCallback;
typedef CBoundCallback<void(const char*, int, const char*)> AssertCallback;
typedef CBoundCallback<void(void*, const char*, float)> ConVarCallback;

} // namespace

TEST(BoundCallback, TheSpewShapeReachesItsInstanceAndAnswers)
{
	CRecorder recorder;
	SpewCallback callback(&recorder, &SpewCallback::Trampoline<CRecorder, &CRecorder::Spew>);
	ASSERT_NE(nullptr, callback.Get());

	EXPECT_EQ(7, callback.Get()(3, "hello"));

	ASSERT_EQ(1u, recorder.m_Seen.size());
	EXPECT_EQ("3:hello", recorder.m_Seen[0]);
}

TEST(BoundCallback, TheAssertShapeCarriesAllThreeArguments)
{
	CRecorder recorder;
	AssertCallback callback(&recorder, &AssertCallback::Trampoline<CRecorder, &CRecorder::Assert>);
	ASSERT_NE(nullptr, callback.Get());

	callback.Get()("a.cpp", 42, "boom");

	ASSERT_EQ(1u, recorder.m_Seen.size());
	EXPECT_EQ("a.cpp(42):boom", recorder.m_Seen[0]);
}

TEST(BoundCallback, TheConVarShapeCarriesItsFloat)
{
	CRecorder recorder;
	ConVarCallback callback(&recorder, &ConVarCallback::Trampoline<CRecorder, &CRecorder::ConVarChanged>);
	ASSERT_NE(nullptr, callback.Get());

	int var = 0;
	callback.Get()(&var, "old", 2.5f);

	EXPECT_EQ(&var, recorder.m_pVar);
	ASSERT_EQ(1u, recorder.m_Seen.size());
	EXPECT_EQ("old=2.500000", recorder.m_Seen[0]);
}

TEST(BoundCallback, EachInstanceGetsItsOwnCalls)
{
	CRecorder one, two;
	SpewCallback first(&one, &SpewCallback::Trampoline<CRecorder, &CRecorder::Spew>);
	SpewCallback second(&two, &SpewCallback::Trampoline<CRecorder, &CRecorder::Spew>);

	EXPECT_NE(first.Get(), second.Get());
	first.Get()(1, "a");
	second.Get()(2, "b");

	ASSERT_EQ(1u, one.m_Seen.size());
	ASSERT_EQ(1u, two.m_Seen.size());
	EXPECT_EQ("1:a", one.m_Seen[0]);
	EXPECT_EQ("2:b", two.m_Seen[0]);
}

namespace
{
std::vector<unsigned char> Bytes(const bound_callback::ArgClass* pArgs, int nArgs, bool bWindows)
{
	unsigned char code[64];
	const size_t n = bound_callback::Assemble(pArgs, nArgs, bWindows, 0x1122334455667788ull, 0xAABBCCDDEEFF0011ull,
											  code, sizeof(code));
	return std::vector<unsigned char>(code, code + n);
}

const bound_callback::ArgClass kConVarArgs[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
												bound_callback::ArgClass::Float};
} // namespace

TEST(BoundCallback, OnWin64EveryArgumentMovesOnePositionAndTheFloatChangesXmmRegister)
{
	const std::vector<unsigned char> expected = {
		0x0F, 0x28, 0xDA, // movaps xmm3, xmm2
		0x49, 0x89, 0xD0, // mov r8, rdx
		0x48, 0x89, 0xCA, // mov rdx, rcx
		0x48, 0xB9, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // mov rcx, instance
		0x48, 0xB8, 0x11, 0x00, 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, // mov rax, target
		0xFF, 0xE0, // jmp rax
	};

	EXPECT_EQ(expected, Bytes(kConVarArgs, 3, true));
}

TEST(BoundCallback, OnSystemVOnlyTheIntegersMoveAndTheFloatStays)
{
	const std::vector<unsigned char> expected = {
		0x48, 0x89, 0xF2, // mov rdx, rsi
		0x48, 0x89, 0xFE, // mov rsi, rdi
		0x48, 0xBF, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // mov rdi, instance
		0x48, 0xB8, 0x11, 0x00, 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, // mov rax, target
		0xFF, 0xE0, // jmp rax
	};

	EXPECT_EQ(expected, Bytes(kConVarArgs, 2 + 1, false));
}

TEST(BoundCallback, ASignatureThatWouldNotFitInRegistersIsRefused)
{
	const bound_callback::ArgClass kFour[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											  bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer};
	EXPECT_TRUE(Bytes(kFour, 4, true).empty()) << "Win64 has four argument registers, and the instance takes one";

	const bound_callback::ArgClass kSix[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											 bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											 bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer};
	EXPECT_TRUE(Bytes(kSix, 6, false).empty()) << "System V has six integer registers";
	EXPECT_FALSE(Bytes(kSix, 5, false).empty());
}

#if defined(__linux__)
TEST(BoundCallback, TheThunkPageIsReadAndExecuteButNotWrite)
{
	CRecorder recorder;
	SpewCallback callback(&recorder, &SpewCallback::Trampoline<CRecorder, &CRecorder::Spew>);
	const uintptr_t uAt = (uintptr_t)callback.Get();
	ASSERT_NE(0u, uAt);

	FILE* pMaps = fopen("/proc/self/maps", "r");
	ASSERT_NE(nullptr, pMaps);

	char line[512];
	std::string perms;
	while (fgets(line, sizeof(line), pMaps))
	{
		unsigned long uStart = 0, uEnd = 0;
		char szPerms[8] = {};
		if (sscanf(line, "%lx-%lx %7s", &uStart, &uEnd, szPerms) == 3 && uAt >= uStart && uAt < uEnd)
			perms = szPerms;
	}
	fclose(pMaps);

	EXPECT_EQ("r-xp", perms);
}
#endif
