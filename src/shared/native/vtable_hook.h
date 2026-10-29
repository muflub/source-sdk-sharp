// Asks the compiler which vtable slot a virtual method occupies, from a pointer to
// member function, rather than counting `virtual` lines in a header. Free of Source
// headers so it can be tested on its own.
#pragma once

#include <stddef.h>

#include <string.h>

namespace VTableHook
{

struct Member
{
	// Index into the vtable of the subobject named by Adjust.
	int slot;

	// What the ABI adds to `this` before the call. Non-zero means the pointer was
	// converted from a base's member, so the slot is numbered against the base's
	// vtable; callers treat that as a reason to stop.
	ptrdiff_t adjust;

	// False for a non-virtual member, or an unknown pointer-to-member layout.
	bool isVirtual;
};

#if defined(_MSC_VER)

// The Microsoft ABI (MSVC, and clang-cl, which also defines __clang__). A pointer to a
// virtual member points at a vcall thunk that loads the vtable from `this` and jumps
// through the slot:
//
//     48 8B 01             mov rax, [rcx]
//     FF 20 | FF 60 d8 | FF A0 d32      jmp [rax] / [rax+d8] / [rax+d32]
//
// or the load and jump split (48 8B 00 / 40 d8 / 80 d32, then FF E0), either jump
// optionally REX.W-prefixed. The slot is the displacement over the pointer size. An
// incremental-link stub (E9 rel32) is followed first.
namespace Detail
{
inline const unsigned char* FollowJumps(const unsigned char* p)
{
	for (int i = 0; i < 4 && p && p[0] == 0xE9; ++i)
	{
		int32_t rel;
		memcpy(&rel, p + 1, sizeof(rel));
		p = p + 5 + rel;
	}
	return p;
}

// `jmp [rax+disp]` when bDisp, else `jmp rax`, optionally REX.W-prefixed. The
// displacement, 0 for none; -1 when the bytes are not that jump.
inline long IndirectJump(const unsigned char* q, bool bDisp)
{
	if (q[0] == 0x48)
		++q;
	if (q[0] != 0xFF)
		return -1;
	if (!bDisp)
		return q[1] == 0xE0 ? 0 : -1;
	if (q[1] == 0x20)
		return 0;
	if (q[1] == 0x60)
		return (signed char)q[2];
	if (q[1] == 0xA0)
	{
		int32_t d32;
		memcpy(&d32, q + 2, sizeof(d32));
		return d32;
	}
	return -1;
}

// The byte offset into the vtable the thunk jumps through, or -1.
inline long VcallOffset(const unsigned char* p)
{
	if (!p || !(p[0] == 0x48 && p[1] == 0x8B && p[2] == 0x01)) // mov rax, [rcx]
		return -1;
	const unsigned char* q = p + 3;
	const long jump = IndirectJump(q, true); // jmp [rax+disp]
	if (jump >= 0)
		return jump;
	if (!(q[0] == 0x48 && q[1] == 0x8B)) // mov rax, [rax+disp]
		return -1;
	long disp;
	const unsigned char* next;
	if (q[2] == 0x00)
	{
		disp = 0;
		next = q + 3;
	}
	else if (q[2] == 0x40)
	{
		disp = (signed char)q[3];
		next = q + 4;
	}
	else if (q[2] == 0x80)
	{
		int32_t d32;
		memcpy(&d32, q + 3, sizeof(d32));
		disp = d32;
		next = q + 7;
	}
	else
	{
		return -1;
	}
	return IndirectJump(next, false) == 0 ? disp : -1; // jmp rax
}
}

template <typename Fn>
inline Member MemberOf(Fn pmf)
{
	Member member;
	member.slot = -1;
	member.adjust = 0;
	member.isVirtual = false;

	// Single inheritance: the address alone; multiple: address, then the adjustment.
	// Virtual inheritance is not guessed at.
	struct Multi
	{
		void* ptr;
		int32_t adj;
	};

	void* code = NULL;
	if (sizeof(Fn) == sizeof(void*))
	{
		memcpy(&code, &pmf, sizeof(code));
	}
	else if (sizeof(Fn) == sizeof(Multi))
	{
		Multi raw;
		memcpy(&raw, &pmf, sizeof(raw));
		code = raw.ptr;
		member.adjust = raw.adj;
	}
	else
	{
		return member;
	}

	const long offset = Detail::VcallOffset(Detail::FollowJumps((const unsigned char*)code));
	if (offset >= 0 && offset % (long)sizeof(void*) == 0)
	{
		member.isVirtual = true;
		member.slot = (int)(offset / (long)sizeof(void*));
	}
	return member;
}

#elif defined(__GNUC__) || defined(__clang__)

template <typename Fn>
inline Member MemberOf(Fn pmf)
{
	struct Raw
	{
		uintptr_t ptr;
		ptrdiff_t adj;
	};

	Member member;
	member.slot = -1;
	member.adjust = 0;
	member.isVirtual = false;

	// A layout this does not recognise must not be guessed at.
	if (sizeof(Fn) != sizeof(Raw))
		return member;

	Raw raw;
	memcpy(&raw, &pmf, sizeof(raw));

	member.adjust = raw.adj;
	member.isVirtual = (raw.ptr & 1) != 0;

	if (member.isVirtual)
		member.slot = (int)((raw.ptr - 1) / sizeof(void*));

	return member;
}

#else

// An unknown pointer-to-member layout reports not virtual, so callers refuse.
template <typename Fn>
inline Member MemberOf(Fn)
{
	Member member;
	member.slot = -1;
	member.adjust = 0;
	member.isVirtual = false;
	return member;
}

#endif

} // namespace VTableHook
