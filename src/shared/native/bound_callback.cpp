// The thunks behind CBoundCallback (bound_callback.h): their bytes, and the pages that hold
// them.

#include "bound_callback.h"

#if defined(_WIN32)
#include <windows.h>
#else
#include <sys/mman.h>
#include <unistd.h>
#endif

namespace
{

// x86-64 register numbers.
enum
{
	kRax = 0,
	kRcx = 1,
	kRdx = 2,
	kRsi = 6,
	kRdi = 7,
	kR8 = 8,
	kR9 = 9
};

class CEmitter
{
public:
	CEmitter(unsigned char* pOut, size_t nOut)
		: m_pOut(pOut)
		, m_nOut(nOut)
	{
	}

	void Byte(unsigned char b)
	{
		if (m_nAt < m_nOut)
			m_pOut[m_nAt] = b;
		m_nAt++;
	}

	// mov dst, src (64-bit general registers).
	void MovReg(int nDst, int nSrc)
	{
		Byte((unsigned char)(0x48 | (nSrc >= 8 ? 0x04 : 0) | (nDst >= 8 ? 0x01 : 0)));
		Byte(0x89);
		Byte((unsigned char)(0xC0 | ((nSrc & 7) << 3) | (nDst & 7)));
	}

	// movaps xmm_dst, xmm_src (xmm0..xmm7).
	void MovXmm(int nDst, int nSrc)
	{
		Byte(0x0F);
		Byte(0x28);
		Byte((unsigned char)(0xC0 | (nDst << 3) | nSrc));
	}

	// mov reg, imm64.
	void MovImm(int nReg, uint64_t uValue)
	{
		Byte((unsigned char)(0x48 | (nReg >= 8 ? 0x01 : 0)));
		Byte((unsigned char)(0xB8 + (nReg & 7)));
		for (int i = 0; i < 8; i++)
			Byte((unsigned char)(uValue >> (8 * i)));
	}

	// jmp rax.
	void JmpRax()
	{
		Byte(0xFF);
		Byte(0xE0);
	}

	// call rax; ret.
	void CallRax()
	{
		Byte(0xFF);
		Byte(0xD0);
	}

	void Ret() { Byte(0xC3); }

	// sub rsp, imm8 / add rsp, imm8.
	void SubRsp(int nBytes) { RspImm(0xEC, nBytes); }

	void AddRsp(int nBytes) { RspImm(0xC4, nBytes); }

	// mov rax, [rsp+disp8].
	void LoadRax(int nDisp)
	{
		Byte(0x48);
		Byte(0x8B);
		RspDisp(kRax, nDisp);
	}

	// mov [rsp+disp8], reg (64-bit general register).
	void StoreReg(int nReg, int nDisp)
	{
		Byte((unsigned char)(0x48 | (nReg >= 8 ? 0x04 : 0)));
		Byte(0x89);
		RspDisp(nReg, nDisp);
	}

	// movsd [rsp+disp8], xmm (xmm0..xmm7): the low 8 bytes, which hold a float or a double.
	void StoreXmm(int nXmm, int nDisp)
	{
		Byte(0xF2);
		Byte(0x0F);
		Byte(0x11);
		RspDisp(nXmm, nDisp);
	}

	size_t Size() const { return m_nAt <= m_nOut ? m_nAt : 0; }

private:
	void RspImm(unsigned char op, int nBytes)
	{
		Byte(0x48);
		Byte(0x83);
		Byte(op);
		Byte((unsigned char)nBytes);
	}

	// The ModRM and SIB of [rsp+disp8] with reg in the reg field.
	void RspDisp(int nReg, int nDisp)
	{
		Byte((unsigned char)(0x44 | ((nReg & 7) << 3)));
		Byte(0x24);
		Byte((unsigned char)nDisp);
	}

	unsigned char* m_pOut;
	size_t m_nOut;
	size_t m_nAt = 0;
};

} // namespace

size_t bound_callback::Assemble(const ArgClass* pArgs, int nArgs, bool bWindows, uint64_t uInstance, uint64_t uTarget,
								unsigned char* pOut, size_t nOut, int* pOutFrame)
{
	CEmitter emit(pOut, nOut);
	int nFrame = 0;

	if (bWindows && nArgs + 1 > 4)
	{
		// Win64 with the last argument pushed onto the stack: the caller's frame has no room
		// for it, so the thunk makes a frame of its own (the home area, then the stack
		// arguments, keeping rsp 16-aligned at the call), copies the arguments one position
		// along and calls. Six at most keeps every displacement in a byte.
		static const int kInt[] = {kRcx, kRdx, kR8, kR9};
		const int nCall = nArgs + 1;
		if (nCall > 6)
			return 0;

		nFrame = 0x20 + 8 * (nCall - 4);
		if ((nFrame + 8) % 16 != 0)
			nFrame += 8;

		emit.SubRsp(nFrame);

		// The caller's stack arguments, past its return address and home area, the last first.
		for (int k = nArgs - 1; k >= 4; k--)
		{
			emit.LoadRax(nFrame + 8 + 0x20 + 8 * (k - 4));
			emit.StoreReg(kRax, 0x20 + 8 * (k + 1 - 4));
		}

		// The fourth argument leaves its register for the first stack slot.
		if (pArgs[3] == ArgClass::Float)
			emit.StoreXmm(3, 0x20);
		else
			emit.StoreReg(kR9, 0x20);

		for (int k = 2; k >= 0; k--)
		{
			if (pArgs[k] == ArgClass::Float)
				emit.MovXmm(k + 1, k);
			else
				emit.MovReg(kInt[k + 1], kInt[k]);
		}

		emit.MovImm(kRcx, uInstance);
		emit.MovImm(kRax, uTarget);
		emit.CallRax();
		emit.AddRsp(nFrame);
		emit.Ret();
	}
	else
	{
		if (bWindows)
		{
			// Win64: an argument's position picks its register, rcx/rdx/r8/r9 or xmm0-3, so
			// every argument moves one position along, the last first.
			static const int kInt[] = {kRcx, kRdx, kR8, kR9};
			for (int k = nArgs - 1; k >= 0; k--)
			{
				if (pArgs[k] == ArgClass::Float)
					emit.MovXmm(k + 1, k);
				else
					emit.MovReg(kInt[k + 1], kInt[k]);
			}

			emit.MovImm(kRcx, uInstance);
		}
		else
		{
			// System V: integers take rdi/rsi/rdx/rcx/r8/r9 in order and floats their own
			// xmm registers, so only the integers move.
			static const int kInt[] = {kRdi, kRsi, kRdx, kRcx, kR8, kR9};
			int nInts = 0;
			for (int k = 0; k < nArgs; k++)
				nInts += pArgs[k] == ArgClass::Integer ? 1 : 0;

			if (nInts + 1 > 6)
				return 0;

			for (int k = nInts - 1; k >= 0; k--)
				emit.MovReg(kInt[k + 1], kInt[k]);

			emit.MovImm(kRdi, uInstance);
		}

		emit.MovImm(kRax, uTarget);
		emit.JmpRax();
	}

	if (pOutFrame)
		*pOutFrame = nFrame;
	return emit.Size();
}

#if defined(_WIN32)
namespace
{
// The page's layout on Windows: the code, then the unwind data the system walks a stack
// through it with. Without it a stack walk through a thunk with a frame loses its caller.
const size_t kUnwindAt = 128;
const size_t kFunctionAt = 144;
} // namespace
#endif

void* BoundCallback_MapCode(const unsigned char* pCode, size_t nBytes, int nFrame)
{
#if defined(_WIN32)
	if (nBytes > kUnwindAt)
		return NULL;

	unsigned char* pPage = (unsigned char*)VirtualAlloc(NULL, kFunctionAt + sizeof(RUNTIME_FUNCTION),
														MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
	if (!pPage)
		return NULL;

	memcpy(pPage, pCode, nBytes);

	// UNWIND_INFO version 1: a thunk with a frame has a 4-byte prolog, its sub rsp, which
	// UWOP_ALLOC_SMALL (2) describes; a bare jump has none.
	unsigned char* pUnwind = pPage + kUnwindAt;
	pUnwind[0] = 1;
	if (nFrame > 0)
	{
		pUnwind[1] = 4;
		pUnwind[2] = 1;
		pUnwind[4] = 4;
		pUnwind[5] = (unsigned char)(2 | (((nFrame - 8) / 8) << 4));
	}

	RUNTIME_FUNCTION* pFunction = (RUNTIME_FUNCTION*)(pPage + kFunctionAt);
	pFunction->BeginAddress = 0;
	pFunction->EndAddress = (DWORD)nBytes;
	pFunction->UnwindData = (DWORD)kUnwindAt;

	DWORD nOld = 0;
	if (!VirtualProtect(pPage, kFunctionAt + sizeof(RUNTIME_FUNCTION), PAGE_EXECUTE_READ, &nOld))
	{
		VirtualFree(pPage, 0, MEM_RELEASE);
		return NULL;
	}

	if (!RtlAddFunctionTable(pFunction, 1, (DWORD64)(uintptr_t)pPage))
	{
		VirtualFree(pPage, 0, MEM_RELEASE);
		return NULL;
	}

	FlushInstructionCache(GetCurrentProcess(), pPage, nBytes);
	return pPage;
#else
	(void)nFrame;
	const size_t nPage = (size_t)sysconf(_SC_PAGESIZE);
	void* pPage = mmap(NULL, nPage, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
	if (pPage == MAP_FAILED)
		return NULL;

	memcpy(pPage, pCode, nBytes);

	if (mprotect(pPage, nPage, PROT_READ | PROT_EXEC) != 0)
	{
		munmap(pPage, nPage);
		return NULL;
	}

	return pPage;
#endif
}

void BoundCallback_Unmap(void* pPage)
{
	if (!pPage)
		return;

#if defined(_WIN32)
	RtlDeleteFunctionTable((RUNTIME_FUNCTION*)((unsigned char*)pPage + kFunctionAt));
	VirtualFree(pPage, 0, MEM_RELEASE);
#else
	munmap(pPage, (size_t)sysconf(_SC_PAGESIZE));
#endif
}
