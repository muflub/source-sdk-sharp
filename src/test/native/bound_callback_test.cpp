// CBoundCallback: a plain function pointer that calls a method of the instance it was bound
// to, for each callback shape the DLLs install (the spew hook, the assert notify, the global
// ConVar change callback, a ClientClass create function and the vtable slots the client
// patches), and the bytes of the thunk behind it on both ABIs.

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

	// A ClientClass create function's: ( entnum, serial ) -> the networkable.
	void* Create(int nEnt, int nSerial)
	{
		m_Seen.push_back(std::to_string(nEnt) + "/" + std::to_string(nSerial));
		return this;
	}

	// IFileSystem::AddSearchPath patched into its vtable: ( this, path, path ID, add type ),
	// one argument more than Win64's registers once the instance is first.
	void AddSearchPath(void* pThis, const char* pPath, const char* pPathID, int nAdd)
	{
		m_pVar = pThis;
		m_Seen.push_back(std::string(pPath) + "@" + pPathID + "#" + std::to_string(nAdd));
	}

	std::vector<std::string> m_Seen;
	int m_nAnswer = 7;
	void* m_pVar = nullptr;
};

typedef CBoundCallback<int(int, const char*)> SpewCallback;
typedef CBoundCallback<void(const char*, int, const char*)> AssertCallback;
typedef CBoundCallback<void(void*, const char*, float)> ConVarCallback;
typedef CBoundCallback<void*(int, int)> CreateCallback;
typedef CBoundCallback<void(void*, const char*, const char*, int)> AddSearchPathCallback;

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

TEST(BoundCallback, TheCreateShapeAnswersAPointer)
{
	CRecorder recorder;
	CreateCallback callback(&recorder, &CreateCallback::Trampoline<CRecorder, &CRecorder::Create>);
	ASSERT_NE(nullptr, callback.Get());

	EXPECT_EQ(&recorder, callback.Get()(5, 1234));

	ASSERT_EQ(1u, recorder.m_Seen.size());
	EXPECT_EQ("5/1234", recorder.m_Seen[0]);
}

TEST(BoundCallback, AVtableSlotWithFourArgumentsCarriesThemAll)
{
	CRecorder recorder;
	AddSearchPathCallback callback(&recorder, &AddSearchPathCallback::Trampoline<CRecorder, &CRecorder::AddSearchPath>);
	ASSERT_NE(nullptr, callback.Get());

	int engineObject = 0;
	callback.Get()(&engineObject, "maps/x.bsp", "GAME", 1);

	EXPECT_EQ(&engineObject, recorder.m_pVar);
	ASSERT_EQ(1u, recorder.m_Seen.size());
	EXPECT_EQ("maps/x.bsp@GAME#1", recorder.m_Seen[0]);
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
std::vector<unsigned char> Bytes(const bound_callback::ArgClass* pArgs, int nArgs, bool bWindows,
								 int* pOutFrame = nullptr)
{
	unsigned char code[96];
	const size_t n = bound_callback::Assemble(pArgs, nArgs, bWindows, 0x1122334455667788ull, 0xAABBCCDDEEFF0011ull,
											  code, sizeof(code), pOutFrame);
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

TEST(BoundCallback, OnWin64AFourthArgumentGoesToTheStackOfAFrameTheThunkMakes)
{
	const bound_callback::ArgClass kArgs[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											  bound_callback::ArgClass::Integer, bound_callback::ArgClass::Float};
	const std::vector<unsigned char> expected = {
		0x48, 0x83, 0xEC, 0x28, // sub rsp, 0x28: the home area and one stack slot, rsp 16-aligned
		0xF2, 0x0F, 0x11, 0x5C, 0x24, 0x20, // movsd [rsp+0x20], xmm3
		0x4D, 0x89, 0xC1, // mov r9, r8
		0x49, 0x89, 0xD0, // mov r8, rdx
		0x48, 0x89, 0xCA, // mov rdx, rcx
		0x48, 0xB9, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // mov rcx, instance
		0x48, 0xB8, 0x11, 0x00, 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, // mov rax, target
		0xFF, 0xD0, // call rax
		0x48, 0x83, 0xC4, 0x28, // add rsp, 0x28
		0xC3, // ret
	};

	int nFrame = -1;
	EXPECT_EQ(expected, Bytes(kArgs, 4, true, &nFrame));
	EXPECT_EQ(0x28, nFrame);
}

TEST(BoundCallback, OnWin64TheCallersStackArgumentMovesOneSlotAlong)
{
	const bound_callback::ArgClass kArgs[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											  bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											  bound_callback::ArgClass::Integer};
	const std::vector<unsigned char> expected = {
		0x48, 0x83, 0xEC, 0x38, // sub rsp, 0x38
		0x48, 0x8B, 0x44, 0x24, 0x60, // mov rax, [rsp+0x60]: past the frame, the return and the home area
		0x48, 0x89, 0x44, 0x24, 0x28, // mov [rsp+0x28], rax
		0x4C, 0x89, 0x4C, 0x24, 0x20, // mov [rsp+0x20], r9
		0x4D, 0x89, 0xC1, // mov r9, r8
		0x49, 0x89, 0xD0, // mov r8, rdx
		0x48, 0x89, 0xCA, // mov rdx, rcx
		0x48, 0xB9, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // mov rcx, instance
		0x48, 0xB8, 0x11, 0x00, 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, // mov rax, target
		0xFF, 0xD0, // call rax
		0x48, 0x83, 0xC4, 0x38, // add rsp, 0x38
		0xC3, // ret
	};

	int nFrame = -1;
	EXPECT_EQ(expected, Bytes(kArgs, 5, true, &nFrame));
	EXPECT_EQ(0x38, nFrame);
}

TEST(BoundCallback, ABareJumpMakesNoFrame)
{
	int nFrame = -1;
	EXPECT_FALSE(Bytes(kConVarArgs, 3, true, &nFrame).empty());
	EXPECT_EQ(0, nFrame);

	nFrame = -1;
	EXPECT_FALSE(Bytes(kConVarArgs, 3, false, &nFrame).empty());
	EXPECT_EQ(0, nFrame);
}

TEST(BoundCallback, ASignatureThatWouldNotFitIsRefused)
{
	const bound_callback::ArgClass kSix[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											 bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											 bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer};
	EXPECT_TRUE(Bytes(kSix, 6, true).empty()) << "Win64's frame thunk takes six arguments with the instance";
	EXPECT_FALSE(Bytes(kSix, 5, true).empty());

	EXPECT_TRUE(Bytes(kSix, 6, false).empty()) << "System V has six integer registers";
	EXPECT_FALSE(Bytes(kSix, 5, false).empty());
}

#if !defined(_WIN32) && (defined(__clang__) || defined(__GNUC__))
// The Win64 thunks run here too: ms_abi makes the compiler call and receive in Microsoft's
// convention, so the frame thunk's stack slots are proven on this machine, not only on CI.
namespace
{
struct CWinSeen
{
	void* pInstance = nullptr;
	int64_t a = 0, b = 0, c = 0, e = 0;
	float d = 0.0f;
};

__attribute__((ms_abi)) int64_t WinTarget(void* pInstance, int64_t a, int64_t b, int64_t c, float d, int64_t e)
{
	CWinSeen* pSeen = static_cast<CWinSeen*>(pInstance);
	pSeen->pInstance = pInstance;
	pSeen->a = a;
	pSeen->b = b;
	pSeen->c = c;
	pSeen->d = d;
	pSeen->e = e;

	// A 16-byte aligned store faults if the thunk left rsp misaligned at the call.
	alignas(16) volatile double aligned[2] = {1.0, 2.0};
	return (int64_t)(aligned[0] + aligned[1]) + a + b + c + e;
}

typedef int64_t(__attribute__((ms_abi)) * WinFn_t)(int64_t, int64_t, int64_t, float, int64_t);
} // namespace

TEST(BoundCallback, TheWin64FrameThunkRunsAndPassesEveryArgument)
{
	const bound_callback::ArgClass kArgs[] = {bound_callback::ArgClass::Integer, bound_callback::ArgClass::Integer,
											  bound_callback::ArgClass::Integer, bound_callback::ArgClass::Float,
											  bound_callback::ArgClass::Integer};
	CWinSeen seen;
	unsigned char code[96];
	int nFrame = 0;
	const size_t n = bound_callback::Assemble(kArgs, 5, true, (uint64_t)(uintptr_t)&seen,
											  (uint64_t)(uintptr_t)&WinTarget, code, sizeof(code), &nFrame);
	ASSERT_NE(0u, n);
	void* pPage = BoundCallback_MapCode(code, n, nFrame);
	ASSERT_NE(nullptr, pPage);

	const int64_t nAnswer = ((WinFn_t)pPage)(10, 200, 3000, 2.5f, 40000);
	BoundCallback_Unmap(pPage);

	EXPECT_EQ(43213, nAnswer);
	EXPECT_EQ(&seen, seen.pInstance);
	EXPECT_EQ(10, seen.a);
	EXPECT_EQ(200, seen.b);
	EXPECT_EQ(3000, seen.c);
	EXPECT_EQ(2.5f, seen.d);
	EXPECT_EQ(40000, seen.e);
}
#endif

#if defined(_WIN32)
#include <windows.h>

namespace
{
class CStackWalker
{
public:
	void Walk(void*, const char*, const char*, int)
	{
		m_nFrames = CaptureStackBackTrace(0, 62, m_Frames, NULL);
		m_nUnwound = UnwindFromHere();
	}

	// The frames a walk with RtlLookupFunctionEntry and RtlVirtualUnwind finds from its caller,
	// the routines exception dispatch unwinds with; a frame with no entry is a leaf.
	static __declspec(noinline) USHORT UnwindFromHere()
	{
		ULONG_PTR uLow = 0, uHigh = 0;
		GetCurrentThreadStackLimits(&uLow, &uHigh);

		CONTEXT context = {};
		RtlCaptureContext(&context);
		USHORT nFrames = 0;
		while (context.Rip != 0 && nFrames < 62)
		{
			// A frame unwound from a wrong return address can leave the stack: stop there.
			if (context.Rsp < uLow || context.Rsp + 8 > uHigh)
				break;

			DWORD64 uImageBase = 0;
			PRUNTIME_FUNCTION pEntry = RtlLookupFunctionEntry(context.Rip, &uImageBase, NULL);
			if (pEntry)
			{
				PVOID pHandler = NULL;
				DWORD64 uFrame = 0;
				RtlVirtualUnwind(UNW_FLAG_NHANDLER, uImageBase, context.Rip, pEntry, &context, &pHandler, &uFrame,
								 NULL);
			}
			else
			{
				context.Rip = *(DWORD64*)context.Rsp;
				context.Rsp += 8;
			}
			nFrames++;
		}
		return nFrames;
	}

	// Each frame the walk captured: its address, its offset in the thunk's page when it is in
	// it, and whether the system finds unwind data for it.
	std::string Describe(const void* pPage) const
	{
		std::string out;
		char line[128];
		for (USHORT k = 0; k < m_nFrames; k++)
		{
			const uintptr_t uAt = (uintptr_t)m_Frames[k];
			DWORD64 uImageBase = 0;
			const bool bEntry = RtlLookupFunctionEntry((DWORD64)uAt, &uImageBase, NULL) != NULL;
			const bool bInPage = uAt >= (uintptr_t)pPage && uAt < (uintptr_t)pPage + 4096;
			snprintf(line, sizeof(line), "  #%u %p%s%s\n", (unsigned)k, m_Frames[k], bInPage ? " (thunk page)" : "",
					 bEntry ? " entry" : " no entry");
			out += line;
		}
		return out;
	}

	void* m_Frames[62] = {};
	USHORT m_nFrames = 0;
	USHORT m_nUnwound = 0;
};

__declspec(noinline) USHORT CallThrough(AddSearchPathCallback::Fn fn)
{
	void* frames[62] = {};
	const USHORT nHere = CaptureStackBackTrace(0, 62, frames, NULL);
	fn(nullptr, "a", "b", 1);
	return nHere;
}
} // namespace

TEST(BoundCallback, AStackWalkThroughAFrameThunkReachesItsCaller)
{
	CStackWalker walker;
	AddSearchPathCallback callback(&walker, &AddSearchPathCallback::Trampoline<CStackWalker, &CStackWalker::Walk>);
	ASSERT_NE(nullptr, callback.Get());

	const USHORT nCaller = CallThrough(callback.Get());

	// Without the page's unwind data the walk stops inside the thunk's frame.
	EXPECT_GT(walker.m_nFrames, nCaller) << "CaptureStackBackTrace's frames:\n"
										 << walker.Describe(callback.Get()) << "RtlVirtualUnwind found "
										 << walker.m_nUnwound << " frames";
}
#endif

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
